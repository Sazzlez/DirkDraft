// The data download: a broken file never replaces the kept one, and without any the app knows it.

import { describe, expect, it } from 'vitest';
import { emptySnapshot } from '../../src/core/model/metaLookup';
import { Lane } from '../../src/core/model/lane';
import { checkMeta, dataLine, loadDataSet, type DataCache } from '../../src/data/remote';

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

function memory(initial: Record<string, string> = {}): DataCache & { files: Record<string, string> } {
  const files = { ...initial };
  return {
    files,
    async read(name) {
      return files[name] ?? null;
    },
    async write(name, text) {
      files[name] = text;
    },
  };
}

const serve = (answers: Record<string, string | Error>) => async (url: string) => {
  const name = url.slice(url.lastIndexOf('/') + 1);
  const answer = answers[name];
  if (answer === undefined || answer instanceof Error) throw answer ?? new Error('404');
  return answer;
};

describe('data download', () => {
  it('uses and keeps a good download', async () => {
    const cache = memory();
    const set = await loadDataSet(serve({ 'meta.json': goodMeta, 'builds.json': goodBuilds }), cache);
    expect(set?.fresh).toBe(true);
    expect(set?.meta.games).toBe(12345);
    expect(cache.files['meta.json']).toBe(goodMeta);
  });

  it('falls back to the kept copy when the download fails', async () => {
    const cache = memory({ 'meta.json': goodMeta });
    const set = await loadDataSet(serve({ 'meta.json': new Error('offline') }), cache);
    expect(set?.fresh).toBe(false);
    expect(set?.builds).toBeNull();
    expect(set?.problems.join()).toContain('offline');
  });

  it('never stores a broken file over a good one', async () => {
    const cache = memory({ 'meta.json': goodMeta });
    const set = await loadDataSet(serve({ 'meta.json': '{"version":1,"patch":"16.23","champions":[]}' }), cache);
    expect(set?.meta.patch).toBe('16.22');
    expect(cache.files['meta.json']).toBe(goodMeta);
  });

  it('has nothing to offer without download and copy', async () => {
    expect(await loadDataSet(serve({}), memory())).toBeNull();
  });

  it('checks what the model needs', () => {
    expect(checkMeta(JSON.parse(goodMeta))).toBeNull();
    expect(checkMeta({ ...JSON.parse(goodMeta), laneStats: [] })).toBe('keine Lane-Zeilen');
    expect(checkMeta({ ...JSON.parse(goodMeta), version: 2 })).toContain('Version');
  });

  it('describes the data for the title bar', () => {
    expect(dataLine(JSON.parse(goodMeta), new Date('2026-11-06T21:00:00Z'))).toBe('Patch 16.22 · Platin · 12.345 Spiele · vor 3 Std.');
  });
});
