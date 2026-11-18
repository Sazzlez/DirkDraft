// Bans on our own data (recommender.ts scoreBansOwn, docs/modellpruefung.md Messung 31): the value
// is an expectation — how often the enemy takes the champion, times what he brings them — and a
// champion the own team is as likely to take counts nothing, because a ban takes him from both.

import { describe, expect, it } from 'vitest';
import { DraftEngine } from '../../src/core/engine';
import { VERSUS_WEIGHT } from '../../src/core/model/draftModel';
import { draftStateFrom } from '../../src/core/model/draftState';
import { Lane } from '../../src/core/model/lane';
import { LanePredictor } from '../../src/core/model/lanePredictor';
import type { MetaLookup, MetaSnapshot } from '../../src/core/model/metaLookup';
import { Recommender, type Recommendation, type RecommendationSet } from '../../src/core/model/recommender';
import { countLeadingBanTies } from '../../src/core/model/scoreError';
import { logit, sigmoid } from '../../src/core/model/scoreModel';
import { TraitTable } from '../../src/core/model/traits';
import { BAN_EVEN_BELOW, GoldenAdapter, OWN_BANS_SHOWN, type GoldenFrame } from '../../src/data/golden';
import { MetaBuilder, SessionBuilder } from './helpers';

const Thresh = 1, Nami = 2, Braum = 3, Zyra = 4, Garen = 5, Darius = 6, MasterYi = 7, Jarvan = 8, Ahri = 9, Jinx = 10;

/** `source`: our own count ("riot") or an OP.GG file. */
function builder(source: 'riot' | 'opgg' = 'riot'): MetaBuilder {
  const b = new MetaBuilder()
    .champion(Thresh, 'Thresh')
    .inLane(Thresh, Lane.Support, { winRate: 0.52, play: 27000, pickRate: 0.12, banRate: 0.08 })
    .champion(Nami, 'Nami')
    .inLane(Nami, Lane.Support, { winRate: 0.52, play: 26000, pickRate: 0.115, banRate: 0.02 })
    .champion(Braum, 'Braum')
    .inLane(Braum, Lane.Support, { winRate: 0.51, play: 11000, pickRate: 0.05, banRate: 0.02 })
    .champion(Zyra, 'Zyra')
    .inLane(Zyra, Lane.Support, { winRate: 0.505, play: 11000, pickRate: 0.05, banRate: 0.03 })
    .champion(Garen, 'Garen')
    .inLane(Garen, Lane.Top, { winRate: 0.53, play: 22000, pickRate: 0.1, banRate: 0.1 })
    .champion(Darius, 'Darius')
    .inLane(Darius, Lane.Top, { winRate: 0.5, play: 18000, pickRate: 0.08, banRate: 0.1 })
    .champion(MasterYi, 'Master Yi')
    .inLane(MasterYi, Lane.Jungle, { winRate: 0.503, play: 15000, pickRate: 0.07, banRate: 0.2 })
    .champion(Jarvan, 'Jarvan IV')
    .inLane(Jarvan, Lane.Jungle, { winRate: 0.5, play: 11000, pickRate: 0.05, banRate: 0.01 })
    .champion(Ahri, 'Ahri')
    .inLane(Ahri, Lane.Mid, { winRate: 0.52, play: 30000, pickRate: 0.14, banRate: 0.05 })
    .champion(Jinx, 'Jinx')
    .inLane(Jinx, Lane.Adc, { winRate: 0.51, play: 33000, pickRate: 0.15, banRate: 0.03 });
  const snapshot = b.snapshotRef();
  if (source === 'riot') {
    snapshot.source = 'riot';
    snapshot.completeDuelSlope = 1;
    snapshot.completeDuelOffset = 0;
    snapshot.matchups.push({ championId: Zyra, opponentId: Braum, lane: Lane.Support, winRate: 0.57, play: 2000, fromCompleteList: true });
    snapshot.matchups.push({ championId: MasterYi, opponentId: Jarvan, lane: Lane.Jungle, winRate: 0.58, play: 2000, fromCompleteList: true });
    snapshot.versus = [{ championId: Thresh, lane: Lane.Support, opponentId: Jinx, opponentLane: Lane.Adc, winRate: 0.56, play: 3000 }];
  }
  return b;
}

/** Ranked, the own seat on support (cell 4) about to ban. */
const banning = () => new SessionBuilder().queue(420).localPlayer(4).onClock(4, 'ban');

function bans(meta: MetaLookup, session: SessionBuilder): RecommendationSet {
  const state = draftStateFrom(session.build());
  const predictor = new LanePredictor(meta);
  const target = { slot: state.allies.find((slot) => slot.cellId === 4)!, action: 'ban' as const, isFollowingTurn: true };
  return new Recommender(meta, TraitTable.empty).recommend(state, target, predictor.predict(state.enemies), predictor.predict(state.allies), null, 20);
}

