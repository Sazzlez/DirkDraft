// Port of DraftPilot.Core.Data.SynergyLine (1.5): what a duo is expected to win before its own games
// are counted — a line in the two champions' lane strength, fitted to the duo rows. Measured slope
// ≈ 0,4, not 1: centring on the full sum predicted held-out duos worse than a global mean.

import type { Lane } from './lane';
import { clamp, logit } from './scoreModel';

export interface SynergyLine {
  intercept: number;
  slope: number;
  rows: number;
}

/** Fewer pairs than this and the line is not fitted: noise in a slope multiplies every candidate. */
export const MINIMUM_ROWS = 100;

export const flatLine = (baseline: number): SynergyLine => ({ intercept: baseline, slope: 0, rows: 0 });

export function expectedLogit(line: SynergyLine, mineLogit: number, partnerLogit: number): number {
  return line.intercept + line.slope * (mineLogit + partnerLogit);
}

export interface SynergyRow {
  championId: number;
  lane: Lane;
  partnerId: number;
  partnerLane: Lane;
  winRate: number;
  play: number;
}

/** Games-weighted least squares, one row per pair; the flat fallback when there is too little. */
export function fitSynergyLine(
  rows: readonly SynergyRow[],
  laneRate: (championId: number, lane: Lane) => number | null,
  fallback: number,
): SynergyLine {
  // Both partners' lists can carry the same duo; counted twice it would pull twice as hard.
  const groups = new Map<string, SynergyRow[]>();
  for (const row of rows) {
    if (!(row.play > 0) || !Number.isFinite(row.winRate)) continue;
    const key = `${Math.min(row.championId, row.partnerId)}:${Math.max(row.championId, row.partnerId)}`;
    const group = groups.get(key);
    if (group) group.push(row);
    else groups.set(key, [row]);
  }

  const points: { x: number; y: number; w: number }[] = [];
  for (const group of groups.values()) {
    // OrderByDescending(Play).First(): stable, so the first of equal samples wins.
    let best = group[0]!;
    for (const row of group) if (row.play > best.play) best = row;
    const mine = laneRate(best.championId, best.lane);
    const theirs = laneRate(best.partnerId, best.partnerLane);
    if (mine === null || theirs === null) continue;
    points.push({ x: logit(mine) + logit(theirs), y: logit(best.winRate), w: best.play });
  }

  if (points.length < MINIMUM_ROWS) return flatLine(fallback);

  let weights = 0;
  let sumX = 0;
  let sumY = 0;
  for (const p of points) {
    weights += p.w;
    sumX += p.w * p.x;
    sumY += p.w * p.y;
  }
  const meanX = sumX / weights;
  const meanY = sumY / weights;
  let sxx = 0;
  let sxy = 0;
  for (const p of points) {
    sxx += p.w * (p.x - meanX) * (p.x - meanX);
    sxy += p.w * (p.x - meanX) * (p.y - meanY);
  }
  if (sxx <= 0) return flatLine(fallback);

  // A negative slope would reward a weak partner; above 1 would count strength more than once.
  const slope = clamp(sxy / sxx, 0, 1);
  return { intercept: meanY - slope * meanX, slope, rows: points.length };
}
