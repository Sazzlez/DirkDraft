namespace DraftPilot.Core.Data;

/// <summary>
/// Pulls small-sample rates towards the mean before anything is scored on them.
/// <para>
/// This matters more than it sounds. The meta endpoint reports duo win rates over as few as
/// fifteen games — a 78 % pairing over 32 games is noise, not synergy, and recommending on the raw
/// number would sell coin flips as insight. Every rate in the snapshot therefore goes through
/// <see cref="Apply"/> with a prior weight chosen for that data set's typical sample size.
/// </para>
/// </summary>
public static class Shrinkage
{
    /// <summary>
    /// Prior weight for lane win rates. Samples run a few hundred games, so a few hundred pseudo
    /// games keeps a 43-game outlier from topping the list without flattening real signal.
    /// </summary>
    public const int LanePrior = 300;

    /// <summary>Prior weight for matchup win rates; samples run 30-170 games.</summary>
    public const int MatchupPrior = 150;

    /// <summary>Prior weight for duo win rates; samples run 15-60 games.</summary>
    public const int SynergyPrior = 100;

    /// <summary>
    /// Blends an observed rate with a 50 % prior, weighted by sample size.
    /// A rate over zero games comes back as exactly the prior.
    /// </summary>
    public static double Apply(double observedRate, int play, int priorWeight)
    {
        if (priorWeight <= 0)
            return observedRate;

        if (play <= 0)
            return 0.5;

        // Math.Clamp(NaN, …) is NaN, and one NaN rate would ride the whole model into a NaN
        // score. A rate that is not a number carries no information — that is the prior.
        if (double.IsNaN(observedRate))
            return 0.5;

        var clamped = Math.Clamp(observedRate, 0, 1);
        return ((clamped * play) + (0.5 * priorWeight)) / (play + priorWeight);
    }

    /// <summary>
    /// How much weight a sample deserves, 0 to 1. Used to fade a term out rather than drop it:
    /// a matchup seen 20 times still says something, just not much.
    /// </summary>
    public static double Confidence(int play, int priorWeight)
    {
        if (play <= 0)
            return 0;

        return (double)play / (play + Math.Max(1, priorWeight));
    }
}
