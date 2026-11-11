// The game moment from the Live Client Data API: next buy against gold, next skill point, enemies.

import { describe, expect, it } from 'vitest';
import { buyOrder, gameModel, itemWorth, remainingCost, type ItemInfo, type LiveGameData } from '../../src/core/game';
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
  // What an inventory's worth leaves out: a potion and the trinket.
  [2003, { total: 50, from: [], tags: ['Consumable'] }],
  [3340, { total: 0, from: [], tags: ['Trinket', 'Vision'] }],
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
  bootsAfterItems: null,
  bootsWorth: null,
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

  it('puts the boots where the build file counted them, after the first item where it did not', () => {
    const step = (slot: 'Start' | 'Schuhe' | 'Support' | 'Kern' | 'Spät', itemId: number) => ({ slot, itemId, owned: false });
    const steps = [step('Start', 1055), step('Schuhe', 3047), step('Kern', 1), step('Kern', 2), step('Kern', 3), step('Spät', 4)];
    const order = (after: number | null) => buyOrder(steps, after).map((s) => s.itemId);
    expect(order(null)).toEqual([1055, 1, 3047, 2, 3, 4]);
    // A boots rush: before the first item, never before the start.
    expect(order(0)).toEqual([1055, 3047, 1, 2, 3, 4]);
    expect(order(1)).toEqual([1055, 1, 3047, 2, 3, 4]);
    expect(order(2)).toEqual([1055, 1, 2, 3047, 3, 4]);
    expect(order(3)).toEqual([1055, 1, 2, 3, 3047, 4]);
    // Late items count as items too; more than the plan holds puts the boots last.
    expect(buyOrder([step('Schuhe', 3047), step('Spät', 4)], 3).map((s) => s.itemId)).toEqual([4, 3047]);
    // Nothing to order without boots or without an item.
    expect(buyOrder([step('Kern', 1), step('Kern', 2)], 1).map((s) => s.itemId)).toEqual([1, 2]);
    expect(buyOrder([step('Start', 1055), step('Schuhe', 3047)], 1).map((s) => s.itemId)).toEqual([1055, 3047]);
  });

  it('suggests the start only in the first minutes', () => {
    expect(gameModel(game({ time: 10, gold: 500 }), meta, plan, ITEMS)!.nextBuy?.itemId).toBe(1055);
    expect(gameModel(game({ time: 600 }), meta, plan, ITEMS)!.path.some((s) => s.slot === 'Start')).toBe(false);
  });
});

describe('the shopping list', () => {
  const buys = (overrides: Parameters<typeof game>[0], p: RiotBuildPlan = plan) => {
    const model = gameModel(game(overrides), meta, p, ITEMS)!;
    return { list: model.basket.map((b) => [b.itemId, b.cost, b.towards]), left: model.goldLeft, next: model.nextBuy };
  };
  // Half the players have their first boots while they hold the starting items alone (step "0").
  const early = { ...plan, bootsWorth: 0 };
  // … or only once they hold 1.000 gold of items.
  const later = { ...plan, bootsWorth: 1000 };

  it('spends the gold at hand on several buys: 1.500 gold are a Phage and a Long Sword', () => {
    expect(buys({ gold: 1500, items: [3047] })).toMatchObject({ list: [[3044, 1100, 3071], [1036, 350, 3071]], left: 50 });
    // The whole item, and nothing after the plan's last one.
    expect(buys({ gold: 3200, items: [3047] })).toMatchObject({ list: [[3071, 3000, null]], left: 200 });
    // Nothing fits: no list, and the next buy is the target the gold does not reach.
    expect(buys({ gold: 100, items: [3047] })).toMatchObject({ list: [], left: 100, next: { itemId: 3071, affordable: false } });
  });

  it('buys Boots and a Long Sword with 650 gold once the boots are due — not one of them', () => {
    // Doran's Blade alone (450): the step in which half the players buy their first boots.
    expect(buys({ gold: 650, items: [1055] }, early)).toMatchObject({
      list: [[1001, 300, 3047], [1036, 350, 3071]],
      left: 0,
      next: { itemId: 1001, cost: 300, affordable: true, towards: 3047 },
    });
    // Boots come first when the gold covers only one of the two.
    expect(buys({ gold: 400, items: [1055] }, early).list).toEqual([[1001, 300, 3047]]);
  });

  it('leaves the boots for later while the items held are worth less than when players buy them', () => {
    expect(buys({ gold: 650, items: [1055] }, later)).toMatchObject({ list: [[1036, 350, 3071]], left: 300 });
    // With a Phage in the bag (450 + 1.100) they are due.
    expect(buys({ gold: 700, items: [1055, 3044] }, later)).toMatchObject({ list: [[1001, 300, 3047], [1036, 350, 3071]], left: 50 });
  });

  it('buys Boots with what is left where the file does not say when: parts first, then the rest', () => {
    expect(buys({ gold: 650, items: [1055] })).toMatchObject({ list: [[1036, 350, 3071], [1001, 300, 3047]], left: 0 });
    // The part comes first: 400 gold are a Long Sword, not Boots.
    expect(buys({ gold: 400, items: [1055] }).list).toEqual([[1036, 350, 3071]]);
    // Nothing of the item fits, the Boots do.
    expect(buys({ gold: 320, items: [1055] })).toMatchObject({ list: [[1001, 300, 3047]], next: { itemId: 1001, towards: 3047 } });
    // A plan without boots has none to add.
    expect(buys({ gold: 650, items: [1055] }, { ...plan, boots: [] }).list).toEqual([[1036, 350, 3071]]);
  });

  it('adds no boots to a pair, to basic boots or before the starting items', () => {
    expect(buys({ gold: 650, items: [1055, 3111] }, early).list).toEqual([[1036, 350, 3071]]);
    expect(buys({ gold: 650, items: [1055, 1001] }, early).list).toEqual([[1036, 350, 3071]]);
    // In the fountain the starting item comes first; 50 gold are left, not a pair of Boots.
    expect(buys({ gold: 500, time: 10 }, early)).toMatchObject({ list: [[1055, 450, null]], left: 50 });
  });

  it('finishes the pair when its turn comes, with the basic boots counted in', () => {
    // First item done: the pair is next, and the Boots bought earlier are 300 of its 1.100.
    expect(buys({ gold: 900, items: [3071, 1001] }, early).list).toEqual([[3047, 800, null]]);
  });

  it('counts parts by instance: one Long Sword owned leaves the recipe\'s other ones to buy', () => {
    // The Phage takes the Long Sword in the bag; the Warhammer still needs two.
    expect(buys({ gold: 400, items: [3047, 1036] }).list).toEqual([[1036, 350, 3071]]);
  });

  it('values the inventory as the collector does', () => {
    // Doran's Blade and a Long Sword; potion, trinket and the support quest are worth nothing.
    expect(itemWorth([1055, 2003, 2003, 3340, 3867, 3876, 1036], ITEMS)).toBe(800);
    expect(itemWorth([], ITEMS)).toBe(0);
    expect(itemWorth([999999], ITEMS)).toBe(0);
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
