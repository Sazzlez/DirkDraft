// Port of DraftPilot.Core.Data.MetaLookup (1.5): the snapshot as the model reads it — lane rows
// shrunk towards the file's lane baseline, duels shrunk towards what both lane strengths predict
// and mirrored after shrinking, duos shrunk towards the fitted synergy line.

import { ALL_LANES, Lane, LANE_COUNT } from './lane';
import { clamp, logit, sigmoid } from './scoreModel';
import { confidence, LANE_PRIOR, MATCHUP_PRIOR, OWN_LANE_PRIOR, OWN_MATCHUP_PRIOR, shrink, SYNERGY_PRIOR } from './shrinkage';
import { expectedLogit, fitSynergyLine, type SynergyLine } from './synergyLine';

export const DamageType = { Unknown: 0, Physical: 1, Magic: 2, Mixed: 3 } as const;
export type DamageType = (typeof DamageType)[keyof typeof DamageType];

/** Attack range from which Data Dragon's champion counts as ranged. */
export const RANGED_THRESHOLD = 300;

export interface ChampionEntry {
  id: number;
  key: string;
  name: string;
  damage: DamageType;
  /**
   * Damage to champions per game, on average, physical and magic (own data, 2.1). Absent in OP.GG
   * files and older own files; the comp then counts heads.
   */
  physicalDamage?: number | null;
  magicDamage?: number | null;
  tags: string[];
  attackRange: number;
  defense: number;
}

export const isRanged = (c: ChampionEntry): boolean => c.attackRange >= RANGED_THRESHOLD;
export const hasStaticData = (c: ChampionEntry): boolean => c.attackRange > 0;
export const isTagged = (c: ChampionEntry, tag: string): boolean =>
  c.tags.some((t) => t.toLowerCase() === tag.toLowerCase());

export interface LaneStat {
  championId: number;
  lane: Lane;
  winRate: number;
  pickRate: number;
  banRate: number;
  roleRate: number;
  tier: number;
  play: number;
}

export interface MatchupStat {
  championId: number;
  opponentId: number;
  lane: Lane;
  winRate: number;
  play: number;
  fromCompleteList?: boolean;
}

export interface SynergyStat {
  championId: number;
  lane: Lane;
  partnerId: number;
  partnerLane: Lane;
  winRate: number;
  play: number;
  tier?: number;
}

/**
 * Two champions on opposite teams and different lanes (own data, counted since 07.11.2026): one row
 * per pair, the champion with the lower id first, its win rate against the other.
 */
export interface VersusStat {
  championId: number;
  lane: Lane;
  opponentId: number;
  opponentLane: Lane;
  winRate: number;
  play: number;
}

export interface MetaSnapshot {
  version: number;
  patch: string;
  dataPatch: string;
  dataAsOfUtc?: string | null;
  builtAtUtc: string;
  gameMode: string;
  tier?: string | null;
  /** 'riot' for DirkDraft's own count (complete duels, no tiers); absent for an OP.GG file. */
  source?: string | null;
  platform?: string | null;
  /** Games the file was counted from; own data only. */
  games?: number;
  synergyBaseline: number;
  laneBaseline: number;
  matchupBaseline: number;
  completeDuelSlope: number;
  completeDuelOffset: number;
  completeDuelSample?: number;
  completeDuelSlopeError?: number;
  champions: ChampionEntry[];
  laneStats: LaneStat[];
  matchups: MatchupStat[];
  synergies: SynergyStat[];
  /** Own data only; absent in OP.GG files and in own files from before 07.11.2026. */
  versus?: VersusStat[];
  warnings: string[];
}

export const emptySnapshot = (): MetaSnapshot => ({
  version: 1,
  patch: '',
  dataPatch: '',
  dataAsOfUtc: null,
  builtAtUtc: '0001-01-01T00:00:00+00:00',
  gameMode: 'ranked',
  tier: null,
  synergyBaseline: 0,
  laneBaseline: 0,
  matchupBaseline: 0,
  completeDuelSlope: 1,
  completeDuelOffset: 0,
  champions: [],
  laneStats: [],
  matchups: [],
  synergies: [],
  warnings: [],
});

export interface LaneView {
  winRate: number;
  pickRate: number;
  banRate: number;
  roleRate: number;
  /** -1 for a fallback row without games. */
  tier: number;
  play: number;
}

