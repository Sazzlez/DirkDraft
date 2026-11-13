// Port of DraftPilot.Core.Draft.Recommender and CounterRisk (1.5). Every piece of evidence is a
// shift in log-odds; picks sum them into a win rate with an error bar, bans into denied points.
// Texts and numbers match the 1.5 model to the character — the golden tests hold it to that.
//
// On our own data (meta.isOwnData) the picks follow the measured draft model instead (draftModel.ts,
// docs/modellpruefung.md Messung 21): a pick's terms are what the champion adds to the team's chance
// — its lane strength, its duel, its pairs with the enemies on other lanes, the botlane duo, and the
// line-up findings it closes or causes. OP.GG files, and with them the golden tests, are untouched.

import { CompAnalyzer, EMPTY_PROFILE, type CompProfile, type Plan } from './compAnalyzer';
import { BOT_DUO_WEIGHT, compValue, isBotDuo, STRENGTH_WEIGHT, VERSUS_WEIGHT, versusKind, type DraftSeat } from './draftModel';
import { effectiveChampionId, isLocked, type DraftState, type TurnAction } from './draftState';
import { ALL_LANES, Lane, laneDisplay } from './lane';
import type { LanePrediction, LanePredictionResult } from './lanePredictor';
import type { LaneView, MatchupView, MetaLookup, SynergyView } from './metaLookup';
import { netN0, netPercent } from './netFormat';
import { contra, neutral, pro, type Reason } from './reason';
import { ErrorBudget, logitVariance } from './scoreError';
import {
  asPoints,
  BAN_ALLY_SHARE,
  BAN_LANE_FOCUS,
  clamp,
  COMP_SCALE,
  LANE_DRIFT_PER_PATCH,
  logit,
  OFF_LANE_SHARE,
  sigmoid,
  SYNERGY_DAMPING,
  tierName,
} from './scoreModel';
import { SYNERGY_PRIOR } from './shrinkage';
import { TankShred } from './tankShred';
import { hasHardEngage, hasPeel, type TraitTable } from './traits';
import type { RecommendationTarget } from './turnTracker';

export type ScoreTermKind =
  | 'laneStrength'
  | 'laneMatchup'
  | 'enemyTeam'
  | 'synergy'
  | 'composition'
  | 'banStrength'
  | 'banLaneThreat'
  | 'banTeamThreat'
  | 'banPopularity';

export interface ScoreTerm {
  kind: ScoreTermKind;
  /** null: no data for this term — shown as "keine Daten", not as zero. */
  logOdds: number | null;
}

export const isGate = (term: ScoreTerm): boolean => term.kind === 'banPopularity';
export const termHasData = (term: ScoreTerm): boolean => term.logOdds !== null;
export const termPoints = (term: ScoreTerm): number => (isGate(term) ? 0 : asPoints(term.logOdds ?? 0));
export const termGate = (term: ScoreTerm): number => (isGate(term) ? (term.logOdds ?? 0) : 1);

export function termLabel(kind: ScoreTermKind): string {
  switch (kind) {
    case 'laneStrength':
      return 'Lane-Stärke';
    case 'laneMatchup':
      return 'Matchup';
    case 'enemyTeam':
      return 'Enemy-Team';
    case 'synergy':
      return 'Synergie';
    case 'composition':
      return 'Teamcomp';
    case 'banStrength':
      return 'Stärke allgemein';
    case 'banLaneThreat':
      return 'Gefahr auf Lane';
    case 'banTeamThreat':
      return 'Gefahr fürs Team';
    case 'banPopularity':
      return 'Pick- und Banrate';
  }
}

/** `ownData`: the numbers are DirkDraft's own count, which has no OP.GG tier to explain away. */
export function termHint(kind: ScoreTermKind, ownData = false): string {
  switch (kind) {
    case 'laneStrength':
      return ownData
        ? 'Wie gut der Champ auf dieser Lane allgemein läuft: die Winrate, geglättet nach Stichprobe.'
        : 'Wie gut der Champ auf dieser Lane allgemein läuft: die Winrate, geglättet nach Stichprobe. OP.GGs Tier steht als eigener Hinweis daneben und zählt hier nicht mit — es mischt Winrate und Pickrate, und die Winrate ist schon diese Zeile.';
    case 'laneMatchup':
      return 'Wie viel besser oder schlechter dieser Champ gegen den erwarteten Lane-Gegner abschneidet als auf dieser Lane üblich. Die allgemeine Stärke steckt schon in der Zeile darüber — sonst würde sie zweimal zählen.';
    case 'enemyTeam':
      return 'Winrate gegen die restlichen aufgedeckten Gegner — alle außer dem Lane-Gegner. Zählt gedämpft, weil sie nicht auf deiner Lane stehen.';
    case 'synergy':
      return 'Winrate zusammen mit den Teammates, die schon gepickt haben.';
    case 'composition':
      return 'Ob der Pick füllt, was dem Team fehlt und was gegen ihre Comp hilft — AD/AP, Frontline, Engage, CC. Der einzige Punkt ohne Winrate-Grundlage, deshalb bewusst klein gehalten.';
    case 'banStrength':
      return 'Wie stark der Champ auf seiner besten Lane gerade ist.';
    case 'banLaneThreat':
      return 'Wie stark der Champ genau auf deiner Lane wäre — dort trifft dich ein Nicht-Ban direkt.';
    case 'banTeamThreat':
      return 'Winrate des Champs gegen die Teammates, die schon gepickt haben.';
    case 'banPopularity':
      return 'Wie wahrscheinlich der Gegner den Champ überhaupt nimmt — Pick- und Banrate. Ein Ban auf einen Champ, den niemand spielt, ist verschenkt.';
  }
}

