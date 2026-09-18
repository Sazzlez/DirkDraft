using DraftPilot.Core.Data;

namespace DraftPilot.Core.Draft;

/// <summary>
/// How the draft stands: one estimated win rate for the own team, the counterpart for theirs.
/// <para>
/// Built from the same evidence as the pick recommendations — shrunk lane win rates and shrunk
/// direct matchups of the champions actually revealed — combined in log-odds. Three deliberate
/// choices keep the number honest: everything is a MEAN, never a sum (five champions at 52 %
/// are not a 95 % team); unrevealed seats simply do not count instead of being guessed; and a duel
/// counts only by how far it deviates from what the two lane rates already predict, so the same
/// advantage is not counted twice. It is a reading of the data, not a prediction of the game.
/// </para>
/// </summary>
/// <param name="AllyWinRate">Estimated win rate of the own team, 0..1.</param>
/// <param name="RatedChampions">Revealed champions that contributed a lane win rate.</param>
/// <param name="ContestedLanes">Lanes where both sides revealed a champion and a matchup existed.</param>
/// <param name="IsComparable">
/// Whether both sides revealed something to compare. Only a difference between the two sides says
/// anything; with one side entirely hidden the estimate collapses to exactly 50 % by construction,
/// and that number would look like a measured verdict instead of the absence of one.
/// </param>
public readonly record struct DraftBalance(
    double AllyWinRate,
    int RatedChampions,
    int ContestedLanes,
    bool IsComparable = true)
{
    /// <summary>Nothing to judge yet — shown as a dash rather than as a confident 50 %.</summary>
    public static DraftBalance Unknown => new(0.5, 0, 0, false);

    public bool HasData => RatedChampions > 0 && IsComparable;

    public double EnemyWinRate => 1 - AllyWinRate;

    /// <summary>
    /// Estimates the balance from the current draft. The lane predictions are used for both
    /// sides so a flex pick lands on the lane it will most likely be played on.
    /// </summary>
    public static DraftBalance Estimate(
        MetaLookup meta,
        DraftState state,
        LanePredictionResult allyLanes,
        LanePredictionResult enemyLanes)
    {
        if (!state.IsActive || meta.IsEmpty)
            return Unknown;

        var (allySum, allyCount) = SideStrength(meta, allyLanes);
        var (enemySum, enemyCount) = SideStrength(meta, enemyLanes);

        var rated = allyCount + enemyCount;
        if (rated == 0)
            return Unknown;

        // Relative strength: both sides' lane win rates are measured against the same field, so
        // the DIFFERENCE of the means is meaningful while either mean alone is not.
        var strengthEdge = allyCount > 0 && enemyCount > 0
            ? (allySum / allyCount) - (enemySum / enemyCount)
            : 0;

        var duelSum = 0.0;
        var contested = 0;

        foreach (var lane in Lanes.All)
        {
            var ally = allyLanes.ChampionOnLane(lane);
            var enemy = enemyLanes.ChampionOnLane(lane);

            if (ally == 0 || enemy == 0 || meta.Matchup(ally, enemy, lane) is not { } matchup)
                continue;

            // What the two lane rates alone would predict for this duel — the same reference the
            // rate was shrunk towards. Only the difference to it is news. Uncentred, the same
            // advantage was counted twice — once in strengthEdge above, once here — so a lane where
            // the stronger champion also wins the duel (the usual case) was worth about double what
            // it is.
            duelSum += ScoreModel.Logit(matchup.WinRate) - meta.MatchupBaseline(ally, enemy, lane);
            contested++;
        }

        var duelEdge = contested > 0 ? duelSum / contested : 0;

        // Blind pick reveals our own team and nothing of theirs: both edges stay zero, the sigmoid
        // returns exactly 0.5, and the window would print "50,0 % WR" on both columns as if the
        // draft had been weighed. Say so instead.
        var comparable = allyCount > 0 && enemyCount > 0;

        return new DraftBalance(ScoreModel.Sigmoid(strengthEdge + duelEdge), rated, contested, comparable);
    }

    /// <summary>Sum of the lane-win-rate log-odds of one side's revealed champions, and how many.</summary>
    private static (double Sum, int Count) SideStrength(MetaLookup meta, LanePredictionResult lanes)
    {
        var sum = 0.0;
        var count = 0;

        foreach (var prediction in lanes.Predictions)
        {
            if (prediction.ChampionId == 0
                || prediction.Lane == Lane.Unknown
                || meta.LaneStat(prediction.ChampionId, prediction.Lane) is not { } stat)
            {
                continue;
            }

            sum += ScoreModel.Logit(stat.WinRate);
            count++;
        }

        return (sum, count);
    }
}
