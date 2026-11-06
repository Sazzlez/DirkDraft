// Port of tests/DraftPilot.Core.Tests/HostileInputTests.cs: the robustness promises the model makes,
// held to account with input nobody would send on purpose. draftStateFrom must never throw (it runs
// on every socket event), and one NaN in a score would scramble the whole sorted list.

import { describe, expect, it } from 'vitest';
import { rankAram, type AramStat } from '../../src/core/model/advisors';
import { draftStateFrom, type DraftState } from '../../src/core/model/draftState';
import { Lane } from '../../src/core/model/lane';
import { LanePredictionResult, LanePredictor } from '../../src/core/model/lanePredictor';
import { MetaLookup } from '../../src/core/model/metaLookup';
import { Recommender } from '../../src/core/model/recommender';
import { TraitTable } from '../../src/core/model/traits';
import { TurnTracker } from '../../src/core/model/turnTracker';
import { MetaBuilder } from './helpers';

const INT_MAX = 2147483647;

/** The same session the recommendation command builds, so the hostile cases see the real shape. */
function recommendCommandBuildState(lane: Lane, allies: number[], enemies: number[]): DraftState {
  const positions = ['top', 'jungle', 'middle', 'bottom', 'utility'];
  const queue = [...allies];
  const myTeam = [];
  for (let i = 0; i < 5; i++) {
    const champion = i === lane ? 0 : queue.length > 0 ? queue.shift()! : 0;
    myTeam.push({ cellId: i, championId: champion, assignedPosition: positions[i], team: 1 });
  }
  const theirTeam = [];
  for (let i = 0; i < 5; i++) {
    theirTeam.push({ cellId: i + 5, championId: i < enemies.length ? enemies[i] : 0, team: 2 });
  }
  return draftStateFrom({
    localPlayerCellId: lane,
    timer: { phase: 'BAN_PICK' },
    myTeam,
    theirTeam,
    actions: [[{ id: 1, actorCellId: lane, type: 'pick', isAllyAction: true, isInProgress: true }]],
  });
}

