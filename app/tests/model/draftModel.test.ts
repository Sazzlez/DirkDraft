// The measured draft model for our own data (core/model/draftModel.ts, docs/modellpruefung.md
// Messung 21): the parts are summed, pairs count by their kind, the line-up by its measured
// findings — and OP.GG files keep the 1.5 model.

import { describe, expect, it } from 'vitest';
import { estimateBalance } from '../../src/core/model/advisors';
import { CompAnalyzer, EMPTY_PROFILE, type CompProfile } from '../../src/core/model/compAnalyzer';
import {
  BOT_DUO_WEIGHT,
  botDuoEdge,
  COMP_WEIGHT,
  compValue,
  draftParts,
  duelEdge,
  VERSUS_WEIGHT,
  versusEdge,
  versusKind,
  type DraftSeat,
} from '../../src/core/model/draftModel';
import { draftStateFrom } from '../../src/core/model/draftState';
import { Lane } from '../../src/core/model/lane';
import { LanePredictionResult, LanePredictor } from '../../src/core/model/lanePredictor';
import { DamageType, MetaLookup } from '../../src/core/model/metaLookup';
import { Recommender } from '../../src/core/model/recommender';
import { logit, sigmoid } from '../../src/core/model/scoreModel';
import { MATCHUP_PRIOR, OWN_MATCHUP_PRIOR, shrink, SYNERGY_PRIOR } from '../../src/core/model/shrinkage';
import { TraitTable } from '../../src/core/model/traits';
import { MetaBuilder, SessionBuilder } from './helpers';

// Own side: Garen top, Jinx bot, Nami support. Enemies: Darius top, Caitlyn bot, Leona support.
const Garen = 1, Jinx = 2, Nami = 3, Darius = 4, Caitlyn = 5, Leona = 6, Lulu = 7;

function builder(): MetaBuilder {
  return new MetaBuilder()
    .champion(Garen, 'Garen')
    .inLane(Garen, Lane.Top, { winRate: 0.53, play: 20000 })
    .champion(Jinx, 'Jinx')
    .inLane(Jinx, Lane.Adc, { winRate: 0.52, play: 20000 })
    .champion(Nami, 'Nami')
    .inLane(Nami, Lane.Support, { winRate: 0.51, play: 20000 })
    .champion(Lulu, 'Lulu')
    .inLane(Lulu, Lane.Support, { winRate: 0.51, play: 20000 })
    .champion(Darius, 'Darius')
    .inLane(Darius, Lane.Top, { winRate: 0.5, play: 20000 })
    .champion(Caitlyn, 'Caitlyn')
    .inLane(Caitlyn, Lane.Adc, { winRate: 0.5, play: 20000 })
    .champion(Leona, 'Leona')
    .inLane(Leona, Lane.Support, { winRate: 0.5, play: 20000 });
}

/** The cast as our own count: duels complete, pairs across lanes counted. */
function own(edit: (b: MetaBuilder) => void = () => {}): MetaLookup {
  const b = builder();
  const snapshot = b.snapshotRef();
  snapshot.source = 'riot';
  snapshot.completeDuelSlope = 1;
  snapshot.completeDuelOffset = 0;
  snapshot.matchups.push({ championId: Garen, opponentId: Darius, lane: Lane.Top, winRate: 0.6, play: 700, fromCompleteList: true });
  snapshot.versus = [
    // Jinx against the enemy support, Nami against the enemy ADC (the botlane cross), Garen against their ADC.
    { championId: Jinx, lane: Lane.Adc, opponentId: Leona, opponentLane: Lane.Support, winRate: 0.6, play: 750 },
    { championId: Nami, lane: Lane.Support, opponentId: Caitlyn, opponentLane: Lane.Adc, winRate: 0.4, play: 750 },
    { championId: Garen, lane: Lane.Top, opponentId: Caitlyn, opponentLane: Lane.Adc, winRate: 0.6, play: 750 },
  ];
  b.synergy(Jinx, Nami, 0.6, { play: 750, lane: Lane.Adc, partnerLane: Lane.Support });
  b.synergy(Garen, Jinx, 0.6, { play: 750, lane: Lane.Top, partnerLane: Lane.Adc });
  edit(b);
  return b.build();
}

