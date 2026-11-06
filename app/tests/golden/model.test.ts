// Golden parity: the TS model against the 1.5 model's own output (Tools -- golden). Same order,
// same tie groups and decimals, same texts; numbers far inside the ±0,05-point budget.

import { readdirSync, readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { describe, expect, it } from 'vitest';
import { MetaLookup } from '../../src/core/model/metaLookup';
import { TurnTracker } from '../../src/core/model/turnTracker';
import { compare, evaluate, loadContext, readJson, replay, stateFromGolden } from './harness';

/** Numbers agree to this in absolute terms — 1e-9 of a win rate is 1e-7 points. */
const TOLERANCE = 1e-9;

const ctx = loadContext();
const dir = (path: string): string => fileURLToPath(new URL(path, import.meta.url));

describe('golden drafts', () => {
  const files = readdirSync(dir('./drafts')).filter((f) => f.endsWith('.json'));
  it.each(files)('%s', (file) => {
    const golden = readJson<any>(`./drafts/${file}`);
    const meta = new MetaLookup(ctx.snapshot);
    const state = stateFromGolden(golden.frame.state);
    const target = new TurnTracker().resolve(state);
    const actual = evaluate(meta, ctx, state, target, golden.input.hover);
    expect(compare(golden.frame, actual, TOLERANCE)).toEqual([]);
  });
});

describe('golden recordings', () => {
  const files = readdirSync(dir('./recordings')).filter((f) => f.endsWith('.json'));
  it.each(files)('%s', (file) => {
    const golden = readJson<any>(`./recordings/${file}`);
    // File.ReadLines skips a byte-order mark; JSON.parse would choke on it.
    const text = readFileSync(dir(`../recordings/${golden.name}`), 'utf8').replace(/^﻿/, '');
    const lines = text.split(/\r?\n/);
    // File.ReadLines drops the empty string after a trailing newline.
    if (lines.length > 0 && lines[lines.length - 1] === '') lines.pop();
    const actual = replay(lines, ctx);
    expect(compare(golden.frames, actual, TOLERANCE)).toEqual([]);
  });
});
