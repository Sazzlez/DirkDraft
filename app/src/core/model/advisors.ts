// Ports of the small 1.5 advisors: DraftBalance (draft chance), LaneMatchups (the duel rows),
// AramAdvisor (bench ranking), AugmentAdvisor (Mayhem augments) and BuildChoice (Wilson bound).

import type { DraftState } from './draftState';
import { ALL_LANES, Lane } from './lane';
import type { LanePredictionResult } from './lanePredictor';
import type { MetaLookup } from './metaLookup';
import { netN0, netPercent } from './netFormat';
import { neutral, type Reason } from './reason';
import type { Recommendation } from './recommender';
import { logitVariance } from './scoreError';
import { clamp, logit, sigmoid } from './scoreModel';
import { shrink } from './shrinkage';

// ---- DraftBalance --------------------------------------------------------------------------------

export interface DraftBalance {
  allyWinRate: number;
  ratedChampions: number;
  contestedLanes: number;
  isComparable: boolean;
}

export const UNKNOWN_BALANCE: DraftBalance = Object.freeze({ allyWinRate: 0.5, ratedChampions: 0, contestedLanes: 0, isComparable: false });

export const balanceHasData = (b: DraftBalance): boolean => b.ratedChampions > 0 && b.isComparable;

export function estimateBalance(
  meta: MetaLookup,
  state: DraftState,
  allyLanes: LanePredictionResult,
  enemyLanes: LanePredictionResult,
): DraftBalance {
  if (!state.isActive || meta.isEmpty) return UNKNOWN_BALANCE;

  const ally = sideStrength(meta, allyLanes);
  const enemy = sideStrength(meta, enemyLanes);
  const rated = ally.count + enemy.count;
  if (rated === 0) return UNKNOWN_BALANCE;

  // Both sides are measured against the same field, so only the difference of the means means anything.
  const strengthEdge = ally.count > 0 && enemy.count > 0 ? ally.sum / ally.count - enemy.sum / enemy.count : 0;

  let duelSum = 0;
  let contested = 0;
  for (const lane of ALL_LANES) {
    const a = allyLanes.championOnLane(lane);
    const e = enemyLanes.championOnLane(lane);
    if (a === 0 || e === 0) continue;
    const matchup = meta.matchup(a, e, lane);
    if (!matchup) continue;
    // Centred on what both lane rates already predict; uncentred, strength counted twice.
    duelSum += logit(matchup.winRate) - meta.matchupBaseline(a, e, lane);
    contested++;
  }
  const duelEdge = contested > 0 ? duelSum / contested : 0;

  return {
    allyWinRate: sigmoid(strengthEdge + duelEdge),
    ratedChampions: rated,
    contestedLanes: contested,
    isComparable: ally.count > 0 && enemy.count > 0,
  };
}

function sideStrength(meta: MetaLookup, lanes: LanePredictionResult): { sum: number; count: number } {
  let sum = 0;
  let count = 0;
  for (const p of lanes.predictions) {
    if (p.championId === 0 || p.lane === Lane.Unknown) continue;
    const stat = meta.laneStat(p.championId, p.lane);
    if (!stat) continue;
    sum += logit(stat.winRate);
    count++;
  }
  return { sum, count };
}

// ---- LaneMatchups --------------------------------------------------------------------------------

export interface LaneMatchup {
  lane: Lane;
  allyId: number;
  enemyId: number;
  winRate: number | null;
  play: number;
  isInferred: boolean;
  measured: number | null;
}

export function laneMatchups(meta: MetaLookup, allies: LanePredictionResult, enemies: LanePredictionResult): LaneMatchup[] {
  const rows: LaneMatchup[] = [];
  for (const lane of ALL_LANES) {
    const ally = allies.championOnLane(lane);
    const enemy = enemies.championOnLane(lane);
    if (ally === 0 && enemy === 0) continue;
    const duel = ally !== 0 && enemy !== 0 ? meta.matchup(ally, enemy, lane) : null;
    rows.push({
      lane,
      allyId: ally,
      enemyId: enemy,
      winRate: duel?.winRate ?? null,
      play: duel?.play ?? 0,
      isInferred: duel?.isInferred ?? false,
      measured: duel && Number.isFinite(duel.measured) ? duel.measured : null,
    });
  }
  return rows;
}

export function laneMatchupForSeat(rows: readonly LaneMatchup[], lane: Lane, championId: number, isAlly: boolean): LaneMatchup | null {
  if (lane === Lane.Unknown || championId === 0) return null;
  return rows.find((row) => row.lane === lane && (isAlly ? row.allyId : row.enemyId) === championId) ?? null;
}

// ---- AramAdvisor ---------------------------------------------------------------------------------

export interface AramStat {
  championId: number;
  winRate: number;
  play: number;
}

export const ARAM_PRIOR = 400;

/** Variance of a rate reported on two decimals: a uniform error over one hundredth. */
export const ARAM_ROUNDING_VARIANCE = (0.01 * 0.01) / 12;

