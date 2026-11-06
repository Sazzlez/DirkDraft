// Port of tests/DraftPilot.Core.Tests/QueueKindsTests.cs: which queue a draft belongs to, and
// whether the game is played on lanes (every number in the snapshot is a per-lane statistic).

import { describe, expect, it } from 'vitest';
import { draftStateFrom } from '../../src/core/model/draftState';
import { isAram, laneCaveat, queueDisplay, queueFromId, usesLanes, type QueueKind } from '../../src/core/model/queue';

describe('QueueKindsTests', () => {
  it.each([
    [420, 'rankedSolo'],
    [440, 'rankedFlex'],
    [400, 'normalDraft'],
    [430, 'normalBlind'],
    [480, 'swiftplay'],
    [450, 'aram'],
    [2400, 'aramMayhem'],
    [1700, 'arena'],
    [900, 'rotating'],
  ] as [number, QueueKind][])('KnownQueueIds_AreNamed(%i, %s)', (id, expected) => {
    expect(queueFromId(id)).toBe(expected);
  });

  // The ids come from the client's own queue table (/lol-game-queues/v1/queues, read 2026-09-19).
  it.each([
    [420, 'Ranked Solo/Duo'],
    [440, 'Ranked Flex'],
    [450, 'ARAM'],
    [2400, 'ARAM Mayhem'],
  ])('TheFourQueuesTheToolIsFor_AreNamedInFull(%i, %s)', (id, expected) => {
    expect(queueDisplay(queueFromId(id))).toBe(expected);
  });

  // Mayhem ships as five queues; all five are the same game as far as anything downstream cares.
  it.each([[2400], [2410], [2450], [3270], [3280]])('EveryMayhemVariant_IsTheSameMode(%i)', (id) => {
    expect(queueFromId(id)).toBe('aramMayhem');
  });

  // A custom ARAM Chaos lobby is queue 3270 and reports itself as custom; the id knows better.
  it('ACustomLobbyOfARealMode_KeepsThatMode', () => {
    const kind = queueFromId(3270, true);

    expect(kind).toBe('aramMayhem');
    expect(usesLanes(kind)).toBe(false);
    expect(isAram(kind)).toBe(true);
  });

  // An unseen id is a lane game on purpose: a wrong warning in ranked costs more than a missing one.
  it('AnUnknownQueueId_IsAssumedToUseLanes', () => {
    const kind = queueFromId(31337);

    expect(kind).toBe('unknown');
    expect(usesLanes(kind)).toBe(true);
    expect(laneCaveat(kind)).toBe('');
  });

  it('ACustomLobby_IsRecognisedByItsOwnFlag', () => {
    // No queue id is sent for a custom game, so the flag is the only signal.
    const kind = queueFromId(0, true);

    expect(kind).toBe('custom');

    // Usually a normal Rift game, so the lane numbers still apply.
    expect(usesLanes(kind)).toBe(true);
  });

  it.each([['aram'], ['aramMayhem'], ['arena'], ['rotating']] as [QueueKind][])(
    'ModesWithoutLanes_SayWhyTheNumbersDoNotFit(%s)',
    (kind) => {
      expect(usesLanes(kind)).toBe(false);
      expect(laneCaveat(kind)).not.toBe('');
    },
  );

  it.each([['rankedSolo'], ['normalBlind'], ['swiftplay'], ['clash'], ['bots']] as [QueueKind][])(
    'LaneModes_SayNothing(%s)',
    (kind) => {
      expect(usesLanes(kind)).toBe(true);
      expect(laneCaveat(kind)).toBe('');
    },
  );

  it.each([
    ['aram', true],
    ['aramMayhem', true],
    ['rankedSolo', false],
    ['arena', false],
  ] as [QueueKind, boolean][])('TheAbyssQueues_AreRecognisedTogether(%s, %s)', (kind, expected) => {
    expect(isAram(kind)).toBe(expected);
  });

  // Skipped: EachQueue_AsksOpGgForItsOwnPopulation (8 cases), ModesWithoutASource_AskForNothing
  // (2 cases) and Mayhem_SaysThatItsNumbersAreBorrowedFromAram test QueueKinds.OpGgMode/ModeCaveat,
  // which are OP.GG-specific and intentionally dropped in 2.0.

  it('TheQueueTravelsFromTheSessionIntoTheDraftState', () => {
    const session = { localPlayerCellId: 0, queueId: 450, myTeam: [{ cellId: 0, championId: 0, team: 1 }] };

    expect(draftStateFrom(session).queue).toBe('aram');
  });

  it('AMayhemSession_ArrivesAsMayhem', () => {
    const session = { localPlayerCellId: 0, queueId: 2400, myTeam: [{ cellId: 0, championId: 0, team: 1 }] };

    expect(draftStateFrom(session).queue).toBe('aramMayhem');
  });

  // A recording made before the field existed carries no id, and must not warn.
  it('ASessionWithoutAQueueId_StaysUnknown', () => {
    const session = { localPlayerCellId: 0, myTeam: [{ cellId: 0, championId: 0, team: 1 }] };

    expect(draftStateFrom(session).queue).toBe('unknown');
  });
});
