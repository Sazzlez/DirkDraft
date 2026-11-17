using DraftPilot.Core.Data;

namespace DraftPilot.Meta.Riot;

/// <summary>The counts a ranked export publishes, see <see cref="ExportSources.Ranked"/>.</summary>
/// <param name="IsFallback">They are the previous patch's: the running one is too thin yet.</param>
/// <param name="LeftOut">Champions taken out of the fallback because they were reworked with the new patch.</param>
public sealed record RankedExportSource(
    string Patch,
    RiotBuildFile Builds,
    RiotMetaCounts Meta,
    List<int[]> Situational,
    bool IsFallback,
    IReadOnlyList<int> LeftOut);

/// <summary>The same for ARAM.</summary>
public sealed record AramExportSource(RiotAramFile Tally, bool IsFallback, IReadOnlyList<int> LeftOut);

/// <summary>
/// Which counts an export publishes. Early in a patch the new count is thin; until it has enough
/// games the previous patch's numbers are published instead, under their own patch, so the app knows
/// they trail. A champion reworked with the new patch is left out of that fallback
/// (<see cref="ChampionResets.Reworked"/>): its old numbers describe a kit that is gone, and no number
/// is better than those.
/// </summary>
public static class ExportSources
{
    /// <summary>The previous patch stands in: the running one has fewer than <paramref name="enoughGames"/> and fewer than it.</summary>
    public static bool IsFallback(CollectorState state, int enoughGames)
        => state.Meta.Matches < enoughGames && state.Previous is { } previous && previous.Meta.Matches > state.Meta.Matches;

    public static bool IsFallback(AramState state, int enoughGames)
        => state.Tally.Games < enoughGames && state.Previous is { } previous && previous.Games > state.Tally.Games;

    /// <param name="plan">data/resets.json; only read for a fallback.</param>
    public static RankedExportSource Ranked(CollectorState state, int enoughGames, ResetPlan plan)
    {
        if (!IsFallback(state, enoughGames) || state.Previous is not { } previous)
            return new RankedExportSource(state.Patch, state.Tally, state.Meta, state.Situational, false, []);

        // Copies: the state itself keeps the previous patch whole.
        var builds = new RiotBuildFile
        {
            Patch = previous.Tally.Patch,
            Platform = previous.Tally.Platform,
            CollectedAtUtc = previous.Tally.CollectedAtUtc,
            Matches = previous.Tally.Matches,
            Builds = [.. previous.Tally.Builds],
        };
        var meta = new RiotMetaCounts
        {
            Matches = previous.Meta.Matches,
            Lanes = [.. previous.Meta.Lanes],
            Champions = [.. previous.Meta.Champions],
            Duels = [.. previous.Meta.Duels],
            Duos = [.. previous.Meta.Duos],
            Versus = [.. previous.Meta.Versus],
        };
        var situational = new List<int[]>(previous.Situational);

        var leftOut = ChampionResets.Reworked(plan, state.Patch).Order().ToList();
        foreach (var championId in leftOut)
        {
            ChampionResets.Remove(meta, championId);
            ChampionResets.Remove(builds.Builds, championId);
            ChampionResets.RemoveSituational(situational, championId);
        }

        return new RankedExportSource(previous.Patch, builds, meta, situational, true, leftOut);
    }

    public static AramExportSource Aram(AramState state, int enoughGames, ResetPlan plan)
    {
        if (!IsFallback(state, enoughGames) || state.Previous is not { } previous)
            return new AramExportSource(state.Tally, false, []);

        var tally = new RiotAramFile
        {
            Version = previous.Version,
            Patch = previous.Patch,
            Platform = previous.Platform,
            CollectedAtUtc = previous.CollectedAtUtc,
            Games = previous.Games,
            Champions = [.. previous.Champions],
            Builds = [.. previous.Builds],
        };

        var leftOut = ChampionResets.Reworked(plan, state.Patch).Order().ToList();
        foreach (var championId in leftOut)
            ChampionResets.Remove(tally, championId);

        return new AramExportSource(tally, true, leftOut);
    }
}
