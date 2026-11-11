// Situational items: same situations as the collector, and only clear, situation-specific edges.

import { describe, expect, it } from 'vitest';
import { Lane } from '../../src/core/model/lane';
import { DamageType } from '../../src/core/model/metaLookup';
import { TraitTable } from '../../src/core/model/traits';
import type { RiotBuildPlan } from '../../src/core/riotBuilds';
import {
  applySwaps,
  boughtFromAhead,
  rateSuggestions,
  situationalBuild,
  situationShare,
  SwapTracker,
  MINIMUM_OWN_BUYS,
  SITUATION,
  SLOT,
  SituationTracker,
  sameStateEdge,
  situationalSuggestions,
  situationsOf,
  WIN_MORE_MINIMUM_BUYS,
  type RiotSituationalFile,
} from '../../src/core/situational';
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

  it('suggest core items too (Mikael’s against heavy crowd control), and only while the core slot is open', () => {
    // The same Spirit Visage rows, counted in the core.
    const core: RiotSituationalFile = { ...file, rows: file.rows.map((r) => [r[0]!, r[1]!, SLOT.Core, ...r.slice(3)]) };
    const list = situationalSuggestions(core, meta, 1, TOP, [A, M], new Set());
    expect(list.map((s) => [s.itemId, s.slot])).toEqual([[3065, SLOT.Core]]);
    expect(situationalSuggestions(core, meta, 1, TOP, [A, M], new Set(), 3, [SLOT.Boots, SLOT.Late])).toEqual([]);
  });

  it('need enough games', () => {
    const thin: RiotSituationalFile = { ...file, rows: file.rows.map((r) => [...r.slice(0, 6), Math.round(r[6]! / 100), Math.round(r[7]! / 100)]) };
    expect(situationalSuggestions(thin, meta, 1, TOP, [A, M], new Set())).toEqual([]);
  });
});

// The class's word against the champion's own record. Talon (jungle, Assassin) with 6.455 late-slot
// games and never a Rabadon's Deathcap; his class buys it in 40 % of the late slots against heavy
// magic resist and in 8 % otherwise (Akali, Katarina). After the file of 11.11.2026, where it was
// 20 % against 8 % and the suggestion read "125 times as often".
const JG = Lane.Jungle;
const MR = SITUATION.EnemyMagicResist;
const assassins = new MetaBuilder().champion(91, 'Talon', { damage: DamageType.Physical, tags: ['Assassin'] }).build();
const split = (owner: number, situation: number, item: number, games: number, rate = 0.5): number[][] =>
  [E, AH, BH].map((state) => [owner, JG, L, situation, state, item, Math.round(games / 3), Math.round((games / 3) * rate)]);
const classRows = [...split(0, A, 0, 60000), ...split(0, A, 3089, 5760), ...split(0, MR, 0, 3000), ...split(0, MR, 3089, 1200), ...split(0, A, 6694, 9000), ...split(0, MR, 6694, 900)];
const talon = (own: number[][]): RiotSituationalFile => ({ ...file, classNames: ['Assassin'], rows: own, classes: classRows });