export interface Recommendation {
  championId: number;
  name: string;
  /** Picks: estimated win rate 0..1. Bans: denied points. */
  score: number;
  reasons: Reason[];
  breakdown: ScoreTerm[];
  /** Standard error of the score in log-odds; 0 for bans. */
  uncertainty: number;
  /** Champions still free that counter this pick on its lane, while the lane opponent is open (2.1). */
  openCounters?: number;
  /** Lanes still open in the own team on which this pick is strong too, two or more (D2). */
  flexLanes?: Lane[];
  /** Bans: the lane the enemy would most likely play the champion on (D3). */
  banLane?: Lane;
}

export interface RecommendationSet {
  cellId: number;
  lane: Lane;
  action: TurnAction;
  items: Recommendation[];
  allyComp: CompProfile;
  enemyComp: CompProfile;
}

export const EMPTY_SET: RecommendationSet = Object.freeze({
  cellId: -1,
  lane: Lane.Unknown,
  action: 'none',
  items: [],
  allyComp: EMPTY_PROFILE,
  enemyComp: EMPTY_PROFILE,
});

export interface PickEvaluation {
  item: Recommendation;
  ranking: Recommendation[];
  /** 1-based; 0 when the champion is not a candidate at all. */
  rank: number;
  lane: Lane;
}

// ---- CounterRisk ---------------------------------------------------------------------------------

export interface CounterThreat {
  championId: number;
  winRate: number;
  play: number;
  edge: number;
  measured: number;
}

/** Minimum edge over the pair's expectation, in log-odds, before a counter is worth naming. */
export const COUNTER_MINIMUM_EDGE = 0.08;

/** Champions still free that beat this one on its lane by more than the pair predicts. */
export function openCounters(
  meta: MetaLookup,
  championId: number,
  lane: Lane,
  unavailable: ReadonlySet<number>,
  limit = 3,
): CounterThreat[] {
  if (lane === Lane.Unknown || meta.laneStat(championId, lane) === null) return [];
  const threats: CounterThreat[] = [];
  for (const candidate of meta.roster(lane)) {
    if (candidate === championId || unavailable.has(candidate)) continue;
    const view = meta.matchup(candidate, championId, lane);
    if (!view) continue;
    const edge = logit(view.winRate) - meta.matchupBaseline(candidate, championId, lane);
    if (edge <= COUNTER_MINIMUM_EDGE) continue;
    threats.push({ championId: candidate, winRate: view.winRate, play: view.play, edge, measured: view.measured });
  }
  threats.sort((left, right) => right.edge - left.edge);
  return limit <= 0 || threats.length <= limit ? threats : threats.slice(0, limit);
}

// ---- Recommender ---------------------------------------------------------------------------------

interface Seat {
  lane: Lane;
  allies: number[];
  /** The allies with the lane each one plays, where it is known (own data: the botlane duo). */
  allySeats: DraftSeat[];
  hoveredByOthers: ReadonlySet<number>;
  /** What another teammate is about to ban; his ban hover counts as banned. */
  bannedByOthers: ReadonlySet<number>;
  /** Lanes of own seats nobody has locked yet, the advised seat's included (D2). */
  openLanes: readonly Lane[];
  allyComp: CompProfile;
  enemyComp: CompProfile;
}

interface BanLane {
  lane: Lane;
  stat: LaneView;
  gate: number;
}

const ordinal = (a: string, b: string): number => (a < b ? -1 : a > b ? 1 : 0);

/** D2: a flex lane needs this shrunk win rate and this share of the champion's games. */
export const FLEX_WIN_RATE = 0.5;
export const FLEX_ROLE_RATE = 0.1;

const FLEX_NAMES: Record<number, string> = { [Lane.Top]: 'Top', [Lane.Jungle]: 'Jungle', [Lane.Mid]: 'Mid', [Lane.Adc]: 'Bot', [Lane.Support]: 'Support' };

const finiteOr = (value: number, fallback: number): number => (Number.isFinite(value) ? value : fallback);

const winRateDelta = (view: { winRate: number }): number => view.winRate - 0.5;

export class Recommender {
  private readonly comp: CompAnalyzer;

  constructor(
    private readonly meta: MetaLookup,
    private readonly traits: TraitTable,
    shred: TankShred = TankShred.none,
    /** D4: the plan the team chose for this draft. */
    private readonly chosenPlan: Plan | null = null,
  ) {
    this.comp = new CompAnalyzer(meta, traits, shred, chosenPlan);
  }

  recommend(
    state: DraftState,
    target: RecommendationTarget,
    enemyLanes: LanePredictionResult,
    allyLanes: LanePredictionResult | null = null,
    selectable: ReadonlySet<number> | null = null,
    limit = 10,
  ): RecommendationSet {
    if (!state.isActive || this.meta.isEmpty) return EMPTY_SET;
    const seat = this.prepare(state, target, enemyLanes, allyLanes);
    const items =
      target.action === 'ban'
        ? this.scoreBans(state, seat.lane, seat.allies, new Set([...seat.hoveredByOthers, ...seat.bannedByOthers]), enemyLanes, limit, this.chosenPlan ?? seat.allyComp.plan)
        : top(this.scorePicks(state, seat, enemyLanes, selectable), limit);
    return { cellId: target.slot.cellId, lane: seat.lane, action: target.action, items, allyComp: seat.allyComp, enemyComp: seat.enemyComp };
  }

