// Port of tests/DraftPilot.Core.Tests/ShrinkageTargetTests.cs.
//
// What a matchup rate falls back to when its own sample is thin: not a flat 50 %, but what the two
// lane rates (plus the listing offset) predict for the pair.

import { describe, expect, it } from 'vitest';
import { Lane } from '../../src/core/model/lane';
import type { MetaLookup } from '../../src/core/model/metaLookup';
import { logit } from '../../src/core/model/scoreModel';
import { MATCHUP_PRIOR, shrink } from '../../src/core/model/shrinkage';
import { MetaBuilder } from './helpers';

const Mine = 1,
  Weak = 2,
  Equal = 3;

/** Mine is a strong laner, Weak a poor one, Equal exactly as good as Mine. */
const meta = (listingOffset = 0, duelPlay = 30, duelRate = 0.5): MetaLookup =>
  new MetaBuilder()
    .champion(Mine, 'Mine')
    .champion(Weak, 'Weak')
    .champion(Equal, 'Equal')
    .inLane(Mine, Lane.Top, { winRate: 0.55, play: 40_000 })
    .inLane(Weak, Lane.Top, { winRate: 0.45, play: 40_000 })
    .inLane(Equal, Lane.Top, { winRate: 0.55, play: 40_000 })
    .matchup(Mine, Weak, Lane.Top, duelRate, duelPlay)
    .matchup(Mine, Equal, Lane.Top, duelRate, duelPlay)
    .matchupBaseline(listingOffset)
    .build();

describe('ShrinkageTargetTests', () => {
  /**
   * A duel with almost no games behind it must not claim an even matchup against a champion
   * ten points weaker.
   */
  it('AThinDuel_FallsBackToWhatTheTwoLaneRatesSay', () => {
    const m = meta();

    const againstWeak = m.matchup(Mine, Weak, Lane.Top)!.winRate;
    const againstEqual = m.matchup(Mine, Equal, Lane.Top)!.winRate;

    expect(againstWeak > 0.53, `Gegen den schwächeren Champion erwartet, gemessen ${againstWeak}.`).toBe(true);
    expect(againstEqual > 0.49 && againstEqual < 0.51, `Gegen den gleich starken erwartet 50 %, gemessen ${againstEqual}.`).toBe(true);
  });

  /**
   * The shrinkage target and the duel term's baseline are the same number, so a thin edge
   * contributes nothing beyond the measured part, damped by the weight the sample deserves.
   */
  it.each([[1], [30], [150]])('AThinDuel_ContributesNothing(%s)', (play) => {
    const m = meta(0, play, 0.5);
    const view = m.matchup(Mine, Weak, Lane.Top)!;

    const contribution = logit(view.winRate) - m.matchupBaseline(Mine, Weak, Lane.Top);
    const scale = play / (play + MATCHUP_PRIOR);

    // What is left is the measured part only, which is the observed rate against the same
    // reference — damped by exactly the weight the sample deserves.
    const measured = logit(0.5) - m.matchupBaseline(Mine, Weak, Lane.Top);
    expect(
      Math.abs(contribution) < Math.abs(measured * scale) + 0.01,
      `${play} Spiele dürfen höchstens ${scale} des Unterschieds tragen, trugen ${contribution.toFixed(4)}.`,
    ).toBe(true);
  });

  /** Mirrored edges still add up to one. */
  it.each([[0], [-0.0295]])('AnEdgeAndItsMirror_StillAddUpToOne(%s)', (listingOffset) => {
    const m = meta(listingOffset);

    const forward = m.matchup(Mine, Weak, Lane.Top)!.winRate;
    const mirror = m.matchup(Weak, Mine, Lane.Top)!.winRate;

    expect(forward + mirror).toBeCloseTo(1.0, 10);
  });

  /** The listing offset shifts the stored direction, because that is the direction the selection applies to. */
  it('TheListingOffset_MovesTheExpectationOfTheStoredDirection', () => {
    const neutral = meta().matchupBaseline(Mine, Weak, Lane.Top);
    const shifted = meta(-0.0295).matchupBaseline(Mine, Weak, Lane.Top);

    expect(shifted - neutral).toBeCloseTo(-0.0295, 10);
  });

  /**
   * Without lane rates on the contested lane the pair says nothing, and the champion's own main
   * lane stands in.
   */
  it('WithoutARowOnThatLane_TheMainLaneStandsIn', () => {
    const m = new MetaBuilder()
      .champion(Mine, 'Mine')
      .champion(Weak, 'Weak')
      .inLane(Mine, Lane.Mid, { winRate: 0.55, play: 40_000 })
      .inLane(Weak, Lane.Top, { winRate: 0.45, play: 40_000 })
      .build();

    // The lane rates themselves are shrunk, so the expectation is read back from the lookup
    // rather than from the raw fixture numbers.
    const expected = logit(m.laneStat(Mine, Lane.Mid)!.winRate) - logit(m.laneStat(Weak, Lane.Top)!.winRate);

    expect(m.matchupBaseline(Mine, Weak, Lane.Top)).toBeCloseTo(expected, 10);
  });

  /** A champion nothing is known about leaves only the listing offset. */
  it('WithoutAnyLaneRow_OnlyTheListingOffsetRemains', () => {
    const m = meta(-0.0295);

    expect(m.matchupBaseline(Mine, 999, Lane.Top)).toBeCloseTo(-0.0295, 10);
  });

  /** An impossible prior must not reach the arithmetic — Logit(0) and Logit(1) are infinite. */
  it.each([[0], [1], [Number.NaN]])('AnImpossiblePrior_FallsBackToEven(%s)', (priorRate) => {
    expect(shrink(0.5, 0, MATCHUP_PRIOR, priorRate)).toBe(0.5);
  });
});
