// Port of tests/DraftPilot.Core.Tests/DraftStateTests.cs. Where C# deserialises JSON into a
// ChampSelectSession, the parsed object goes to draftStateFrom directly.

import { describe, expect, it } from 'vitest';
import { draftStateFrom, effectiveChampionId, type DraftPhase } from '../../src/core/model/draftState';
import { Lane } from '../../src/core/model/lane';
import { SessionBuilder } from './helpers';

describe('DraftStateTests', () => {
  it('NullSession_IsInactive', () => {
    expect(draftStateFrom(null).isActive).toBe(false);
  });

  it('Spectating_IsInactive', () => {
    // Spectated drafts are not ours to advise on.
    expect(draftStateFrom(new SessionBuilder().spectating().build()).isActive).toBe(false);
  });

  it.each([
    [0, Lane.Top],
    [1, Lane.Jungle],
    [2, Lane.Mid],
    [3, Lane.Adc],
    [4, Lane.Support],
  ])('AllyLanes_ComeFromAssignedPosition(%i, %i)', (cellId, expected) => {
    const state = draftStateFrom(new SessionBuilder().build());

    expect(state.allies[cellId]!.assignedLane).toBe(expected);
  });

  it('EnemyLanes_AreAlwaysUnknown', () => {
    // Riot hides enemy positions; anything else would be a parsing bug pretending to know.
    const state = draftStateFrom(new SessionBuilder().build());

    for (const slot of state.enemies) expect(slot.assignedLane).toBe(Lane.Unknown);
  });

  it('MissingAllyPosition_IsUnknownRatherThanGuessed', () => {
    const state = draftStateFrom(new SessionBuilder().allyLane(0, '').build());

    expect(state.allies[0]!.assignedLane).toBe(Lane.Unknown);
  });

  it('Bans_MergeSummaryAndCompletedActions', () => {
    const session = new SessionBuilder().summaryBans([157], [555]).completedBan(1, 84).completedBan(6, 200).build();

    const state = draftStateFrom(session);

    expect(state.allyBans).toEqual([157, 84]);
    expect(state.enemyBans).toEqual([555, 200]);
  });

  it('Bans_AreNotDuplicatedWhenPresentInBothSources', () => {
    const session = new SessionBuilder().summaryBans([157], []).completedBan(0, 157).build();

    expect(draftStateFrom(session).allyBans).toEqual([157]);
  });

  it('IncompleteBan_IsNotCountedYet', () => {
    const session = new SessionBuilder().action(0, 157, 'ban', false, true).build();

    expect(draftStateFrom(session).allyBans).toHaveLength(0);
  });

  it('Unavailable_CoversBansAndLockedPicks', () => {
    const session = new SessionBuilder().summaryBans([157], [555]).locked(0, 266).locked(5, 24).hovering(2, 103).build();

    const state = draftStateFrom(session);

    expect(state.unavailable.has(157)).toBe(true);
    expect(state.unavailable.has(555)).toBe(true);
    expect(state.unavailable.has(266)).toBe(true);
    expect(state.unavailable.has(24)).toBe(true);

    // A hover is not a commitment, so it stays available.
    expect(state.unavailable.has(103)).toBe(false);
  });

  it('Turn_PrefersLocalPlayerWhenSeveralActionsRunAtOnce', () => {
    const session = new SessionBuilder().localPlayer(2).onClock(1, 'ban').onClock(2, 'ban').onClock(6, 'ban').build();

    const turn = draftStateFrom(session).turn;

    expect(turn).not.toBeNull();
    expect(turn!.cellId).toBe(2);
    expect(turn!.isLocalPlayer).toBe(true);
    expect(turn!.action).toBe('ban');
  });

  it('Turn_PrefersLowestAllyCellWhenLocalPlayerIsNotActing', () => {
    const session = new SessionBuilder().localPlayer(2).onClock(6, 'pick').onClock(3, 'pick').onClock(1, 'pick').build();

    const turn = draftStateFrom(session).turn;

    expect(turn).not.toBeNull();
    expect(turn!.cellId).toBe(1);
    expect(turn!.isAlly).toBe(true);
  });

  it('Turn_FallsBackToEnemyWhenOnlyTheyAreActing', () => {
    const session = new SessionBuilder().localPlayer(2).onClock(7, 'pick').build();

    const turn = draftStateFrom(session).turn;

    expect(turn).not.toBeNull();
    expect(turn!.cellId).toBe(7);
    expect(turn!.isAlly).toBe(false);
  });

  it('Turn_IgnoresCompletedAndUnknownActions', () => {
    const session = new SessionBuilder()
      .action(0, 266, 'pick', true, false)
      .action(1, 0, 'ten_bans_reveal', false, true)
      .build();

    expect(draftStateFrom(session).turn).toBeNull();
  });

  it.each([
    ['PLANNING', 'planning'],
    ['BAN_PICK', 'banPick'],
    ['FINALIZATION', 'finalization'],
    ['SOMETHING_NEW', 'unknown'],
  ] as [string, DraftPhase][])('Phase_IsMappedAndUnknownIsTolerated(%s, %s)', (raw, expected) => {
    expect(draftStateFrom(new SessionBuilder().phase(raw).build()).phase).toBe(expected);
  });

  it('EffectiveChampion_PrefersLockedOverHover', () => {
    const session = new SessionBuilder().hovering(0, 103).locked(0, 266).build();

    expect(effectiveChampionId(draftStateFrom(session).allies[0]!)).toBe(266);
  });

  it('ExplicitJsonNulls_DoNotThrow', () => {
    // The LCU sends explicit nulls, and "never throws on odd input" has to survive that.
    const json = `{
      "localPlayerCellId": 2,
      "myTeam": null,
      "theirTeam": null,
      "actions": null,
      "bans": null,
      "timer": null,
      "isSpectating": false
    }`;

    expect(draftStateFrom(JSON.parse(json)).isActive).toBe(false);
  });

  it('NullActionGroupsAndPlayers_AreSkipped', () => {
    const json = `{
      "localPlayerCellId": 0,
      "myTeam": [ { "cellId": 0, "championId": 122 }, null ],
      "theirTeam": [ { "cellId": 5 } ],
      "actions": [ null, [ null, { "actorCellId": 0, "type": "ban", "championId": 51, "completed": true, "isAllyAction": true } ] ],
      "bans": { "myTeamBans": [], "theirTeamBans": [] },
      "timer": { "phase": "BAN_PICK", "adjustedTimeLeftInPhase": 10000 }
    }`;

    const state = draftStateFrom(JSON.parse(json));

    expect(state.isActive).toBe(true);
    expect(state.allies).toHaveLength(1);
    expect(state.allyBans).toContain(51);
  });
});
