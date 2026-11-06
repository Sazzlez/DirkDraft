// DirkDraft's own data: meta.json (draft numbers), builds.json (builds) and mayhem.json (ARAM
// Mayhem), published by the collector to the branch "data" of the repository. Checked before use
// and kept: a broken or missing download never replaces the last good copy, and without any copy
// the app says so instead of inventing numbers. Nothing about the user is sent — plain GET requests
// for public files. When the downloads may happen is the DataRefresher's business (core/refresh.ts).

import type { MetaSnapshot } from '../core/model/metaLookup';
import type { RiotMayhemFile } from '../core/mayhem';
import type { RiotBuildFile } from '../core/riotBuilds';
import type { RiotSituationalFile } from '../core/situational';

export const DATA_BASE = 'https://raw.githubusercontent.com/Sazzlez/DirkDraft/data';

export const FILES = { meta: 'meta.json', builds: 'builds.json', mayhem: 'mayhem.json', situational: 'situational.json' } as const;
export type DataFile = keyof typeof FILES;

/** Where downloaded files are kept between runs (Tauri: app data folder; browser: localStorage). */
export interface DataCache {
  read(name: string): Promise<string | null>;
  write(name: string, text: string): Promise<void>;
}

/**
 * One GET. With the stored ETag the server can answer "unchanged" without sending the file again —
 * which is what keeps a check every half hour at a few hundred bytes.
 */
export type FetchResult = { status: 'notModified' } | { status: 'ok'; text: string; etag: string | null };
export type Fetcher = (url: string, etag: string | null) => Promise<FetchResult>;

export interface DataSet {
  meta: MetaSnapshot;
  builds: RiotBuildFile | null;
  /** Absent until the collector has a Riot ID to start Mayhem from. */
  mayhem: RiotMayhemFile | null;
  /** Situational items for the game view; null until published. */
  situational: RiotSituationalFile | null;
  /** Whether the server was reached this time, or the numbers are the kept copy only. */
  fresh: boolean;
  /** Whether any file differs from the kept copy — false means nothing to swap in. */
  changed: boolean;
  problems: string[];
}

/** A meta file the model can stand on: a patch, champions, lane rows and the baselines. */
export function checkMeta(value: unknown): string | null {
  if (!value || typeof value !== 'object') return 'keine Daten';
  const meta = value as Partial<MetaSnapshot>;
  if (meta.version !== 1) return `unbekannte Version ${String(meta.version)}`;
  if (typeof meta.patch !== 'string' || meta.patch.length === 0) return 'kein Patch';
  if (!Array.isArray(meta.champions) || meta.champions.length === 0) return 'keine Champions';
  if (!Array.isArray(meta.laneStats) || meta.laneStats.length === 0) return 'keine Lane-Zeilen';
  if (!Array.isArray(meta.matchups) || !Array.isArray(meta.synergies)) return 'Duelle oder Duos fehlen';
  for (const key of ['laneBaseline', 'synergyBaseline', 'matchupBaseline', 'completeDuelSlope', 'completeDuelOffset'] as const) {
    if (typeof meta[key] !== 'number') return `${key} fehlt`;
  }
  return null;
}

export function checkBuilds(value: unknown): string | null {
  if (!value || typeof value !== 'object') return 'keine Daten';
  const file = value as Partial<RiotBuildFile>;
  if (file.version !== 1) return `unbekannte Version ${String(file.version)}`;
  if (!Array.isArray(file.builds)) return 'keine Builds';
  return null;
}

export function checkMayhem(value: unknown): string | null {
  if (!value || typeof value !== 'object') return 'keine Daten';
  const file = value as Partial<RiotMayhemFile>;
  if (file.version !== 1) return `unbekannte Version ${String(file.version)}`;
  if (!Array.isArray(file.champions) || !Array.isArray(file.augments) || !Array.isArray(file.builds)) return 'unvollständig';
  return null;
}

export function checkSituational(value: unknown): string | null {
  if (!value || typeof value !== 'object') return 'keine Daten';
  const file = value as Partial<RiotSituationalFile>;
  if (file.version !== 1) return `unbekannte Version ${String(file.version)}`;
  if (!Array.isArray(file.rows) || !Array.isArray(file.classes) || !Array.isArray(file.classNames)) return 'unvollständig';
  return null;
}

const CHECKS: Record<DataFile, (value: unknown) => string | null> = {
  meta: checkMeta,
  builds: checkBuilds,
  mayhem: checkMayhem,
  situational: checkSituational,
};

interface Loaded {
  value: unknown;
  fresh: boolean;
  changed: boolean;
}

async function kept(name: DataFile, cache: DataCache): Promise<unknown | null> {
  const text = await cache.read(FILES[name]);
  if (text === null) return null;
  try {
    const value = JSON.parse(text) as unknown;
    return CHECKS[name](value) ? null : value;
  } catch {
    return null;
  }
}

