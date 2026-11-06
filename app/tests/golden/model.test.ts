// Golden parity: the app's DraftEngine against the 1.5 model's own output (Tools -- golden). Same
// order, same tie groups and decimals, same texts; numbers far inside the ±0,05-point budget.

import { readdirSync, readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { describe, expect, it } from 'vitest';
import { stateFromFrame, type DraftFrame } from '../../src/core/engine';
import { compare, engineFor, loadContext, readJson, replay } from './harness';

/** Numbers agree to this in absolute terms — 1e-9 of a win rate is 1e-7 points. */
const TOLERANCE = 1e-9;

const ctx = loadContext();
const dir = (path: string): string => fileURLToPath(new URL(path, import.meta.url));

describe('golden drafts', () => {
  const files = readdirSync(dir('./drafts')).filter((f) => f.endsWith('.json'));
  it.each(files)('%s', (file) => {
    const golden = readJson<{ input: { hover: number }; frame: DraftFrame }>(`./drafts/${file}`);
    const engine = engineFor(ctx);
    const state = stateFromFrame(golden.frame.state);
    const actual = engine.evaluate(state, engine.resolveTarget(state), golden.input.hover);
    expect(compare(golden.frame, actual, TOLERANCE)).toEqual([]);
  });
});

describe('golden recordings', () => {
  const files = readdirSync(dir('./recordings')).filter((f) => f.endsWith('.json'));
  it.each(files)('%s', (file) => {
    const golden = readJson<{ name: string; frames: unknown[] }>(`./recordings/${file}`);
    // File.ReadLines skips a byte-order mark and the empty string after a trailing newline.
    const text = readFileSync(dir(`../recordings/${golden.name}`), 'utf8').replace(/^﻿/, '');
    const lines = text.split(/\r?\n/);
    if (lines.length > 0 && lines[lines.length - 1] === '') lines.pop();
    expect(compare(golden.frames, replay(lines, ctx), TOLERANCE)).toEqual([]);
  });
});
