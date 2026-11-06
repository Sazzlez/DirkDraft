// Port of tests/DraftPilot.Core.Tests/LanePredictorTests.cs.

import { describe, expect, it } from 'vitest';
import { draftStateFrom } from '../../src/core/model/draftState';
import { Lane, LANE_COUNT } from '../../src/core/model/lane';
import { isUncertain, LanePredictor, LOW_CONFIDENCE, SeatPriors } from '../../src/core/model/lanePredictor';
import { MetaLookup } from '../../src/core/model/metaLookup';
import { MetaBuilder, SessionBuilder, TestMeta } from './helpers';

const predictor = (): LanePredictor => new LanePredictor(TestMeta.standardCast());

/** tests/DraftPilot.Core.Tests/Fixtures/pick_order_priors.json — flat, like the shipped table. */
const seatPriorsFixture = {
  version: 2,
  lanes: ['top', 'jungle', 'mid', 'adc', 'support'],
  rows: [
    [0.2, 0.2, 0.2, 0.2, 0.2],
    [0.2, 0.2, 0.2, 0.2, 0.2],
    [0.2, 0.2, 0.2, 0.2, 0.2],
    [0.2, 0.2, 0.2, 0.2, 0.2],
    [0.2, 0.2, 0.2, 0.2, 0.2],
  ],
};

