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
/** The same as a fixed five: everyone queued with the local player. */
const fiveBlind = () => new SessionBuilder().queue(440).localPlayer(2).party(0, 1, 3, 4).onClock(2, 'pick').build();

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
    engine.update(fiveBlind());
    engine.choosePlan('teamfight');
    const frame = engine.update(fiveBlind()).frame as unknown as GoldenFrame;

    expect(adapter.comp(frame).plans!.text).toBe('Euer Plan: Teamfight (gewählt) · Ihr Plan: noch offen');
    const choice = adapter.planChoice(frame)!;
    expect(choice.chosen).toBe('Teamfight');
    expect(choice.options.filter((o) => o.active).map((o) => o.plan)).toEqual(['teamfight']);
    expect(choice.options.map((o) => o.label)).toEqual(['Teamfight', 'Pick', 'Poke', 'Protect', 'Splitpush']);
    expect(choice.options.every((o) => o.wants.length > 0)).toBe(true);

    engine.choosePlan(null);
    const cleared = adapter.planChoice(engine.update(fiveBlind()).frame as unknown as GoldenFrame)!;
    expect(cleared.chosen).toBeNull();
    expect(cleared.options.some((o) => o.active)).toBe(false);
  });

  it('offers the choice to a fixed five only: nobody makes strangers follow a plan', () => {
    const choice = (party: number[]) =>
      adapter.planChoice(new DraftEngine(snapshot).update(new SessionBuilder().queue(420).localPlayer(2).party(...party).onClock(2, 'pick').build()).frame as unknown as GoldenFrame);
    expect(choice([])).toBeNull();
    expect(choice([0])).toBeNull();
    expect(choice([0, 1, 3])).toBeNull();
    expect(choice([0, 1, 3, 4])).not.toBeNull();
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
