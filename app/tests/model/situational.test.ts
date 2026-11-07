// Situational items: same situations as the collector, and only clear, situation-specific edges.

import { describe, expect, it } from 'vitest';
import { Lane } from '../../src/core/model/lane';
import { DamageType } from '../../src/core/model/metaLookup';
import { TraitTable } from '../../src/core/model/traits';
import { SITUATION, SLOT, SituationTracker, situationalSuggestions, situationsOf, type RiotSituationalFile } from '../../src/core/situational';
import { enemyItemsOf, type ItemInfo } from '../../src/core/game';
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

describe('situations from the enemies’ items', () => {
  const items = new Map<number, ItemInfo>([
    [1001, { total: 300, from: [], into: [3047], tags: ['Boots'] }],
    [3047, { total: 1100, from: [1001], tags: ['Boots', 'Armor'], armor: 25 }],
    [3075, { total: 2450, from: [1029], tags: ['Armor'], armor: 75 }],
    [1029, { total: 300, from: [], into: [3075], tags: ['Armor'], armor: 15 }],
    [3065, { total: 2700, from: [], tags: ['SpellBlock'], magicResist: 60 }],
    [3072, { total: 3400, from: [], tags: ['LifeSteal'] }],
  ]);

  it('count finished items and boots, as the collector does', () => {
    expect(enemyItemsOf([3047, 3075, 1029, 3065, 3072], items)).toEqual({ armor: 100, magicResist: 60, sustain: 1 });
  });

  it('turn on at the same thresholds', () => {
    const flags = situationsOf([15], meta, traits, { armor: 150, magicResist: 119, sustain: 2 });
    expect(flags).toEqual([SITUATION.All, SITUATION.EnemyArmor, SITUATION.EnemySustain]);
  });

  it('stay on for the rest of the game once reached', () => {
    const tracker = new SituationTracker();
    expect(tracker.update([SITUATION.All, SITUATION.EnemyArmor])).toEqual([SITUATION.All, SITUATION.EnemyArmor]);
    expect(tracker.update([SITUATION.All])).toEqual([SITUATION.All, SITUATION.EnemyArmor]); // an item sold
    tracker.reset();
    expect(tracker.update([SITUATION.All])).toEqual([SITUATION.All]);
  });
});

const TOP = Lane.Top;
const L = SLOT.Late;
const M = SITUATION.Magic;
const A = SITUATION.All;

// Darius, late slot, rows per game state (0 even, 1 ahead, 2 behind). The slot wins 50/60/40 %.
//  - Spirit Visage (3065): bought 40 % of the time against magic damage, 7 % otherwise, and does
//    as well as the slot's other choices in every state — the situational item.
//  - Sterak's (3053): bought 30 % of the time everywhere — a base-build item, not a situational one.
//  - Death's Dance (6333): bought far more often against magic damage, but measurably worse within
//    each state — the guard keeps it out.
const E = 0, AH = 1, BH = 2;
const file: RiotSituationalFile = {
  version: 2,
  patch: '16.22',
  platform: 'euw1',
  collectedAtUtc: '',
  situations: ['all', 'physical', 'magic', 'tanks', 'cc', 'armor', 'magicResist', 'sustain'],
  classNames: ['Fighter'],
  rows: [
    [1, TOP, L, A, E, 0, 2000, 1000],
    [1, TOP, L, A, AH, 0, 1000, 600],
    [1, TOP, L, A, BH, 0, 1000, 400],
    [1, TOP, L, A, E, 3065, 300, 150],
    [1, TOP, L, A, AH, 3065, 150, 90],
    [1, TOP, L, A, BH, 3065, 150, 60],
    [1, TOP, L, A, E, 3053, 600, 300],
    [1, TOP, L, A, AH, 3053, 300, 180],
    [1, TOP, L, A, BH, 3053, 300, 120],
    [1, TOP, L, A, E, 6333, 220, 66],
    [1, TOP, L, A, AH, 6333, 110, 44],
    [1, TOP, L, A, BH, 6333, 110, 28],
    [1, TOP, L, M, E, 0, 500, 250],
    [1, TOP, L, M, AH, 0, 250, 150],
    [1, TOP, L, M, BH, 0, 250, 100],
    [1, TOP, L, M, E, 3065, 200, 100],
    [1, TOP, L, M, AH, 3065, 100, 60],
    [1, TOP, L, M, BH, 3065, 100, 40],
    [1, TOP, L, M, E, 3053, 150, 75],
    [1, TOP, L, M, AH, 3053, 75, 45],
    [1, TOP, L, M, BH, 3053, 75, 30],
    [1, TOP, L, M, E, 6333, 200, 60],
    [1, TOP, L, M, AH, 6333, 100, 40],
    [1, TOP, L, M, BH, 6333, 100, 25],
  ],
  classes: [],
};

describe('situational suggestions', () => {
  it('suggest what players buy clearly more often in this situation', () => {
    const list = situationalSuggestions(file, meta, 1, TOP, [A, M], new Set());
    expect(list.map((s) => s.itemId)).toEqual([3065]);
    expect(list[0]!.situation).toBe(M);
    expect(list[0]!.timesMoreOften).toBeGreaterThan(5);
    // Not worse than the alternatives within the same game states.
    expect(Math.abs(list[0]!.winRate - list[0]!.otherRate)).toBeLessThan(0.02);
  });

  it('say nothing when the situation is not on', () => {
    expect(situationalSuggestions(file, meta, 1, TOP, [A], new Set())).toEqual([]);
  });

  it('leave out what the base build or the inventory already has', () => {
    expect(situationalSuggestions(file, meta, 1, TOP, [A, M], new Set([3065]))).toEqual([]);
  });

  it('answer the enemies’ items only with late items — boots follow the line-up — and no boots once a pair is bought', () => {
    // The same Spirit Visage rows, once as boots bought "against sustain" and once against magic damage.
    const asBoots = (situation: number) => file.rows.filter((r) => r[3] === A || r[3] === M).map((r) => [r[0]!, r[1]!, SLOT.Boots, r[3] === M ? situation : A, ...r.slice(4)]);
    const sustain: RiotSituationalFile = { ...file, rows: asBoots(SITUATION.EnemySustain) };
    expect(situationalSuggestions(sustain, meta, 1, TOP, [A, SITUATION.EnemySustain], new Set())).toEqual([]);
    const magic: RiotSituationalFile = { ...file, rows: asBoots(M) };
    expect(situationalSuggestions(magic, meta, 1, TOP, [A, M], new Set()).map((x) => x.slot)).toEqual([SLOT.Boots]);
    expect(situationalSuggestions(magic, meta, 1, TOP, [A, M], new Set(), 3, [SLOT.Late])).toEqual([]);
  });

  it('need enough games', () => {
    const thin: RiotSituationalFile = { ...file, rows: file.rows.map((r) => [...r.slice(0, 6), Math.round(r[6]! / 100), Math.round(r[7]! / 100)]) };
    expect(situationalSuggestions(thin, meta, 1, TOP, [A, M], new Set())).toEqual([]);
  });
});
