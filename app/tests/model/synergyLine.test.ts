// Port of tests/DraftPilot.Core.Tests/SynergyLineTests.cs.
//
// The duo expectation: what a pair should win from the two champions' lane strength alone. The
// synergy term counts only what a duo does beyond it.

import { describe, expect, it } from 'vitest';
import { Lane } from '../../src/core/model/lane';
import { logit, sigmoid } from '../../src/core/model/scoreModel';
import { expectedLogit, fitSynergyLine, flatLine, MINIMUM_ROWS, type SynergyRow } from '../../src/core/model/synergyLine';

/** Lane rates for champions 1..n, spread evenly between 47 % and 53 %. */
const laneRate = (championId: number, _lane: Lane): number | null => 0.47 + (championId % 61) * 0.001;

/** Duo rows built to lie exactly on a known line, so the fit has a right answer to find. */
function onTheLine(intercept: number, slope: number, pairs: number): SynergyRow[] {
  const rows: SynergyRow[] = [];

  for (let i = 0; i < pairs; i++) {
    const mine = 1 + (i % 60);
    const partner = 100 + i;
    const x = logit(laneRate(mine, Lane.Mid)!) + logit(laneRate(partner, Lane.Support)!);

    rows.push({
      championId: mine,
      lane: Lane.Mid,
      partnerId: partner,
      partnerLane: Lane.Support,
      winRate: sigmoid(intercept + slope * x),
      play: 500 + i * 7,
    });
  }

  return rows;
}

describe('SynergyLineTests', () => {
  it('ALineInTheDataIsFound', () => {
    const line = fitSynergyLine(onTheLine(0.05, 0.4, 300), laneRate, 0.07);

    expect(line.slope).toBeCloseTo(0.4, 3);
    expect(line.intercept).toBeCloseTo(0.05, 3);
    expect(line.rows).toBe(300);
  });

  /** A slope fitted on a handful of pairs would multiply every candidate's strength by noise. */
  it('TooFewPairsFallBackToTheFlatBaseline', () => {
    const line = fitSynergyLine(onTheLine(0.05, 0.4, MINIMUM_ROWS - 1), laneRate, 0.07);

    expect(line).toEqual(flatLine(0.07));
    expect(expectedLogit(line, 0.3, -0.2)).toBeCloseTo(0.07, 12);
  });

  /**
   * Outside 0..1 the slope would mean something the model cannot: rewarding a weak partner, or
   * counting strength more than once inside the duo itself.
   */
  it.each([
    [-0.5, 0.0],
    [1.8, 1.0],
  ])('TheSlopeStaysWithinWhatItCanMean(%s, %s)', (slope, expected) => {
    const line = fitSynergyLine(onTheLine(0.0, slope, 200), laneRate, 0);

    expect(line.slope).toBeCloseTo(expected, 9);
  });

  /** The same duo listed by both partners is one set of games, not two. */
  it('ADuoListedFromBothSidesCountsOnce', () => {
    const rows = onTheLine(0.05, 0.4, 150);
    const mirrored: SynergyRow[] = rows.map((row) => ({
      championId: row.partnerId,
      lane: row.partnerLane,
      partnerId: row.championId,
      partnerLane: row.lane,
      winRate: row.winRate,
      play: row.play,
    }));

    const line = fitSynergyLine([...rows, ...mirrored], laneRate, 0);

    expect(line.rows).toBe(150);
  });

  /** Pairs without a lane row on either side cannot be placed on the line and are left out. */
  it('PairsWithoutLaneRowsAreLeftOut', () => {
    const line = fitSynergyLine(onTheLine(0.05, 0.4, 200), () => null, 0.07);

    expect(line).toEqual(flatLine(0.07));
  });
});
