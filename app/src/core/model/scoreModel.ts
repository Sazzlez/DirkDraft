// Port of DraftPilot.Core.Draft.ScoreModel (1.5). Every piece of evidence is a shift in log-odds of
// winning; the shifts add up and the sum maps back to an estimated win rate. The few constants that
// remain carry the reasoning they rest on.

/**
 * How much an enemy OFF my lane counts relative to a lane opponent. The enemy jungler shapes my
 * lane for real; their support almost never does. A third-ish is the honest middle. Estimate, not
 * measurement — calibration against recorded ranked drafts is an open task.
 */
export const OFF_LANE_SHARE = 0.35;

/**
 * Damping for duo win rates. They are the thinnest data in the snapshot and correlated across
 * team-mates (a fed team wins with everyone); undamped, four allies would dominate the score.
 */
export const SYNERGY_DAMPING = 0.6;

/**
 * Log-odds for a fully covered composition gap. The comp rules have no win-rate basis at all, which
 * is exactly why this is the smallest term: a covered gap ≈ +4 points, never more.
 */
export const COMP_SCALE = 0.16;

/**
 * How much a lane rate moves per patch beyond sampling noise, as a variance in log-odds. Measured
 * with Tools -- patchdrift between two Gold files, 16.18 and 16.20: across 268 lane rows the rates
 * moved by 0,68 points beyond what their samples explain. Taken as a random walk, that is 0,48
 * points per patch — 3,7e-4 of variance per patch.
 */
export const LANE_DRIFT_PER_PATCH = 3.7e-4;

/**
 * For bans: share of a champion's lane strength counted AGAIN when that lane is the seat's own.
 * Deliberate double counting — a champion you personally have to face is worth more of a ban.
 */
export const BAN_LANE_FOCUS = 0.5;

/** For bans: damping on the candidate's matchups against our locked picks (as SYNERGY_DAMPING). */
export const BAN_ALLY_SHARE = 0.5;

/**
 * OP.GG's numeric lane tiers in the letters every tier list uses. Tier 0 is OP.GG's OP tier, one
 * step above S — measured, the ladder is monotone (0 → 52,0 %, … 5 → 47,3 %).
 */
export function tierName(tier: number): string {
  switch (tier) {
    case 0:
      return 'OP-Tier';
    case 1:
      return 'S-Tier';
    case 2:
      return 'A-Tier';
    case 3:
      return 'B-Tier';
    case 4:
      return 'C-Tier';
    case 5:
      return 'D-Tier';
    default:
      return '';
  }
}

/** ln(p / (1-p)), clamped away from the poles; NaN reads as 0 so one bad rate cannot poison a score. */
export function logit(probability: number): number {
  if (Number.isNaN(probability)) return 0;
  const p = Math.min(0.99, Math.max(0.01, probability));
  return Math.log(p / (1 - p));
}

/** Inverse of logit; always strictly between 0 and 1. */
export function sigmoid(logOdds: number): number {
  return 1 / (1 + Math.exp(-logOdds));
}

/** A log-odds shift as percentage points at the 50 % operating point (slope 0.25). Display only. */
export function asPoints(logOdds: number): number {
  return logOdds * 25;
}

/** Math.Clamp. */
export function clamp(value: number, min: number, max: number): number {
  return Math.min(max, Math.max(min, value));
}
