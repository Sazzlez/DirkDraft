// The game moment from the Live Client Data API: next buy against gold, next skill point, enemies.

import { describe, expect, it } from 'vitest';
import { gameModel, remainingCost, type ItemInfo, type LiveGameData } from '../../src/core/game';
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

describe('next buy', () => {
  it('subtracts owned components like the shop does', () => {
    expect(remainingCost(3071, [1036], ITEMS)).toBe(3000 - 350);
    expect(remainingCost(3071, [3133, 1036], ITEMS)).toBe(3000 - 1100 - 350);
  });

  it('offers the whole item when the gold is there', () => {
    const model = gameModel(game({ gold: 3200, items: [3047] }), meta, plan, ITEMS)!;
    expect(model.nextBuy).toEqual({ itemId: 3071, cost: 3000, affordable: true, towards: null });
  });

  it('else the most expensive component that is affordable', () => {
    // Two components at 1.100: the first in the recipe wins. With 500 gold only a Long Sword fits.
    expect(gameModel(game({ gold: 1150, items: [3047] }), meta, plan, ITEMS)!.nextBuy).toEqual({
      itemId: 3044,
      cost: 1100,
      affordable: true,
      towards: 3071,
    });
    expect(gameModel(game({ gold: 500, items: [3047] }), meta, plan, ITEMS)!.nextBuy).toEqual({
      itemId: 1036,
      cost: 350,
      affordable: true,
      towards: 3071,
    });
    expect(gameModel(game({ gold: 100, items: [3047] }), meta, plan, ITEMS)!.nextBuy?.affordable).toBe(false);
  });

  it('counts other finished boots as the boots slot', () => {
    const model = gameModel(game({ gold: 5000, items: [3111] }), meta, plan, ITEMS)!;
    expect(model.path.find((s) => s.slot === 'Schuhe')?.owned).toBe(true);
    expect(model.nextBuy?.itemId).toBe(3071);
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
