// DirkDraft's own data: meta.json (draft numbers) and builds.json (builds), both published by the
// collector to the branch "data" of the repository. Downloaded on start and on request, checked
// before use, and kept: a broken or missing download never replaces the last good copy, and without
// any copy the app says so instead of inventing numbers. Nothing about the user is sent — two plain
// GET requests for public files.

import type { MetaSnapshot } from '../core/model/metaLookup';
import type { RiotBuildFile } from '../core/riotBuilds';

export const DATA_BASE = 'https://raw.githubusercontent.com/Sazzlez/DirkDraft/data';

export const FILES = { meta: 'meta.json', builds: 'builds.json' } as const;
export type DataFile = keyof typeof FILES;

/** Where downloaded files are kept between runs (Tauri: app data folder; browser: localStorage). */
export interface DataCache {
  read(name: string): Promise<string | null>;
  write(name: string, text: string): Promise<void>;
}

export type FetchText = (url: string) => Promise<string>;

export interface DataSet {
  meta: MetaSnapshot;
  builds: RiotBuildFile | null;
  /** Whether this run's download succeeded, or the numbers are the kept copy. */
  fresh: boolean;
  problems: string[];
}

/** A meta file the model can stand on: own data, a patch, champions and lane rows. */
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

async function one<T>(
  name: DataFile,
  fetchText: FetchText,
  cache: DataCache,
  check: (value: unknown) => string | null,
  problems: string[],
): Promise<{ value: T; fresh: boolean } | null> {
  const file = FILES[name];
  try {
    const text = await fetchText(`${DATA_BASE}/${file}`);
    const value = JSON.parse(text) as unknown;
    const problem = check(value);
    if (problem) throw new Error(problem);
    await cache.write(file, text);
    return { value: value as T, fresh: true };
  } catch (error) {
    problems.push(`${file}: ${error instanceof Error ? error.message : String(error)}`);
  }

  const kept = await cache.read(file);
  if (kept === null) return null;
  try {
    const value = JSON.parse(kept) as unknown;
    return check(value) ? null : { value: value as T, fresh: false };
  } catch {
    return null;
  }
}

/** Both files, each freshly downloaded or else kept; null when there is no usable meta file at all. */
export async function loadDataSet(fetchText: FetchText, cache: DataCache): Promise<DataSet | null> {
  const problems: string[] = [];
  const [meta, builds] = await Promise.all([
    one<MetaSnapshot>('meta', fetchText, cache, checkMeta, problems),
    one<RiotBuildFile>('builds', fetchText, cache, checkBuilds, problems),
  ]);
  if (!meta) return null;
  return { meta: meta.value, builds: builds?.value ?? null, fresh: meta.fresh, problems };
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
