// D1, team mode: the open seats of the own party planned together. The search must find the best
// combination (checked against trying every one where that is cheap), never pick a champion twice,
// and name who best picks first and last. Whom the plan is for comes from the client: the seats the
// local player queued with (a duo, three in Flex, a fixed five) — alone there is no plan.

import { readFileSync } from 'node:fs';
import { describe, expect, it } from 'vitest';
import { DraftEngine } from '../../src/core/engine';
import { draftStateFrom, partyCells } from '../../src/core/model/draftState';
import { LanePredictor, SeatPriors } from '../../src/core/model/lanePredictor';
import { MetaLookup, type MetaSnapshot } from '../../src/core/model/metaLookup';
import { Recommender } from '../../src/core/model/recommender';
import { logit, SYNERGY_DAMPING } from '../../src/core/model/scoreModel';
import { blindRisk, planTeam, TEAM_CANDIDATES } from '../../src/core/model/teamPlanner';
import { TraitTable } from '../../src/core/model/traits';
import priors from '../../src/core/model/data/pick_order_priors.json';
import { GoldenAdapter, type GoldenFrame } from '../../src/data/golden';
import { SessionBuilder } from './helpers';

const snapshot = JSON.parse(readFileSync(new URL('../golden/snapshot.json', import.meta.url), 'utf8').replace(/^﻿/, '')) as MetaSnapshot;

/**
 * Top, jungle and mid open; bot and support locked (Jinx, Thresh); Jax and Viego shown for the enemy.
 * A fixed five by default: everyone queued with the local player (cell 0).
 */
const session = (party: number[] = [1, 2, 3, 4]) =>
  new SessionBuilder().queue(420).localPlayer(0).party(...party).locked(3, 222).locked(4, 412).locked(5, 24).locked(6, 254).onClock(0, 'pick').build();

describe('team plan', () => {
  it('finds the same combination as trying every one', () => {
    const meta = new MetaLookup(snapshot);
    const traits = TraitTable.bundled();
    const recommender = new Recommender(meta, traits);
    const state = draftStateFrom(session());
    const predictor = new LanePredictor(meta, SeatPriors.fromJson(priors));
    const enemyLanes = predictor.predict(state.enemies, null);
    const allyLanes = predictor.predict(state.allies, null);

    const plan = planTeam(recommender, meta, state, enemyLanes, allyLanes, null)!;
    expect(plan.picks.map((p) => p.cellId)).toEqual([0, 1, 2]);

    const lists = [0, 1, 2].map(
      (cell) => recommender.recommend(state, { slot: state.allies[cell]!, action: 'pick', isFollowingTurn: false }, enemyLanes, allyLanes, null, TEAM_CANDIDATES).items,
    );
    const value = (picks: typeof lists[number]) => {
      let total = picks.reduce((sum, p) => sum + logit(p.score), 0);
      for (let i = 0; i < picks.length; i++)
        for (let j = 0; j < i; j++) {
          const s = meta.synergy(picks[i]!.championId, picks[j]!.championId);
          if (s) total += (SYNERGY_DAMPING / 2) * (logit(s.winRate) - logit(s.expected));
        }
      return total;
    };
    let best = -Infinity;
    for (const a of lists[0]!)
      for (const b of lists[1]!)
        for (const c of lists[2]!) {
          if (new Set([a.championId, b.championId, c.championId]).size < 3) continue;
          best = Math.max(best, value([a, b, c]));
        }

    expect(value(plan.picks.map((p) => p.item))).toBeCloseTo(best, 12);
    expect(new Set(plan.picks.map((p) => p.item.championId)).size).toBe(3);
  });

  it('names no order while nobody is exposed', () => {
    // Jax and Viego shown: top and jungle know their opponent; mid (Heimerdinger) has no open counter.
    const engine = new DraftEngine(snapshot);
    engine.setTeamMode(true);
    const plan = engine.update(session()).frame.teamPlan!;
    expect(plan.picks.every((p) => (p.item.openCounters ?? 0) === 0)).toBe(true);
    expect([plan.first, plan.last]).toEqual([null, null]);
  });

  it('orders by blind risk: fewest open counters first, a flex pick one fewer', () => {
    const frame = new DraftEngine(snapshot);
    frame.setTeamMode(true);
    // Nobody shown yet: every lane open, counters everywhere.
    const plan = frame.update(new SessionBuilder().queue(420).localPlayer(0).party(1, 2, 3, 4).onClock(0, 'pick').build()).frame.teamPlan!;
    expect(plan.picks).toHaveLength(5);
    const risk = (cellId: number) => {
      const item = plan.picks.find((p) => p.cellId === cellId)!.item;
      return (item.openCounters ?? 0) - (item.flexLanes ? 1 : 0);
    };

    const first = plan.first!;
    const last = plan.last!;
    expect([first, last]).not.toContain(null);
    for (const p of plan.picks) expect(risk(first)).toBeLessThanOrEqual(risk(p.cellId));
    for (const p of plan.picks) expect(risk(last)).toBeGreaterThanOrEqual(risk(p.cellId));
    expect(risk(first)).toBeLessThan(risk(last));
    expect(blindRisk({ championId: 1, name: '', score: 0.5, reasons: [], breakdown: [], uncertainty: 0, openCounters: 2, flexLanes: [0, 2] })).toBe(1);
  });

  it('stays off unless switched on, and shows as a tile when on', () => {
    expect(new DraftEngine(snapshot).update(session()).frame.teamPlan).toBeNull();

    const engine = new DraftEngine(snapshot);
    engine.setTeamMode(true);
    const frame = engine.update(session()).frame as unknown as GoldenFrame;
    const tile = new GoldenAdapter(snapshot.champions).teamPlan(frame)!;

    expect(tile.rows.map((r) => r.label)).toEqual(['Du', 'Teammate 2', 'Teammate 3']);
    expect(tile.rows.every((r) => /^\d+ %$/.test(r.value))).toBe(true);
    expect(tile.note).toContain('nicht eingerechnet');
    // A fixed five needs no "für euch".
    expect(tile.scope).toBeNull();
  });
});

