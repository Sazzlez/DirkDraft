// The comp tile's top line: what the own team still lacks, for the seat about to pick. That seat's
// own hover must not count — hovering a tank would otherwise make "Frontline" vanish before the pick.

import { readFileSync } from 'node:fs';
import { describe, expect, it } from 'vitest';
import { DraftEngine } from '../../src/core/engine';
import type { MetaSnapshot } from '../../src/core/model/metaLookup';
import { GoldenAdapter, gapText, type GoldenFrame } from '../../src/data/golden';
import { SessionBuilder } from './helpers';

const snapshot = JSON.parse(readFileSync(new URL('../golden/snapshot.json', import.meta.url), 'utf8').replace(/^﻿/, '')) as MetaSnapshot;
const adapter = new GoldenAdapter(snapshot.champions);

const LeeSin = 64,
  Ahri = 103,
  Jinx = 222,
  Malphite = 54,
  Leona = 89,
  Zed = 238,
  Yasuo = 157,
  Draven = 119;

const frameOf = (session: Record<string, unknown>) => new DraftEngine(snapshot).update(session).frame as unknown as GoldenFrame;

describe('comp gap', () => {
  it("names what the team lacks without the picking seat's own hover", () => {
    const frame = frameOf(
      new SessionBuilder().queue(420).localPlayer(0).locked(1, LeeSin).locked(2, Ahri).locked(3, Jinx).hovering(0, Malphite).onClock(0, 'pick').build(),
    );

    // The whole team, hover included, has its tank; the seat about to pick does not have one yet.
    expect(frame.comp.allies.findings.map((f) => f.issue)).not.toContain('noFrontline');
    expect(frame.gap!.findings.map((f) => f.issue)).toContain('noFrontline');
    const gap = adapter.comp(frame).gap!;
    expect(gap.lead).toBe('Eurem Team fehlt noch');
    expect(gap.items).toContain('Frontline');
    expect(gap.tone).toBe('warn');
    expect(gapText(gap)).toMatch(/^Eurem Team fehlt noch: .*Frontline/);
  });

  it('lists every gap, most severe first, in the names players use', () => {
    const frame = frameOf(
      new SessionBuilder().queue(420).localPlayer(4).locked(0, Yasuo).locked(1, LeeSin).locked(2, Zed).locked(3, Draven).onClock(4, 'pick').build(),
    );
    const issues = [...frame.gap!.findings].sort((a, b) => b.severity - a.severity).map((f) => f.issue);
    const names: Record<string, string> = {
      noMagicDamage: 'AP-Schaden',
      noPhysicalDamage: 'AD-Schaden',
      noFrontline: 'Frontline',
      noEngage: 'Engage',
      noPeel: 'Peel',
      littleCrowdControl: 'CC',
      allMelee: 'Range',
      littleWaveclear: 'Waveclear',
    };

    expect(issues.length).toBeGreaterThan(1);
    expect(adapter.comp(frame).gap!.items).toEqual(issues.map((issue) => names[issue]));
  });

  it('speaks of the whole team once everyone has locked, and of the enemy below', () => {
    const frame = frameOf(
      new SessionBuilder()
        .queue(420)
        .localPlayer(0)
        .locked(0, Malphite)
        .locked(1, LeeSin)
        .locked(2, Ahri)
        .locked(3, Jinx)
        .locked(4, Leona)
        .locked(5, Yasuo)
        .locked(6, Zed)
        .locked(7, Draven)
        .phase('FINALIZATION')
        .build(),
    );
    const view = adapter.comp(frame);

    expect(frame.gap).toBeNull();
    // A full team without gaps; no "noch" or "bisher" once nobody is left to pick.
    expect(view.gap!.lead).toBe('Eurem Team fehlt nichts');
    expect(gapText(view.enemyGap!)).toMatch(/^Dem Gegnerteam fehlt: /);
  });

  it('names both plans and what each side wants', () => {
    const Xerath = 101,
      Ziggs = 115,
      Ezreal = 81,
      Thresh = 412,
      Blitzcrank = 53,
      Ahri103 = Ahri;
    const frame = frameOf(
      new SessionBuilder()
        .queue(420)
        .localPlayer(0)
        .locked(1, Xerath)
        .locked(2, Ziggs)
        .locked(3, Ezreal)
        .locked(5, Thresh)
        .locked(6, Blitzcrank)
        .locked(7, Ahri103)
        .onClock(0, 'pick')
        .build(),
    );

    expect(adapter.comp(frame).plans).toEqual({
      text: 'Euer Plan: Poke · Ihr Plan: Pick',
      detail: 'Ihr wollt aus der Distanz Schaden machen und Türme belagern, bevor gekämpft wird; sie wollen einzelne Gegner abfangen, bevor es zum Kampf kommt.',
    });
  });

  it('says nothing about the own team before three champions are known', () => {
    const frame = frameOf(new SessionBuilder().queue(420).localPlayer(0).locked(1, LeeSin).onClock(0, 'pick').build());

    expect(adapter.comp(frame).gap).toBeNull();
  });
});
