// The measurement counts the games itself (tools/measure/tally.ts) and reads them through the app's
// MetaLookup. A side mixed up there would turn every duel and pair around without a sound — these
// tests pin the direction.

import { readFileSync } from 'node:fs';
import { describe, expect, it } from 'vitest';
import { Lane } from '../../src/core/model/lane';
import { MetaLookup, type MetaSnapshot } from '../../src/core/model/metaLookup';
import type { Game, Seat } from '../../tools/measure/games';
import { partsOf, synergyType, versusType } from '../../tools/measure/model';
import { tally, versusKey } from '../../tools/measure/tally';

const base = JSON.parse(readFileSync(new URL('../golden/snapshot.json', import.meta.url), 'utf8').replace(/^﻿/, '')) as MetaSnapshot;

const team = (top: number, jungle: number, mid: number, adc: number, support: number): Seat[] =>
  [top, jungle, mid, adc, support].map((championId, lane) => ({ championId, lane: lane as Lane }));

// Garen (86) beats Darius (122) on top far more often than both lane rates predict: 80 of 100.
const games: Game[] = [];
for (let i = 0; i < 100; i++) {
  const garenBlue = i % 2 === 0;
  const garenWins = i % 5 !== 0;
  const blue = team(garenBlue ? 86 : 122, 64, 103, 222, 412);
  const red = team(garenBlue ? 122 : 86, 254, 7, 51, 89);
  games.push({ blue, red, blueWins: garenBlue === garenWins ? 1 : 0, minutes: 30 });
}

describe('measurement tally', () => {
  it('counts a duel for the right side, both directions', () => {
    const counted = tally(games, base);
    const garen = counted.snapshot.matchups.find((m) => m.championId === 86 && m.opponentId === 122 && m.lane === Lane.Top)!;
    const darius = counted.snapshot.matchups.find((m) => m.championId === 122 && m.opponentId === 86 && m.lane === Lane.Top)!;

    expect(garen.play).toBe(100);
    expect(garen.winRate).toBeCloseTo(0.8, 10);
    expect(darius.winRate).toBeCloseTo(0.2, 10);
  });

  it('counts enemies on other lanes with the lower id first', () => {
    const counted = tally(games, base);
    // Lee Sin (64, blue jungle) against Caitlyn (51, red bot): stored as Caitlyn first.
    const pair = counted.versus.get(versusKey(51, Lane.Adc, 64, Lane.Jungle))!;
    expect(pair.games).toBe(100);
    const leeWins = games.filter((g) => g.blueWins === 1).length;
    expect(pair.wins).toBe(100 - leeWins);
  });

  it('turns a duel edge around with the sides', () => {
    const counted = tally(games, base);
    const meta = new MetaLookup(counted.snapshot);
    const numbers = { meta, versus: counted.versus };
    const garenBlue = partsOf(numbers, games[0]!);
    const garenRed = partsOf(numbers, games[1]!);

    expect(garenBlue.duels[Lane.Top]!).toBeGreaterThan(0);
    // Mirrored up to the app's duel baseline, which is not exactly symmetric (fitted line).
    expect(garenRed.duels[Lane.Top]!).toBeLessThan(0);
    expect(garenRed.duels[Lane.Top]!).toBeCloseTo(-garenBlue.duels[Lane.Top]!, 2);
  });

  it('sorts pairs by kind', () => {
    expect(synergyType(Lane.Support, Lane.Adc)).toBe(0);
    expect(synergyType(Lane.Mid, Lane.Jungle)).toBe(1);
    expect(synergyType(Lane.Top, Lane.Mid)).toBe(5);
    expect(versusType(Lane.Adc, Lane.Support)).toBe(0);
    expect(versusType(Lane.Top, Lane.Jungle)).toBe(1);
    expect(versusType(Lane.Top, Lane.Mid)).toBe(2);
  });
});
