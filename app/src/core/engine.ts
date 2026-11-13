// The model run on one champ-select session: lane predictions, picks, bans, the hover, comp, draft
// chance, lane duels and the ARAM bench, as one plain DraftFrame. The frame has exactly the shape the
// 1.5 golden files use, so the view adapter reads live frames and reference frames alike, and the
// golden test checks this very function.

import { estimateBalance, laneMatchups, rankAram, type AramStat } from './model/advisors';
import { CompAnalyzer, traitCoverage, type CompProfile, type Plan } from './model/compAnalyzer';
import { draftParts } from './model/draftModel';
import { sigmoid } from './model/scoreModel';
import {
  draftStateFrom,
  partyCells,
  effectiveChampionId,
  isLocked,
  localSlot,
  type DraftPhase,
  type DraftSlot,
  type DraftState,
  type TurnAction,
} from './model/draftState';
import { Lane, laneFromKey, laneKey } from './model/lane';
import { isUncertain, LanePredictor, SeatPriors, type LanePredictionResult } from './model/lanePredictor';
import { MetaLookup, type MetaSnapshot } from './model/metaLookup';
import { isAram, usesLanes, type QueueKind } from './model/queue';
import { isGate, Recommender, termGate, termLabel, termPoints, type Recommendation, type RecommendationSet } from './model/recommender';
import { countLeadingTies, decimalsOf, errorAsPoints } from './model/scoreError';
import { TankShred } from './model/tankShred';
import { planTeam, planTeamBans, type TeamPlan } from './model/teamPlanner';
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
  /** Only on the seats of the local player's party, and only when true. */
  inParty?: true;
}

export interface FrameItem {
  championId: number;
  name: string;
  score: number;
  uncertainty: number;
  uncertaintyPoints: number;
  reasons: { text: string; tone: string; hint: string | null }[];
  breakdown: { kind: string; logOdds: number | null; points: number; isGate: boolean; gate: number; label: string }[];
  /** New in 2.1: open counters while the lane opponent is open; absent otherwise. */
  openCounters?: number;
  /** New in 2.1 (D2): open own lanes the pick is strong on, two or more. */
  flexLanes?: LaneName[];
  /** New in 2.1 (D3): bans only, the lane the enemy would play the champion on. */
  banLane?: LaneName;
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
  /** New in 2.1. */
  earlyScalingCount: number;
  /** New in 2.1. */
  strongWaveclearCount: number;
  traitsKnown: number;
  traitCoverage: number;
  findings: { issue: string; text: string; severity: number }[];
  /** New in 2.1 (B3). */
  plan: Plan | null;
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
    /** New in 2.1; absent from the 1.5 reference frames. */
    allyBanIntents?: { cellId: number; championId: number }[];
    /** New in 2.1. */
    allyPickOrder?: number[];
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
  balance: {
    allyWinRate: number;
    ratedChampions: number;
    contestedLanes: number;
    isComparable: boolean;
    hasData: boolean;
    /** Own data (2.1): what the chance is made of, in log-odds from the own side. */
    parts?: { strength: number; duels: number; duo: number; versus: number; comp: number };
    /** Own data (2.1): the chance with the list's first pick on the advised seat, while that seat is empty. */
    withTop?: { championId: number; allyWinRate: number };
  };
  laneMatchups: { lane: LaneName; allyId: number; enemyId: number; winRate: number | null; play: number; isInferred: boolean; measured: number | null }[];
  aramBench: { own: number; bench: number[]; rerolls: number; tied: number; decimals: number; items: FrameItem[] } | null;
  /**
   * New in 2.1: what the own team still lacks for the seat about to pick. Counted without that seat's
   * own hover, which would otherwise fill the gap before the pick. Null without an open ally seat to advise.
   */
  gap?: { cellId: number; count: number; findings: FrameProfile['findings'] } | null;
  /** New in 2.1: where the advised seat stands in the pick order (pickHintOf). */
  pickHint?: PickHint | null;
  /** New in 2.1 (D1): the open seats planned together, in team mode only. */
  teamPlan?: { picks: { cellId: number; lane: LaneName; item: FrameItem }[]; first: number | null; last: number | null } | null;
  /** New in 2.1 (D4): the plan the team chose for this draft. */
  chosenPlan?: Plan | null;
  /** New in 2.1 (D3): the team's ban set, in team mode while bans are left. */
  teamBans?: { championId: number; lane: LaneName; score: number | null; by: number | null }[] | null;
}

