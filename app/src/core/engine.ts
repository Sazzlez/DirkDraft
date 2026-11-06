// The model run on one champ-select session: lane predictions, picks, bans, the hover, comp, draft
// chance, lane duels and the ARAM bench, as one plain DraftFrame. The frame has exactly the shape the
// 1.5 golden files use, so the view adapter reads live frames and reference frames alike, and the
// golden test checks this very function.

import { estimateBalance, laneMatchups, rankAram, type AramStat } from './model/advisors';
import { CompAnalyzer, traitCoverage, type CompProfile } from './model/compAnalyzer';
import {
  draftStateFrom,
  effectiveChampionId,
  isLocked,
  localSlot,
  type DraftPhase,
  type DraftSlot,
  type DraftState,
  type TurnAction,
} from './model/draftState';
import { laneFromKey, laneKey, type Lane } from './model/lane';
import { isUncertain, LanePredictor, SeatPriors, type LanePredictionResult } from './model/lanePredictor';
import { MetaLookup, type MetaSnapshot } from './model/metaLookup';
import { isAram, usesLanes, type QueueKind } from './model/queue';
import { isGate, Recommender, termGate, termLabel, termPoints, type Recommendation, type RecommendationSet } from './model/recommender';
import { countLeadingTies, decimalsOf, errorAsPoints } from './model/scoreError';
import { TraitTable } from './model/traits';
import { TurnTracker, type RecommendationTarget } from './model/turnTracker';
import priors from './model/data/pick_order_priors.json';

/** Rows per list, as in 1.5. */
export const LIST_LIMIT = 8;

type LaneName = ReturnType<typeof laneKey>;

export interface FrameSlot {
  cellId: number;
  index: number;
  isAlly: boolean;
  lockedChampionId: number;
  hoverChampionId: number;
  assignedLane: LaneName;
  effectiveChampionId: number;
  isLocked: boolean;
}

export interface FrameItem {
  championId: number;
  name: string;
  score: number;
  uncertainty: number;
  uncertaintyPoints: number;
  reasons: { text: string; tone: string; hint: string | null }[];
  breakdown: { kind: string; logOdds: number | null; points: number; isGate: boolean; gate: number; label: string }[];
}

export interface FrameSet {
  cellId: number;
  lane: LaneName;
  action: TurnAction;
  tied: number;
  decimals: number;
  items: FrameItem[];
}

export interface FrameProfile {
  count: number;
  physicalShare: number;
  magicShare: number;
  frontlineCount: number;
  rangedCount: number;
  maxEngage: number;
  maxPeel: number;
  totalCrowdControl: number;
  lateScalingCount: number;
  traitsKnown: number;
  traitCoverage: number;
  findings: { issue: string; text: string; severity: number }[];
}

export interface FramePrediction {
  cellId: number;
  championId: number;
  lane: LaneName;
  confidence: number;
  probabilities: number[];
  isManual: boolean;
  isUncertain: boolean;
}

export interface DraftFrame {
  state: {
    isActive: boolean;
    phase: DraftPhase;
    queue: QueueKind;
    usesLanes: boolean;
    localCellId: number;
    allies: FrameSlot[];
    enemies: FrameSlot[];
    allyBans: number[];
    enemyBans: number[];
    unavailable: number[];
    turn: { cellId: number; isAlly: boolean; isLocalPlayer: boolean; action: TurnAction } | null;
    bench: number[];
    benchEnabled: boolean;
    rerollsRemaining: number;
  };
  target: { cellId: number; action: TurnAction; isFollowingTurn: boolean } | null;
  predictions: { enemies: FramePrediction[]; allies: FramePrediction[] };
  picks: FrameSet | null;
  bans: FrameSet | null;
  hover: { championId: number; rank: number; candidates: number; lane: LaneName; leader: FrameItem | null; item: FrameItem; decimals: number } | null;
  comp: { allies: FrameProfile; enemies: FrameProfile };
  balance: { allyWinRate: number; ratedChampions: number; contestedLanes: number; isComparable: boolean; hasData: boolean };
  laneMatchups: { lane: LaneName; allyId: number; enemyId: number; winRate: number | null; play: number; isInferred: boolean; measured: number | null }[];
  aramBench: { own: number; bench: number[]; rerolls: number; tied: number; decimals: number; items: FrameItem[] } | null;
  /** The best picks among the player's pool; absent without a pool, null when it is empty here. */
  poolPicks?: FrameSet | null;
}

