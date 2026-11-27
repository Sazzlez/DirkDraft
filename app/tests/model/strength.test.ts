// The strength scale: you against your lane opponent, from the levels and items on the scoreboard
// (docs/lane-vergleich.md). The weights are measured per lane and phase (docs/modellpruefung.md,
// Messung 35), the champions' strengths come with meta.json; the tests
// pin how the scale reads the game, with weights of their own where the number matters.

import { describe, expect, it } from 'vitest';
import type { ItemInfo, LiveGameData, LivePlayer } from '../../src/core/game';
import { Lane } from '../../src/core/model/lane';
import { DamageType, MetaLookup, type LaneScaleFile, type TeamfightScaleFile } from '../../src/core/model/metaLookup';
import { STRENGTH_STEP, STRENGTH_WEIGHTS, laneScaleOf, strengthOf, teamBuffsOf, teamStandOf, teamStrengthOf, type LaneScale } from '../../src/core/strength';
import { strengthLabel, strengthView, teamfightOdds, teamStandView, teamStrengthView } from '../../src/data/gameView';
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

    // Level 9 and 8: a mean of 8,5, the middle phase.
    expect(s.phase).toBe(1);
    expect(s.duel).toBeNull(); // no strengths in this meta
    const w = STRENGTH_WEIGHTS[0]![1]!;
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
    const flat = { level: 0.4, gold: 0.5 };
    const weights: LaneScale = { weights: [[flat, flat, flat], [], [], []], strength: () => 0, measured: false };
    const mine = strengthOf(top({ level: 7, items: [1036] }, { level: 6, items: [1055] }), meta, ITEMS, null, weights)!;
    const theirs = strengthOf(top({ level: 6, items: [1055] }, { level: 7, items: [1036] }), meta, ITEMS, null, weights)!;
    expect(mine.value).toBeCloseTo(-theirs.value, 12);
    expect(mine.value).toBeCloseTo((0.4 - 0.5 * 0.1) / STRENGTH_STEP, 12);
  });
});

