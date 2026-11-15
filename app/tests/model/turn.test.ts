// What the clock says (core/turn.ts), checked on made-up action groups and on a real recording.

import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { describe, expect, it } from 'vitest';
import { simultaneousTurn } from '../../src/core/turn';
import type { MetaSnapshot } from '../../src/core/model/metaLookup';
import { LiveSession } from '../../src/live/session';

describe('the clock', () => {
  it('a real ranked draft (recorded 07.11.2026, scrubbed): ten bans at once, then the picks', () => {
    const read = (p: string) => readFileSync(fileURLToPath(new URL(p, import.meta.url)), 'utf8').replace(/^﻿/, '');
    const snapshot = JSON.parse(read('../golden/snapshot.json')) as MetaSnapshot;
    const live = new LiveSession();
    live.setData({ meta: snapshot, builds: null, aram: null, situational: null, fresh: true, changed: false, problems: [] });
    live.onStatus({ leagueFound: true, clientRunning: true, socketConnected: true });
    const clocks = new Set<string>();
    for (const text of read('../recordings/live-ranked.jsonl').split(/\r?\n/).filter((l) => l.trim())) {
      const record = JSON.parse(text) as { phase?: string; session?: Record<string, unknown> | null };
      if (record.phase) live.onPhase(record.phase);
      else live.onSession(record.session ?? null);
      const board = live.view().board;
      if (board) clocks.add(board.turnText);
    }
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
});
