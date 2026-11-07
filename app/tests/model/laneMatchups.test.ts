// Port of tests/DraftPilot.Core.Tests/LaneMatchupsTests.cs.
//
// The lane-by-lane overview the in-game view shows. Two properties matter: it pairs the right
// champions on the right lanes, and it never fills a gap with a number — a missing duel statistic
// and an even duel look nothing alike to a reader, so they must not look alike in the data.

import { describe, expect, it } from 'vitest';
import { laneMatchupForSeat, laneMatchups, type LaneMatchup } from '../../src/core/model/advisors';
import { draftStateFrom } from '../../src/core/model/draftState';
import { Lane } from '../../src/core/model/lane';
import { LanePredictionResult, LanePredictor } from '../../src/core/model/lanePredictor';
import type { MetaLookup } from '../../src/core/model/metaLookup';
import { MetaBuilder, SessionBuilder } from './helpers';

const AllyTop = 1, AllyMid = 2, EnemyTop = 3, EnemyMid = 4;

/** C# LaneMatchup.HasWinRate. */
const hasWinRate = (row: LaneMatchup): boolean => row.winRate !== null;

function meta(withTopMatchup = true): MetaLookup {
  const builder = new MetaBuilder()
    .champion(AllyTop, 'AllyTop')
    .inLane(AllyTop, Lane.Top, { winRate: 0.52, play: 5000 })
    .champion(AllyMid, 'AllyMid')
    .inLane(AllyMid, Lane.Mid, { winRate: 0.5, play: 5000 })
    .champion(EnemyTop, 'EnemyTop')
    .inLane(EnemyTop, Lane.Top, { winRate: 0.5, play: 5000 })
    .champion(EnemyMid, 'EnemyMid')
    .inLane(EnemyMid, Lane.Mid, { winRate: 0.5, play: 5000 });

  if (withTopMatchup) builder.matchup(AllyTop, EnemyTop, Lane.Top, 0.58, 4000);

  return builder.build();
}

function predict(m: MetaLookup, withEnemies = true): { allies: LanePredictionResult; enemies: LanePredictionResult } {
  const session = new SessionBuilder().localPlayer(0).locked(0, AllyTop).locked(2, AllyMid);

  if (withEnemies) session.locked(5, EnemyTop).locked(7, EnemyMid);

  const state = draftStateFrom(session.build());
  const predictor = new LanePredictor(m);

  return { allies: predictor.predict(state.allies), enemies: predictor.predict(state.enemies) };
}

function single(rows: readonly LaneMatchup[], lane: Lane): LaneMatchup {
  const matches = rows.filter((row) => row.lane === lane);
  expect(matches).toHaveLength(1);
  return matches[0]!;
}

describe('LaneMatchupsTests', () => {
  it('EachOccupiedLane_BecomesOneRow_InDraftOrder', () => {
    const m = meta();
    const { allies, enemies } = predict(m);

    const rows = laneMatchups(m, allies, enemies);

    expect(rows.map((row) => row.lane)).toEqual([Lane.Top, Lane.Mid]);
  });

  it('ALaneNobodyPlaysOn_IsLeftOut', () => {
    const m = meta();
    const { allies, enemies } = predict(m);

    const rows = laneMatchups(m, allies, enemies);

    expect(rows.map((row) => row.lane)).not.toContain(Lane.Jungle);
    expect(rows.map((row) => row.lane)).not.toContain(Lane.Support);
  });

  it('AKnownDuel_CarriesItsRateAndSample', () => {
    const m = meta();
    const { allies, enemies } = predict(m);

    const top = single(laneMatchups(m, allies, enemies), Lane.Top);

    expect(top.allyId).toBe(AllyTop);
    expect(top.enemyId).toBe(EnemyTop);
    expect(hasWinRate(top)).toBe(true);

    // Shrunk towards 50 %, so above the middle but short of the raw 58 %.
    expect(top.winRate!).toBeGreaterThanOrEqual(0.5);
    expect(top.winRate!).toBeLessThanOrEqual(0.58);
    expect(top.play).toBe(4000);
  });

  // Mid has both champions but no matchup row. Null rather than 0.5: the view shows the pairing
  // without a figure, instead of claiming a measured even duel.
  it('ADuelWithoutAStatistic_HasNoRate', () => {
    const m = meta();
    const { allies, enemies } = predict(m);

    const mid = single(laneMatchups(m, allies, enemies), Lane.Mid);

    expect(mid.allyId).toBe(AllyMid);
    expect(mid.enemyId).toBe(EnemyMid);
    expect(hasWinRate(mid)).toBe(false);
    expect(mid.winRate).toBeNull();
    expect(mid.play).toBe(0);
  });

  // Blind pick: our side is there, theirs is not — the row still names who we sent.
  it('WithTheEnemyHidden_TheRowKeepsOurSideAndHasNoRate', () => {
    const m = meta();
    const { allies, enemies } = predict(m, false);

    const rows = laneMatchups(m, allies, enemies);
    const top = single(rows, Lane.Top);

    expect(top.allyId).toBe(AllyTop);
    expect(top.enemyId).toBe(0);
    expect(hasWinRate(top)).toBe(false);
  });

  it('WithoutAnyDraft_ThereAreNoRows', () => {
    const rows = laneMatchups(meta(), LanePredictionResult.empty, LanePredictionResult.empty);

    expect(rows).toHaveLength(0);
  });

  // The seat lookup behind the figures in the team columns: each row gets the duel it is
  // actually standing in, read from its own side.
  it('ASeat_FindsTheDuelItStandsIn_FromEitherSide', () => {
    const m = meta();
    const { allies, enemies } = predict(m);
    const rows = laneMatchups(m, allies, enemies);

    const ours = laneMatchupForSeat(rows, Lane.Top, AllyTop, true);
    const theirs = laneMatchupForSeat(rows, Lane.Top, EnemyTop, false);

    expect(ours).not.toBeNull();
    expect(ours!.lane).toBe(Lane.Top);
    expect(ours).toEqual(theirs);
  });

  // The champion is checked, not just the lane — otherwise a seat borrows the pick that actually
  // stands there.
  it('ASeatOnALaneSomebodyElseHolds_GetsNoDuel', () => {
    const m = meta();
    const { allies, enemies } = predict(m);
    const rows = laneMatchups(m, allies, enemies);

    expect(laneMatchupForSeat(rows, Lane.Top, AllyMid, true)).toBeNull();

    // Right champion, wrong side: our top laner is not the enemy of the top row.
    expect(laneMatchupForSeat(rows, Lane.Top, AllyTop, false)).toBeNull();
  });

  it('ASeatWithoutAPickOrALane_GetsNoDuel', () => {
    const m = meta();
    const { allies, enemies } = predict(m);
    const rows = laneMatchups(m, allies, enemies);

    expect(laneMatchupForSeat(rows, Lane.Top, 0, true)).toBeNull();
    expect(laneMatchupForSeat(rows, Lane.Unknown, AllyTop, true)).toBeNull();
  });

  // Blind pick: the seat is found, so its lane still reads, but the row carries no rate — the
  // figure stays away rather than turning an unopposed lane into a number.
  it('ASeatFacingNobody_FindsItsRow_WithoutARate', () => {
    const m = meta();
    const { allies, enemies } = predict(m, false);
    const rows = laneMatchups(m, allies, enemies);

    const seat = laneMatchupForSeat(rows, Lane.Top, AllyTop, true);

    expect(seat).not.toBeNull();
    expect(hasWinRate(seat!)).toBe(false);
  });
});