describe('by phase and champion', () => {
  it('weighs a level and items by the phase: in the first levels they count far more', () => {
    // Level 4 against 3, nothing bought: a mean of 3,5, the early phase.
    const early = strengthOf(top({ level: 4, items: [] }, { level: 3, items: [] }), meta, ITEMS)!;
    expect(early.phase).toBe(0);
    expect(early.value).toBeCloseTo(STRENGTH_WEIGHTS[0]![0]!.level / STRENGTH_STEP, 12);
    // The same level ahead at 8 against 7 counts less.
    const later = strengthOf(top({ level: 8, items: [] }, { level: 7, items: [] }), meta, ITEMS)!;
    expect(later.phase).toBe(1);
    expect(later.value).toBeLessThan(early.value);
  });

  // As meta.json brings it: Darius and Jax on top, Jinx, Thresh, Caitlyn and Leona on the bot lane.
  const laneScale: LaneScaleFile = {
    duels: 391_952,
    tau: 0.45,
    weights: [
      { lane: 0, phase: 1, level: 0.5, gold: 0.7 },
      { lane: 3, phase: 1, level: 0.3, gold: 0.25 },
    ],
    champions: [
      { championId: 122, lane: 0, phase: 0, strength: 0.05, duels: 900 },
      { championId: 122, lane: 0, phase: 1, strength: 0.3, duels: 1500 },
      { championId: 122, lane: 0, phase: 2, strength: -0.2, duels: 700 },
      { championId: 24, lane: 0, phase: 1, strength: -0.1, duels: 1400 },
      { championId: 24, lane: 0, phase: 2, strength: 0.2, duels: 800 },
      { championId: 222, lane: 3, phase: 1, strength: 0.2, duels: 3000 },
      { championId: 412, lane: 4, phase: 1, strength: 0.1, duels: 2800 },
      { championId: 412, lane: 3, phase: 1, strength: 5, duels: 1 }, // Thresh as ADC: not the lane he plays here
      { championId: 89, lane: 4, phase: 1, strength: -0.1, duels: 2500 },
    ],
  };
  const measured = new MetaLookup({ ...meta.snapshot, laneScale });

  it('adds what the two champions bring in this phase, at equal level and items', () => {
    const s = strengthOf(top({ level: 8, items: [1036] }, { level: 8, items: [1036] }), measured, ITEMS)!;
    expect(s.phase).toBe(1);
    expect(s.duel![0]).toBeCloseTo(0.05, 12);
    expect(s.duel![1]).toBeCloseTo(0.4, 12);
    expect(s.duel![2]).toBeCloseTo(-0.4, 12);
    expect(s.value).toBeCloseTo(0.4 / STRENGTH_STEP, 12);
    // The file's weights for top in this phase, its own champions' part on top.
    const ahead = strengthOf(top({ level: 9, items: [1036] }, { level: 8, items: [1036] }), measured, ITEMS)!;
    expect(ahead.value).toBeCloseTo((0.5 + 0.4) / STRENGTH_STEP, 12);
    // Where the file has no weights for a lane and phase, the built-in ones stand.
    expect(laneScaleOf(measured).weights[1]![1]).toEqual(STRENGTH_WEIGHTS[1]![1]);
  });

  it('counts the bot lane as ADC and support, each on his own lane', () => {
    const data: LiveGameData = {
      activePlayer: { riotId: 'Ich#EUW', level: 8 },
      allPlayers: [
        row('Jinx', 'ORDER', 'BOTTOM', 8, [1036], 'Ich#EUW'),
        row('Thresh', 'ORDER', 'UTILITY', 7, []),
        row('Caitlyn', 'CHAOS', 'BOTTOM', 8, [1036]),
        row('Leona', 'CHAOS', 'UTILITY', 7, []),
      ],
    };
    const s = strengthOf(data, measured, ITEMS)!;
    // Jinx 0,2 and Thresh 0,1 against Caitlyn 0 and Leona −0,1.
    expect(s.duel![1]).toBeCloseTo(0.4, 12);
    expect(s.value).toBeCloseTo(0.4 / STRENGTH_STEP, 12);
  });

  it('keeps the built-in weights and shows no duel where meta.json has no strengths', () => {
    expect(laneScaleOf(meta).measured).toBe(false);
    expect(laneScaleOf(new MetaLookup({ ...meta.snapshot, laneScale: { ...laneScale, champions: [] } })).measured).toBe(false);
    const s = strengthOf(top({ level: 8, items: [] }, { level: 8, items: [] }), meta, ITEMS)!;
    expect(s.duel).toBeNull();
    expect(s.value).toBe(0);
  });

  it('shows who is stronger in each phase, and which phase it is now', () => {
    const champion = (id: number) => ({ id, key: `K${id}`, name: meta.champion(id)?.name ?? `#${id}` });
    const view = strengthView(strengthOf(top({ level: 8, items: [] }, { level: 8, items: [] }), measured, ITEMS)!, champion);
    expect(view.phases).toEqual([
      { label: 'früh · Lvl 1–5', who: 'offen', tone: 'even', now: false },
      { label: 'Mitte · 6–10', who: 'Darius', tone: 'up', now: true },
      { label: 'spät · 11+', who: 'Jax', tone: 'down', now: false },
    ]);
    const botView = strengthView(
      strengthOf(
        {
          activePlayer: { riotId: 'Ich#EUW', level: 8 },
          allPlayers: [row('Jinx', 'ORDER', 'BOTTOM', 8, [], 'Ich#EUW'), row('Thresh', 'ORDER', 'UTILITY', 7, []), row('Caitlyn', 'CHAOS', 'BOTTOM', 8, []), row('Leona', 'CHAOS', 'UTILITY', 7, [])],
        },
        measured,
        ITEMS,
      )!,
      champion,
    );
    expect(botView.phases!.map((p) => p.who)).toEqual(['offen', 'ihr', 'offen']);
    // Words of who is stronger, never of what to do.
    expect(JSON.stringify(view.phases)).not.toMatch(/kämpf|fight|passiv|trade|go\b/i);
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
    expect(s.phase).toBe(1); // 8 + 7 against 8 + 6
    expect(s.value).toBeCloseTo(STRENGTH_WEIGHTS[3]![1]!.level / STRENGTH_STEP, 12);
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
      { label: 'Level', value: '−1', tone: 'down' },
      { label: 'Gold', value: '+950', tone: 'up' },
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
    expect(view.rows[0]).toEqual({ label: 'Level', value: '±0', tone: 'even' }); // 8 + 7 against 8 + 7
    expect(view.rows[1]).toEqual({ label: 'Gold', value: '±0', tone: 'even' });
    expect(view.label).toBe('Ausgeglichen');
  });
});

