// The postgame: the pick's place when it was locked, the build against the plan, the history.

import { describe, expect, it } from 'vitest';
import { DraftEngine } from '../../src/core/engine';
import { addToHistory, HISTORY_LENGTH, PickRecorder, postgameModel, topThreeLine, type HistoryEntry, type PickRecord } from '../../src/core/postgame';
import { loadContext } from '../golden/harness';
import { SessionBuilder } from './helpers';

const ctx = loadContext();

describe('recording the pick', () => {
  it('takes the place from the frame before the lock — the list the player chose from', () => {
    const engine = new DraftEngine(ctx.snapshot);
    const recorder = new PickRecorder();
    const base = () => new SessionBuilder().localPlayer(0).queue(420).locked(5, 24).onClock(0, 'pick');

    const hovering = engine.update(base().hovering(0, 122).build()).frame;
    expect(recorder.observe(hovering, 1)).toBeNull();

    const locked = engine.update(base().locked(0, 122).build()).frame;
    const record = recorder.observe(locked, 2)!;

    expect(record.championId).toBe(122);
    expect(record.rank).toBe(hovering.hover!.rank);
    expect(record.candidates).toBe(hovering.hover!.candidates);
    expect(record.fromTop3).toBe(record.rank <= 3 || record.tiedWithFirst);
    expect(recorder.observe(locked, 3)).toBeNull(); // once per lock
  });

  it('has no verdict in a mode without suggestions', () => {
    const engine = new DraftEngine(ctx.snapshot);
    const recorder = new PickRecorder();
    recorder.observe(engine.update(new SessionBuilder().localPlayer(0).queue(450).hovering(0, 122).build()).frame, 1);
    const record = recorder.observe(engine.update(new SessionBuilder().localPlayer(0).queue(450).locked(0, 122).build()).frame, 2);
    expect(record?.fromTop3).toBeNull();
  });
});

describe('the postgame', () => {
  const name = (id: number) => (id === 122 ? 'Darius' : `#${id}`);
  const record = (over: Partial<PickRecord>): PickRecord => ({ championId: 122, rank: 2, candidates: 58, tiedWithFirst: true, fromTop3: true, at: 0, ...over });

  it('says where the pick stood', () => {
    expect(postgameModel(122, record({}), { win: true, items: [] }, [], name).pickNote).toBe('Darius lag auf Platz 2, gleichauf mit Platz 1.');
    expect(postgameModel(122, record({ rank: 1 }), { win: true, items: [] }, [], name).pickNote).toBe('Darius war der beste Vorschlag.');
    expect(postgameModel(122, record({ rank: 9, tiedWithFirst: false, fromTop3: false }), { win: false, items: [] }, [], name).pickNote).toBe(
      'Darius lag auf Platz 9 von 58.',
    );
    expect(postgameModel(122, null, { win: false, items: [] }, [], name).pickNote).toBe('Für diesen Pick gab es keine Vorschläge.');
  });

  it('compares the final items with the planned core', () => {
    const end = (items: number[]) => ({ win: true, items });
    expect(postgameModel(122, null, end([1, 2, 3, 9]), [1, 2, 3], name).buildNote).toBe('Kern wie empfohlen.');
    expect(postgameModel(122, null, end([1, 9]), [1, 2, 3], name).buildNote).toBe('Vom empfohlenen Kern gebaut: 1 von 3.');
    expect(postgameModel(122, null, end([9]), [1, 2, 3], name).buildNote).toBe('Anderer Kern als empfohlen.');
  });

  it('keeps twenty games, newest first, and counts only those with suggestions', () => {
    let history: HistoryEntry[] = Array.from({ length: HISTORY_LENGTH }, (_, i) => ({ championId: i, win: true, fromTop3: i % 2 === 0, at: i }));
    history = addToHistory(history, { championId: 999, win: false, fromTop3: null, at: 100 });
    expect(history).toHaveLength(HISTORY_LENGTH);
    expect(history[0]!.championId).toBe(999);
    expect(topThreeLine(history)).toBe('10 von 19 Picks aus den ersten drei');
  });
});
