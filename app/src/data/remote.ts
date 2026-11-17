// DirkDraft's own data: meta.json (draft numbers), builds.json (builds), aram.json (plain ARAM,
// standing in for Mayhem, which Riot does not hand out), situational.json and the curated traits.json
// and combos.json (data/ in the repository), published by the collector to the branch "data".
//
// The shell downloads (src-tauri/src/data.rs): one conditional GET per file with the stored ETag,
// and a file is written only when the server sends a new one. Until 17.11.2026 the page fetched
// itself and, on every check without new data, read 40,6 MB and wrote 20,3 MB through the IPC — every
// half hour. Now a check without new data reads nothing, writes nothing and parses nothing here.
//
// Checked before use and kept: a new file is staged by the shell, read and checked here, and only
// then replaces the kept copy (commit) — a broken download never does, and is not fetched again
// (discard). Without any copy the app says so instead of inventing numbers. Nothing about the user
// is sent — plain GET requests for public files. When a check may happen is the DataRefresher's
// business (core/refresh.ts).

import type { MetaSnapshot } from '../core/model/metaLookup';
import type { RiotAramFile } from '../core/aram';
import type { RiotBuildFile } from '../core/riotBuilds';
import { readComboFile } from '../core/model/kits';
import type { RiotSituationalFile } from '../core/situational';

/** The files by name, as on the branch (https://raw.githubusercontent.com/Sazzlez/DirkDraft/data/, data.rs BASE and NAMES). */
export const FILES = {
  meta: 'meta.json',
  builds: 'builds.json',
  aram: 'aram.json',
  situational: 'situational.json',
  traits: 'traits.json',
  combos: 'combos.json',
} as const;
export type DataFile = keyof typeof FILES;

/** What one check against the server found. */
export interface CheckOutcome {
  /** File names with a new version waiting: downloaded, not yet the kept copy. */
  staged: string[];
  problems: string[];
  /** The server answered for at least one file. */
  reached: boolean;
}

/**
 * Where the files are fetched and kept: the shell (platform/shell.ts shellSource). Reads hand out
 * text; nothing here writes a file's content.
 */
export interface DataSource {
  /** The kept copy of a file, or null. */
  read(name: string): Promise<string | null>;
  /** One round of conditional requests; unchanged files cost a few hundred bytes and nothing else. */
  check(): Promise<CheckOutcome>;
  /** A file the last check staged. */
  readStaged(name: string): Promise<string | null>;
  /** The staged file is good: it becomes the kept copy. */
  commit(name: string): Promise<void>;
  /** The staged file is unusable: thrown away, and that version is not fetched again. */
  discard(name: string): Promise<void>;
}

export interface DataSet {
  meta: MetaSnapshot;
  builds: RiotBuildFile | null;
  /** The ARAM count for the bench and the ARAM build; null until published. */
  aram: RiotAramFile | null;
  /** Situational items for the game view; null until published. */
  situational: RiotSituationalFile | null;
  /**
   * The curated champion traits and combo lists as published with the numbers (data/ in the
   * repository); absent or null until downloaded — the copies built into the app stand in.
   */
  traits?: Record<string, unknown> | null;
  combos?: Record<string, unknown> | null;
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

export function checkAram(value: unknown): string | null {
  if (!value || typeof value !== 'object') return 'keine Daten';
  const file = value as Partial<RiotAramFile>;
  if (file.version !== 1) return `unbekannte Version ${String(file.version)}`;
  if (!Array.isArray(file.champions) || !Array.isArray(file.builds)) return 'unvollständig';
  return null;
}

/** The situational.json layout this reader understands. */
export const SITUATIONAL_VERSION = 2;

export function checkSituational(value: unknown): string | null {
  if (!value || typeof value !== 'object') return 'keine Daten';
  const file = value as Partial<RiotSituationalFile>;
  // Version 2 (RiotSituationalFile.CurrentVersion in C#): rows carry the game state. Version 1 had
  // no state column, which this reader would misread — and 2 is what the collector publishes.
  if (file.version !== SITUATIONAL_VERSION) return `unbekannte Version ${String(file.version)}`;
  if (!Array.isArray(file.rows) || !Array.isArray(file.classes) || !Array.isArray(file.classNames)) return 'unvollständig';
  return null;
}

/** The layout of traits.json and combos.json this reader understands (CuratedFiles.Version in C#). */
export const CURATED_VERSION = 1;

export function checkTraits(value: unknown): string | null {
  if (!value || typeof value !== 'object') return 'keine Daten';
  const file = value as { version?: unknown; champions?: unknown };
  if (file.version !== CURATED_VERSION) return `unbekannte Version ${String(file.version)}`;
  const champions = file.champions;
  if (!champions || typeof champions !== 'object' || Array.isArray(champions) || Object.keys(champions).length === 0) return 'keine Champions';
  return null;
}

export function checkCombos(value: unknown): string | null {
  if (!value || typeof value !== 'object') return 'keine Daten';
  if ((value as { version?: unknown }).version !== CURATED_VERSION) return `unbekannte Version ${String((value as { version?: unknown }).version)}`;
  return readComboFile(value) ? null : 'Listen fehlen';
}

const CHECKS: Record<DataFile, (value: unknown) => string | null> = {
  meta: checkMeta,
  builds: checkBuilds,
  aram: checkAram,
  situational: checkSituational,
  traits: checkTraits,
  combos: checkCombos,
};

const NAMES = Object.keys(FILES) as DataFile[];

/** Text to a value the app can stand on, or the reason it cannot. */
function parse(name: DataFile, text: string): { value: unknown } | { problem: string } {
  try {
    const value = JSON.parse(text) as unknown;
    const problem = CHECKS[name](value);
    return problem ? { problem } : { value };
  } catch (error) {
    return { problem: error instanceof Error ? error.message : String(error) };
  }
}

/**
 * The files as the app holds them: parsed once and kept in memory, so a check that finds nothing
 * new costs no read and no parse. One store per run of the app.
 */
export class DataStore {
  private readonly values = new Map<DataFile, unknown>();
  private keptRead = false;