  /** Scores one champion — usually the hover — by the same rules, with its place in the list. */
  evaluate(
    state: DraftState,
    target: RecommendationTarget,
    championId: number,
    enemyLanes: LanePredictionResult,
    allyLanes: LanePredictionResult | null = null,
    selectable: ReadonlySet<number> | null = null,
  ): PickEvaluation | null {
    if (!state.isActive || this.meta.isEmpty || championId === 0) return null;
    const seat = this.prepare(state, { ...target, action: 'pick' }, enemyLanes, allyLanes);
    const ranking = ordered(this.scorePicks(state, seat, enemyLanes, selectable));
    const index = ranking.findIndex((item) => item.championId === championId);
    const item = index >= 0 ? ranking[index]! : this.scorePick(championId, seat, enemyLanes, openLaneBlocked(state, seat, enemyLanes));
    return { item, ranking, rank: index + 1, lane: seat.lane };
  }

  private prepare(
    state: DraftState,
    target: RecommendationTarget,
    enemyLanes: LanePredictionResult,
    allyLanes: LanePredictionResult | null,
  ): Seat {
    const lane = resolveLane(target, enemyLanes, allyLanes);
    const others = state.allies.filter((slot) => slot.cellId !== target.slot.cellId && effectiveChampionId(slot) !== 0);
    const allyChampions = others.map(effectiveChampionId);
    const allySeats = others.map((slot) => ({
      championId: effectiveChampionId(slot),
      lane: slot.assignedLane !== Lane.Unknown ? slot.assignedLane : (allyLanes?.forCell(slot.cellId)?.lane ?? Lane.Unknown),
    }));
    const enemyChampions = state.enemies.map(effectiveChampionId).filter((id) => id !== 0);
    // An ally's hover is as good as taken; for bans the advised seat's own hover is excluded too.
    const hoveredByOthers = new Set(
      state.allies
        .filter((slot) => !isLocked(slot) && slot.hoverChampionId !== 0)
        .filter((slot) => target.action === 'ban' || slot.cellId !== target.slot.cellId)
        .map((slot) => slot.hoverChampionId),
    );
    // The advised seat's own ban hover stays in its list, or the list would jump while it hovers.
    const bannedByOthers = new Set(
      state.allyBanIntents.filter((intent) => intent.cellId !== target.slot.cellId).map((intent) => intent.championId),
    );
    const openLanes = new Set<Lane>();
    for (const slot of state.allies) {
      if (isLocked(slot) && slot.cellId !== target.slot.cellId) continue;
      const seatLane = slot.assignedLane !== Lane.Unknown ? slot.assignedLane : (allyLanes?.forCell(slot.cellId)?.lane ?? Lane.Unknown);
      if (seatLane !== Lane.Unknown) openLanes.add(seatLane);
    }
    if (lane !== Lane.Unknown) openLanes.add(lane);
    return {
      lane,
      allies: allyChampions,
      allySeats,
      hoveredByOthers,
      bannedByOthers,
      openLanes: ALL_LANES.filter((l) => openLanes.has(l)),
      allyComp: this.comp.analyze(allyChampions),
      enemyComp: this.comp.analyze(enemyChampions),
    };
  }

  private scorePicks(
    state: DraftState,
    seat: Seat,
    enemyLanes: LanePredictionResult,
    selectable: ReadonlySet<number> | null,
  ): Recommendation[] {
    const results: Recommendation[] = [];
    const blocked = openLaneBlocked(state, seat, enemyLanes);
    for (const candidate of this.candidates(seat.lane)) {
      if (state.unavailable.has(candidate) || seat.hoveredByOthers.has(candidate)) continue;
      if (selectable && !selectable.has(candidate)) continue;
      results.push(this.scorePick(candidate, seat, enemyLanes, blocked));
    }
    return results;
  }

  private scorePick(
    candidate: number,
    seat: Seat,
    enemyLanes: LanePredictionResult,
    openLaneBlocked: ReadonlySet<number> | null,
  ): Recommendation {
    const lane = seat.lane;
    const reasons: Reason[] = [];
    const budget = new ErrorBudget();

    // Without a lane the list would degenerate into an alphabet; the main lane is the substitute.
    const baseTerm =
      lane === Lane.Unknown
        ? this.mainLaneLogOdds(candidate, lane, reasons, budget)
        : this.laneLogOdds(candidate, lane, reasons, budget);

    const own = this.meta.isOwnData;
    const compFit = this.comp.fit(candidate, seat.allyComp, seat.enemyComp, reasons, lane, own);

    const counters = openLaneBlocked ? this.addCounterRisk(candidate, lane, openLaneBlocked, reasons) : undefined;
    const flexLanes = this.flexLanes(candidate, seat.openLanes, reasons);

    const terms: ScoreTerm[] = own
      ? [
          { kind: 'laneStrength', logOdds: baseTerm === null ? null : STRENGTH_WEIGHT * baseTerm },
          { kind: 'laneMatchup', logOdds: this.laneMatchupLogOdds(candidate, lane, enemyLanes, reasons, budget) },
          { kind: 'enemyTeam', logOdds: this.versusLogOdds(candidate, lane, enemyLanes, reasons, budget) },
          { kind: 'synergy', logOdds: this.botDuoLogOdds(candidate, lane, seat.allySeats, reasons, budget) },
          { kind: 'composition', logOdds: this.measuredComp(candidate, seat, compFit) },
        ]
      : [
          { kind: 'laneStrength', logOdds: baseTerm },
          { kind: 'laneMatchup', logOdds: this.laneMatchupLogOdds(candidate, lane, enemyLanes, reasons, budget) },
          { kind: 'enemyTeam', logOdds: this.offLaneMatchupLogOdds(candidate, lane, enemyLanes, reasons, budget) },
          { kind: 'synergy', logOdds: this.synergyLogOdds(candidate, seat.allies, reasons, budget) },
          { kind: 'composition', logOdds: compFit === null ? null : COMP_SCALE * compFit },
        ];

    let total = 0;
    for (const term of terms) total += term.logOdds ?? 0;

    return {
      championId: candidate,
      name: this.meta.championName(candidate),
      score: sigmoid(total),
      reasons: dedupe(reasons),
      breakdown: terms,
      uncertainty: budget.standardError,
      ...(counters !== undefined ? { openCounters: counters } : {}),
      ...(flexLanes ? { flexLanes } : {}),
    };
  }

