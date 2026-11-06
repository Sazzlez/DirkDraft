// Mirrors Tools -- golden (GoldenCommand.Evaluate / Replay): feeds the TS model the same inputs and
// returns the same JSON shape, so a golden file and the port's output can be compared key by key.

import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import {
  estimateBalance,
  laneMatchups,
  rankAram,
  type AramStat,
} from '../../src/core/model/advisors';
import { CompAnalyzer, traitCoverage, type CompProfile } from '../../src/core/model/compAnalyzer';
import { draftStateFrom, effectiveChampionId, isLocked, localSlot, type DraftSlot, type DraftState } from '../../src/core/model/draftState';
import { laneKey, Lane } from '../../src/core/model/lane';
import { isUncertain, LanePredictor, SeatPriors, type LanePredictionResult } from '../../src/core/model/lanePredictor';
import { MetaLookup, type MetaSnapshot } from '../../src/core/model/metaLookup';
import { isAram, usesLanes } from '../../src/core/model/queue';
import { isGate, Recommender, termGate, termLabel, termPoints, type Recommendation, type RecommendationSet } from '../../src/core/model/recommender';
import { countLeadingTies, decimalsOf, errorAsPoints } from '../../src/core/model/scoreError';
import { TraitTable } from '../../src/core/model/traits';
import { TurnTracker, type RecommendationTarget } from '../../src/core/model/turnTracker';
import priors from '../../src/core/model/data/pick_order_priors.json';

export const LIMIT = 8;

const here = (path: string): string => fileURLToPath(new URL(path, import.meta.url));

export const readJson = <T>(path: string): T => JSON.parse(readFileSync(here(path), 'utf8')) as T;

export interface Context {
  snapshot: MetaSnapshot;
  traits: TraitTable;
  seats: SeatPriors;
  aramStats: ReadonlyMap<number, AramStat>;
}

export function loadContext(): Context {
  const snapshot = readJson<MetaSnapshot>('./snapshot.json');
  const aram = readJson<AramStat[]>('./aram-stats.json');
  return {
    snapshot,
    traits: TraitTable.bundled(),
    seats: SeatPriors.fromJson(priors),
    aramStats: new Map(aram.map((s) => [s.championId, s])),
  };
}

// ---- golden state → DraftState (drafts are built by the tool, not parsed from a session) ---------

interface GoldenSlot {
  cellId: number;
  index: number;
  isAlly: boolean;
  lockedChampionId: number;
  hoverChampionId: number;
  assignedLane: string;
}

const laneOf = (key: string): Lane =>
  (({ top: Lane.Top, jungle: Lane.Jungle, mid: Lane.Mid, adc: Lane.Adc, support: Lane.Support }) as Record<string, Lane>)[key] ??
  Lane.Unknown;

export function stateFromGolden(g: Record<string, any>): DraftState {
  const slot = (s: GoldenSlot): DraftSlot => ({
    cellId: s.cellId,
    index: s.index,
    isAlly: s.isAlly,
    lockedChampionId: s.lockedChampionId,
    hoverChampionId: s.hoverChampionId,
    assignedLane: laneOf(s.assignedLane),
  });
  return {
    isActive: g.isActive,
    localCellId: g.localCellId,
    allies: g.allies.map(slot),
    enemies: g.enemies.map(slot),
    allyBans: g.allyBans,
    enemyBans: g.enemyBans,
    phase: g.phase,
    turn: g.turn,
    unavailable: new Set<number>(g.unavailable),
    queue: g.queue,
    bench: g.bench,
    benchEnabled: g.benchEnabled,
    rerollsRemaining: g.rerollsRemaining,
  };
}

// ---- Evaluate -------------------------------------------------------------------------------------

