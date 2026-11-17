using System.Text.Json;
using DraftPilot.Core.Data;

namespace DraftPilot.Meta.Riot;

/// <summary>
/// One line of <c>data/resets.json</c>: a champion whose numbers of a patch stopped describing it —
/// a hotfix in the middle of the patch, or a rework that came with it.
/// </summary>
/// <param name="Champion">Data Dragon key ("MonkeyKing"); the display name is understood too.</param>
/// <param name="Patch">The patch it happened in, "16.23".</param>
/// <param name="Reason">"hotfix" or "rework".</param>
/// <param name="Note">Free text; two hotfixes of one champion in one patch need different notes.</param>
public sealed record ChampionReset(string Champion, string Patch, string Reason, string? Note)
{
    public bool IsRework => string.Equals(Reason, "rework", StringComparison.OrdinalIgnoreCase);

    /// <summary>What a state remembers once the entry is applied: every field, so a new entry is a new key.</summary>
    public string Key => $"{Champion.Trim().ToLowerInvariant()}|{PatchVersion.Line(Patch)}|{Reason.Trim().ToLowerInvariant()}|{(Note ?? string.Empty).Trim()}";

    internal static string PatchOfKey(string key) => key.Split('|') is { Length: >= 2 } parts ? parts[1] : string.Empty;
}

/// <summary>The resets file together with the champions its names stand for.</summary>
public sealed record ResetPlan(IReadOnlyList<ChampionReset> Entries, IReadOnlyDictionary<string, int> ChampionIds)
{
    public static readonly ResetPlan None = new([], new Dictionary<string, int>());

    /// <summary>Champions by Data Dragon key and by name, however they are capitalised or spaced.</summary>
    public static IReadOnlyDictionary<string, int> Names(IEnumerable<ChampionEntry> champions)
    {
        var ids = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var champion in champions)
        {
            foreach (var name in new[] { champion.Key, champion.Name, Plain(champion.Name) })
            {
                if (name.Length > 0)
                    ids.TryAdd(name, champion.Id);
            }
        }

        return ids;
    }

    public int? IdOf(ChampionReset entry)
        => ChampionIds.TryGetValue(entry.Champion.Trim(), out var id) || ChampionIds.TryGetValue(Plain(entry.Champion), out id) ? id : null;

    private static string Plain(string name) => new([.. name.Where(char.IsLetterOrDigit)]);
}

/// <summary>
/// Counting a champion anew. The collector tells patches apart and nothing finer
/// (<see cref="PatchVersion.Line"/>): a hotfix mixes the games before it with the games after until
/// the patch ends, and in the days after a patch the published fallback shows a reworked champion
/// with the numbers of its old kit. <c>data/resets.json</c>, kept by hand, names such champions.
/// <list type="bullet">
/// <item>At the start of a run every entry of the running patch that the state has not applied yet
/// takes its champion out of everything counted for that patch — lane rows, duels and pairs across
/// lanes, duos, bans, damage, builds, situational items, matchup builds — and is remembered in the
/// state, so a second run changes nothing. The game lines stay: they are what was played.</item>
/// <item>In the fallback window the export leaves out champions with a <c>rework</c> for the new
/// patch: no number is better than the old kit's.</item>
/// </list>
/// </summary>
public static class ChampionResets
{
    /// <summary>The entries of the file; what cannot be read is said in <paramref name="problems"/>, never thrown.</summary>
    public static List<ChampionReset> Parse(string? json, out List<string> problems)
    {
        problems = [];
        var entries = new List<ChampionReset>();
        if (string.IsNullOrWhiteSpace(json))
            return entries;

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json, new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
        }
        catch (JsonException ex)
        {
            problems.Add($"resets.json ist nicht lesbar ({ex.Message}) — kein Eintrag angewendet.");
            return entries;
        }

        using (document)
        {
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                problems.Add("resets.json ist keine Liste — kein Eintrag angewendet.");
                return entries;
            }

