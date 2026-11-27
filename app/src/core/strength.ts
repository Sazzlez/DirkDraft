// The strength scale (docs/lane-vergleich.md): you against your lane opponent, from what the game's
// own scoreboard shows — levels and items. Up is stronger, down is weaker. It describes; it never
// says what to do, and nothing in it is a timer or knowledge the player does not have: no gold in
// anyone's pocket, no health, no cooldowns.
//
// The number behind it is the chance to win the next duel between the lane opponents, as the
// collector's duels say (docs/modellpruefung.md, Messung 35): a level and 1.000 gold of items each
// move the log-odds by a weight measured per lane and phase; on the bot lane both players of a side
// count together. On top comes how strong each champion is in the duel on its lane in that phase,
// at equal level and items — measured at export and read from meta.json (`laneScale`).

import { championIdOf, itemWorth, type ItemInfo, type LiveEvent, type LiveGameData, type LivePlayer } from './game';
import { Lane, laneFromLcu } from './model/lane';
import type { LaneScaleFile, MetaLookup, TeamfightScaleFile } from './model/metaLookup';

/** Log-odds per level and per 1.000 gold of item worth ahead. */
export interface StrengthWeights {
  level: number;
  gold: number;
}

/** Weights per lane group (0 top, 1 jungle, 2 mid, 3 the bot lane) and phase (0, 1, 2). */
export type PhaseWeights = readonly (readonly StrengthWeights[])[];

/** The phase of a duel from its players' mean level: under 6, under 11, from 11 — as the export counts it (C# LaneScaleModel.PhaseOf). */
export const strengthPhaseOf = (meanLevel: number): number => (meanLevel < 6 ? 0 : meanLevel < 11 ? 1 : 2);

/**
 * Measured on 22.11.2026 (Messung 35) on 313.859 clean duels of the training games, checked on
 * 78.093 of held-out games. Used where meta.json brings no `laneScale` of its own (an older file, too
 * few clean duels); the export measures the same numbers afresh every run. Early a level and 1.000
 * gold are worth far more than late — the scale with one pair of weights from minute 10–11 said
 * „Leicht stärker“ in the first minutes where that side won 71 % of the duels.
 */
export const STRENGTH_WEIGHTS: PhaseWeights = [
  [{ level: 0.78, gold: 1.2 }, { level: 0.38, gold: 0.79 }, { level: 0.41, gold: 0.45 }],
  [{ level: 1.45, gold: 1.45 }, { level: 0.86, gold: 0.42 }, { level: 0.69, gold: 0.09 }],
  [{ level: 0.99, gold: 1.42 }, { level: 0.58, gold: 0.65 }, { level: 0.38, gold: 0.29 }],
  [{ level: 0.48, gold: 0.42 }, { level: 0.29, gold: 0.26 }, { level: 0.2, gold: 0.17 }],
];

/** What the scale reckons with: the weights, and each champion's duel strength on a lane in a phase. */
export interface LaneScale {
  weights: PhaseWeights;
  /** Log-odds the champion adds in a duel on that lane in that phase; 0 where nothing is measured. */
  strength: (championId: number, lane: Lane, phase: number) => number;
  /** Whether the champions' strengths are there (meta.json with `laneScale`). */
  measured: boolean;
}

const BUILT_IN: LaneScale = { weights: STRENGTH_WEIGHTS, strength: () => 0, measured: false };
const scales = new WeakMap<LaneScaleFile, LaneScale>();

/** The lane scale from meta.json, or the built-in weights without champion strengths. */
export function laneScaleOf(meta: MetaLookup): LaneScale {
  const file = meta.snapshot.laneScale;
  if (!file || !Array.isArray(file.weights) || !Array.isArray(file.champions) || file.champions.length === 0) return BUILT_IN;
  let scale = scales.get(file);
  if (!scale) {
    const weights = STRENGTH_WEIGHTS.map((phases, lane) =>
      phases.map((fallback, phase) => {
        const w = file.weights.find((x) => x.lane === lane && x.phase === phase);
        return w && Number.isFinite(w.level) && Number.isFinite(w.gold) ? { level: w.level, gold: w.gold } : fallback;
      }),
    );
    const strengths = new Map(file.champions.filter((c) => Number.isFinite(c.strength)).map((c) => [`${c.championId}:${c.lane}:${c.phase}`, c.strength]));
    scale = { weights, strength: (id, lane, phase) => strengths.get(`${id}:${lane}:${phase}`) ?? 0, measured: true };
    scales.set(file, scale);
  }
  return scale;
}

