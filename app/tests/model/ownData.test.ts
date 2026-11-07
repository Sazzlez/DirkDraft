// DirkDraft's own data (source "riot"): complete duels, no OP.GG wording, builds by best win rate.

import { describe, expect, it } from 'vitest';
import { Lane } from '../../src/core/model/lane';
import { MetaLookup } from '../../src/core/model/metaLookup';
import { termHint } from '../../src/core/model/recommender';
import { riotPlan, slotWeights, type BuildCount, type ChampionBuildStats, type RiotBuildFile } from '../../src/core/riotBuilds';
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

  it('follows the rates once the file shows options truly differ', () => {
    // Sixty rows where option B wins 60 % over hundreds of games and A 40 %: a spread no noise explains.
    const rows = [...Array(60)].map((_, i) =>
      stats(1000 + i, Lane.Top, 1000, { core, boots: [count([3047], 600, 240), count([3111], 400, 240)] }),
    );
    const f = file(rows);
    expect(Number.isFinite(slotWeights(f).boots)).toBe(true);
    expect(riotPlan(f, 1000, Lane.Top)!.boots[0]!.ids).toEqual([3111]);
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
