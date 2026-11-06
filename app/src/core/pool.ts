// "Mein Pool": the champions the player actually plays, per lane, from their own client — champion
// mastery and their own recent games, both read from the LCU. A filter only: the pool decides which
// champions are listed, never how they score (spec §6). A champion outside it keeps its number; it
// is just not shown under "Mein Pool".

import { Lane, laneDisplay } from './model/lane';
import type { MetaLookup } from './model/metaLookup';
import { netN0 } from './model/netFormat';

export interface MasteryEntry {
  championId: number;
  points: number;
}

/** One of the player's own recent games; lane Unknown for modes without lanes. */
export interface PlayedGame {
  championId: number;
  lane: Lane;
}

/** Hand edits, kept in the settings: always in, never in. */
export interface PoolEdits {
  added: readonly number[];
  removed: readonly number[];
}

export interface PoolInput {
  masteries: readonly MasteryEntry[];
  games: readonly PlayedGame[];
  edits?: PoolEdits;
}

/** Two games on the lane among the recent ones: played there on purpose, not once by autofill. */
export const POOL_MINIMUM_GAMES = 2;

/** Mastery that says "plays this champion", whatever the recent games show (a break, a new season). */
export const POOL_MASTERY_POINTS = 50_000;

/**
 * Mastery counts for a lane only where the champion is played at least this often (its share of
 * games there in the data): 300.000 points on Thresh put him in the support pool, not in the top one.
 */
export const POOL_MASTERY_ROLE_SHARE = 0.15;

export interface Pool {
  champions: ReadonlySet<number>;
  /** Why each champion is in: "3 Spiele auf Toplane zuletzt", "Meisterschaft 120.000", "von dir hinzugefügt". */
  reasons: ReadonlyMap<number, string>;
}

export function poolFor(lane: Lane, input: PoolInput, meta: MetaLookup): Pool {
  const reasons = new Map<number, string>();
  const removed = new Set(input.edits?.removed ?? []);

  const counts = new Map<number, number>();
  for (const game of input.games) {
    if (game.championId <= 0) continue;
    if (lane !== Lane.Unknown && game.lane !== lane) continue;
    counts.set(game.championId, (counts.get(game.championId) ?? 0) + 1);
  }
  for (const [championId, games] of counts) {
    if (games < POOL_MINIMUM_GAMES) continue;
    reasons.set(championId, lane === Lane.Unknown ? `${games} Spiele zuletzt` : `${games} Spiele auf ${laneDisplay(lane)} zuletzt`);
  }

  for (const entry of input.masteries) {
    if (entry.championId <= 0 || entry.points < POOL_MASTERY_POINTS || reasons.has(entry.championId)) continue;
    if (lane !== Lane.Unknown && meta.rolePrior(entry.championId, lane) < POOL_MASTERY_ROLE_SHARE) continue;
    reasons.set(entry.championId, `Meisterschaft ${netN0(entry.points)}`);
  }

  for (const championId of input.edits?.added ?? []) reasons.set(championId, 'von dir hinzugefügt');
  for (const championId of removed) reasons.delete(championId);

  return { champions: new Set(reasons.keys()), reasons };
}
