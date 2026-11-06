// "Mein Pool": who is in it, and that the pool list is the best of the pool, not a cut of the top 8.

import { describe, expect, it } from 'vitest';
import { DraftEngine, stateFromFrame, type DraftFrame } from '../../src/core/engine';
import { Lane } from '../../src/core/model/lane';
import { poolFor } from '../../src/core/pool';
import { loadContext, readJson } from '../golden/harness';
import { MetaBuilder } from './helpers';

const meta = new MetaBuilder()
  .champion(1, 'Darius')
  .champion(2, 'Thresh')
  .champion(3, 'Garen')
  .inLane(1, Lane.Top, { roleRate: 0.95 })
  .inLane(2, Lane.Support, { roleRate: 0.99 })
  .inLane(3, Lane.Top, { roleRate: 0.9 })
  .build();

describe('who is in the pool', () => {
  it('takes champions played at least twice on the lane', () => {
    const pool = poolFor(Lane.Top, { masteries: [], games: [{ championId: 1, lane: Lane.Top }, { championId: 1, lane: Lane.Top }, { championId: 3, lane: Lane.Top }] }, meta);
    expect([...pool.champions]).toEqual([1]);
    expect(pool.reasons.get(1)).toBe('2 Spiele auf Toplane zuletzt');
  });

  it('counts mastery only on lanes the champion is played on', () => {
    const input = { masteries: [{ championId: 2, points: 300_000 }], games: [] };
    expect(poolFor(Lane.Top, input, meta).champions.has(2)).toBe(false);
    expect(poolFor(Lane.Support, input, meta).reasons.get(2)).toBe('Meisterschaft 300.000');
  });

  it('follows the hand edits', () => {
    const pool = poolFor(
      Lane.Top,
      { masteries: [], games: [{ championId: 1, lane: Lane.Top }, { championId: 1, lane: Lane.Top }], edits: { added: [3], removed: [1] } },
      meta,
    );
    expect([...pool.champions]).toEqual([3]);
  });
});

describe('the pool list', () => {
  const ctx = loadContext();
  const golden = readJson<{ input: { hover: number }; frame: DraftFrame }>('./drafts/readme-top-vs-jax.json');
  const state = stateFromFrame(golden.frame.state);

  it('ranks the pool alone and leaves every score as it was', () => {
    // Two pool champions far down the full list: a filter of the top eight would show neither.
    const all = new DraftEngine(ctx.snapshot);
    const full = all.evaluate(state, all.resolveTarget(state), 0);
    const ranking = new DraftEngine(ctx.snapshot).meta.roster(Lane.Top);
    const deep = full.picks!.items.length; // 8
    const pool = new Set(ranking.filter((id) => !full.picks!.items.some((i) => i.championId === id)).slice(0, 2));

    const engine = new DraftEngine(ctx.snapshot, { pool: () => pool });
    const frame = engine.evaluate(state, engine.resolveTarget(state), 0);

    expect(deep).toBe(8);
    expect(frame.poolPicks!.items.map((i) => i.championId).sort()).toEqual([...pool].sort());
    expect(frame.picks).toEqual(full.picks);
  });

  it('is absent without a pool, so the golden frames stay key-exact', () => {
    const engine = new DraftEngine(ctx.snapshot);
    expect('poolPicks' in engine.evaluate(state, engine.resolveTarget(state), 0)).toBe(false);
  });
});
