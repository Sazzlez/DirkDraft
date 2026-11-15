// Open seats (core/model/openSeats.ts, docs/modellpruefung.md Messung 29): a finding counts by how
// likely it stands at the end, with the picks to come taken as what players pick on those lanes —
// and a complete team reads exactly as the analyzer says.

import { describe, expect, it } from 'vitest';
import { CompAnalyzer } from '../../src/core/model/compAnalyzer';
import { Lane } from '../../src/core/model/lane';
import { DamageType, type MetaLookup } from '../../src/core/model/metaLookup';
import { OpenSeats } from '../../src/core/model/openSeats';
import { TraitTable } from '../../src/core/model/traits';
import { MetaBuilder } from './helpers';

// Two picks a lane, each half of the lane's games.
const TopTank = 1, TopFighter = 2, JungleTank = 3, JungleAssassin = 4, MidMage = 5, MidAssassin = 6, BotAdc = 7, BotMage = 8, SupTank = 9, SupMage = 10;
const AD = { damage: DamageType.Physical, physicalDamage: 20000, magicDamage: 1000 };
const AP = { damage: DamageType.Magic, physicalDamage: 1000, magicDamage: 20000 };
const SUPPORT_AP = { damage: DamageType.Magic, physicalDamage: 500, magicDamage: 6000 };

function meta(source: 'riot' | 'opgg' = 'riot'): MetaLookup {
  const b = new MetaBuilder();
  const add = (id: number, name: string, lane: Lane, options: Parameters<MetaBuilder['champion']>[2]) =>
    b.champion(id, name, { attackRange: 550, defense: 5, ...options }).inLane(id, lane, { winRate: 0.5, play: 5000, pickRate: 0.05 });
  add(TopTank, 'TopTank', Lane.Top, { ...AP, tags: ['Tank'] });
  add(TopFighter, 'TopFighter', Lane.Top, { ...AD, tags: ['Fighter'] });
  add(JungleTank, 'JungleTank', Lane.Jungle, { ...AP, tags: ['Tank'] });
  add(JungleAssassin, 'JungleAssassin', Lane.Jungle, { ...AD, tags: ['Assassin'] });
  add(MidMage, 'MidMage', Lane.Mid, { ...AP, tags: ['Mage'] });
  add(MidAssassin, 'MidAssassin', Lane.Mid, { ...AD, tags: ['Assassin'] });
  add(BotAdc, 'BotAdc', Lane.Adc, { ...AD, tags: ['Marksman'] });
  add(BotMage, 'BotMage', Lane.Adc, { ...AP, tags: ['Mage'], defense: 6 });
  add(SupTank, 'SupTank', Lane.Support, { ...SUPPORT_AP, tags: ['Tank', 'Support'] });
  add(SupMage, 'SupMage', Lane.Support, { ...SUPPORT_AP, tags: ['Mage', 'Support'] });
  if (source === 'riot') b.snapshotRef().source = 'riot';
  return b.build();
}

const seats = (...picks: [number, Lane][]) => picks.map(([championId, lane]) => ({ championId, lane }));
const open = (m: MetaLookup = meta()) => new OpenSeats(new CompAnalyzer(m, TraitTable.empty), m, TraitTable.empty);

describe('the frontline on our own data', () => {
  it('is the Tank tag alone; OP.GG files keep the defence rating of 1.5', () => {
    const own = meta('riot');
    const opgg = meta('opgg');
    const mage = (m: MetaLookup) => new CompAnalyzer(m, TraitTable.empty).isFrontline(m.champion(BotMage)!);
    expect(mage(own)).toBe(false);
    expect(mage(opgg)).toBe(true);
    expect(new CompAnalyzer(own, TraitTable.empty).isFrontline(own.champion(TopTank)!)).toBe(true);
  });
});

