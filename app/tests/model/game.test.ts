// The game moment from the Live Client Data API: next buy against gold, next skill point, enemies.

import { describe, expect, it } from 'vitest';
import { buyOrder, gameModel, remainingCost, type ItemInfo, type LiveGameData } from '../../src/core/game';
import { Lane } from '../../src/core/model/lane';
import { DamageType } from '../../src/core/model/metaLookup';
import type { RiotBuildPlan } from '../../src/core/riotBuilds';
import { MetaBuilder } from './helpers';

// Long Sword 350, Pickaxe 875, Caulfield's 1100 (from Long Sword ×2), Black Cleaver 3000
// (from Phage-like 1100 + Caulfield + Long Sword), Boots 300, Plated 1100, Mercury 1100.
const ITEMS = new Map<number, ItemInfo>([
  [1036, { total: 350, from: [] }],
  [1037, { total: 875, from: [] }],
  [3133, { total: 1100, from: [1036, 1036] }],
  [3044, { total: 1100, from: [1036] }],
  [3071, { total: 3000, from: [3044, 3133] }],
  [1001, { total: 300, from: [] }],
  [3047, { total: 1100, from: [1001] }],
  [3111, { total: 1100, from: [1001] }],
  [1055, { total: 450, from: [] }],
  // Tier-3 boots: built from a pair, not from 1001, and without the Boots tag (Data Dragon 16.22).
  [3174, { total: 1200, from: [3047], tags: ['Armor'] }],
  // Manamune turns into Muramana, which has no recipe of its own, only specialRecipe.
  [3004, { total: 2900, from: [3133] }],
  [3042, { total: 2900, from: [], purchasable: false, specialRecipe: 3004 }],
  // The support quest (Data Dragon 16.22): Bounty of Worlds, then a free pick of five final items.
  [3867, { total: 400, from: [], purchasable: false, into: [3876, 3869] }],
  [3876, { total: 400, from: [3867] }],
  [3869, { total: 400, from: [3867] }],
]);

const meta = new MetaBuilder()
  .champion(122, 'Darius', { damage: DamageType.Physical })
  .champion(24, 'Jax', { damage: DamageType.Mixed })
  .champion(103, 'Ahri', { damage: DamageType.Magic })
  .build();

const option = (ids: number[]) => ({ ids, text: null, games: 100, winRate: 0.5 });
const plan: RiotBuildPlan = {
  championId: 122,
  lane: Lane.Top,
  inPlaceOf: null,
  patch: '16.22',
  games: 1000,
  winRate: 0.51,
  runes: null,
  spells: null,
  starters: [option([1055])],
  boots: [option([3047])],
  core: [option([3071])],
  late: [],
  supportChoice: null,
  supportOptions: [],
  skillOrder: 'QEWQQRQEQERE',
  skillMax: 'QEW',
};

function game(overrides: { gold?: number; items?: number[]; time?: number; level?: number; abilities?: Partial<Record<'Q' | 'W' | 'E' | 'R', number>> } = {}): LiveGameData {
  const abilities = overrides.abilities ?? { Q: 1, W: 0, E: 1, R: 0 };
  return {
    activePlayer: {
      riotId: 'Ich#EUW',
      level: overrides.level ?? 2,
      currentGold: overrides.gold ?? 0,
      abilities: Object.fromEntries(Object.entries(abilities).map(([k, v]) => [k, { abilityLevel: v }])),
    },
    allPlayers: [
      { riotId: 'Ich#EUW', rawChampionName: 'game_character_displayname_Darius', team: 'ORDER', items: (overrides.items ?? []).map((itemID) => ({ itemID })) },
      { riotId: 'Fremd#1', rawChampionName: 'game_character_displayname_Jax', team: 'CHAOS', items: [{ itemID: 3047 }] },
      { riotId: 'Fremd#2', championName: 'Ahri', team: 'CHAOS', items: [] },
    ],
    gameData: { gameTime: overrides.time ?? 600 },
  };
}

describe('the support item upgrade', () => {
  const support = { ...plan, supportChoice: option([3876]), supportOptions: [option([3876]), option([3869])] };

  it('is no buy before the quest is done, though the path shows it', () => {
    const model = gameModel(game({ items: [3047], gold: 5000, time: 600 }), meta, support, ITEMS)!;
    expect(model.path.find((s) => s.slot === 'Support')).toEqual({ slot: 'Support', itemId: 3876, owned: false });
    expect(model.nextBuy!.itemId).toBe(3071);
    expect(model.nextBuy!.supportChoice).toBe(false);
  });

  it('comes first, at no cost, once Bounty of Worlds is in the inventory', () => {
    const model = gameModel(game({ items: [3047, 3867], gold: 50, time: 900 }), meta, support, ITEMS)!;
    expect(model.nextBuy).toEqual({ itemId: 3876, cost: 0, affordable: true, towards: null, supportChoice: true });
  });

  it('counts as done with any of the final items, not only the planned one', () => {
    const model = gameModel(game({ items: [3047, 3869], gold: 5000, time: 1200 }), meta, support, ITEMS)!;
    expect(model.path.find((s) => s.slot === 'Support')!.owned).toBe(true);
    expect(model.nextBuy!.itemId).toBe(3071);
  });
});

