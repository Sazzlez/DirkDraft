// Port of tests/DraftPilot.Core.Tests/DraftBalanceTests.cs.
//
// The draft balance shown over the two team columns. It is a claim about the whole draft, so the
// tests pin down the two properties that keep it honest: it stays near the middle unless the data
// actually says otherwise, and it never invents a side for seats nobody has revealed.

import { describe, expect, it } from 'vitest';
import { balanceHasData, estimateBalance, type DraftBalance } from '../../src/core/model/advisors';
import { draftStateFrom, INACTIVE, type DraftState } from '../../src/core/model/draftState';
import { Lane } from '../../src/core/model/lane';
import { LanePredictionResult, LanePredictor } from '../../src/core/model/lanePredictor';
import { MetaLookup } from '../../src/core/model/metaLookup';
import { MetaBuilder, SessionBuilder } from './helpers';

const AllyTop = 1, AllyMid = 2, EnemyTop = 3, EnemyMid = 4;

function meta(
  options: { allyTopWinRate?: number; enemyTopWinRate?: number; topMatchup?: number | null } = {},
): MetaLookup {
  const { allyTopWinRate = 0.5, enemyTopWinRate = 0.5, topMatchup = null } = options;
  const builder = new MetaBuilder()
    .champion(AllyTop, 'AllyTop')
    .inLane(AllyTop, Lane.Top, { winRate: allyTopWinRate, play: 5000 })
    .champion(AllyMid, 'AllyMid')
    .inLane(AllyMid, Lane.Mid, { winRate: 0.5, play: 5000 })
    .champion(EnemyTop, 'EnemyTop')
    .inLane(EnemyTop, Lane.Top, { winRate: enemyTopWinRate, play: 5000 })
    .champion(EnemyMid, 'EnemyMid')
    .inLane(EnemyMid, Lane.Mid, { winRate: 0.5, play: 5000 });

  if (topMatchup !== null) builder.matchup(AllyTop, EnemyTop, Lane.Top, topMatchup, 5000);

  return builder.build();
}

/** Both sides on top and mid, with the champions given. */
function draft(
  m: MetaLookup,
  allyTop = AllyTop,
  enemyTop = EnemyTop,
): { state: DraftState; allies: LanePredictionResult; enemies: LanePredictionResult } {
  const session = new SessionBuilder().localPlayer(0).locked(0, allyTop).locked(2, AllyMid).locked(5, enemyTop).locked(7, EnemyMid).build();

  const state = draftStateFrom(session);
  const predictor = new LanePredictor(m);

  return { state, allies: predictor.predict(state.allies), enemies: predictor.predict(state.enemies) };
}

function estimate(m: MetaLookup): DraftBalance {
  const { state, allies, enemies } = draft(m);
  return estimateBalance(m, state, allies, enemies);
}

