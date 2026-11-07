// Port of tests/DraftPilot.Core.Tests/RecommenderTests.cs.

import { describe, expect, it } from 'vitest';
import { draftStateFrom, INACTIVE, type DraftState } from '../../src/core/model/draftState';
import { Lane } from '../../src/core/model/lane';
import { LanePredictionResult, LanePredictor } from '../../src/core/model/lanePredictor';
import { DamageType, MetaLookup } from '../../src/core/model/metaLookup';
import { neutral, pro } from '../../src/core/model/reason';
import {
  Recommender,
  termHasData,
  termHint,
  termLabel,
  termPoints,
  type Recommendation,
  type ScoreTerm,
  type ScoreTermKind,
} from '../../src/core/model/recommender';
import { BAN_LANE_FOCUS, clamp, LANE_DRIFT_PER_PATCH, logit, OFF_LANE_SHARE, sigmoid } from '../../src/core/model/scoreModel';
import { LANE_PRIOR, MATCHUP_PRIOR, shrink, SYNERGY_PRIOR } from '../../src/core/model/shrinkage';
import { MINIMUM_ROWS } from '../../src/core/model/synergyLine';
import { TraitTable } from '../../src/core/model/traits';
import { TurnTracker, type RecommendationTarget } from '../../src/core/model/turnTracker';
import { MetaBuilder, SessionBuilder, slot } from './helpers';

// Mid candidates, deliberately ordered by strength.
const Strong = 101,
  Average = 102,
  Weak = 103,
  CounterPick = 104;

// Other seats.
const AllyTop = 201,
  AllySupport = 202;
const EnemyMid = 301,
  EnemyTop = 302;

/** Strong, unpicked and bad news for our top laner: the archetypal ban target. */
const Menace = 303;

const JungleOnly = 401,
  SupportOnly = 402,
  EnemyJungler = 403;

const ALL_TERM_KINDS: ScoreTermKind[] = [
  'laneStrength',
  'laneMatchup',
  'enemyTeam',
  'synergy',
  'composition',
  'banStrength',
  'banLaneThreat',
  'banTeamThreat',
  'banPopularity',
];

/** Enumerable.Single: exactly one match, or the test fails. */
function single<T>(items: readonly T[], predicate: (item: T) => boolean): T {
  const matches = items.filter(predicate);
  expect(matches).toHaveLength(1);
  return matches[0]!;
}

const termOf = (item: Recommendation, kind: ScoreTermKind): ScoreTerm => single(item.breakdown, (term) => term.kind === kind);

const meta = (): MetaLookup =>
  new MetaBuilder()
    .champion(Strong, 'Strong', { damage: DamageType.Magic, tags: ['Mage'], attackRange: 550, defense: 3 })
    .champion(Average, 'Average', { damage: DamageType.Magic, tags: ['Mage'], attackRange: 525, defense: 3 })
    .champion(Weak, 'Weak', { damage: DamageType.Magic, tags: ['Mage'], attackRange: 500, defense: 2 })
    .champion(CounterPick, 'CounterPick', { damage: DamageType.Physical, tags: ['Assassin'], attackRange: 125, defense: 3 })
    .champion(AllyTop, 'AllyTop', { damage: DamageType.Physical, tags: ['Fighter'], attackRange: 175, defense: 5 })
    .champion(AllySupport, 'AllySupport', { damage: DamageType.Magic, tags: ['Tank'], attackRange: 125, defense: 6 })
    .champion(EnemyMid, 'EnemyMid', { damage: DamageType.Magic, tags: ['Mage'], attackRange: 550, defense: 3 })
    .champion(EnemyTop, 'EnemyTop', { damage: DamageType.Physical, tags: ['Fighter'], attackRange: 175, defense: 5 })
    .champion(Menace, 'Menace', { damage: DamageType.Physical, tags: ['Fighter'], attackRange: 175, defense: 5 })
    .inLane(Strong, Lane.Mid, { winRate: 0.54, tier: 1, play: 2000, pickRate: 0.08, banRate: 0.1 })
    .inLane(Average, Lane.Mid, { winRate: 0.5, tier: 3, play: 2000, pickRate: 0.04 })
    .inLane(Weak, Lane.Mid, { winRate: 0.46, tier: 5, play: 2000, pickRate: 0.01 })
    .inLane(CounterPick, Lane.Mid, { winRate: 0.5, tier: 3, play: 2000, pickRate: 0.03 })
    .inLane(AllyTop, Lane.Top, { winRate: 0.51, tier: 2, play: 2000 })
    .inLane(AllySupport, Lane.Support, { winRate: 0.51, tier: 2, play: 2000 })
    .inLane(EnemyMid, Lane.Mid, { winRate: 0.5, tier: 3, play: 2000 })
    .inLane(EnemyTop, Lane.Top, { winRate: 0.52, tier: 1, play: 2000, pickRate: 0.09, banRate: 0.12 })
    .inLane(Menace, Lane.Top, { winRate: 0.55, tier: 1, play: 2000, pickRate: 0.11, banRate: 0.15 })
    // CounterPick beats the enemy mid decisively; nobody else has matchup data.
    .matchup(CounterPick, EnemyMid, Lane.Mid, 0.62, 3000)
    // The enemy top laner is a menace to our top laner: relevant for bans.
    .matchup(Menace, AllyTop, Lane.Top, 0.6, 3000)
    .synergy(Strong, AllySupport, 0.56, { play: 2000, tier: 0 })
    .build();

const recommender = (): Recommender => new Recommender(meta(), TraitTable.empty);

/** A draft where the local mid laner is on the clock. */
function midPickState(...bans: number[]): DraftState {
  const builder = new SessionBuilder()
    .localPlayer(2)
    .locked(0, AllyTop)
    .locked(4, AllySupport)
    .locked(7, EnemyMid)
    .locked(5, EnemyTop)
    .onClock(2, 'pick');

  for (const ban of bans) builder.completedBan(1, ban);

  return draftStateFrom(builder.build());
}

function scenario(...bans: number[]): { state: DraftState; target: RecommendationTarget; lanes: LanePredictionResult } {
  const state = midPickState(...bans);
  const target = new TurnTracker().resolve(state)!;
  const lanes = new LanePredictor(meta()).predict(state.enemies);
  return { state, target, lanes };
}

const score = (items: readonly Recommendation[], championId: number): number =>
  single(items, (item) => item.championId === championId).score;

const term = (items: readonly Recommendation[], championId: number, kind: ScoreTermKind): number | null =>
  termOf(
    single(items, (item) => item.championId === championId),
    kind,
  ).logOdds;

/** The EnemyTeam term of the one candidate that has matchup data, in log-odds. */
function offLaneTerm(m: MetaLookup): number {
  const state = draftStateFrom(new SessionBuilder().localPlayer(2).locked(5, EnemyTop).onClock(2, 'pick').build());
  const target = new TurnTracker().resolve(state)!;
  const lanes = new LanePredictor(m).predict(state.enemies);
  return term(new Recommender(m, TraitTable.empty).recommend(state, target, lanes).items, Strong, 'enemyTeam') ?? 0;
}

/** The LaneMatchup term of the one candidate that has matchup data, in log-odds. */
function duelTerm(m: MetaLookup): number {
  const state = draftStateFrom(new SessionBuilder().localPlayer(2).locked(7, EnemyMid).onClock(2, 'pick').build());
  const target = new TurnTracker().resolve(state)!;
  const lanes = new LanePredictor(m).predict(state.enemies);
  return term(new Recommender(m, TraitTable.empty).recommend(state, target, lanes).items, Strong, 'laneMatchup') ?? 0;
}

/** The Synergy term of the one candidate with a duo row, in log-odds. */
function synergyTerm(m: MetaLookup): number {
  const state = draftStateFrom(new SessionBuilder().localPlayer(2).locked(4, AllySupport).onClock(2, 'pick').build());
  const target = new TurnTracker().resolve(state)!;
  return term(new Recommender(m, TraitTable.empty).recommend(state, target, LanePredictionResult.empty, null, null, 50).items, Strong, 'synergy') ?? 0;
}

