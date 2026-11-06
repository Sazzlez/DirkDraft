// Port of DraftPilot.Core.Data.Shrinkage (1.5): pulls small-sample rates towards their expectation
// before anything is scored on them.

/** Lane win rates: a few hundred pseudo games keep a 43-game outlier from topping the list. */
export const LANE_PRIOR = 300;

/**
 * Matchup win rates. Measured against complete duel lists (Tools -- guidecheck, 2026-10-08): the
 * weight under which a notable duel best predicts the complete one is 1.000 (MSE 12,4 against 20,5
 * at 150), and the complete lists' own split-half test bottoms out at 1.000 too. Beyond both lane
 * strengths a duel moves the win rate by about 1,6 points.
 */
export const MATCHUP_PRIOR = 1000;

/**
 * Duo win rates. Measured with Tools -- priors on the Gold file (2.670 pairs): true spread 1,8
 * points around each pair's expectation, 753 by the moment estimate, held-out loss lowest between
 * 600 and 1.000.
 */
export const SYNERGY_PRIOR = 750;

/**
 * Blends an observed rate with a prior, weighted by sample size. A rate over zero games comes back
 * as exactly the prior; a prior outside (0,1) falls back to 50 %.
 */
export function shrink(observedRate: number, play: number, priorWeight: number, priorRate = 0.5): number {
  if (!Number.isFinite(priorRate) || priorRate <= 0 || priorRate >= 1) priorRate = 0.5;
  if (priorWeight <= 0) return observedRate;
  if (play <= 0) return priorRate;
  if (Number.isNaN(observedRate)) return priorRate;
  const clamped = Math.min(1, Math.max(0, observedRate));
  return (clamped * play + priorRate * priorWeight) / (play + priorWeight);
}

/** How much weight a sample deserves, 0 to 1. */
export function confidence(play: number, priorWeight: number): number {
  if (play <= 0) return 0;
  return play / (play + Math.max(1, priorWeight));
}