const seat = (championId: number, lane: Lane): DraftSeat => ({ championId, lane });
const allies = [seat(Garen, Lane.Top), seat(Jinx, Lane.Adc), seat(Nami, Lane.Support)];
const enemies = [seat(Darius, Lane.Top), seat(Caitlyn, Lane.Adc), seat(Leona, Lane.Support)];

describe('our own data as the model reads it', () => {
  it('shrinks a duel with the weight measured for its lane; OP.GG files keep one weight', () => {
    const meta = own();
    expect(ALL_PRIORS(meta)).toEqual([...OWN_MATCHUP_PRIOR]);
    // 60 % over 700 games on Top: exactly half way to what both lane strengths predict (κ = 700).
    const expected = meta.matchupTarget(Garen, Darius, Lane.Top);
    expect(meta.matchup(Garen, Darius, Lane.Top)!.winRate).toBeCloseTo(shrink(0.6, 700, 700, expected), 12);
    expect(meta.matchup(Garen, Darius, Lane.Top)!.winRate).toBeCloseTo((0.6 + expected) / 2, 12);

    const opgg = builder().matchup(Garen, Darius, Lane.Top, 0.6, 700).build();
    expect(ALL_PRIORS(opgg)).toEqual([MATCHUP_PRIOR, MATCHUP_PRIOR, MATCHUP_PRIOR, MATCHUP_PRIOR, MATCHUP_PRIOR]);
  });

  it('reads a pair across lanes shrunk like a duo towards both lane strengths, and its mirror as the complement', () => {
    const meta = own();
    const view = meta.versus(Jinx, Lane.Adc, Leona, Lane.Support)!;
    const expected = sigmoid(logit(meta.laneStat(Jinx, Lane.Adc)!.winRate) - logit(meta.laneStat(Leona, Lane.Support)!.winRate));
    expect(view.expected).toBeCloseTo(expected, 12);
    expect(view.winRate).toBeCloseTo(shrink(0.6, 750, SYNERGY_PRIOR, expected), 12);
    expect(view.measured).toBe(0.6);
    const mirror = meta.versus(Leona, Lane.Support, Jinx, Lane.Adc)!;
    expect(mirror.winRate + view.winRate).toBeCloseTo(1, 12);
    expect(mirror.expected + view.expected).toBeCloseTo(1, 12);
    // No row, the same lane, or an OP.GG file: nothing.
    expect(meta.versus(Jinx, Lane.Adc, Darius, Lane.Top)).toBeNull();
    expect(meta.versus(Garen, Lane.Top, Darius, Lane.Top)).toBeNull();
    expect(builder().build().versus(Jinx, Lane.Adc, Leona, Lane.Support)).toBeNull();
  });
});

const ALL_PRIORS = (meta: MetaLookup) => [Lane.Top, Lane.Jungle, Lane.Mid, Lane.Adc, Lane.Support].map((lane) => meta.matchupPrior(lane));