/**
 * Log-odds per step of the scale; ±3 steps is where it ends. On the held-out duels the steps win
 * 56 % (even), 65 %, 76 % and 85 % of the time.
 */
export const STRENGTH_STEP = 0.6;
export const STRENGTH_STEPS = 3;

export interface StrengthSide {
  championIds: number[];
  levels: number[];
  /** Item worth of the side, by the collector's rule (core/game itemWorth). */
  worth: number;
}

export interface Strength {
  /** The lane the comparison is for; Adc stands for the bot lane as two against two. */
  lane: Lane;
  mine: StrengthSide;
  theirs: StrengthSide;
  levelDiff: number;
  worthDiff: number;
  /** The phase now, from the lane players' mean level (strengthPhaseOf). */
  phase: number;
  /**
   * The champions' part per phase, own side minus the other, in log-odds at equal level and items
   * („Dieses Duell“); null without measured strengths.
   */
  duel: number[] | null;
  /** Chance to win the next duel, as level, items and the champions say. */
  chance: number;
  /** −3 … +3, positive is stronger. */
  value: number;
}

const sigmoid = (z: number): number => 1 / (1 + Math.exp(-z));

function ownRow(data: LiveGameData): LivePlayer | null {
  const me = data.activePlayer;
  const players = data.allPlayers ?? [];
  if (!me) return null;
  return players.find((p) => !!me.riotId && p.riotId === me.riotId) ?? players.find((p) => !!me.summonerName && p.summonerName === me.summonerName) ?? null;
}

function owned(player: LivePlayer): number[] {
  const ids: number[] = [];
  for (const item of player.items ?? []) {
    for (let i = 0; i < Math.max(1, item.count ?? 1); i++) ids.push(item.itemID);
  }
  return ids;
}

const isBot = (lane: Lane): boolean => lane === Lane.Adc || lane === Lane.Support;

function sideOf(players: readonly LivePlayer[], meta: MetaLookup, items: ReadonlyMap<number, ItemInfo>, levelOf: (p: LivePlayer) => number): StrengthSide {
  return {
    championIds: players.map((p) => championIdOf(p, meta)),
    levels: players.map(levelOf),
    worth: players.reduce((sum, p) => sum + itemWorth(owned(p), items), 0),
  };
}

/**
 * The comparison for the own lane, or null where the opponent is not known.
 *
 * Who stands against whom comes from the game's own `position` (it is filled in ranked and normal
 * draft). Where the game gives none, `fallback` — the lane and lane opponent of the draft — finds a
 * single opponent; the bot lane needs all four positions and is left out without them.
 */
