// The game moment as the game area shows it: what the hint and the buy box say beside the build.

import { describe, expect, it } from 'vitest';
import type { GameModel } from '../../src/core/game';
import { SITUATION, SLOT, type RatedSuggestion } from '../../src/core/situational';
import { DamageType } from '../../src/core/model/metaLookup';
import { gameView } from '../../src/data/gameView';

// Braum on 15.11.2026: Steelcaps planned, three of five enemies deal magic damage.
const model = (overrides: Partial<GameModel> = {}): GameModel => ({
  gameTime: 300,
  level: 4,
  gold: 212,
  championId: 201,
  path: [
    { slot: 'Schuhe', itemId: 3047, owned: false },
    { slot: 'Kern', itemId: 3190, owned: false },
    { slot: 'Spät', itemId: 3075, owned: false },
    { slot: 'Spät', itemId: 2504, owned: false },
  ],
  nextBuy: { itemId: 3047, cost: 600, affordable: false, towards: null, supportChoice: false },
  basket: [],
  goldLeft: 212,
  nextSkill: null,
  skillPoints: 4,
  enemies: [1, 2, 3, 4, 5].map((championId) => ({ championId, items: [], damage: championId <= 3 ? DamageType.Magic : championId === 4 ? DamageType.Mixed : DamageType.Physical })),
  enemyPhysical: 0.3,
  enemyMagic: 0.7,
  enemyDamage: 'magic',
  answerItem: 2504,
  ...overrides,
});

const mercs: RatedSuggestion = { itemId: 3111, slot: SLOT.Boots, situation: SITUATION.Magic, timesMoreOften: 2.4, winRate: 0.51, otherRate: 0.5, games: 16391, share: 0.37, instead: 3047, adopt: false };
const view = (m: GameModel, situational: RatedSuggestion[] | null = [mercs]) => gameView(m, () => null, 'Braum', null, situational);

describe('the damage hint', () => {
  it('names the item of the build that answers it', () => {
    // Without loaded assets an item's name is its number.
    expect(view(model()).hints).toEqual([
      { title: 'Magieresistenz lohnt sich', detail: 'Etwa 70 % des Schadens im Gegnerteam ist magisch. In deinem Build steht dafür: #2504.', tone: 'info' },
    ]);
    expect(view(model({ answerItem: null })).hints[0]!.detail).toBe('Etwa 70 % des Schadens im Gegnerteam ist magisch.');
    expect(view(model({ enemyDamage: 'physical', enemyPhysical: 0.8, enemyMagic: 0.2, answerItem: 3075 })).hints[0]).toMatchObject({
      title: 'Rüstung lohnt sich',
      detail: 'Etwa 80 % des Schadens im Gegnerteam ist physisch. In deinem Build steht dafür: #3075.',
    });
    expect(view(model({ enemyDamage: null, answerItem: null })).hints).toEqual([]);
  });
});

describe('a late item brought forward', () => {
  it('leads the situational tile, with the item it stands before and the buys it rests on', () => {
    const move = { itemId: 2504, before: 3075, situation: SITUATION.Magic, share: 0.31, otherShare: 0.17, games: 248 };
    const tile = gameView(model(), () => null, 'Braum', null, [mercs], null, [move]).situational!;
    expect(tile[0]).toEqual({
      item: { id: 2504, name: '#2504' },
      instead: { id: 3075, name: '#3075' },
      adopted: true,
      moved: true,
      reason: 'Gegen viel magischen Schaden',
      stats: '31 % der späten Käufe, #3075 17 % · 248 Käufe',
    });
    expect(tile[1]).toMatchObject({ item: { id: 3111 }, moved: false, adopted: false });
    // Shown even where the file has no other suggestion to make; without either the tile stays away.
    expect(gameView(model(), () => null, 'Braum', null, null, null, [move]).situational).toHaveLength(1);
    expect(view(model(), null).situational).toBeNull();
  });
});

describe('the alternative in the buy box', () => {
  it('stands beside the item the list is buying towards', () => {
    expect(view(model()).buyAlternative).toEqual({ item: { id: 3111, name: '#3111' }, instead: { id: 3047, name: '#3047' }, reason: 'gegen viel magischen Schaden' });
    // Also while only a part of it is affordable.
    const part = model({ nextBuy: { itemId: 1001, cost: 300, affordable: true, towards: 3047, supportChoice: false } });
    expect(view(part).buyAlternative!.item.id).toBe(3111);
  });

  it('is silent for other buys, for an item already in the build and without situational data', () => {
    expect(view(model({ nextBuy: { itemId: 3190, cost: 2200, affordable: false, towards: null, supportChoice: false } })).buyAlternative).toBeNull();
    expect(view(model(), [{ ...mercs, adopt: true }]).buyAlternative).toBeNull();
    expect(view(model(), [{ ...mercs, instead: null }]).buyAlternative).toBeNull();
    expect(view(model(), null).buyAlternative).toBeNull();
    expect(view(model({ nextBuy: null })).buyAlternative).toBeNull();
  });
});