describe('team against team', () => {
  // Darius 9 with 1.750 of items and Ahri 9 with 3.000 against Jax 8 with 800 and Ahri 3 with nothing.
  const game = top({ level: 9, items: [3133, 1036, 1001, 2003, 3340] }, { level: 8, items: [1055, 1036] });

  it('sums levels and item worth of both sides, as the scoreboard shows them', () => {
    const stand = teamStandOf(game, meta, ITEMS)!;
    expect(stand.mine).toEqual({ players: 2, level: 18, worth: 4750 });
    expect(stand.theirs).toEqual({ players: 2, level: 11, worth: 800 });
    expect(stand.levelDiff).toBe(7);
    expect(stand.worthDiff).toBe(3950);
    // The enemies in the game's own order, and the one with the most item worth.
    expect(stand.enemies).toEqual([
      { championId: 24, level: 8, worth: 800 },
      { championId: 103, level: 3, worth: 0 },
    ]);
    expect(stand.leaderId).toBe(24);
  });

  it('names no leader while no enemy has bought anything that counts', () => {
    expect(teamStandOf(top({ level: 1, items: [] }, { level: 1, items: [2003, 3340] }), meta, ITEMS)!.leaderId).toBe(0);
  });

  it('has nothing to say without the own row or without enemies', () => {
    const alone: LiveGameData = { ...game, allPlayers: game.allPlayers!.filter((p) => p.team === 'ORDER') };
    expect(teamStandOf(alone, meta, ITEMS)).toBeNull();
    const stranger: LiveGameData = { ...game, activePlayer: { riotId: 'Jemand#X' } };
    expect(teamStandOf(stranger, meta, ITEMS)).toBeNull();
  });

  it('shows numbers and each side\'s part, and no verdict', () => {
    const view = teamStandView(teamStandOf(game, meta, ITEMS)!);
    expect(view.rows).toEqual([
      { label: 'Level', mine: '18', theirs: '11', diff: '+7', tone: 'up', share: 18 / 29 },
      { label: 'Items', mine: '4.750', theirs: '800', diff: '+3.950', tone: 'up', share: 4750 / 5550 },
    ]);
    expect(JSON.stringify(view)).not.toMatch(/stärker|schwächer/);
    // Nothing bought on either side: the bar stands in the middle.
    const none = { players: 5, level: 5, worth: 0 };
    expect(teamStandView({ mine: none, theirs: none, levelDiff: 0, worthDiff: 0, enemies: [], leaderId: 0 }).rows[1]).toMatchObject({ diff: '±0', tone: 'even', share: 0.5 });
  });
});