/** Own champion and bench in one list, best first; empty when the own champion has no number. */
export function rankAram(
  ownChampion: number,
  bench: readonly number[],
  stats: ReadonlyMap<number, AramStat>,
  names: (id: number) => string,
): Recommendation[] {
  const candidates: number[] = [];
  if (ownChampion !== 0) candidates.push(ownChampion);
  for (const champion of bench) if (champion !== ownChampion && !candidates.includes(champion)) candidates.push(champion);

  const rows: Recommendation[] = [];
  for (const champion of candidates) {
    const stat = stats.get(champion);
    if (!stat || stat.play <= 0) continue;
    const shrunk = shrink(stat.winRate, stat.play, ARAM_PRIOR);
    const variance = logitVariance(shrunk, stat.play, ARAM_PRIOR, 0.5) + logitRoundingVariance(shrunk);
    rows.push({
      championId: champion,
      name: names(champion),
      score: shrunk,
      reasons: aramReasons(stat, shrunk, champion === ownChampion),
      breakdown: [{ kind: 'laneStrength', logOdds: logit(shrunk) }],
      uncertainty: Math.sqrt(variance),
    });
  }

  if (ownChampion !== 0 && rows.every((row) => row.championId !== ownChampion)) return [];

  return rows.sort((a, b) => (b.score > a.score ? 1 : b.score < a.score ? -1 : a.name < b.name ? -1 : a.name > b.name ? 1 : 0));
}

function logitRoundingVariance(rate: number): number {
  const clamped = clamp(rate, 0.01, 0.99);
  const slope = 1 / (clamped * (1 - clamped));
  return slope * slope * ARAM_ROUNDING_VARIANCE;
}

function aramReasons(stat: AramStat, shrunk: number, isOwn: boolean): Reason[] {
  const reasons: Reason[] = [];
  if (isOwn) {
    reasons.push(
      neutral(
        'dein Champ',
        'Der Champion, den du gerade hast. Er steht in derselben Liste wie die Bank, damit ' +
          'sichtbar ist, ob ein Tausch überhaupt etwas bringt.',
      ),
    );
  }
  reasons.push(
    neutral(
      `${netPercent(stat.winRate, 0)} WR in ARAM aus ${netN0(stat.play)} Games`,
      'OP.GGs ARAM-Zahl für diesen Champion. Sie kommt auf zwei Nachkommastellen gerundet — ' +
        'ein Prozentpunkt Auflösung — und die Rundung steckt im Fehlerbalken mit drin. ' +
        `Nach Glättung über die Stichprobe: ${netPercent(shrunk, 1)}.`,
    ),
  );
  return reasons;
}

// ---- AugmentAdvisor ------------------------------------------------------------------------------

export interface AugmentOption {
  id: number;
  name: string;
  tier: number;
  performance: number;
  pickRate: number;
}

export const AUGMENT_FULL_SCALE = 170;
export const AUGMENT_COARSE_SAMPLE_LIMIT = 40;

/** Augments worth showing: not betrayed as a tiny sample, taken at least as often as the median. */
export function rankAugments(options: Iterable<AugmentOption>, take: number): AugmentOption[] {
  if (take <= 0) return [];
  const rows: AugmentOption[] = [];
  for (const option of options) {
    if (!Number.isFinite(option.pickRate) || option.pickRate <= 0) continue;
    if (!Number.isFinite(option.performance) || option.performance <= 0) continue;
    if (!option.name || option.name.trim() === '') continue;
    if (isCoarseAugment(option.performance)) continue;
    rows.push({ id: option.id, name: option.name, tier: option.tier, performance: option.performance, pickRate: option.pickRate });
  }
  const floor = medianPickRate(rows);
  return rows
    .filter((row) => row.pickRate >= floor)
    .sort(
      (a, b) =>
        b.performance - a.performance || b.pickRate - a.pickRate || (a.name < b.name ? -1 : a.name > b.name ? 1 : 0),
    )
    .slice(0, take);
}

/** Whether the figure is wins/n · 170 for some n ≤ 40 — a sample too small to rank on. */
export function isCoarseAugment(performance: number): boolean {
  if (!Number.isFinite(performance)) return true;
  for (let n = 1; n <= AUGMENT_COARSE_SAMPLE_LIMIT; n++) {
    const wins = roundAwayFromZero((performance * n) / AUGMENT_FULL_SCALE);
    if (wins < 0 || wins > n) continue;
    if (Math.abs((AUGMENT_FULL_SCALE * wins) / n - performance) < 0.005) return true;
  }
  return false;
}

function medianPickRate(rows: readonly AugmentOption[]): number {
  if (rows.length === 0) return 0;
  const sorted = rows.map((row) => row.pickRate).sort((a, b) => a - b);
  return sorted[Math.floor(sorted.length / 2)]!;
}

const roundAwayFromZero = (value: number): number => Math.sign(value) * Math.round(Math.abs(value));

// ---- BuildChoice ---------------------------------------------------------------------------------

export interface ItemSetLike {
  play: number;
  winRate: number;
}

const Z = 1.96;

/** Wilson lower bound at 95 %: a 13-game 62 % set does not outrank a 113-game 49 % one. */
export function wilsonLowerBound(set: ItemSetLike): number {
  if (set.play <= 0) return 0;
  const rate = Number.isNaN(set.winRate) ? 0 : clamp(set.winRate, 0, 1);
  const play = set.play;
  const zSquared = Z * Z;
  const denominator = 1 + zSquared / play;
  const centre = rate + zSquared / (2 * play);
  const margin = Z * Math.sqrt((rate * (1 - rate)) / play + zSquared / (4 * play * play));
  return (centre - margin) / denominator;
}

export function byWinRate<T extends ItemSetLike>(sets: Iterable<T>, take: number): T[] {
  return [...sets]
    .filter((set) => set.play > 0)
    .map((set) => ({ set, bound: wilsonLowerBound(set) }))
    .sort((a, b) => b.bound - a.bound || b.set.play - a.set.play)
    .slice(0, Math.max(0, take))
    .map((entry) => entry.set);
}
