// mayhem.json in the app: exact bench rates without the rounding term, augments by Wilson bound.

import { describe, expect, it } from 'vitest';
import { aramStatsFrom, mayhemAugments, type RiotMayhemFile } from '../../src/core/mayhem';
import { rankAram } from '../../src/core/model/advisors';

const file: RiotMayhemFile = {
  version: 1,
  patch: '16.22',
  platform: 'euw1',
  collectedAtUtc: '',
  games: 1000,
  champions: [
    { championId: 1, games: 400, wins: 220 },
    { championId: 2, games: 300, wins: 150 },
    { championId: 3, games: 0, wins: 0 },
  ],
  augments: [
    { championId: 1, augmentId: 10, games: 13, wins: 9 }, // 69 % on 13 games
    { championId: 1, augmentId: 11, games: 120, wins: 66 }, // 55 % on 120
    { championId: 1, augmentId: 12, games: 5, wins: 5 }, // below the 3 % share
    { championId: 2, augmentId: 10, games: 80, wins: 40 },
  ],
  builds: [],
};

describe('own Mayhem data', () => {
  it('gives the bench exact rates and leaves out champions without games', () => {
    const stats = aramStatsFrom(file);
    expect(stats.get(1)).toEqual({ championId: 1, winRate: 0.55, play: 400 });
    expect(stats.has(3)).toBe(false);
  });

  it('ranks the bench without the rounding term', () => {
    const stats = aramStatsFrom(file);
    const exact = rankAram(1, [2], stats, String, false);
    const rounded = rankAram(1, [2], stats, String, true);
    expect(exact.map((r) => r.championId)).toEqual([1, 2]);
    expect(exact[0]!.uncertainty).toBeLessThan(rounded[0]!.uncertainty);
    expect(exact[0]!.reasons.at(-1)!.text).toBe('55,0 % WR in Mayhem aus 400 Games');
  });

  it('ranks augments by what their games can vouch for, and drops rare ones', () => {
    expect(mayhemAugments(file, 1).map((a) => a.augmentId)).toEqual([11, 10]);
    expect(mayhemAugments(file, 1)[0]!.pickRate).toBeCloseTo(0.3, 12);
    expect(mayhemAugments(file, 99)).toEqual([]);
  });
});
