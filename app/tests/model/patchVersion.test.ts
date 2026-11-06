// Port of tests/DraftPilot.Core.Tests/PatchVersionTests.cs. Three sources write the patch three
// ways, and the footer's warning hangs on comparing them correctly.

import { describe, expect, it } from 'vitest';
import { patchesBetween, patchLine, samePatchLine } from '../../src/core/model/patchVersion';

describe('PatchVersionTests', () => {
  it.each([
    // Data Dragon.
    ['16.18.1', '16.18'],
    // OP.GG's aggregate.
    ['16.18', '16.18'],
    // The League client, verbatim from /lol-patch/v1/game-version.
    ['16.18.8175716+branch.releases-16-18.code.public.content.release', '16.18'],
    ['16.18+branch.releases', '16.18'],
    ['9.24.1', '9.24'],
  ])('EverySpelling_ReducesToItsPatchLine(%s, %s)', (value, expected) => {
    expect(patchLine(value)).toBe(expected);
  });

  it.each([[null], [''], ['   '], ['16'], ['unbekannt'], ['v16.18'], ['16.']])(
    'WhatIsNotAPatchLine_ReadsAsUnknown(%s)',
    (value) => {
      expect(patchLine(value)).toBe('');
    },
  );

  it('TheSamePatchInDifferentSpellings_Agrees', () => {
    expect(samePatchLine('16.18.1', '16.18.8175716+branch.releases-16-18')).toBe(true);
    expect(samePatchLine('16.18', '16.18.1')).toBe(true);
  });

  it('ADifferentPatch_Disagrees', () => {
    expect(samePatchLine('16.17', '16.18.8175716+branch')).toBe(false);
    expect(samePatchLine('15.24.1', '16.1.1')).toBe(false);
  });

  // An unreadable version must never produce a warning.
  it.each([
    ['16.18', null],
    ['16.18', ''],
    ['', '16.18'],
    ['unbekannt', '16.18'],
  ])('AnUnknownOnEitherSide_CountsAsAgreement(%s, %s)', (left, right) => {
    expect(samePatchLine(left, right)).toBe(true);
  });

  it.each([
    ['16.18', '16.20.8175716+branch.releases-16-20.code.public.release', 2],
    ['16.20', '16.20.1', 0],
    ['16.24', '17.1', 1], // a season boundary: about 24 patches a year
    ['16.20', '16.18', 0], // data newer than the client is not "behind"
    ['', '16.20', 0], // unknown is no distance, never a guess
    ['16.18', null, 0],
  ] as [string | null, string | null, number][])('PatchesBetween_CountsHowFarTheDataLags(%s, %s, %i)', (data, client, expected) => {
    expect(patchesBetween(data, client)).toBe(expected);
  });
});
