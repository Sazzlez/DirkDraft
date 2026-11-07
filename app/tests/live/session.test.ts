// The live session fed like the shell feeds it: client status, phases, recorded sessions, the game.

import { readFileSync } from 'node:fs';
import { describe, expect, it } from 'vitest';
import type { MetaSnapshot } from '../../src/core/model/metaLookup';
import type { DataSet } from '../../src/data/remote';
import { LiveSession } from '../../src/live/session';

const read = (path: string) => readFileSync(new URL(path, import.meta.url), 'utf8').replace(/^﻿/, '');
const snapshot = JSON.parse(read('../golden/snapshot.json')) as MetaSnapshot;
const data: DataSet = { meta: snapshot, builds: null, aram: null, situational: null, fresh: true, changed: false, problems: [] };
const sessions = (name: string) =>
  read(`../recordings/${name}.jsonl`)
    .split(/\r?\n/)
    .filter((line) => line.trim())
    .map((line) => JSON.parse(line) as { session?: Record<string, unknown> | null; phase?: string })
    .filter((record) => record.phase === undefined);

const connected = { leagueFound: true, clientRunning: true, socketConnected: true };

describe('LiveSession', () => {
  it('says what is missing before data and client are there', () => {
    const live = new LiveSession();
    expect(live.view().connection).toEqual({ state: 'off', text: 'startet …' });
    expect(live.view().emptyText).toMatch(/geladen/);
    live.onStatus({ leagueFound: false, clientRunning: false, socketConnected: false });
    expect(live.view().connection.text).toBe('League nicht gefunden');
  });

  it('shows the lobby when connected without a draft', () => {
    const live = new LiveSession();
    live.setData(data);
    live.onStatus(connected);
    live.onPhase('Lobby');
    const view = live.view();
    expect(view.moment).toBe('lobby');
    expect(view.main.kind).toBe('lobby');
    expect(view.data.text).toMatch(/^Patch /);
  });

  it('follows a recorded draft to the own lock', () => {
    const live = new LiveSession();
    live.setData(data);
    live.onStatus(connected);
    live.onPhase('ChampSelect');
    for (const record of sessions('state-nachlock')) live.onSession(record.session ?? null);
    const view = live.view();
    expect(view.board).not.toBeNull();
    expect(view.moment).toBe('locked');
    expect(view.main.kind).toBe('matchup');
    // No build file loaded: nothing to import, and the view says no build rather than inventing one.
    expect(live.importPlan()).toBeNull();
  });

  it('closes the board when the client says champion select is over', () => {
    const live = new LiveSession();
    live.setData(data);
    live.onStatus(connected);
    for (const record of sessions('state-nachlock')) live.onSession(record.session ?? null);
    live.onSession(null);
    live.onPhase('Lobby');
    expect(live.view().board).toBeNull();
  });

  it('counts the clock down between session events', () => {
    const live = new LiveSession();
    live.setData(data);
    live.onStatus(connected);
    live.onPhase('ChampSelect');
    const last = sessions('live-tournament-draft').filter((r) => r.session).at(-1)!.session!;
    const session = { ...last, timer: { adjustedTimeLeftInPhase: 20_000 } };
    live.onSession(session, 1_000);
    expect(live.view(1_000).board?.seconds).toBe(20);
    expect(live.view(6_000).board?.seconds).toBe(15);
  });

  it('keeps the matchup on screen through the loading screen', () => {
    const live = new LiveSession();
    live.setData(data);
    live.onStatus(connected);
    live.onPhase('ChampSelect');
    for (const record of sessions('state-nachlock')) live.onSession(record.session ?? null);
    live.onSession(null);
    live.onPhase('GameStart');
    const view = live.view();
    expect(view.moment).toBe('loading');
    expect(view.main.kind).toBe('matchup');
    expect(view.board?.seconds).toBeNull();
    live.onPhase('Lobby');
    expect(live.view().board).toBeNull();
  });

  it('treats "InProgress" without game data as the loading screen', () => {
    const live = new LiveSession();
    live.setData(data);
    live.onStatus(connected);
    live.onPhase('ChampSelect');
    for (const record of sessions('state-nachlock')) live.onSession(record.session ?? null);
    live.onSession(null);
    live.onPhase('GameStart');
    live.onPhase('InProgress');
    expect(live.view().moment).toBe('loading');
    expect(live.view().main.kind).toBe('matchup');
    live.onGame(read('../fixtures/live-mayhem.json'));
    expect(live.view().moment).toBe('game');
  });

  it('shows no ban steps in blind pick', () => {
    const live = new LiveSession();
    live.setData(data);
    live.onStatus(connected);
    live.onPhase('ChampSelect');
    const last = sessions('live-tournament-draft').filter((r) => r.session).at(-1)!.session!;
    live.onSession({ ...last, bans: { myTeamBans: [], theirTeamBans: [], numBans: 0 } });
    expect(live.view().board?.steps.map((s) => s.label)).toEqual(['Picks', 'Finalisierung']);
    live.onSession({ ...last, bans: { myTeamBans: [], theirTeamBans: [], numBans: 10 } });
    expect(live.view().board?.steps.map((s) => s.label)).toContain('Bann 1');
  });

  it('suggests only champions the player may pick, for the own seat only', () => {
    const live = new LiveSession();
    live.setData(data);
    live.onStatus(connected);
    live.onPhase('ChampSelect');
    const last = sessions('live-tournament-draft').filter((r) => r.session).at(-1)!.session!;
    live.onSession(last);
    const before = live.view().main;
    if (before.kind !== 'picks' && before.kind !== 'bans') throw new Error(`kein Pick-Tile: ${before.kind}`);
    expect(live.needsSelectable).toBe(true);

    const first = before.tile.items[0]!.champion.id;
    const owned = snapshot.champions.map((c) => c.id).filter((id) => id !== first);
    live.setSelectable(owned);
    expect(live.needsSelectable).toBe(false);
    const after = live.view().main;
    if (after.kind === 'picks') expect(after.tile.items.map((i) => i.champion.id)).not.toContain(first);

    // A new draft asks again: the free rotation may have changed.
    live.onSession(null);
    live.onSession(last);
    expect(live.needsSelectable).toBe(true);
  });

  it('shows the game from the live client', () => {
    const live = new LiveSession();
    live.setData(data);
    live.onStatus(connected);
    live.onPhase('InProgress');
    live.onGame(read('../fixtures/live-mayhem.json'));
    const view = live.view();
    expect(view.moment).toBe('game');
    expect(view.connection.text).toBe('im Spiel');
    expect(view.game?.enemies).toHaveLength(5);
    expect(view.game?.situational).toBeNull();
  });
});
