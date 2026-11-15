// The ability combos at a pick (core/model/kits.ts, docs/modellpruefung.md Messung 28): a hint with
// the teammate's name, never a weight — and only on our own data, so the 1.5 texts stay as they are.

import { describe, expect, it } from 'vitest';
import { draftStateFrom } from '../../src/core/model/draftState';
import { isComboReason, KitTable } from '../../src/core/model/kits';
import { Lane } from '../../src/core/model/lane';
import { LanePredictor } from '../../src/core/model/lanePredictor';
import type { MetaLookup } from '../../src/core/model/metaLookup';
import { Recommender, type Recommendation } from '../../src/core/model/recommender';
import { TraitTable } from '../../src/core/model/traits';
import { MetaBuilder, SessionBuilder } from './helpers';

const Malphite = 1, Amumu = 2, Orianna = 3, MissFortune = 4, Lulu = 5, Yasuo = 6, KogMaw = 7, Garen = 8, Ahri = 9, Soraka = 10, Xayah = 11, Janna = 12, Sion = 13, Lux = 14;
const CAST: [number, string, Lane][] = [
  [Malphite, 'Malphite', Lane.Top],
  [Garen, 'Garen', Lane.Top],
  [Amumu, 'Amumu', Lane.Jungle],
  [Orianna, 'Orianna', Lane.Mid],
  [Yasuo, 'Yasuo', Lane.Mid],
  [Ahri, 'Ahri', Lane.Mid],
  [MissFortune, 'Miss Fortune', Lane.Adc],
  [KogMaw, "Kog'Maw", Lane.Adc],
  [Xayah, 'Xayah', Lane.Adc],
  [Lulu, 'Lulu', Lane.Support],
  [Soraka, 'Soraka', Lane.Support],
  [Janna, 'Janna', Lane.Support],
  [Sion, 'Sion', Lane.Top],
  [Lux, 'Lux', Lane.Mid],
];

function meta(source: 'riot' | 'opgg'): MetaLookup {
  const builder = new MetaBuilder();
  for (const [id, name, lane] of CAST) builder.champion(id, name).inLane(id, lane, { winRate: 0.5, play: 20000 });
  if (source === 'riot') builder.snapshotRef().source = 'riot';
  return builder.build();
}

const kits = new KitTable(CAST.map(([id, name]) => ({ id, key: name, name })));
const texts = (candidate: number, allies: number[]): string[] => kits.reasons(candidate, allies).map((r) => r.text);

describe('ability combos as hints', () => {
  it('names the teammate whose engage the pick follows up', () => {
    expect(texts(MissFortune, [Amumu])).toEqual(['Wombo mit Amumu']);
    // The user's example: Orianna with Amumu and Malphite.
    expect(texts(Orianna, [Amumu, Malphite, Garen])).toEqual(['Wombo mit Amumu und Malphite']);
    expect(kits.reasons(Orianna, [Amumu])[0]!.hint).toContain('Amumu (R)');
  });

  it('names the teammate the pick engages for', () => {
    expect(texts(Amumu, [MissFortune, Garen])).toEqual(['Engage für Miss Fortune']);
  });

  it('needs a second champion: Orianna alone is no wombo', () => {
    expect(texts(Orianna, [Garen, Ahri])).toEqual([]);
    expect(texts(Orianna, [Orianna])).toEqual([]);
  });

  it('opens Yasuo’s ult both ways round', () => {
    expect(texts(Janna, [Yasuo])).toEqual(['Knock-up für Yasuo']);
    expect(texts(Yasuo, [Janna, Garen])).toEqual(['Knock-up von Janna']);
    // Malphite is engage and knock-up: the wombo comes first, the knock-up second.
    expect(texts(Malphite, [Yasuo])).toEqual(['Engage für Yasuo', 'Knock-up für Yasuo']);
  });

  it('puts an enchanter behind a hypercarry', () => {
    expect(texts(Lulu, [KogMaw])).toEqual(["Enchanter für Kog'Maw"]);
    expect(texts(KogMaw, [Lulu])).toEqual(['Hypercarry mit Lulu']);
  });

  it('shows two at most', () => {
    expect(texts(Malphite, [Yasuo, MissFortune, KogMaw])).toHaveLength(2);
  });

  it('keeps to the kits a comp is built around', () => {
    // On the wide lists that were measured, but too common to say anything at a pick.
    expect(texts(Sion, [MissFortune, Lux])).toEqual([]);
    expect(texts(Lux, [Amumu])).toEqual([]);
    expect(texts(Lulu, [Xayah])).toEqual([]);
  });

  it('says in every hint that it does not count', () => {
    for (const reason of [...kits.reasons(Malphite, [Yasuo]), ...kits.reasons(Lulu, [KogMaw]), ...kits.reasons(Yasuo, [Janna])]) {
      expect(reason.tone).toBe('neutral');
      expect(reason.hint).toContain('zählt nicht in die Prozentzahl');
      expect(isComboReason(reason.text)).toBe(true);
    }
  });
});

