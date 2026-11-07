// DirkDraft's own data (source "riot"): complete duels, no OP.GG wording, builds by best win rate.

import { describe, expect, it } from 'vitest';
import { Lane } from '../../src/core/model/lane';
import { MetaLookup } from '../../src/core/model/metaLookup';
import { termHint } from '../../src/core/model/recommender';
import { riotPlan, type BuildCount, type ChampionBuildStats, type RiotBuildFile } from '../../src/core/riotBuilds';
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

describe('builds from own data', () => {
  it('takes the best win rate its games can vouch for', () => {
    const plan = riotPlan(
      file([
        stats(122, Lane.Top, 400, {
          boots: [count([3047], 13, 8), count([3111], 113, 55), count([3006], 60, 31)],
        }),
      ]),
      122,
      Lane.Top,
    )!;
    // 13 games at 62 % lose to 60 at 52 % and 113 at 49 % on the Wilson bound.
    expect(plan.boots.map((o) => o.ids[0])).toEqual([3111, 3006]);
  });

  it('drops thin alternatives but never the best option', () => {
    const plan = riotPlan(file([stats(122, Lane.Top, 10, { core: [count([1, 2, 3], 5, 4), count([4, 5, 6], 4, 1)] })]), 122, Lane.Top)!;
    expect(plan.core).toHaveLength(1);
  });

  it('shows the main lane when the asked lane has no games', () => {
    const plan = riotPlan(file([stats(122, Lane.Top, 900), stats(122, Lane.Mid, 40)]), 122, Lane.Jungle)!;
    expect(plan.lane).toBe(Lane.Top);
    expect(plan.inPlaceOf).toBe(Lane.Jungle);
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