async function one(name: DataFile, fetcher: Fetcher, cache: DataCache, problems: string[]): Promise<Loaded | null> {
  const file = FILES[name];
  const etagName = `${file}.etag`;
  // An ETag without a usable copy is worthless: "unchanged" would leave nothing to read.
  const copy = await kept(name, cache);
  const etag = copy === null ? null : await cache.read(etagName);

  try {
    const answer = await fetcher(`${DATA_BASE}/${file}`, etag);
    if (answer.status === 'notModified') {
      if (copy !== null) return { value: copy, fresh: true, changed: false };
      throw new Error('unverändert gemeldet, aber keine Kopie');
    }
    const value = JSON.parse(answer.text) as unknown;
    const problem = CHECKS[name](value);
    if (problem) throw new Error(problem);
    const before = await cache.read(file);
    await cache.write(file, answer.text);
    if (answer.etag) await cache.write(etagName, answer.etag);
    return { value, fresh: true, changed: before !== answer.text };
  } catch (error) {
    problems.push(`${file}: ${error instanceof Error ? error.message : String(error)}`);
  }

  return copy === null ? null : { value: copy, fresh: false, changed: false };
}

/** All files, each downloaded (or confirmed unchanged) or else kept; null without any usable meta. */
export async function loadDataSet(fetcher: Fetcher, cache: DataCache): Promise<DataSet | null> {
  const problems: string[] = [];
  const [meta, builds, mayhem, situational] = await Promise.all([
    one('meta', fetcher, cache, problems),
    one('builds', fetcher, cache, problems),
    one('mayhem', fetcher, cache, problems),
    one('situational', fetcher, cache, problems),
  ]);
  if (!meta) return null;
  return {
    meta: meta.value as MetaSnapshot,
    builds: (builds?.value as RiotBuildFile | undefined) ?? null,
    mayhem: (mayhem?.value as RiotMayhemFile | undefined) ?? null,
    situational: (situational?.value as RiotSituationalFile | undefined) ?? null,
    fresh: meta.fresh,
    changed: meta.changed || !!builds?.changed || !!mayhem?.changed || !!situational?.changed,
    problems,
  };
}

/** The kept copies only — no network. What the app starts with, whatever the client is doing. */
export async function loadKept(cache: DataCache): Promise<DataSet | null> {
  const [meta, builds, mayhem, situational] = await Promise.all([
    kept('meta', cache),
    kept('builds', cache),
    kept('mayhem', cache),
    kept('situational', cache),
  ]);
  if (meta === null) return null;
  return {
    meta: meta as MetaSnapshot,
    builds: (builds as RiotBuildFile | null) ?? null,
    mayhem: (mayhem as RiotMayhemFile | null) ?? null,
    situational: (situational as RiotSituationalFile | null) ?? null,
    fresh: false,
    changed: false,
    problems: [],
  };
}

const BRACKETS: Record<string, string> = {
  iron: 'Eisen',
  bronze: 'Bronze',
  silver: 'Silber',
  gold: 'Gold',
  platinum: 'Platin',
  emerald: 'Smaragd',
  diamond: 'Diamant',
  all: 'alle Ränge',
};

/** "Patch 16.22 · Platin · 12.345 Spiele · vor 3 Std." for the title bar. */
export function dataLine(meta: MetaSnapshot, now: Date): string {
  const parts = [`Patch ${meta.patch}`];
  if (meta.tier) parts.push(BRACKETS[meta.tier] ?? meta.tier);
  if (meta.games) parts.push(`${meta.games.toLocaleString('de-DE')} Spiele`);
  const asOf = Date.parse(meta.dataAsOfUtc ?? meta.builtAtUtc);
  if (Number.isFinite(asOf)) parts.push(age(now.getTime() - asOf));
  return parts.join(' · ');
}

function age(ms: number): string {
  const minutes = Math.max(0, Math.round(ms / 60000));
  if (minutes < 60) return minutes <= 1 ? 'gerade eben' : `vor ${minutes} Min.`;
  const hours = Math.round(minutes / 60);
  if (hours < 24) return `vor ${hours} Std.`;
  const days = Math.round(hours / 24);
  return days === 1 ? 'vor 1 Tag' : `vor ${days} Tagen`;
}

/**
 * The browser's fetch, for the preview. `no-cache` makes the browser revalidate with its own stored
 * ETag, so an unchanged file costs a 304 there too; the Tauri shell brings its own fetcher.
 */
export const browserFetcher: Fetcher = async (url) => {
  const response = await fetch(url, { cache: 'no-cache' });
  if (!response.ok) throw new Error(`HTTP ${response.status}`);
  return { status: 'ok', text: await response.text(), etag: response.headers.get('etag') };
};

/** localStorage as a cache, for the browser preview; every access may throw (private mode). */
export const browserCache: DataCache = {
  async read(name) {
    try {
      return localStorage.getItem(`dirkdraft:${name}`);
    } catch {
      return null;
    }
  },
  async write(name, text) {
    try {
      localStorage.setItem(`dirkdraft:${name}`, text);
    } catch {
      // A full or blocked storage costs the offline copy, not the data in hand.
    }
  },
};
