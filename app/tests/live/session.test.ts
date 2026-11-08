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
    // No build file loaded: nothing to import, and the button says why rather than inventing one.
    expect(live.importPlan()).toBe('Für Darius gibt es noch keinen Build.');
    expect(view.canImport).toBe(true);
  });

  it('takes over nothing outside champion select', () => {
    const live = new LiveSession();
    live.setData(data);
    live.onStatus(connected);
    live.onPhase('Lobby');
    expect(live.importPlan()).toBe('Übernehmen geht nur im Champ Select.');
    expect(live.view().canImport).toBe(false);
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

  it('reads the step line from the draft order: blind pick has no ban steps', () => {
    const live = new LiveSession();
    live.setData(data);
    live.onStatus(connected);
    live.onPhase('ChampSelect');
    const last = sessions('live-tournament-draft').filter((r) => r.session).at(-1)!.session!;
    const pick = { id: 1, actorCellId: 0, championId: 0, type: 'pick', completed: false, isAllyAction: true, isInProgress: true };
    live.onSession({ ...last, timer: { phase: 'BAN_PICK' }, actions: [[pick]] });
    expect(live.view().board?.steps.map((s) => s.label)).toEqual(['Picks', 'Finalisierung']);
    const ban = { ...pick, id: 2, type: 'ban' };
    live.onSession({ ...last, timer: { phase: 'BAN_PICK' }, actions: [[ban], [pick]] });
    expect(live.view().board?.steps.map((s) => `${s.label}:${s.state}`)).toEqual(['Banns:now', 'Picks:next', 'Finalisierung:next']);
    // The loading screen looks back on a finished draft, and says what it is.
    live.onSession(null);
    live.onPhase('GameStart');
    expect(live.view().board?.steps.every((s) => s.state === 'done')).toBe(true);
    expect(live.view().board?.turnText).toBe('Ladebildschirm');
  });

  it('Arena and rotating modes: no list, a word why; ARAM without numbers: no empty bench', () => {
    const live = new LiveSession();
    live.setData(data);
    live.onStatus(connected);
    live.onPhase('ChampSelect');
    const last = sessions('state-myturn').filter((r) => r.session).at(-1)!.session!;
    live.onSession({ ...last, queueId: 1700 });
    const arena = live.view();
    expect(arena.board!.mode).toBe('Arena');
    expect(arena.main.kind).toBe('lobby');
    expect(arena.emptyText).toMatch(/keine Lanes/);

    const bench = sessions('state-aram-bench').filter((r) => r.session).at(-1)!.session!;
    live.onSession(bench);
    const aram = live.view();
    expect(aram.moment).toBe('aramBench');
    expect(aram.main.kind).toBe('lobby');
    expect(aram.emptyText).toMatch(/ARAM-Zahlen/);
  });

  it('names every queue and says where Flex numbers come from', () => {
    const live = new LiveSession();
    live.setData(data);
    live.onStatus(connected);
    live.onPhase('ChampSelect');
    const last = sessions('state-myturn').filter((r) => r.session).at(-1)!.session!;
    live.onSession({ ...last, queueId: 440 });
    expect(live.view().board!.mode).toBe('Ranked Flex');
    expect(live.view().board!.modeNote).toBe('Zahlen aus Solo/Duo');
    live.onSession({ ...last, queueId: 430 });
    expect(live.view().board!.mode).toBe('Normal Blind');
  });

  it('puts no guessed lane on an enemy seat nobody has shown yet', () => {
    const live = new LiveSession();
    live.setData(data);
    live.onStatus(connected);
    live.onPhase('ChampSelect');
    const last = sessions('state-myturn').filter((r) => r.session).at(-1)!.session!;
    const theirTeam = (last.theirTeam as Record<string, unknown>[]).map((p) => ({ ...p, championId: 0, championPickIntent: 0 }));
    live.onSession({ ...last, theirTeam });
    expect(live.view().board!.enemies.map((e) => e.lane)).toEqual([null, null, null, null, null]);
  });

  it('says "Planung" while the first ban is only marked in progress', () => {
    const live = new LiveSession();
    live.setData(data);
    live.onStatus(connected);
    live.onPhase('ChampSelect');
    const first = sessions('live-draft').find((r) => r.session)!.session!;
    live.onSession(first);
    const board = live.view().board!;
    expect(board.turnText).toBe('Planung');
    expect(board.myTurn).toBe(false);
  });

  it('a click on a teammate shows that seat’s list until the clock moves on, without the own hover', () => {
    const live = new LiveSession();
    live.setData(data);
    live.onStatus(connected);
    live.onPhase('ChampSelect');
    // Ranked, own pick locked, two teammates still choosing.
    const open = sessions('live-ranked').filter((r) => r.session)[40]!.session!;
    live.onSession(open);
    const before = live.view();
    const mate = before.board!.allies.find((s) => !s.isMe && s.canTarget && !s.isTarget && (!s.champion || s.hovering))!;
    live.selectSeat(mate.cellId);
    const after = live.view();
    expect(after.board!.allies.find((s) => s.cellId === mate.cellId)!.isTarget).toBe(true);
    if (after.main.kind !== 'picks') throw new Error(after.main.kind);
    expect(after.main.tile.title.startsWith(`Picks für ${mate.label}`)).toBe(true);
    expect(after.main.tile.hover).toBeNull();
  });

  it('a click on a teammate who has locked in shows his matchup, without a build to import', () => {
    const live = new LiveSession();
    live.setData(data);
    live.onStatus(connected);
    live.onPhase('ChampSelect');
    live.onSession(sessions('live-ranked').filter((r) => r.session)[40]!.session!);
    const mate = live.view().board!.allies.find((s) => !s.isMe && s.canTarget && s.champion && !s.hovering && s.lane)!;
    live.selectSeat(mate.cellId);
    const view = live.view();
    if (view.main.kind !== 'matchup') throw new Error(view.main.kind);
    expect(view.main.matchup.title).toBe(`Matchup von ${mate.label}`);
    expect(view.main.matchup.mine).toBe(false);
    expect(view.main.matchup.me.id).toBe(mate.champion!.id);
    expect(view.main.matchup.lane).toBe(mate.lane);
    expect(view.main.matchup.verdict).not.toMatch(/dich/);
    expect(view.main.build).toBeNull();
  });

  it('a lane set by hand holds and goes back to the prediction on "automatisch"', () => {
    const live = new LiveSession();
    live.setData(data);
    live.onStatus(connected);
    live.onPhase('ChampSelect');
    const last = sessions('build-demo').filter((r) => r.session).at(-1)!.session!;
    live.onSession(last);
    const enemy = live.view().board!.enemies.find((s) => s.champion && s.laneEditable)!;
    const other = (['top', 'jungle', 'mid', 'adc', 'support'] as const).find((l) => l !== enemy.lane)!;
    live.setManualLane(enemy.cellId, other);
    const fixed = live.view().board!.enemies.find((s) => s.cellId === enemy.cellId)!;
    expect(fixed.lane).toBe(other);
    expect(fixed.laneManual).toBe(true);
    live.setManualLane(enemy.cellId, null);
    expect(live.view().board!.enemies.find((s) => s.cellId === enemy.cellId)!.lane).toBe(enemy.lane);
  });

  it('data from an older patch than the client widen the error bars and say so', () => {
    const live = new LiveSession();
    live.setData(data);
    live.onStatus(connected);
    live.onPhase('ChampSelect');
    const last = sessions('state-myturn').filter((r) => r.session).at(-1)!.session!;
    live.onSession(last);
    const errorOf = (v: ReturnType<LiveSession['view']>) => (v.main.kind === 'picks' ? v.main.tile.items[0]!.errorPoints : NaN);
    const same = live.view();
    expect(same.data.chip).toBeNull();
    live.setClientPatch(`${snapshot.patch}.1+branch.releases`);
    expect(live.view().data.chip).toBeNull();
    const [major, minor] = snapshot.patch.split('.').map(Number);
    live.setClientPatch(`${major}.${minor! + 2}.1`);
    const later = live.view();
    expect(later.data.chip).toMatch(/der Client spielt/);
    expect(errorOf(later)).toBeGreaterThan(errorOf(same));
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
