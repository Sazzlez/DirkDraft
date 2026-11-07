// Port of tests/DraftPilot.Core.Tests/LiveOverlayTests.cs.
//
// The live overlay holds matchups fetched for the draft on screen. It must win over the stocked
// matrix, provide the mirror edge for free, and vanish completely when the draft ends.

import { describe, expect, it } from 'vitest';
import { Lane } from '../../src/core/model/lane';
import { DamageType, type MatchupStat, type MetaLookup } from '../../src/core/model/metaLookup';
import { MetaBuilder } from './helpers';

const A = 1,
  B = 2;

const meta = (): MetaLookup =>
  new MetaBuilder()
    .champion(A, 'Alpha', { damage: DamageType.Physical, tags: ['Fighter'], attackRange: 175, defense: 5 })
    .champion(B, 'Beta', { damage: DamageType.Physical, tags: ['Fighter'], attackRange: 175, defense: 5 })
    .inLane(A, Lane.Top, { winRate: 0.5, tier: 3, play: 2000 })
    .inLane(B, Lane.Top, { winRate: 0.5, tier: 3, play: 2000 })
    .matchup(A, B, Lane.Top, 0.52, 200)
    .build();

const live = (winRate: number, play = 4000): MatchupStat => ({
  championId: A,
  opponentId: B,
  lane: Lane.Top,
  winRate,
  play,
});

describe('LiveOverlayTests', () => {
  it('OverlayWinsOverTheSnapshot', () => {
    const m = meta();
    const before = m.matchup(A, B, Lane.Top)!;

    m.applyLiveMatchups([live(0.6)]);
    const after = m.matchup(A, B, Lane.Top)!;

    expect(after.isLive).toBe(true);
    expect(after.winRate > before.winRate).toBe(true);
    expect(after.play).toBe(4000);
  });

  it('MirrorEdgeIsInferredFromTheLiveData', () => {
    const m = meta();
    m.applyLiveMatchups([live(0.6)]);

    const mirror = m.matchup(B, A, Lane.Top)!;

    expect(mirror.isLive).toBe(true);
    expect(mirror.isInferred).toBe(true);
    expect(mirror.winRate < 0.5).toBe(true);
  });

  it('ClearRestoresTheSnapshotView', () => {
    const m = meta();
    const before = m.matchup(A, B, Lane.Top)!;

    m.applyLiveMatchups([live(0.6)]);
    m.clearLiveMatchups();

    expect(m.liveMatchupCount).toBe(0);
    expect(m.matchup(A, B, Lane.Top)!).toEqual(before);
  });

  it('UnknownChampionsAreIgnored', () => {
    const m = meta();
    m.applyLiveMatchups([{ championId: 999, opponentId: B, lane: Lane.Top, winRate: 0.6, play: 100 }]);

    expect(m.liveMatchupCount).toBe(0);
  });
});