describe('a champion leaning on its class', () => {
  it('is not told to buy what its own players never buy', () => {
    // 77 late slots against magic resist, 6.455 otherwise, no Rabadon's in either.
    const f = talon([...split(91, A, 0, 6532), ...split(91, MR, 0, 77)]);
    expect(situationalSuggestions(f, assassins, 91, JG, [A, MR], new Set())).toEqual([]);
  });

  it('compares like with like: the class in the situation against the class otherwise', () => {
    // A few buys of his own, so the item is his at all — 1 % of his late slots, none of the 78
    // against magic resist. The class's 40 % against 8 % then counts on both sides with the same
    // weight (50 of 128 games): 0,39 · 40 % against 0,39 · 8 % + 0,61 · 1 %, about four times as
    // often. Before, the class stood against his own 1 % alone: fourteen times.
    const f = talon([...split(91, A, 0, 6532), ...split(91, MR, 0, 78), ...split(91, A, 3089, 66), ...split(91, MR, 3089, 0)]);
    const list = situationalSuggestions(f, assassins, 91, JG, [A, MR], new Set());
    expect(list.map((s) => s.itemId)).toEqual([3089]);
    expect(list[0]!.timesMoreOften).toBeGreaterThan(3.5);
    expect(list[0]!.timesMoreOften).toBeLessThan(5);
  });

  it('says nothing where the class buys the item no more often in the situation', () => {
    // Serpent's Fang (6694): 30 % of the class's late slots against magic resist, 15 % otherwise —
    // and Talon's own 15 % everywhere. With few games in the situation the old rule compared the
    // class there with Talon alone otherwise; with many it sees his own flat share.
    const many = talon([...split(91, A, 0, 9000), ...split(91, MR, 0, 3000), ...split(91, A, 6694, 1350), ...split(91, MR, 6694, 450)]);
    expect(situationalSuggestions(many, assassins, 91, JG, [A, MR], new Set())).toEqual([]);
  });

  it('needs a handful of own buys', () => {
    const rows = (buys: number) => talon([...split(91, A, 0, 300), ...split(91, MR, 0, 0), ...split(91, A, 3089, buys)]);
    expect(situationalSuggestions(rows(MINIMUM_OWN_BUYS - 2), assassins, 91, JG, [A, MR], new Set())).toEqual([]);
    expect(situationalSuggestions(rows(MINIMUM_OWN_BUYS + 1), assassins, 91, JG, [A, MR], new Set()).map((s) => s.itemId)).toEqual([3089]);
  });
});

describe('the same game state', () => {
  // Jinx's boots on 11.11.2026, after the pattern of Taliyah's: one pair wins 64 % against the most
  // played pair's 48 % over all games, and 67/91/30 % against 50/77/16 % within the states.
  const boots: RiotSituationalFile = {
    ...file,
    rows: [
      [163, Lane.Mid, SLOT.Boots, A, E, 0, 883, 450],
      [163, Lane.Mid, SLOT.Boots, A, AH, 0, 454, 354],
      [163, Lane.Mid, SLOT.Boots, A, BH, 0, 535, 107],
      [163, Lane.Mid, SLOT.Boots, A, E, 3009, 52, 35],
      [163, Lane.Mid, SLOT.Boots, A, AH, 3009, 23, 21],
      [163, Lane.Mid, SLOT.Boots, A, BH, 3009, 23, 7],
      [163, Lane.Mid, SLOT.Boots, A, E, 3158, 484, 242],
      [163, Lane.Mid, SLOT.Boots, A, AH, 3158, 204, 157],
      [163, Lane.Mid, SLOT.Boots, A, BH, 3158, 225, 36],
    ],
  };

  it('compares two items state by state', () => {
    const edge = sameStateEdge(boots, 163, Lane.Mid, SLOT.Boots, [3009], [3158])!;
    expect(edge).toBeGreaterThan(2);
    expect(edge).toBeLessThan(4);
    expect(sameStateEdge(boots, 163, Lane.Mid, SLOT.Boots, [3158], [3009])).toBeCloseTo(-edge, 12);
  });

  it('counts several items on a side together', () => {
    const one = sameStateEdge(boots, 163, Lane.Mid, SLOT.Boots, [3009], [3158])!;
    expect(sameStateEdge(boots, 163, Lane.Mid, SLOT.Boots, [3009, 9999], [3158])).toBeCloseTo(one, 12);
  });

  it('has nothing to say without both sides in one state', () => {
    expect(sameStateEdge(boots, 163, Lane.Mid, SLOT.Boots, [3009], [9999])).toBeNull();
    expect(sameStateEdge(boots, 163, Lane.Mid, L, [3009], [3158])).toBeNull();
  });

  // Ahri's fourth to sixth item on 11.11.2026: Mejai's wins 73 % against Rabadon's 59 % over all
  // games, and still 57/84/30 % against 54/80/25 % within the states — with the players who were
  // ahead themselves. Bought 14 % of the time from ahead and 5 % from behind.
  const ahri: RiotSituationalFile = {
    ...file,
    rows: [
      [103, Lane.Mid, L, A, E, 0, 1060, 572],
      [103, Lane.Mid, L, A, AH, 0, 2242, 1816],
      [103, Lane.Mid, L, A, BH, 0, 1409, 352],
      [103, Lane.Mid, L, A, E, 3041, 91, 52],
      [103, Lane.Mid, L, A, AH, 3041, 313, 263],
      [103, Lane.Mid, L, A, BH, 3041, 73, 22],
      [103, Lane.Mid, L, A, E, 3089, 383, 207],
      [103, Lane.Mid, L, A, AH, 3089, 867, 694],
      [103, Lane.Mid, L, A, BH, 3089, 526, 132],
    ],
  };

  it('knows the items bought from ahead, over all champions', () => {
    expect([...boughtFromAhead(ahri)]).toEqual([3041]);
    expect([...boughtFromAhead(boots)]).toEqual([]);
    // Too few buys to take the ratio at its word.
    const thin: RiotSituationalFile = { ...ahri, rows: ahri.rows.map((r) => (r[5] === 3041 ? [...r.slice(0, 6), Math.round(r[6]! / 2), Math.round(r[7]! / 2)] : r)) };
    expect(thin.rows.filter((r) => r[5] === 3041).reduce((n, r) => n + r[6]!, 0)).toBeLessThan(WIN_MORE_MINIMUM_BUYS);
    expect([...boughtFromAhead(thin)]).toEqual([]);
  });

  it('never calls an item bought from ahead clear', () => {
    expect(sameStateEdge(ahri, 103, Lane.Mid, L, [3041], [3089])).toBeNull();
    // The other way round it is an ordinary comparison.
    expect(sameStateEdge(ahri, 103, Lane.Mid, L, [3089], [3041])).toBeLessThan(0);
  });
});

