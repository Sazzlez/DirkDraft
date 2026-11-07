// The step line follows the draft's own order (core/steps.ts), checked on the three orders Riot runs
// and on real recordings.

import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { describe, expect, it } from 'vitest';
import { draftSteps } from '../../src/core/steps';

type A = { type: string; ally: boolean; done?: boolean; now?: boolean };
const a = (type: string, ally: boolean, done = false, now = false): A => ({ type, ally, done, now });
const session = (groups: A[][], phase = 'BAN_PICK') => ({
  timer: { phase },
  actions: groups.map((g) => g.map((x) => ({ type: x.type, isAllyAction: x.ally, completed: !!x.done, isInProgress: !!x.now }))),
});
const line = (steps: { label: string; state: string }[]) => steps.map((s) => (s.state === 'now' ? `[${s.label}]` : s.state === 'done' ? `✓${s.label}` : s.label)).join(' ');

const ten = (done: boolean) => [...Array(5)].flatMap(() => [a('ban', true, done), a('ban', false, done)]);

describe('draftSteps', () => {
  it('ranked and "Abwechselnde Wahl": one round of bans, then the picks', () => {
    const picks = [[a('pick', true)], [a('pick', false), a('pick', false)], [a('pick', true), a('pick', true)]];
    expect(line(draftSteps(session([ten(false), [a('ten_bans_reveal', false)], ...picks])))).toBe('[Banns] Picks Finalisierung');
    expect(line(draftSteps(session([ten(true), [a('ten_bans_reveal', false, true)], ...picks])))).toBe('✓Banns [Picks] Finalisierung');
  });

  it('tournament draft: two ban rounds, picks 1–3 and 4–5', () => {
    const bans = (n: number, done: boolean) => [...Array(n)].map((_, i) => [a('ban', i % 2 === 0, done)]);
    const picks = (pattern: boolean[], done: boolean) => pattern.map((ally) => [a('pick', ally, done)]);
    const groups = [...bans(6, true), ...picks([true, false, false, true, true, false], true), ...bans(4, false), ...picks([false, true, true, false], false)];
    expect(line(draftSteps(session(groups)))).toBe('✓Bann 1 ✓Pick 1–3 [Bann 2] Pick 4–5 Finalisierung');
  });

  it('blind pick: picks only', () => {
    expect(line(draftSteps(session([[a('pick', true, true), a('pick', false, true), a('pick', true, false, true)]])))).toBe('[Picks] Finalisierung');
  });

  it('planning marks nothing as current, finalization marks the last step, a past session all done', () => {
    const s = session([ten(false), [a('pick', true)]], 'PLANNING');
    expect(line(draftSteps(s))).toBe('Banns Picks Finalisierung');
    expect(line(draftSteps({ ...s, timer: { phase: 'FINALIZATION' } }))).toBe('✓Banns ✓Picks [Finalisierung]');
    expect(line(draftSteps(s, true))).toBe('✓Banns ✓Picks ✓Finalisierung');
  });

  it('modes without an order (ARAM) and broken input show no steps', () => {
    expect(draftSteps(null)).toEqual([]);
    expect(draftSteps({ actions: [] })).toEqual([]);
    expect(draftSteps({ actions: 'nope' })).toEqual([]);
    expect(draftSteps({ actions: [[null, { type: 'ten_bans_reveal' }]] })).toEqual([]);
  });

  it('a recorded custom draft ("Abwechselnde Wahl" with bots): bans, picks, finalization', () => {
    const path = fileURLToPath(new URL('../recordings/live-draft.jsonl', import.meta.url));
    const first = readFileSync(path, 'utf8')
      .split(/\r?\n/)
      .map((l) => {
        try {
          return JSON.parse(l) as { session?: unknown };
        } catch {
          return {};
        }
      })
      .find((r) => r.session);
    // The ban is already "in progress" during planning — the clock must still say planning.
    expect(line(draftSteps(first!.session))).toBe('Banns Picks Finalisierung');
  });
});