  constructor(private readonly source: DataSource) {}

  /** The kept copies only — no network. What the app starts with, whatever the client is doing. */
  async loadKept(): Promise<DataSet | null> {
    await this.readKept();
    return this.set(false, false, []);
  }

  /**
   * One check. Only the files the server has new are read and parsed; each replaces its kept copy
   * once it passed its check, and is thrown away otherwise. Null without any usable meta.
   */
  async refresh(): Promise<DataSet | null> {
    await this.readKept();
    const problems: string[] = [];
    let outcome: CheckOutcome;
    try {
      outcome = await this.source.check();
    } catch (error) {
      outcome = { staged: [], problems: [error instanceof Error ? error.message : String(error)], reached: false };
    }
    problems.push(...outcome.problems);

    let changed = false;
    for (const name of NAMES) {
      const file = FILES[name];
      if (!outcome.staged.includes(file)) continue;
      const text = await this.source.readStaged(file).catch(() => null);
      const read = text === null ? { problem: 'nicht lesbar' } : parse(name, text);
      if ('problem' in read) {
        problems.push(`${file}: ${read.problem}`);
        await this.source.discard(file).catch(() => undefined);
        continue;
      }
      // Kept first, then used: a copy that could not be written is still the data in hand.
      await this.source.commit(file).catch((error: unknown) => problems.push(`${file}: ${error instanceof Error ? error.message : String(error)}`));
      this.values.set(name, read.value);
      changed = true;
    }
    return this.set(outcome.reached, changed, problems);
  }

  /** The kept copies, read and parsed once per run. */
  private async readKept(): Promise<void> {
    if (this.keptRead) return;
    this.keptRead = true;
    await Promise.all(
      NAMES.map(async (name) => {
        const text = await this.source.read(FILES[name]).catch(() => null);
        if (text === null) return;
        const read = parse(name, text);
        if ('value' in read) this.values.set(name, read.value);
      }),
    );
  }

  private set(fresh: boolean, changed: boolean, problems: string[]): DataSet | null {
    const meta = this.values.get('meta');
    if (!meta) return null;
    return {
      meta: meta as MetaSnapshot,
      builds: (this.values.get('builds') as RiotBuildFile | undefined) ?? null,
      aram: (this.values.get('aram') as RiotAramFile | undefined) ?? null,
      situational: (this.values.get('situational') as RiotSituationalFile | undefined) ?? null,
      traits: (this.values.get('traits') as Record<string, unknown> | undefined) ?? null,
      combos: (this.values.get('combos') as Record<string, unknown> | undefined) ?? null,
      fresh,
      changed,
      problems,
    };
  }
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

/** "Patch 16.22 · vor 3 Std." where the full line has no room. */
export function dataLineShort(meta: MetaSnapshot, now: Date): string {
  const asOf = Date.parse(meta.dataAsOfUtc ?? meta.builtAtUtc);
  return Number.isFinite(asOf) ? `Patch ${meta.patch} · ${age(now.getTime() - asOf)}` : `Patch ${meta.patch}`;
}

function age(ms: number): string {
  const minutes = Math.max(0, Math.round(ms / 60000));
  if (minutes < 60) return minutes <= 1 ? 'gerade eben' : `vor ${minutes} Min.`;
  const hours = Math.round(minutes / 60);
  if (hours < 24) return `vor ${hours} Std.`;
  const days = Math.round(hours / 24);
  return days === 1 ? 'vor 1 Tag' : `vor ${days} Tagen`;
}