export function evaluate(meta: MetaLookup, ctx: Context, state: DraftState, target: RecommendationTarget | null, hover: number): unknown {
  const predictor = new LanePredictor(meta, ctx.seats);
  const enemyPredictions = predictor.predict(state.enemies);
  const allyPredictions = predictor.predict(state.allies);
  const comp = new CompAnalyzer(meta, ctx.traits);
  const recommender = new Recommender(meta, ctx.traits);

  let picks: unknown = null;
  let bans: unknown = null;
  let hoverResult: unknown = null;
  let bench: unknown = null;

  if (state.isActive && target && usesLanes(state.queue)) {
    picks = set(recommender.recommend(state, { ...target, action: 'pick' }, enemyPredictions, allyPredictions, null, LIMIT));
    bans = set(recommender.recommend(state, { ...target, action: 'ban' }, enemyPredictions, allyPredictions, null, LIMIT));
  }

  const mine = localSlot(state);
  if (state.isActive && hover !== 0 && mine && usesLanes(state.queue)) {
    const evaluation = recommender.evaluate(state, { slot: mine, action: 'pick', isFollowingTurn: false }, hover, enemyPredictions, allyPredictions);
    if (evaluation) {
      hoverResult = {
        championId: hover,
        rank: evaluation.rank,
        candidates: evaluation.ranking.length,
        lane: laneKey(evaluation.lane),
        leader: evaluation.ranking.length > 0 ? item(evaluation.ranking[0]!) : null,
        item: item(evaluation.item),
        decimals: decimalsOf(evaluation.ranking.slice(0, LIMIT)),
      };
    }
  }

  if (state.isActive && isAram(state.queue) && mine) {
    const own = effectiveChampionId(mine);
    const ids = [...state.bench, own].filter((id, i, all) => id !== 0 && ctx.aramStats.has(id) && all.indexOf(id) === i);
    const stats = new Map(ids.map((id) => [id, ctx.aramStats.get(id)!]));
    const rows = rankAram(own, state.bench, stats, (id) => meta.championName(id));
    bench = {
      own,
      bench: state.bench,
      rerolls: state.rerollsRemaining,
      tied: countLeadingTies(rows),
      decimals: decimalsOf(rows),
      items: rows.map(item),
    };
  }

  const balance = estimateBalance(meta, state, allyPredictions, enemyPredictions);

  return {
    state: {
      isActive: state.isActive,
      phase: state.phase,
      queue: state.queue,
      usesLanes: usesLanes(state.queue),
      localCellId: state.localCellId,
      allies: state.allies.map(slotJson),
      enemies: state.enemies.map(slotJson),
      allyBans: state.allyBans,
      enemyBans: state.enemyBans,
      unavailable: [...state.unavailable].sort((a, b) => a - b),
      turn: state.turn,
      bench: state.bench,
      benchEnabled: state.benchEnabled,
      rerollsRemaining: state.rerollsRemaining,
    },
    target: target ? { cellId: target.slot.cellId, action: target.action, isFollowingTurn: target.isFollowingTurn } : null,
    predictions: { enemies: predictions(enemyPredictions), allies: predictions(allyPredictions) },
    picks,
    bans,
    hover: hoverResult,
    comp: {
      allies: profile(comp.analyze(state.allies.map(effectiveChampionId))),
      enemies: profile(comp.analyze(state.enemies.map(effectiveChampionId))),
    },
    balance: {
      allyWinRate: balance.allyWinRate,
      ratedChampions: balance.ratedChampions,
      contestedLanes: balance.contestedLanes,
      isComparable: balance.isComparable,
      hasData: balance.ratedChampions > 0 && balance.isComparable,
    },
    laneMatchups: laneMatchups(meta, allyPredictions, enemyPredictions).map((m) => ({
      lane: laneKey(m.lane),
      allyId: m.allyId,
      enemyId: m.enemyId,
      winRate: m.winRate,
      play: m.play,
      isInferred: m.isInferred,
      measured: m.measured,
    })),
    aramBench: bench,
  };
}

const slotJson = (s: DraftSlot) => ({
  cellId: s.cellId,
  index: s.index,
  isAlly: s.isAlly,
  lockedChampionId: s.lockedChampionId,
  hoverChampionId: s.hoverChampionId,
  assignedLane: laneKey(s.assignedLane),
  effectiveChampionId: effectiveChampionId(s),
  isLocked: isLocked(s),
});

function set(s: RecommendationSet) {
  const isBan = s.action === 'ban';
  return {
    cellId: s.cellId,
    lane: laneKey(s.lane),
    action: s.action,
    tied: isBan ? 0 : countLeadingTies(s.items),
    decimals: isBan ? 1 : decimalsOf(s.items),
    items: s.items.map(item),
  };
}

function item(r: Recommendation) {
  return {
    championId: r.championId,
    name: r.name,
    score: r.score,
    uncertainty: r.uncertainty,
    uncertaintyPoints: errorAsPoints(r.uncertainty),
    reasons: r.reasons.map((x) => ({ text: x.text, tone: x.tone, hint: x.hint })),
    breakdown: r.breakdown.map((t) => ({
      kind: t.kind,
      logOdds: t.logOdds,
      points: termPoints(t),
      isGate: isGate(t),
      gate: termGate(t),
      label: termLabel(t.kind),
    })),
  };
}

