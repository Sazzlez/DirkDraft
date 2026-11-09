// Pick → lane lead → win, every lane on its own. A counter on top wins the laning phase: more farm,
// solo kills, plates. Whether a pick does that shows in the gold after ten minutes between the two
// lane opponents long before it shows in the winner — the gold is far less noisy than one win or
// loss. So the gold answers two questions per lane:
//
//   1. What decides the lane's gold: the champion (its mean against everyone) or the specific
//      matchup (what the pair does beyond both means)? Each as its real spread, the chance part of
//      the observed spread taken out (method of moments).
//   2. What a lead at ten minutes is worth in win rate, per lane, all five lanes fitted at once.
//
// Together: what a matchup is worth on each lane, and how many games a pair needs before its own
// gold says more than the average — the smoothing a gold-based matchup would get.

import { ALL_LANES, Lane } from '../../src/core/model/lane';
import type { Game } from './games';
import { fitLogistic, type LogisticFit } from './logistic';

/** Games of a champion on a lane (with gold) before its mean gold counts as its strength. */
export const LANE_GOLD_MINIMUM = 40;

/** One lane duel with gold: champion a (blue) against b (red), gold blue minus red. */
export interface GoldDuel {
  a: number;
  b: number;
  gold: number;
}

/** The place (0–9) of the single player of a side on a lane, −1 where there is none or more than one. */
function placeOf(game: Game, side: 'blue' | 'red', lane: Lane): number {
  const seats = game[side];
  let found = -1;
  for (let i = 0; i < seats.length; i++) {
    if (seats[i]!.lane !== lane) continue;
    if (found >= 0) return -1;
    found = i;
  }
  return found < 0 ? -1 : side === 'blue' ? found : 5 + found;
}

/** Gold after ten minutes per lane (Top … Support), blue's laner minus red's; null where unknown. */
export function laneGoldDiffs(game: Game): (number | null)[] {
  return ALL_LANES.map((lane) => {
    if (!game.gold) return null;
    const blue = placeOf(game, 'blue', lane);
    const red = placeOf(game, 'red', lane);
    return blue < 0 || red < 0 ? null : game.gold[blue]! - game.gold[red]!;
  });
}

/** The duels of one lane in games with gold; mirror matches left out. */
export function goldDuels(games: readonly Game[], lane: Lane): GoldDuel[] {
  const duels: GoldDuel[] = [];
  for (const game of games) {
    if (!game.gold) continue;
    const blue = placeOf(game, 'blue', lane);
    const red = placeOf(game, 'red', lane);
    if (blue < 0 || red < 0) continue;
    const a = game.blue[blue]!.championId;
    const b = game.red[red - 5]!.championId;
    if (a === b) continue;
    duels.push({ a, b, gold: game.gold[blue]! - game.gold[red]! });
  }
  return duels;
}

export interface LaneGoldSplit {
  duels: number;
  /** Standard deviation of the gold difference between the lane opponents. */
  spread: number;
  /** Mean gold edge per champion (n ≥ minimum), against everyone on the lane. */
  strength: Map<number, number>;
  /** Real spread of the champions' mean edges, chance taken out. */
  championSpread: number;
  champions: number;
  /** Real spread of what a pair does beyond both champions' means; 0 when chance explains it all. */
  matchupSpread: number;
  /** 95 % upper bound of the matchup spread. */
  matchupUpper: number;
  /** How far the matchup variance stands above 0, in standard errors. */
  matchupZ: number;
  pairs: number;
  /** Residual variance per duel once both champions are accounted for. */
  residualVariance: number;
  /** Games at which a pair's own gold counts half (residual variance over matchup variance); Infinity when there is none. */
  prior: number;
}

const pairKey = (low: number, high: number): string => `${low}|${high}`;