describe('HostileInputTests', () => {
  // ----- the champion select payload ------------------------------------------------------------

  // Explicit nulls at every nesting level; the LCU really does send them.
  it('ASessionFullOfNulls_ProducesAState', () => {
    const session = {
      localPlayerCellId: 0,
      myTeam: [null, { cellId: 0, championId: 122, team: 1 }],
      theirTeam: null,
      actions: [null, [null]],
      bans: null,
      timer: null,
      benchChampions: null,
    };

    const state = draftStateFrom(session);

    expect(state.isActive).toBe(true);
    expect(state.allies).toHaveLength(1);
    expect(state.bench).toHaveLength(0);
  });

  // More than five seats, negative ids, duplicate cell ids: none may throw, and the seat index must
  // stay dense, because SeatPriors addresses seats by it.
  it('AMisshapenTeam_DoesNotThrowAndKeepsIndicesDense', () => {
    const myTeam: ({ cellId: number; championId: number; team: number } | null)[] = [];
    for (let i = 0; i < 8; i++) myTeam.push({ cellId: 3, championId: -5, team: 1 });
    myTeam.splice(3, 0, null);

    const state = draftStateFrom({ localPlayerCellId: 7, myTeam });

    expect(state.allies).toHaveLength(8);
    expect(state.allies.map((slot) => slot.index)).toEqual([0, 1, 2, 3, 4, 5, 6, 7]);
  });

  it('ABenchOfNullsAndZeros_IsEmptyRatherThanBroken', () => {
    const session = {
      localPlayerCellId: 0,
      queueId: 450,
      benchEnabled: true,
      myTeam: [{ cellId: 0, championId: 122, team: 1 }],
      benchChampions: [null, { championId: 0 }],
    };

    expect(draftStateFrom(session).bench).toHaveLength(0);
  });

  // A negative reroll count is not a count; it must not reach the header as one.
  it('ANegativeRerollCount_IsClampedToZero', () => {
    const session = {
      localPlayerCellId: 0,
      queueId: 450,
      rerollsRemaining: -3,
      myTeam: [{ cellId: 0, championId: 122, team: 1 }],
    };

    expect(draftStateFrom(session).rerollsRemaining).toBe(0);
  });

  // ----- the score ------------------------------------------------------------------------------

  // Values no measurement produces. One NaN would ride through the logit into the sum and leave
  // the sort in an arbitrary order that looks exactly like a normal one.
  it('ASnapshotWithImpossibleNumbers_StillProducesFiniteScores', () => {
    const meta = new MetaBuilder()
      .champion(1, 'NaN').inLane(1, Lane.Top, { winRate: Number.NaN, play: 1_000 })
      .champion(2, 'Unendlich').inLane(2, Lane.Top, { winRate: Number.POSITIVE_INFINITY, play: 1_000 })
      .champion(3, 'Negativ').inLane(3, Lane.Top, { winRate: -4, play: -20 })
      .champion(4, 'Zuviel').inLane(4, Lane.Top, { winRate: 12, play: INT_MAX })
      .champion(5, 'Normal').inLane(5, Lane.Top, { winRate: 0.52, play: 20_000 })
      .champion(9, 'Gegner').inLane(9, Lane.Top, { winRate: 0.5, play: 20_000 })
      .matchup(1, 9, Lane.Top, Number.NaN, 500)
      .matchup(5, 9, Lane.Top, 0.55, 500)
      .build();

    const state = recommendCommandBuildState(Lane.Top, [], [9]);
    const target = new TurnTracker().resolve(state)!;
    const predictions = new LanePredictor(meta).predict(state.enemies);

    const set = new Recommender(meta, TraitTable.empty).recommend(state, target, predictions, null, null, 10);

    expect(set.items.length).toBeGreaterThan(0);

    for (const item of set.items) {
      expect(Number.isFinite(item.score), `${item.name} hat den Score ${item.score}.`).toBe(true);
      expect(item.score).toBeGreaterThanOrEqual(0);
      expect(item.score).toBeLessThanOrEqual(1);
      expect(Number.isFinite(item.uncertainty), `${item.name} hat den Fehler ${item.uncertainty}.`).toBe(true);
      expect(item.uncertainty >= 0, `${item.name} hat einen negativen Fehler.`).toBe(true);
    }

    // A total order: sorted descending, and every pair comparable. NaN breaks both silently.
    for (let i = 1; i < set.items.length; i++) expect(set.items[i - 1]!.score >= set.items[i]!.score).toBe(true);
  });

  // Bans: a different unit, and a gate that multiplies — a NaN rate there orders the list by nothing.
  it('BansSurviveImpossibleRatesToo', () => {
    const meta = new MetaBuilder()
      .champion(1, 'NaN').inLane(1, Lane.Mid, { winRate: Number.NaN, pickRate: Number.NaN, banRate: -1 })
      .champion(2, 'Normal').inLane(2, Lane.Mid, { winRate: 0.53, pickRate: 0.08, banRate: 0.04 })
      .build();

    const state = recommendCommandBuildState(Lane.Mid, [], []);
    const target = { ...new TurnTracker().resolve(state)!, action: 'ban' as const };

    const set = new Recommender(meta, TraitTable.empty).recommend(state, target, LanePredictionResult.empty, null, null, 10);

    for (const item of set.items) expect(Number.isFinite(item.score), `${item.name} hat den Ban-Wert ${item.score}.`).toBe(true);
  });

  // An empty snapshot answers with nothing, not with an exception.
  it('AnEmptySnapshot_AnswersWithAnEmptySet', () => {
    const state = recommendCommandBuildState(Lane.Top, [], []);
    const target = new TurnTracker().resolve(state)!;

    const set = new Recommender(MetaLookup.empty(), TraitTable.empty).recommend(state, target, LanePredictionResult.empty);

    expect(set.items).toHaveLength(0);
  });

  // ----- the Abyss ranking ----------------------------------------------------------------------

  it('TheBenchRankingSurvivesImpossibleRates', () => {
    const stats = new Map<number, AramStat>([
      [1, { championId: 1, winRate: Number.NaN, play: 5_000 }],
      [2, { championId: 2, winRate: 4, play: 5_000 }],
      [3, { championId: 3, winRate: -1, play: 5_000 }],
      [4, { championId: 4, winRate: 0.52, play: INT_MAX }],
    ]);

    const rows = rankAram(1, [2, 3, 4], stats, (id) => `#${id}`);

    for (const row of rows) {
      expect(Number.isFinite(row.score), `${row.name} hat den Score ${row.score}.`).toBe(true);
      expect(row.score).toBeGreaterThanOrEqual(0);
      expect(row.score).toBeLessThanOrEqual(1);
      expect(Number.isFinite(row.uncertainty) && row.uncertainty >= 0).toBe(true);
    }

    for (let i = 1; i < rows.length; i++) expect(rows[i - 1]!.score >= rows[i]!.score).toBe(true);
  });

  // Skipped: TheMatchupGuideRejectsMalformedJsonAsJsonException (3 cases),
  // TheMatchupGuideTurnsAnUnexpectedShapeIntoAnEmptyPlan (5), TheTupleParserRejectsGarbageWithItsOwnException (5)
  // and ReadingFieldsThatAreNotThere_AnswersWithDefaults exercise the OP.GG parsers, which are not in the TS port.
  // Skipped: ACorruptSnapshotFile_IsRefusedWithAReasonRatherThanAnException (5 cases) is SnapshotStore file IO.
});