  /**
   * D2: strong on two or more lanes the own team still has open — the enemy sees the champion, not
   * its lane. Strong: a shrunk lane win rate of 50 % or more, on a lane that holds a tenth of its
   * games. A hint only, it adds nothing to the score.
   */
  private flexLanes(candidate: number, openLanes: readonly Lane[], reasons: Reason[]): Lane[] | undefined {
    if (openLanes.length < 2) return undefined;
    const strong = openLanes.filter((l) => {
      const stat = this.meta.laneStat(candidate, l);
      return !!stat && stat.winRate >= FLEX_WIN_RATE && stat.roleRate >= FLEX_ROLE_RATE;
    });
    if (strong.length < 2) return undefined;
    const names = strong.map((l) => FLEX_NAMES[l]);
    const list = `${names.slice(0, -1).join(', ')} oder ${names[names.length - 1]}`;
    reasons.push(
      neutral(
        `Flex: ${list}`,
        `Stark auf ${names.slice(0, -1).join(', ')} und ${names[names.length - 1]} (Lane-Winrate ab 50 %, dort oft gespielt), ` +
          'und diese Lanes sind in eurem Team noch offen. Der Gegner sieht den Champion, aber nicht, wo er spielt — ' +
          'gezielt kontern kann er erst später. Zählt nicht in die Prozentzahl.',
      ),
    );
    return strong;
  }

  private scoreBans(
    state: DraftState,
    lane: Lane,
    allyChampions: number[],
    hoveredByOthers: ReadonlySet<number>,
    enemyLanes: LanePredictionResult,
    limit: number,
    plan: Plan | null = null,
  ): Recommendation[] {
    const results: Recommendation[] = [];
    const takenByEnemy = enemyLanesTaken(state, enemyLanes);
    for (const candidate of this.candidates(Lane.Unknown)) {
      if (state.unavailable.has(candidate) || hoveredByOthers.has(candidate)) continue;
      const reasons: Reason[] = [];
      const banLane = this.banLane(candidate, takenByEnemy);
      const terms: ScoreTerm[] = [
        { kind: 'banStrength', logOdds: this.banStrengthLogOdds(banLane, lane, reasons) },
        { kind: 'banLaneThreat', logOdds: this.laneThreatLogOdds(candidate, lane, reasons) },
        { kind: 'banTeamThreat', logOdds: this.threatToAlliesLogOdds(candidate, allyChampions, reasons) },
        { kind: 'banPopularity', logOdds: banPopularity(banLane, reasons) },
      ];
      let threat = 0;
      for (const term of terms) if (!isGate(term)) threat += term.logOdds ?? 0;
      const last = terms[terms.length - 1]!;
      const gate = termHasData(last) ? termGate(last) : 0;
      const deniedPoints = gate * (sigmoid(threat) - 0.5) * 100;
      this.addPlanThreat(candidate, plan, reasons);
      results.push({
        championId: candidate,
        name: this.meta.championName(candidate),
        score: deniedPoints,
        reasons: dedupe(reasons),
        breakdown: terms,
        uncertainty: 0,
        ...(banLane ? { banLane: banLane.lane } : {}),
      });
    }
    return top(results, limit);
  }

  /**
   * D3: a champion that breaks the plan the own team is built around — disengage against a
   * teamfight plan, hard engage against a poke plan. A hint only, it adds nothing to the ban value.
   */
  private addPlanThreat(candidate: number, plan: Plan | null, reasons: Reason[]): void {
    if (plan !== 'teamfight' && plan !== 'poke') return;
    const trait = this.traits.for(this.meta.champion(candidate)?.key);
    if (plan === 'teamfight' && hasPeel(trait)) {
      reasons.push(
        neutral(
          'bedroht euren Teamfight-Plan',
          'Mit viel Peel holt er Getroffene aus eurem Engage wieder heraus (Disengage) — euer Teamfight-Plan läuft dann ins Leere. Zählt nicht in den Bann-Wert.',
        ),
      );
    } else if (plan === 'poke' && hasHardEngage(trait)) {
      reasons.push(
        neutral(
          'bedroht euren Poke-Plan',
          'Mit hartem Engage startet er den Kampf, bevor euer Poke wirkt. Zählt nicht in den Bann-Wert.',
        ),
      );
    }
  }

  /** Names the open counters as a reason; returns how many there are. */
  private addCounterRisk(candidate: number, lane: Lane, blocked: ReadonlySet<number>, reasons: Reason[]): number {
    const threats = openCounters(this.meta, candidate, lane, blocked, 0);
    if (threats.length === 0) return 0;
    const named = threats
      .slice(0, 3)
      .map(
        (threat) =>
          `${this.meta.championName(threat.championId)} ${netPercent(finiteOr(threat.measured, threat.winRate), 0)} aus ${netN0(threat.play)} Games`,
      )
      .join(', ');
    const more = threats.length > 3 ? ` und ${threats.length - 3} weitere` : '';
    reasons.push(
      contra(
        threats.length === 1 ? '1 offener Counter' : `${threats.length} offene Counter`,
        `Auf ${laneDisplay(lane)} ist noch niemand aufgedeckt, und diese Champs sind noch frei ` +
          `und schneiden gegen ${this.meta.championName(candidate)} besser ab als seine Gegner ` +
          `üblicherweise: ${named}${more}. Sie zu nehmen steht dem Gegner frei — ob er es tut, ` +
          'sagen die Daten nicht, deshalb zählt das hier nicht in die Prozentzahl hinein.' +
          (this.meta.isOwnData ? '' : ' Aufgelistet ist, was OP.GG als auffällige Gegner kennt, nicht jeder mögliche.'),
      ),
    );
    return threats.length;
  }