describe('the parts of the draft', () => {
  it('sorts pairs across lanes into their kinds, each with its measured weight', () => {
    expect(versusKind(Lane.Adc, Lane.Support)).toBe('botCross');
    expect(versusKind(Lane.Support, Lane.Adc)).toBe('botCross');
    expect(versusKind(Lane.Jungle, Lane.Mid)).toBe('jungle');
    expect(versusKind(Lane.Top, Lane.Jungle)).toBe('jungle');
    expect(versusKind(Lane.Top, Lane.Adc)).toBe('other');
    // The botlane cross weighs most; a jungler against a laner is not told from nothing yet.
    expect(VERSUS_WEIGHT.botCross).toBeGreaterThan(VERSUS_WEIGHT.other);
    expect(VERSUS_WEIGHT.jungle).toBe(0);

    const meta = own();
    const raw = (a: DraftSeat, b: DraftSeat) => {
      const view = meta.versus(a.championId, a.lane, b.championId, b.lane)!;
      return logit(view.winRate) - logit(view.expected);
    };
    expect(versusEdge(meta, allies[1]!, enemies[2]!)).toBeCloseTo(VERSUS_WEIGHT.botCross * raw(allies[1]!, enemies[2]!), 12);
    expect(versusEdge(meta, allies[0]!, enemies[1]!)).toBeCloseTo(VERSUS_WEIGHT.other * raw(allies[0]!, enemies[1]!), 12);
    expect(versusEdge(meta, allies[0]!, enemies[0]!)).toBeNull(); // the same lane is the duel
  });

  it('counts a duo only on the botlane', () => {
    const meta = own();
    const view = meta.synergy(Jinx, Nami)!;
    expect(botDuoEdge(meta, allies[1]!, allies[2]!)).toBeCloseTo(BOT_DUO_WEIGHT * (logit(view.winRate) - logit(view.expected)), 12);
    expect(botDuoEdge(meta, allies[2]!, allies[1]!)).toBeCloseTo(botDuoEdge(meta, allies[1]!, allies[2]!)!, 12);
    // Garen with Jinx has a row and a rate of 60 % too — and counts nothing.
    expect(meta.synergy(Garen, Jinx)).not.toBeNull();
    expect(botDuoEdge(meta, allies[0]!, allies[1]!)).toBeNull();
  });

  it('sums the parts instead of averaging them', () => {
    const meta = own();
    const parts = draftParts(meta, allies, enemies);
    const strength = (s: DraftSeat) => logit(meta.laneStat(s.championId, s.lane)!.winRate);
    expect(parts.strength).toBeCloseTo(allies.reduce((n, s) => n + strength(s), 0) - enemies.reduce((n, s) => n + strength(s), 0), 12);
    expect(parts.duels).toBeCloseTo(duelEdge(meta, Garen, Darius, Lane.Top)!, 12);
    expect(parts.duo).toBeCloseTo(botDuoEdge(meta, allies[1]!, allies[2]!)!, 12);
    expect(parts.versus).toBeCloseTo(
      versusEdge(meta, allies[1]!, enemies[2]!)! + versusEdge(meta, allies[2]!, enemies[1]!)! + versusEdge(meta, allies[0]!, enemies[1]!)!,
      12,
    );
    expect(parts.total).toBeCloseTo(parts.strength + parts.duels + parts.duo + parts.versus + parts.comp, 12);
    expect(parts).toMatchObject({ rated: 6, contested: 1, comparable: true, comp: 0 });
    // The other side sees the same draft with every sign turned.
    expect(draftParts(meta, enemies, allies).total).toBeCloseTo(-parts.total, 12);
  });

  it('counts an open seat as an average champion', () => {
    const meta = own();
    const two = draftParts(meta, allies.slice(0, 2), enemies);
    const strength = (s: DraftSeat) => logit(meta.laneStat(s.championId, s.lane)!.winRate);
    expect(two.strength).toBeCloseTo(strength(allies[0]!) + strength(allies[1]!) - enemies.reduce((n, s) => n + strength(s), 0), 12);
    expect(two.rated).toBe(5);
    expect(draftParts(meta, [], enemies).comparable).toBe(false);
  });

  it('values a line-up by its measured findings, by the share of the team that is fixed', () => {
    const lacking = (count: number, ...issues: CompProfile['findings'][number]['issue'][]): CompProfile => ({
      ...EMPTY_PROFILE,
      count,
      findings: issues.map((issue) => ({ issue, text: '', severity: 1 })),
    });
    expect(compValue(EMPTY_PROFILE)).toBe(0);
    expect(compValue(lacking(5, 'noMagicDamage'))).toBeCloseTo(-COMP_WEIGHT.noMagicDamage!, 12);
    expect(compValue(lacking(5, 'noMagicDamage', 'noEngage'))).toBeCloseTo(-(COMP_WEIGHT.noMagicDamage! + COMP_WEIGHT.noEngage!), 12);
    // Four of five fixed: the last pick can still close it.
    expect(compValue(lacking(4, 'noMagicDamage'))).toBeCloseTo(-0.8 * COMP_WEIGHT.noMagicDamage!, 12);
    // Findings without a measured effect cost nothing.
    expect(compValue(lacking(5, 'mixedTiming', 'noPeel', 'allMelee'))).toBe(0);
    const meta = own();
    expect(draftParts(meta, allies, enemies, { allies: lacking(5, 'noMagicDamage'), enemies: lacking(5, 'noEngage') }).comp).toBeCloseTo(
      COMP_WEIGHT.noEngage! - COMP_WEIGHT.noMagicDamage!,
      12,
    );
  });
});