export function strengthOf(
  data: LiveGameData,
  meta: MetaLookup,
  items: ReadonlyMap<number, ItemInfo>,
  fallback: { lane: Lane; opponentId: number } | null = null,
  scale: LaneScale = laneScaleOf(meta),
): Strength | null {
  const me = ownRow(data);
  if (!me || !me.team) return null;
  const players = data.allPlayers ?? [];
  const allies = players.filter((p) => p.team === me.team);
  const enemies = players.filter((p) => p.team && p.team !== me.team);
  const laneOf = (p: LivePlayer): Lane => laneFromLcu(p.position);
  // The own level is exact on the active player; the scoreboard's is the same number a tick later.
  const levelOf = (p: LivePlayer): number => Math.max(1, (p === me ? data.activePlayer?.level : undefined) ?? p.level ?? 1);

  let lane = laneOf(me);
  let mine: LivePlayer[];
  let theirs: LivePlayer[];
  if (isBot(lane)) {
    mine = allies.filter((p) => isBot(laneOf(p)));
    theirs = enemies.filter((p) => isBot(laneOf(p)));
    if (mine.length !== 2 || theirs.length !== 2) return null;
    // ADC first on both sides, so the tile reads the same way every time.
    const adcFirst = (a: LivePlayer, b: LivePlayer): number => laneOf(a) - laneOf(b);
    mine.sort(adcFirst);
    theirs.sort(adcFirst);
    lane = Lane.Adc;
  } else {
    let opponent: LivePlayer | undefined;
    if (lane !== Lane.Unknown) {
      const same = enemies.filter((p) => laneOf(p) === lane);
      opponent = same.length === 1 ? same[0] : undefined;
    }
    if (!opponent && fallback && fallback.opponentId > 0 && !isBot(fallback.lane) && fallback.lane !== Lane.Unknown) {
      const same = enemies.filter((p) => championIdOf(p, meta) === fallback.opponentId);
      opponent = same.length === 1 ? same[0] : undefined;
      if (opponent) lane = fallback.lane;
    }
    if (!opponent || lane === Lane.Unknown) return null;
    mine = [me];
    theirs = [opponent];
  }

  const a = sideOf(mine, meta, items, levelOf);
  const b = sideOf(theirs, meta, items, levelOf);
  const sum = (values: number[]): number => values.reduce((total, v) => total + v, 0);
  const levelDiff = sum(a.levels) - sum(b.levels);
  const worthDiff = a.worth - b.worth;
  const phase = strengthPhaseOf(sum([...a.levels, ...b.levels]) / (a.levels.length + b.levels.length));
  const group = lane === Lane.Top ? 0 : lane === Lane.Jungle ? 1 : lane === Lane.Mid ? 2 : 3;
  const w = scale.weights[group]![phase]!;

  // Each champion on the lane it plays here: the compared lane, on the bot lane ADC and support.
  const lanesOf = (side: LivePlayer[]): Lane[] => side.map((p) => (lane === Lane.Adc ? laneOf(p) : lane));
  const mineLanes = lanesOf(mine);
  const theirLanes = lanesOf(theirs);
  const duelIn = (p: number): number =>
    sum(a.championIds.map((id, i) => scale.strength(id, mineLanes[i]!, p))) - sum(b.championIds.map((id, i) => scale.strength(id, theirLanes[i]!, p)));
  const duel = scale.measured ? [0, 1, 2].map(duelIn) : null;

  const logOdds = w.level * levelDiff + (w.gold * worthDiff) / 1000 + (duel?.[phase] ?? 0);
  return {
    lane,
    mine: a,
    theirs: b,
    levelDiff,
    worthDiff,
    phase,
    duel,
    chance: sigmoid(logOdds),
    value: Math.max(-STRENGTH_STEPS, Math.min(STRENGTH_STEPS, logOdds / STRENGTH_STEP)),
  };
}

// ---- after the lane phase: team against team -----------------------------------------------------

/**
 * When the tile changes from "you against your lane opponent" to "team against team". The lane
 * scale is checked on the duels before 14:00 (Messung 35); at 14:00 the turret plating falls and the
 * lanes break up — a support stood "deutlich stärker" against the enemy bot lane at minute 35
 * (15.11.2026), from his ADC's items. A convention, not a measured boundary.
 */
export const LANE_PHASE_SECONDS = 14 * 60;

export interface TeamStand {
  /** Levels and item worth of each side, summed over its players. */
  mine: { players: number; level: number; worth: number };
  theirs: { players: number; level: number; worth: number };
  levelDiff: number;
  worthDiff: number;
  /** Every enemy as the scoreboard shows him, in the game's own order. */
  enemies: { championId: number; level: number; worth: number }[];
  /** The enemy with the most item worth; 0 while nobody has bought anything that counts. */
  leaderId: number;
}

/**
 * Both teams as the scoreboard shows them: levels and item worth, summed. It describes and carries
 * no verdict — the lane scale's weights are measured on duels of lane opponents, and nothing says
 * yet what a level or 1.000 gold are worth to a team. Null without the own row or without enemies.
 */