  private candidates(lane: Lane): readonly number[] {
    if (lane !== Lane.Unknown) {
      const roster = this.meta.roster(lane);
      if (roster.length > 0) return roster;
    }
    return this.meta.champions.map((c) => c.id);
  }

  private laneLogOdds(championId: number, lane: Lane, reasons: Reason[], budget: ErrorBudget): number | null {
    const stat = this.meta.laneStat(championId, lane);
    if (!stat) return null;
    const logOdds = logit(stat.winRate);
    budget.add(logitVariance(stat.winRate, stat.play, this.meta.lanePrior, this.meta.laneTarget) + this.laneDrift());

    if (winRateDelta(stat) > 0.015) {
      reasons.push(
        pro(
          `${netPercent(stat.winRate, 1)} WR auf ${laneDisplay(lane)}`,
          `Aus ${netN0(stat.play)} Games im aktuellen Patch. 50 % wäre Durchschnitt.`,
        ),
      );
    }

    if (stat.tier >= 0 && stat.tier <= 2) {
      reasons.push(
        neutral(
          `${tierName(stat.tier)} auf ${laneDisplay(lane)}`,
          `OP.GGs eigene Einstufung für ${laneDisplay(lane)}, von OP (stärkste) über S bis D ` +
            '(schwächste). Zählt nicht in die Prozentzahl: sie mischt Winrate und ' +
            'Pickrate, und die Winrate steckt schon drin.',
        ),
      );
    }
    return logOdds;
  }

  private laneDrift(): number {
    return Math.max(0, this.meta.patchesBehind) * LANE_DRIFT_PER_PATCH;
  }

  private banStrengthLogOdds(banLane: BanLane | null, seatLane: Lane, reasons: Reason[]): number | null {
    if (!banLane) return null;
    const logOdds = logit(banLane.stat.winRate);
    if (banLane.lane !== seatLane && logOdds > 0.08) {
      reasons.push(
        pro(
          `stark auf ${laneDisplay(banLane.lane)}`,
          `${laneDisplay(banLane.lane)} ist die Lane, auf der dieser Champ gerade am ` +
            'gefährlichsten ist und auf der ihn der Gegner am ehesten spielt: ' +
            `${netPercent(banLane.stat.winRate, 1)} Winrate aus ${netN0(banLane.stat.play)} Games.`,
        ),
      );
    }
    return logOdds;
  }

  private banLane(championId: number, takenByEnemy: ReadonlyMap<Lane, number>): BanLane | null {
    let best: BanLane | null = null;
    let bestPlausibility = -1;
    for (const lane of ALL_LANES) {
      const stat = this.meta.laneStat(championId, lane);
      if (!stat) continue;
      const taken = takenByEnemy.get(lane);
      const room = taken !== undefined ? 1 - taken : 1;
      const plausibility = Math.max(0, stat.roleRate) * room;
      if (plausibility <= bestPlausibility) continue;
      const gate = clamp(stat.pickRate * 8 + stat.banRate * 4, 0, 1) * room;
      bestPlausibility = plausibility;
      best = { lane, stat, gate };
    }
    return best;
  }

  private mainLaneLogOdds(championId: number, seatLane: Lane, reasons: Reason[], budget: ErrorBudget): number | null {
    const mainLane = this.meta.mainLane(championId);
    const main = mainLane === Lane.Unknown ? null : this.meta.laneStat(championId, mainLane);
    if (!main) return null;
    budget.add(logitVariance(main.winRate, main.play, this.meta.lanePrior, this.meta.laneTarget) + this.laneDrift());
    const logOdds = logit(main.winRate);
    if (mainLane !== seatLane && logOdds > 0.08) {
      reasons.push(
        pro(
          `stark auf ${laneDisplay(mainLane)}`,
          `${laneDisplay(mainLane)} ist die Lane, auf der dieser Champ meistens gespielt wird: ` +
            `${netPercent(main.winRate, 1)} Winrate aus ${netN0(main.play)} Games.`,
        ),
      );
    }
    return logOdds;
  }

  private laneMatchupLogOdds(
    championId: number,
    lane: Lane,
    enemyLanes: LanePredictionResult,
    reasons: Reason[],
    budget: ErrorBudget,
  ): number | null {
    if (lane === Lane.Unknown) return null;

    let total = 0;
    let variance = 0;
    let counted = 0;
    let bestProbability = 0;
    let bestMatchup: MatchupView | null = null;
    let bestOpponent = 0;

    for (const prediction of enemyLanes.predictions) {
      if (prediction.championId === 0) continue;
      const probability = probabilityOnLane(prediction, lane);
      if (probability < 0.05) continue;
      const matchup = this.meta.matchup(championId, prediction.championId, lane);
      if (!matchup) continue;

      counted++;
      const baseline = this.meta.matchupBaseline(championId, prediction.championId, lane);
      variance +=
        probability *
        probability *
        (logitVariance(matchup.winRate, matchup.play, this.meta.matchupPrior(lane), sigmoid(baseline)) +
          this.opponentLaneVariance(prediction.championId, lane));
      total += probability * (logit(matchup.winRate) - baseline);

      if (probability > bestProbability) {
        bestProbability = probability;
        bestMatchup = matchup;
        bestOpponent = prediction.championId;
      }
    }

    if (bestMatchup && bestMatchup.confidence > 0.2 && Math.abs(winRateDelta(bestMatchup)) > 0.01) {
      const opponent = this.meta.championName(bestOpponent);
      const uncertain = bestProbability < 0.6;
      reasons.push({
        text: `${netPercent(bestMatchup.winRate, 0)} WR gegen ${opponent}${uncertain ? ' (falls Lane-Gegner)' : ''}`,
        tone: winRateDelta(bestMatchup) > 0 ? 'pro' : 'contra',
        hint: describeMatchup(bestMatchup, opponent, lane, bestProbability),
      });
    }

    if (counted === 0) return null;
    budget.add(variance);
    return total;
  }

