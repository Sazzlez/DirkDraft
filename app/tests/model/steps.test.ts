// The step line follows the draft's own order (core/steps.ts), checked on the three orders Riot runs
// and on real recordings.

import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { describe, expect, it } from 'vitest';
import { draftSteps, simultaneousTurn } from '../../src/core/steps';
import type { MetaSnapshot } from '../../src/core/model/metaLookup';
import { LiveSession } from '../../src/live/session';

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

  it('a real ranked draft (recorded 07.11.2026, scrubbed): ten bans at once, then the picks', () => {
    const read = (p: string) => readFileSync(fileURLToPath(new URL(p, import.meta.url)), 'utf8').replace(/^\uFEFF/, '');
    const snapshot = JSON.parse(read('../golden/snapshot.json')) as MetaSnapshot;
    const live = new LiveSession();
    live.setData({ meta: snapshot, builds: null, aram: null, situational: null, fresh: true, changed: false, problems: [] });
    live.onStatus({ leagueFound: true, clientRunning: true, socketConnected: true });
    const seen: string[] = [];
    const clocks = new Set<string>();
    for (const text of read('../recordings/live-ranked.jsonl').split(/\r?\n/).filter((l) => l.trim())) {
      const record = JSON.parse(text) as { phase?: string; session?: Record<string, unknown> | null };
      if (record.phase) live.onPhase(record.phase);
      else live.onSession(record.session ?? null);
      const board = live.view().board;
      if (!board) continue;
      clocks.add(board.turnText);
      const steps = line(board.steps);
      if (seen.at(-1) !== steps) seen.push(steps);
    }
    expect(seen).toEqual([
      'Banns Picks Finalisierung',
      '[Banns] Picks Finalisierung',
      '✓Banns [Picks] Finalisierung',
      '✓Banns ✓Picks [Finalisierung]',
      '✓Banns ✓Picks ✓Finalisierung',
    ]);
    // Planning, the own ban, then all the others at once — never one teammate for ten bans.
    expect(clocks.has('Planung')).toBe(true);
    expect(clocks.has('Du bannst')).toBe(true);
    expect([...clocks].some((c) => /^Teammate \d bannt$/.test(c))).toBe(false);
    expect(clocks.has('Ladebildschirm')).toBe(true);
  });

  it('says who acts when several seats are on the clock at once', () => {
    const at = (actors: [number, boolean, string][], local = 9) => ({
      localPlayerCellId: local,
      actions: [actors.map(([actorCellId, isAllyAction, type]) => ({ actorCellId, isAllyAction, type, isInProgress: true, completed: false }))],
    });
    expect(simultaneousTurn(at([[0, false, 'ban'], [6, true, 'ban']]))).toBe('Alle bannen');
    expect(simultaneousTurn(at([[6, true, 'pick'], [7, true, 'pick']]))).toBe('Dein Team pickt');
    expect(simultaneousTurn(at([[1, false, 'pick'], [2, false, 'pick']]))).toBe('Gegner picken');
    expect(simultaneousTurn(at([[9, true, 'ban'], [6, true, 'ban']]))).toBeNull();
    expect(simultaneousTurn(at([[6, true, 'pick']]))).toBeNull();
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
