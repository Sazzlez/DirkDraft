using System.Globalization;
using System.Text.Json;
using DraftPilot.Core.Data;

namespace DraftPilot.Meta.Riot;

/// <summary>What <see cref="PublishGuard"/> decided, and the sentence for the run's page.</summary>
public sealed record GuardVerdict(bool Publish, string Reason, int? PublishedGames, int? NewGames);

/// <summary>
/// Keeps a thin count from replacing a full one. The collector's state lives in the Actions cache;
/// when that is gone once, the next run starts at zero and its export would put a few thousand games
/// where 270.000 stood. Before the push the new file's <c>games</c> are held against the published
/// file's: on the same patch less than half is not published. A new patch, and the very first file,
/// pass — a new count is supposed to start small then.
/// <para>
/// Read from the two fields every published file carries (<c>patch</c>, <c>games</c>): meta.json for
/// the ranked files, aram.json for ARAM.
/// </para>
/// </summary>
public static class PublishGuard
{
    /// <summary>On the same patch the new count needs this share of the published one.</summary>
    public const double MinimumShare = 0.5;

    /// <param name="publishedJson">The file on the data branch; null or unreadable when there is none yet.</param>
    /// <param name="newJson">The file the export just wrote.</param>
    public static GuardVerdict Check(string? publishedJson, string? newJson)
    {
        var fresh = Read(newJson);
        var published = Read(publishedJson);

        // Nothing counted is nothing to publish, whatever stood there before (a state that failed to
        // load exports an empty file with no patch).
        if (fresh is not { Games: > 0 })
            return new GuardVerdict(false, "Die neue Datei zählt keine Spiele — nichts zu veröffentlichen.", published?.Games, fresh?.Games);

        if (published is not { Games: > 0 })
            return new GuardVerdict(true, $"Erste Veröffentlichung: {Number(fresh.Value.Games)} Spiele.", null, fresh.Value.Games);

        var (oldPatch, oldGames) = published.Value;
        var (newPatch, newGames) = fresh.Value;

        if (!PatchVersion.SameLine(oldPatch, newPatch))
        {
            // A state restored from an old backup counts a patch that is over: never back.
            return PatchVersion.PatchesBetween(newPatch, oldPatch) > 0
                ? new GuardVerdict(false, $"Die neue Datei ist von Patch {newPatch}, veröffentlicht ist schon Patch {oldPatch} — nicht zurück.", oldGames, newGames)
                : new GuardVerdict(true, $"Neuer Patch {newPatch} (veröffentlicht war {oldPatch}): {Number(newGames)} Spiele.", oldGames, newGames);
        }

        return newGames < MinimumShare * oldGames
            ? new GuardVerdict(
                false,
                $"Patch {newPatch}: die neue Datei zählt {Number(newGames)} Spiele, veröffentlicht sind {Number(oldGames)} — weniger als die Hälfte, der veröffentlichte Stand bleibt.",
                oldGames,
                newGames)
            : new GuardVerdict(true, $"Patch {newPatch}: {Number(newGames)} Spiele (veröffentlicht waren {Number(oldGames)}).", oldGames, newGames);
    }

    private static (string Patch, int Games)? Read(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;

        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return null;

            var patch = root.TryGetProperty("patch", out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() ?? string.Empty : string.Empty;
            var games = root.TryGetProperty("games", out var g) && g.ValueKind == JsonValueKind.Number && g.TryGetInt32(out var count) ? count : 0;
            return (patch, games);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string Number(int value) => value.ToString("N0", CultureInfo.GetCultureInfo("de-DE"));
}
