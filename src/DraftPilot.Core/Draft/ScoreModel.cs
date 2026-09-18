namespace DraftPilot.Core.Draft;

/// <summary>
/// The scoring model: every piece of evidence is expressed as a shift in log-odds of winning, the
/// shifts add up, and the sum maps back to an estimated win rate for the line-up.
/// <para>
/// This replaced a scheme of hand-tuned weights over -1..+1 signals. That scheme had no unit, so
/// nobody could say whether "tier × 1.0" and "matchup × 1.0" were in proportion — which is why the
/// UI grew three preset buttons asking the user to decide. In log-odds the weighting question
/// mostly disappears: a shrunk win rate carries its own strength, missing data sits at exactly
/// zero, and several small edges add up instead of hitting a clamp. The few constants that remain
/// are below, each with the reasoning it rests on.
/// </para>
/// </summary>
public static class ScoreModel
{
    /// <summary>
    /// How much an enemy OFF my lane counts relative to a lane opponent. The enemy jungler shapes
    /// my lane for real; their support almost never does. A third-ish is the honest middle.
    /// Estimate, not measurement — calibration against recorded ranked drafts is an open task.
    /// </summary>
    public const double OffLaneShare = 0.35;

    // There used to be a TierNudge of 0.04 log-odds per tier step here, on the argument that the
    // tier "only adds what OP.GG's blend of pick and ban pressure knows on top". Measured on the
    // stored Gold snapshot, the argument does not hold: across the ladder the nudge spans 0,16
    // log-odds while the win rate it is added to spans 0,156 across the same ladder, and the tier
    // explains 46,8 % of that win rate's variance — and 45,2 % of the PICK rate's. It was half a
    // second helping of the win rate and half a popularity vote. The tier is now a chip, not a
    // term. See Recommender.LaneLogOdds.

    /// <summary>
    /// Damping for duo win rates. They are the thinnest data in the snapshot and correlated across
    /// team-mates (a fed team wins with everyone); undamped, four allies would dominate the score.
    /// </summary>
    public const double SynergyDamping = 0.6;

    /// <summary>
    /// Log-odds for a fully covered composition gap. The comp rules have no win-rate basis at all,
    /// which is exactly why this is the smallest term: a covered gap ≈ +4 points, never more.
    /// </summary>
    public const double CompScale = 0.16;

    /// <summary>
    /// For bans: share of a champion's lane strength counted AGAIN when that lane is the seat's
    /// own. Deliberate double counting — a champion you personally have to face is worth more of a
    /// ban than one terrorising some other lane.
    /// </summary>
    public const double BanLaneFocus = 0.5;

    /// <summary>
    /// For bans: damping on the candidate's matchups against our locked picks. Same reasoning as
    /// <see cref="SynergyDamping"/> — few data points, correlated across allies.
    /// </summary>
    public const double BanAllyShare = 0.5;

    /// <summary>
    /// OP.GG's numeric lane tiers in the letters every tier list uses. "S-Tier" needs no
    /// explanation; "Stufe 1 von 5" did.
    /// <para>
    /// Tier 0 is OP.GG's OP tier, one step above S, and not a missing value — measured on the
    /// stored snapshot the ladder is monotone: 0 → 52,0 %, 1 → 51,0 %, 2 → 50,6 %, 3 → 50,3 %,
    /// 4 → 49,5 %, 5 → 47,3 %. Unknown is -1, written by the fallback that has no tier at all.
    /// </para>
    /// </summary>
    public static string TierName(int tier) => tier switch
    {
        0 => "OP-Tier",
        1 => "S-Tier",
        2 => "A-Tier",
        3 => "B-Tier",
        4 => "C-Tier",
        5 => "D-Tier",
        _ => string.Empty,
    };

    /// <summary>ln(p / (1-p)), clamped away from the poles so a degenerate rate cannot explode.</summary>
    public static double Logit(double probability)
    {
        // Math.Clamp(NaN, …) is NaN, and one NaN term would silently ride into a "NaN %" score.
        if (double.IsNaN(probability))
            return 0;

        var p = Math.Clamp(probability, 0.01, 0.99);
        return Math.Log(p / (1 - p));
    }

    /// <summary>Inverse of <see cref="Logit"/>; always strictly between 0 and 1.</summary>
    public static double Sigmoid(double logOdds)
        => 1 / (1 + Math.Exp(-logOdds));

    /// <summary>
    /// A log-odds shift as percentage points of win rate, evaluated at the 50 % operating point
    /// (d/dx sigmoid at 0 is 0.25). Display only — the score itself always goes through
    /// <see cref="Sigmoid"/> so the terms in the breakdown stay comparable to each other.
    /// </summary>
    public static double AsPoints(double logOdds) => logOdds * 25;
}