const banMeta = (): MetaLookup =>
  new MetaBuilder()
    .champion(JungleOnly, 'JungleOnly', { damage: DamageType.Physical, tags: ['Fighter'], attackRange: 175, defense: 5 })
    .champion(SupportOnly, 'SupportOnly', { damage: DamageType.Magic, tags: ['Tank'], attackRange: 125, defense: 6 })
    .champion(EnemyJungler, 'EnemyJungler', { damage: DamageType.Physical, tags: ['Fighter'], attackRange: 175, defense: 5 })
    .inLane(JungleOnly, Lane.Jungle, { roleRate: 0.98, winRate: 0.54, play: 20_000, tier: 1, pickRate: 0.1 })
    .inLane(SupportOnly, Lane.Support, { roleRate: 0.98, winRate: 0.54, play: 20_000, tier: 1, pickRate: 0.1 })
    .inLane(EnemyJungler, Lane.Jungle, { roleRate: 0.98, winRate: 0.5, play: 20_000 })
    .build();

function banItem(m: MetaLookup, candidate: number, enemies: number[] | null = null): Recommendation {
  const builder = new SessionBuilder().localPlayer(2).onClock(2, 'ban');

  let cell = 5;
  for (const enemy of enemies ?? []) builder.locked(cell++, enemy);

  const state = draftStateFrom(builder.build());
  const target = new TurnTracker().resolve(state)!;
  const lanes = new LanePredictor(m).predict(state.enemies);

  return single(new Recommender(m, TraitTable.empty).recommend(state, target, lanes, null, null, 50).items, (item) => item.championId === candidate);
}

/** The ban score of one candidate, with the given enemies locked in. */
const banScore = (m: MetaLookup, candidate: number, enemies: number[]): number => banItem(m, candidate, enemies).score;

/** The mid seat on the clock, hovering `hover`. */
function hovering(hover: number, turn = 'pick'): { state: DraftState; target: RecommendationTarget; lanes: LanePredictionResult } {
  const state = draftStateFrom(
    new SessionBuilder()
      .localPlayer(2)
      .locked(0, AllyTop)
      .locked(4, AllySupport)
      .locked(7, EnemyMid)
      .locked(5, EnemyTop)
      .hovering(2, hover)
      .onClock(2, turn)
      .build(),
  );
  return { state, target: new TurnTracker().resolve(state)!, lanes: new LanePredictor(meta()).predict(state.enemies) };
}