describe('the teamfight scale', () => {
  // Weights of its own, so that the numbers are easy to follow; Darius and Jax with a strength in the middle phase.
  const teamfightScale: TeamfightScaleFile = {
    fights: 45_447,
    tau: 0.06,
    weights: { level: 0.2, gold: 0.05, goldCarry: 0.08, goldRest: 0.02, baron: 0.25, elder: 0.5, dragon: 0.05, soul: 0.35 },
    champions: [
      { championId: 122, phase: 1, strength: 0.1, fights: 900 },
      { championId: 24, phase: 1, strength: -0.08, fights: 800 },
    ],
  };
  const scaled = new MetaLookup({ ...meta.snapshot, teamfightScale });
  const named = (player: LivePlayer, gameName: string | undefined): LivePlayer => ({ ...player, riotIdGameName: gameName });

  // Minute 24 (1.450 s): Darius 14 and Ahri 13 against Jax 13 and Caitlyn 12. The event feed names
  // killers by the Riot ID without its tag, as the scrubber writes it.
  function fight(events: { EventName: string; EventTime: number; KillerName: string; DragonType?: string }[], gameTime = 1450): LiveGameData {
    return {
      activePlayer: { riotId: 'Ich#EUW', level: 14 },
      allPlayers: [
        named(row('Darius', 'ORDER', 'TOP', 14, [3071], 'Ich#EUW'), 'Ich'),
        named(row('Ahri', 'ORDER', 'MIDDLE', 13, [3133]), 'Fremd A'),
        named(row('Jax', 'CHAOS', 'TOP', 13, [3071, 1001]), 'Fremd J'),
        // No game name: the summoner name stands in.
        { ...row('Caitlyn', 'CHAOS', 'BOTTOM', 12, [1036]), summonerName: 'Fremd C' },
      ],
      gameData: { gameTime },
      events: { Events: events },
    };
  }

  const events = [
    { EventName: 'DragonKill', EventTime: 300, KillerName: 'Ich', DragonType: 'Fire' },
    { EventName: 'DragonKill', EventTime: 600, KillerName: 'Fremd C', DragonType: 'Water' },
    { EventName: 'DragonKill', EventTime: 900, KillerName: 'Fremd A', DragonType: 'Earth' },
    { EventName: 'DragonKill', EventTime: 1100, KillerName: 'Ich', DragonType: 'Air' },
    { EventName: 'DragonKill', EventTime: 1250, KillerName: 'Ich', DragonType: 'Elder' }, // 200 s ago: over
    { EventName: 'DragonKill', EventTime: 1270, KillerName: 'Fremd A', DragonType: 'Hextech' }, // the fourth: the soul
    { EventName: 'BaronKill', EventTime: 1300, KillerName: 'Fremd J' }, // 150 s ago: still on
    { EventName: 'DragonKill', EventTime: 1400, KillerName: 'Minion', DragonType: 'Air' }, // nobody the list knows
  ];

  it('reads the monsters from the event feed: dragons, the soul at four, Baron and the Elder while they last', () => {
    const buffs = teamBuffsOf(fight(events), 'ORDER');
    expect(buffs.mine).toEqual({ dragons: 4, soul: true, baron: false, elder: false });
    expect(buffs.theirs).toEqual({ dragons: 1, soul: false, baron: true, elder: false });
    // Baron for 180 s, the Elder for 150 s, counted from the kill.
    expect(teamBuffsOf(fight(events, 1480), 'ORDER').theirs.baron).toBe(true);
    expect(teamBuffsOf(fight(events, 1481), 'ORDER').theirs.baron).toBe(false);
    expect(teamBuffsOf(fight(events, 1400), 'ORDER').mine.elder).toBe(true);
    // Seen from the other team, the same feed.
    expect(teamBuffsOf(fight(events), 'CHAOS').mine.baron).toBe(true);
  });

  it('adds up level, items, the monsters and the champions of this phase', () => {
    const t = teamStrengthOf(fight(events), scaled, ITEMS)!;
    expect(t.phase).toBe(1);
    expect(t.stand.mine).toEqual({ players: 2, level: 27, worth: 4100 });
    expect(t.stand.theirs).toEqual({ players: 2, level: 25, worth: 3650 });
    expect(t.parts.level).toBeCloseTo(0.4, 12);
    expect(t.parts.gold).toBeCloseTo(0.036, 12); // all four play carry roles: 450 gold × 0,08
    expect(t.parts.baron).toBeCloseTo(-0.25, 12);
    expect(t.parts.elder).toBe(0);
    expect(t.parts.dragons).toBeCloseTo(0.15, 12);
    expect(t.parts.soul).toBeCloseTo(0.35, 12);
    expect(t.parts.champions).toBeCloseTo(0.18, 12);
    expect(t.value).toBeCloseTo(0.866 / STRENGTH_STEP, 12);
  });

  it('weighs gold by who holds it: on the carries it counts more than on jungle and support', () => {
    // The same 3.000 gold on both sides — ours on the support, theirs on the top laner.
    const split = (positions: boolean): LiveGameData => ({
      activePlayer: { riotId: 'Ich#EUW', level: 12 },
      allPlayers: [
        row('Darius', 'ORDER', positions ? 'TOP' : '', 12, [], 'Ich#EUW'),
        row('Thresh', 'ORDER', positions ? 'UTILITY' : '', 12, [3071]),
        row('Jax', 'CHAOS', positions ? 'TOP' : '', 12, [3071]),
        row('Leona', 'CHAOS', positions ? 'UTILITY' : '', 12, []),
      ],
      gameData: { gameTime: 1000 },
    });
    const t = teamStrengthOf(split(true), scaled, ITEMS)!;
    expect(t.stand.worthDiff).toBe(0);
    // 3.000 more on our support (×0,02), 3.000 more on their top laner (×0,08).
    expect(t.parts.gold).toBeCloseTo(0.06 - 0.24, 12);
    expect(teamStrengthView(t).rows.find((r) => r.label === 'Gold')).toMatchObject({ text: '±0', arrows: '·' });
    // Without positions the summed weight: equal gold, nothing either way.
    expect(teamStrengthOf(split(false), scaled, ITEMS)!.parts.gold).toBe(0);
  });

  it('gives no verdict without the measured scale', () => {
    expect(teamStrengthOf(fight(events), meta, ITEMS)).toBeNull();
  });

  it('shows the step, its measured odds and the parts, the largest first', () => {
    const view = teamStrengthView(teamStrengthOf(fight(events), scaled, ITEMS)!);
    expect(view.label).toBe('Leicht stärker');
    expect(view.odds).toBe('Teams in dieser Lage gewinnen rund 7 von 10 Teamfights');
    // Only the differences: no team sums to work out.
    expect(view.rows).toEqual([
      { label: 'Level', text: '+2', diff: '', arrows: '▲', tone: 'up' },
      { label: 'Seele', text: 'ihr', diff: '', arrows: '▲', tone: 'up' },
      { label: 'Baron', text: 'Gegner', diff: '', arrows: '·', tone: 'even' },
      { label: 'Champions', text: 'ihr', diff: '', arrows: '·', tone: 'even' },
      { label: 'Drachen', text: '4 : 1', diff: '', arrows: '·', tone: 'even' },
      { label: 'Gold', text: '+450', diff: '', arrows: '·', tone: 'even' },
    ]);
    // Without a monster on the map there are no rows for them.
    const quiet = teamStrengthView(teamStrengthOf(fight([]), scaled, ITEMS)!);
    expect(quiet.rows.map((r) => r.label)).toEqual(['Level', 'Champions', 'Gold']);
    // Words of who is stronger, never of what to do, and nothing of a player.
    expect(JSON.stringify(view)).not.toMatch(/kämpf|passiv|engage|Baron machen|erzwing/i);
    expect(JSON.stringify(view)).not.toMatch(/Ich#|"Ich"|Fremd/);
  });

  it('says how often teams in that step won, from the measurement', () => {
    expect([0, 0.4, -0.4].map(teamfightOdds)).toEqual(Array(3).fill('Teams in dieser Lage gewinnen rund 5 von 10 Teamfights'));
    expect([1, -1, 2, -2, 3, -3].map((v) => teamfightOdds(v).match(/rund (\d+)/)![1])).toEqual(['7', '3', '8', '2', '9', '1']);
  });
});
