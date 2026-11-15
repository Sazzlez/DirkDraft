// The ability combos (tools/measure/combos.ts): every listed champion exists, a combo needs two
// different champions, and the feature is blue's value minus red's — a side mixed up would turn
// every measured combo around without a sound.

import { readFileSync } from 'node:fs';
import { describe, expect, it } from 'vitest';
import { Lane } from '../../src/core/model/lane';
import type { MetaSnapshot } from '../../src/core/model/metaLookup';
import type { Game, Seat } from '../../tools/measure/games';
import { COMBO_RULES, comboFeatures, comboSets, hasPair } from '../../tools/measure/combos';

const snapshot = JSON.parse(readFileSync(new URL('../golden/snapshot.json', import.meta.url), 'utf8').replace(/^﻿/, '')) as MetaSnapshot;
const { sets, missing } = comboSets(snapshot.champions);
const id = (name: string) => snapshot.champions.find((c) => c.name === name)!.id;
const featureOf = (game: Game, label: string) => comboFeatures(game, sets)[COMBO_RULES.findIndex((r) => r.label === label)];

/** Top, jungle, mid, ADC, support, by name. */
const team = (names: string[]): Seat[] => names.map((name, lane) => ({ championId: id(name), lane: lane as Lane }));
const game = (blue: string[], red: string[]): Game => ({ blue: team(blue), red: team(red), blueWins: 1, minutes: 30 });

const PLAIN = ['Garen', 'Graves', 'Ahri', 'Ashe', 'Soraka'];

describe('ability combos', () => {
  it('finds every listed champion in the roster', () => {
    // The golden snapshot is older than the newest champions; only those may be missing.
    expect(missing.filter((name) => !['Mel', 'Zaahen', 'Yunara', 'Locke'].includes(name))).toEqual([]);
    expect(sets.aoeEngage.has(id('Amumu'))).toBe(true);
    expect(sets.aoeDamage.has(id('Miss Fortune'))).toBe(true);
  });

  it('needs two different champions for a pair', () => {
    // Orianna is engage and damage at once, but alone she is no wombo.
    expect(hasPair(team(['Garen', 'Graves', 'Orianna', 'Ashe', 'Soraka']), sets.aoeEngage, sets.aoeDamage)).toBe(false);
    expect(hasPair(team(['Malphite', 'Graves', 'Orianna', 'Ashe', 'Soraka']), sets.aoeEngage, sets.aoeDamage)).toBe(true);
    expect(hasPair(team(['Garen', 'Amumu', 'Ahri', 'Miss Fortune', 'Soraka']), sets.aoeEngage, sets.aoeDamage)).toBe(true);
  });

  it('counts for the side that has the combo', () => {
    const wombo = ['Garen', 'Amumu', 'Ahri', 'Miss Fortune', 'Soraka'];
    const label = 'Wombo: AoE-Engage + AoE-Schaden im Team';
    expect(featureOf(game(wombo, PLAIN), label)).toBe(1);
    expect(featureOf(game(PLAIN, wombo), label)).toBe(-1);
    expect(featureOf(game(wombo, wombo), label)).toBe(0);
  });

  it('counts knock-ups only in a team with Yasuo', () => {
    const label = 'Yasuo: je Knock-up im Team';
    expect(featureOf(game(['Malphite', 'Vi', 'Yasuo', 'Ashe', 'Soraka'], PLAIN), label)).toBe(2);
    expect(featureOf(game(['Malphite', 'Vi', 'Ahri', 'Ashe', 'Soraka'], PLAIN), label)).toBe(0);
  });
});
