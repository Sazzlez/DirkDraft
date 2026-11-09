// D3, team mode: the team's bans as one set — what teammates hover is set, the rest the strongest
// bans of every seat, at most two per lane — and a hint where a champion breaks the own plan.

import { readFileSync } from 'node:fs';
import { describe, expect, it } from 'vitest';
import { DraftEngine } from '../../src/core/engine';
import type { MetaSnapshot } from '../../src/core/model/metaLookup';
import { TEAM_BANS_PER_LANE } from '../../src/core/model/teamPlanner';
import { GoldenAdapter, type GoldenFrame } from '../../src/data/golden';
import { SessionBuilder } from './helpers';

const snapshot = JSON.parse(readFileSync(new URL('../golden/snapshot.json', import.meta.url), 'utf8').replace(/^\uFEFF/, '')) as MetaSnapshot;

/** Everyone banning at once; teammate 3 (cell 2) hovers Malphite, cell 4 has banned Zed already. */
const banPhase = () =>
  new SessionBuilder()
    .queue(420)
    .localPlayer(0)
    .onClock(0, 'ban')
    .onClock(1, 'ban')
    .action(2, 54, 'ban', false, true)
    .onClock(3, 'ban')
    .completedBan(4, 238)
    .build();

describe('team bans', () => {
  it('fills the open bans: hovers set, the rest spread over the lanes', () => {
    const engine = new DraftEngine(snapshot);
    engine.setTeamMode(true);
    const frame = engine.update(banPhase()).frame;
    const bans = frame.teamBans!;

    expect(bans).toHaveLength(4); // five bans, Zed done
    expect(bans[0]).toMatchObject({ championId: 54, by: 2 });
    expect(new Set(bans.map((b) => b.championId)).size).toBe(4);
    expect(bans.some((b) => b.championId === 238)).toBe(false);
    const perLane = new Map<string, number>();
    for (const b of bans) perLane.set(b.lane, (perLane.get(b.lane) ?? 0) + 1);
    for (const n of perLane.values()) expect(n).toBeLessThanOrEqual(TEAM_BANS_PER_LANE);

    const tile = new GoldenAdapter(snapshot.champions).teamBans(frame as unknown as GoldenFrame)!;
    expect(tile.rows[0]!.by).toBe('Teammate 3 hovert ihn');
    expect(tile.rows.filter((r) => r.mine)).toHaveLength(1);
    expect(tile.rows.find((r) => r.mine)!.by).toBeNull();
  });

  it('stays off unless switched on', () => {
    expect(new DraftEngine(snapshot).update(banPhase()).frame.teamBans).toBeNull();
  });

  it('names a hard engage as a threat to a poke plan, without changing the ban value', () => {
    // Xerath, Ziggs and Ezreal locked: a poke plan. Hard engage breaks it.
    const session = new SessionBuilder().queue(420).localPlayer(4).locked(0, 101).locked(1, 115).locked(2, 81).onClock(4, 'ban').build();
    const frame = new DraftEngine(snapshot).update(session).frame;
    expect(frame.comp.allies.plan).toBe('poke');

    const threatened = frame.bans!.items.filter((i) => i.reasons.some((r) => r.text === 'bedroht euren Poke-Plan'));
    expect(threatened.length).toBeGreaterThan(0);
    for (const item of threatened) expect(item.reasons.find((r) => r.text === 'bedroht euren Poke-Plan')!.tone).toBe('neutral');
  });
});
