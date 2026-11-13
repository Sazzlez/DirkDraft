// The strength scale: you against your lane opponent, from the levels and items on the scoreboard
// (docs/lane-vergleich.md). The weights are measured (docs/modellpruefung.md, Messung 27); the tests
// pin how the scale reads the game, with weights of their own where the number matters.

import { describe, expect, it } from 'vitest';
import type { ItemInfo, LiveGameData, LivePlayer } from '../../src/core/game';
import { Lane } from '../../src/core/model/lane';
import { DamageType } from '../../src/core/model/metaLookup';
import { STRENGTH_STEP, STRENGTH_WEIGHTS, strengthOf } from '../../src/core/strength';
import { strengthLabel, strengthView } from '../../src/data/gameView';
import { MetaBuilder } from './helpers';

const ITEMS = new Map<number, ItemInfo>([
  [1036, { total: 350, from: [] }],
  [1055, { total: 450, from: [] }],
  [3133, { total: 1100, from: [1036, 1036] }],
  [3071, { total: 3000, from: [3133] }],
  [1001, { total: 300, from: [] }],
  // What the worth leaves out: a potion, the trinket and the support quest.
  [2003, { total: 50, from: [], tags: ['Consumable'] }],
  [3340, { total: 0, from: [], tags: ['Trinket', 'Vision'] }],
  [3865, { total: 400, from: [] }],
]);

const meta = new MetaBuilder()
  .champion(122, 'Darius', { damage: DamageType.Physical })
  .champion(24, 'Jax', { damage: DamageType.Mixed })
  .champion(103, 'Ahri', { damage: DamageType.Magic })
  .champion(222, 'Jinx', { damage: DamageType.Physical })
  .champion(412, 'Thresh', { damage: DamageType.Magic })
  .champion(51, 'Caitlyn', { damage: DamageType.Physical })
  .champion(89, 'Leona', { damage: DamageType.Magic })
  .build();

const row = (name: string, team: string, position: string | undefined, level: number, items: number[], riotId = `Fremd#${name}`): LivePlayer => ({
  riotId,
  championName: name,
  team,
  position,
  level,
  items: items.map((itemID) => ({ itemID })),
});

function top(mine: { level: number; items: number[]; position?: string }, jax: { level: number; items: number[]; position?: string }): LiveGameData {
  return {
    activePlayer: { riotId: 'Ich#EUW', level: mine.level, currentGold: 2000 },
    allPlayers: [
      row('Darius', 'ORDER', 'position' in mine ? mine.position : 'TOP', mine.level, mine.items, 'Ich#EUW'),
      row('Ahri', 'ORDER', 'MIDDLE', 9, [3071]),
      row('Jax', 'CHAOS', 'position' in jax ? jax.position : 'TOP', jax.level, jax.items),
      row('Ahri', 'CHAOS', 'MIDDLE', 3, []),
    ],
    gameData: { gameTime: 600 },
  };
}