export interface MatchupView {
  winRate: number;
  play: number;
  confidence: number;
  isInferred: boolean;
  isLive: boolean;
  fromCompleteList: boolean;
  /** The raw rate before shrinking, NaN when unknown. */
  measured: number;
}

export interface SynergyView {
  winRate: number;
  play: number;
  confidence: number;
  tier: number;
  expected: number;
  measured: number;
}

/** A pair across the teams on different lanes, shrunk towards what both lane strengths predict. */
export interface VersusView {
  winRate: number;
  play: number;
  /** What the two lane strengths alone predict for the first champion. */
  expected: number;
  measured: number;
}

const rate = (value: number): number => (Number.isFinite(value) ? clamp(value, 0, 1) : 0);

/** Math.Clamp semantics for the measured rate: NaN stays NaN. */
const clampRate = (value: number): number => (Number.isNaN(value) ? Number.NaN : clamp(value, 0, 1));

export class MetaLookup {
  readonly snapshot: MetaSnapshot;
  readonly champions: readonly ChampionEntry[];
  readonly synergyLine: SynergyLine;

  /** How many patches the data trails the client; set by whoever loaded it. */
  patchesBehind = 0;

  /** The lane rows' prior weight: measured per source (shrinkage.ts). */
  readonly lanePrior: number;

  private readonly indexById = new Map<number, number>();
  private readonly laneStats: (LaneView | null)[];
  private readonly rolePriors: Float64Array;
  private readonly laneRosters: number[][];
  private readonly matchups: Map<number, MatchupView>;
  private readonly synergies: Map<number, SynergyView>;
  private readonly versusPairs: Map<string, VersusView>;
  private readonly liveMatchups = new Map<number, MatchupView>();
  private readonly matchupPriors: readonly number[];

  /**
   * `matchupPrior`: one weight for the duels of every lane instead of the measured ones — for
   * measuring what the weights per lane are worth (tools/measure/model.ts).
   */
  constructor(snapshot: MetaSnapshot, options: { lanePrior?: number; matchupPrior?: number } = {}) {
    this.snapshot = snapshot;
    this.lanePrior = options.lanePrior ?? (snapshot.source === 'riot' ? OWN_LANE_PRIOR : LANE_PRIOR);
    this.matchupPriors =
      options.matchupPrior !== undefined
        ? ALL_LANES.map(() => options.matchupPrior!)
        : snapshot.source === 'riot'
          ? OWN_MATCHUP_PRIOR
          : ALL_LANES.map(() => MATCHUP_PRIOR);
    this.champions = [...snapshot.champions];
    this.champions.forEach((c, i) => this.indexById.set(c.id, i));

    const cells = LANE_COUNT * this.champions.length;
    this.laneStats = new Array<LaneView | null>(cells).fill(null);
    this.rolePriors = new Float64Array(cells);
    this.laneRosters = ALL_LANES.map(() => []);

    this.loadLaneStats(snapshot);
    this.normaliseRolePriors();
    this.matchups = this.buildMatchups(snapshot);

    // After the lane rows, because the line is fitted on them; before the duo views, because each
    // view is shrunk towards it.
    this.synergyLine = fitSynergyLine(
      snapshot.synergies,
      (championId, lane) => this.laneStat(championId, lane)?.winRate ?? null,
      snapshot.synergyBaseline,
    );
    this.synergies = this.buildSynergies(snapshot);
    this.versusPairs = this.buildVersus(snapshot);
  }

  static empty(): MetaLookup {
    return new MetaLookup(emptySnapshot());
  }

  get patch(): string {
    return this.snapshot.patch;
  }

  get dataPatch(): string {
    return this.snapshot.dataPatch;
  }

  get tier(): string {
    return this.snapshot.tier ?? '';
  }

  get gameMode(): string {
    return this.snapshot.gameMode ?? '';
  }

  /** DirkDraft's own count of ranked games rather than an OP.GG file; the texts are worded by it. */
  get isOwnData(): boolean {
    return this.snapshot.source === 'riot';
  }

  get warnings(): readonly string[] {
    return this.snapshot.warnings;
  }

  /** Pseudo games a duel on this lane is shrunk with: measured per lane for own data (shrinkage.ts). */
  matchupPrior(lane: Lane): number {
    return this.matchupPriors[lane] ?? MATCHUP_PRIOR;
  }

  get isEmpty(): boolean {
    return this.champions.length === 0;
  }

  get synergyBaseline(): number {
    return this.snapshot.synergyBaseline;
  }