const of = (set: RecommendationSet, id: number): Recommendation | undefined => set.items.find((item) => item.championId === id);
const part = (item: Recommendation, kind: string): number | null => item.breakdown.find((t) => t.kind === kind)!.logOdds;
const texts = (item: Recommendation): string[] => item.reasons.map((r) => r.text);

describe('bans as an expectation (own data)', () => {
  it('is how often the enemy takes the champion times what he brings them', () => {
    const meta = builder().build();
    const set = bans(meta, banning());
    expect(set.expected).toBe(true);

    const thresh = of(set, Thresh)!;
    const stat = meta.laneStat(Thresh, Lane.Support)!;
    // Each team takes him in pick / 2 of the games he is free in; of ten bans five are the enemy's.
    const chance = (Math.sqrt(1 - stat.banRate) * stat.pickRate) / 2 / (1 - stat.banRate);
    expect(part(thresh, 'banPopularity')).toBeCloseTo(chance, 12);
    expect(part(thresh, 'banStrength')).toBeCloseTo(logit(stat.winRate), 12);
    expect(thresh.score).toBeCloseTo(chance * (sigmoid(logit(stat.winRate)) - 0.5) * 100, 12);
    // A tenth of a point, not two and a half: the old gate stood at 1 from 12,5 % pick rate.
    expect(thresh.score).toBeGreaterThan(0.05);
    expect(thresh.score).toBeLessThan(0.3);
    expect(thresh.banLane).toBe(Lane.Support);
    expect(texts(thresh)).toContain('beim Gegner in 6 % der Games');
  });

  it('counts nothing for a champion the own team is as likely to take', () => {
    const set = bans(builder().build(), banning());
    // Nobody has shown a pick: on every lane but the own one the ban takes the champion from both.
    expect(set.items.map((item) => item.championId).sort()).toEqual([Thresh, Nami, Braum, Zyra].sort());
    expect(of(set, Garen)).toBeUndefined();
    expect(of(set, Ahri)).toBeUndefined();
  });

  it("counts a lane in full once the own seat there has shown a pick, and what beats that pick on top", () => {
    const meta = builder().build();
    // The top laner shows Darius: Garen can only go to the enemy now.
    const top = bans(meta, banning().hovering(0, Darius));
    const garen = of(top, Garen)!;
    expect(part(garen, 'banStrength')).toBeCloseTo(logit(meta.laneStat(Garen, Lane.Top)!.winRate), 12);
    expect(texts(garen).some((t) => / WR auf /.test(t))).toBe(true);
    expect(of(top, Darius)).toBeUndefined(); // a teammate's hover is taken

    // The jungler shows Jarvan IV: Master Yi beats him, far beyond his own lane strength.
    const jungle = bans(meta, banning().hovering(1, Jarvan));
    const yi = of(jungle, MasterYi)!;
    expect(part(yi, 'banLaneThreat')).toBeGreaterThan(0.05);
    expect(texts(yi)).toContain('schlägt euren Jarvan IV');
    expect(jungle.items[0]!.championId).toBe(MasterYi);
  });

  it("takes the advised seat's own pick intent as the champion to protect", () => {
    const meta = builder().build();
    const before = of(bans(meta, banning()), Zyra)!;
    const set = bans(meta, banning().hovering(4, Braum));
    const zyra = of(set, Zyra)!;
    expect(part(before, 'banLaneThreat')).toBeNull();
    expect(part(zyra, 'banLaneThreat')).toBeGreaterThan(0.05);
    expect(zyra.score).toBeGreaterThan(before.score * 2);
    expect(texts(zyra)).toContain('schlägt deinen Braum');
    expect(of(set, Braum)).toBeUndefined(); // nobody bans his own pick
  });

  it('counts a pair across lanes by its measured weight', () => {
    const meta = builder().build();
    const set = bans(meta, banning().hovering(3, Jinx));
    const thresh = of(set, Thresh)!;
    const view = meta.versus(Thresh, Lane.Support, Jinx, Lane.Adc)!;
    expect(part(thresh, 'banTeamThreat')).toBeCloseTo(VERSUS_WEIGHT.botCross * (logit(view.winRate) - logit(view.expected)), 12);
    expect(texts(thresh)).toContain('gut gegen euren Jinx');
  });

  it('carries an error, and says which bans cannot be told apart', () => {
    const set = bans(builder().build(), banning());
    expect(set.items.every((item) => item.uncertainty > 0)).toBe(true);
    // Thresh and Nami stand a hundredth of a point apart, less than their errors: tied. A clear leader stands alone.
    expect(countLeadingBanTies(set.items)).toBeGreaterThanOrEqual(2);
    expect(countLeadingBanTies([{ score: 0.5, uncertainty: 0.002 }, { score: 0.1, uncertainty: 0.002 }])).toBe(0);
    // Without an error (OP.GG files) nothing is called tied.
    expect(countLeadingBanTies([{ score: 2, uncertainty: 0 }, { score: 2, uncertainty: 0 }])).toBe(0);
  });

  it('leaves OP.GG files with the 1.5 sum', () => {
    const set = bans(builder('opgg').build(), banning());
    expect(set.expected).toBeUndefined();
    expect(of(set, Garen)).toBeDefined();
    expect(set.items.every((item) => item.uncertainty === 0)).toBe(true);
  });
});