describe('ability combos in the pick list', () => {
  /** Amumu jungle and Miss Fortune bot are locked; the mid seat (cell 2) is the own one. */
  const board = () => new SessionBuilder().localPlayer(2).locked(1, Amumu).completedPick(1).locked(3, MissFortune).completedPick(3);

  function picks(lookup: MetaLookup): Recommendation[] {
    const state = draftStateFrom(board().build());
    const predictor = new LanePredictor(lookup);
    const seat = state.allies.find((slot) => slot.cellId === 2)!;
    // Without traits, so that nothing but the hint can tell Orianna from Ahri.
    return new Recommender(lookup, TraitTable.empty).recommend(state, { slot: seat, action: 'pick', isFollowingTurn: false }, predictor.predict(state.enemies), predictor.predict(state.allies)).items;
  }

  it('hangs the hint on the pick without moving its percentage', () => {
    const items = picks(meta('riot'));
    const orianna = items.find((item) => item.championId === Orianna)!;
    const ahri = items.find((item) => item.championId === Ahri)!;
    expect(orianna.reasons.map((r) => r.text)).toContain('Wombo mit Amumu');
    expect(ahri.reasons.some((r) => isComboReason(r.text))).toBe(false);
    // Same lane strength, nothing else known: the combo must not put Orianna ahead.
    expect(orianna.score).toBeCloseTo(ahri.score, 12);
  });

  it('stays out of OP.GG files', () => {
    for (const item of picks(meta('opgg'))) expect(item.reasons.some((r) => isComboReason(r.text))).toBe(false);
  });
});

describe('the peel chip on our own data', () => {
  /** Xayah bot is locked, the support seat (cell 4) is the own one; the enemy shows Malphite and Garen. */
  const board = () => new SessionBuilder().localPlayer(4).locked(3, Xayah).completedPick(3).locked(5, Malphite).completedPick(5).locked(6, Garen).completedPick(6);

  function soraka(lookup: MetaLookup): Recommendation {
    const state = draftStateFrom(board().build());
    const predictor = new LanePredictor(lookup);
    const seat = state.allies.find((slot) => slot.cellId === 4)!;
    const items = new Recommender(lookup, TraitTable.bundled()).recommend(state, { slot: seat, action: 'pick', isFollowingTurn: false }, predictor.predict(state.enemies), predictor.predict(state.allies)).items;
    return items.find((item) => item.championId === Soraka)!;
  }

  it('names the carry and the engage it is about', () => {
    expect(soraka(meta('riot')).reasons.map((r) => r.text)).toContain('Peel für Xayah gegen Malphite');
  });

  it('keeps the 1.5 wording on OP.GG files', () => {
    const reasons = soraka(meta('opgg')).reasons.map((r) => r.text);
    expect(reasons).toContain('Disengage');
    expect(reasons.some((text) => text.startsWith('Peel '))).toBe(false);
  });
});