describe('LanePredictorTests', () => {
  it('UnambiguousComp_IsAssignedCorrectly', () => {
    const slots = TestMeta.enemySlots(TestMeta.Jax, TestMeta.Elise, TestMeta.Syndra, TestMeta.Lucian, TestMeta.Nautilus);

    const result = predictor().predict(slots);

    expect(result.forCell(5)!.lane).toBe(Lane.Top);
    expect(result.forCell(6)!.lane).toBe(Lane.Jungle);
    expect(result.forCell(7)!.lane).toBe(Lane.Mid);
    expect(result.forCell(8)!.lane).toBe(Lane.Adc);
    expect(result.forCell(9)!.lane).toBe(Lane.Support);
  });

  it('OutOfOrderComp_IsStillAssignedByChampion', () => {
    // Support first, top last: if seat order dominated the champion prior, this would fail.
    const slots = TestMeta.enemySlots(TestMeta.Nautilus, TestMeta.Syndra, TestMeta.Lucian, TestMeta.Elise, TestMeta.Jax);

    const result = predictor().predict(slots);

    expect(result.forCell(5)!.lane).toBe(Lane.Support);
    expect(result.forCell(6)!.lane).toBe(Lane.Mid);
    expect(result.forCell(7)!.lane).toBe(Lane.Adc);
    expect(result.forCell(8)!.lane).toBe(Lane.Jungle);
    expect(result.forCell(9)!.lane).toBe(Lane.Top);
  });

  it('ProbabilitiesSumToOnePerSeat', () => {
    const result = predictor().predict(TestMeta.enemySlots(TestMeta.Jax, TestMeta.Elise, TestMeta.Syndra, 0, 0));

    for (const prediction of result.predictions)
      expect(prediction.probabilities.reduce((sum, p) => sum + p, 0)).toBeCloseTo(1.0, 6);
  });

  it('UnrevealedSeats_AreSpreadOverTheRemainingLanes', () => {
    // Three locked picks pin three lanes, so the two hidden seats can only be adc or support.
    const result = predictor().predict(TestMeta.enemySlots(TestMeta.Jax, TestMeta.Elise, TestMeta.Syndra, 0, 0));

    const fourth = result.forCell(8)!;

    expect(fourth.probabilities[Lane.Adc]!).toBeGreaterThan(0.4);
    expect(fourth.probabilities[Lane.Support]!).toBeGreaterThan(0.4);
    expect(fourth.probabilities[Lane.Top]!).toBeLessThan(0.05);
  });

  it('LockedPicks_GetHighConfidence', () => {
    const result = predictor().predict(
      TestMeta.enemySlots(TestMeta.Jax, TestMeta.Elise, TestMeta.Syndra, TestMeta.Lucian, TestMeta.Nautilus),
    );

    for (const prediction of result.predictions) {
      expect(prediction.confidence, `cell ${prediction.cellId} only reached ${prediction.confidence}`).toBeGreaterThan(0.7);
      expect(isUncertain(prediction)).toBe(false);
    }
  });

  it('FlexPick_IsReportedAsUncertain', () => {
    // Sylas alongside two other mid-capable champions: the tool should admit it does not know.
    const slots = TestMeta.enemySlots(TestMeta.Sylas, TestMeta.Lucian, 0, 0, 0);

    const sylas = predictor().predict(slots).forCell(5)!;

    expect(sylas.confidence).toBeLessThan(LOW_CONFIDENCE);
    expect(isUncertain(sylas)).toBe(true);
  });

  it('OffMetaPick_StillGetsAnAssignment', () => {
    // Thresh has no recorded top games at all. Forcing the rest of the comp onto the other
    // lanes must not leave the solver with nothing.
    const slots = TestMeta.enemySlots(TestMeta.Thresh, TestMeta.Elise, TestMeta.Syndra, TestMeta.Lucian, TestMeta.Nautilus);

    const result = predictor().predict(slots);

    expect(result.forCell(5)!.lane).not.toBe(Lane.Unknown);
    expect(result.predictions.filter((p) => p.lane !== Lane.Unknown)).toHaveLength(5);
  });

  it('EveryLaneIsAssignedExactlyOnce', () => {
    const result = predictor().predict(
      TestMeta.enemySlots(TestMeta.Jax, TestMeta.Sylas, TestMeta.Syndra, TestMeta.Lucian, TestMeta.Nautilus),
    );

    const lanes = result.predictions.map((p) => p.lane);

    expect(new Set(lanes).size).toBe(LANE_COUNT);
    expect(lanes).not.toContain(Lane.Unknown);
  });

  it('ManualOverride_IsHonouredAndReshufflesTheRest', () => {
    // Insist Jax is jungling; Elise then has to move off jungle.
    const slots = TestMeta.enemySlots(TestMeta.Jax, TestMeta.Elise, TestMeta.Syndra, TestMeta.Lucian, TestMeta.Nautilus);
    const overrides = new Map<number, Lane>([[5, Lane.Jungle]]);

    const result = predictor().predict(slots, overrides);

    expect(result.forCell(5)!.lane).toBe(Lane.Jungle);
    expect(result.forCell(5)!.isManual).toBe(true);
    expect(result.forCell(6)!.lane).toBe(Lane.Top);
    expect(result.forCell(7)!.lane).toBe(Lane.Mid);
  });

  it('ManualOverride_IsNeverFlaggedUncertain', () => {
    const slots = TestMeta.enemySlots(TestMeta.Sylas, 0, 0, 0, 0);
    const overrides = new Map<number, Lane>([[5, Lane.Top]]);

    const sylas = predictor().predict(slots, overrides).forCell(5)!;

    expect(sylas.isManual).toBe(true);
    expect(isUncertain(sylas)).toBe(false);
  });

  it('AllyLanes_ComeStraightFromTheClient', () => {
    const state = draftStateFrom(new SessionBuilder().build());

    const result = predictor().predict(state.allies);

    expect(result.forCell(0)!.lane).toBe(Lane.Top);
    expect(result.forCell(4)!.lane).toBe(Lane.Support);
    expect(result.forCell(2)!.confidence).toBeCloseTo(1.0, 6);
  });

  it('AllyLanes_FallBackToPredictionWhenTheClientIsSilent', () => {
    // Blind pick and custom games leave assignedPosition empty.
    const session = new SessionBuilder()
      .allyLane(0, '')
      .allyLane(1, '')
      .allyLane(2, '')
      .allyLane(3, '')
      .allyLane(4, '')
      .locked(0, TestMeta.Aatrox)
      .locked(1, TestMeta.LeeSin)
      .locked(2, TestMeta.Ahri)
      .locked(3, TestMeta.Ashe)
      .locked(4, TestMeta.Thresh)
      .build();

    const result = predictor().predict(draftStateFrom(session).allies);

    expect(result.forCell(0)!.lane).toBe(Lane.Top);
    expect(result.forCell(1)!.lane).toBe(Lane.Jungle);
    expect(result.forCell(2)!.lane).toBe(Lane.Mid);
    expect(result.forCell(3)!.lane).toBe(Lane.Adc);
    expect(result.forCell(4)!.lane).toBe(Lane.Support);
  });

  it('ChampionOnLane_ResolvesTheOpponentForScoring', () => {
    const slots = TestMeta.enemySlots(TestMeta.Jax, TestMeta.Elise, TestMeta.Syndra, TestMeta.Lucian, TestMeta.Nautilus);

    const result = predictor().predict(slots);

    expect(result.championOnLane(Lane.Top)).toBe(TestMeta.Jax);
    expect(result.championOnLane(Lane.Mid)).toBe(TestMeta.Syndra);
    expect(result.championOnLane(Lane.Support)).toBe(TestMeta.Nautilus);
  });

  it('ChampionOnLane_IsZeroWhileTheSeatIsHidden', () => {
    const result = predictor().predict(TestMeta.enemySlots(TestMeta.Jax, 0, 0, 0, 0));

    expect(result.championOnLane(Lane.Top)).toBe(TestMeta.Jax);
    expect(result.championOnLane(Lane.Mid)).toBe(0);
  });

  it('NoSlots_YieldsNothing', () => {
    expect(predictor().predict([]).predictions).toHaveLength(0);
  });

  it('EmptyMeta_StillProducesAValidAssignment', () => {
    // Before the first data update there are no role priors at all.
    const p = new LanePredictor(MetaLookup.empty());

    const result = p.predict(TestMeta.enemySlots(TestMeta.Jax, TestMeta.Elise, TestMeta.Syndra, TestMeta.Lucian, TestMeta.Nautilus));

    expect(new Set(result.predictions.map((x) => x.lane)).size).toBe(LANE_COUNT);
    for (const x of result.predictions) expect(isUncertain(x)).toBe(true);
  });

  it('SeatPriors_NudgeButDoNotOverrideTheChampion', () => {
    const slots = TestMeta.enemySlots(TestMeta.Nautilus, TestMeta.Syndra, TestMeta.Lucian, TestMeta.Elise, TestMeta.Jax);
    const withSeatHint = new LanePredictor(TestMeta.standardCast(), SeatPriors.fromJson(seatPriorsFixture));

    const result = withSeatHint.predict(slots);

    // Seat 0 is nudged towards top, but Nautilus is a support and that has to win.
    expect(result.forCell(5)!.lane).toBe(Lane.Support);
  });

  // Two seats manually forced onto the same lane leave no valid one-to-one assignment. That
  // contradiction must degrade to a prediction WITHOUT the constraints — not collapse every
  // seat, including the unambiguous ones, to Unknown.
  it('ContradictoryManualLanes_DoNotCollapseEveryPrediction', () => {
    const slots = TestMeta.enemySlots(TestMeta.Jax, TestMeta.Elise, TestMeta.Syndra, TestMeta.Lucian, TestMeta.Nautilus);
    const manual = new Map<number, Lane>([
      [5, Lane.Mid],
      [6, Lane.Mid],
    ]);

    const result = predictor().predict(slots, manual);

    expect(result.forCell(7)!.lane).toBe(Lane.Mid);
    expect(result.forCell(8)!.lane).toBe(Lane.Adc);
    for (const prediction of result.predictions) expect(prediction.lane).not.toBe(Lane.Unknown);
  });

  // One NaN role rate in a snapshot must not poison the whole prediction: NaN survives the
  // normalisation and every comparison, and used to turn EVERY seat into Unknown.
  it('ANaNRoleRate_DoesNotPoisonThePrediction', () => {
    const meta = new MetaBuilder()
      .champion(1, 'Sane')
      .inLane(1, Lane.Top, { roleRate: 0.9 })
      .champion(2, 'Broken')
      .inLane(2, Lane.Jungle, { roleRate: Number.NaN })
      .build();

    const slots = TestMeta.enemySlots(1, 2, 0, 0, 0);
    const result = new LanePredictor(meta).predict(slots);

    expect(result.forCell(5)!.lane).toBe(Lane.Top);
    expect(result.forCell(6)!.lane).not.toBe(Lane.Unknown);
  });
});