describe('against the lane opponent', () => {
  it('compares level and item worth with the enemy on the same position', () => {
    const s = strengthOf(top({ level: 9, items: [3133, 1036, 1001] }, { level: 8, items: [1055, 1036] }), meta, ITEMS)!;
    expect(s.lane).toBe(Lane.Top);
    expect(s.mine).toEqual({ championIds: [122], levels: [9], worth: 1750 });
    expect(s.theirs).toEqual({ championIds: [24], levels: [8], worth: 800 });
    expect(s.levelDiff).toBe(1);
    expect(s.worthDiff).toBe(950);

    const w = STRENGTH_WEIGHTS.top;
    const logOdds = w.level + w.gold * 0.95;
    expect(s.chance).toBeCloseTo(1 / (1 + Math.exp(-logOdds)), 12);
    expect(s.value).toBeCloseTo(Math.min(3, logOdds / STRENGTH_STEP), 12);
  });

  it('leaves out what the collector leaves out: potions, the trinket, the support quest — and never the gold in the pocket', () => {
    const plain = strengthOf(top({ level: 5, items: [1055] }, { level: 5, items: [1055] }), meta, ITEMS)!;
    const withExtras = strengthOf(top({ level: 5, items: [1055, 2003, 2003, 3340, 3865] }, { level: 5, items: [1055] }), meta, ITEMS)!;
    expect(withExtras.mine.worth).toBe(450);
    expect(withExtras.value).toBe(plain.value);
    expect(plain.value).toBe(0);
    expect(plain.chance).toBe(0.5);
  });

  it('counts a stack of the same item once per piece', () => {
    const data = top({ level: 5, items: [] }, { level: 5, items: [] });
    data.allPlayers![0]!.items = [{ itemID: 1036, count: 2 }];
    expect(strengthOf(data, meta, ITEMS)!.mine.worth).toBe(700);
  });

  it('stops at three steps either way', () => {
    const ahead = strengthOf(top({ level: 16, items: [3071, 3071, 3071] }, { level: 9, items: [] }), meta, ITEMS)!;
    const behind = strengthOf(top({ level: 9, items: [] }, { level: 16, items: [3071, 3071, 3071] }), meta, ITEMS)!;
    expect(ahead.value).toBe(3);
    expect(behind.value).toBe(-3);
    expect(ahead.chance + behind.chance).toBeCloseTo(1, 12);
  });

  it('is the mirror image from the other side', () => {
    const weights = { top: { level: 0.4, gold: 0.5 }, jungle: { level: 0, gold: 0 }, mid: { level: 0, gold: 0 }, bot: { level: 0, gold: 0 } };
    const mine = strengthOf(top({ level: 7, items: [1036] }, { level: 6, items: [1055] }), meta, ITEMS, null, weights)!;
    const theirs = strengthOf(top({ level: 6, items: [1055] }, { level: 7, items: [1036] }), meta, ITEMS, null, weights)!;
    expect(mine.value).toBeCloseTo(-theirs.value, 12);
    expect(mine.value).toBeCloseTo((0.4 - 0.5 * 0.1) / STRENGTH_STEP, 12);
  });
});

describe('who the opponent is', () => {
  it('falls back to the draft\'s lane opponent where the game gives no positions', () => {
    const data = top({ level: 6, items: [], position: undefined }, { level: 6, items: [1036], position: 'NONE' });
    for (const p of data.allPlayers!) p.position = '';
    expect(strengthOf(data, meta, ITEMS)).toBeNull();
    const s = strengthOf(data, meta, ITEMS, { lane: Lane.Top, opponentId: 24 })!;
    expect(s.theirs.championIds).toEqual([24]);
    expect(s.lane).toBe(Lane.Top);
    expect(s.worthDiff).toBe(-350);
  });

  it('says nothing when two enemies claim the lane, or none', () => {
    const two = top({ level: 6, items: [] }, { level: 6, items: [] });
    two.allPlayers![3]!.position = 'TOP';
    expect(strengthOf(two, meta, ITEMS)).toBeNull();
    const none = top({ level: 6, items: [] }, { level: 6, items: [], position: 'JUNGLE' });
    expect(strengthOf(none, meta, ITEMS)).toBeNull();
    // The draft knows better: Jax was the lane opponent.
    expect(strengthOf(none, meta, ITEMS, { lane: Lane.Top, opponentId: 24 })!.theirs.championIds).toEqual([24]);
  });

  it('says nothing without the own row', () => {
    const data = top({ level: 6, items: [] }, { level: 6, items: [] });
    data.activePlayer!.riotId = 'Jemand#Anders';
    expect(strengthOf(data, meta, ITEMS)).toBeNull();
  });
});

