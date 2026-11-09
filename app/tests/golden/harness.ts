// Mirrors Tools -- golden (GoldenCommand.Evaluate / Replay) on top of the app's own DraftEngine, so
// the golden test checks exactly the code the window runs.

import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import type { AramStat } from '../../src/core/model/advisors';
import type { MetaSnapshot } from '../../src/core/model/metaLookup';
import { DraftEngine } from '../../src/core/engine';

const here = (path: string): string => fileURLToPath(new URL(path, import.meta.url));

export const readJson = <T>(path: string): T => JSON.parse(readFileSync(here(path), 'utf8')) as T;

export interface Context {
  snapshot: MetaSnapshot;
  aramStats: ReadonlyMap<number, AramStat>;
}

export function loadContext(): Context {
  const snapshot = readJson<MetaSnapshot>('./snapshot.json');
  const aram = readJson<AramStat[]>('./aram-stats.json');
  return { snapshot, aramStats: new Map(aram.map((s) => [s.championId, s])) };
}

export const engineFor = (ctx: Context): DraftEngine => new DraftEngine(ctx.snapshot, { aramStats: ctx.aramStats });

/** GoldenCommand.Replay: one frame per line, gameflow lines and unreadable lines marked as such. */
export function replay(lines: string[], ctx: Context): unknown[] {
  const engine = engineFor(ctx);
  const frames: unknown[] = [];
  lines.forEach((line, index) => {
    if (line.trim() === '') return;
    let root: unknown;
    try {
      root = JSON.parse(line);
    } catch {
      frames.push({ line: index, skipped: 'unlesbar' });
      return;
    }
    const record = (root ?? {}) as Record<string, unknown>;
    if (typeof record.phase === 'string') {
      frames.push({ line: index, gameflowPhase: record.phase });
      return;
    }
    const { isNewDraft, frame } = engine.update(record.session ?? null);
    frames.push({ line: index, isNewDraft, frame });
  });
  return frames;
}

// ---- comparison -----------------------------------------------------------------------------------

export interface Difference {
  path: string;
  expected: unknown;
  actual: unknown;
}

/**
 * Fields the 2.1 engine adds to a frame that the 1.5 reference never had (README.md, "Neu in 2.1").
 * Only these may be absent from a reference frame; a changed value in any field still counts.
 */
export const ADDED_IN_2_1: readonly string[] = ['state.allyBanIntents', 'gap', 'strongWaveclearCount', 'state.allyPickOrder', 'openCounters', 'pickHint', 'earlyScalingCount', 'plan', 'flexLanes'];

const isAdded = (path: string): boolean => {
  const bare = path.replace(/\[\d+\]/g, '');
  return ADDED_IN_2_1.some((field) => bare.endsWith(`.${field}`));
};

/** Strings, booleans, order and lengths exactly; numbers to `tolerance`; "NaN" matches NaN. */
export function compare(expected: unknown, actual: unknown, tolerance: number, path = '$', out: Difference[] = []): Difference[] {
  if (out.length > 40) return out;
  if (expected === 'NaN' && typeof actual === 'number' && Number.isNaN(actual)) return out;
  if (typeof expected === 'number' && typeof actual === 'number') {
    if (!(Math.abs(expected - actual) <= tolerance)) out.push({ path, expected, actual });
    return out;
  }
  if (Array.isArray(expected)) {
    if (!Array.isArray(actual) || actual.length !== expected.length) {
      out.push({ path: `${path}.length`, expected: expected.length, actual: Array.isArray(actual) ? actual.length : actual });
      if (!Array.isArray(actual)) return out;
    }
    const n = Math.min(expected.length, (actual as unknown[]).length);
    for (let i = 0; i < n; i++) compare(expected[i], (actual as unknown[])[i], tolerance, `${path}[${i}]`, out);
    return out;
  }
  if (expected !== null && typeof expected === 'object') {
    if (actual === null || typeof actual !== 'object') {
      out.push({ path, expected: '{…}', actual });
      return out;
    }
    const e = expected as Record<string, unknown>;
    const a = actual as Record<string, unknown>;
    for (const key of Object.keys(e)) compare(e[key], a[key], tolerance, `${path}.${key}`, out);
    for (const key of Object.keys(a)) if (!(key in e) && !isAdded(`${path}.${key}`)) out.push({ path: `${path}.${key}`, expected: undefined, actual: a[key] });
    return out;
  }
  if (expected !== actual) out.push({ path, expected, actual });
  return out;
}