export interface EngineOptions {
  traits?: TraitTable;
  seats?: SeatPriors;
  aramStats?: ReadonlyMap<number, AramStat>;
  /** Whether the ARAM rates are rounded to whole percent (OP.GG) rather than counted exactly (own data). */
  aramRounded?: boolean;
  /**
   * The player's pool for a lane ("Mein Pool"). When given, the frame carries a second pick list
   * ranked over the pool alone — the best eight of the pool, not the pool members of the top eight.
   */
  pool?: (lane: Lane) => ReadonlySet<number> | null;
}

export class DraftEngine {
  readonly meta: MetaLookup;
  readonly traits: TraitTable;
  readonly seats: SeatPriors;
  readonly aramStats: ReadonlyMap<number, AramStat>;
  readonly aramRounded: boolean;

  private readonly turns = new TurnTracker();
  private wasActive = false;
  private lastProgress = 0;
  private lastLocal = -1;

  constructor(snapshot: MetaSnapshot, options: EngineOptions = {}) {
    this.meta = new MetaLookup(snapshot);
    this.traits = options.traits ?? TraitTable.bundled();
    this.seats = options.seats ?? SeatPriors.fromJson(priors);
    this.aramStats = options.aramStats ?? new Map();
    this.aramRounded = options.aramRounded ?? true;
    this.pool = options.pool ?? null;
  }

  private readonly pool: ((lane: Lane) => ReadonlySet<number> | null) | null;

  /**
   * One session update from the client (the parsed /lol-champ-select/v1/session, or null outside
   * champ select). A new draft resets the turn tracker: the session went active, fewer champions
   * are taken than before, or the local seat moved.
   */
  update(session: unknown): { isNewDraft: boolean; state: DraftState; frame: DraftFrame } {
    const state = draftStateFrom(session);
    const progress = state.unavailable.size;
    const isNewDraft = state.isActive && (!this.wasActive || progress < this.lastProgress || state.localCellId !== this.lastLocal);
    if (isNewDraft) this.turns.reset();
    this.wasActive = state.isActive;
    this.lastProgress = progress;
    this.lastLocal = state.localCellId;

    const target = this.turns.resolve(state);
    const mine = localSlot(state);
    const hover = mine && !isLocked(mine) && mine.hoverChampionId > 0 ? mine.hoverChampionId : 0;
    return { isNewDraft, state, frame: this.evaluate(state, target, hover) };
  }

  /** A manual click on an ally seat; holds until the clock passes to someone else. */
  selectSeat(cellId: number, state: DraftState): void {
    this.turns.selectManually(cellId, state);
  }

  resolveTarget(state: DraftState): RecommendationTarget | null {
    return this.turns.resolve(state);
  }