export function teamStandOf(data: LiveGameData, meta: MetaLookup, items: ReadonlyMap<number, ItemInfo>): TeamStand | null {
  const me = ownRow(data);
  if (!me || !me.team) return null;
  const players = data.allPlayers ?? [];
  const allies = players.filter((p) => p.team === me.team);
  const enemies = players.filter((p) => p.team && p.team !== me.team);
  if (enemies.length === 0) return null;
  const levelOf = (p: LivePlayer): number => Math.max(1, (p === me ? data.activePlayer?.level : undefined) ?? p.level ?? 1);
  const side = (team: readonly LivePlayer[]) => ({
    players: team.length,
    level: team.reduce((sum, p) => sum + levelOf(p), 0),
    worth: team.reduce((sum, p) => sum + itemWorth(owned(p), items), 0),
  });
  const mine = side(allies);
  const theirs = side(enemies);
  const rows = enemies.map((p) => ({ championId: championIdOf(p, meta), level: levelOf(p), worth: itemWorth(owned(p), items) }));
  const leader = rows.reduce<(typeof rows)[number] | null>((best, row) => (row.worth > (best?.worth ?? 0) ? row : best), null);
  return { mine, theirs, levelDiff: mine.level - theirs.level, worthDiff: mine.worth - theirs.worth, enemies: rows, leaderId: leader?.championId ?? 0 };
}

// ---- the teamfight scale: team against team, with a verdict ----------------------------------------

/** How long Baron's and the Elder's buffs last; the measurement gives them to the whole team, and so does the scale. */
export const BARON_SECONDS = 180;
export const ELDER_SECONDS = 150;

/** The phase of the game for the champions' teamfight strength: before minute 20, before minute 30, after (C# TeamfightScaleModel.PhaseOf). */
export const teamfightPhaseOf = (seconds: number): number => (seconds < 1200 ? 0 : seconds < 1800 ? 1 : 2);

/** What the teamfight scale reckons with (meta.json `teamfightScale`, Messung 36). */
export interface TeamfightScale {
  weights: TeamfightScaleFile['weights'];
  /** Log-odds the champion adds to its team in a fight in that phase; 0 where nothing is measured. */
  strength: (championId: number, phase: number) => number;
}

const teamfightScales = new WeakMap<TeamfightScaleFile, TeamfightScale>();

/** The teamfight scale from meta.json, or null — without measured weights the tile gives no verdict. */
export function teamfightScaleOf(meta: MetaLookup): TeamfightScale | null {
  const file = meta.snapshot.teamfightScale;
  const w = file?.weights;
  if (!file || !w || ![w.level, w.gold, w.baron, w.elder, w.dragon, w.soul].every(Number.isFinite)) return null;
  let scale = teamfightScales.get(file);
  if (!scale) {
    const strengths = new Map((file.champions ?? []).filter((c) => Number.isFinite(c.strength)).map((c) => [`${c.championId}:${c.phase}`, c.strength]));
    scale = { weights: w, strength: (id, phase) => strengths.get(`${id}:${phase}`) ?? 0 };
    teamfightScales.set(file, scale);
  }
  return scale;
}

/** What a team brings into a fight from the monsters: as the game's event feed shows them to everybody. */
export interface TeamBuffs {
  /** Elemental dragons taken. */
  dragons: number;
  /** The first team to four elemental dragons holds the soul. */
  soul: boolean;
  /** Baron taken in the last BARON_SECONDS. */
  baron: boolean;
  /** The Elder taken in the last ELDER_SECONDS. */
  elder: boolean;
}

/**
 * Both teams' buffs from the event feed. The killer is named as the scrubber writes the Riot ID
 * without its tag — the same pseudonym as the player's `riotIdGameName`; where a player has none,
 * the summoner name. A kill by nobody the list knows (a minion's last hit) counts for neither.
 */
export function teamBuffsOf(data: LiveGameData, mineTeam: string): { mine: TeamBuffs; theirs: TeamBuffs } {
  const teamOf = new Map<string, string>();
  for (const p of data.allPlayers ?? []) {
    if (!p.team) continue;
    for (const name of [p.riotIdGameName, p.summonerName, p.riotId, p.riotId?.split('#')[0]]) if (name && !teamOf.has(name)) teamOf.set(name, p.team);
  }
  const now = data.gameData?.gameTime ?? 0;
  const empty = (): TeamBuffs => ({ dragons: 0, soul: false, baron: false, elder: false });
  const mine = empty();
  const theirs = empty();
  const events: LiveEvent[] = [...(data.events?.Events ?? [])].sort((a, b) => (a.EventTime ?? 0) - (b.EventTime ?? 0));
  for (const e of events) {
    const team = e.KillerName ? teamOf.get(e.KillerName) : undefined;
    if (!team) continue;
    const side = team === mineTeam ? mine : theirs;
    const ago = now - (e.EventTime ?? 0);
    if (e.EventName === 'BaronKill' && ago >= 0 && ago <= BARON_SECONDS) side.baron = true;
    if (e.EventName !== 'DragonKill') continue;
    if (e.DragonType === 'Elder') {
      if (ago >= 0 && ago <= ELDER_SECONDS) side.elder = true;
    } else {
      side.dragons++;
      if (side.dragons >= 4 && !mine.soul && !theirs.soul) side.soul = true;
    }
  }
  return { mine, theirs };
}

