// Port of DraftPilot.Core.Draft.ScoreError / ErrorBudget (1.5): the sampling error of a score in
// log-odds, propagated through shrinkage, and what it says about two neighbouring scores.

import { asPoints, clamp, logit } from './scoreModel';

export type ScoreStanding = 'tied' | 'ahead' | 'clear';

export const TIE_SIGMA = 1.0;
export const CLEAR_SIGMA = 3.0;

/** Variance in log-odds of a shrunk rate, through the shrinkage it went through. */
export function logitVariance(shrunkRate: number, play: number, prior: number, priorRate = 0.5): number {
  if (play <= 0) return 0;
  if (!Number.isFinite(priorRate) || priorRate <= 0 || priorRate >= 1) priorRate = 0.5;
  const factor = play / (play + prior);
  if (!Number.isFinite(shrunkRate)) return 0;
  const raw = clamp(priorRate + (shrunkRate - priorRate) / factor, 0, 1);
  const shrunk = clamp(shrunkRate, 0.01, 0.99);
  const slope = factor / (shrunk * (1 - shrunk));
  return (slope * slope * raw * (1 - raw)) / play;
}

export const errorAsPoints = (standardError: number): number => asPoints(standardError);

/** One decimal only where the error bar is narrow enough to carry it. */
export const decimalsFor = (standardError: number): number => (asPoints(standardError) >= 0.5 ? 0 : 1);

export function decimalsOf(items: readonly { uncertainty: number }[]): number {
  if (items.length === 0) return 1;
  const errors = items.map((i) => i.uncertainty).sort((a, b) => a - b);
  return decimalsFor(errors[Math.floor(errors.length / 2)]!);
}

export function standing(score: number, error: number, nextScore: number, nextError: number): ScoreStanding {
  const gap = logit(score) - logit(nextScore);
  const spread = Math.sqrt(error * error + nextError * nextError);
  if (spread <= 0) return gap > 0 ? 'ahead' : 'tied';
  const sigmas = gap / spread;
  if (sigmas >= CLEAR_SIGMA) return 'clear';
  if (sigmas >= TIE_SIGMA) return 'ahead';
  return 'tied';
}

/** How many rows from the top are within one sigma of the leader; 0 when the leader stands alone. */
export function countLeadingTies(items: readonly { score: number; uncertainty: number }[]): number {
  if (items.length < 2) return 0;
  const leader = items[0]!;
  let tied = 1;
  while (tied < items.length && standing(leader.score, leader.uncertainty, items[tied]!.score, items[tied]!.uncertainty) === 'tied') tied++;
  return tied >= 2 ? tied : 0;
}

export class ErrorBudget {
  private variance = 0;

  get standardError(): number {
    return Math.sqrt(this.variance);
  }

  add(variance: number): void {
    if (Number.isFinite(variance) && variance > 0) this.variance += variance;
  }
}
