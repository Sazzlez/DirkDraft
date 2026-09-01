using DraftPilot.Core.Data;

namespace DraftPilot.Core.Draft;

/// <summary>
/// How the draft stands: one estimated win rate for the own team, the counterpart for theirs.
/// <para>
/// Built from the same evidence as the pick recommendations — shrunk lane win rates and shrunk
/// direct matchups of the champions actually revealed — combined in log-odds. Two deliberate
/// choices keep the number honest: everything is a MEAN, never a sum (five champions at 52 %
/// are not a 95 % team), and unrevealed seats simply do not count instead of being guessed.
/// It is a reading of the data, not a prediction of the game.
/// </para>
/// </summary>
/// <param name="AllyWinRate">Estimated win rate of the own team, 0..1.</param>
/// <param name="RatedChampions">Revealed champions that contributed a lane win rate.</param>
/// <param name="ContestedLanes">Lanes where both sides revealed a champion and a matchup existed.</param>
public readonly record struct DraftBalance(double AllyWinRate, int RatedChampions, int ContestedLanes)
{
    /// <summary>Nothing to judge yet — shown as a dash rather than as a confident 50 %.</summary>
    public static DraftBalance Unknown => new(0.5, 0, 0);

    public bool HasData => RatedChampions > 0;

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

            // Already centred: an even matchup contributes exactly zero.
            duelSum += ScoreModel.Logit(matchup.WinRate);
            contested++;
        }

        var duelEdge = contested > 0 ? duelSum / contested : 0;

        return new DraftBalance(ScoreModel.Sigmoid(strengthEdge + duelEdge), rated, contested);
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