export interface TeamStrength {
  stand: TeamStand;
  buffs: { mine: TeamBuffs; theirs: TeamBuffs };
  /** The phase of the game for the champions (teamfightPhaseOf). */
  phase: number;
  /** What each factor adds for the own team, in log-odds: the rows of the tile, sorted there by size. */
  parts: { level: number; gold: number; baron: number; elder: number; dragons: number; soul: number; champions: number };
  /** Chance to win a teamfight of five against five now, as the measured weights say. */
  chance: number;
  /** −3 … +3, positive is stronger; the lane scale's steps (STRENGTH_STEP). */
  value: number;
}

/**
 * The teamfight scale: if all ten met now, which team would win the fight? From the levels and items
 * on the scoreboard, the monsters in the event feed and the champions — never who is dead now, no
 * timer, no cooldown (docs/teamfight-skala.md, „Bewusst nicht drin“). Null without the measured scale
 * in meta.json or without the own row.
 */
export function teamStrengthOf(data: LiveGameData, meta: MetaLookup, items: ReadonlyMap<number, ItemInfo>, scale: TeamfightScale | null = teamfightScaleOf(meta)): TeamStrength | null {
  if (!scale) return null;
  const stand = teamStandOf(data, meta, items);
  const me = ownRow(data);
  if (!stand || !me?.team) return null;
  const players = data.allPlayers ?? [];
  const phase = teamfightPhaseOf(data.gameData?.gameTime ?? 0);
  const champions = (team: (p: LivePlayer) => boolean): number => players.filter(team).reduce((sum, p) => sum + scale.strength(championIdOf(p, meta), phase), 0);
  const buffs = teamBuffsOf(data, me.team);
  const flag = (value: boolean): number => (value ? 1 : 0);
  const w = scale.weights;

  // Gold on top, mid and ADC counts about twice as much as on jungle and support (Messung 36): an
  // enemy ADC with less gold than our support is still the bigger threat. Where a position is
  // missing (blind pick, custom game) the summed weight stands in.
  const carries = new Set<Lane>([Lane.Top, Lane.Mid, Lane.Adc]);
  const inTeams = players.filter((p) => !!p.team);
  const byRole = w.goldCarry !== undefined && w.goldRest !== undefined && Number.isFinite(w.goldCarry) && Number.isFinite(w.goldRest) && inTeams.every((p) => laneFromLcu(p.position) !== Lane.Unknown);
  const goldOf = (carry: boolean): number =>
    inTeams.reduce((sum, p) => (carries.has(laneFromLcu(p.position)) === carry ? sum + (p.team === me.team ? 1 : -1) * itemWorth(owned(p), items) : sum), 0);
  const gold = byRole ? (w.goldCarry! * goldOf(true) + w.goldRest! * goldOf(false)) / 1000 : (w.gold * stand.worthDiff) / 1000;

  const parts = {
    level: w.level * stand.levelDiff,
    gold,
    baron: w.baron * (flag(buffs.mine.baron) - flag(buffs.theirs.baron)),
    elder: w.elder * (flag(buffs.mine.elder) - flag(buffs.theirs.elder)),
    dragons: w.dragon * (buffs.mine.dragons - buffs.theirs.dragons),
    soul: w.soul * (flag(buffs.mine.soul) - flag(buffs.theirs.soul)),
    champions: champions((p) => p.team === me.team) - champions((p) => !!p.team && p.team !== me.team),
  };
  const logOdds = Object.values(parts).reduce((sum, v) => sum + v, 0);
  return { stand, buffs, phase, parts, chance: sigmoid(logOdds), value: Math.max(-STRENGTH_STEPS, Math.min(STRENGTH_STEPS, logOdds / STRENGTH_STEP)) };
}