describe('a situational item in the plan\'s place', () => {
  // The Darius file from above: against magic damage Spirit Visage is 40 % of the late slot,
  // Sterak's 30 % (as everywhere), and item 7000 is never bought there.
  const suggestions = () => situationalSuggestions(file, meta, 1, TOP, [A, M], new Set());
  const option = (ids: number[]) => ({ ids, text: null, games: 100, winRate: 0.5 });
  const plan: RiotBuildPlan = {
    championId: 1,
    lane: TOP,
    inPlaceOf: null,
    patch: '16.22',
    games: 4000,
    winRate: 0.5,
    runes: null,
    spells: null,
    starters: [],
    boots: [option([3047])],
    core: [option([9001, 9002, 9003])],
    late: [option([3053]), option([7000])],
    supportChoice: null,
    supportOptions: [],
    skillOrder: '',
    skillMax: '',
    bootsAfterItems: null,
    bootsWorth: null,
  };
  const all = [SLOT.Boots, SLOT.Core, SLOT.Late];

  it('counts an item\'s share of its slot in the situation', () => {
    // 400 of 1.000 late buys against magic damage, and the 50 pseudo games of a class that is not there.
    expect(situationShare(file, meta, 1, TOP, L, M, 3065)).toEqual({ share: 400 / 1050, games: 1000 });
    expect(situationShare(file, meta, 1, TOP, L, M, 7000).share).toBe(0);
  });

  it('replaces the plan item players buy least there, when it is itself bought clearly more often', () => {
    const [rated] = rateSuggestions(file, meta, 1, TOP, suggestions(), (slot) => (slot === L ? [3053, 7000] : []));
    expect(rated).toMatchObject({ itemId: 3065, instead: 3053, adopt: true });
  });

  it('stays an alternative next to a plan item that is bought as often or more', () => {
    // Sterak's as the suggestion's rival is at 30 %; make the suggestion the rarer one.
    const rarer = suggestions().map((s) => ({ ...s, share: 0.2 }));
    expect(rateSuggestions(file, meta, 1, TOP, rarer, () => [3053])[0]).toMatchObject({ instead: 3053, adopt: false });
    // A hair more often is not clearly more often.
    const hair = suggestions().map((s) => ({ ...s, share: 0.3 }));
    expect(rateSuggestions(file, meta, 1, TOP, hair, () => [3053])[0]).toMatchObject({ instead: 3053, adopt: false });
  });

  it('never takes the place of an item without buys of its own in the situation, or of the first item late in a game', () => {
    // 7000 is never bought against magic damage: nothing to compare with, so nothing to replace.
    expect(rateSuggestions(file, meta, 1, TOP, suggestions(), () => [7000])[0]).toMatchObject({ instead: null, adopt: false });
    // A situation made of the enemies' items leaves the first item alone …
    const late = suggestions().map((s) => ({ ...s, situation: SITUATION.EnemyMagicResist }));
    const magicResist: RiotSituationalFile = { ...file, rows: file.rows.map((r) => (r[3] === M ? [...r.slice(0, 3), SITUATION.EnemyMagicResist, ...r.slice(4)] : r)) };
    expect(rateSuggestions(magicResist, meta, 1, TOP, late, () => [3053], 3053)[0]).toMatchObject({ instead: null, adopt: false });
    expect(rateSuggestions(magicResist, meta, 1, TOP, late, () => [3053], 9001)[0]).toMatchObject({ instead: 3053, adopt: true });
    // … a line-up known from the first minute does not.
    expect(rateSuggestions(file, meta, 1, TOP, suggestions(), () => [3053], 3053)[0]).toMatchObject({ instead: 3053, adopt: true });
  });

  it('gives each plan item to one suggestion only', () => {
    const twice = [...suggestions(), ...suggestions().map((s) => ({ ...s, itemId: 8888 }))];
    const rated = rateSuggestions(file, meta, 1, TOP, twice, () => [3053]);
    expect(rated.map((r) => [r.itemId, r.instead, r.adopt])).toEqual([[3065, 3053, true], [8888, null, false]]);
  });

  it('puts the swap into the plan and keeps it there for the game', () => {
    const tracker = new SwapTracker();
    const built = situationalBuild(file, meta, plan, 1, [A, M], [], () => false, all, tracker);
    expect(built.plan.late.map((o) => o.ids[0])).toEqual([3065, 7000]);
    expect(built.plan.boots[0]!.ids).toEqual([3047]);
    expect(built.swaps.map((s) => [s.itemId, s.instead])).toEqual([[3065, 3053]]);
    // The tile lists it as part of the build.
    expect(built.suggestions.map((s) => [s.itemId, s.adopt])).toEqual([[3065, true]]);
    // The base plan itself is untouched.
    expect(plan.late.map((o) => o.ids[0])).toEqual([3053, 7000]);

    // Bought: no suggestion any more, but the build does not fall back to Sterak's.
    const bought = situationalBuild(file, meta, plan, 1, [A, M], [3065], (id) => id === 3065, all, tracker);
    expect(bought.plan.late.map((o) => o.ids[0])).toEqual([3065, 7000]);
    expect(bought.suggestions).toEqual([]);
    expect(bought.swaps).toHaveLength(1);

    // Not bought, but the slot is filled otherwise (three other items, here: the late slot closed):
    // it stays in the plan and leaves the tile.
    const filled = situationalBuild(file, meta, plan, 1, [A, M], [], () => false, [SLOT.Boots, SLOT.Core], tracker);
    expect(filled.plan.late.map((o) => o.ids[0])).toEqual([3065, 7000]);
    expect(filled.suggestions).toEqual([]);

    // A new game starts from the base plan.
    tracker.reset();
    expect(situationalBuild(file, meta, plan, 1, [A], [], () => false, all, tracker).plan.late.map((o) => o.ids[0])).toEqual([3053, 7000]);
  });

  it('leaves a plan item alone once it is owned or its slot is closed', () => {
    // Sterak's is bought already: Spirit Visage stays an alternative with nothing to stand against.
    const owned = situationalBuild(file, meta, plan, 1, [A, M], [3053], (id) => id === 3053, all, new SwapTracker());
    expect(owned.plan.late.map((o) => o.ids[0])).toEqual([3053, 7000]);
    expect(owned.suggestions.map((s) => [s.itemId, s.instead, s.adopt])).toEqual([[3065, null, false]]);
  });

  it('swaps an item once: what was replaced or brought in is not swapped again', () => {
    const tracker = new SwapTracker();
    const [first] = rateSuggestions(file, meta, 1, TOP, suggestions(), () => [3053]);
    const swap = { ...first!, instead: 3053 };
    expect(tracker.update([swap])).toHaveLength(1);
    expect(tracker.update([{ ...swap, itemId: 8888 }])).toHaveLength(1); // Sterak's is gone already
    expect(tracker.update([{ ...swap, instead: 7000 }])).toHaveLength(1); // Spirit Visage is in already
    expect(tracker.update([{ ...swap, itemId: 8888, instead: 3065 }])).toHaveLength(1); // nothing replaces a situational item
    expect(applySwaps(plan, tracker.current).late.map((o) => o.ids[0])).toEqual([3065, 7000]);
    expect(applySwaps(plan, [])).toBe(plan);
  });
});
