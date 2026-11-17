// The data files as the page holds them (data/remote.ts DataStore): the shell fetches and keeps,
// the page reads and parses only what is new. A broken file never replaces the kept one, a check
// without new data costs no read, no write and no parse, and without any data the app knows it.

import { describe, expect, it } from 'vitest';
import { emptySnapshot } from '../../src/core/model/metaLookup';
import { Lane } from '../../src/core/model/lane';
import { checkMeta, checkSituational, dataLine, DataStore, type DataSource } from '../../src/data/remote';

const goodMeta = JSON.stringify({
  ...emptySnapshot(),
  patch: '16.22',
  tier: 'platinum',
  source: 'riot',
  games: 12345,
  dataAsOfUtc: '2026-11-06T18:00:00Z',
  champions: [{ id: 1, key: 'Annie', name: 'Annie', damage: 2, tags: [], attackRange: 625, defense: 3 }],
  laneStats: [{ championId: 1, lane: Lane.Mid, winRate: 0.5, pickRate: 0.01, banRate: 0, roleRate: 1, tier: -1, play: 100 }],
});
const goodBuilds = JSON.stringify({ version: 1, patch: '16.22', platform: 'euw1', collectedAtUtc: '', matches: {}, builds: [] });

/**
 * The shell, in memory: kept copies, what the server has, and a count of everything asked of it.
 * `server` maps a file name to its content; a file whose content equals the kept (or a rejected)
 * one is "not modified", as with an ETag.
 */
function shell(kept: Record<string, string> = {}, server: Record<string, string> | Error = {}) {
  const files = { ...kept };
  const staged: Record<string, string> = {};
  const rejected: Record<string, string> = {};
  const calls = { read: [] as string[], readStaged: [] as string[], commit: [] as string[], discard: [] as string[], check: 0 };
  const state = { server };
  const source: DataSource = {
    async read(name) {
      calls.read.push(name);
      return files[name] ?? null;
    },
    async check() {
      calls.check++;
      if (state.server instanceof Error) return { staged: [], problems: [state.server.message], reached: false };
      const fresh: string[] = [];
      for (const [name, text] of Object.entries(state.server)) {
        if (files[name] === text || rejected[name] === text) continue;
        staged[name] = text;
        fresh.push(name);
      }
      return { staged: fresh, problems: [], reached: true };
    },
    async readStaged(name) {
      calls.readStaged.push(name);
      return staged[name] ?? null;
    },
    async commit(name) {
      calls.commit.push(name);
      files[name] = staged[name]!;
      delete staged[name];
    },
    async discard(name) {
      calls.discard.push(name);
      rejected[name] = staged[name]!;
      delete staged[name];
    },
  };
  return { source, files, calls, state };
}

