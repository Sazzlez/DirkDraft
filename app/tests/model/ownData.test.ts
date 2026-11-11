// DirkDraft's own data (source "riot"): complete duels, no OP.GG wording, builds by best win rate.

import { describe, expect, it } from 'vitest';
import { Lane } from '../../src/core/model/lane';
import { MetaLookup } from '../../src/core/model/metaLookup';
import { termHint } from '../../src/core/model/recommender';
import { clearance, LEAD_SIGMA, riotPlan, slotWeights, type BuildCount, type ChampionBuildStats, type RiotBuildFile } from '../../src/core/riotBuilds';
import { MetaBuilder } from './helpers';

function ownMeta(): MetaLookup {
  const builder = new MetaBuilder()
    .champion(1, 'Aatrox')
    .champion(2, 'Jax')
    .inLane(1, Lane.Top, { winRate: 0.52, play: 5000 })
    .inLane(2, Lane.Top, { winRate: 0.5, play: 5000 })
    .matchupBaseline(-0.2);
  const snapshot = builder.snapshotRef();
  snapshot.source = 'riot';
  snapshot.completeDuelSlope = 1;
  snapshot.completeDuelOffset = 0;
  snapshot.matchups.push(
    { championId: 1, opponentId: 2, lane: Lane.Top, winRate: 0.55, play: 800, fromCompleteList: true },
    { championId: 2, opponentId: 1, lane: Lane.Top, winRate: 0.45, play: 800, fromCompleteList: true },
  );
  return builder.build();
}

describe('own data in the model', () => {
  it('shrinks lane rows with the weight measured on our own count (800), OP.GG rows with 300', () => {
    const own = ownMeta();
    expect(own.lanePrior).toBe(800);
    // 52 % over 5.000 games, pulled towards the lane target with 800 pseudo games.
    const target = own.laneTarget;
    expect(own.laneStat(1, Lane.Top)!.winRate).toBeCloseTo((0.52 * 5000 + target * 800) / 5800, 12);
    const opgg = new MetaBuilder().champion(1, 'Aatrox').inLane(1, Lane.Top, { winRate: 0.52, play: 5000 }).build();
    expect(opgg.lanePrior).toBe(300);
  });

  it('reads every duel as measured and complete', () => {
    const meta = ownMeta();
    const view = meta.matchup(1, 2, Lane.Top)!;
    expect(meta.isOwnData).toBe(true);
    expect(view.fromCompleteList).toBe(true);
    expect(view.isInferred).toBe(false);
  });

  it('expects a duel from the complete-duel line, not from the OP.GG listing offset', () => {
    const meta = ownMeta();
    // Slope 1, offset 0: exactly the lane-rate difference; the -0.2 listing offset plays no part.
    expect(meta.matchupBaseline(1, 2, Lane.Top)).toBeCloseTo(meta.laneRateDifference(1, 2, Lane.Top)!, 12);
  });

  it('keeps both directions complementary after shrinking', () => {
    const meta = ownMeta();
    expect(meta.matchup(1, 2, Lane.Top)!.winRate + meta.matchup(2, 1, Lane.Top)!.winRate).toBeCloseTo(1, 12);
  });

  it('explains lane strength without OP.GG', () => {
    expect(termHint('laneStrength', true)).not.toContain('OP.GG');
    expect(termHint('laneStrength')).toContain('OP.GG');
  });
});

const count = (ids: number[], games: number, wins: number, text?: string): BuildCount => ({ ids, games, wins, ...(text ? { text } : {}) });

function stats(championId: number, lane: Lane, games: number, extra: Partial<ChampionBuildStats> = {}): ChampionBuildStats {
  return {
    championId,
    lane,
    tier: 'platinum',
    games,
    wins: Math.round(games / 2),
    runes: [],
    spells: [],
    starters: [],
    boots: [],
    core: [],
    fourth: [],
    fifth: [],
    sixth: [],
    supportChoice: [],
    skillOrder: [],
    skillMax: [],
    ...extra,
  };
}

