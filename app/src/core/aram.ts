// DirkDraft's own ARAM count (aram.json): the bench's win rates and each champion's ARAM build.
//
// Plain ARAM stands in for ARAM Mayhem: Riot's API does not hand out Mayhem games (403, measured
// 07.11.2026), so Mayhem cannot be counted. The bench and the build say so on screen. There are no
// augments in plain ARAM, so there is no augment list.

import type { AramStat } from './model/advisors';
import type { ChampionBuildStats, RiotBuildFile } from './riotBuilds';

export interface RiotAramFile {
  version: number;
  patch: string;
  platform: string;
  collectedAtUtc: string;
  games: number;
  champions: { championId: number; games: number; wins: number }[];
  /** Lane -1, tier "aram". */
  builds: ChampionBuildStats[];
}

/** The bench table: exact counts, so the ARAM ranking runs without OP.GG's rounding term. */
export function aramStatsFrom(file: RiotAramFile): Map<number, AramStat> {
  return new Map(
    file.champions
      .filter((c) => c.games > 0)
      .map((c) => [c.championId, { championId: c.championId, winRate: c.wins / c.games, play: c.games }]),
  );
}

/** The ARAM builds in the shape of a build file, for the build plan (core/riotBuilds aramPlan). */
export function aramBuildFile(file: RiotAramFile): RiotBuildFile {
  return { version: 1, patch: file.patch, platform: file.platform, collectedAtUtc: file.collectedAtUtc, matches: {}, builds: file.builds };
}
