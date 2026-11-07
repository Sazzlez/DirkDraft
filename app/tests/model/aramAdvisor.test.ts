// Port of tests/DraftPilot.Core.Tests/AramAdvisorTests.cs: the Abyss decision — keep the champion
// you were handed, or take one off the bench.

import { describe, expect, it } from 'vitest';
import { ARAM_PRIOR, ARAM_ROUNDING_VARIANCE, rankAram, type AramStat } from '../../src/core/model/advisors';
import { draftStateFrom } from '../../src/core/model/draftState';
import { logitVariance, standing } from '../../src/core/model/scoreError';

const Mine = 1;
const Strong = 2;
const Weak = 3;

function name(id: number): string {
  switch (id) {
    case Mine:
      return 'Meiner';
    case Strong:
      return 'Stark';
    case Weak:
      return 'Schwach';
    default:
      return `#${id}`;
  }
}

function stats(...rows: [champion: number, winRate: number, play: number][]): Map<number, AramStat> {
  return new Map(rows.map(([championId, winRate, play]) => [championId, { championId, winRate, play }]));
}

describe('AramAdvisorTests', () => {
  it('TheBetterRecordLeads', () => {
    const rows = rankAram(Mine, [Strong, Weak], stats([Mine, 0.49, 20_000], [Strong, 0.55, 20_000], [Weak, 0.45, 20_000]), name);

    expect(rows.map((row) => row.name)).toEqual(['Stark', 'Meiner', 'Schwach']);
  });

  // The champion in hand belongs in the list even when it loses: the question is "is any of them
  // better than mine", not "which of these is best".
  it('TheChampionInHandIsAlwaysInTheList', () => {
    const rows = rankAram(Mine, [Strong], stats([Mine, 0.45, 20_000], [Strong, 0.55, 20_000]), name);

    expect(rows.some((row) => row.championId === Mine)).toBe(true);
    expect(rows.some((row) => row.reasons.some((reason) => reason.text === 'dein Champ'))).toBe(true);
  });

  // Without a number for the champion in hand there is nothing to compare against.
  it('WithoutANumberForTheOwnChampion_TheListStaysEmpty', () => {
    const rows = rankAram(Mine, [Strong], stats([Strong, 0.55, 20_000]), name);

    expect(rows).toHaveLength(0);
  });

  it('ChampionsWithoutDataAreLeftOutRatherThanRankedAtZero', () => {
    const rows = rankAram(Mine, [Strong, Weak], stats([Mine, 0.5, 20_000], [Strong, 0.53, 20_000]), name);

    expect(rows).toHaveLength(2);
    expect(rows.some((row) => row.championId === Weak)).toBe(false);
  });

  // A champion sitting on the bench AND in your hand is one row, not two.
  it('TheOwnChampionIsNotDuplicatedByTheBench', () => {
    const rows = rankAram(Mine, [Mine, Strong], stats([Mine, 0.5, 9_000], [Strong, 0.51, 9_000]), name);

    expect(rows).toHaveLength(2);
    expect(rows.filter((row) => row.championId === Mine)).toHaveLength(1);
  });

  // A handful of games must not lead the bench; only shrinkage tells the two 60 % apart.
  it('AThinSampleDoesNotLeadTheBench', () => {
    const rows = rankAram(Mine, [Strong], stats([Mine, 0.53, 30_000], [Strong, 0.6, 12]), name);

    expect(rows[0]!.name).toBe('Meiner');
  });

  // OP.GG reports the ARAM rate rounded to two decimals; that point of resolution belongs in the bar.
  it('TheRoundingOfTheReportedRateIsInsideTheErrorBar', () => {
    const rows = rankAram(Mine, [], stats([Mine, 0.52, 400_000]), name);

    // At 400.000 games the sampling error is negligible, so what is left is the rounding.
    const floor = Math.sqrt(ARAM_ROUNDING_VARIANCE) / (0.52 * (1 - 0.52));

    expect(
      rows[0]!.uncertainty,
      `Fehlerbalken ${rows[0]!.uncertainty.toFixed(5)} unterschreitet die Rundung allein (${floor.toFixed(5)}).`,
    ).toBeGreaterThan(floor * 0.9);
  });

  // At large samples the rounding dominates the sampling error several times over.
  it('AtLargeSamplesTheRoundingDominatesTheErrorBar', () => {
    const rows = rankAram(Mine, [], stats([Mine, 0.52, 400_000]), name);

    const samplingOnly = Math.sqrt(logitVariance(0.52, 400_000, ARAM_PRIOR, 0.5));

    expect(
      rows[0]!.uncertainty,
      `Fehlerbalken ${rows[0]!.uncertainty.toFixed(5)} gegen reine Stichprobe ${samplingOnly.toFixed(5)} — ` +
        'die Rundung müsste hier klar überwiegen.',
    ).toBeGreaterThan(3 * samplingOnly);
  });

  // The flip side: a full point over real samples still separates two champions.
  it('AFullPointApartIsStillDecidable', () => {
    const rows = rankAram(Mine, [Strong], stats([Mine, 0.52, 400_000], [Strong, 0.53, 400_000]), name);

    expect(rows[0]!.name).toBe('Stark');
    expect(standing(rows[0]!.score, rows[0]!.uncertainty, rows[1]!.score, rows[1]!.uncertainty)).not.toBe('tied');
  });

  // ----- the bench as it arrives from the client ------------------------------------------------

  function session(): {
    localPlayerCellId: number;
    queueId: number;
    benchEnabled: boolean;
    myTeam: { cellId: number; championId: number; team: number }[];
    benchChampions: { championId: number; isPriority?: boolean }[];
    rerollsRemaining?: number;
  } {
    return {
      localPlayerCellId: 0,
      queueId: 450,
      benchEnabled: true,
      myTeam: [{ cellId: 0, championId: Mine, team: 1 }],
      benchChampions: [],
    };
  }

  it('TheBenchTravelsFromTheSessionIntoTheDraftState', () => {
    const raw = session();
    raw.benchChampions.push({ championId: Strong });
    raw.benchChampions.push({ championId: Weak, isPriority: true });
    raw.rerollsRemaining = 2;

    const state = draftStateFrom(raw);

    expect(state.benchEnabled).toBe(true);
    expect(state.bench).toEqual([Strong, Weak]);
    expect(state.rerollsRemaining).toBe(2);
  });

  // The client repeats an entry while a swap is in flight, and pads the list with zero ids.
  it('EmptyAndRepeatedBenchEntriesAreDropped', () => {
    const raw = session();
    raw.benchChampions.push({ championId: Strong });
    raw.benchChampions.push({ championId: 0 });
    raw.benchChampions.push({ championId: Strong });

    expect(draftStateFrom(raw).bench).toEqual([Strong]);
  });

  // A mode without a bench must not look like a bench that is momentarily empty.
  it('ARiftDraftHasNoBenchAtAll', () => {
    const state = draftStateFrom({ localPlayerCellId: 0, queueId: 420, myTeam: [{ cellId: 0, championId: Mine, team: 1 }] });

    expect(state.benchEnabled).toBe(false);
    expect(state.bench).toHaveLength(0);
  });

  // A recording from before the fields existed must parse, not throw.
  it('ASessionWithoutBenchFields_IsNotABench', () => {
    const state = draftStateFrom(session());

    expect(state.benchEnabled).toBe(true);
    expect(state.bench).toHaveLength(0);
    expect(state.rerollsRemaining).toBe(0);
  });
});