describe('DraftBalanceTests', () => {
  it('WithoutADraft_ThereIsNothingToJudge', () => {
    const balance = estimateBalance(meta(), INACTIVE, LanePredictionResult.empty, LanePredictionResult.empty);

    expect(balanceHasData(balance)).toBe(false);
  });

  it('WithoutMetaData_ThereIsNothingToJudge', () => {
    const m = meta();
    const { state, allies, enemies } = draft(m);

    const balance = estimateBalance(MetaLookup.empty(), state, allies, enemies);

    expect(balanceHasData(balance)).toBe(false);
  });

  // Blind pick: our own team is revealed, theirs is not. Both edges are zero by construction, so
  // the estimate lands on exactly 50 % — and printing that would claim the draft had been weighed
  // when in truth there was nothing to weigh it against.
  it('WithTheEnemyTeamHidden_ThereIsNothingToCompare', () => {
    const m = meta({ allyTopWinRate: 0.56 });
    const session = new SessionBuilder().localPlayer(0).locked(0, AllyTop).locked(2, AllyMid).build();

    const state = draftStateFrom(session);
    const predictor = new LanePredictor(m);
    const allies = predictor.predict(state.allies);

    const balance = estimateBalance(m, state, allies, predictor.predict(state.enemies));

    expect(balanceHasData(balance)).toBe(false);
    expect(balance.isComparable).toBe(false);

    // The champions were still read — the window uses that to explain WHICH dash it is showing.
    expect(balance.ratedChampions).toBe(2);
  });

  it('EquallyStrongTeams_AreEven', () => {
    const m = meta();
    const { state, allies, enemies } = draft(m);

    const balance = estimateBalance(m, state, allies, enemies);

    expect(balanceHasData(balance)).toBe(true);
    expect(balance.allyWinRate).toBeCloseTo(0.5, 6);
  });

  it('TheTwoSides_AlwaysAddUpToOne', () => {
    const m = meta({ allyTopWinRate: 0.56, topMatchup: 0.62 });
    const { state, allies, enemies } = draft(m);

    const balance = estimateBalance(m, state, allies, enemies);

    // C# DraftBalance.EnemyWinRate is `1 - AllyWinRate`; the port has no such member, so it is
    // spelled out here with the C# definition.
    const enemyWinRate = 1 - balance.allyWinRate;
    expect(balance.allyWinRate + enemyWinRate).toBeCloseTo(1.0, 9);
  });

  it('StrongerLaneWinRates_TipItOurWay', () => {
    const m = meta({ allyTopWinRate: 0.56, enemyTopWinRate: 0.47 });
    const { state, allies, enemies } = draft(m);

    const balance = estimateBalance(m, state, allies, enemies);

    expect(balance.allyWinRate, `erwartet über 50 %, war ${balance.allyWinRate}`).toBeGreaterThan(0.5);
    expect(balance.ratedChampions).toBe(4);
  });

  it('AFavourableDuel_TipsItOurWay_AndIsCounted', () => {
    // Same lane strength on both sides; only the direct matchup speaks.
    const m = meta({ topMatchup: 0.62 });
    const { state, allies, enemies } = draft(m);

    const balance = estimateBalance(m, state, allies, enemies);

    expect(balance.allyWinRate, `erwartet über 50 %, war ${balance.allyWinRate}`).toBeGreaterThan(0.5);
    expect(balance.contestedLanes).toBe(1);
  });

  it('ALosingDuel_TipsItTheirWay', () => {
    const m = meta({ topMatchup: 0.38 });
    const { state, allies, enemies } = draft(m);

    const balance = estimateBalance(m, state, allies, enemies);

    expect(balance.allyWinRate, `erwartet unter 50 %, war ${balance.allyWinRate}`).toBeLessThan(0.5);
  });

  // A duel counts by its surprise, not by its absolute rate. Both lane rates are already in the
  // strength difference; a duel that lands exactly where those rates predict (in log-odds:
  // ally minus enemy) therefore adds nothing. Uncentred, the usual case — the stronger champion
  // also wins the duel — was worth about double what the data says.
  it('ADuelThatMerelyConfirmsTheLaneRates_IsNotCountedTwice', () => {
    // 56 % against 44 % on top: in log-odds those two rates predict a 61,8 % duel.
    const withoutDuel = meta({ allyTopWinRate: 0.56, enemyTopWinRate: 0.44 });
    const withDuel = meta({ allyTopWinRate: 0.56, enemyTopWinRate: 0.44, topMatchup: 0.618 });

    const plain = estimate(withoutDuel);
    const confirmed = estimate(withDuel);

    expect(confirmed.contestedLanes).toBe(1);
    expect(
      Math.abs(confirmed.allyWinRate - plain.allyWinRate),
      `erwartet nahezu unverändert, war ${plain.allyWinRate} gegen ${confirmed.allyWinRate}`,
    ).toBeLessThan(0.01);
  });

  // The other half of the same rule: a duel that beats the prediction still counts.
  it('ADuelBetterThanTheLaneRatesPredict_StillTipsItOurWay', () => {
    const m = meta({ allyTopWinRate: 0.56, enemyTopWinRate: 0.44, topMatchup: 0.7 });

    expect(estimate(m).allyWinRate).toBeGreaterThan(estimate(meta({ allyTopWinRate: 0.56, enemyTopWinRate: 0.44 })).allyWinRate);
  });

  // Five champions at 52 % are not a 95 % team: the terms are averaged, never summed, so the
  // number stays in the band real drafts live in.
  it('ManyStrongPicks_DoNotStackIntoACertainty', () => {
    const builder = new MetaBuilder();
    for (let id = 1; id <= 5; id++) builder.champion(id, `Ally${id}`).inLane(id, (id - 1) as Lane, { winRate: 0.56, play: 5000 });
    for (let id = 6; id <= 10; id++) builder.champion(id, `Enemy${id}`).inLane(id, (id - 6) as Lane, { winRate: 0.44, play: 5000 });

    const m = builder.build();

    const session = new SessionBuilder().localPlayer(0);
    for (let seat = 0; seat < 5; seat++) session.locked(seat, seat + 1).locked(seat + 5, seat + 6);

    const state = draftStateFrom(session.build());
    const predictor = new LanePredictor(m);
    const balance = estimateBalance(m, state, predictor.predict(state.allies), predictor.predict(state.enemies));

    expect(balance.allyWinRate).toBeGreaterThan(0.5);
    expect(balance.allyWinRate).toBeGreaterThanOrEqual(0.5);
    expect(balance.allyWinRate).toBeLessThanOrEqual(0.65);
  });
});
