// The reference writer must reproduce every file exactly, or a rebase would rewrite more than it
// changes. The rebase itself runs only on request, after a deliberate model change whose reason goes
// into the commit, the CHANGELOG and README.md ("Neu in 2.1"):
//
//   REBASE_GOLDEN=1 npx vitest run tests/golden/rebase
//
// It writes every changed value into the reference files and lists them in build/golden-rebase.txt.

import { mkdirSync, readdirSync, readFileSync, writeFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { describe, expect, it } from 'vitest';
import { stateFromFrame, type DraftFrame } from '../../src/core/engine';
import { engineFor, loadContext, replay } from './harness';
import { rebase, writeFile, type Change } from './rebase';

/** As model.test.ts. */
const TOLERANCE = 1e-9;

const dir = (path: string): string => fileURLToPath(new URL(path, import.meta.url));
const files = (sub: string): string[] => readdirSync(dir(sub)).filter((f) => f.endsWith('.json'));

describe('reference writer', () => {
  const all = [...files('./drafts').map((f) => `drafts/${f}`), ...files('./recordings').map((f) => `recordings/${f}`)];
  it.each(all)('writes %s byte for byte as the C# tool did', (file) => {
    const text = readFileSync(dir(`./${file}`), 'utf8');
    expect(writeFile(JSON.parse(text.replace(/^﻿/, '')), text) === text).toBe(true);
  });
});

const show = (value: unknown): string => {
  const text = JSON.stringify(value) ?? 'undefined';
  return text.length > 160 ? `${text.slice(0, 157)}…` : text;
};

describe.runIf(process.env.REBASE_GOLDEN === '1')('rebase onto the current model', () => {
  it('writes every differing value into the references', () => {
    const ctx = loadContext();
    const report: string[] = [];
    const record = (file: string, changes: Change[]) => {
      if (changes.length === 0) return;
      report.push(`## ${file}: ${changes.length} Werte`);
      for (const c of changes) report.push(`${c.path}\n    vorher ${show(c.expected)}\n    jetzt  ${show(c.actual)}`);
      report.push('');
    };

    for (const file of files('./drafts')) {
      const path = dir(`./drafts/${file}`);
      const text = readFileSync(path, 'utf8');
      const golden = JSON.parse(text.replace(/^﻿/, '')) as { input: { hover: number }; frame: DraftFrame };
      const engine = engineFor(ctx);
      const state = stateFromFrame(golden.frame.state);
      const changes: Change[] = [];
      const frame = rebase(golden.frame, engine.evaluate(state, engine.resolveTarget(state), golden.input.hover), TOLERANCE, '$.frame', changes);
      if (changes.length > 0) writeFileSync(path, writeFile({ ...golden, frame }, text));
      record(`drafts/${file}`, changes);
    }

    for (const file of files('./recordings')) {
      const path = dir(`./recordings/${file}`);
      const text = readFileSync(path, 'utf8');
      const golden = JSON.parse(text.replace(/^﻿/, '')) as { name: string; frames: unknown[] };
      const source = readFileSync(dir(`../recordings/${golden.name}`), 'utf8').replace(/^﻿/, '');
      const lines = source.split(/\r?\n/);
      if (lines.length > 0 && lines[lines.length - 1] === '') lines.pop();
      const changes: Change[] = [];
      const frames = rebase(golden.frames, replay(lines, ctx), TOLERANCE, '$.frames', changes);
      if (changes.length > 0) writeFileSync(path, writeFile({ ...golden, frames }, text));
      record(`recordings/${file}`, changes);
    }

    const out = dir('../../../build/golden-rebase.txt');
    mkdirSync(dir('../../../build'), { recursive: true });
    writeFileSync(out, report.length > 0 ? report.join('\n') : 'Keine Abweichung.\n');
  });
});