describe('data download', () => {
  it('uses and keeps a good download', async () => {
    const s = shell({}, { 'meta.json': goodMeta, 'builds.json': goodBuilds });
    const set = await new DataStore(s.source).refresh();
    expect(set?.fresh).toBe(true);
    expect(set?.changed).toBe(true);
    expect(set?.meta.games).toBe(12345);
    expect(set?.builds).not.toBeNull();
    expect(s.files['meta.json']).toBe(goodMeta);
    expect(s.calls.commit.sort()).toEqual(['builds.json', 'meta.json']);
  });

  it('reads, writes and parses nothing on a check without new data', async () => {
    const s = shell({ 'meta.json': goodMeta, 'builds.json': goodBuilds }, { 'meta.json': goodMeta, 'builds.json': goodBuilds });
    const store = new DataStore(s.source);
    const kept = await store.loadKept();
    expect(kept?.meta.patch).toBe('16.22');
    const readsAtStart = s.calls.read.length;

    // Every half hour, for as long as the app runs: nothing but the check itself.
    const parse = JSON.parse;
    let parsed = 0;
    JSON.parse = ((...args: Parameters<typeof JSON.parse>) => (parsed++, parse(...args))) as typeof JSON.parse;
    try {
      for (let i = 0; i < 3; i++) {
        const set = await store.refresh();
        expect(set?.fresh).toBe(true);
        expect(set?.changed).toBe(false);
        // The very values of the first read, not copies parsed again.
        expect(set?.meta).toBe(kept?.meta);
      }
    } finally {
      JSON.parse = parse;
    }
    expect(parsed).toBe(0);
    expect(s.calls.check).toBe(3);
    expect(s.calls.read.length).toBe(readsAtStart);
    expect(s.calls.readStaged).toEqual([]);
    expect(s.calls.commit).toEqual([]);
    expect(s.calls.discard).toEqual([]);
  });

  it('reads and parses only the file that is new', async () => {
    const s = shell({ 'meta.json': goodMeta, 'builds.json': goodBuilds }, { 'meta.json': goodMeta, 'builds.json': goodBuilds });
    const store = new DataStore(s.source);
    const kept = await store.loadKept();

    const newer = goodBuilds.replace('"16.22"', '"16.23"');
    s.state.server = { 'meta.json': goodMeta, 'builds.json': newer };
    const set = await store.refresh();
    expect(set?.changed).toBe(true);
    expect(set?.builds?.patch).toBe('16.23');
    expect(set?.meta).toBe(kept?.meta);
    expect(s.calls.readStaged).toEqual(['builds.json']);
    expect(s.calls.commit).toEqual(['builds.json']);
    expect(s.files['builds.json']).toBe(newer);
  });

  it('falls back to the kept copy when the server cannot be reached', async () => {
    const s = shell({ 'meta.json': goodMeta }, new Error('offline'));
    const set = await new DataStore(s.source).refresh();
    expect(set?.fresh).toBe(false);
    expect(set?.changed).toBe(false);
    expect(set?.builds).toBeNull();
    expect(set?.problems.join()).toContain('offline');
  });

  it('never keeps a broken file over a good one, and does not fetch it again', async () => {
    const broken = '{"version":1,"patch":"16.23","champions":[]}';
    const s = shell({ 'meta.json': goodMeta }, { 'meta.json': broken });
    const store = new DataStore(s.source);
    const set = await store.refresh();
    expect(set?.meta.patch).toBe('16.22');
    expect(set?.changed).toBe(false);
    expect(set?.problems.join()).toContain('meta.json');
    expect(s.files['meta.json']).toBe(goodMeta);
    expect(s.calls.discard).toEqual(['meta.json']);
    expect(s.calls.commit).toEqual([]);

    // The same broken version next time: the shell does not stage it again, nothing is read.
    const again = await store.refresh();
    expect(again?.problems).toEqual([]);
    expect(s.calls.readStaged).toEqual(['meta.json']);
  });

  it('has nothing to offer without download and copy', async () => {
    expect(await new DataStore(shell().source).refresh()).toBeNull();
    expect(await new DataStore(shell({}, new Error('offline')).source).refresh()).toBeNull();
  });

  it('reads the kept copy without any request, and a kept file that is broken as none', async () => {
    const s = shell({ 'meta.json': goodMeta, 'builds.json': '{ kaputt' });
    const set = await new DataStore(s.source).loadKept();
    expect(set?.meta.patch).toBe('16.22');
    expect(set?.builds).toBeNull();
    expect(set?.fresh).toBe(false);
    expect(s.calls.check).toBe(0);
    expect(await new DataStore(shell().source).loadKept()).toBeNull();
  });

  it('survives a shell that fails on a read or a write', async () => {
    const s = shell({}, { 'meta.json': goodMeta });
    s.source.commit = async () => {
      throw new Error('Datenträger voll');
    };
    // The copy could not be kept; the numbers in hand are still used.
    const set = await new DataStore(s.source).refresh();
    expect(set?.meta.games).toBe(12345);
    expect(set?.problems.join()).toContain('Datenträger voll');
  });

  it('checks what the model needs', () => {
    expect(checkMeta(JSON.parse(goodMeta))).toBeNull();
    expect(checkMeta({ ...JSON.parse(goodMeta), laneStats: [] })).toBe('keine Lane-Zeilen');
    expect(checkMeta({ ...JSON.parse(goodMeta), version: 2 })).toContain('Version');
  });

  it('describes the data for the title bar', () => {
    expect(dataLine(JSON.parse(goodMeta), new Date('2026-11-06T21:00:00Z'))).toBe('Patch 16.22 · Platin · 12.345 Spiele · vor 3 Std.');
  });

  it('accepts situational.json as the collector writes it (version 2, rows with the game state)', () => {
    // The first 2.0 build checked for version 1 and threw the published file away.
    const published = { version: 2, patch: '16.22', platform: 'euw1', collectedAtUtc: '', situations: [], classNames: [], rows: [[1, 0, 1, 2, 0, 3065, 10, 5]], classes: [] };
    expect(checkSituational(published)).toBeNull();
    expect(checkSituational({ ...published, version: 1 })).toContain('Version');
  });
});
