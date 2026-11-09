// The draft numbers counted from a set of games, the way the collector counts and exports them
// (RiotMetaStats.cs: same pairs, same minimum games) — so a measurement can count on its training
// games only and read them through the app's own MetaLookup, shrinkage and baselines included.

import { ALL_LANES, Lane } from '../../src/core/model/lane';
import type { LaneStat, MatchupStat, MetaSnapshot, SynergyStat } from '../../src/core/model/metaLookup';
import type { Game } from './games';

/** MetaExportLimits in RiotMetaStats.cs. */
export const LANE_MINIMUM_GAMES = 20;
export const LANE_MINIMUM_ROLE_RATE = 0.05;
export const DUEL_MINIMUM_GAMES = 10;
export const DUO_MINIMUM_GAMES = 30;
export const VERSUS_MINIMUM_GAMES = 30;

/** Games and wins; for a pair, the wins of the champion with the lower id. */
export interface Count {
  games: number;
  wins: number;
}

export interface Tally {
  /** Lane rows, duels and duos as a meta.json would carry them; the rest from `base`. */
  snapshot: MetaSnapshot;
  /** Opposite teams, different lanes: `${a}:${laneA}:${b}:${laneB}`, a < b, wins of a. */
  versus: ReadonlyMap<string, Count>;
}

const bump = (map: Map<string, Count>, key: string, win: boolean) => {
  const count = map.get(key) ?? { games: 0, wins: 0 };
  count.games++;
  if (win) count.wins++;
  map.set(key, count);
};

export const versusKey = (a: number, laneA: Lane, b: number, laneB: Lane): string => `${a}:${laneA}:${b}:${laneB}`;

export function tally(games: readonly Game[], base: MetaSnapshot): Tally {
  const lanes = new Map<string, Count>();
  const champions = new Map<number, number>();
  const duels = new Map<string, Count>();
  const duos = new Map<string, Count>();
  const versus = new Map<string, Count>();

  for (const game of games) {
    const sides = [
      { seats: game.blue, won: game.blueWins === 1 },
      { seats: game.red, won: game.blueWins === 0 },
    ];
    for (const { seats, won } of sides) {
      for (const seat of seats) {
        champions.set(seat.championId, (champions.get(seat.championId) ?? 0) + 1);
        if (seat.lane !== Lane.Unknown) bump(lanes, `${seat.championId}:${seat.lane}`, won);
      }
      for (let i = 0; i < seats.length; i++)
        for (let j = i + 1; j < seats.length; j++) {
          const [a, b] = seats[i]!.championId < seats[j]!.championId ? [seats[i]!, seats[j]!] : [seats[j]!, seats[i]!];
          if (a.championId !== b.championId) bump(duos, `${a.championId}:${a.lane}:${b.championId}:${b.lane}`, won);
        }
    }
    for (const lane of ALL_LANES) {
      const blue = game.blue.filter((s) => s.lane === lane);
      const red = game.red.filter((s) => s.lane === lane);
      if (blue.length !== 1 || red.length !== 1 || blue[0]!.championId === red[0]!.championId) continue;
      const blueLow = blue[0]!.championId < red[0]!.championId;
      const [low, high] = blueLow ? [blue[0]!, red[0]!] : [red[0]!, blue[0]!];
      bump(duels, `${lane}:${low.championId}:${high.championId}`, blueLow ? game.blueWins === 1 : game.blueWins === 0);
    }
    for (const mine of game.blue)
      for (const theirs of game.red) {
        if (mine.lane === Lane.Unknown || theirs.lane === Lane.Unknown || mine.lane === theirs.lane || mine.championId === theirs.championId) continue;
        const blueLow = mine.championId < theirs.championId;
        const [a, b] = blueLow ? [mine, theirs] : [theirs, mine];
        bump(versus, versusKey(a.championId, a.lane, b.championId, b.lane), blueLow ? game.blueWins === 1 : game.blueWins === 0);
      }
  }

  const matches = Math.max(1, games.length);
  const laneStats: LaneStat[] = [];
  for (const [key, count] of lanes) {
    const [championId, lane] = key.split(':').map(Number) as [number, Lane];
    const total = Math.max(1, champions.get(championId) ?? count.games);
    const roleRate = count.games / total;
    if (count.games < LANE_MINIMUM_GAMES || roleRate < LANE_MINIMUM_ROLE_RATE) continue;
    laneStats.push({ championId, lane, winRate: count.wins / count.games, pickRate: count.games / matches, banRate: 0, roleRate, tier: -1, play: count.games });
  }

  const matchups: MatchupStat[] = [];
  for (const [key, count] of duels) {
    if (count.games < DUEL_MINIMUM_GAMES) continue;
    const [lane, low, high] = key.split(':').map(Number) as [Lane, number, number];
    const rate = count.wins / count.games;
    matchups.push({ championId: low, opponentId: high, lane, winRate: rate, play: count.games, fromCompleteList: true });
    matchups.push({ championId: high, opponentId: low, lane, winRate: 1 - rate, play: count.games, fromCompleteList: true });
  }

  const synergies: SynergyStat[] = [];
  for (const [key, count] of duos) {
    if (count.games < DUO_MINIMUM_GAMES) continue;
    const [championId, lane, partnerId, partnerLane] = key.split(':').map(Number) as [number, Lane, number, Lane];
    synergies.push({ championId, lane, partnerId, partnerLane, winRate: count.wins / count.games, play: count.games, tier: -1 });
  }

  return {
    snapshot: { ...base, games: games.length, laneStats, matchups, synergies, warnings: [] },
    versus,
  };
}