/** Garen top (cell 0) and Jinx bot (3) are locked, the support seat (4) is the own one; three enemies are shown. */
const board = () =>
  new SessionBuilder()
    .localPlayer(4)
    .locked(0, Garen)
    .completedPick(0)
    .locked(3, Jinx)
    .completedPick(3)
    .locked(5, Darius)
    .completedPick(5)
    .locked(6, Caitlyn)
    .completedPick(6)
    .locked(7, Leona)
    .completedPick(7);

describe('the draft chance on our own data', () => {
  const session = () => board().locked(4, Nami).completedPick(4);

  it('is the measured model, and says what it is made of', () => {
    const meta = own();
    const state = draftStateFrom(session().build());
    const predictor = new LanePredictor(meta);
    const allyLanes = predictor.predict(state.allies);
    const enemyLanes = predictor.predict(state.enemies);
    const balance = estimateBalance(meta, state, allyLanes, enemyLanes);
    const seats = (r: LanePredictionResult) => r.predictions.filter((p) => p.championId !== 0).map((p) => ({ championId: p.championId, lane: p.lane }));
    const parts = draftParts(meta, seats(allyLanes), seats(enemyLanes));
    expect(balance.parts).toEqual(parts);
    expect(balance.allyWinRate).toBeCloseTo(sigmoid(parts.total), 12);
    expect(balance).toMatchObject({ ratedChampions: 6, contestedLanes: 1, isComparable: true });
  });

  it('stays the 1.5 estimate on an OP.GG file: averaged, without parts', () => {
    const meta = builder().matchup(Garen, Darius, Lane.Top, 0.6, 700).build();
    const state = draftStateFrom(session().build());
    const predictor = new LanePredictor(meta);
    const balance = estimateBalance(meta, state, predictor.predict(state.allies), predictor.predict(state.enemies));
    expect(balance.parts).toBeUndefined();
    const strength = (id: number, lane: Lane) => logit(meta.laneStat(id, lane)!.winRate);
    const mean = (strength(Garen, Lane.Top) + strength(Jinx, Lane.Adc) + strength(Nami, Lane.Support)) / 3 - (strength(Darius, Lane.Top) + strength(Caitlyn, Lane.Adc) + strength(Leona, Lane.Support)) / 3;
    const duel = logit(meta.matchup(Garen, Darius, Lane.Top)!.winRate) - meta.matchupBaseline(Garen, Darius, Lane.Top);
    expect(balance.allyWinRate).toBeCloseTo(sigmoid(mean + duel), 12);
  });
});

describe('a pick on our own data', () => {
  // The support seat picks: Garen and Jinx are locked, all three enemies shown.
  const session = () => board().onClock(4, 'pick');

  function score(meta: MetaLookup, championId: number) {
    const state = draftStateFrom(session().build());
    const predictor = new LanePredictor(meta);
    const allyLanes = predictor.predict(state.allies);
    const enemyLanes = predictor.predict(state.enemies);
    const recommender = new Recommender(meta, TraitTable.empty);
    const target = { slot: state.allies.find((slot) => slot.cellId === 4)!, action: 'pick' as const, isFollowingTurn: true };
    const set = recommender.recommend(state, target, enemyLanes, allyLanes, null, 50);
    const item = set.items.find((i) => i.championId === championId)!;
    const term = (kind: string) => item.breakdown.find((t) => t.kind === kind)!.logOdds;
    return { item, term, state, allyLanes, enemyLanes };
  }

  it('adds what the champion adds to the team: its strength, its botlane duo, its pairs with the enemies', () => {
    const meta = own();
    const { item, term } = score(meta, Nami);
    expect(term('laneStrength')).toBeCloseTo(logit(meta.laneStat(Nami, Lane.Support)!.winRate), 12);
    // The duo with Jinx, at its measured weight — Garen's row with Jinx plays no part.
    expect(term('synergy')).toBeCloseTo(botDuoEdge(meta, seat(Nami, Lane.Support), seat(Jinx, Lane.Adc))!, 9);
    // Against their ADC across the botlane; the lane opponent Leona is the duel, not this term. Weighed by how
    // sure Caitlyn's lane is (a sliver of doubt stays on every prediction), hence the tolerance.
    expect(term('enemyTeam')).toBeCloseTo(versusEdge(meta, seat(Nami, Lane.Support), seat(Caitlyn, Lane.Adc))!, 2);
    expect(item.reasons.map((r) => r.text)).toContain('starkes Botlane-Duo mit Jinx');
    expect(item.reasons.map((r) => r.text)).toContain('schwer gegen Caitlyn (Botlane)');
  });

  it('says nothing about a duo or enemy pairs where the file has no row', () => {
    const { term } = score(own(), Lulu);
    expect(term('synergy')).toBeNull();
    expect(term('enemyTeam')).toBeNull();
  });

  it('moves the team chance by its terms, up to how sure the enemy lanes are', () => {
    const meta = own();
    const { item, state, allyLanes, enemyLanes } = score(meta, Nami);
    const seats = (r: LanePredictionResult) => r.predictions.filter((p) => p.championId !== 0).map((p) => ({ championId: p.championId, lane: p.lane }));
    const before = draftParts(meta, seats(allyLanes), seats(enemyLanes));
    const after = draftParts(meta, [...seats(allyLanes), seat(Nami, Lane.Support)], seats(enemyLanes));
    const total = item.breakdown.reduce((n, t) => n + (t.logOdds ?? 0), 0);
    expect(state.allies.find((slot) => slot.cellId === 4)!.lockedChampionId).toBe(0);
    expect(after.total - before.total).toBeCloseTo(total, 2);
  });
});