  evaluate(state: DraftState, target: RecommendationTarget | null, hover: number): DraftFrame {
    const meta = this.meta;
    const predictor = new LanePredictor(meta, this.seats);
    const enemyPredictions = predictor.predict(state.enemies);
    const allyPredictions = predictor.predict(state.allies);
    const comp = new CompAnalyzer(meta, this.traits);
    const recommender = new Recommender(meta, this.traits);

    let picks: FrameSet | null = null;
    let poolPicks: FrameSet | null = null;
    let bans: FrameSet | null = null;
    let hoverResult: DraftFrame['hover'] = null;
    let bench: DraftFrame['aramBench'] = null;

    if (state.isActive && target && usesLanes(state.queue)) {
      const pickSet = recommender.recommend(state, { ...target, action: 'pick' }, enemyPredictions, allyPredictions, null, LIST_LIMIT);
      picks = frameSet(pickSet);
      bans = frameSet(recommender.recommend(state, { ...target, action: 'ban' }, enemyPredictions, allyPredictions, null, LIST_LIMIT));

      const pool = this.pool?.(pickSet.lane) ?? null;
      if (pool && pool.size > 0) {
        poolPicks = frameSet(recommender.recommend(state, { ...target, action: 'pick' }, enemyPredictions, allyPredictions, pool, LIST_LIMIT));
      }
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
          leader: evaluation.ranking.length > 0 ? frameItem(evaluation.ranking[0]!) : null,
          item: frameItem(evaluation.item),
          decimals: decimalsOf(evaluation.ranking.slice(0, LIST_LIMIT)),
        };
      }
    }

    if (state.isActive && isAram(state.queue) && mine) {
      const own = effectiveChampionId(mine);
      const ids = [...state.bench, own].filter((id, i, all) => id !== 0 && this.aramStats.has(id) && all.indexOf(id) === i);
      const stats = new Map(ids.map((id) => [id, this.aramStats.get(id)!]));
      const rows = rankAram(own, state.bench, stats, (id) => meta.championName(id), this.aramRounded);
      bench = {
        own,
        bench: [...state.bench],
        rerolls: state.rerollsRemaining,
        tied: countLeadingTies(rows),
        decimals: decimalsOf(rows),
        items: rows.map(frameItem),
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
        allies: state.allies.map(frameSlot),
        enemies: state.enemies.map(frameSlot),
        allyBans: [...state.allyBans],
        enemyBans: [...state.enemyBans],
        unavailable: [...state.unavailable].sort((a, b) => a - b),
        turn: state.turn,
        bench: [...state.bench],
        benchEnabled: state.benchEnabled,
        rerollsRemaining: state.rerollsRemaining,
      },
      target: target ? { cellId: target.slot.cellId, action: target.action, isFollowingTurn: target.isFollowingTurn } : null,
      predictions: { enemies: framePredictions(enemyPredictions), allies: framePredictions(allyPredictions) },
      picks,
      bans,
      hover: hoverResult,
      comp: {
        allies: frameProfile(comp.analyze(state.allies.map(effectiveChampionId))),
        enemies: frameProfile(comp.analyze(state.enemies.map(effectiveChampionId))),
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
      // Only with a pool: the golden frames have no such key, and the comparison is key-exact.
      ...(this.pool ? { poolPicks } : {}),
    };
  }
}

/** The inverse of the frame's state block, for drafts that were stored rather than recorded. */
export function stateFromFrame(s: DraftFrame['state']): DraftState {
  const slot = (g: FrameSlot): DraftSlot => ({
    cellId: g.cellId,
    index: g.index,
    isAlly: g.isAlly,
    lockedChampionId: g.lockedChampionId,
    hoverChampionId: g.hoverChampionId,
    assignedLane: laneFromKey(g.assignedLane),
  });
  return {
    isActive: s.isActive,
    localCellId: s.localCellId,
    allies: s.allies.map(slot),
    enemies: s.enemies.map(slot),
    allyBans: s.allyBans,
    enemyBans: s.enemyBans,
    phase: s.phase,
    turn: s.turn,
    unavailable: new Set(s.unavailable),
    queue: s.queue,
    bench: s.bench,
    benchEnabled: s.benchEnabled,
    rerollsRemaining: s.rerollsRemaining,
  };
}

const frameSlot = (s: DraftSlot): FrameSlot => ({
  cellId: s.cellId,
  index: s.index,
  isAlly: s.isAlly,
  lockedChampionId: s.lockedChampionId,
  hoverChampionId: s.hoverChampionId,
  assignedLane: laneKey(s.assignedLane),
  effectiveChampionId: effectiveChampionId(s),
  isLocked: isLocked(s),
});

function frameSet(s: RecommendationSet): FrameSet {
  const isBan = s.action === 'ban';
  return {
    cellId: s.cellId,
    lane: laneKey(s.lane),
    action: s.action,
    tied: isBan ? 0 : countLeadingTies(s.items),
    decimals: isBan ? 1 : decimalsOf(s.items),
    items: s.items.map(frameItem),
  };
}

function frameItem(r: Recommendation): FrameItem {
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

const framePredictions = (result: LanePredictionResult): FramePrediction[] =>
  result.predictions.map((p) => ({
    cellId: p.cellId,
    championId: p.championId,
    lane: laneKey(p.lane),
    confidence: p.confidence,
    probabilities: [...p.probabilities],
    isManual: p.isManual,
    isUncertain: isUncertain(p),
  }));

const frameProfile = (p: CompProfile): FrameProfile => ({
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
