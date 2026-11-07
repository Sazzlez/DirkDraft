// Port of tests/DraftPilot.Core.Tests/SeatPriorsTests.cs.
//
// The seat table is hand-editable JSON that feeds every lane prediction, and the shipped one is
// deliberately flat — so these tests use tables that are NOT flat, to make the loader visible.
// SeatPriors.Load(path) is SeatPriors.fromJson(parsedJson) in the port: the tables are written
// inline instead of to temp files, and a document that does not parse is handed over as it is.

import { describe, expect, it } from 'vitest';
import shipped from '../../src/core/model/data/pick_order_priors.json';
import { ALL_LANES, Lane, LANE_COUNT } from '../../src/core/model/lane';
import { SeatPriors } from '../../src/core/model/lanePredictor';

/** JSON.parse where it parses; the raw text where it does not (a file that is not JSON at all). */
function parseLenient(json: string): unknown {
  try {
    return JSON.parse(json) as unknown;
  } catch {
    return json;
  }
}

describe('SeatPriorsTests', () => {
  it('ARowIsNormalised_SoOneSeatCannotOutweighTheOthers', () => {
    // Seat 0 leans top, and the row sums to 10 instead of 1.
    const priors = SeatPriors.fromJson({
      rows: [
        [6, 1, 1, 1, 1],
        [1, 1, 1, 1, 1],
        [1, 1, 1, 1, 1],
        [1, 1, 1, 1, 1],
        [1, 1, 1, 1, 1],
      ],
    });

    expect(priors.get(0, Lane.Top)).toBeCloseTo(0.6, 6);
    expect(priors.get(0, Lane.Mid)).toBeCloseTo(0.1, 6);
    expect(priors.get(1, Lane.Top)).toBeCloseTo(0.2, 6);
  });

  it.each([
    // A cell that is not a number — the case a hand-edited file produces most easily.
    '{ "rows": [["viel",1,1,1,1],[1,1,1,1,1],[1,1,1,1,1],[1,1,1,1,1],[1,1,1,1,1]] }',
    // A row that sums to zero would divide by it.
    '{ "rows": [[0,0,0,0,0],[1,1,1,1,1],[1,1,1,1,1],[1,1,1,1,1],[1,1,1,1,1]] }',
    // Fewer rows than seats: an incomplete table is not a table.
    '{ "rows": [[1,1,1,1,1],[1,1,1,1,1]] }',
    // The key itself missing.
    '{ "kommentar": "nichts hier" }',
    // Not even JSON.
    'kaputt',
  ])('ABrokenTable_FallsBackToNoHintAtAll(%s)', (json) => {
    const priors = SeatPriors.fromJson(parseLenient(json));

    for (const lane of ALL_LANES) expect(priors.get(0, lane)).toBeCloseTo(1.0 / LANE_COUNT, 6);
  });

  // A missing file reaches the port as no document at all.
  it('AMissingFile_IsNotAnError', () => {
    const priors = SeatPriors.fromJson(undefined);

    expect(priors.get(2, Lane.Adc)).toBeCloseTo(1.0 / LANE_COUNT, 6);
  });

  // Out-of-range seats and the unknown lane answer with the flat value, not an exception.
  it('ASeatOutsideTheTable_AnswersFlat', () => {
    const priors = SeatPriors.uniform;

    expect(priors.get(-1, Lane.Top)).toBeCloseTo(1.0 / LANE_COUNT, 6);
    expect(priors.get(99, Lane.Top)).toBeCloseTo(1.0 / LANE_COUNT, 6);
    expect(priors.get(0, Lane.Unknown)).toBeCloseTo(1.0 / LANE_COUNT, 6);
  });

  // The shipped table is flat on purpose — the one recorded 5v5 session had seats 2 and 3 swapped
  // against the canonical order, so the seat index says nothing we could verify. A test rather
  // than a comment, because "the file is flat" is an assumption the rest of the tuning rests on.
  it('TheShippedTable_IsStillFlat', () => {
    const priors = SeatPriors.fromJson(shipped);

    for (let seat = 0; seat < LANE_COUNT; seat++) {
      for (const lane of ALL_LANES) expect(priors.get(seat, lane)).toBeCloseTo(1.0 / LANE_COUNT, 6);
    }
  });
});
