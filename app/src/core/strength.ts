// The strength scale (docs/lane-vergleich.md): you against your lane opponent, from what the game's
// own scoreboard shows — levels and items. Up is stronger, down is weaker. It describes; it never
// says what to do, and nothing in it is a timer or knowledge the player does not have: no gold in
// anyone's pocket, no health, no cooldowns.
//
// The number behind it is the chance to win the next duel between the lane opponents, as the
// collector's duels say (docs/modellpruefung.md, Messung 27): a level and 1.000 gold of items each
// move the log-odds by a measured weight, per lane; on the bot lane both players of a side count
// together.

import { championIdOf, itemWorth, type ItemInfo, type LiveGameData, type LivePlayer } from './game';
import { Lane, laneFromLcu } from './model/lane';
import type { MetaLookup } from './model/metaLookup';

/** Log-odds per level and per 1.000 gold of item worth ahead. */
export interface StrengthWeights {
  level: number;
  gold: number;
}

/**
 * Measured on 13.11.2026 (Messung 27) on the 10.583 duels between 10:00 and 11:00 of the training
 * games, where the true level is known, and checked on 2.768 duels of held-out games: each step of
 * the scale wins as often as it says. Top, jungle and mid share one pair of weights — weights per
 * lane predict the held-out duels no better (0,0 standard errors), and the jungle alone has 367
 * duels. Bot: both players of a side summed.
 *
 * The window is minute 10 to 11 (levels 6 to 9); early and late game are unchecked. The collector's
 * duels before 13.11.2026 carry the winner's level-up from the kill and cannot be used; measure
 * again over the whole game once 40.000 clean duels are counted (`npm run measure:duels`).
 */
const SOLO: StrengthWeights = { level: 0.46, gold: 0.81 };
export const STRENGTH_WEIGHTS: Record<'top' | 'jungle' | 'mid' | 'bot', StrengthWeights> = {
  top: SOLO,
  jungle: SOLO,
  mid: SOLO,
  bot: { level: 0.18, gold: 0.41 },
};

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
  /** Chance to win the next duel, as level and items say. */
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
  weights: Record<'top' | 'jungle' | 'mid' | 'bot', StrengthWeights> = STRENGTH_WEIGHTS,
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
  const w = weights[lane === Lane.Top ? 'top' : lane === Lane.Jungle ? 'jungle' : lane === Lane.Mid ? 'mid' : 'bot'];
  const logOdds = w.level * levelDiff + (w.gold * worthDiff) / 1000;
  return {
    lane,
    mine: a,
    theirs: b,
    levelDiff,
    worthDiff,
    chance: sigmoid(logOdds),
    value: Math.max(-STRENGTH_STEPS, Math.min(STRENGTH_STEPS, logOdds / STRENGTH_STEP)),
  };
}
