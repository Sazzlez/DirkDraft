// B1: the pick position. Early with the lane opponent open, a pick with several open counters is a
// risk the list can name — swap the order with a later teammate, or take the equally strong pick that
// is easiest to answer. A counter pick or the team's last pick looks at the comp gap first.

import { readFileSync } from 'node:fs';
import { describe, expect, it } from 'vitest';
import { DraftEngine, pickHintOf, type FrameItem, type FrameSet } from '../../src/core/engine';
import { draftStateFrom } from '../../src/core/model/draftState';
import type { MetaSnapshot } from '../../src/core/model/metaLookup';
import { GoldenAdapter, type GoldenFrame } from '../../src/data/golden';
import { SessionBuilder } from './helpers';

const snapshot = JSON.parse(readFileSync(new URL('../golden/snapshot.json', import.meta.url), 'utf8').replace(/^﻿/, '')) as MetaSnapshot;
const adapter = new GoldenAdapter(snapshot.champions);

const item = (championId: number, openCounters?: number): FrameItem => ({
  championId,
  name: `#${championId}`,
  score: 0.5,
  uncertainty: 0,
  uncertaintyPoints: 0,
  reasons: [],
  breakdown: [],
  ...(openCounters !== undefined ? { openCounters } : {}),
});

const set = (tied: number, ...items: FrameItem[]): FrameSet => ({ cellId: 0, lane: 'top', action: 'pick', tied, decimals: 0, items });

/** Top picks first of five, the rest of the team after it; no enemy shown yet. */
const blindTop = () =>
  new SessionBuilder()
    .queue(420)
    .localPlayer(0)
    .onClock(0, 'pick')
    .action(1, 0, 'pick', false, false)
    .action(2, 0, 'pick', false, false)
    .action(3, 0, 'pick', false, false)
    .action(4, 0, 'pick', false, false);

describe('pick position', () => {
  it('reads the own team’s pick order from the actions', () => {
    const state = draftStateFrom(blindTop().build());

    expect(state.allyPickOrder).toEqual([0, 1, 2, 3, 4]);
  });

  it('marks the pick with the fewest open counters in the leading tie group', () => {
    const hint = pickHintOf(set(3, item(1, 4), item(2, 1), item(3, 2), item(4, 0)), null, 0, [0, 1]);

    // Item 4 has none, but it is not in the tie group: equally strong only counts there.
    expect(hint).toEqual({ risky: { championId: 1, counters: 4 }, safest: { championId: 2, counters: 1 }, laterAlly: true, gapFirst: false });
  });

  it('takes the hover when it is the riskier one, and stays quiet below two counters', () => {
    expect(pickHintOf(set(2, item(1, 1), item(2, 0)), item(9, 3), 0, [])!.risky).toEqual({ championId: 9, counters: 3 });
    expect(pickHintOf(set(2, item(1, 1), item(2, 0)), item(9, 1), 0, [])!.risky).toBeNull();
  });

  it('puts the comp gap first for the last pick of the team and once the lane opponent is known', () => {
    const last = pickHintOf(set(2, item(1, 3), item(2, 1)), null, 4, [0, 1, 4]);
    expect(last).toMatchObject({ laterAlly: false, gapFirst: true });
    expect(pickHintOf(set(1, item(1)), null, 0, [0, 1])).toMatchObject({ risky: null, gapFirst: true });
  });

  it('counts open counters on every pick while the lane is open, and none once the opponent is known', () => {
    const engine = new DraftEngine(snapshot);
    const open = engine.update(blindTop().build()).frame;
    expect(open.picks!.items.every((i) => i.openCounters !== undefined)).toBe(true);

    // Jax locked on the enemy side: the predictor puts him top, the duel is known.
    const known = new DraftEngine(snapshot).update(blindTop().locked(5, 24).build()).frame;
    expect(known.state.enemies.find((s) => s.lockedChampionId === 24)).toBeDefined();
    expect(known.picks!.items.every((i) => i.openCounters === undefined)).toBe(true);
    expect(known.pickHint).toMatchObject({ gapFirst: true });
  });

  it('shows the swap question and the safe pick in the list', () => {
    // Malphite hovered top: two open counters in the reference data, while the leading tie group
    // holds picks with none.
    const frame = new DraftEngine(snapshot).update(blindTop().hovering(0, 54).build()).frame as unknown as GoldenFrame;
    const hint = frame.pickHint!;
    const tile = adapter.pickTile(frame.picks!, frame, 'Picks für dich');

    expect(hint.risky).toEqual({ championId: 54, counters: 2 });
    expect(hint.safest).not.toBeNull();
    expect(tile.note).toMatchObject({ kind: 'early', title: 'Früh dran — Pick-Reihenfolge tauschen?' });
    expect(tile.note!.kind === 'early' && tile.note!.detail).toContain('Malphite hat noch 2 offene Counter auf Toplane');
    const badged = tile.items.filter((row) => row.badge !== null);
    expect(badged.map((row) => row.champion.id)).toEqual([hint.safest!.championId]);
    expect(badged[0]!.badge).toBe('sicherer Blind Pick');
  });

  it('shows the comp gap above the list for the last pick of the team', () => {
    const session = new SessionBuilder()
      .queue(420)
      .localPlayer(4)
      .locked(0, 157)
      .locked(1, 64)
      .locked(2, 238)
      .locked(3, 119)
      .completedPick(0)
      .completedPick(1)
      .completedPick(2)
      .completedPick(3)
      .onClock(4, 'pick')
      .build();
    const frame = new DraftEngine(snapshot).update(session).frame as unknown as GoldenFrame;
    const tile = adapter.pickTile(frame.picks!, frame, 'Picks für dich');

    expect(frame.pickHint).toMatchObject({ laterAlly: false, gapFirst: true });
    expect(tile.note).toMatchObject({ kind: 'gap', line: { lead: 'Eurem Team fehlt noch' } });
  });
});
