// Port of DraftPilot.Core.Data.Shrinkage (1.5): pulls small-sample rates towards their expectation
// before anything is scored on them.

/** Lane win rates: a few hundred pseudo games keep a 43-game outlier from topping the list. */
export const LANE_PRIOR = 300;

/**
 * Lane win rates from our own count (meta.json with source "riot"). Measured with Tools -- priors on
 * the collector's file of 07.11.2026 (Platin, 6.185 games, median 149 per lane row): held-out loss
 * lowest at 800 (split-half, 400 splits), moment estimate 851 (true spread 1,71 points) — and 1.5
 * already found 600–932 on OP.GG's Gold rows. κ belongs to the population, not to the sample size,
 * so it holds as the count grows. OP.GG files keep 300: the golden parity with 1.5 is measured there,
 * and at their 29.000 games a row the weight changes nothing (docs/modellpruefung.md, Messung 10).
 */
export const OWN_LANE_PRIOR = 800;

/**
 * Matchup win rates. Measured against complete duel lists (Tools -- guidecheck, 2026-10-08): the
 * weight under which a notable duel best predicts the complete one is 1.000 (MSE 12,4 against 20,5
 * at 150), and the complete lists' own split-half test bottoms out at 1.000 too. Beyond both lane
 * strengths a duel moves the win rate by about 1,6 points.
 */
export const MATCHUP_PRIOR = 1000;

/**
 * Matchup win rates from our own count, per lane (Top, Jungle, Mid, Bot, Support): duels do not
 * spread alike on every lane. Measured on the collector's file of 13.11.2026 (158.087 games) with
 * Tools -- priors: true spread around what both lane strengths predict 1,86 points on Top, 1,76 in
 * the Jungle, 1,51 on Mid, 1,21 on Bot and 0,86 on Support — κ by the moment estimate 719, 804,
 * 1.090, 1.698, 3.348, the split-half optimum 600, 800, 1.000, 2.000, 5.000. The model comparison on
 * held-out games orders the lanes the same way without sharing anything with that estimate: under
 * one weight of 1.000 the Top duel wants a factor of 2,11 ± 0,64 and the Bot duel 0,59 ± 0,38
 * (docs/modellpruefung.md, Messung 13, 14 and 21). Over all lanes together the single weight stands
 * confirmed (moments 1.131, best 1.200). OP.GG files keep MATCHUP_PRIOR for every lane.
 */
export const OWN_MATCHUP_PRIOR: readonly number[] = [700, 800, 1100, 1700, 3300];

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
