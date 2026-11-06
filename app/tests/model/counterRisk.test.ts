// Port of tests/DraftPilot.Core.Tests/CounterRiskTests.cs.
//
// What a pick still risks while nobody stands opposite it. The first pick of a draft has no duel
// term at all, so the list answers "how strong is this champion" while the question in that moment
// is "how easily can this be answered" — these tests pin the difference between the two.

import { describe, expect, it } from 'vitest';
import { Lane } from '../../src/core/model/lane';
import type { MetaLookup } from '../../src/core/model/metaLookup';
import { openCounters } from '../../src/core/model/recommender';
import { MetaBuilder } from './helpers';

const Mine = 1,
  Counter = 2,
  Strong = 3,
  Even = 4;

/**
 * Mine wins 52 % on its lane, so an average opponent takes 48 % off it. Counter takes 58 %,
 * which is the excess this is about; Strong is a better champion overall but does nothing
 * special into Mine; Even is exactly average against it.
 */
const meta = (): MetaLookup =>
  new MetaBuilder()
    .champion(Mine, 'Mine')
    .champion(Counter, 'Counter')
    .champion(Strong, 'Strong')
    .champion(Even, 'Even')
    .inLane(Mine, Lane.Top, { winRate: 0.52, play: 40_000 })
    .inLane(Counter, Lane.Top, { winRate: 0.5, play: 40_000 })
    .inLane(Strong, Lane.Top, { winRate: 0.56, play: 40_000 })
    .inLane(Even, Lane.Top, { winRate: 0.5, play: 40_000 })
    .matchup(Counter, Mine, Lane.Top, 0.58, 40_000)
    .matchup(Strong, Mine, Lane.Top, 0.48, 40_000)
    .matchup(Even, Mine, Lane.Top, 0.48, 40_000)
    .build();

describe('CounterRiskTests', () => {
  it('AnAvailableCounter_IsListed', () => {
    const threats = openCounters(meta(), Mine, Lane.Top, new Set<number>());

    expect(threats).toHaveLength(1);
    const threat = threats[0]!;
    expect(threat.championId).toBe(Counter);
    expect(threat.play).toBe(40_000);
  });

  /**
   * The whole point of the chip: a counter nobody can take any more is not a risk. Bans, locked
   * picks and ally hovers all arrive here in the same set.
   */
  it('ACounterThatIsBannedOrTaken_IsNotARisk', () => {
    const threats = openCounters(meta(), Mine, Lane.Top, new Set([Counter]));

    expect(threats).toHaveLength(0);
  });

  /**
   * A champion that is simply strong is not a counter. Strong wins 56 % on the lane and still
   * only 48 % into Mine — exactly what an average opponent gets, so it says nothing about
   * picking into it.
   */
  it('AStrongChampionThatDoesNothingSpecial_IsNotACounter', () => {
    const threats = openCounters(meta(), Mine, Lane.Top, new Set<number>());

    expect(threats.some((threat) => threat.championId === Strong)).toBe(false);
    expect(threats.some((threat) => threat.championId === Even)).toBe(false);
  });

  it('TheStrongestCounterComesFirst_AndTheLimitHolds', () => {
    const m = new MetaBuilder()
      .champion(Mine, 'Mine')
      .champion(Counter, 'Counter')
      .champion(Strong, 'Strong')
      .champion(Even, 'Even')
      .inLane(Mine, Lane.Top, { winRate: 0.5, play: 40_000 })
      .inLane(Counter, Lane.Top, { winRate: 0.5, play: 40_000 })
      .inLane(Strong, Lane.Top, { winRate: 0.5, play: 40_000 })
      .inLane(Even, Lane.Top, { winRate: 0.5, play: 40_000 })
      .matchup(Counter, Mine, Lane.Top, 0.6, 40_000)
      .matchup(Strong, Mine, Lane.Top, 0.56, 40_000)
      .matchup(Even, Mine, Lane.Top, 0.54, 40_000)
      .build();

    const all = openCounters(m, Mine, Lane.Top, new Set<number>(), 0);
    const two = openCounters(m, Mine, Lane.Top, new Set<number>(), 2);

    expect(all.map((threat) => threat.championId)).toEqual([Counter, Strong, Even]);
    expect(two).toHaveLength(2);
  });

  it.each([
    [Lane.Unknown],
    // A lane the champion is not listed on has no reference rate to measure against.
    [Lane.Mid],
  ])('WithoutALaneToStandOn_ThereIsNothingToSay(%s)', (lane) => {
    expect(openCounters(meta(), Mine, lane, new Set<number>())).toHaveLength(0);
  });

  it('AChampionTheSnapshotDoesNotKnow_IsNotAnError', () => {
    expect(openCounters(meta(), 999, Lane.Top, new Set<number>())).toHaveLength(0);
  });
});