const file = (builds: ChampionBuildStats[]): RiotBuildFile => ({
  version: 1,
  patch: '16.22',
  platform: 'euw1',
  collectedAtUtc: '2026-11-06T00:00:00Z',
  matches: { platinum: 1000 },
  builds,
});

const core = [count([1, 2, 3], 40, 20)];

describe('builds from own data', () => {
  it('without a measurable spread between options, the most played one leads — a perfect thin record does not', () => {
    const plan = riotPlan(
      file([
        stats(122, Lane.Top, 400, {
          core,
          boots: [count([3047], 8, 8), count([3111], 113, 55), count([3006], 60, 31)],
          runes: [count([8000, 8010, 9111, 9104, 8299, 8400, 8473, 8242, 5005, 5008, 5001], 9, 9), count([8200, 8230, 8275, 8234, 8236, 8000, 9104, 9111, 5005, 5008, 5001], 180, 88)],
        }),
      ]),
      122,
      Lane.Top,
    )!;
    // The Wilson bound used to rank 8 of 8 (lower bound 68 %) above 113 games at 49 %.
    expect(plan.boots.map((o) => o.ids[0])).toEqual([3111, 3006]);
    expect(plan.runes!.games).toBe(180);
  });

  // Sixty rows where option B wins 60 % over hundreds of games and A 40 %: a spread no noise explains.
  const spread = (slotName: 'boots' | 'core' | 'fourth' | 'starters', a: number[], b: number[]) =>
    [...Array(60)].map((_, i) => stats(1000 + i, Lane.Top, 1000, { core, [slotName]: [count(a, 600, 240), count(b, 400, 240)] }));

  it('follows the rates once the file shows options truly differ', () => {
    const f = file(spread('starters', [1055, 2003], [1054, 2003]));
    expect(Number.isFinite(slotWeights(f).starters)).toBe(true);
    expect(riotPlan(f, 1000, Lane.Top)!.starters.map((o) => o.ids[0])).toEqual([1054, 1055]);
  });

  it('follows them for boots and items only where they hold within the same game state', () => {
    const f = file(spread('boots', [3047], [3111]));
    expect(Number.isFinite(slotWeights(f).boots)).toBe(true);
    // No counts by game state: the most played pair stays, however clear the slot looks.
    expect(riotPlan(f, 1000, Lane.Top)!.boots[0]!.ids).toEqual([3047]);
    // The lead explains most of the difference: 1,8 standard errors within the states.
    expect(riotPlan(f, 1000, Lane.Top, () => 1.8)!.boots[0]!.ids).toEqual([3047]);
    // Nothing to compare (an item bought from ahead, or no state with both).
    expect(riotPlan(f, 1000, Lane.Top, () => null)!.boots[0]!.ids).toEqual([3047]);
    // Clear within the states too.
    const asked: unknown[][] = [];
    const clear = riotPlan(f, 1000, Lane.Top, (...args) => (asked.push(args), 4))!;
    expect(clear.boots[0]!.ids).toEqual([3111]);
    expect(asked).toContainEqual([1000, Lane.Top, 'boots', [3111], [3047]]);

    const late = file(spread('fourth', [3089], [3041]));
    expect(riotPlan(late, 1000, Lane.Top)!.late[0]!.ids).toEqual([3089]);
    expect(riotPlan(late, 1000, Lane.Top, (_c, _l, slotKind) => (slotKind === 'late' ? 4 : null))!.late[0]!.ids).toEqual([3041]);
  });

  it('compares two cores by the items that tell them apart, and never by their order alone', () => {
    const cores = (b: number[]) => {
      const rows = [...Array(60)].map((_, i) => stats(1000 + i, Lane.Top, 1000, { core: [count([1, 2, 3], 600, 240), count(b, 400, 240)] }));
      return file(rows);
    };
    const asked: unknown[][] = [];
    const plan = riotPlan(cores([1, 4, 2]), 1000, Lane.Top, (...args) => (asked.push(args), 4))!;
    expect(plan.core[0]!.ids).toEqual([1, 4, 2]);
    expect(asked).toContainEqual([1000, Lane.Top, 'core', [4], [3]]);
    // The same three items in another order: no counts by game state can tell them apart.
    expect(riotPlan(cores([1, 3, 2]), 1000, Lane.Top, () => 4)!.core[0]!.ids).toEqual([1, 2, 3]);
    expect(riotPlan(cores([1, 4, 2]), 1000, Lane.Top)!.core[0]!.ids).toEqual([1, 2, 3]);
  });

  it('keeps the most played option against a thin one a hair above the slot\'s rate (Jinx, 11.11.2026)', () => {
    // The slot spreads (κ finite), and Boots of Swiftness won 21 of 35 against Berserker's 6.165 of 11.994.
    const jinx = stats(222, Lane.Adc, 12761, { core, boots: [count([3006], 11994, 6165), count([3009], 35, 21), count([3013], 648, 333)] });
    const f = file([...spread('boots', [3047], [3111]), jinx]);
    expect(Number.isFinite(slotWeights(f).boots)).toBe(true);
    expect(riotPlan(f, 222, Lane.Adc, () => 4)!.boots[0]!.ids).toEqual([3006]);
    // Twelve wins in thirteen games against 103 in 233 are three standard errors on the raw rates
    // and one on the smoothed ones (the record of Cassiopeia's Flash and Barrier in the same file).
    const cass = stats(69, Lane.Top, 300, { core, starters: [count([1056, 2003], 233, 103), count([1082, 2003], 13, 12)] });
    const g = file([...spread('starters', [1055, 2003], [1054, 2003]).map((row) => ({ ...row, starters: [count([1055, 2003], 600, 282), count([1054, 2003], 400, 218)] })), cass]);
    expect(Number.isFinite(slotWeights(g).starters)).toBe(true);
    expect(riotPlan(g, 69, Lane.Top)!.starters[0]!.ids).toEqual([1056, 2003]);
  });

  it('counts how clearly one option is better on the smoothed rates', () => {
    const a = count([1], 400, 240);
    const b = count([2], 600, 240);
    // κ = 0 would be the plain two-proportion test; κ pseudo games pull both to the slot's 48 %.
    const kappa = 500;
    const estimate = (o: BuildCount) => (o.wins + kappa * 0.48) / (o.games + kappa);
    expect(clearance(a, b, 0.48, kappa)).toBeCloseTo((estimate(a) - estimate(b)) / Math.sqrt(0.48 * 0.52 * (1 / 900 + 1 / 1100)), 12);
    expect(clearance(a, b, 0.48, kappa)).toBeGreaterThan(LEAD_SIGMA);
    expect(clearance(b, a, 0.48, kappa)).toBeLessThan(0);
    // No spread in the slot: nothing is clear, whatever the records say.
    expect(clearance(a, b, 0.48, Number.POSITIVE_INFINITY)).toBe(0);
  });

  it('measures the spread against the noise around the slot\'s own rate', () => {
    // 53 % and 47 % over 200 games each: 3 points off a rate counted from the same 400 games. Noise
    // alone gives 2,5 points there (not 3,5, as against a rate known from elsewhere) — a spread.
    const rows = [...Array(120)].map((_, i) => stats(2000 + i, Lane.Top, 400, { core, boots: [count([3047], 200, 106), count([3111], 200, 94)] }));
    const kappa = slotWeights(file(rows)).boots;
    expect(kappa).toBeCloseTo(0.25 / (0.03 ** 2 - 0.25 * (1 / 200 - 1 / 400)) - 1, 6);
    // And none where the options sit exactly at the noise.
    const flat = [...Array(120)].map((_, i) => stats(3000 + i, Lane.Top, 400, { core, boots: [count([3047], 200, 105), count([3111], 200, 95)] }));
    expect(slotWeights(file(flat)).boots).toBe(Number.POSITIVE_INFINITY);
  });

  it('counts a spell pair and a rune page once, whatever their order', () => {
    const page = (a: number, b: number) => [8100, 8112, 8139, 8140, 8106, 8200, a, b, 5005, 5008, 5001];
    const plan = riotPlan(
      file([
        stats(103, Lane.Mid, 300, {
          core,
          spells: [count([4, 14], 70, 35), count([14, 4], 50, 25), count([4, 12], 100, 50)],
          runes: [count(page(8226, 8210), 60, 30), count(page(8210, 8226), 50, 25), count(page(8233, 8236), 80, 40)],
        }),
      ]),
      103,
      Lane.Mid,
    )!;
    expect(plan.spells!.ids).toEqual([4, 14]);
    expect(plan.spells!.games).toBe(120);
    // The most played spelling keeps its order for the client.
    expect(plan.runes!.ids).toEqual(page(8226, 8210));
    expect(plan.runes!.games).toBe(110);
  });

  it('never repeats a core item or an earlier slot in items four to six', () => {
    const plan = riotPlan(
      file([
        stats(157, Lane.Mid, 300, {
          core: [count([3153, 3031, 3036], 100, 50)],
          fourth: [count([3036], 60, 30), count([3026], 40, 20)],
          fifth: [count([3026], 50, 25), count([3072], 20, 10)],
          sixth: [count([3026], 9, 5), count([3072], 8, 4)],
        }),
      ]),
      157,
      Lane.Mid,
    )!;
    expect(plan.late.map((o) => o.ids[0])).toEqual([3026, 3072]);
  });

  it('offers the support upgrade with its alternatives', () => {
    const plan = riotPlan(
      file([stats(412, Lane.Support, 700, { core, supportChoice: [count([3876], 459, 238), count([3869], 264, 135), count([3877], 10, 6)] })]),
      412,
      Lane.Support,
    )!;
    expect(plan.supportChoice!.ids).toEqual([3876]);
    expect(plan.supportOptions.map((o) => o.ids[0])).toEqual([3876, 3869]);
  });

  it('drops thin alternatives but never the best option', () => {
    const plan = riotPlan(file([stats(122, Lane.Top, 10, { core: [count([1, 2, 3], 5, 4), count([4, 5, 6], 4, 1)] })]), 122, Lane.Top)!;
    expect(plan.core).toHaveLength(1);
  });

  it('shows the main lane when the asked lane has no games or too few', () => {
    const f = file([stats(122, Lane.Top, 900, { core }), stats(122, Lane.Mid, 40, { core }), stats(122, Lane.Jungle, 12, { core })]);
    const none = riotPlan(f, 122, Lane.Support)!;
    expect(none.lane).toBe(Lane.Top);
    expect(none.inPlaceOf).toBe(Lane.Support);
    const thin = riotPlan(f, 122, Lane.Jungle)!;
    expect(thin.lane).toBe(Lane.Top);
    expect(thin.inPlaceOf).toBe(Lane.Jungle);
    // 40 games with a core are a plan of their own.
    expect(riotPlan(f, 122, Lane.Mid)!.lane).toBe(Lane.Mid);
  });

  it('sums the brackets of one champion and lane', () => {
    const plan = riotPlan(
      file([
        stats(122, Lane.Top, 100, { skillOrder: [count([], 50, 30, 'QEWQQRQEQERE EWW')] }),
        { ...stats(122, Lane.Top, 100, { skillOrder: [count([], 50, 20, 'QEWQQRQEQERE EWW')] }), tier: 'gold' },
      ]),
      122,
      Lane.Top,
    )!;
    expect(plan.games).toBe(200);
    expect(plan.skillOrder).toBe('QEWQQRQEQERE EWW');
  });

  it('has no plan for a champion without any games', () => {
    expect(riotPlan(file([]), 122, Lane.Top)).toBeNull();
  });
});