describe('the ban tile on own data', () => {
  const tile = (session: SessionBuilder) => {
    const snapshot = builder().snapshotRef() as MetaSnapshot;
    const frame = new DraftEngine(snapshot).update(session.build()).frame as unknown as GoldenFrame;
    return new GoldenAdapter(snapshot.champions).pickTile(frame.bans!, frame, 'Banns für dich');
  };

  it('shows the value to the hundredth, the chance in the row and the tie group', () => {
    const view = tile(banning());
    const thresh = view.items.find((item) => item.champion.id === Thresh)!;
    expect(thresh.value).toMatch(/^\+0,\d\d Pkt$/);
    expect(thresh.reason).toMatch(/ WR auf Support · beim Gegner in 6 % der Games$/);
    expect(thresh.terms.map((t) => t.label)).toEqual(['Stärke auf seiner Lane', 'Matchup gegen euren Pick', 'Gegen eure anderen Picks', 'Gegner pickt ihn']);
    expect(view.tied).toBeGreaterThanOrEqual(2);
  });

  it("marks a ban that stands among the seat's own first picks, until the seat shows a pick", () => {
    const open = tile(banning());
    expect(open.items.filter((item) => item.badge === 'oben in deiner Pick-Liste').length).toBeGreaterThan(0);
    expect(open.footnote).toMatch(/Hover deinen Pick/);

    const decided = tile(banning().hovering(4, Braum));
    expect(decided.items.every((item) => item.badge === null)).toBe(true);
    expect(decided.footnote).not.toMatch(/Hover deinen Pick/);
    expect(decided.footnote).toMatch(/ein Bann nimmt ihn beiden/);
  });

  // Since 17.11.2026: on the real numbers the best ban is worth about a tenth of a point (Twitch
  // 0.126, Nami 0.120), and a long list of such rows claimed a choice that is none.
  describe('is short and says when the choice hardly matters', () => {
    const snapshot = builder().snapshotRef() as MetaSnapshot;
    const frame = new DraftEngine(snapshot).update(banning().build()).frame as unknown as GoldenFrame;
    const adapter = new GoldenAdapter(snapshot.champions);
    const set = frame.bans!;
    /** The same list with other values: the first `scores` replace the leaders'. */
    const withScores = (tied: number, ...scores: number[]) =>
      adapter.pickTile({ ...set, tied, items: set.items.map((item, i) => (i < scores.length ? { ...item, score: scores[i]! } : item)) }, frame, 'Banns für dich');

    it('shows the first three of the model’s list, in its order', () => {
      expect(set.items.length).toBeGreaterThan(OWN_BANS_SHOWN);
      const view = adapter.pickTile(set, frame, 'Banns für dich');
      expect(view.items.map((item) => item.champion.id)).toEqual(set.items.slice(0, 3).map((item) => item.championId));
      // The tie group never counts rows that are not shown.
      expect(view.tied).toBe(Math.min(set.tied, 3));
      // The model's own list is not cut: the team's ban plan and the golden files read it whole.
      expect(frame.bans!.items.length).toBe(set.items.length);
    });

    it('says so when the leaders are tied', () => {
      expect(set.tied).toBeGreaterThanOrEqual(2);
      expect(adapter.pickTile(set, frame, 'Banns für dich').even).toBe('kaum Unterschied – bann, was dich stört');
      expect(withScores(2, 1.4, 1.39).even).toBe('kaum Unterschied – bann, was dich stört');
    });

    it('says so when the best ban is worth less than half a point', () => {
      expect(withScores(0, 0.49, 0.2).even).toBe('kaum Unterschied – bann, was dich stört');
      expect(withScores(0, BAN_EVEN_BELOW, 0.2).even).toBeNull();
    });

    it('says nothing when one ban clearly leads', () => {
      const clear = withScores(0, 0.8, 0.2, 0.1);
      expect(clear.even).toBeNull();
      expect(clear.tied).toBe(0);
      expect(clear.items).toHaveLength(3);
    });

    it('leaves the pick list as long as it is, and without the line', () => {
      const picking = new DraftEngine(snapshot).update(banning().onClock(4, 'pick').build()).frame as unknown as GoldenFrame;
      if (!picking.picks) return;
      const view = adapter.pickTile(picking.picks, picking, 'Picks für dich');
      expect(view.items.length).toBe(picking.picks.items.length);
      expect(view.even).toBeNull();
    });
  });
});
