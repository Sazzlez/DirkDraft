// The mechanic counters (tools/measure/mechanics.ts): every listed champion exists, and an
// encounter is found on the right lanes for the right side — a side mixed up would turn every
// measured counter around without a sound.

import { readFileSync } from 'node:fs';
import { describe, expect, it } from 'vitest';
import { Lane } from '../../src/core/model/lane';
import type { MetaSnapshot } from '../../src/core/model/metaLookup';
import type { Game, Seat } from '../../tools/measure/games';
import { encountersOf, MECHANIC_RULES, mechanicFeatures, mechanicSets } from '../../tools/measure/mechanics';

const snapshot = JSON.parse(readFileSync(new URL('../golden/snapshot.json', import.meta.url), 'utf8').replace(/^﻿/, '')) as MetaSnapshot;
const { sets, missing } = mechanicSets(snapshot.champions);
const id = (name: string) => snapshot.champions.find((c) => c.name === name)!.id;
const rule = (label: string) => MECHANIC_RULES.find((r) => r.label === label)!;

/** Top, jungle, mid, ADC, support per side, by name. */
function game(blue: string[], red: string[]): Game {
  const seats = (names: string[]): Seat[] => names.map((name, lane) => ({ championId: id(name), lane: lane as Lane }));
  return { blue: seats(blue), red: seats(red), blueWins: 1, minutes: 30 };
}

describe('mechanic counters', () => {
  it('finds every listed champion in the roster', () => {
    expect(missing).toEqual([]);
    expect(sets.stopsDash.size).toBe(4);
    expect(sets.hook.has(id('Thresh'))).toBe(true);
  });

  it('finds a duel for the countering side, both ways round', () => {
    const poppy = rule('Dash-Stopper gegen Dash-Champ');
    const blueStops = game(['Poppy', 'Lee Sin', 'Ahri', 'Jinx', 'Lulu'], ['Yasuo', 'Vi', 'Lux', 'Ashe', 'Soraka']);
    // Poppy against Yasuo on top, and nothing else: Lee Sin and Vi both dash, but nobody stops them.
    expect(encountersOf(blueStops, sets, poppy)).toEqual([{ mine: 0, theirs: 5, side: 1 }]);
    const redStops = game(['Yasuo', 'Vi', 'Lux', 'Ashe', 'Soraka'], ['Poppy', 'Lee Sin', 'Ahri', 'Jinx', 'Lulu']);
    expect(encountersOf(redStops, sets, poppy)).toEqual([{ mine: 5, theirs: 0, side: -1 }]);
  });

  it('meets only on the lanes the rule names', () => {
    const hook = rule('Hook gegen ADC ohne Dash');
    // Thresh (support) against Jhin (ADC without a dash): one; against Ezreal, who dashes, none.
    expect(mechanicFeatures(game(['Garen', 'Vi', 'Lux', 'Ezreal', 'Thresh'], ['Darius', 'Amumu', 'Ahri', 'Jhin', 'Lulu']), sets, [hook])).toEqual([1]);
    expect(mechanicFeatures(game(['Garen', 'Vi', 'Lux', 'Ezreal', 'Lulu'], ['Darius', 'Lee Sin', 'Ahri', 'Jinx', 'Thresh']), sets, [hook])).toEqual([0]);

    const blocker = rule('Autoattack-Blocker gegen Autoattack-Champ');
    // Teemo as support against Jinx: different lanes, and this rule wants the same one.
    expect(mechanicFeatures(game(['Garen', 'Vi', 'Lux', 'Ezreal', 'Teemo'], ['Darius', 'Lee Sin', 'Ahri', 'Jinx', 'Lulu']), sets, [blocker])).toEqual([0]);
    // Jax against Tryndamere on top: one.
    expect(mechanicFeatures(game(['Jax', 'Vi', 'Lux', 'Ezreal', 'Lulu'], ['Tryndamere', 'Lee Sin', 'Ahri', 'Jinx', 'Soraka']), sets, [blocker])).toEqual([1]);
  });

  it('nets blue against red', () => {
    const hook = rule('Hook gegen ADC ohne Dash');
    const shield = rule('Spell Shield gegen Hook');
    // Blue's Thresh hooks red's Jhin; red's Morgana support shields against blue's Thresh.
    const both = game(['Garen', 'Vi', 'Lux', 'Ezreal', 'Thresh'], ['Darius', 'Lee Sin', 'Ahri', 'Jhin', 'Morgana']);
    expect(mechanicFeatures(both, sets, [hook, shield])).toEqual([1, -1]);
  });
});
