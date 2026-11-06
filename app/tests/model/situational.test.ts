// Situational items: same situations as the collector, and only clear, situation-specific edges.

import { describe, expect, it } from 'vitest';
import { Lane } from '../../src/core/model/lane';
import { DamageType } from '../../src/core/model/metaLookup';
import { TraitTable } from '../../src/core/model/traits';
import { SITUATION, SLOT, situationalSuggestions, situationsOf, type RiotSituationalFile } from '../../src/core/situational';
import { MetaBuilder } from './helpers';

const meta = new MetaBuilder()
  .champion(1, 'Darius', { damage: DamageType.Physical, tags: ['Fighter', 'Tank'], defense: 5 })
  .champion(11, 'Ahri', { damage: DamageType.Magic, tags: ['Mage'] })
  .champion(12, 'Syndra', { damage: DamageType.Magic, tags: ['Mage'] })
  .champion(13, 'Leona', { damage: DamageType.Magic, tags: ['Tank', 'Support'], defense: 8 })
  .champion(14, 'Ornn', { damage: DamageType.Mixed, tags: ['Tank'], defense: 9 })
  .champion(15, 'Jinx', { damage: DamageType.Physical, tags: ['Marksman'] })
  .build();

const traits = TraitTable.fromEntries([
  ['Leona', { engage: 3, peel: 2, cc: 3, scaling: 'early', waveclear: 1 }],
  ['Ornn', { engage: 3, peel: 1, cc: 3, scaling: 'mid', waveclear: 2 }],
  ['Syndra', { engage: 1, peel: 1, cc: 2, scaling: 'mid', waveclear: 3 }],
]);

describe('situations in the game', () => {
  it('match the collector: dominant damage, two tanks, three hard-CC champions', () => {
    // Magic 1 + 1 + 1 + 0,5 of 5 = 70 %; Leona and Ornn are frontline; three with hard CC.
    expect(situationsOf([11, 12, 13, 14, 15], meta, traits)).toEqual([SITUATION.All, SITUATION.Magic, SITUATION.Tanks, SITUATION.HardCc]);
  });
});

const TOP = Lane.Top;
const L = SLOT.Late;
const M = SITUATION.Magic;
const A = SITUATION.All;

// Darius, late slot. Spirit Visage (3065) is ordinary in general and strong against magic damage;
// Sterak's (3053) is good everywhere — that is a base-build item, not a situational one.
const file: RiotSituationalFile = {
  version: 1,
  patch: '16.22',
  platform: 'euw1',
  collectedAtUtc: '',
  situations: ['all', 'physical', 'magic', 'tanks', 'cc'],
  classNames: ['Fighter'],
  rows: [
    [1, TOP, L, A, 0, 4000, 2000],
    [1, TOP, L, A, 3065, 800, 404],
    [1, TOP, L, A, 3053, 1500, 810],
    [1, TOP, L, M, 0, 1000, 500],
    [1, TOP, L, M, 3065, 400, 232],
    [1, TOP, L, M, 3053, 350, 189],
  ],
  classes: [
    [0, TOP, L, A, 0, 20000, 10000],
    [0, TOP, L, A, 3065, 4000, 2010],
    [0, TOP, L, A, 3053, 7000, 3780],
    [0, TOP, L, M, 0, 5000, 2500],
    [0, TOP, L, M, 3065, 2000, 1120],
    [0, TOP, L, M, 3053, 1700, 918],
  ],
};

describe('situational suggestions', () => {
  it('suggest an item that is better in this situation than in general', () => {
    const list = situationalSuggestions(file, meta, 1, TOP, [A, M], new Set());
    expect(list.map((s) => s.itemId)).toEqual([3065]);
    expect(list[0]!.situation).toBe(M);
    expect(list[0]!.winRate).toBeGreaterThan(list[0]!.otherRate + 0.02);
  });

  it('say nothing when the situation is not on', () => {
    expect(situationalSuggestions(file, meta, 1, TOP, [A], new Set())).toEqual([]);
  });

  it('leave out what the base build or the inventory already has', () => {
    expect(situationalSuggestions(file, meta, 1, TOP, [A, M], new Set([3065]))).toEqual([]);
  });

  it('need enough games', () => {
    const thin: RiotSituationalFile = { ...file, rows: file.rows.map((r) => [...r.slice(0, 5), Math.round(r[5]! / 100), Math.round(r[6]! / 100)]), classes: [] };
    expect(situationalSuggestions(thin, meta, 1, TOP, [A, M], new Set())).toEqual([]);
  });
});