  private opponentLaneVariance(opponentId: number, lane: Lane): number {
    const stat = this.meta.laneStat(opponentId, lane);
    return stat ? logitVariance(stat.winRate, stat.play, this.meta.lanePrior, this.meta.laneTarget) : 0;
  }

  private offLaneMatchupLogOdds(
    championId: number,
    lane: Lane,
    enemyLanes: LanePredictionResult,
    reasons: Reason[],
    budget: ErrorBudget,
  ): number | null {
    let total = 0;
    let weightSum = 0;
    let variance = 0;
    let counted = 0;
    let favourable = 0;

    for (const prediction of enemyLanes.predictions) {
      if (prediction.championId === 0) continue;
      const offLane = 1 - probabilityOnLane(prediction, lane);
      if (offLane < 0.05) continue;
      const found = this.findAnyLaneMatchup(championId, prediction.championId);
      if (!found) continue;

      const view = found.view;
      const baseline = this.meta.matchupBaseline(championId, prediction.championId, found.lane);
      counted++;
      weightSum += offLane;
      total += offLane * (logit(view.winRate) - baseline);
      variance +=
        offLane *
        offLane *
        (logitVariance(view.winRate, view.play, this.meta.matchupPrior(found.lane), sigmoid(baseline)) +
          this.opponentLaneVariance(prediction.championId, found.lane));
      if (winRateDelta(view) > 0.01) favourable++;
    }

    if (counted === 0) return null;

    const meanLogOdds = OFF_LANE_SHARE * (total / weightSum);
    const scale = OFF_LANE_SHARE / weightSum;
    budget.add(scale * scale * variance);

    if (favourable >= 2) {
      reasons.push(
        pro(
          `über 50 % WR gegen ${favourable} von ${counted} weiteren Gegnern`,
          'Gegner außerhalb der eigenen Lane: gegen so viele von ihnen hat dieser Champ ' +
            'eine Winrate über 50 %. Zählt weniger als das direkte Lane-Matchup.',
        ),
      );
    }
    return meanLogOdds;
  }

  /**
   * Own data: the enemies on other lanes, from the pairs counted across lanes (meta.versus) instead
   * of duels borrowed from whatever lane the two ever met on. Each pair by its measured weight — the
   * botlane cross most (draftModel VERSUS_WEIGHT) — and by how likely the enemy is on that lane.
   */
  private versusLogOdds(championId: number, lane: Lane, enemyLanes: LanePredictionResult, reasons: Reason[], budget: ErrorBudget): number | null {
    if (lane === Lane.Unknown) return null;
    let total = 0;
    let variance = 0;
    let counted = 0;
    let best: { edge: number; opponent: number; lane: Lane; play: number; measured: number; expected: number } | null = null;

    for (const prediction of enemyLanes.predictions) {
      if (prediction.championId === 0 || prediction.championId === championId) continue;
      for (const theirs of ALL_LANES) {
        if (theirs === lane) continue;
        const probability = probabilityOnLane(prediction, theirs);
        if (probability < 0.05) continue;
        const weight = VERSUS_WEIGHT[versusKind(lane, theirs)];
        const view = weight > 0 ? this.meta.versus(championId, lane, prediction.championId, theirs) : null;
        if (!view) continue;
        const edge = logit(view.winRate) - logit(view.expected);
        counted++;
        total += probability * weight * edge;
        variance += (probability * weight) ** 2 * logitVariance(view.winRate, view.play, SYNERGY_PRIOR, view.expected);
        if (probability >= 0.6 && (!best || Math.abs(edge) > Math.abs(best.edge)))
          best = { edge, opponent: prediction.championId, lane: theirs, play: view.play, measured: view.measured, expected: view.expected };
      }
    }

    if (counted === 0) return null;
    budget.add(variance);
    if (best && Math.abs(best.edge) > 0.04) {
      const opponent = this.meta.championName(best.opponent);
      const text = best.edge > 0 ? `gut gegen ${opponent} (${laneDisplay(best.lane)})` : `schwer gegen ${opponent} (${laneDisplay(best.lane)})`;
      const hint =
        `Auf ${laneDisplay(lane)} gegen ${opponent} auf ${laneDisplay(best.lane)}: ${netPercent(finiteOr(best.measured, 0.5), 1)} Winrate aus ${netN0(best.play)} Games — ` +
        `aus der Stärke beider allein wären ${netPercent(best.expected, 1)} zu erwarten. Gezählt wird nur der Unterschied, geglättet und mit dem Gewicht, ` +
        'das solche Paare in unseren Spielen haben: das Botlane-Kreuz (ADC gegen Support) gut zur Hälfte, andere Paare zu einem Fünftel.';
      reasons.push(best.edge > 0 ? pro(text, hint) : contra(text, hint));
    }
    return total;
  }

