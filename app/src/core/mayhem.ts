// DirkDraft's own ARAM Mayhem count (mayhem.json): the bench's win rates and each champion's
// augments. Augments are ranked like build options — by the win rate their games can vouch for
// (Wilson lower bound) — and only those a champion takes often enough to mean something are shown.

import { wilsonLowerBound, type AramStat } from './model/advisors';
import type { ChampionBuildStats } from './riotBuilds';

export interface RiotMayhemFile {
  version: number;
  patch: string;
  platform: string;
  collectedAtUtc: string;
  games: number;
  champions: { championId: number; games: number; wins: number }[];
  augments: { championId: number; augmentId: number; games: number; wins: number }[];
  builds: ChampionBuildStats[];
}

/** The bench table: exact counts, so the ARAM ranking runs without OP.GG's rounding term. */
export function aramStatsFrom(file: RiotMayhemFile): Map<number, AramStat> {
  return new Map(
    file.champions
      .filter((c) => c.games > 0)
      .map((c) => [c.championId, { championId: c.championId, winRate: c.wins / c.games, play: c.games }]),
  );
}

export interface RankedAugmentView {
  augmentId: number;
  games: number;
  winRate: number;
  /** Share of the champion's games in which it was taken. */
  pickRate: number;
}

/** An augment needs this share of the champion's games before it is ranked at all. */
export const MINIMUM_AUGMENT_SHARE = 0.03;

export function mayhemAugments(file: RiotMayhemFile, championId: number, take = 8): RankedAugmentView[] {
  const champion = file.champions.find((c) => c.championId === championId);
  if (!champion || champion.games <= 0 || take <= 0) return [];
  return file.augments
    .filter((a) => a.championId === championId && a.games > 0 && a.games / champion.games >= MINIMUM_AUGMENT_SHARE)
    .map((a) => ({
      view: { augmentId: a.augmentId, games: a.games, winRate: a.wins / a.games, pickRate: a.games / champion.games },
      bound: wilsonLowerBound({ play: a.games, winRate: a.wins / a.games }),
    }))
    .sort((a, b) => b.bound - a.bound || b.view.games - a.view.games || a.view.augmentId - b.view.augmentId)
    .slice(0, take)
    .map((entry) => entry.view);
}