describe('the line-up on our own data', () => {
  // Four physical champions are fixed; the last seat brings magic damage or one more of the same.
  const AD = [11, 12, 13, 14];
  const Mage = 20, Fighter = 21;
  function meta(source: 'riot' | null): MetaLookup {
    const b = new MetaBuilder();
    // One of them stands in front, so the missing damage type is the only finding.
    for (const id of AD) b.champion(id, `AD${id}`, { damage: DamageType.Physical, attackRange: 550, tags: id === AD[0] ? ['Tank'] : [] });
    b.champion(Mage, 'Mage', { damage: DamageType.Magic, attackRange: 550 }).champion(Fighter, 'Fighter', { damage: DamageType.Physical, attackRange: 550 });
    if (source) b.snapshotRef().source = source;
    return b.build();
  }

  it('gives a pick that closes a measured gap what the gap costs, and takes a little from one that keeps it', () => {
    const own = meta('riot');
    const analyzer = new CompAnalyzer(own, TraitTable.empty);
    const team = analyzer.analyze(AD);
    expect(team.findings.map((f) => f.issue)).toContain('noMagicDamage');
    const closes = compValue(analyzer.analyze([...AD, Mage])) - compValue(team);
    const keeps = compValue(analyzer.analyze([...AD, Fighter])) - compValue(team);
    expect(closes).toBeCloseTo(0.8 * COMP_WEIGHT.noMagicDamage!, 12);
    expect(keeps).toBeCloseTo(-0.2 * COMP_WEIGHT.noMagicDamage!, 12);
    // Last pick: the difference between closing and keeping is exactly the measured cost.
    expect(closes - keeps).toBeCloseTo(COMP_WEIGHT.noMagicDamage!, 12);
  });

  it('leaves the measured findings out of the rule score, and keeps their reasons', () => {
    const own = meta('riot');
    const analyzer = new CompAnalyzer(own, TraitTable.empty);
    const team = analyzer.analyze(AD);
    const reasons: { text: string }[] = [];
    // Under the 1.5 rules the gap is a score; on our own data the model counts it instead.
    expect(analyzer.fit(Mage, team, EMPTY_PROFILE, [], Lane.Mid, false)).toBeGreaterThan(0);
    expect(analyzer.fit(Mage, team, EMPTY_PROFILE, reasons as never, Lane.Mid, true)).toBe(0);
    expect(reasons.map((r) => r.text)).toContain('bringt fehlenden AP-Schaden');
    expect(analyzer.fit(Fighter, team, EMPTY_PROFILE, [], Lane.Mid, false)).toBeLessThan(0);
    expect(analyzer.fit(Fighter, team, EMPTY_PROFILE, [], Lane.Mid, true)).toBe(0);
  });
});