describe('the party', () => {
  const planFor = (party: number[]) => {
    const engine = new DraftEngine(snapshot);
    engine.setTeamMode(true);
    return engine.update(session(party)).frame;
  };

  it('is the seats the local player queued with, the own one included', () => {
    expect([...partyCells(draftStateFrom(session([2])))].sort()).toEqual([0, 2]);
    expect([...partyCells(draftStateFrom(session([1, 4])))].sort()).toEqual([0, 1, 4]);
    expect(partyCells(draftStateFrom(session([]))).size).toBe(0);
    // The shell may flag the own seat too: alone is still alone.
    expect(partyCells(draftStateFrom(session([0]))).size).toBe(0);
  });

  it('plans nothing for a player who queued alone, team mode or not', () => {
    const frame = planFor([]);
    expect(frame.teamPlan).toBeNull();
    expect(frame.teamBans).toBeNull();
    // And the lists are the ones without team mode.
    expect(frame.picks).toEqual(new DraftEngine(snapshot).update(session([])).frame.picks);
  });

  it('plans a duo\'s two seats and leaves the others alone', () => {
    const frame = planFor([2]);
    expect(frame.teamPlan!.picks.map((p) => p.cellId)).toEqual([0, 2]);
    expect(new Set(frame.teamPlan!.picks.map((p) => p.item.championId)).size).toBe(2);

    const tile = new GoldenAdapter(snapshot.champions).teamPlan(frame as unknown as GoldenFrame)!;
    expect(tile.rows.map((r) => r.label)).toEqual(['Du', 'dein Duo']);
    expect(tile.scope).toBe('geplant für euch 2');
    expect(tile.note).toContain('eurer Party');
  });

  it('plans three in Flex, a locked party seat no more', () => {
    // Cells 0, 1 and 3 queued together; 3 has locked Jinx already.
    const frame = planFor([1, 3]);
    expect(frame.teamPlan!.picks.map((p) => p.cellId)).toEqual([0, 1]);
    expect(new GoldenAdapter(snapshot.champions).teamPlan(frame as unknown as GoldenFrame)!.scope).toBe('geplant für euch 3');
  });

  it('changes no pick list: the plan stands beside them', () => {
    const off = new DraftEngine(snapshot).update(session([2])).frame;
    const on = planFor([2]);
    expect(on.picks).toEqual(off.picks);
    expect(on.bans).toEqual(off.bans);
  });

  it('marks the party seats on the board without a name, and only them', () => {
    const adapter = new GoldenAdapter(snapshot.champions);
    const duo = adapter.board(planFor([2]) as unknown as GoldenFrame);
    expect(duo.allies.map((slot) => slot.party)).toEqual([null, null, 'dein Duo', null, null]);
    expect(duo.allies.map((slot) => slot.label)).toEqual(['Du', 'Teammate 2', 'dein Duo', 'Teammate 4', 'Teammate 5']);
    expect(duo.enemies.every((slot) => slot.party === null)).toBe(true);
    const three = adapter.board(planFor([1, 3]) as unknown as GoldenFrame);
    expect(three.allies.map((slot) => slot.party)).toEqual([null, 'Party', null, 'Party', null]);
    expect(three.allies.map((slot) => slot.label)).toEqual(['Du', 'Teammate 2', 'Teammate 3', 'Teammate 4', 'Teammate 5']);
    // The mark is there without team mode too: it says who is who, the plan is the setting.
    const off = adapter.board(new DraftEngine(snapshot).update(session([2])).frame as unknown as GoldenFrame);
    expect(off.allies[2]!.party).toBe('dein Duo');
  });

  it('writes the flag into the frame only where it is true', () => {
    const frame = planFor([2]);
    expect(frame.state.allies.map((slot) => 'inParty' in slot)).toEqual([false, false, true, false, false]);
    expect(frame.state.enemies.some((slot) => 'inParty' in slot)).toBe(false);
  });
});