  get laneTarget(): number {
    return sigmoid(this.snapshot.laneBaseline);
  }

  get synergyTarget(): number {
    return sigmoid(this.snapshot.synergyBaseline);
  }

  matchupTarget(championId: number, opponentId: number, lane: Lane): number {
    return sigmoid(this.matchupBaseline(championId, opponentId, lane));
  }

  matchupBaseline(championId: number, opponentId: number, lane: Lane): number {
    const view = this.matchup(championId, opponentId, lane);
    const listed = !(view?.fromCompleteList ?? false);
    return view?.isInferred
      ? -this.pairBaseline(opponentId, championId, lane, listed)
      : this.pairBaseline(championId, opponentId, lane, listed);
  }

  private pairBaseline(championId: number, opponentId: number, lane: Lane, listed: boolean): number {
    const s = this.snapshot;
    const lineValid =
      Number.isFinite(s.completeDuelSlope) &&
      s.completeDuelSlope > 0 &&
      s.completeDuelSlope <= 2 &&
      Number.isFinite(s.completeDuelOffset);
    const slope = listed || !lineValid ? 1 : s.completeDuelSlope;
    const offset = listed ? s.matchupBaseline : lineValid ? s.completeDuelOffset : 0;
    const difference = this.laneRateDifference(championId, opponentId, lane);
    return difference !== null ? slope * difference + offset : offset;
  }

  laneRateDifference(championId: number, opponentId: number, lane: Lane): number | null {
    const mine = this.laneStat(championId, lane)?.winRate ?? this.mainLaneWinRate(championId);
    const theirs = this.laneStat(opponentId, lane)?.winRate ?? this.mainLaneWinRate(opponentId);
    return mine === null || theirs === null ? null : logit(mine) - logit(theirs);
  }

  private mainLaneWinRate(championId: number): number | null {
    let result: number | null = null;
    let mostPlayed = 0;
    for (const lane of ALL_LANES) {
      const stat = this.laneStat(championId, lane);
      if (!stat || stat.play <= mostPlayed) continue;
      mostPlayed = stat.play;
      result = stat.winRate;
    }
    return result;
  }

  champion(championId: number): ChampionEntry | null {
    const index = this.indexById.get(championId);
    return index === undefined ? null : this.champions[index]!;
  }

  championName(championId: number): string {
    return this.champion(championId)?.name ?? `#${championId}`;
  }

  roster(lane: Lane): readonly number[] {
    return lane === Lane.Unknown ? [] : this.laneRosters[lane]!;
  }

  mainLane(championId: number): Lane {
    let best: Lane = Lane.Unknown;
    // -1, not 0: a champion whose only rows are fallbacks should still be placed somewhere.
    let mostPlayed = -1;
    for (const lane of ALL_LANES) {
      const stat = this.laneStat(championId, lane);
      if (!stat || stat.play <= mostPlayed) continue;
      mostPlayed = stat.play;
      best = lane;
    }
    return best;
  }

  laneStat(championId: number, lane: Lane): LaneView | null {
    if (lane === Lane.Unknown) return null;
    const index = this.indexById.get(championId);
    if (index === undefined) return null;
    return this.laneStats[lane * this.champions.length + index] ?? null;
  }

  rolePrior(championId: number, lane: Lane): number {
    const index = this.indexById.get(championId);
    if (lane === Lane.Unknown || index === undefined) return 1 / LANE_COUNT;
    const prior = this.rolePriors[lane * this.champions.length + index]!;
    return Number.isFinite(prior) && prior > 0 ? prior : 0;
  }

  matchup(championId: number, opponentId: number, lane: Lane): MatchupView | null {
    if (lane === Lane.Unknown) return null;
    const a = this.indexById.get(championId);
    const b = this.indexById.get(opponentId);
    if (a === undefined || b === undefined) return null;
    const key = matchupKey(lane, a, b);
    return this.liveMatchups.get(key) ?? this.matchups.get(key) ?? null;
  }

  get liveMatchupCount(): number {
    return this.liveMatchups.size;
  }

