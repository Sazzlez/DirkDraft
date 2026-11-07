// aram.json in the app: exact bench rates without the rounding term, and the ARAM build plan.

import { describe, expect, it } from 'vitest';
import { aramBuildFile, aramStatsFrom, type RiotAramFile } from '../../src/core/aram';
import { Lane } from '../../src/core/model/lane';
import { rankAram } from '../../src/core/model/advisors';
import { aramPlan } from '../../src/core/riotBuilds';

const count = (ids: number[], games: number, wins: number, text?: string) => ({ ids, games, wins, ...(text ? { text } : {}) });

const file: RiotAramFile = {
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
  builds: [
    {
      championId: 1,
      lane: Lane.Unknown,
      tier: 'aram',
      games: 400,
      wins: 220,
      runes: [],
      spells: [count([4, 32], 300, 170)],
      starters: [],
      boots: [count([3020], 200, 110)],
      core: [count([6655, 4645], 150, 90)],
      fourth: [],
      fifth: [],
      sixth: [],
      supportChoice: [],
      skillOrder: [count([], 100, 55, 'QWEQQRQWQWRWWEEREE')],
      skillMax: [count([], 100, 55, 'QWE')],
    },
  ],
};

describe('own ARAM data', () => {
  it('gives the bench exact rates and leaves out champions without games', () => {
    const stats = aramStatsFrom(file);
    expect(stats.get(1)).toEqual({ championId: 1, winRate: 0.55, play: 400 });
    expect(stats.has(3)).toBe(false);
  });

  it('ranks the bench without the rounding term and says where the numbers come from', () => {
    const stats = aramStatsFrom(file);
    const exact = rankAram(1, [2], stats, String, false);
    const rounded = rankAram(1, [2], stats, String, true);
    expect(exact.map((r) => r.championId)).toEqual([1, 2]);
    expect(exact[0]!.uncertainty).toBeLessThan(rounded[0]!.uncertainty);
    expect(exact[0]!.reasons.at(-1)!.text).toBe('55,0 % WR in ARAM aus 400 Games');
  });

  it('reads the lane-less ARAM build into a plan', () => {
    const plan = aramPlan(aramBuildFile(file), 1)!;
    expect(plan.lane).toBe(Lane.Unknown);
    expect(plan.spells?.ids).toEqual([4, 32]);
    expect(plan.core[0]?.ids).toEqual([6655, 4645]);
    expect(plan.skillMax).toBe('QWE');
    expect(aramPlan(aramBuildFile(file), 2)).toBeNull();
  });
});