describe('open seats', () => {
  it('knows what an average pick of a lane brings', () => {
    const o = open();
    expect(o.average(Lane.Top).frontline).toBeCloseTo(0.5, 12);
    expect(o.average(Lane.Adc).frontline).toBe(0);
    expect(o.average(Lane.Jungle).assassin).toBeCloseTo(0.5, 12);
    // A seat on no known lane: the mean of the five.
    expect(o.average(Lane.Unknown).frontline).toBeCloseTo((0.5 + 0.5 + 0 + 0 + 0.5) / 5, 12);
  });

  it('names the lanes still to come, or seats on any lane where the lanes shown do not add up', () => {
    const o = open();
    expect(o.openLanes(seats([MidMage, Lane.Mid], [BotAdc, Lane.Adc]))).toEqual([Lane.Top, Lane.Jungle, Lane.Support]);
    expect(o.openLanes(seats([MidMage, Lane.Unknown], [BotAdc, Lane.Adc]))).toEqual([Lane.Unknown, Lane.Unknown, Lane.Unknown]);
    expect(o.openLanes(seats([MidMage, Lane.Mid], [MidAssassin, Lane.Mid]))).toEqual([Lane.Unknown, Lane.Unknown, Lane.Unknown]);
  });

  it('counts no frontline by the odds that none of the open lanes brings one', () => {
    const o = open();
    // Mid, bot and support shown without a tank; top and jungle open, half of each lane's picks are tanks.
    const odds = o.odds(seats([MidMage, Lane.Mid], [BotAdc, Lane.Adc], [SupMage, Lane.Support]));
    expect(odds.noFrontline).toBeCloseTo(0.5 * 0.5, 12);
    // A tank shown: no gap. Only the bot seat open, where nobody is a tank: the gap is sure.
    expect(o.odds(seats([TopTank, Lane.Top], [MidMage, Lane.Mid])).noFrontline).toBe(0);
    expect(o.odds(seats([TopFighter, Lane.Top], [JungleAssassin, Lane.Jungle], [MidMage, Lane.Mid], [SupMage, Lane.Support])).noFrontline).toBe(1);
  });

  it('does not call physical damage missing while the seats that bring it are open', () => {
    const o = open();
    // Two mages and a mage support shown — the old rule: three of five, 60 % of the cost.
    const shown = seats([TopTank, Lane.Top], [MidMage, Lane.Mid], [SupMage, Lane.Support]);
    const analyzer = new CompAnalyzer(meta(), TraitTable.empty);
    expect(analyzer.analyze(shown.map((s) => s.championId)).findings.map((f) => f.issue)).toContain('noPhysicalDamage');
    // Jungle and bot open: only the two AP picks together (a quarter) leave it missing, and
    // teammates fill a gap they see — a quarter over 1 + 0,4 per open seat.
    expect(o.odds(shown).noPhysicalDamage).toBeCloseTo(0.25 / 1.8, 12);
    // With the AD carry shown the gap is gone whatever the jungler brings.
    expect(o.odds([...shown, { championId: BotAdc, lane: Lane.Adc }]).noPhysicalDamage).toBe(0);
  });

  it('reads a complete team exactly as the analyzer does', () => {
    const o = open();
    const allAp = seats([TopTank, Lane.Top], [JungleTank, Lane.Jungle], [MidMage, Lane.Mid], [BotMage, Lane.Adc], [SupMage, Lane.Support]);
    expect(o.odds(allAp)).toMatchObject({ noPhysicalDamage: 1, noMagicDamage: 0, noFrontline: 0 });
    const outlook = o.outlook(seats([TopFighter, Lane.Top], [JungleAssassin, Lane.Jungle], [MidAssassin, Lane.Mid], [BotAdc, Lane.Adc], [SupTank, Lane.Support]));
    expect(outlook.assassins).toBe(2);
    expect(outlook.wall).toBe(0);
    expect(outlook.odds.noMagicDamage).toBe(1);
  });

  it('expects assassins and a second frontliner from the lanes still open', () => {
    const o = open();
    // One tank shown; top and jungle open, each half tanks: a second one comes unless both miss.
    const outlook = o.outlook(seats([SupTank, Lane.Support], [MidMage, Lane.Mid], [BotAdc, Lane.Adc]));
    expect(outlook.wall).toBeCloseTo(1 - 0.5 * 0.5, 12);
    expect(outlook.assassins).toBeCloseTo(0.5, 12);
  });
});
