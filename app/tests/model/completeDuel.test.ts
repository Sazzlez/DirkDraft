// Port of tests/DraftPilot.Core.Tests/CompleteDuelTests.cs.
//
// The complete duel list from OP.GG's matchup guide: every opponent a champion has on a lane, not
// the three notable ones. Pinned against the one rule that sets these duels apart — they are not a
// selection, so no listing offset applies.

import { describe, expect, it } from 'vitest';
import { Lane } from '../../src/core/model/lane';
import { MetaLookup, type MatchupStat } from '../../src/core/model/metaLookup';
import { logit, sigmoid } from '../../src/core/model/scoreModel';
import { MetaBuilder } from './helpers';

const Darius = 122,
  Jax = 24;

describe('CompleteDuelTests', () => {
  // Skipped: OP.GG guide parsing (LiveDraftFetcher.ParseCompleteDuels) is not part of the TS model port.
  it.skip('TheWholeListIsRead', () => {});

  // Skipped: OP.GG guide parsing (LiveDraftFetcher.ParseCompleteDuels) is not part of the TS model port.
  it.skip('NoDuelAgainstItself', () => {});

  // Skipped (4 InlineData cases): OP.GG guide parsing (LiveDraftFetcher.ParseCompleteDuels) is not part of the TS model port.
  it.skip.each([[''], ['{}'], ['{"data":{"counters":"nope"}}'], ['{"data":{"counters":[{"champion_id":"x","play":-3,"win":9}]}}']])(
    'AnUnusableAnswerGivesNoDuels(%s)',
    () => {},
  );

  /**
   * A complete-list duel is expected where the line measured for this file puts it. A duel
   * exactly on that line adds nothing; with the plain expectation it would have read as a
   * deficit of the stronger champion.
   */
  it('ACompleteListDuelIsReadAgainstTheMeasuredLine', () => {
    const snapshot = new MetaBuilder()
      .champion(Darius, 'Darius')
      .champion(Jax, 'Jax')
      .inLane(Darius, Lane.Top, { winRate: 0.54, play: 40_000 })
      .inLane(Jax, Lane.Top, { winRate: 0.48, play: 40_000 })
      .snapshotRef();
    snapshot.completeDuelSlope = 0.5;
    snapshot.completeDuelOffset = 0.03;
    snapshot.matchupBaseline = -0.1;

    const meta = new MetaLookup(snapshot);
    const difference = logit(meta.laneStat(Darius, Lane.Top)!.winRate) - logit(meta.laneStat(Jax, Lane.Top)!.winRate);

    // A notable-list expectation follows the file's own bracket and carries the listing offset.
    expect(meta.matchupBaseline(Darius, Jax, Lane.Top)).toBeCloseTo(difference - 0.1, 9);

    meta.applyLiveMatchups([
      {
        championId: Darius,
        opponentId: Jax,
        lane: Lane.Top,
        winRate: sigmoid(0.03 + 0.5 * difference),
        play: 400,
        fromCompleteList: true,
      },
    ]);

    expect(meta.matchupBaseline(Darius, Jax, Lane.Top)).toBeCloseTo(0.03 + 0.5 * difference, 9);
    expect(logit(meta.matchup(Darius, Jax, Lane.Top)!.winRate) - meta.matchupBaseline(Darius, Jax, Lane.Top)).toBeCloseTo(0, 9);
  });

  /** An unusable line in a damaged file falls back to the plain expectation, never to nonsense. */
  it.each([
    [Number.NaN, 0],
    [0, 0],
    [-0.5, 0],
    [3, 0],
    [0.8, Number.POSITIVE_INFINITY],
  ])('ABrokenLine_ReadsAsThePlainExpectation(%s, %s)', (slope, offset) => {
    const snapshot = new MetaBuilder()
      .champion(Darius, 'Darius')
      .champion(Jax, 'Jax')
      .inLane(Darius, Lane.Top, { winRate: 0.54, play: 40_000 })
      .inLane(Jax, Lane.Top, { winRate: 0.48, play: 40_000 })
      .snapshotRef();
    snapshot.completeDuelSlope = slope;
    snapshot.completeDuelOffset = offset;

    const meta = new MetaLookup(snapshot);
    meta.applyLiveMatchups([{ championId: Darius, opponentId: Jax, lane: Lane.Top, winRate: 0.5, play: 400, fromCompleteList: true }]);

    const difference = logit(meta.laneStat(Darius, Lane.Top)!.winRate) - logit(meta.laneStat(Jax, Lane.Top)!.winRate);
    expect(meta.matchupBaseline(Darius, Jax, Lane.Top)).toBeCloseTo(difference, 9);
  });

  // Skipped: SnapshotBuilder.MeasureCompleteDuelLine (snapshot building) is not part of the TS model port.
  it.skip('TheLineIsFittedFromTheSampleDuels', () => {});

  /**
   * Every candidate reads the enemy's list from the other side. An edge exactly where its own
   * expectation put it says nothing, so its mirror must say nothing either: both offsets belong
   * to the direction OP.GG wrote down and flip with it.
   */
  it.each([[false], [true]])('AMirroredEdgeAtItsExpectation_ContributesNothing(%s)', (complete) => {
    const snapshot = new MetaBuilder()
      .champion(Darius, 'Darius')
      .champion(Jax, 'Jax')
      .inLane(Darius, Lane.Top, { winRate: 0.53, play: 40_000 })
      .inLane(Jax, Lane.Top, { winRate: 0.48, play: 40_000 })
      .snapshotRef();
    snapshot.matchupBaseline = -0.03;
    snapshot.completeDuelSlope = 0.76;
    snapshot.completeDuelOffset = 0.04;

    const meta = new MetaLookup(snapshot);
    const difference = logit(meta.laneStat(Jax, Lane.Top)!.winRate) - logit(meta.laneStat(Darius, Lane.Top)!.winRate);
    const expected = complete ? 0.04 + 0.76 * difference : -0.03 + difference;

    // Jax's own list names Darius, exactly as expected.
    meta.applyLiveMatchups([
      {
        championId: Jax,
        opponentId: Darius,
        lane: Lane.Top,
        winRate: sigmoid(expected),
        play: 300,
        fromCompleteList: complete,
      },
    ]);

    const stored = meta.matchup(Jax, Darius, Lane.Top)!;
    expect(logit(stored.winRate) - meta.matchupBaseline(Jax, Darius, Lane.Top)).toBeCloseTo(0, 9);

    const mirror = meta.matchup(Darius, Jax, Lane.Top)!;
    expect(mirror.isInferred).toBe(true);
    expect(logit(mirror.winRate) - meta.matchupBaseline(Darius, Jax, Lane.Top)).toBeCloseTo(0, 9);
    expect(meta.matchupBaseline(Darius, Jax, Lane.Top)).toBeCloseTo(-meta.matchupBaseline(Jax, Darius, Lane.Top), 12);
  });

  /** The same for the stored file's notable lists, whose mirrors are inferred at load. */
  it('AStoredMirrorAtItsExpectation_ContributesNothing', () => {
    const builder = new MetaBuilder()
      .champion(Darius, 'Darius')
      .champion(Jax, 'Jax')
      .inLane(Darius, Lane.Top, { winRate: 0.53, play: 40_000 })
      .inLane(Jax, Lane.Top, { winRate: 0.48, play: 40_000 })
      .matchupBaseline(-0.03);
    const lanes = new MetaLookup(builder.snapshotRef());
    const expected = -0.03 + logit(lanes.laneStat(Jax, Lane.Top)!.winRate) - logit(lanes.laneStat(Darius, Lane.Top)!.winRate);

    const meta = new MetaLookup(builder.matchup(Jax, Darius, Lane.Top, sigmoid(expected), 200).snapshotRef());

    const mirror = meta.matchup(Darius, Jax, Lane.Top)!;
    expect(mirror.isInferred).toBe(true);
    expect(logit(mirror.winRate) - meta.matchupBaseline(Darius, Jax, Lane.Top)).toBeCloseTo(0, 9);
  });

  /**
   * A notable-list duel names an extreme, so the complete list wins whatever the samples — in
   * both orders of arrival, and for the mirrored edge a candidate actually reads.
   */
  it.each([[true], [false]])('TheCompleteListWinsOverTheNotableOne(%s)', (completeFirst) => {
    const meta = new MetaLookup(
      new MetaBuilder()
        .champion(Darius, 'Darius')
        .champion(Jax, 'Jax')
        .inLane(Darius, Lane.Top, { winRate: 0.52, play: 40_000 })
        .inLane(Jax, Lane.Top, { winRate: 0.49, play: 40_000 })
        .snapshotRef(),
    );

    const notable: MatchupStat = { championId: Jax, opponentId: Darius, lane: Lane.Top, winRate: 0.4, play: 5_000 };
    const complete: MatchupStat = { championId: Jax, opponentId: Darius, lane: Lane.Top, winRate: 0.47, play: 300, fromCompleteList: true };

    meta.applyLiveMatchups(completeFirst ? [complete] : [notable]);
    meta.applyLiveMatchups(completeFirst ? [notable] : [complete]);

    expect(meta.matchup(Jax, Darius, Lane.Top)!.fromCompleteList).toBe(true);
    expect(meta.matchup(Darius, Jax, Lane.Top)!.fromCompleteList).toBe(true);
    expect(meta.matchup(Darius, Jax, Lane.Top)!.play).toBe(300);
  });

  /**
   * The rule these duels exist for. A notable-opponent edge sits above or below the plain
   * lane-rate expectation by the listing offset; a complete-list edge does not.
   */
  it('ACompleteListDuelIsMeasuredWithoutTheListingOffset', () => {
    const Offset = -0.1;

    const snapshot = new MetaBuilder()
      .champion(Darius, 'Darius')
      .champion(Jax, 'Jax')
      .inLane(Darius, Lane.Top, { winRate: 0.52, play: 40_000 })
      .inLane(Jax, Lane.Top, { winRate: 0.49, play: 40_000 })
      .snapshotRef();
    snapshot.matchupBaseline = Offset;

    const meta = new MetaLookup(snapshot);
    const plain = logit(meta.laneStat(Darius, Lane.Top)!.winRate) - logit(meta.laneStat(Jax, Lane.Top)!.winRate);

    // Without any edge, the expectation carries the listing offset.
    expect(meta.matchupBaseline(Darius, Jax, Lane.Top)).toBeCloseTo(plain + Offset, 9);

    // A complete-list duel exactly at the plain expectation: no offset, so it is exactly expected.
    meta.applyLiveMatchups([
      {
        championId: Darius,
        opponentId: Jax,
        lane: Lane.Top,
        winRate: sigmoid(plain),
        play: 300,
        fromCompleteList: true,
      },
    ]);

    const view = meta.matchup(Darius, Jax, Lane.Top)!;

    expect(view.fromCompleteList).toBe(true);
    expect(meta.matchupBaseline(Darius, Jax, Lane.Top)).toBeCloseTo(plain, 9);
    expect(logit(view.winRate) - meta.matchupBaseline(Darius, Jax, Lane.Top)).toBeCloseTo(0, 9);

    // And from the other side, which is how a candidate meets the revealed enemy.
    const mirror = meta.matchup(Jax, Darius, Lane.Top)!;
    expect(mirror.fromCompleteList).toBe(true);
    expect(logit(mirror.winRate) - meta.matchupBaseline(Jax, Darius, Lane.Top)).toBeCloseTo(0, 9);
  });
});
