// D4: the plan the team chooses before the draft. Picks that bring it get a small pro inside the
// comp cap; without a choice nothing changes (B3's detection stays a hint). The choice lives only as
// long as the draft.

import { readFileSync } from 'node:fs';
import { describe, expect, it } from 'vitest';
import { DraftEngine } from '../../src/core/engine';
import type { MetaSnapshot } from '../../src/core/model/metaLookup';
import { COMP_SCALE } from '../../src/core/model/scoreModel';
import { GoldenAdapter, type GoldenFrame } from '../../src/data/golden';
import { SessionBuilder } from './helpers';

const snapshot = JSON.parse(readFileSync(new URL('../golden/snapshot.json', import.meta.url), 'utf8').replace(/^\uFEFF/, '')) as MetaSnapshot;
const adapter = new GoldenAdapter(snapshot.champions);

/** Mid on the clock, nobody shown: a blind mid pick. */
const midBlind = () => new SessionBuilder().queue(420).localPlayer(2).onClock(2, 'pick').build();

describe('chosen plan', () => {
  it('gives the picks that bring it a small pro, and nothing without a choice', () => {
    const plain = new DraftEngine(snapshot).update(midBlind()).frame;
    const engine = new DraftEngine(snapshot);
    engine.update(midBlind());
    engine.choosePlan('poke');
    const chosen = engine.update(midBlind()).frame;

    const chip = 'passt zum gewählten Plan (Poke)';
    expect(plain.picks!.items.some((i) => i.reasons.some((r) => r.text === chip))).toBe(false);
    const fitting = chosen.picks!.items.filter((i) => i.reasons.some((r) => r.text === chip));
    expect(fitting.length).toBeGreaterThan(0);
    for (const item of fitting) {
      const comp = item.breakdown.find((t) => t.kind === 'composition')!.logOdds!;
      expect(comp).toBeCloseTo(COMP_SCALE * 0.2, 10);
    }
    expect(chosen.chosenPlan).toBe('poke');
  });

  it('names the chosen plan in the comp tile and lets the second click clear it', () => {
    const engine = new DraftEngine(snapshot);
    engine.update(midBlind());
    engine.choosePlan('teamfight');
    const view = adapter.comp(engine.update(midBlind()).frame as unknown as GoldenFrame);

    expect(view.plans!.text).toBe('Euer Plan: Teamfight (gewählt) · Ihr Plan: noch offen');
    expect(view.planChoice!.filter((o) => o.active).map((o) => o.plan)).toEqual(['teamfight']);

    engine.choosePlan(null);
    expect(adapter.comp(engine.update(midBlind()).frame as unknown as GoldenFrame).planChoice!.some((o) => o.active)).toBe(false);
  });

  it('warns in the bans what threatens the chosen plan', () => {
    const engine = new DraftEngine(snapshot);
    const bans = () => new SessionBuilder().queue(420).localPlayer(0).onClock(0, 'ban').build();
    engine.update(bans());
    engine.choosePlan('teamfight');
    const frame = engine.update(bans()).frame;

    expect(frame.bans!.items.some((i) => i.reasons.some((r) => r.text === 'bedroht euren Teamfight-Plan'))).toBe(true);
  });

  it('is gone with the draft', () => {
    const engine = new DraftEngine(snapshot);
    engine.update(midBlind());
    engine.choosePlan('pick');
    engine.update(null);
    expect(engine.update(midBlind()).frame.chosenPlan).toBeNull();
  });
});
