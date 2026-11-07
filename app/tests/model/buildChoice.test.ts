// Port of tests/DraftPilot.Core.Tests/BuildChoiceTests.cs: which variant of a build slot goes on the
// card when the choice is made on the record rather than on popularity. The thin numbers come from
// the captured Darius-vs-Jax matchup guide, the thick ones from the Darius Top champion analysis.

import { describe, expect, it } from 'vitest';
import { byWinRate, wilsonLowerBound } from '../../src/core/model/advisors';

interface ItemSet {
  items: string[];
  play: number;
  winRate: number;
}

const set = (name: string, play: number, wins: number): ItemSet => ({
  items: [name],
  play,
  winRate: play > 0 ? wins / play : 0,
});

describe('BuildChoiceTests', () => {
  // Thirteen games cannot carry a 13-point lead in raw win rate, and the ranking must not let them.
  it('AThinSample_CannotOutrankAThickOne_OnItsRawRate', () => {
    const ranked = byWinRate([set("Doran's Helm", 13, 8), set("Doran's Blade", 113, 55)], 2);

    expect(ranked[0]!.items[0]).toBe("Doran's Blade");
    expect(ranked[1]!.items[0]).toBe("Doran's Helm");
  });

  // Where both samples are real, the better record wins even if the other was bought more often.
  it('WithRealSamplesOnBothSides_TheBetterRecordWins', () => {
    const ranked = byWinRate(
      [set("Youmuu's Ghostblade", 250_507, 125_757), set("Dead Man's Plate", 251_388, 134_109)],
      2,
    );

    expect(ranked[0]!.items[0]).toBe("Dead Man's Plate");
  });

  // A variant nobody played carries no rate to rank by.
  it('VariantsWithoutGames_AreDropped', () => {
    const ranked = byWinRate([set('Never bought', 0, 0), set('Bought', 40, 21)], 5);

    expect(ranked).toHaveLength(1);
    expect(ranked[0]!.items[0]).toBe('Bought');
  });

  // Same record, more games: the better-established one goes first.
  it('OnAnEqualRecord_TheLargerSampleLeads', () => {
    const ranked = byWinRate([set('Rare', 100, 55), set('Common', 1_000, 550)], 2);

    expect(ranked[0]!.items[0]).toBe('Common');
  });

  it('TheSlotKeepsOnlyAsManyEntriesAsItHasRoomFor', () => {
    const ranked = byWinRate([set('a', 500, 260), set('b', 500, 255), set('c', 500, 250), set('d', 500, 245)], 3);

    expect(ranked).toHaveLength(3);
    expect(ranked.map((s) => s.items[0])).toEqual(['a', 'b', 'c']);
  });

  // The bound sits below the observed rate and closes in on it as the sample grows; without the
  // second property the rule would be a sample-size ranking wearing a win rate's clothes.
  it('TheBoundSitsBelowTheRateAndTightensWithTheSample', () => {
    const thin = wilsonLowerBound(set('thin', 20, 12));
    const thick = wilsonLowerBound(set('thick', 20_000, 12_000));

    expect(thin, `Untergrenze ${thin.toFixed(3)} muss unter der beobachteten Rate 0,600 liegen.`).toBeLessThan(0.6);
    expect(thick, `Untergrenze ${thick.toFixed(3)} muss unter der beobachteten Rate 0,600 liegen.`).toBeLessThan(0.6);
    expect(thick, `Mehr Games muss enger werden: ${thick.toFixed(3)} gegen ${thin.toFixed(3)}.`).toBeGreaterThan(thin);
    expect(
      0.6 - thick,
      `20.000 Games sollten die Untergrenze fast auf die Rate ziehen, sind aber ${(0.6 - thick).toFixed(3)} entfernt.`,
    ).toBeLessThan(0.01);
  });

  // A rate that is not a number must not sort anything; NaN poisons a comparison sort.
  it('ARateThatIsNotANumber_DoesNotBreakTheOrder', () => {
    const broken = set('kaputt', 50, 0);
    broken.winRate = Number.NaN;

    const ranked = byWinRate([broken, set('heil', 50, 30)], 2);

    expect(ranked[0]!.items[0]).toBe('heil');
  });
});