describe('the bot lane, two against two', () => {
  const bot = (support: string | undefined = 'UTILITY'): LiveGameData => ({
    activePlayer: { riotId: 'Ich#EUW', level: 7 },
    allPlayers: [
      // The scoreboard lists the support first here: the scale still names the ADC first.
      row('Thresh', 'ORDER', 'UTILITY', 7, [3865], 'Ich#EUW'),
      row('Jinx', 'ORDER', 'BOTTOM', 8, [3133, 1001]),
      row('Darius', 'ORDER', 'TOP', 10, [3071]),
      row('Leona', 'CHAOS', support, 6, [3865, 1001]),
      row('Caitlyn', 'CHAOS', 'BOTTOM', 8, [3133]),
      row('Jax', 'CHAOS', 'TOP', 11, [3071, 3071]),
    ],
    gameData: { gameTime: 720 },
  });

  it('sums both players of each side, ADC first', () => {
    const s = strengthOf(bot(), meta, ITEMS)!;
    expect(s.lane).toBe(Lane.Adc);
    expect(s.mine).toEqual({ championIds: [222, 412], levels: [8, 7], worth: 1400 });
    expect(s.theirs).toEqual({ championIds: [51, 89], levels: [8, 6], worth: 1400 });
    expect(s.levelDiff).toBe(1);
    expect(s.worthDiff).toBe(0);
    expect(s.value).toBeCloseTo(STRENGTH_WEIGHTS.bot.level / STRENGTH_STEP, 12);
  });

  it('needs all four positions — the draft\'s single opponent is no two against two', () => {
    expect(strengthOf(bot(''), meta, ITEMS)).toBeNull();
    expect(strengthOf(bot(''), meta, ITEMS, { lane: Lane.Support, opponentId: 89 })).toBeNull();
  });
});

describe('as the tile shows it', () => {
  const champion = (id: number) => ({ id, key: `K${id}`, name: meta.champion(id)?.name ?? `#${id}` });

  it('names the step, never an action', () => {
    expect([0, 0.49, -0.49].map(strengthLabel)).toEqual(['Ausgeglichen', 'Ausgeglichen', 'Ausgeglichen']);
    expect([0.5, 1.49, -0.5].map(strengthLabel)).toEqual(['Leicht stärker', 'Leicht stärker', 'Leicht schwächer']);
    expect([1.5, -2.49].map(strengthLabel)).toEqual(['Stärker', 'Schwächer']);
    expect([2.5, 3, -3].map(strengthLabel)).toEqual(['Deutlich stärker', 'Deutlich stärker', 'Deutlich schwächer']);
  });

  it('shows the opponent\'s champion and what the scale is made of', () => {
    const view = strengthView(strengthOf(top({ level: 9, items: [3133, 1036, 1001] }, { level: 10, items: [1055, 1036] }), meta, ITEMS)!, champion);
    expect(view.opponents).toEqual([{ id: 24, key: 'K24', name: 'Jax' }]);
    expect(view.rows).toEqual([
      { label: 'Level', mine: '9', theirs: '10', diff: '−1', tone: 'down' },
      { label: 'Items', mine: '1.750', theirs: '800', diff: '+950', tone: 'up' },
    ]);
    expect(view.phases).toBeNull();
    // Nothing of a player in it: only champions, numbers and the step.
    expect(JSON.stringify(view)).not.toMatch(/Ich#EUW|Fremd#/);
  });

  it('writes the bot lane as two and two', () => {
    const data: LiveGameData = {
      activePlayer: { riotId: 'Ich#EUW', level: 8 },
      allPlayers: [
        row('Jinx', 'ORDER', 'BOTTOM', 8, [1036], 'Ich#EUW'),
        row('Thresh', 'ORDER', 'UTILITY', 7, []),
        row('Caitlyn', 'CHAOS', 'BOTTOM', 8, [1036]),
        row('Leona', 'CHAOS', 'UTILITY', 7, []),
      ],
    };
    const view = strengthView(strengthOf(data, meta, ITEMS)!, champion);
    expect(view.opponents.map((o) => o.name)).toEqual(['Caitlyn', 'Leona']);
    expect(view.rows[0]).toEqual({ label: 'Level', mine: '8 + 7', theirs: '8 + 7', diff: '±0', tone: 'even' });
    expect(view.rows[1]).toEqual({ label: 'Items', mine: '350', theirs: '350', diff: '±0', tone: 'even' });
    expect(view.label).toBe('Ausgeglichen');
  });
});
