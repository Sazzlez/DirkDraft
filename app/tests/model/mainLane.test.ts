// Port of tests/DraftPilot.Core.Tests/MainLaneTests.cs.
//
// Where a champion belongs when nobody has said. The build fetch needs a position — OP.GG has no
// "any lane" build — and this is the last answer that is still measured rather than guessed.

import { describe, expect, it } from 'vitest';
import { Lane } from '../../src/core/model/lane';
import { MetaBuilder } from './helpers';

describe('MainLaneTests', () => {
  it('TheLaneWithTheMostGamesWins', () => {
    const meta = new MetaBuilder()
      .champion(1, 'Wanderer')
      .inLane(1, Lane.Top, { play: 4_000 })
      .inLane(1, Lane.Jungle, { play: 31_000 })
      .inLane(1, Lane.Mid, { play: 900 })
      .build();

    expect(meta.mainLane(1)).toBe(Lane.Jungle);
  });

  /**
   * By games, not by win rate. A champion played forty times on a lane it happens to win on is
   * not a champion that belongs there.
   */
  it('ARareLaneWithAGreatRecord_DoesNotWin', () => {
    const meta = new MetaBuilder()
      .champion(1, 'Wanderer')
      .inLane(1, Lane.Top, { play: 80_000, winRate: 0.49 })
      .inLane(1, Lane.Support, { play: 40, winRate: 0.72 })
      .build();

    expect(meta.mainLane(1)).toBe(Lane.Top);
  });

  /** A champion the snapshot lists nowhere has no lane, and the caller must not invent one. */
  it('AChampionWithoutLaneRows_HasNoMainLane', () => {
    const meta = new MetaBuilder().champion(1, 'Neu').build();

    expect(meta.mainLane(1)).toBe(Lane.Unknown);
    expect(meta.mainLane(999)).toBe(Lane.Unknown);
  });

  /**
   * A row that exists but carries no games — the fallback rows the snapshot writes — still
   * places the champion. "Listed with zero games" is more than "not listed at all".
   */
  it('ALaneKnownWithoutGames_StillPlacesTheChampion', () => {
    const meta = new MetaBuilder().champion(1, 'Selten').inLane(1, Lane.Support, { play: 0 }).build();

    expect(meta.mainLane(1)).toBe(Lane.Support);
  });
});
