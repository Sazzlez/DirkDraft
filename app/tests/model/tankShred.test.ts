// B4: against two or more frontliners, a pick that builds through armour, resist and health is worth
// a little — read from the own build file, not judged. Assassins are left out: against frontliners
// they already carry the contra "kommt schwer an ihre Carrys".

import { readFileSync } from 'node:fs';
import { describe, expect, it } from 'vitest';
import { DraftEngine } from '../../src/core/engine';
import { CompAnalyzer, EMPTY_PROFILE } from '../../src/core/model/compAnalyzer';
import { Lane } from '../../src/core/model/lane';
import { DamageType, type MetaSnapshot } from '../../src/core/model/metaLookup';
import type { Reason } from '../../src/core/model/reason';
import { TankShred } from '../../src/core/model/tankShred';
import { TraitTable } from '../../src/core/model/traits';
import type { RiotBuildFile } from '../../src/core/riotBuilds';
import { MetaBuilder, SessionBuilder } from './helpers';

const Cleaver = 3071,
  Dominik = 3036,
  Other1 = 3031,
  Other2 = 3046,
  Other3 = 6672;

const row = (championId: number, lane: number, core: [number[], number][], tier = 'platinum') => ({
  championId,
  lane,
  tier,
  games: core.reduce((s, [, g]) => s + g, 0),
  wins: 0,
  runes: [],
  spells: [],
  starters: [],
  boots: [],
  core: core.map(([ids, games]) => ({ ids, games, wins: Math.floor(games / 2) })),
  fourth: [],
  fifth: [],
  sixth: [],
  supportChoice: [],
  skillOrder: [],
  skillMax: [],
});

const file = (...builds: ReturnType<typeof row>[]): RiotBuildFile =>
  ({ version: 1, patch: '16.22', platform: 'euw1', collectedAtUtc: '', matches: {}, builds }) as unknown as RiotBuildFile;

describe('tank shred from the build file', () => {
  it('counts a champion-lane where half of the core builds hold a shred item', () => {
    const shred = TankShred.fromBuilds(
      file(
        row(1, Lane.Adc, [[[Other1, Dominik, Other2], 20], [[Other1, Other2, Other3], 20]]),
        row(2, Lane.Top, [[[Cleaver, Other1, Other2], 10], [[Other1, Other2, Other3], 30]]),
        row(3, Lane.Mid, [[[Cleaver, Other1, Other2], 20]]), // too few games to tell
        // Tiers of one champion-lane add up: 15 + 20 shred games of 50.
        row(4, Lane.Top, [[[Cleaver, Other1, Other2], 15], [[Other1, Other2, Other3], 15]], 'gold'),
        row(4, Lane.Top, [[[Dominik, Other1, Other2], 20]], 'platinum'),
      ),
    );

    expect(shred.has(1, Lane.Adc)).toBe(true);
    expect(shred.has(1, Lane.Top)).toBe(false);
    expect(shred.has(1, Lane.Unknown)).toBe(true);
    expect(shred.has(2, Lane.Top)).toBe(false);
    expect(shred.has(3, Lane.Mid)).toBe(false);
    expect(shred.has(4, Lane.Top)).toBe(true);
    expect(TankShred.fromBuilds(null).count).toBe(0);
  });

  it('gives the pro against two frontliners, and not to assassins', () => {
    const meta = new MetaBuilder()
      .champion(1, 'Marksman', { damage: DamageType.Physical, tags: ['Marksman'], attackRange: 550, defense: 2 })
      .champion(2, 'Assassin', { damage: DamageType.Physical, tags: ['Assassin'], attackRange: 125, defense: 2 })
      .champion(3, 'Plain', { damage: DamageType.Physical, tags: ['Marksman'], attackRange: 550, defense: 2 })
      .champion(11, 'TankA', { damage: DamageType.Magic, tags: ['Tank'], attackRange: 125, defense: 8 })
      .champion(12, 'TankB', { damage: DamageType.Magic, tags: ['Tank'], attackRange: 125, defense: 9 })
      .champion(13, 'Mage', { damage: DamageType.Magic, tags: ['Mage'], attackRange: 550, defense: 2 })
      .build();
    const shred = TankShred.fromBuilds(
      file(row(1, Lane.Adc, [[[Dominik, Other1, Other2], 40]]), row(2, Lane.Mid, [[[Cleaver, Other1, Other2], 40]])),
    );
    const a = new CompAnalyzer(meta, TraitTable.empty, shred);
    const twoTanks = a.analyze([11, 12, 13]);
    const oneTank = a.analyze([11, 13]);

    const reasons: Reason[] = [];
    const marksman = a.fit(1, EMPTY_PROFILE, twoTanks, reasons, Lane.Adc)!;
    const plain = a.fit(3, EMPTY_PROFILE, twoTanks, [], Lane.Adc)!;
    expect(reasons.some((r) => r.text === 'Tank-Shred' && r.tone === 'pro' && !!r.hint)).toBe(true);
    expect(marksman - plain).toBeCloseTo(0.25, 10);

    const assassin: Reason[] = [];
    a.fit(2, EMPTY_PROFILE, twoTanks, assassin, Lane.Mid);
    expect(assassin.some((r) => r.text === 'Tank-Shred')).toBe(false);

    const few: Reason[] = [];
    a.fit(1, EMPTY_PROFILE, oneTank, few, Lane.Adc);
    expect(few.some((r) => r.text === 'Tank-Shred')).toBe(false);
  });

  it('reaches the pick list through the engine', () => {
    const snapshot = JSON.parse(readFileSync(new URL('../golden/snapshot.json', import.meta.url), 'utf8').replace(/^﻿/, '')) as MetaSnapshot;
    // Every champion counted as a shredder on bot: what matters here is the way through the engine.
    const shred = TankShred.fromBuilds(file(...snapshot.champions.map((c) => row(c.id, Lane.Adc, [[[Dominik, Other1, Other2], 40]]))));
    // Malphite, Ornn and Sion locked for the enemy: three frontliners.
    const session = new SessionBuilder().queue(420).localPlayer(3).locked(5, 54).locked(6, 516).locked(7, 14).onClock(3, 'pick').build();
    const chips = (engine: DraftEngine) =>
      engine.update(session).frame.picks!.items.filter((item) => item.reasons.some((r) => r.text === 'Tank-Shred')).length;

    expect(chips(new DraftEngine(snapshot, { tankShred: shred }))).toBeGreaterThan(0);
    expect(chips(new DraftEngine(snapshot))).toBe(0);
  });
});
