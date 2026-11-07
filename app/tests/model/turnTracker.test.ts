// Port of tests/DraftPilot.Core.Tests/TurnTrackerTests.cs.

import { describe, expect, it } from 'vitest';
import { draftStateFrom, INACTIVE } from '../../src/core/model/draftState';
import { Lane } from '../../src/core/model/lane';
import { TurnTracker } from '../../src/core/model/turnTracker';
import { SessionBuilder } from './helpers';

describe('TurnTrackerTests', () => {
  it('FollowsAllyOnTheClock', () => {
    const state = draftStateFrom(new SessionBuilder().localPlayer(2).onClock(4, 'pick').build());
    const target = new TurnTracker().resolve(state);

    expect(target).not.toBeNull();
    expect(target!.slot.cellId).toBe(4);
    expect(target!.slot.assignedLane).toBe(Lane.Support);
    expect(target!.action).toBe('pick');
    expect(target!.isFollowingTurn).toBe(true);
  });

  it('SwitchesAsTheClockMoves', () => {
    const tracker = new TurnTracker();

    tracker.resolve(draftStateFrom(new SessionBuilder().onClock(0, 'pick').build()));
    const second = tracker.resolve(draftStateFrom(new SessionBuilder().onClock(3, 'pick').build()));

    expect(second).not.toBeNull();
    expect(second!.slot.cellId).toBe(3);
  });

  it('EnemyOnTheClock_KeepsLastAllySeatAsOutlook', () => {
    const tracker = new TurnTracker();
    tracker.resolve(draftStateFrom(new SessionBuilder().onClock(1, 'pick').build()));

    const target = tracker.resolve(draftStateFrom(new SessionBuilder().onClock(7, 'pick').build()));

    expect(target).not.toBeNull();
    expect(target!.slot.cellId).toBe(1);
    expect(target!.isFollowingTurn).toBe(false);
  });

  it('NobodyOnTheClock_FallsBackToLocalPlayer', () => {
    const state = draftStateFrom(new SessionBuilder().localPlayer(3).phase('PLANNING').build());

    const target = new TurnTracker().resolve(state);

    expect(target).not.toBeNull();
    expect(target!.slot.cellId).toBe(3);
    expect(target!.isFollowingTurn).toBe(false);
    expect(target!.action).toBe('pick');
  });

  it('BanRoundOutlook_ShowsBansRatherThanPicks', () => {
    // While the enemy is banning, the whole team is thinking about bans too.
    const tracker = new TurnTracker();
    const state = draftStateFrom(new SessionBuilder().localPlayer(2).onClock(8, 'ban').build());

    const target = tracker.resolve(state);

    expect(target).not.toBeNull();
    expect(target!.action).toBe('ban');
    expect(target!.isFollowingTurn).toBe(false);
  });

  it('ManualSelection_HoldsWhileTheSameSeatIsOnTheClock', () => {
    const tracker = new TurnTracker();
    const state = draftStateFrom(new SessionBuilder().onClock(0, 'pick').build());
    tracker.resolve(state);

    tracker.selectManually(2, state);
    const target = tracker.resolve(state);

    expect(target).not.toBeNull();
    expect(target!.slot.cellId).toBe(2);
    expect(target!.isFollowingTurn).toBe(false);
  });

  it('ManualSelection_ReleasesWhenTheClockMovesOn', () => {
    const tracker = new TurnTracker();
    const first = draftStateFrom(new SessionBuilder().onClock(0, 'pick').build());
    tracker.resolve(first);
    tracker.selectManually(2, first);

    const target = tracker.resolve(draftStateFrom(new SessionBuilder().onClock(3, 'pick').build()));

    expect(target).not.toBeNull();
    expect(target!.slot.cellId).toBe(3);
    expect(target!.isFollowingTurn).toBe(true);
  });

  it('ManualSelection_CanOnlyTargetOwnTeam', () => {
    // Enemy seats are not advisable; the request is ignored rather than half-applied.
    const tracker = new TurnTracker();
    const state = draftStateFrom(new SessionBuilder().onClock(0, 'pick').build());

    tracker.selectManually(7, state);
    const target = tracker.resolve(state);

    expect(target).not.toBeNull();
    expect(target!.slot.cellId).toBe(0);
  });

  it('Reset_ClearsTheManualSelection', () => {
    const tracker = new TurnTracker();
    const state = draftStateFrom(new SessionBuilder().onClock(0, 'pick').build());
    tracker.selectManually(2, state);

    tracker.reset();

    const target = tracker.resolve(state);
    expect(target).not.toBeNull();
    expect(target!.slot.cellId).toBe(0);
  });

  it('InactiveDraft_HasNoTarget', () => {
    expect(new TurnTracker().resolve(INACTIVE)).toBeNull();
  });
});
