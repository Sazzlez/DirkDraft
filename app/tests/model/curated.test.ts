// Traits, combos and seat priors have one source — data/ in the repository root — and traits and
// combos arrive with the downloaded numbers (docs/redesign/daten.md). Until 17.11.2026 the traits
// lay in the repository twice and the combo lists in kits.ts, all built into the app: a new champion
// had no traits until the next release.

import { existsSync, readFileSync } from 'node:fs';
import { describe, expect, it } from 'vitest';
import combosFile from '../../../data/combos.json';
import traitsFile from '../../../data/champion_traits.json';
import { draftStateFrom } from '../../src/core/model/draftState';
import { BUNDLED_COMBOS, COMBO_NAMES, COMBOS, isComboReason, KitTable, readComboFile } from '../../src/core/model/kits';
import { Lane } from '../../src/core/model/lane';
import { LanePredictor } from '../../src/core/model/lanePredictor';
import type { MetaSnapshot } from '../../src/core/model/metaLookup';
import { Recommender } from '../../src/core/model/recommender';
import { TraitTable } from '../../src/core/model/traits';
import { checkCombos, checkTraits, CURATED_VERSION, DataStore, FILES, type DataSet, type DataSource } from '../../src/data/remote';
import { LiveSession } from '../../src/live/session';
import { MetaBuilder, SessionBuilder } from './helpers';

const snapshot = JSON.parse(readFileSync(new URL('../golden/snapshot.json', import.meta.url), 'utf8').replace(/^﻿/, '')) as MetaSnapshot;

describe('one source', () => {
  it('keeps no second copy of the curated files inside the app', () => {
    expect(existsSync(new URL('../../src/core/model/data', import.meta.url))).toBe(false);
    // What the app builds in is the repository's file.
    expect(TraitTable.bundled().count).toBe(Object.keys(traitsFile.champions).length);
    expect(BUNDLED_COMBOS.combos).toEqual(combosFile.combos);
    expect(COMBOS.aoeEngage).toContain('Amumu');
    expect(BUNDLED_COMBOS.engageSkill.jarvaniv).toBe('E-Q und R');
  });

  it('names the two files among the downloads, in the published layout', () => {
    expect(FILES.traits).toBe('traits.json');
    expect(FILES.combos).toBe('combos.json');
    expect(checkTraits({ version: CURATED_VERSION, champions: { Ahri: { engage: 1 } } })).toBeNull();
    expect(checkTraits({ version: 2, champions: { Ahri: {} } })).toBe('unbekannte Version 2');
    expect(checkTraits({ version: CURATED_VERSION, champions: {} })).toBe('keine Champions');
    expect(checkTraits(null)).toBe('keine Daten');
    expect(checkCombos({ ...combosFile, version: CURATED_VERSION })).toBeNull();
    expect(checkCombos({ version: CURATED_VERSION, combos: { aoeEngage: ['Amumu'] } })).toBe('Listen fehlen');
    expect(checkCombos({ version: 9, combos: combosFile.combos })).toBe('unbekannte Version 9');
  });

  it('reads the combo file whole or not at all', () => {
    const file = readComboFile(combosFile)!;
    expect(Object.keys(file.combos).sort()).toEqual([...COMBO_NAMES].sort());
    // A list that is missing or holds something else than names: nothing, the copy at hand stays.
    expect(readComboFile({ combos: { ...combosFile.combos, yasuo: undefined } })).toBeNull();
    expect(readComboFile({ combos: { ...combosFile.combos, yasuo: [157] } })).toBeNull();
    expect(readComboFile({ combos: [] })).toBeNull();
    expect(readComboFile(null)).toBeNull();
    // The skills are optional and matched loosely.
    expect(readComboFile({ combos: combosFile.combos })!.engageSkill).toEqual({});
    expect(readComboFile({ combos: combosFile.combos, engageSkill: { 'Jarvan IV': 'E-Q und R', zac: 7 } })!.engageSkill).toEqual({ jarvaniv: 'E-Q und R' });
  });
});

