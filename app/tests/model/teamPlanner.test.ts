// D1, team mode: the open seats planned together. The search must find the best combination (checked
// against trying every one where that is cheap), never pick a champion twice, and name who best picks
// first and last.

import { readFileSync } from 'node:fs';
import { describe, expect, it } from 'vitest';
import { DraftEngine } from '../../src/core/engine';
import { draftStateFrom } from '../../src/core/model/draftState';
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

/** Top, jungle and mid open; bot and support locked (Jinx, Thresh); Jax and Viego shown for the enemy. */
const session = () =>
  new SessionBuilder().queue(420).localPlayer(0).locked(3, 222).locked(4, 412).locked(5, 24).locked(6, 254).onClock(0, 'pick').build();

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
    const plan = frame.update(new SessionBuilder().queue(420).localPlayer(0).onClock(0, 'pick').build()).frame.teamPlan!;
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
  });
});
