// The game moment as the game area shows it: what the hint and the buy box say beside the build.

import { describe, expect, it } from 'vitest';
import type { GameModel } from '../../src/core/game';
import { Lane } from '../../src/core/model/lane';
import { LANE_PHASE_SECONDS, type Strength, type TeamStand } from '../../src/core/strength';
import type { VerdictLine } from '../../src/core/view';
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
  enemyDamageBy: 'amount',
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
      { title: 'Magieresistenz lohnt sich', detail: 'Etwa 70 % des Schadens, den diese Gegner im Schnitt machen, ist magisch. In deinem Build steht dafür: #2504.', tone: 'info' },
    ]);
    expect(view(model({ answerItem: null })).hints[0]!.detail).toBe('Etwa 70 % des Schadens, den diese Gegner im Schnitt machen, ist magisch.');
    expect(view(model({ enemyDamage: 'physical', enemyPhysical: 0.8, enemyMagic: 0.2, answerItem: 3075 })).hints[0]).toMatchObject({
      title: 'Rüstung lohnt sich',
      detail: 'Etwa 80 % des Schadens, den diese Gegner im Schnitt machen, ist physisch. In deinem Build steht dafür: #3075.',
    });
    // A file without damage amounts (OP.GG) counts heads, and says so instead of calling it damage.
    expect(view(model({ enemyDamageBy: 'heads', answerItem: null })).hints[0]!.detail).toBe('Etwa 70 % der Gegner machen vor allem magischen Schaden.');
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

describe('after the lane phase', () => {
  const scale: Strength = {
    lane: Lane.Support,
    mine: { championIds: [201], levels: [5], worth: 1200 },
    theirs: { championIds: [1], levels: [5], worth: 900 },
    levelDiff: 0,
    worthDiff: 300,
    chance: 0.55,
    value: 0.4,
  };
  const stand: TeamStand = {
    mine: { players: 5, level: 62, worth: 41200 },
    theirs: { players: 5, level: 58, worth: 38900 },
    levelDiff: 4,
    worthDiff: 2300,
    enemies: [1, 2, 3, 4, 5].map((championId) => ({ championId, level: 10 + championId, worth: championId === 3 ? 12000 : 6000 })),
    leaderId: 3,
  };
  const verdict: VerdictLine[] = [
    { icon: 'scaling', tone: 'warn', title: 'Gegner skalieren besser', detail: 'Früh Druck machen, Objectives nehmen.' },
    { icon: 'damage', tone: 'info', title: 'Viel AP beim Gegner', detail: 'MR zuerst lohnt sich.' },
  ];
  const at = (gameTime: number, strength: Strength | null = scale, team: TeamStand | null = stand) =>
    gameView(model({ gameTime }), () => null, 'Braum', null, null, strength, [], { team, verdict });

  it('shows the lane scale during the lane phase and the team stand after it', () => {
    const lane = at(LANE_PHASE_SECONDS - 1);
    expect(lane.strength).not.toBeNull();
    expect(lane.team).toBeNull();
    const later = at(LANE_PHASE_SECONDS);
    expect(later.strength).toBeNull();
    expect(later.team!.rows.map((r) => [r.label, r.mine, r.theirs, r.diff])).toEqual([
      ['Level', '62', '58', '+4'],
      ['Items', '41.200', '38.900', '+2.300'],
    ]);
    // Without a lane opponent the team stand stands in from the start; without either, nothing.
    expect(at(300, null).team).not.toBeNull();
    expect(at(300, null, null)).toMatchObject({ strength: null, team: null });
  });

  it('gives every enemy his level and item worth, and marks the one with the most items once the lanes are over', () => {
    const lane = at(600).enemies;
    expect(lane.map((e) => e.stand)).toEqual([1, 2, 3, 4, 5].map((id) => ({ level: 10 + id, worth: id === 3 ? 12000 : 6000 })));
    expect(lane.some((e) => e.lead)).toBe(false);
    expect(at(1500).enemies.map((e) => e.lead)).toEqual([false, false, true, false, false]);
    // No team stand, no numbers and no mark.
    expect(at(1500, null, null).enemies.every((e) => e.stand === null && !e.lead)).toBe(true);
  });

  it('keeps the draft\'s verdict through the game, without the damage line the hint already says', () => {
    expect(at(600).verdict.map((line) => line.title)).toEqual(['Gegner skalieren besser']);
    expect(view(model()).verdict).toEqual([]);
  });
});
