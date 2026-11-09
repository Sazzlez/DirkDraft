using System.Text.Json;

namespace DraftPilot.Core.Lcu;

/// <summary>
/// E1 of the 2.1 list: which seats of the own team are the local player's party — so DirkDraft could
/// mark the duo partner and show the synergy with him apart. Read from the champ select session and
/// the lobby and reported without a single name or id: only which fields exist, their visibility
/// value and whether session and lobby agree. Decides between the two possible sources before any of
/// it is built; nothing in the app reads this.
/// </summary>
public static class PartyProbe
{
    public static IReadOnlyList<string> Report(string? phase, string? sessionJson, string? lobbyJson)
    {
        var lines = new List<string> { $"Gameflow-Phase: {phase ?? "keine Antwort"}" };
        using var session = Parse(sessionJson);
        using var lobby = Parse(lobbyJson);

        var lobbyPuuids = new HashSet<string>(StringComparer.Ordinal);
        var lobbySummoners = new HashSet<long>();
        if (lobby is null)
        {
            lines.Add("Lobby: keine Antwort (404 oder leer)");
        }
        else if (lobby.RootElement.TryGetProperty("members", out var members) && members.ValueKind == JsonValueKind.Array)
        {
            var fields = new SortedSet<string>(StringComparer.Ordinal);
            foreach (var member in members.EnumerateArray().Where(m => m.ValueKind == JsonValueKind.Object))
            {
                foreach (var property in member.EnumerateObject())
                    fields.Add(property.Name);
                if (Text(member, "puuid") is { Length: > 0 } puuid)
                    lobbyPuuids.Add(puuid);
                if (Number(member, "summonerId") is long id && id > 0)
                    lobbySummoners.Add(id);
            }
            lines.Add($"Lobby: {members.GetArrayLength()} Mitglieder; Felder je Mitglied: {string.Join(", ", fields)}");
        }
        else
        {
            lines.Add("Lobby: Antwort ohne Mitgliederliste");
        }

        if (session is null)
        {
            lines.Add("Champ Select: keine Session — die Probe braucht einen laufenden Champ Select.");
            return lines;
        }

        var root = session.RootElement;
        var local = Number(root, "localPlayerCellId") ?? -1;
        lines.Add($"Champ Select: Queue {Number(root, "queueId")?.ToString() ?? "?"}, eigener Platz {local}");

        var byLobby = new List<long>();
        var notHidden = new List<long>();
        foreach (var player in Players(root, "myTeam"))
        {
            var cell = Number(player, "cellId") ?? -1;
            var isLocal = cell == local;
            var visibility = Text(player, "nameVisibilityType") ?? "—";
            var puuid = Text(player, "puuid");
            var summoner = Number(player, "summonerId");
            var inLobby = lobby is null
                ? "—"
                : (puuid is { Length: > 0 } && lobbyPuuids.Contains(puuid)) || (summoner is > 0 && lobbySummoners.Contains(summoner.Value)) ? "ja" : "nein";
            if (!isLocal && inLobby == "ja")
                byLobby.Add(cell);
            if (!isLocal && visibility is not ("—" or "HIDDEN"))
                notHidden.Add(cell);

            lines.Add(
                $"  Platz {cell}{(isLocal ? " (du)" : "")}: Sichtbarkeit {visibility}, puuid {State(player, "puuid")}, " +
                $"obfuscatedPuuid {State(player, "obfuscatedPuuid")}, summonerId {State(player, "summonerId")}, " +
                $"Name {State(player, "gameName")}, in der Lobby {inLobby}");
        }

        var enemy = Players(root, "theirTeam")
            .GroupBy(player => Text(player, "nameVisibilityType") ?? "—")
            .Select(group => $"{group.Count()} × {group.Key}");
        lines.Add($"Gegner: Sichtbarkeit {string.Join(", ", enemy)}");

        lines.Add($"Party laut Lobby (ohne dich): {Cells(byLobby)}");
        lines.Add($"Nicht verborgen (ohne dich): {Cells(notHidden)}");
        lines.Add(byLobby.Count > 0 && byLobby.SequenceEqual(notHidden)
            ? "→ Beide Quellen stimmen überein."
            : "→ Die Quellen stimmen nicht überein oder eine fehlt — das entscheidet, welche DirkDraft nehmen kann.");
        return lines;
    }

    private static string Cells(List<long> cells) => cells.Count == 0 ? "keine" : string.Join(", ", cells.Select(c => $"Platz {c}"));

    /// <summary>Whether a field is there and filled — never its value.</summary>
    private static string State(JsonElement player, string name)
    {
        if (!player.TryGetProperty(name, out var value) || value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            return "fehlt";
        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString() is { Length: > 0 } ? "da" : "leer",
            JsonValueKind.Number => value.TryGetInt64(out var n) && n != 0 ? "da" : "0",
            _ => "da",
        };
    }

    private static IEnumerable<JsonElement> Players(JsonElement root, string team)
        => root.TryGetProperty(team, out var list) && list.ValueKind == JsonValueKind.Array
            ? list.EnumerateArray().Where(player => player.ValueKind == JsonValueKind.Object).ToList()
            : [];

    private static string? Text(JsonElement element, string name)
        => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static long? Number(JsonElement element, string name)
        => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var n) ? n : null;

    private static JsonDocument? Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;
        try
        {
            var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind == JsonValueKind.Object)
                return document;
            document.Dispose();
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