describe('items that changed shape', () => {
  it('upgraded tier-3 boots fill the boots slot and the planned pair counts as owned', () => {
    const model = gameModel(game({ items: [3174], gold: 5000, time: 900 }), meta, plan, ITEMS)!;
    expect(model.path.find((s) => s.slot === 'Schuhe')!.owned).toBe(true);
    expect(model.nextBuy!.itemId).toBe(3071);
  });

  it('a transformed item (Muramana) stands for the planned one (Manamune)', () => {
    const tear = { ...plan, boots: [], core: [option([3004])] };
    const model = gameModel(game({ items: [3042], gold: 5000, time: 900 }), meta, tear, ITEMS)!;
    expect(model.path.find((s) => s.itemId === 3004)!.owned).toBe(true);
    expect(model.nextBuy).toBeNull();
  });

  it('counts spent skill points for the grid', () => {
    expect(gameModel(game({ abilities: { Q: 3, W: 1, E: 2, R: 1 } }), meta, plan, ITEMS)!.skillPoints).toBe(7);
  });
});

describe('next buy', () => {
  it('subtracts owned components like the shop does', () => {
    expect(remainingCost(3071, [1036], ITEMS)).toBe(3000 - 350);
    expect(remainingCost(3071, [3133, 1036], ITEMS)).toBe(3000 - 1100 - 350);
  });

  it('offers the whole item when the gold is there', () => {
    const model = gameModel(game({ gold: 3200, items: [3047] }), meta, plan, ITEMS)!;
    expect(model.nextBuy).toEqual({ itemId: 3071, cost: 3000, affordable: true, towards: null, supportChoice: false });
  });

  it('else the most expensive component that is affordable', () => {
    // Two components at 1.100: the first in the recipe wins. With 500 gold only a Long Sword fits.
    expect(gameModel(game({ gold: 1150, items: [3047] }), meta, plan, ITEMS)!.nextBuy).toEqual({
      itemId: 3044,
      cost: 1100,
      affordable: true,
      towards: 3071,
      supportChoice: false,
    });
    expect(gameModel(game({ gold: 500, items: [3047] }), meta, plan, ITEMS)!.nextBuy).toEqual({
      itemId: 1036,
      cost: 350,
      affordable: true,
      towards: 3071,
      supportChoice: false,
    });
    expect(gameModel(game({ gold: 100, items: [3047] }), meta, plan, ITEMS)!.nextBuy?.affordable).toBe(false);
  });

  it('counts other finished boots as the boots slot', () => {
    const model = gameModel(game({ gold: 5000, items: [3111] }), meta, plan, ITEMS)!;
    expect(model.path.find((s) => s.slot === 'Schuhe')?.owned).toBe(true);
    expect(model.nextBuy?.itemId).toBe(3071);
  });

  it('builds the first core item before the finished boots, and the boots before the second', () => {
    const two = { ...plan, core: [option([3071, 3004])] };
    // First recall, 1.150 gold, no boots: a part of the first item, not a pair of boots.
    expect(gameModel(game({ gold: 1150 }), meta, two, ITEMS)!.nextBuy).toEqual({ itemId: 3044, cost: 1100, affordable: true, towards: 3071, supportChoice: false });
    // Basic boots bought on the way change nothing about the target.
    expect(gameModel(game({ gold: 1150, items: [1001] }), meta, two, ITEMS)!.nextBuy?.towards).toBe(3071);
    // The first item is done: now the pair, then the second item.
    expect(gameModel(game({ gold: 1200, items: [3071] }), meta, two, ITEMS)!.nextBuy?.itemId).toBe(3047);
    expect(gameModel(game({ gold: 3000, items: [3071, 3047] }), meta, two, ITEMS)!.nextBuy?.itemId).toBe(3004);
    // The path still shows the boots as a slot of their own, first.
    expect(gameModel(game({ gold: 0 }), meta, two, ITEMS)!.path.map((s) => s.slot)).toEqual(['Schuhe', 'Kern', 'Kern']);
  });

  it('keeps the order of a plan without boots or without a core', () => {
    const steps = [
      { slot: 'Schuhe' as const, itemId: 3047, owned: false },
      { slot: 'Spät' as const, itemId: 3071, owned: false },
    ];
    expect(buyOrder(steps)).toEqual(steps);
    expect(buyOrder([steps[1]!])).toEqual([steps[1]!]);
  });

  it('suggests the start only in the first minutes', () => {
    expect(gameModel(game({ time: 10, gold: 500 }), meta, plan, ITEMS)!.nextBuy?.itemId).toBe(1055);
    expect(gameModel(game({ time: 600 }), meta, plan, ITEMS)!.path.some((s) => s.slot === 'Start')).toBe(false);
  });
});

describe('next skill point', () => {
  it('is the first point the plan has that the game does not', () => {
    // Plan QEW…, game has Q and E at level 2: next is W at level 3, not yet spendable.
    expect(gameModel(game(), meta, plan, ITEMS)!.nextSkill).toEqual({ key: 'W', level: 3, now: false });
  });

  it('says "now" when a point is unspent', () => {
    expect(gameModel(game({ level: 3 }), meta, plan, ITEMS)!.nextSkill).toEqual({ key: 'W', level: 3, now: true });
  });
});

describe('enemies', () => {
  it('lists champions, items and damage, without any name', () => {
    const model = gameModel(game(), meta, plan, ITEMS)!;
    expect(model.championId).toBe(122);
    expect(model.enemies).toEqual([
      { championId: 24, items: [3047], damage: DamageType.Mixed },
      { championId: 103, items: [], damage: DamageType.Magic },
    ]);
    expect(model.enemyMagic).toBeCloseTo(0.75, 12);
    expect(JSON.stringify(model)).not.toContain('Fremd');
  });

  it('has nothing to say without the own row', () => {
    const data = game();
    data.activePlayer!.riotId = 'Jemand#X';
    expect(gameModel(data, meta, plan, ITEMS)).toBeNull();
  });
});