  /**
   * Own data: of all duos only the botlane's shows in the games (draftModel BOT_DUO_WEIGHT); the
   * others cannot be told from nothing and count nothing.
   */
  private botDuoLogOdds(championId: number, lane: Lane, allySeats: readonly DraftSeat[], reasons: Reason[], budget: ErrorBudget): number | null {
    const partner = allySeats.find((ally) => isBotDuo(lane, ally.lane));
    const view = partner ? this.meta.synergy(championId, partner.championId) : null;
    if (!partner || !view) return null;
    const edge = logit(view.winRate) - logit(view.expected);
    budget.add(BOT_DUO_WEIGHT ** 2 * logitVariance(view.winRate, view.play, SYNERGY_PRIOR, view.expected));
    if (Math.abs(edge) > 0.04) {
      const name = this.meta.championName(partner.championId);
      const hint =
        `Zusammen mit ${name} auf der Botlane liegt die Winrate bei ${netPercent(finiteOr(view.measured, view.winRate), 1)} aus ${netN0(view.play)} Games ` +
        `— erwartet hätte man aus der Stärke der beiden allein ${netPercent(view.expected, 1)}. Gezählt wird nur der Unterschied, geglättet. ` +
        'Das Botlane-Duo ist das einzige Paar im Team, bei dem unsere Spiele eine Wirkung über die Stärke der beiden hinaus zeigen.';
      reasons.push(edge > 0 ? pro(`starkes Botlane-Duo mit ${name}`, hint) : contra(`schwaches Botlane-Duo mit ${name}`, hint));
    }
    return BOT_DUO_WEIGHT * edge;
  }

  /**
   * Own data: the line-up findings with a measured cost (draftModel COMP_WEIGHT) — what the team is
   * worth with the candidate against without it, so a pick that closes a gap gains what the gap
   * costs and one that keeps it open loses a little more of it. On top, under the old cap, the
   * rules the games are too few to measure (`unmeasured`, from CompAnalyzer.fit).
   */
  private measuredComp(candidate: number, seat: Seat, unmeasured: number | null): number | null {
    const before = seat.allyComp;
    const after = this.comp.analyze([...seat.allies, candidate]);
    const measured = compValue(after) - compValue(before);
    if (unmeasured === null && measured === 0) return null;
    return measured + COMP_SCALE * (unmeasured ?? 0);
  }

  private findAnyLaneMatchup(championId: number, opponentId: number): { lane: Lane; view: MatchupView } | null {
    let best: { lane: Lane; view: MatchupView } | null = null;
    for (const lane of ALL_LANES) {
      const view = this.meta.matchup(championId, opponentId, lane);
      if (!view) continue;
      if (!best || view.confidence > best.view.confidence) best = { lane, view };
    }
    return best;
  }

  private synergyLogOdds(championId: number, allyChampions: number[], reasons: Reason[], budget: ErrorBudget): number | null {
    let total = 0;
    let variance = 0;
    let counted = 0;
    let bestPartner: string | null = null;
    let bestLogOdds = 0;
    let bestView: SynergyView | null = null;

    for (const ally of allyChampions) {
      const synergy = this.meta.synergy(championId, ally);
      if (!synergy) continue;
      // Against what this pair was expected to win from both lane strengths: only the part the
      // pair alone explains.
      const logOdds = logit(synergy.winRate) - logit(synergy.expected);
      total += logOdds;
      variance += logitVariance(synergy.winRate, synergy.play, SYNERGY_PRIOR, synergy.expected);
      counted++;
      if (logOdds > bestLogOdds) {
        bestLogOdds = logOdds;
        bestPartner = this.meta.championName(ally);
        bestView = synergy;
      }
    }

    if (counted === 0) return null;

    if (bestPartner !== null && bestView && bestLogOdds > 0.08) {
      reasons.push(
        pro(
          `spielt gut mit ${bestPartner}`,
          `Zusammen mit ${bestPartner} im selben Team liegt die Winrate bei ` +
            `${netPercent(finiteOr(bestView.measured, bestView.winRate), 1)} aus ${netN0(bestView.play)} Games ` +
            `— erwartet hätte man aus der Stärke der beiden allein ${netPercent(bestView.expected, 1)}. Gezählt ` +
            `wird nur der Unterschied, und geglättet: ${netPercent(bestView.winRate, 1)} statt des gemessenen Werts.`,
        ),
      );
    }

    const scale = SYNERGY_DAMPING / counted;
    budget.add(scale * scale * variance);
    return SYNERGY_DAMPING * (total / counted);
  }

  private laneThreatLogOdds(championId: number, lane: Lane, reasons: Reason[]): number | null {
    const stat = lane === Lane.Unknown ? null : this.meta.laneStat(championId, lane);
    if (!stat) return null;
    const logOdds = BAN_LANE_FOCUS * logit(stat.winRate);
    if (logOdds > 0.05) {
      reasons.push(
        pro(
          `${netPercent(stat.winRate, 1)} WR auf ${laneDisplay(lane)}`,
          `Genau die Lane dieses Slots: ${netPercent(stat.winRate, 1)} Winrate aus ` +
            `${netN0(stat.play)} Games. Ein Ban wirkt hier direkt.`,
        ),
      );
    }
    return logOdds;
  }

  private threatToAlliesLogOdds(championId: number, allyChampions: number[], reasons: Reason[]): number | null {
    let total = 0;
    let counted = 0;
    let worst: string | null = null;
    let worstLogOdds = 0;
    let worstView: MatchupView | null = null;

    for (const ally of allyChampions) {
      const found = this.findAnyLaneMatchup(championId, ally);
      if (!found) continue;
      counted++;
      const logOdds = logit(found.view.winRate) - this.meta.matchupBaseline(championId, ally, found.lane);
      total += logOdds;
      if (logOdds > worstLogOdds) {
        worstLogOdds = logOdds;
        worst = this.meta.championName(ally);
        worstView = found.view;
      }
    }

    if (counted === 0) return null;

    if (worst !== null && worstView && worstLogOdds > 0.1) {
      reasons.push(
        pro(
          `schlägt unseren ${worst}`,
          `Im direkten Matchup gewinnt dieser Champ ${netPercent(finiteOr(worstView.measured, worstView.winRate), 1)} ` +
            `gegen ${worst}, der bei uns schon gepickt ist. Aus ${netN0(worstView.play)} Games; ` +
            `gezählt wird ${netPercent(worstView.winRate, 1)}, weil ein einzelnes Duell über so wenige Games ` +
            'zum großen Teil Zufall ist.',
        ),
      );
    }
    return BAN_ALLY_SHARE * (total / counted);
  }
}