  applyLiveMatchups(stats: Iterable<MatchupStat>): void {
    for (const stat of stats) {
      if (stat.lane === Lane.Unknown || stat.play <= 0) continue;
      const a = this.indexById.get(stat.championId);
      const b = this.indexById.get(stat.opponentId);
      if (a === undefined || b === undefined) continue;
      const fromCompleteList = stat.fromCompleteList ?? false;

      const view: MatchupView = {
        winRate: this.shrinkMatchup(stat.championId, stat.opponentId, stat.lane, stat.winRate, stat.play, !fromCompleteList),
        play: stat.play,
        confidence: confidence(stat.play, this.matchupPrior(stat.lane)),
        isInferred: false,
        isLive: true,
        fromCompleteList,
        measured: clampRate(stat.winRate),
      };

      const key = matchupKey(stat.lane, a, b);
      const existing = this.liveMatchups.get(key);
      if (!existing || supersedes(view, existing)) this.liveMatchups.set(key, view);

      const mirrorView: MatchupView = {
        ...view,
        winRate: 1 - view.winRate,
        isInferred: true,
        measured: 1 - view.measured,
      };
      const mirror = matchupKey(stat.lane, b, a);
      const existingMirror = this.liveMatchups.get(mirror);
      if (!existingMirror || supersedes(mirrorView, existingMirror)) this.liveMatchups.set(mirror, mirrorView);
    }
  }

  clearLiveMatchups(): void {
    this.liveMatchups.clear();
  }

  synergy(championId: number, partnerId: number): SynergyView | null {
    const a = this.indexById.get(championId);
    const b = this.indexById.get(partnerId);
    if (a === undefined || b === undefined) return null;
    return this.synergies.get(synergyKey(a, b)) ?? null;
  }

  /**
   * The champion on its lane against an enemy on another lane (own data): how the pair has gone,
   * shrunk like a duo towards what both lane strengths predict. Null where the file has no row for
   * the pair — it counts them from 30 games on.
   */
  versus(championId: number, lane: Lane, opponentId: number, opponentLane: Lane): VersusView | null {
    if (lane === Lane.Unknown || opponentLane === Lane.Unknown || lane === opponentLane) return null;
    return this.versusPairs.get(versusKey(championId, lane, opponentId, opponentLane)) ?? null;
  }

  private buildVersus(snapshot: MetaSnapshot): Map<string, VersusView> {
    const map = new Map<string, VersusView>();
    for (const stat of snapshot.versus ?? []) {
      if (!(stat.play > 0) || stat.lane === Lane.Unknown || stat.opponentLane === Lane.Unknown || stat.lane === stat.opponentLane) continue;
      const mine = this.laneStat(stat.championId, stat.lane);
      const theirs = this.laneStat(stat.opponentId, stat.opponentLane);
      if (!mine || !theirs) continue;
      const expected = sigmoid(logit(mine.winRate) - logit(theirs.winRate));
      const winRate = shrink(stat.winRate, stat.play, SYNERGY_PRIOR, expected);
      const measured = clampRate(stat.winRate);
      map.set(versusKey(stat.championId, stat.lane, stat.opponentId, stat.opponentLane), { winRate, play: stat.play, expected, measured });
      // Mirrored after shrinking: the other side is the exact complement.
      map.set(versusKey(stat.opponentId, stat.opponentLane, stat.championId, stat.lane), { winRate: 1 - winRate, play: stat.play, expected: 1 - expected, measured: 1 - measured });
    }
    return map;
  }

  private loadLaneStats(snapshot: MetaSnapshot): void {
    const count = this.champions.length;
    for (const stat of snapshot.laneStats) {
      const index = this.indexById.get(stat.championId);
      if (stat.lane === Lane.Unknown || index === undefined) continue;
      const slot = stat.lane * count + index;

      this.laneStats[slot] = {
        winRate: shrink(stat.winRate, stat.play, this.lanePrior, this.laneTarget),
        pickRate: rate(stat.pickRate),
        banRate: rate(stat.banRate),
        roleRate: rate(stat.roleRate),
        // Tier 0 is OP.GG's OP tier in a listed row and "no tier" in a fallback row; the game count
        // tells the two apart.
        tier: stat.play > 0 ? stat.tier : -1,
        play: stat.play,
      };
      this.rolePriors[slot] = rate(stat.roleRate);

      const roster = this.laneRosters[stat.lane]!;
      if (!roster.includes(stat.championId)) roster.push(stat.championId);
    }
  }

  private normaliseRolePriors(): void {
    const count = this.champions.length;
    for (let index = 0; index < count; index++) {
      let total = 0;
      for (let lane = 0; lane < LANE_COUNT; lane++) total += this.rolePriors[lane * count + index]!;
      if (total <= 0) continue;
      for (let lane = 0; lane < LANE_COUNT; lane++) this.rolePriors[lane * count + index]! /= total;
    }
  }