describe('downloaded traits and combos beat the built-in copies', () => {
  const Ezreal = 81, Xerath = 101, Ziggs = 115;
  const data = (extra: Partial<DataSet> = {}): DataSet => ({ meta: snapshot, builds: null, aram: null, situational: null, fresh: true, changed: false, problems: [], ...extra });

  /** What the comp tile says the own team lacks, with three champions locked that bring no engage. */
  function lacks(set: DataSet): string[] {
    const live = new LiveSession();
    live.setData(set);
    live.onStatus({ leagueFound: true, clientRunning: true, socketConnected: true });
    live.onPhase('ChampSelect');
    live.onSession(new SessionBuilder().queue(420).localPlayer(0).locked(1, Ezreal).locked(2, Xerath).locked(3, Ziggs).onClock(0, 'pick').build());
    const comp = live.view().side.find((tile) => tile.kind === 'comp');
    return comp && comp.kind === 'comp' ? (comp.comp.gap?.items ?? []) : [];
  }

  it('uses the traits that came with the numbers', () => {
    expect(lacks(data())).toContain('Engage');

    // The published file says Ezreal engages: the gap closes without a release.
    const published = { version: CURATED_VERSION, champions: { ...traitsFile.champions, Ezreal: { ...traitsFile.champions.Ezreal, engage: 3 } } };
    expect(lacks(data({ traits: published }))).not.toContain('Engage');
  });

  it('keeps the built-in traits when the download reads as nothing', () => {
    expect(lacks(data({ traits: null }))).toContain('Engage');
    expect(lacks(data({ traits: { version: CURATED_VERSION, champions: {} } }))).toContain('Engage');
    expect(lacks(data({ traits: { version: CURATED_VERSION } }))).toContain('Engage');
  });

  it('shows the combos that came with the numbers', () => {
    const Lulu = 1, Ahri = 2, Soraka = 3;
    const builder = new MetaBuilder();
    for (const [id, name, lane] of [[Lulu, 'Lulu', Lane.Support], [Soraka, 'Soraka', Lane.Support], [Ahri, 'Ahri', Lane.Mid]] as const)
      builder.champion(id, name).inLane(id, lane, { winRate: 0.5, play: 20000 });
    builder.snapshotRef().source = 'riot';
    const meta = builder.build();
    const state = draftStateFrom(new SessionBuilder().localPlayer(4).locked(2, Ahri).completedPick(2).build());
    const predictor = new LanePredictor(meta);
    const seat = state.allies.find((slot) => slot.cellId === 4)!;
    const chips = (recommender: Recommender): string[] =>
      recommender
        .recommend(state, { slot: seat, action: 'pick', isFollowingTurn: false }, predictor.predict(state.enemies), predictor.predict(state.allies))
        .items.find((item) => item.championId === Lulu)!
        .reasons.map((r) => r.text)
        .filter(isComboReason);

    // Built in, Ahri is no hypercarry; the published list makes her one.
    expect(chips(new Recommender(meta, TraitTable.empty))).toEqual([]);
    const published = readComboFile({ combos: { ...combosFile.combos, hypercarry: [...combosFile.combos.hypercarry, 'Ahri'] }, engageSkill: {} })!;
    expect(chips(new Recommender(meta, TraitTable.empty, undefined, null, undefined, published))).toEqual(['Enchanter für Ahri']);
    expect(new KitTable(meta.champions, published).has('hypercarry', Ahri)).toBe(true);
  });
});

describe('the curated files among the downloads', () => {
  const files: Record<string, string> = {
    'meta.json': JSON.stringify(snapshot),
    'traits.json': JSON.stringify({ version: CURATED_VERSION, champions: { Ahri: { engage: 1, peel: 0, cc: 2, scaling: 'mid', waveclear: 1 } } }),
    'combos.json': JSON.stringify({ version: CURATED_VERSION, combos: combosFile.combos, engageSkill: combosFile.engageSkill }),
  };

  /** The shell in memory: what is kept, and what the server offers as new. */
  const shell = (server: Record<string, string>, kept: Record<string, string> = {}): DataSource & { kept: Record<string, string> } => {
    const staged: Record<string, string> = {};
    return {
      kept,
      read: async (name) => kept[name] ?? null,
      check: async () => {
        const fresh = Object.keys(server).filter((name) => kept[name] !== server[name]);
        for (const name of fresh) staged[name] = server[name]!;
        return { staged: fresh, problems: [], reached: true };
      },
      readStaged: async (name) => staged[name] ?? null,
      commit: async (name) => void (kept[name] = staged[name]!),
      discard: async (name) => void delete staged[name],
    };
  };

  it('takes and keeps them like the numbers', async () => {
    const source = shell(files);
    const set = (await new DataStore(source).refresh())!;
    expect(set.traits).toEqual(JSON.parse(files['traits.json']!));
    expect(Object.keys((set.combos as { combos: object }).combos)).toEqual(Object.keys(combosFile.combos));
    expect(source.kept['traits.json']).toBe(files['traits.json']);

    // Offline next time: the kept copies.
    const kept = (await new DataStore(shell({}, source.kept)).loadKept())!;
    expect(kept.traits).toEqual(set.traits);
    expect(kept.combos).toEqual(set.combos);
  });

  it('does without them on a branch that has none yet, or a broken one', async () => {
    const before = (await new DataStore(shell({ 'meta.json': files['meta.json']! })).refresh())!;
    expect(before.traits).toBeNull();
    expect(before.combos).toBeNull();

    const source = shell({ ...files, 'traits.json': '{"version":1,"champions":{}}', 'combos.json': '{ kaputt' });
    const broken = (await new DataStore(source).refresh())!;
    expect(broken.traits).toBeNull();
    expect(broken.combos).toBeNull();
    expect(broken.problems.some((p) => p.startsWith('traits.json'))).toBe(true);
    expect(source.kept['traits.json']).toBeUndefined();
  });
});