function resolveLane(target: RecommendationTarget, enemyLanes: LanePredictionResult, allyLanes: LanePredictionResult | null): Lane {
  if (target.slot.assignedLane !== Lane.Unknown) return target.slot.assignedLane;
  const predictions = target.slot.isAlly ? allyLanes : enemyLanes;
  return predictions?.forCell(target.slot.cellId)?.lane ?? Lane.Unknown;
}

function openLaneBlocked(state: DraftState, seat: Seat, enemyLanes: LanePredictionResult): ReadonlySet<number> | null {
  return seat.lane !== Lane.Unknown && enemyLanes.championOnLane(seat.lane) === 0
    ? new Set([...state.unavailable, ...seat.hoveredByOthers])
    : null;
}

function banPopularity(banLane: BanLane | null, reasons: Reason[]): number | null {
  if (!banLane) return null;
  if (banLane.gate > 0.6) {
    reasons.push(
      banLane.stat.banRate > banLane.stat.pickRate
        ? neutral(
            `in ${netPercent(banLane.stat.banRate, 0)} der Games gebannt`,
            'So oft bannen andere diesen Champ. Ein hoher Wert heißt: viele halten ihn ' +
              'für gefährlich — aber vielleicht bannt ihn ohnehin jemand anders.',
          )
        : neutral(
            `in ${netPercent(banLane.stat.pickRate, 0)} der Games gepickt`,
            'So oft wird dieser Champ gespielt. Je häufiger, desto wahrscheinlicher ' +
              'nimmt ihn der Gegner, wenn du ihn nicht bannst.',
          ),
    );
  }
  return banLane.gate;
}

function enemyLanesTaken(state: DraftState, enemyLanes: LanePredictionResult): Map<Lane, number> {
  const taken = new Map<Lane, number>();
  for (const slot of state.enemies) {
    if (!isLocked(slot)) continue;
    const prediction = enemyLanes.forCell(slot.cellId);
    if (!prediction || prediction.lane === Lane.Unknown) continue;
    const confidence = clamp(prediction.confidence, 0, 1);
    const known = taken.get(prediction.lane);
    if (known === undefined || confidence > known) taken.set(prediction.lane, confidence);
  }
  return taken;
}

function describeMatchup(view: MatchupView, opponent: string, lane: Lane, probability: number): string {
  let text =
    Number.isFinite(view.measured) && view.play > 0
      ? `Gemessen auf ${laneDisplay(lane)}: ${netPercent(view.measured, 1)} gegen ${opponent} aus ${netN0(view.play)} Games. ` +
        `Gezählt wird ${netPercent(view.winRate, 1)} — ein einzelnes Duell über so wenige Games ist zum ` +
        'großen Teil Zufall, deshalb rückt die Zahl zu dem, was die Lane-Stärke beider ' +
        'Champs ohnehin erwarten lässt. 50 % wäre ausgeglichen.'
      : `Geschätzt ${netPercent(view.winRate, 1)} gegen ${opponent} auf ${laneDisplay(lane)}. 50 % wäre ausgeglichen.`;

  if (!view.fromCompleteList && view.play > 0) {
    text +=
      '\n\nStammt aus OP.GGs Liste der auffälligsten Gegner. Solche Werte sind eine ' +
      'Auswahl der Extreme und fallen bei neuer Messung erfahrungsgemäß deutlich zur Mitte ' +
      'zurück — auch das steckt in der gezählten Zahl.';
  }
  if (view.isLive) text += ' Gerade für diesen Draft von OP.GG geholt.';
  if (view.isInferred) text += ` Abgeleitet aus der Gegenrichtung — gemessen wurde ${opponent} gegen diesen Champ.`;
  if (probability < 0.6) {
    text +=
      `\n\n${opponent} steht nur mit ${netPercent(probability, 0)} Wahrscheinlichkeit auf dieser Lane, ` +
      'deshalb zählt das Matchup hier nur anteilig.';
  }
  return text;
}

function probabilityOnLane(prediction: LanePrediction, lane: Lane): number {
  return lane >= 0 && lane < prediction.probabilities.length ? prediction.probabilities[lane]! : 0;
}

function dedupe(reasons: Reason[]): Reason[] {
  const seen = new Set<string>();
  const result: Reason[] = [];
  for (const reason of reasons) {
    if (seen.has(reason.text)) continue;
    seen.add(reason.text);
    result.push(reason);
  }
  return result;
}

function ordered(results: Recommendation[]): Recommendation[] {
  // Stable: equal scores keep their name order between events.
  return [...results].sort((a, b) => compareDouble(b.score, a.score) || ordinal(a.name, b.name));
}

/** double.CompareTo: NaN sorts below every number and equal to itself. */
function compareDouble(x: number, y: number): number {
  if (Number.isNaN(x)) return Number.isNaN(y) ? 0 : -1;
  if (Number.isNaN(y)) return 1;
  return x < y ? -1 : x > y ? 1 : 0;
}

function top(results: Recommendation[], limit: number): Recommendation[] {
  return ordered(results).slice(0, Math.max(0, limit));
}