  private buildMatchups(snapshot: MetaSnapshot): Map<number, MatchupView> {
    const map = new Map<number, MatchupView>();

    for (const stat of snapshot.matchups) {
      if (stat.lane === Lane.Unknown) continue;
      const a = this.indexById.get(stat.championId);
      const b = this.indexById.get(stat.opponentId);
      if (a === undefined || b === undefined) continue;

      // On duplicates the larger sample wins.
      const key = matchupKey(stat.lane, a, b);
      const existing = map.get(key);
      if (existing && existing.play >= stat.play) continue;

      // A complete duel (own data counts every pairing) is shrunk towards the complete-duel line;
      // OP.GG's stored rows never carried the flag, so 1.5 files read exactly as before.
      const complete = stat.fromCompleteList ?? false;
      map.set(key, {
        winRate: this.shrinkMatchup(stat.championId, stat.opponentId, stat.lane, stat.winRate, stat.play, !complete),
        play: stat.play,
        confidence: confidence(stat.play, this.matchupPrior(stat.lane)),
        isInferred: false,
        isLive: false,
        fromCompleteList: complete,
        measured: clampRate(stat.winRate),
      });
    }

    for (const stat of snapshot.matchups) {
      if (stat.lane === Lane.Unknown) continue;
      const a = this.indexById.get(stat.championId);
      const b = this.indexById.get(stat.opponentId);
      if (a === undefined || b === undefined) continue;

      const mirror = matchupKey(stat.lane, b, a);
      if (map.has(mirror)) continue;

      // Mirrored AFTER shrinking: the reverse edge is the exact complement of the stored one.
      const complete = stat.fromCompleteList ?? false;
      map.set(mirror, {
        winRate: 1 - this.shrinkMatchup(stat.championId, stat.opponentId, stat.lane, stat.winRate, stat.play, !complete),
        play: stat.play,
        confidence: confidence(stat.play, this.matchupPrior(stat.lane)),
        isInferred: true,
        isLive: false,
        fromCompleteList: complete,
        measured: 1 - clampRate(stat.winRate),
      });
    }

    return map;
  }

  private shrinkMatchup(championId: number, opponentId: number, lane: Lane, winRate: number, play: number, listed = true): number {
    return shrink(winRate, play, this.matchupPrior(lane), sigmoid(this.pairBaseline(championId, opponentId, lane, listed)));
  }

  private buildSynergies(snapshot: MetaSnapshot): Map<number, SynergyView> {
    const map = new Map<number, SynergyView>();
    for (const stat of snapshot.synergies) {
      const a = this.indexById.get(stat.championId);
      const b = this.indexById.get(stat.partnerId);
      if (a === undefined || b === undefined) continue;

      const expected = this.expectedDuo(stat);
      const view: SynergyView = {
        winRate: shrink(stat.winRate, stat.play, SYNERGY_PRIOR, expected),
        play: stat.play,
        confidence: confidence(stat.play, SYNERGY_PRIOR),
        tier: stat.tier ?? -1,
        expected,
        measured: clampRate(stat.winRate),
      };

      const forward = synergyKey(a, b);
      const existing = map.get(forward);
      if (!existing || stat.play > existing.play) map.set(forward, view);
    }
    return map;
  }

  private expectedDuo(stat: SynergyStat): number {
    const mine = this.laneStat(stat.championId, stat.lane);
    const theirs = this.laneStat(stat.partnerId, stat.partnerLane);
    if (!mine || !theirs) return this.synergyTarget;
    return sigmoid(expectedLogit(this.synergyLine, logit(mine.winRate), logit(theirs.winRate)));
  }
}

function supersedes(candidate: MatchupView, existing: MatchupView): boolean {
  return candidate.fromCompleteList !== existing.fromCompleteList
    ? candidate.fromCompleteList
    : candidate.play > existing.play;
}

// Numeric keys stay below 2^53: lane (3 bits) · 2^40 + index · 2^20 + index.
const matchupKey = (lane: Lane, a: number, b: number): number => lane * 2 ** 40 + a * 2 ** 20 + b;

const versusKey = (a: number, laneA: Lane, b: number, laneB: Lane): string => `${a}:${laneA}:${b}:${laneB}`;

function synergyKey(a: number, b: number): number {
  return Math.min(a, b) * 2 ** 20 + Math.max(a, b);
}