describe('RecommenderTests', () => {
  it('EmptyMeta_ProducesNothing', () => {
    const state = midPickState();
    const target = new TurnTracker().resolve(state)!;
    const rec = new Recommender(MetaLookup.empty(), TraitTable.empty);

    const set = rec.recommend(state, target, LanePredictionResult.empty);

    expect(set.items).toHaveLength(0);
  });

  it('TargetLane_ComesFromTheSeat', () => {
    const { state, target, lanes } = scenario();

    const set = recommender().recommend(state, target, lanes);

    expect(set.lane).toBe(Lane.Mid);
    expect(set.cellId).toBe(2);
    expect(set.action).toBe('pick');
  });

  it('StrongerChampionRanksHigher', () => {
    const { state, target, lanes } = scenario();

    const items = recommender().recommend(state, target, lanes).items;

    const strong = single(items, (item) => item.championId === Strong);
    const weak = single(items, (item) => item.championId === Weak);

    expect(strong.score > weak.score).toBe(true);
  });

  /** The score is an estimated win rate: strictly a probability, never outside (0, 1). */
  it('PickScores_AreProbabilities', () => {
    const { state, target, lanes } = scenario();

    const items = recommender().recommend(state, target, lanes, null, null, 50).items;

    expect(items.length).toBeGreaterThan(0);
    for (const item of items) {
      expect(item.score).toBeGreaterThanOrEqual(0.001);
      expect(item.score).toBeLessThanOrEqual(0.999);
    }
  });

  /** The pill's percentage must be exactly what the breakdown's terms add up to. */
  it('PickScore_IsTheSigmoidOfItsBreakdown', () => {
    const { state, target, lanes } = scenario();

    for (const item of recommender().recommend(state, target, lanes).items) {
      const total = item.breakdown.reduce((sum, t) => sum + (t.logOdds ?? 0), 0);
      expect(item.score).toBeCloseTo(sigmoid(total), 9);
    }
  });

  /** A favourable matchup must lift the pick above an otherwise identical champion. */
  it('FavourableMatchup_RaisesTheScore', () => {
    const { state, target, lanes } = scenario();

    const items = recommender().recommend(state, target, lanes).items;

    // CounterPick and Average share the same lane stats; only the matchup differs.
    expect(score(items, CounterPick) > score(items, Average)).toBe(true);
  });

  /**
   * The same 62 % matchup over 3000 games must move the score more than over 30: shrinkage
   * weighs the evidence — but only once, not squared as the old confidence factor did.
   */
  it('LargerSample_MovesTheScoreMore', () => {
    const matchupPoints = (play: number): number => {
      const m = new MetaBuilder()
        .champion(CounterPick, 'CounterPick', { damage: DamageType.Physical, tags: ['Assassin'], attackRange: 125, defense: 3 })
        .champion(EnemyMid, 'EnemyMid', { damage: DamageType.Magic, tags: ['Mage'], attackRange: 550, defense: 3 })
        .inLane(CounterPick, Lane.Mid, { winRate: 0.5, tier: 3, play: 2000 })
        .inLane(EnemyMid, Lane.Mid, { winRate: 0.5, tier: 3, play: 2000 })
        .matchup(CounterPick, EnemyMid, Lane.Mid, 0.62, play)
        .build();

      const state = draftStateFrom(new SessionBuilder().localPlayer(2).locked(7, EnemyMid).onClock(2, 'pick').build());

      const target = new TurnTracker().resolve(state)!;
      const predictions = new LanePredictor(m).predict(state.enemies);
      const item = single(
        new Recommender(m, TraitTable.empty).recommend(state, target, predictions).items,
        (i) => i.championId === CounterPick,
      );

      return termPoints(termOf(item, 'laneMatchup'));
    };

    const thin = matchupPoints(30);
    const thick = matchupPoints(3000);

    expect(thin > 0, 'Auch 30 Spiele sind Evidenz und müssen etwas zählen.').toBe(true);
    expect(thick > thin * 2, `3000 Spiele müssen deutlich mehr bewegen: dünn=${thin.toFixed(2)}, dick=${thick.toFixed(2)}`).toBe(true);
  });

  /** A duo at exactly 50 % over any sample is no evidence and must shift nothing. */
  it('EvenDuo_ChangesNothing', () => {
    const m = new MetaBuilder()
      .champion(Strong, 'Strong', { damage: DamageType.Magic, tags: ['Mage'], attackRange: 550, defense: 3 })
      .champion(AllySupport, 'AllySupport', { damage: DamageType.Magic, tags: ['Tank'], attackRange: 125, defense: 6 })
      .inLane(Strong, Lane.Mid, { winRate: 0.52, tier: 2, play: 2000 })
      .inLane(AllySupport, Lane.Support, { winRate: 0.51, tier: 2, play: 2000 })
      .synergy(Strong, AllySupport, 0.5, { play: 2000, tier: 2 })
      .build();

    const state = draftStateFrom(new SessionBuilder().localPlayer(2).locked(4, AllySupport).onClock(2, 'pick').build());

    const target = new TurnTracker().resolve(state)!;
    const item = single(
      new Recommender(m, TraitTable.empty).recommend(state, target, LanePredictionResult.empty).items,
      (i) => i.championId === Strong,
    );

    const synergy = termOf(item, 'synergy');

    expect(termHasData(synergy)).toBe(true);
    expect(termPoints(synergy)).toBeCloseTo(0, 6);
  });

  /**
   * A flex enemy that could be Mid or Top must count on both lanes by probability, not fully
   * on whichever the predictor happens to rank first.
   */
  it('FlexEnemy_SplitsItsMatchupWeightAcrossLanes', () => {
    const Flex = 401,
      Candidate = 402;

    const m = new MetaBuilder()
      .champion(Flex, 'Flex', { damage: DamageType.Magic, tags: ['Mage'], attackRange: 550, defense: 3 })
      .champion(Candidate, 'Candidate', { damage: DamageType.Magic, tags: ['Mage'], attackRange: 550, defense: 3 })
      // Genuinely ambiguous: identical presence on both lanes.
      .inLane(Flex, Lane.Mid, { winRate: 0.51, tier: 2, play: 2000, roleRate: 0.5 })
      .inLane(Flex, Lane.Top, { winRate: 0.51, tier: 2, play: 2000, roleRate: 0.5 })
      .inLane(Candidate, Lane.Mid, { winRate: 0.5, tier: 3, play: 2000 })
      .matchup(Candidate, Flex, Lane.Mid, 0.62, 3000)
      .build();

    const session = new SessionBuilder().localPlayer(2).locked(7, Flex).onClock(2, 'pick').build();

    // The local seat needs a lane; the builder leaves assignments empty, so fix it manually.
    const state = draftStateFrom(session);
    const target: RecommendationTarget = { slot: state.allies.find((s) => s.cellId === 2)!, action: 'pick', isFollowingTurn: true };
    const rec = new Recommender(m, TraitTable.empty);

    const laneTerm = (lanes: LanePredictionResult): number => {
      const set = rec.recommend(state, target, lanes);
      const item = single(set.items, (i) => i.championId === Candidate);
      return termOf(item, 'laneMatchup').logOdds ?? 0;
    };

    const predictor = new LanePredictor(m);
    const uncertain = laneTerm(predictor.predict(state.enemies));
    const pinnedMid = laneTerm(predictor.predict(state.enemies, new Map([[7, Lane.Mid]])));

    expect(pinnedMid > 0, 'Mit fixiertem Mid-Gegner muss der Matchup-Term positiv sein.').toBe(true);
    expect(uncertain > 0, 'Auch der unsichere Fall muss anteilig zählen.').toBe(true);
    expect(
      uncertain < pinnedMid * 0.75,
      `Der 50/50-Flex darf nicht voll zählen: unsicher=${uncertain.toFixed(3)}, fixiert=${pinnedMid.toFixed(3)}`,
    ).toBe(true);
  });

  /** The seat lane comes from the client here, so this must behave exactly like before. */
  it('CertainOpponent_CountsFully', () => {
    const { state, target, lanes } = scenario();

    const counter = single(recommender().recommend(state, target, lanes).items, (item) => item.championId === CounterPick);

    const laneTerm = termOf(counter, 'laneMatchup').logOdds ?? 0;
    expect(laneTerm > 0.2, `Sicherer Gegner muss voll zählen, war ${laneTerm.toFixed(3)}`).toBe(true);
  });

  it('MatchupReason_NamesTheOpponent', () => {
    const { state, target, lanes } = scenario();

    const counter = single(recommender().recommend(state, target, lanes).items, (item) => item.championId === CounterPick);

    expect(counter.reasons.some((reason) => reason.text.includes('EnemyMid'))).toBe(true);
  });

  it('SynergyReason_NamesTheAlly', () => {
    const { state, target, lanes } = scenario();

    const strong = single(recommender().recommend(state, target, lanes).items, (item) => item.championId === Strong);

    expect(strong.reasons.some((reason) => reason.text.includes('AllySupport'))).toBe(true);
  });

  it('BannedChampions_AreNeverRecommended', () => {
    const { state, target, lanes } = scenario(Strong);

    const items = recommender().recommend(state, target, lanes).items;

    expect(items.some((item) => item.championId === Strong)).toBe(false);
  });

  it('AlreadyPickedChampions_AreNeverRecommended', () => {
    const { state, target, lanes } = scenario();

    const items = recommender().recommend(state, target, lanes).items;

    expect(items.some((item) => item.championId === EnemyMid)).toBe(false);
    expect(items.some((item) => item.championId === AllyTop)).toBe(false);
  });

  it('SelectableFilter_RestrictsTheList', () => {
    const { state, target, lanes } = scenario();

    const items = recommender().recommend(state, target, lanes, null, new Set([Average, Weak])).items;

    expect(items.map((item) => item.championId).sort((a, b) => a - b)).toEqual([Average, Weak]);
  });

  it('NoSelectableFilter_MeansEveryLaneCandidate', () => {
    const { state, target, lanes } = scenario();

    const items = recommender().recommend(state, target, lanes, null, null).items;

    expect(items.some((item) => item.championId === Strong)).toBe(true);
    expect(items.some((item) => item.championId === CounterPick)).toBe(true);
  });

  /**
   * A criterion with nothing to go on must say so instead of reporting a neutral zero. The
   * breakdown renders the two differently, and "even matchup" is a claim while "no data" is not.
   */
  it('MissingData_IsDistinguishableFromNeutral', () => {
    const { state, target, lanes } = scenario();
    const items = recommender().recommend(state, target, lanes).items;

    const t = (championId: number, kind: ScoreTermKind): ScoreTerm =>
      termOf(
        single(items, (item) => item.championId === championId),
        kind,
      );

    // Only CounterPick has a recorded matchup against the enemy mid laner.
    const known = t(CounterPick, 'laneMatchup');
    const unknown = t(Strong, 'laneMatchup');

    expect(termHasData(known)).toBe(true);
    expect(termHasData(unknown)).toBe(false);
    expect(unknown.logOdds).toBeNull();

    // Either way the score is unaffected: a missing criterion contributes nothing.
    expect(termPoints(unknown)).toBe(0);

    // Same for the duo record, which only Strong has.
    expect(termHasData(t(Strong, 'synergy'))).toBe(true);
    expect(termHasData(t(Weak, 'synergy'))).toBe(false);
  });

  /** Nothing to fit into yet, so the composition term must stay silent. */
  it('CompositionTerm_HasNoDataBeforeAnyAllyPicked', () => {
    const session = new SessionBuilder().localPlayer(2).locked(7, EnemyMid).onClock(2, 'pick').build();

    const state = draftStateFrom(session);
    const target = new TurnTracker().resolve(state)!;
    const lanes = new LanePredictor(meta()).predict(state.enemies);

    const items = recommender().recommend(state, target, lanes).items;

    for (const item of items) expect(termHasData(termOf(item, 'composition'))).toBe(false);
  });

  /**
   * A chip is the first thing read during a draft, and there is no room on it for the caveats.
   * Every one must therefore carry the longer explanation behind it.
   */
  it('EveryReasonChip_CarriesAnExplanation', () => {
    const { state, target, lanes } = scenario();
    const rec = recommender();

    const sets = [
      rec.recommend(state, target, lanes),
      rec.recommend(state, target, lanes),
      rec.recommend(state, target, lanes),
      rec.recommend(state, { ...target, action: 'ban' }, lanes),
    ];

    const chips = sets.flatMap((set) => set.items).flatMap((item) => item.reasons);

    expect(chips.length).toBeGreaterThan(0);
    for (const chip of chips) {
      expect((chip.hint ?? '').trim() === '', `Chip "${chip.text}" hat keine Erklärung.`).toBe(false);
    }
  });

  /** Every criterion has to be introduceable on screen, or the line reads as jargon. */
  it('EveryTermKind_HasALabelAndAnExplanation', () => {
    for (const kind of ALL_TERM_KINDS) {
      expect(termLabel(kind).trim() === '', `${kind} braucht ein Label.`).toBe(false);
      expect(termHint(kind).trim() === '', `${kind} braucht eine Erklärung.`).toBe(false);
    }
  });

  it('ReasonsAreNeverDuplicated', () => {
    const { state, target, lanes } = scenario();
    const banTarget: RecommendationTarget = { ...target, action: 'ban' };

    const sets = [recommender().recommend(state, target, lanes), recommender().recommend(state, banTarget, lanes)];

    for (const item of sets.flatMap((set) => set.items)) {
      expect(new Set(item.reasons.map((reason) => reason.text)).size).toBe(item.reasons.length);
    }
  });

  it('OrderIsStableAcrossCalls', () => {
    const { state, target, lanes } = scenario();
    const rec = recommender();

    const first = rec.recommend(state, target, lanes).items.map((i) => i.championId);
    const second = rec.recommend(state, target, lanes).items.map((i) => i.championId);

    expect(second).toEqual(first);
  });

  /**
   * The chip lists only write positions that changed, which relies on the reasons coming back
   * value-equal between two renders. A culture-dependent format or a timestamp in a chip would
   * make that guard never fire, and the flickering would be back without a red test.
   */
  it('IdenticalInput_ProducesValueEqualReasonChips', () => {
    const { state, target, lanes } = scenario();
    const rec = recommender();

    const first = rec.recommend(state, target, lanes).items;
    const second = rec.recommend(state, target, lanes).items;

    expect(second.length).toBe(first.length);
    for (let i = 0; i < first.length; i++) expect(second[i]!.reasons).toEqual(first[i]!.reasons);
  });

  /**
   * The duel term is a difference, not an absolute rate: a champion that does against this
   * opponent exactly what it does on the lane in general has learnt nothing from the matchup, so
   * the term is zero.
   */
  it('ADuelThatMatchesTheChampionsUsualRate_AddsNothing', () => {
    const m = new MetaBuilder()
      .champion(Strong, 'Strong', { damage: DamageType.Magic, tags: ['Mage'], attackRange: 550, defense: 3 })
      .champion(EnemyMid, 'EnemyMid', { damage: DamageType.Magic, tags: ['Mage'], attackRange: 550, defense: 3 })
      .inLane(Strong, Lane.Mid, { winRate: 0.56, play: 40_000 })
      .inLane(EnemyMid, Lane.Mid, { winRate: 0.5, play: 40_000 })
      // Exactly the champion's own lane rate: no news about this particular opponent.
      .matchup(Strong, EnemyMid, Lane.Mid, 0.56, 40_000)
      .build();

    // Not exactly zero: the lane rate and the matchup are shrunk with different priors, so the
    // two differ slightly even for identical raw rates — a rounding residual, not a term.
    expect(duelTerm(m)).toBeCloseTo(0, 2);
  });

  it('ADuelAboveTheChampionsUsualRate_StillCounts', () => {
    const m = new MetaBuilder()
      .champion(Strong, 'Strong', { damage: DamageType.Magic, tags: ['Mage'], attackRange: 550, defense: 3 })
      .champion(EnemyMid, 'EnemyMid', { damage: DamageType.Magic, tags: ['Mage'], attackRange: 550, defense: 3 })
      .inLane(Strong, Lane.Mid, { winRate: 0.5, play: 40_000 })
      .inLane(EnemyMid, Lane.Mid, { winRate: 0.5, play: 40_000 })
      .matchup(Strong, EnemyMid, Lane.Mid, 0.56, 40_000)
      .build();

    expect(duelTerm(m) > 0.2, 'Ein echter Vorteil gegen den Gegner muss zählen.').toBe(true);
  });

  /**
   * Two champions equally good against this opponent end up within a rounding error of each
   * other, however different their general reputation — the duel measurement replaces the
   * general one instead of stacking on it.
   */
  it('TwoChampionsEquallyGoodAgainstTheOpponent_ScoreTheSameOnThatLane', () => {
    const m = new MetaBuilder()
      .champion(Strong, 'Strong', { damage: DamageType.Magic, tags: ['Mage'], attackRange: 550, defense: 3 })
      .champion(Average, 'Average', { damage: DamageType.Magic, tags: ['Mage'], attackRange: 550, defense: 3 })
      .champion(EnemyMid, 'EnemyMid', { damage: DamageType.Magic, tags: ['Mage'], attackRange: 550, defense: 3 })
      .inLane(Strong, Lane.Mid, { winRate: 0.56, play: 40_000 })
      .inLane(Average, Lane.Mid, { winRate: 0.5, play: 40_000 })
      .inLane(EnemyMid, Lane.Mid, { winRate: 0.5, play: 40_000 })
      .matchup(Strong, EnemyMid, Lane.Mid, 0.58, 40_000)
      .matchup(Average, EnemyMid, Lane.Mid, 0.58, 40_000)
      .build();

    const state = draftStateFrom(new SessionBuilder().localPlayer(2).locked(7, EnemyMid).onClock(2, 'pick').build());

    const target = new TurnTracker().resolve(state)!;
    const lanes = new LanePredictor(m).predict(state.enemies);
    const items = new Recommender(m, TraitTable.empty).recommend(state, target, lanes).items;

    const gap = Math.abs(logit(score(items, Strong)) - logit(score(items, Average)));
    const reputationGap = Math.abs(logit(0.55955) - logit(0.5));

    expect(
      gap < reputationGap / 10,
      `Der Reputationsvorsprung darf nicht durchschlagen: ${gap.toFixed(4)} gegen ${reputationGap.toFixed(4)} Logit.`,
    ).toBe(true);
  });

  /** The OP.GG tier does not move the score. It stays on screen as a chip; it stays out of the number. */
  it('TheOpGgTier_DoesNotMoveTheScore', () => {
    const laneTerm = (tier: number): number => {
      const m = new MetaBuilder()
        .champion(Strong, 'Strong', { damage: DamageType.Magic, tags: ['Mage'], attackRange: 550, defense: 3 })
        .inLane(Strong, Lane.Mid, { winRate: 0.52, play: 40_000, tier })
        .build();

      const state = draftStateFrom(new SessionBuilder().localPlayer(2).onClock(2, 'pick').build());
      const target = new TurnTracker().resolve(state)!;
      const lanes = new LanePredictor(m).predict(state.enemies);

      return term(new Recommender(m, TraitTable.empty).recommend(state, target, lanes).items, Strong, 'laneStrength') ?? 0;
    };

    // Every step of the ladder, including the -1 that means "no tier at all".
    const terms = [-1, 0, 1, 2, 3, 4, 5].map(laneTerm);

    for (const t of terms) expect(t).toBeCloseTo(terms[0]!, 9);
  });

  /**
   * The tier is still shown — as context, not as an argument. A chip that scores nothing must
   * not be coloured as if it did, or the list would look like the tier had moved it.
   */
  it('TheTierChip_IsNeutral', () => {
    const m = new MetaBuilder()
      .champion(Strong, 'Strong', { damage: DamageType.Magic, tags: ['Mage'], attackRange: 550, defense: 3 })
      .inLane(Strong, Lane.Mid, { winRate: 0.52, play: 40_000, tier: 1 })
      .build();

    const state = draftStateFrom(new SessionBuilder().localPlayer(2).onClock(2, 'pick').build());
    const target = new TurnTracker().resolve(state)!;
    const lanes = new LanePredictor(m).predict(state.enemies);

    const reasons = single(new Recommender(m, TraitTable.empty).recommend(state, target, lanes).items, (item) => item.championId === Strong).reasons;

    const chip = single(reasons, (reason) => reason.text.includes('Tier'));
    expect(chip.tone).toBe('neutral');
  });

  /**
   * Rows the analysis writes for a lane the tier list never listed carry no tier AND no games.
   * Those must stay unrated; the game count is what tells the two apart.
   */
  it('ATierWithoutGamesBehind_It_StaysUnrated', () => {
    const m = new MetaBuilder()
      .champion(Strong, 'Strong', { damage: DamageType.Magic, tags: ['Mage'], attackRange: 550, defense: 3 })
      .inLane(Strong, Lane.Mid, { winRate: 0.5, play: 0, tier: 0 })
      .build();

    expect(m.laneStat(Strong, Lane.Mid)!.tier).toBe(-1);
  });

  /**
   * The rest of the enemy team is measured the same way as the direct duel: against what this
   * champion does anyway. An edge that merely matches the champion's own rate must not move the score.
   */
  it('OffLaneDuelsAtTheChampionsUsualRate_AddNothing', () => {
    const m = new MetaBuilder()
      .champion(Strong, 'Strong', { damage: DamageType.Magic, tags: ['Mage'], attackRange: 550, defense: 3 })
      .champion(EnemyTop, 'EnemyTop', { damage: DamageType.Physical, tags: ['Fighter'], attackRange: 175, defense: 5 })
      .inLane(Strong, Lane.Mid, { winRate: 0.56, play: 40_000 })
      .inLane(EnemyTop, Lane.Top, { winRate: 0.5, play: 40_000 })
      // Recorded on THEIR lane, so this is an off-lane edge for our mid candidate — and it is
      // exactly what the champion does on its own lane anyway.
      .matchup(Strong, EnemyTop, Lane.Top, 0.56, 40_000)
      .build();

    expect(offLaneTerm(m)).toBeCloseTo(0, 2);
  });

  it('OffLaneDuelsAboveTheChampionsUsualRate_StillCount', () => {
    const m = new MetaBuilder()
      .champion(Strong, 'Strong', { damage: DamageType.Magic, tags: ['Mage'], attackRange: 550, defense: 3 })
      .champion(EnemyTop, 'EnemyTop', { damage: DamageType.Physical, tags: ['Fighter'], attackRange: 175, defense: 5 })
      .inLane(Strong, Lane.Mid, { winRate: 0.5, play: 40_000 })
      .inLane(EnemyTop, Lane.Top, { winRate: 0.5, play: 40_000 })
      .matchup(Strong, EnemyTop, Lane.Top, 0.6, 40_000)
      .build();

    expect(offLaneTerm(m) > 0.1, 'Ein echter Vorteil gegen das übrige Team muss zählen.').toBe(true);
  });

  /**
   * The same duo win rate measured over 40 games has to come out less certain than over 40.000.
   * What it must never do is look MORE certain.
   */
  it('AThinSynergy_MakesTheScoreLessCertainThanAThickOne', () => {
    const uncertainty = (synergyPlay: number): number => {
      const m = new MetaBuilder()
        .champion(Strong, 'Strong', { damage: DamageType.Magic, tags: ['Mage'], attackRange: 550, defense: 3 })
        .champion(AllySupport, 'AllySupport', { damage: DamageType.Magic, tags: ['Tank'], attackRange: 125, defense: 6 })
        .inLane(Strong, Lane.Mid, { winRate: 0.52, play: 40_000 })
        .inLane(AllySupport, Lane.Support, { winRate: 0.51, play: 40_000 })
        .synergy(Strong, AllySupport, 0.58, { play: synergyPlay })
        .build();

      const state = draftStateFrom(new SessionBuilder().localPlayer(2).locked(4, AllySupport).onClock(2, 'pick').build());

      const target = new TurnTracker().resolve(state)!;
      const lanes = new LanePredictor(m).predict(state.enemies);

      return single(new Recommender(m, TraitTable.empty).recommend(state, target, lanes).items, (item) => item.championId === Strong).uncertainty;
    };

    const thin = uncertainty(40);
    const thick = uncertainty(40_000);

    expect(thin > 0, 'Der Fehlerbalken darf nicht null sein.').toBe(true);
    expect(thin > thick, `40 Spiele müssen unsicherer sein als 40.000 (${thin} gegen ${thick}).`).toBe(true);
  });

  /**
   * A candidate with no sampled evidence at all must report no error rather than a small one —
   * the panel turns that into "Datenlage zu dünn" instead of a confident-looking verdict.
   */
  it('WithoutAnySampledTerm_TheScoreReportsNoError', () => {
    const m = new MetaBuilder()
      .champion(Strong, 'Strong', { damage: DamageType.Magic, tags: ['Mage'], attackRange: 550, defense: 3 })
      .inLane(Strong, Lane.Mid, { winRate: 0.5, play: 0 })
      .build();

    const state = draftStateFrom(new SessionBuilder().localPlayer(2).onClock(2, 'pick').build());
    const target = new TurnTracker().resolve(state)!;

    const item = single(
      new Recommender(m, TraitTable.empty).recommend(state, target, LanePredictionResult.empty).items,
      (entry) => entry.championId === Strong,
    );

    expect(item.uncertainty).toBe(0);
  });

  it('TwoReasonsWithTheSameTextToneAndHint_AreEqual', () => {
    // Reasons are compared by value; in TS that is structural equality.
    expect(neutral('gleich')).toEqual(neutral('gleich'));
    expect(neutral('gleich')).not.toEqual(pro('gleich'));
    expect(neutral('gleich')).not.toEqual(neutral('gleich', 'anders'));
  });

  it('LimitIsRespected', () => {
    const { state, target, lanes } = scenario();

    expect(recommender().recommend(state, target, lanes, null, null, 2).items).toHaveLength(2);
  });

  /** A zero limit means an empty list, not a surprise single row. */
  it('LimitZero_ReturnsNothing', () => {
    const { state, target, lanes } = scenario();

    expect(recommender().recommend(state, target, lanes, null, null, 0).items).toHaveLength(0);
  });

  /**
   * A lane whose roster is empty must not end the list — every champion is then a candidate,
   * scored on whatever the data says.
   */
  it('ALaneWithoutARoster_FallsBackToEveryChampion', () => {
    const m = new MetaBuilder()
      .champion(Strong, 'Strong', { damage: DamageType.Magic, tags: ['Mage'], attackRange: 550, defense: 3 })
      .champion(Average, 'Average', { damage: DamageType.Magic, tags: ['Mage'], attackRange: 525, defense: 3 })
      // Both are listed on Mid only; the advised seat sits on Top, whose roster is empty.
      .inLane(Strong, Lane.Mid, { winRate: 0.54, play: 2000 })
      .inLane(Average, Lane.Mid, { winRate: 0.5, play: 2000 })
      .build();

    const state = draftStateFrom(new SessionBuilder().localPlayer(0).onClock(0, 'pick').build());
    const target = new TurnTracker().resolve(state)!;

    const items = new Recommender(m, TraitTable.empty).recommend(state, target, LanePredictionResult.empty, null, null, 20).items;

    expect(items).toHaveLength(2);
  });

  /**
   * The ban score is denied win-rate points: the gate (how likely the enemy takes the champion)
   * times the edge the champion would give them, in points. Pinned numerically.
   */
  it('ABanScore_IsTheGateTimesTheDeniedPoints', () => {
    const m = new MetaBuilder()
      .champion(Menace, 'Menace', { damage: DamageType.Physical, tags: ['Fighter'], attackRange: 175, defense: 5 })
      // Tier 3 is the middle, so the tier nudge contributes nothing to this check.
      .inLane(Menace, Lane.Top, { winRate: 0.56, play: 40_000, tier: 3, pickRate: 0.05, banRate: 0.05 })
      .build();

    const state = draftStateFrom(new SessionBuilder().localPlayer(0).onClock(0, 'ban').build());
    const target = new TurnTracker().resolve(state)!;

    const item = single(
      new Recommender(m, TraitTable.empty).recommend(state, target, LanePredictionResult.empty, null, null, 20).items,
      (entry) => entry.championId === Menace,
    );

    const strength = logit(shrink(0.56, 40_000, LANE_PRIOR));

    // Strength plus the seat's own lane threat at half weight — the deliberate double count.
    const threat = strength + BAN_LANE_FOCUS * strength;
    const gate = clamp(0.05 * 8 + 0.05 * 4, 0, 1);

    expect(item.score).toBeCloseTo(gate * (sigmoid(threat) - 0.5) * 100, 6);
  });

  /**
   * Only LOCKED enemies discount a lane for the ban list. A hover can still change, and treating
   * it as taken would quietly drop that lane's champions out of the list one pick too early.
   */
  it('AHoveredEnemy_DoesNotYetCloseItsLane', () => {
    const m = new MetaBuilder()
      .champion(Menace, 'Menace', { damage: DamageType.Physical, tags: ['Fighter'], attackRange: 175, defense: 5 })
      .champion(EnemyTop, 'EnemyTop', { damage: DamageType.Physical, tags: ['Fighter'], attackRange: 175, defense: 5 })
      .inLane(Menace, Lane.Top, { winRate: 0.56, play: 40_000, pickRate: 0.05, banRate: 0.05 })
      .inLane(EnemyTop, Lane.Top, { winRate: 0.52, play: 40_000 })
      .build();

    const banValue = (session: SessionBuilder): number => {
      const state = draftStateFrom(session.build());
      const target = new TurnTracker().resolve(state)!;
      const lanes = new LanePredictor(m).predict(state.enemies);

      return single(new Recommender(m, TraitTable.empty).recommend(state, target, lanes, null, null, 20).items, (entry) => entry.championId === Menace)
        .score;
    };

    const hover = banValue(new SessionBuilder().localPlayer(0).hovering(5, EnemyTop).onClock(0, 'ban'));
    const locked = banValue(new SessionBuilder().localPlayer(0).locked(5, EnemyTop).onClock(0, 'ban'));

    expect(hover > locked, `Ein Hover darf die Lane nicht schliessen (${hover} gegen ${locked}).`).toBe(true);
  });

  it('BanMode_LooksAcrossAllLanes', () => {
    const { state, target, lanes } = scenario();
    const banTarget: RecommendationTarget = { ...target, action: 'ban' };

    const set = recommender().recommend(state, banTarget, lanes, null, null, 20);

    expect(set.action).toBe('ban');
    // A top laner is a legitimate ban even though the seat on the clock plays mid.
    expect(set.items.some((item) => item.championId === Menace)).toBe(true);
  });

  it('BanMode_FlagsAChampionThatCountersALockedAlly', () => {
    const { state, target, lanes } = scenario();
    const banTarget: RecommendationTarget = { ...target, action: 'ban' };

    const set = recommender().recommend(state, banTarget, lanes, null, null, 20);
    const matches = set.items.filter((item) => item.championId === Menace);

    expect(matches.length).toBeLessThanOrEqual(1);
    const threat = matches[0];
    expect(threat).toBeDefined();
    expect(threat!.reasons.some((reason) => reason.text.includes('AllyTop'))).toBe(true);
  });

  it('BanMode_ExcludesWhatIsAlreadyGone', () => {
    const { state, target, lanes } = scenario(Strong);
    const banTarget: RecommendationTarget = { ...target, action: 'ban' };

    const items = recommender().recommend(state, banTarget, lanes, null, null, 20).items;

    expect(items.some((item) => item.championId === Strong)).toBe(false);
    expect(items.some((item) => item.championId === EnemyMid)).toBe(false);
  });

  it('AdvisingAnAlly_UsesThatAllysLane', () => {
    // The support seat is on the clock instead of us.
    const state = draftStateFrom(new SessionBuilder().localPlayer(2).locked(0, AllyTop).locked(7, EnemyMid).onClock(4, 'pick').build());

    const target = new TurnTracker().resolve(state)!;
    const lanes = new LanePredictor(meta()).predict(state.enemies);

    const set = recommender().recommend(state, target, lanes);

    expect(set.cellId).toBe(4);
    expect(set.lane).toBe(Lane.Support);
    expect(set.items.some((item) => item.championId === AllySupport)).toBe(true);
  });

  it('AllyCompExcludesTheSeatBeingAdvised', () => {
    // Advising the support seat while a support is somehow already locked there must not treat
    // that champion as a team-mate of itself.
    const state = draftStateFrom(new SessionBuilder().localPlayer(2).locked(0, AllyTop).locked(4, AllySupport).onClock(4, 'pick').build());

    const target = new TurnTracker().resolve(state)!;
    const set = recommender().recommend(state, target, LanePredictionResult.empty);

    expect(set.allyComp.count).toBe(1);
  });

  it('InactiveDraft_ProducesNothing', () => {
    const target: RecommendationTarget = { slot: slot(2, 2, true, 0, 0, Lane.Mid), action: 'pick', isFollowingTurn: true };

    const set = recommender().recommend(INACTIVE, target, LanePredictionResult.empty);

    expect(set.items).toHaveLength(0);
  });

  /**
   * Strength and popularity have to describe the same lane. They used to be chosen
   * independently, which let a ban read "stark auf Bot" while its value came from a third
   * lane's pick rate.
   */
  it('BanStrengthAndPopularity_ComeFromTheSameLane', () => {
    // Mostly played mid and merely respectable there; a rare but monstrous top pick.
    const m = new MetaBuilder()
      .champion(Menace, 'Menace', { damage: DamageType.Physical, tags: ['Fighter'], attackRange: 175, defense: 5 })
      .champion(EnemyMid, 'EnemyMid', { damage: DamageType.Magic, tags: ['Mage'], attackRange: 550, defense: 3 })
      .inLane(Menace, Lane.Mid, { roleRate: 0.85, winRate: 0.5, play: 20_000, tier: 3, pickRate: 0.1 })
      .inLane(Menace, Lane.Top, { roleRate: 0.1, winRate: 0.58, play: 2_000, tier: 1, pickRate: 0.01 })
      .inLane(EnemyMid, Lane.Mid, { roleRate: 0.9, winRate: 0.5, play: 20_000 })
      .build();

    const item = banItem(m, Menace);

    // The mid row is the one the enemy would use, so no "stark auf Top" chip may appear.
    expect(item.reasons.some((reason) => reason.text.includes('Top'))).toBe(false);
  });

  /**
   * A champion whose only lane the enemy has already locked is nearly worthless to ban: they
   * have no seat left for them. What survives is our own doubt about the lane read.
   */
  it('AChampionWhoseLaneTheEnemyFilled_IsWorthLessThanOneOnAnOpenLane', () => {
    const m = banMeta();

    const withoutInfo = banScore(m, JungleOnly, []);
    const afterTheirJunglerLocked = banScore(m, JungleOnly, [EnemyJungler]);

    expect(withoutInfo > 0, 'Ohne Information muss der Bann etwas wert sein.').toBe(true);
    expect(
      afterTheirJunglerLocked < withoutInfo / 3,
      `Mit gesperrtem Gegner-Jungler muss der Wert einbrechen (${afterTheirJunglerLocked} gegen ${withoutInfo}).`,
    ).toBe(true);
  });

  it('AChampionOnAnOpenLane_KeepsItsBanValue', () => {
    const m = banMeta();

    const withoutInfo = banScore(m, SupportOnly, []);
    const afterTheirJunglerLocked = banScore(m, SupportOnly, [EnemyJungler]);

    expect(afterTheirJunglerLocked).toBeCloseTo(withoutInfo, 6);
  });

  /**
   * The first ban round has nothing locked, so nothing may be discounted — the whole point of
   * reading the enemy lanes is that it only starts mattering once they commit.
   */
  it('WithNothingLocked_TheBanListIsUnrestricted', () => {
    const m = banMeta();

    const jungle = banScore(m, JungleOnly, []);
    const support = banScore(m, SupportOnly, []);

    expect(jungle > 0).toBe(true);
    expect(support > 0).toBe(true);
  });

  /**
   * The synergy term is a damped MEAN: the same duo win rate must contribute the same shift
   * whether one ally has locked or four.
   */
  it('FourEqualDuos_ShiftTheScoreLikeOne', () => {
    const m = new MetaBuilder()
      .champion(Strong, 'Strong')
      .inLane(Strong, Lane.Mid, { winRate: 0.52, play: 2000 })
      .champion(201, 'A1')
      .champion(202, 'A2')
      .champion(203, 'A3')
      .champion(204, 'A4')
      .synergy(Strong, 201, 0.55, { play: 2000 })
      .synergy(Strong, 202, 0.55, { play: 2000 })
      .synergy(Strong, 203, 0.55, { play: 2000 })
      .synergy(Strong, 204, 0.55, { play: 2000 })
      .build();
    const rec = new Recommender(m, TraitTable.empty);

    const one = draftStateFrom(new SessionBuilder().localPlayer(2).locked(0, 201).onClock(2, 'pick').build());
    const four = draftStateFrom(
      new SessionBuilder().localPlayer(2).locked(0, 201).locked(1, 202).locked(3, 203).locked(4, 204).onClock(2, 'pick').build(),
    );

    const target = new TurnTracker().resolve(one)!;
    const withOne = term(rec.recommend(one, target, LanePredictionResult.empty, null, null, 50).items, Strong, 'synergy');
    const withFour = term(
      rec.recommend(four, new TurnTracker().resolve(four)!, LanePredictionResult.empty, null, null, 50).items,
      Strong,
      'synergy',
    );

    expect(withOne).not.toBeNull();
    expect(withFour!).toBeCloseTo(withOne!, 10);
  });

  /**
   * The average stored duo sits above even, so the term is measured against THAT, not against
   * 50 %: a duo that is merely as good as the usual listed one says nothing about this pick.
   */
  it('ADuoAsGoodAsTheAverageListedOne_AddsNothing', () => {
    const m = new MetaBuilder()
      .champion(Strong, 'Strong')
      .inLane(Strong, Lane.Mid, { winRate: 0.52, play: 2000 })
      .champion(AllySupport, 'AllySupport')
      .synergy(Strong, AllySupport, 0.53, { play: 2000 })
      .synergyBaseline(logit(0.53))
      .build();

    expect(synergyTerm(m)).toBeCloseTo(0, 3);
  });

  it('ADuoBetterThanTheAverageListedOne_StillCounts', () => {
    const m = new MetaBuilder()
      .champion(Strong, 'Strong')
      .inLane(Strong, Lane.Mid, { winRate: 0.52, play: 2000 })
      .champion(AllySupport, 'AllySupport')
      .synergy(Strong, AllySupport, 0.6, { play: 2000 })
      .synergyBaseline(logit(shrink(0.53, 2000, SYNERGY_PRIOR)))
      .build();

    expect(synergyTerm(m) > 0.1, 'Ein ueberdurchschnittliches Duo muss zaehlen.').toBe(true);
  });

  /**
   * The off-lane term promises to count LESS than the direct duel. With four enemies at the
   * same matchup rate, its weighted mean must stay at OffLaneShare of one duel.
   */
  it('OffLaneEnemies_StayBelowTheDirectDuelsWeight', () => {
    const { state, target, lanes } = scenario();

    const items = recommender().recommend(state, target, lanes, null, null, 50).items;
    const offLane = term(items, CounterPick, 'enemyTeam');

    if (offLane === null) return;

    // The strongest single matchup in the fixture is 62 % over 3000 games; even if every
    // off-lane enemy matched it, the mean caps the term at OffLaneShare of that one duel.
    const duelCap = OFF_LANE_SHARE * Math.abs(logit(shrink(0.62, 3000, MATCHUP_PRIOR)));

    expect(Math.abs(offLane) <= duelCap + 1e-9).toBe(true);
  });

  /**
   * Without an assigned lane (blind pick, customs) the seat's lane comes from the ALLY
   * prediction. The enemy prediction cannot contain the advised seat.
   */
  it('BlindPick_ResolvesTheLaneFromTheAllyPrediction', () => {
    const { state, target, lanes: enemyLanes } = scenario();

    const blindTarget: RecommendationTarget = { ...target, slot: { ...target.slot, assignedLane: Lane.Unknown } };
    const allySlots = state.allies.map((s) =>
      s.cellId === target.slot.cellId ? { ...s, assignedLane: Lane.Unknown, hoverChampionId: Strong } : { ...s, assignedLane: Lane.Unknown },
    );
    const allyLanes = new LanePredictor(meta()).predict(allySlots);

    const set = recommender().recommend(state, blindTarget, enemyLanes, allyLanes);

    expect(set.lane).toBe(Lane.Mid);
  });

  it('ChampionsHoveredByOtherAllies_AreNotRecommended', () => {
    const session = new SessionBuilder().localPlayer(2).locked(0, AllyTop).hovering(4, Strong).locked(7, EnemyMid).onClock(2, 'pick').build();
    const state = draftStateFrom(session);
    const target = new TurnTracker().resolve(state)!;
    const lanes = new LanePredictor(meta()).predict(state.enemies);

    const items = recommender().recommend(state, target, lanes, null, null, 50).items;

    expect(items.some((item) => item.championId === Strong)).toBe(false);
  });

  it('TheTargetsOwnHover_StaysRecommendable', () => {
    const session = new SessionBuilder().localPlayer(2).locked(0, AllyTop).hovering(2, Strong).locked(7, EnemyMid).onClock(2, 'pick').build();
    const state = draftStateFrom(session);
    const target = new TurnTracker().resolve(state)!;
    const lanes = new LanePredictor(meta()).predict(state.enemies);

    const items = recommender().recommend(state, target, lanes, null, null, 50).items;

    expect(items.some((item) => item.championId === Strong)).toBe(true);
  });

  /**
   * First pick of the own team against three revealed enemies: the composition term reads the
   * enemy line-up before any ally has picked.
   */
  it('CompositionTerm_ReadsTheEnemyBeforeAnyAllyHasPicked', () => {
    const session = new SessionBuilder()
      .localPlayer(2)
      .locked(5, EnemyTop)
      .locked(6, Menace)
      .locked(7, EnemyMid)
      .onClock(2, 'pick')
      .build();

    const state = draftStateFrom(session);
    const target = new TurnTracker().resolve(state)!;
    const lanes = new LanePredictor(meta()).predict(state.enemies);

    const item = single(recommender().recommend(state, target, lanes, null, null, 50).items, (entry) => entry.championId === CounterPick);

    // Three enemies, none of them a tank or six defence: the assassin's targets stand open.
    expect(termHasData(termOf(item, 'composition'))).toBe(true);
    expect(item.reasons.some((reason) => reason.text.includes('Backline'))).toBe(true);
  });

  /**
   * Without a lane the base term falls back to the champion's own lane, not its BEST lane: a
   * maximum over five noisy rows systematically returns the luckiest sample.
   */
  it('WithoutALane_TheBaseTermUsesTheLaneTheChampionActuallyPlays', () => {
    const Flex = 111;

    const m = new MetaBuilder()
      .champion(Flex, 'Flex', { damage: DamageType.Magic, tags: ['Mage'], attackRange: 550, defense: 3 })
      .inLane(Flex, Lane.Mid, { winRate: 0.5, play: 20000 })
      .inLane(Flex, Lane.Top, { winRate: 0.58, play: 120 })
      .build();

    const session = new SessionBuilder().localPlayer(2).allyLane(2, null).onClock(2, 'pick').build();

    const state = draftStateFrom(session);
    const target = new TurnTracker().resolve(state)!;

    const item = single(
      new Recommender(m, TraitTable.empty).recommend(state, target, LanePredictionResult.empty, null, null, 50).items,
      (entry) => entry.championId === Flex,
    );

    // The 20 000-game row, not the 120-game one that happens to read two points higher.
    expect(item.score).toBeCloseTo(m.laneStat(Flex, Lane.Mid)!.winRate, 6);
    expect(item.reasons.some((reason) => reason.text.includes('stark auf'))).toBe(false);
  });

  /**
   * A duo rate carries part of each champion's own strength; the synergy term must not book
   * that part a second time. Two candidates of very different lane strength, each with a duo
   * exactly as good as the line predicts for them, get the same synergy term — zero.
   */
  it('ADuoExactlyAsGoodAsItsTwoChampions_AddsNothing', () => {
    const Intercept = 0.05,
      Slope = 0.4;

    const builder = new MetaBuilder()
      .champion(Strong, 'Strong', { damage: DamageType.Magic, tags: ['Mage'], attackRange: 550, defense: 3 })
      .champion(Weak, 'Weak', { damage: DamageType.Magic, tags: ['Mage'], attackRange: 550, defense: 3 })
      .champion(AllySupport, 'AllySupport', { damage: DamageType.Magic, tags: ['Tank'], attackRange: 125, defense: 6 })
      .inLane(Strong, Lane.Mid, { winRate: 0.54, play: 40_000 })
      .inLane(Weak, Lane.Mid, { winRate: 0.46, play: 40_000 })
      .inLane(AllySupport, Lane.Support, { winRate: 0.51, play: 40_000 });

    const onTheLine = (mine: number, partner: number): number => sigmoid(Intercept + Slope * (logit(mine) + logit(partner)));

    // Enough unrelated pairs on the same line for it to be fitted at all.
    for (let i = 0; i < MINIMUM_ROWS + 20; i++) {
      const mid = 1000 + i,
        support = 2000 + i;
      const midRate = 0.47 + (i % 13) * 0.005,
        supportRate = 0.48 + (i % 7) * 0.006;

      builder
        .champion(mid, `Mid${i}`)
        .champion(support, `Support${i}`)
        .inLane(mid, Lane.Mid, { winRate: midRate, play: 30_000 })
        .inLane(support, Lane.Support, { winRate: supportRate, play: 30_000 })
        .synergy(mid, support, onTheLine(midRate, supportRate), { play: 2_000, lane: Lane.Mid, partnerLane: Lane.Support });
    }

    const m = builder
      .synergy(Strong, AllySupport, onTheLine(0.54, 0.51), { play: 5_000, lane: Lane.Mid, partnerLane: Lane.Support })
      .synergy(Weak, AllySupport, onTheLine(0.46, 0.51), { play: 5_000, lane: Lane.Mid, partnerLane: Lane.Support })
      .build();

    expect(m.synergyLine.slope).toBeCloseTo(Slope, 2);

    const state = draftStateFrom(new SessionBuilder().localPlayer(2).locked(4, AllySupport).onClock(2, 'pick').build());
    const target = new TurnTracker().resolve(state)!;
    const items = new Recommender(m, TraitTable.empty).recommend(state, target, new LanePredictor(m).predict(state.enemies), null, null, 300).items;

    const synergyOf = (championId: number): number => term(items, championId, 'synergy') ?? Number.NaN;

    expect(synergyOf(Strong)).toBeCloseTo(0, 3);
    expect(synergyOf(Weak)).toBeCloseTo(0, 3);
  });

  /**
   * Numbers from a patch that is no longer played describe lane strengths that have since moved.
   * The score stays the same — nothing newer is known — but its error bar has to widen.
   */
  it('StaleData_WidensTheErrorButNotTheScore', () => {
    const { state, target, lanes } = scenario();
    const m = meta();
    const rec = new Recommender(m, TraitTable.empty);

    const current = single(rec.recommend(state, target, lanes).items, (item) => item.championId === Strong);

    m.patchesBehind = 2;
    const stale = single(rec.recommend(state, target, lanes).items, (item) => item.championId === Strong);

    expect(stale.score).toBeCloseTo(current.score, 12);
    expect(stale.uncertainty > current.uncertainty).toBe(true);

    // Two patches of measured drift on the lane term alone: 0,0272 log-odds, about 0,68 points.
    const added = Math.sqrt(stale.uncertainty * stale.uncertainty - current.uncertainty * current.uncertainty);
    expect(added).toBeCloseTo(Math.sqrt(2 * LANE_DRIFT_PER_PATCH), 4);
  });

  // ---- Evaluate: the figure shown for a hover ----

  /**
   * The hover's figure IS the list's figure. Computed twice, the two would sooner or later
   * disagree about the same champion on the same screen.
   */
  it('Evaluate_GivesTheListsOwnScoreAndPlace', () => {
    const { state, target, lanes } = scenario();
    const rec = recommender();
    const list = rec.recommend(state, target, lanes, null, null, 50).items;

    expect(list.length).toBeGreaterThan(0);

    for (let i = 0; i < list.length; i++) {
      const evaluation = rec.evaluate(state, target, list[i]!.championId, lanes)!;

      expect(evaluation.item.score).toBeCloseTo(list[i]!.score, 12);
      expect(evaluation.item.uncertainty).toBeCloseTo(list[i]!.uncertainty, 12);
      expect(evaluation.rank).toBe(i + 1);
      expect(evaluation.ranking.map((item) => item.championId)).toEqual(list.map((item) => item.championId));
    }
  });

  /**
   * What the feature is for: the champion somebody hovers is often not among the rows on screen.
   * It still gets its number, and its place below them.
   */
  it('Evaluate_ReachesBelowTheVisibleRows', () => {
    const { state, target, lanes } = scenario();
    const rec = recommender();
    const full = rec.recommend(state, target, lanes, null, null, 50).items;
    const last = full[full.length - 1]!;

    expect(rec.recommend(state, target, lanes, null, null, 1).items.some((item) => item.championId === last.championId)).toBe(false);

    const evaluation = rec.evaluate(state, target, last.championId, lanes)!;

    expect(evaluation.rank).toBe(full.length);
    expect(evaluation.item.score).toBeCloseTo(last.score, 12);
  });

  /** The seat's own hover is one of its candidates: that hover is what the list is about. */
  it('Evaluate_TheSeatsOwnHoverIsACandidate', () => {
    const { state, target, lanes } = hovering(Weak);

    const evaluation = recommender().evaluate(state, target, Weak, lanes)!;

    expect(evaluation.rank > 0).toBe(true);
    expect(evaluation.ranking.some((item) => item.championId === Weak)).toBe(true);
  });

  /**
   * A pick intent declared in planning stays one through the bans. Asked during a ban turn, the
   * answer is still the champion's win rate as a pick — not its value as a ban.
   */
  it('Evaluate_ScoresAPickEvenDuringABanTurn', () => {
    const { state, target, lanes } = hovering(Strong, 'ban');
    const rec = recommender();

    expect(target.action).toBe('ban');

    const evaluation = rec.evaluate(state, target, Strong, lanes)!;
    const asPick = single(rec.recommend(state, { ...target, action: 'pick' }, lanes, null, null, 50).items, (item) => item.championId === Strong);

    expect(evaluation.item.score).toBeCloseTo(asPick.score, 12);
    expect(evaluation.item.breakdown[0]!.kind).toBe('laneStrength');
  });

  /**
   * Off the seat's lane: scored by the same rules, reported without a place — and the missing lane
   * statistic stays missing instead of quietly reading as an average champion.
   */
  it('Evaluate_AnOffLaneChampionHasNoPlaceAndNoLaneStrength', () => {
    const { state, target, lanes } = scenario();

    const evaluation = recommender().evaluate(state, target, Menace, lanes)!;

    expect(evaluation.rank).toBe(0);
    expect(evaluation.item.championId).toBe(Menace);
    expect(evaluation.lane).toBe(Lane.Mid);
    expect(termHasData(termOf(evaluation.item, 'laneStrength'))).toBe(false);
  });

  it('Evaluate_ABannedChampionHasNoPlace', () => {
    const { state, target, lanes } = scenario(Strong);

    const evaluation = recommender().evaluate(state, target, Strong, lanes)!;

    expect(evaluation.rank).toBe(0);
    expect(evaluation.ranking.some((item) => item.championId === Strong)).toBe(false);
  });

  it('Evaluate_WithoutAChampionOrData_SaysNothing', () => {
    const { state, target, lanes } = scenario();

    expect(recommender().evaluate(state, target, 0, lanes)).toBeNull();
    expect(new Recommender(MetaLookup.empty(), TraitTable.empty).evaluate(state, target, Strong, lanes)).toBeNull();
  });
});