            var index = 0;
            foreach (var element in document.RootElement.EnumerateArray())
            {
                index++;
                var champion = Text(element, "champion");
                var patch = Text(element, "patch");
                var reason = Text(element, "reason").ToLowerInvariant();
                if (champion.Length == 0 || PatchVersion.Line(patch).Length == 0 || reason is not ("hotfix" or "rework"))
                {
                    problems.Add($"resets.json, Eintrag {index}: braucht champion, patch (\"16.23\") und reason (\"hotfix\" oder \"rework\") — übergangen.");
                    continue;
                }

                entries.Add(new ChampionReset(champion, patch, reason, Text(element, "note") is { Length: > 0 } note ? note : null));
            }
        }

        return entries;

        static string Text(JsonElement element, string name)
            => element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
                ? (value.GetString() ?? string.Empty).Trim()
                : string.Empty;
    }

    /// <summary>The file at <paramref name="path"/>; a missing file is an empty list.</summary>
    public static List<ChampionReset> Load(string path, out List<string> problems)
    {
        problems = [];
        return File.Exists(path) ? Parse(File.ReadAllText(path), out problems) : [];
    }

    /// <summary>Applies the entries of the state's patch that it has not applied yet. Returns how many.</summary>
    public static int Apply(CollectorState state, ResetPlan plan, Action<string>? log = null)
    {
        state.AppliedResets = Pending(state.AppliedResets, state.Patch);
        var applied = 0;
        foreach (var (entry, id) in Due(state.AppliedResets, state.Patch, plan, log))
        {
            var removed = Remove(state.Meta, id) + Remove(state.Tally.Builds, id) + RemoveSituational(state.Situational, id) + RemoveMatchups(state.Matchups ??= [], id);
            state.AppliedResets.Add(entry.Key);
            applied++;
            log?.Invoke($"Reset ({entry.Reason}) für {entry.Champion}, Patch {state.Patch}: {removed:N0} Zeilen aus der Zählung genommen, der Champion zählt ab jetzt neu.");
        }

        return applied;
    }

    /// <summary>The same for the ARAM count: the champion's games and builds.</summary>
    public static int Apply(AramState state, ResetPlan plan, Action<string>? log = null)
    {
        state.AppliedResets = Pending(state.AppliedResets, state.Patch);
        var applied = 0;
        foreach (var (entry, id) in Due(state.AppliedResets, state.Patch, plan, log))
        {
            var removed = Remove(state.Tally, id);
            state.AppliedResets.Add(entry.Key);
            applied++;
            log?.Invoke($"ARAM-Reset ({entry.Reason}) für {entry.Champion}, Patch {state.Patch}: {removed:N0} Zeilen aus der Zählung genommen.");
        }

        return applied;
    }

    /// <summary>Champions with a rework that came with <paramref name="patch"/>: left out of the fallback files of the patch before.</summary>
    public static HashSet<int> Reworked(ResetPlan plan, string patch)
    {
        var line = PatchVersion.Line(patch);
        return [.. plan.Entries
            .Where(entry => entry.IsRework && line.Length > 0 && PatchVersion.Line(entry.Patch) == line)
            .Select(plan.IdOf)
            .OfType<int>()];
    }

    /// <summary>Lane rows, the champion's own row (games, bans, damage), duels, duos and pairs across lanes.</summary>
    public static int Remove(RiotMetaCounts counts, int championId)
    {
        return counts.Lanes.RemoveAll(row => row.Length > 0 && row[0] == championId)
            + counts.Champions.RemoveAll(row => row.Length > 0 && row[0] == championId)
            + counts.Duels.RemoveAll(row => row.Length > 2 && (row[1] == championId || row[2] == championId))
            + counts.Duos.RemoveAll(row => row.Length > 2 && (row[0] == championId || row[2] == championId))
            + counts.Versus.RemoveAll(row => row.Length > 2 && (row[0] == championId || row[2] == championId));
    }

    public static int Remove(List<ChampionBuildStats> builds, int championId) => builds.RemoveAll(build => build.ChampionId == championId);

    /// <summary>[champion, lane, slot, situation, state, item, games, wins].</summary>
    public static int RemoveSituational(List<int[]> rows, int championId) => rows.RemoveAll(row => row.Length > 0 && row[0] == championId);

    /// <summary>[champion, lane, opponent, kind, value, games, wins]: what the champion takes, and what is taken against it.</summary>
    public static int RemoveMatchups(List<int[]> rows, int championId) => rows.RemoveAll(row => row.Length > 2 && (row[0] == championId || row[2] == championId));

    public static int Remove(RiotAramFile file, int championId)
        => file.Champions.RemoveAll(champion => champion.ChampionId == championId) + Remove(file.Builds, championId);

    /// <summary>The applied keys of the running patch; older ones are of no use any more.</summary>
    private static List<string> Pending(List<string>? applied, string patch)
        => [.. (applied ?? []).Where(key => ChampionReset.PatchOfKey(key) == patch)];

    private static IEnumerable<(ChampionReset Entry, int Id)> Due(List<string> applied, string patch, ResetPlan plan, Action<string>? log)
    {
        if (patch.Length == 0)
            yield break;

        // Materialised first: the caller adds to "applied" while it walks the result.
        var due = plan.Entries.Where(entry => PatchVersion.Line(entry.Patch) == patch && !applied.Contains(entry.Key)).ToList();
        foreach (var entry in due)
        {
            if (plan.IdOf(entry) is { } id)
                yield return (entry, id);
            else
                log?.Invoke($"resets.json: Champion \"{entry.Champion}\" ist unbekannt (Data-Dragon-Key, etwa MonkeyKing) — Eintrag nicht angewendet.");
        }
    }
}
