// In ranked the whole team bans at once and sees each other's hovers. A champion a teammate is
// about to ban is no ban suggestion for anyone else — two bans on one champion waste one.

import { readFileSync } from 'node:fs';
import { describe, expect, it } from 'vitest';
import { DraftEngine } from '../../src/core/engine';
import type { MetaSnapshot } from '../../src/core/model/metaLookup';

const read = (path: string) => readFileSync(new URL(path, import.meta.url), 'utf8').replace(/^﻿/, '');
const snapshot = JSON.parse(read('../golden/snapshot.json')) as MetaSnapshot;

type Action = { actorCellId: number; championId: number; type: string; completed: boolean; isAllyAction: boolean };
type Session = { localPlayerCellId: number; actions: Action[][] };

const sessions = read('../recordings/live-ranked.jsonl')
  .split(/\r?\n/)
  .filter((line) => line.trim())
  .map((line) => JSON.parse(line) as { session?: Session | null; phase?: string })
  .filter((record) => record.phase === undefined && record.session)
  .map((record) => record.session!);

/** The first moment of the recording where the own team bans and a teammate's ban is still open. */
function banMoment(): { session: Session; mate: Action } {
  for (const session of sessions) {
    const open = session.actions.flat().filter((a) => a.type === 'ban' && a.isAllyAction && !a.completed);
    const mate = open.find((a) => a.actorCellId !== session.localPlayerCellId);
    if (open.some((a) => a.actorCellId === session.localPlayerCellId) && mate) return { session, mate };
  }
  throw new Error('live-ranked has no ban phase');
}

const bansOf = (session: unknown): number[] => new DraftEngine(snapshot).update(session).frame.bans!.items.map((item) => item.championId);

/** The session with one action's hover set, everything else as recorded. */
function hovering(session: Session, actor: number, championId: number): Session {
  const copy = structuredClone(session);
  for (const action of copy.actions.flat()) if (action.type === 'ban' && action.actorCellId === actor) action.championId = championId;
  return copy;
}

describe('ban hovers of teammates', () => {
  it('reads the recording: teammates show their ban hovers, the enemy never does', () => {
    const open = sessions.flatMap((s) => s.actions.flat()).filter((a) => a.type === 'ban' && !a.completed && a.championId !== 0);
    expect(open.length).toBeGreaterThan(0);
    expect(open.every((a) => a.isAllyAction)).toBe(true);
  });

  it("leaves out what another teammate is banning, and keeps the rest of the list's order", () => {
    const { session, mate } = banMoment();
    const before = bansOf(hovering(session, mate.actorCellId, 0));
    const top = before[0]!;

    const after = bansOf(hovering(session, mate.actorCellId, top));

    expect(after).not.toContain(top);
    expect(after.slice(0, before.length - 1)).toEqual(before.slice(1));
  });

  it('keeps the own ban hover in the own list', () => {
    const { session } = banMoment();
    const me = session.localPlayerCellId;
    const top = bansOf(hovering(session, me, 0))[0]!;

    expect(bansOf(hovering(session, me, top))[0]).toBe(top);
  });
});