/** What the pick position means for the advised seat's list. */
export interface PickHint {
  /** Lane opponent open and the hover or the top pick has two or more open counters. */
  risky: { championId: number; counters: number } | null;
  /** In the leading tie group, the pick with the fewest open counters — fewer than the risky one's. */
  safest: { championId: number; counters: number } | null;
  /** Another ally picks after this seat, so the two could swap. Unknown order counts as yes. */
  laterAlly: boolean;
  /** Lane opponent known, or this seat picks last for its team: the comp gap matters first. */
  gapFirst: boolean;
}

/**
 * B1: an early pick with several open counters may be better swapped to later, or exchanged for the
 * equally strong pick that is easiest to answer; a late or counter pick looks at the comp gap first.
 */
export function pickHintOf(picks: FrameSet, hover: FrameItem | null, cellId: number, allyPickOrder: readonly number[]): PickHint | null {
  const top = picks.items[0];
  if (!top) return null;
  const position = allyPickOrder.indexOf(cellId);
  const last = position >= 0 && position === allyPickOrder.length - 1;
  const laterAlly = position < 0 || position < allyPickOrder.length - 1;
  const laneOpen = top.openCounters !== undefined;

  let risky: PickHint['risky'] = null;
  for (const item of [hover, top]) {
    if (!item || item.openCounters === undefined || item.openCounters < 2) continue;
    if (!risky || item.openCounters > risky.counters) risky = { championId: item.championId, counters: item.openCounters };
  }

  let safest: PickHint['safest'] = null;
  if (risky) {
    for (const item of picks.items.slice(0, Math.max(1, picks.tied))) {
      const counters = item.openCounters ?? Infinity;
      if (counters < risky.counters && (!safest || counters < safest.counters)) safest = { championId: item.championId, counters };
    }
  }

  return { risky, safest, laterAlly, gapFirst: !laneOpen || last };
}

export interface EngineOptions {
  traits?: TraitTable;
  seats?: SeatPriors;
  aramStats?: ReadonlyMap<number, AramStat>;
  /** Whether the ARAM rates are rounded to whole percent (OP.GG) rather than counted exactly (own data). */
  aramRounded?: boolean;
  /** Who shreds tanks, from the own build file (B4); none without one. */
  tankShred?: TankShred;
}

