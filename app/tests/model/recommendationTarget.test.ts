// Port of tests/DraftPilot.Core.Tests/RecommendationTargetTests.cs.
//
// When a pick list stops being advice. This decides which content the widest column carries for
// the last minute of every draft, so the boundary matters more than it looks: one seat too many
// and the panel hides a list somebody still needs.

import { describe, expect, it } from 'vitest';
import { draftStateFrom, localSlot, type DraftState } from '../../src/core/model/draftState';
import { isSettled, TurnTracker, type RecommendationTarget } from '../../src/core/model/turnTracker';
import { SessionBuilder } from './helpers';

const Mine = 122,
  Ally = 64,
  Enemy = 24;

const target = (state: DraftState, following: boolean): RecommendationTarget => ({
  slot: localSlot(state)!,
  action: 'pick',
  isFollowingTurn: following,
});

describe('RecommendationTargetTests', () => {
  it('OurOwnLockedSeatWithNobodyOnTheClock_IsSettled', () => {
    const state = draftStateFrom(new SessionBuilder().localPlayer(0).locked(0, Mine).locked(7, Enemy).build());

    expect(isSettled(target(state, false))).toBe(true);
  });

  /**
   * The seat is on the clock — that is a live turn, and the list is the whole point of the
   * window. Not settled even though the seat happens to carry a champion already.
   */
  it('ASeatOnTheClock_IsNeverSettled', () => {
    const state = draftStateFrom(new SessionBuilder().localPlayer(0).locked(0, Mine).onClock(0, 'pick').build());

    expect(isSettled(target(state, true))).toBe(false);
  });

  it('AnUnlockedSeat_IsNeverSettled', () => {
    // Before our own pick there is nothing settled about it, whoever is on the clock.
    const state = draftStateFrom(new SessionBuilder().localPlayer(0).hovering(0, Mine).locked(7, Enemy).build());

    expect(isSettled(target(state, false))).toBe(false);
  });

  /**
   * A team-mate on the clock keeps the list, which is why the rule reads the ADVISED seat rather
   * than the local player: the advised seat is then the team-mate, and theirs is not locked.
   */
  it('ATeamMateStillPicking_KeepsTheList', () => {
    const state = draftStateFrom(new SessionBuilder().localPlayer(0).locked(0, Mine).onClock(1, 'pick').build());

    const resolved = new TurnTracker().resolve(state);

    expect(resolved).not.toBeNull();
    expect(resolved!.slot.cellId).toBe(1);
    expect(isSettled(resolved!)).toBe(false);
  });

  /**
   * The state the panel was built for: our pick is in, a team-mate has already picked too, and
   * the enemy is on the clock. The tracker falls back to the last advised seat, and whichever it
   * picks is locked — so the list has nothing left to say.
   */
  it('WithTheEnemyOnTheClockAfterOurTeamHasPicked_TheListIsSettled', () => {
    const tracker = new TurnTracker();

    const picking = draftStateFrom(new SessionBuilder().localPlayer(0).locked(0, Mine).onClock(1, 'pick').build());

    expect(isSettled(tracker.resolve(picking)!)).toBe(false);

    const enemyTurn = draftStateFrom(new SessionBuilder().localPlayer(0).locked(0, Mine).locked(1, Ally).onClock(8, 'pick').build());

    const resolved = tracker.resolve(enemyTurn);

    expect(resolved).not.toBeNull();
    expect(isSettled(resolved!)).toBe(true);
  });
});