const predictions = (result: LanePredictionResult) =>
  result.predictions.map((p) => ({
    cellId: p.cellId,
    championId: p.championId,
    lane: laneKey(p.lane),
    confidence: p.confidence,
    probabilities: p.probabilities,
    isManual: p.isManual,
    isUncertain: isUncertain(p),
  }));

const profile = (p: CompProfile) => ({
  count: p.count,
  physicalShare: p.physicalShare,
  magicShare: p.magicShare,
  frontlineCount: p.frontlineCount,
  rangedCount: p.rangedCount,
  maxEngage: p.maxEngage,
  maxPeel: p.maxPeel,
  totalCrowdControl: p.totalCrowdControl,
  lateScalingCount: p.lateScalingCount,
  traitsKnown: p.traitsKnown,
  traitCoverage: traitCoverage(p),
  findings: p.findings.map((f) => ({ issue: f.issue, text: f.text, severity: f.severity })),
});

// ---- Replay ---------------------------------------------------------------------------------------

export function replay(lines: string[], ctx: Context): unknown[] {
  const meta = new MetaLookup(ctx.snapshot);
  const turns = new TurnTracker();
  const frames: unknown[] = [];
  let wasActive = false;
  let lastProgress = 0;
  let lastLocal = -1;

  lines.forEach((line, index) => {
    if (line.trim() === '') return;

    let root: unknown;
    try {
      root = JSON.parse(line);
    } catch {
      frames.push({ line: index, skipped: 'unlesbar' });
      return;
    }
    const record = (root ?? {}) as Record<string, unknown>;
    if (typeof record.phase === 'string') {
      frames.push({ line: index, gameflowPhase: record.phase });
      return;
    }

    const state = draftStateFrom(record.session ?? null);
    const progress = state.unavailable.size;
    const isNewDraft = state.isActive && (!wasActive || progress < lastProgress || state.localCellId !== lastLocal);
    if (isNewDraft) turns.reset();
    wasActive = state.isActive;
    lastProgress = progress;
    lastLocal = state.localCellId;

    const target = turns.resolve(state);
    const mine = localSlot(state);
    const hover = mine && !isLocked(mine) && mine.hoverChampionId > 0 ? mine.hoverChampionId : 0;
    frames.push({ line: index, isNewDraft, frame: evaluate(meta, ctx, state, target, hover) });
  });
  return frames;
}

// ---- comparison -----------------------------------------------------------------------------------

export interface Difference {
  path: string;
  expected: unknown;
  actual: unknown;
}

/** Strings, booleans, order and lengths exactly; numbers to `tolerance`; "NaN" matches NaN. */
export function compare(expected: unknown, actual: unknown, tolerance: number, path = '$', out: Difference[] = []): Difference[] {
  if (out.length > 40) return out;
  if (expected === 'NaN' && typeof actual === 'number' && Number.isNaN(actual)) return out;
  if (typeof expected === 'number' && typeof actual === 'number') {
    if (!(Math.abs(expected - actual) <= tolerance)) out.push({ path, expected, actual });
    return out;
  }
  if (Array.isArray(expected)) {
    if (!Array.isArray(actual) || actual.length !== expected.length) {
      out.push({ path: `${path}.length`, expected: expected.length, actual: Array.isArray(actual) ? actual.length : actual });
      if (!Array.isArray(actual)) return out;
    }
    const n = Math.min(expected.length, (actual as unknown[]).length);
    for (let i = 0; i < n; i++) compare(expected[i], (actual as unknown[])[i], tolerance, `${path}[${i}]`, out);
    return out;
  }
  if (expected !== null && typeof expected === 'object') {
    if (actual === null || typeof actual !== 'object') {
      out.push({ path, expected: '{…}', actual });
      return out;
    }
    for (const key of Object.keys(expected)) compare((expected as any)[key], (actual as any)[key], tolerance, `${path}.${key}`, out);
    for (const key of Object.keys(actual)) if (!(key in expected)) out.push({ path: `${path}.${key}`, expected: undefined, actual: (actual as any)[key] });
    return out;
  }
  if (expected !== actual) out.push({ path, expected, actual });
  return out;
}