export class DraftEngine {
  readonly meta: MetaLookup;
  readonly traits: TraitTable;
  readonly seats: SeatPriors;
  readonly aramStats: ReadonlyMap<number, AramStat>;
  readonly aramRounded: boolean;
  private readonly shred: TankShred;

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
    this.shred = options.tankShred ?? TankShred.none;
  }

  /** Champions the local player may pick (owned or free this week); null until the client said. */
  private selectable: ReadonlySet<number> | null = null;

  /**
   * The client's pickable-champion-ids. Filters the own seat's picks and its hover rank — never a
   * teammate's seat and never bans (1.5's ownership filter). Reset with every new draft, because
   * the free rotation and the account can change between drafts.
   */
  /** D4: the plan the team chose for this draft; reset with every new draft, never stored. */
  private chosenPlan: Plan | null = null;

  choosePlan(plan: Plan | null): void {
    this.chosenPlan = plan;
  }

  /**
   * D1: plan the open seats of the own party together (setting "Team-Modus"). It acts by itself
   * wherever the client says who queued together, and nowhere else: alone there is nothing to plan.
   */
  private teamMode = false;

  setTeamMode(on: boolean): void {
    this.teamMode = on;
  }

  setSelectable(ids: ReadonlySet<number> | null): void {
    this.selectable = ids && ids.size > 0 ? ids : null;
  }

  get hasSelectable(): boolean {
    return this.selectable !== null;
  }

  /** Lanes the user fixed by hand (1.5's lane picker), by cell; they hold until the draft ends. */
  private readonly manualLanes = new Map<number, Lane>();

  /**
   * Fixes a seat's lane by hand, or gives it back to the prediction (Lane.Unknown). One lane, one
   * seat: the same lane pinned on another seat of that team would contradict and collapse every
   * prediction, so the other pin goes (1.5's SetManualLane).
   */
  setManualLane(cellId: number, lane: Lane, state: DraftState): void {
    if (lane === Lane.Unknown) {
      this.manualLanes.delete(cellId);
      return;
    }
    const team = state.allies.some((slot) => slot.cellId === cellId) ? state.allies : state.enemies;
    for (const slot of team) if (slot.cellId !== cellId && this.manualLanes.get(slot.cellId) === lane) this.manualLanes.delete(slot.cellId);
    this.manualLanes.set(cellId, lane);
  }

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
    if (isNewDraft || !state.isActive) {
      this.selectable = null;
      this.manualLanes.clear();
      this.chosenPlan = null;
    }
    this.wasActive = state.isActive;
    this.lastProgress = progress;
    this.lastLocal = state.localCellId;

    const target = this.turns.resolve(state);
    const mine = localSlot(state);
    const hover = mine && !isLocked(mine) && mine.hoverChampionId > 0 ? mine.hoverChampionId : 0;
    return { isNewDraft, state, frame: this.evaluate(state, target, hover) };
  }

  /** The seat a click chose, while it holds; null when the list follows the clock. */
  get manualSeat(): number | null {
    return this.turns.manualSeat;
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
    const manual = this.manualLanes.size > 0 ? this.manualLanes : null;
    const enemyPredictions = predictor.predict(state.enemies, manual);
    const allyPredictions = predictor.predict(state.allies, manual);
    // The party the team mode plans for: two seats or more, or nothing.
    const partySeats = this.teamMode ? partyCells(state) : null;
    const party = partySeats && partySeats.size >= 2 ? partySeats : null;
    const comp = new CompAnalyzer(meta, this.traits, this.shred);
    const recommender = new Recommender(meta, this.traits, this.shred, this.chosenPlan);

    let picks: FrameSet | null = null;
    let bans: FrameSet | null = null;
    let hoverResult: DraftFrame['hover'] = null;
    let bench: DraftFrame['aramBench'] = null;

    if (state.isActive && target && usesLanes(state.queue)) {
      const own = target.slot.cellId === state.localCellId ? this.selectable : null;
      picks = frameSet(recommender.recommend(state, { ...target, action: 'pick' }, enemyPredictions, allyPredictions, own, LIST_LIMIT));
      bans = frameSet(recommender.recommend(state, { ...target, action: 'ban' }, enemyPredictions, allyPredictions, null, LIST_LIMIT));
    }

    const mine = localSlot(state);
    if (state.isActive && hover !== 0 && mine && usesLanes(state.queue)) {
      const evaluation = recommender.evaluate(state, { slot: mine, action: 'pick', isFollowingTurn: false }, hover, enemyPredictions, allyPredictions, this.selectable);
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

    const profiles = { allies: comp.analyze(state.allies.map(effectiveChampionId)), enemies: comp.analyze(state.enemies.map(effectiveChampionId)) };
    const balance = estimateBalance(meta, state, allyPredictions, enemyPredictions, profiles);

    const advised = target ? state.allies.find((slot) => slot.cellId === target.slot.cellId) : undefined;
    // "Your draft: 52 % → with Nami 54 %": the same model with the list's first pick on the empty seat.
    let withTop: { championId: number; allyWinRate: number } | null = null;
    const first = picks?.items[0];
    const pickLane = picks ? laneFromKey(picks.lane) : Lane.Unknown;
    if (balance.parts && first && advised && effectiveChampionId(advised) === 0 && pickLane !== Lane.Unknown) {
      const seats = (predictions: LanePredictionResult) => predictions.predictions.filter((p) => p.championId !== 0).map((p) => ({ championId: p.championId, lane: p.lane }));
      const withPick = draftParts(meta, [...seats(allyPredictions), { championId: first.championId, lane: pickLane }], seats(enemyPredictions), {
        allies: comp.analyze([...state.allies.map(effectiveChampionId), first.championId]),
        enemies: profiles.enemies,
      });
      withTop = { championId: first.championId, allyWinRate: sigmoid(withPick.total) };
    }
    const pickHint =
      picks && advised && !isLocked(advised)
        ? pickHintOf(picks, advised.cellId === mine?.cellId && hoverResult ? hoverResult.item : null, advised.cellId, state.allyPickOrder)
        : null;
    const gap =
      state.isActive && usesLanes(state.queue) && advised && !isLocked(advised)
        ? gapOf(advised.cellId, comp.analyze(state.allies.filter((slot) => slot !== advised).map(effectiveChampionId)))
        : null;

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
        allyBanIntents: state.allyBanIntents.map((intent) => ({ ...intent })),
        allyPickOrder: [...state.allyPickOrder],
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
        allies: frameProfile(profiles.allies),
        enemies: frameProfile(profiles.enemies),
      },
      balance: {
        allyWinRate: balance.allyWinRate,
        ratedChampions: balance.ratedChampions,
        contestedLanes: balance.contestedLanes,
        isComparable: balance.isComparable,
        hasData: balance.ratedChampions > 0 && balance.isComparable,
        ...(balance.parts
          ? { parts: { strength: balance.parts.strength, duels: balance.parts.duels, duo: balance.parts.duo, versus: balance.parts.versus, comp: balance.parts.comp } }
          : {}),
        ...(withTop ? { withTop } : {}),
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
      gap,
      pickHint,
      chosenPlan: this.chosenPlan,
      teamPlan: party && state.isActive && usesLanes(state.queue) ? frameTeamPlan(planTeam(recommender, meta, state, enemyPredictions, allyPredictions, this.selectable, party)) : null,
      teamBans:
        party && bans && state.phase !== 'finalization'
          ? (planTeamBans(recommender, meta, state, enemyPredictions, allyPredictions, party)?.map((b) => ({ ...b, lane: laneKey(b.lane) })) ?? null)
          : null,
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
    ...(g.inParty ? { inParty: true } : {}),
  });
  return {
    isActive: s.isActive,
    localCellId: s.localCellId,
    allies: s.allies.map(slot),
    enemies: s.enemies.map(slot),
    allyBans: s.allyBans,
    enemyBans: s.enemyBans,
    allyBanIntents: s.allyBanIntents ?? [],
    allyPickOrder: s.allyPickOrder ?? [],
    // A stored frame has no actions: every ban not yet in the summary counts as open.
    allyBansOpen: Math.max(0, s.allies.length - s.allyBans.length),
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
  ...(s.inParty ? { inParty: true as const } : {}),
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
    ...(r.openCounters !== undefined ? { openCounters: r.openCounters } : {}),
    ...(r.flexLanes ? { flexLanes: r.flexLanes.map(laneKey) } : {}),
    ...(r.banLane !== undefined ? { banLane: laneKey(r.banLane) } : {}),
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

const frameTeamPlan = (plan: TeamPlan | null): DraftFrame['teamPlan'] =>
  plan && {
    picks: plan.picks.map((p) => ({ cellId: p.cellId, lane: laneKey(p.lane), item: frameItem(p.item) })),
    first: plan.first,
    last: plan.last,
  };

const gapOf = (cellId: number, p: CompProfile): NonNullable<DraftFrame['gap']> => ({
  cellId,
  count: p.count,
  findings: p.findings.map((f) => ({ issue: f.issue, text: f.text, severity: f.severity })),
});

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
  earlyScalingCount: p.earlyScalingCount,
  strongWaveclearCount: p.strongWaveclearCount,
  traitsKnown: p.traitsKnown,
  traitCoverage: traitCoverage(p),
  findings: p.findings.map((f) => ({ issue: f.issue, text: f.text, severity: f.severity })),
  plan: p.plan,
});