export function splitLane(duels: readonly GoldDuel[], minimum = LANE_GOLD_MINIMUM): LaneGoldSplit {
  const sums = new Map<number, { n: number; sum: number }>();
  const add = (champion: number, gold: number) => {
    const entry = sums.get(champion) ?? { n: 0, sum: 0 };
    entry.n++;
    entry.sum += gold;
    sums.set(champion, entry);
  };
  let squares = 0;
  for (const d of duels) {
    add(d.a, d.gold);
    add(d.b, -d.gold);
    squares += d.gold * d.gold;
  }
  const variance = duels.length > 0 ? squares / duels.length : 0;

  // E[n · mean²] = n · τ² + σ²: the chance part of each champion's mean is σ² / n.
  const strength = new Map<number, number>();
  let weighted = 0;
  let games = 0;
  for (const [champion, { n, sum }] of sums) {
    if (n < minimum) continue;
    const mean = sum / n;
    strength.set(champion, mean);
    weighted += n * mean * mean;
    games += n;
  }
  const championVariance = games > 0 ? (weighted - strength.size * variance) / games : 0;

  // The same for the pairs, on what is left once both means are taken off.
  const pairs = new Map<string, { n: number; sum: number }>();
  let residualSquares = 0;
  let residuals = 0;
  for (const d of duels) {
    const ma = strength.get(d.a);
    const mb = strength.get(d.b);
    if (ma === undefined || mb === undefined) continue;
    const low = d.a < d.b;
    const residual = (low ? 1 : -1) * (d.gold - (ma - mb));
    residualSquares += residual * residual;
    residuals++;
    const key = low ? pairKey(d.a, d.b) : pairKey(d.b, d.a);
    const entry = pairs.get(key) ?? { n: 0, sum: 0 };
    entry.n++;
    entry.sum += residual;
    pairs.set(key, entry);
  }
  const residualVariance = residuals > 0 ? residualSquares / residuals : 0;
  let pairWeighted = 0;
  for (const { n, sum } of pairs.values()) pairWeighted += n * (sum / n) ** 2;
  const matchupVariance = residuals > 0 ? (pairWeighted - pairs.size * residualVariance) / residuals : 0;
  // Each pair's n · mean² scatters like σ² · χ²₁ around its expectation: variance 2σ⁴.
  const matchupError = residuals > 0 ? Math.sqrt(pairs.size * 2 * residualVariance ** 2) / residuals : 0;

  return {
    duels: duels.length,
    spread: Math.sqrt(variance),
    strength,
    championSpread: Math.sqrt(Math.max(0, championVariance)),
    champions: strength.size,
    matchupSpread: Math.sqrt(Math.max(0, matchupVariance)),
    matchupUpper: Math.sqrt(Math.max(0, matchupVariance) + 1.96 * matchupError),
    matchupZ: matchupError > 0 ? matchupVariance / matchupError : 0,
    pairs: pairs.size,
    residualVariance,
    prior: matchupVariance > 0 ? residualVariance / matchupVariance : Infinity,
  };
}

/**
 * Each pair's own gold edge beyond both champions' means, for the lower champion id, shrunk towards 0
 * with `split.prior` games. Empty when the lane shows no matchup at all.
 */
export function matchupGold(duels: readonly GoldDuel[], split: LaneGoldSplit): Map<string, number> {
  const edges = new Map<string, number>();
  if (!Number.isFinite(split.prior)) return edges;
  const pairs = new Map<string, { n: number; sum: number }>();
  for (const d of duels) {
    const ma = split.strength.get(d.a);
    const mb = split.strength.get(d.b);
    if (ma === undefined || mb === undefined) continue;
    const low = d.a < d.b;
    const key = low ? pairKey(d.a, d.b) : pairKey(d.b, d.a);
    const entry = pairs.get(key) ?? { n: 0, sum: 0 };
    entry.n++;
    entry.sum += (low ? 1 : -1) * (d.gold - (ma - mb));
    pairs.set(key, entry);
  }
  for (const [key, { n, sum }] of pairs) edges.set(key, sum / (n + split.prior));
  return edges;
}

/** The gold edge of champion a against b from `matchupGold`; 0 for a pair without one. */
export function edgeOf(edges: ReadonlyMap<string, number>, a: number, b: number): number {
  if (a === b) return 0;
  return a < b ? (edges.get(pairKey(a, b)) ?? 0) : -(edges.get(pairKey(b, a)) ?? 0);
}

/**
 * What a lead after ten minutes is worth: blue's win against the five lane differences (in thousands
 * of gold), all at once. Coefficient j + 1 is lane j's log-odds per 1.000 gold. Null below 200 games.
 */
export function conversion(games: readonly Game[]): { fit: LogisticFit; games: number } | null {
  const x: number[][] = [];
  const y: number[] = [];
  for (const game of games) {
    const diffs = laneGoldDiffs(game);
    if (diffs.some((d) => d === null)) continue;
    x.push(diffs.map((d) => d! / 1000));
    y.push(game.blueWins);
  }
  return x.length >= 200 ? { fit: fitLogistic(x, y), games: x.length } : null;
}
