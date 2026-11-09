// D2: a pick strong on two or more lanes the own team still has open — the enemy sees the champion,
// not where it will play. A hint only: it changes no score.

import { readFileSync } from 'node:fs';
import { describe, expect, it } from 'vitest';
import { DraftEngine } from '../../src/core/engine';
import type { MetaSnapshot } from '../../src/core/model/metaLookup';
import { SessionBuilder } from './helpers';

const snapshot = JSON.parse(readFileSync(new URL('../golden/snapshot.json', import.meta.url), 'utf8').replace(/^﻿/, '')) as MetaSnapshot;

const LANE_NAMES: Record<string, string> = { top: 'Top', jungle: 'Jungle', mid: 'Mid', adc: 'Bot', support: 'Support' };

describe('flex picks', () => {
  it('names the open lanes a pick is strong on, and nothing in the score', () => {
    const frame = new DraftEngine(snapshot).update(new SessionBuilder().queue(420).localPlayer(0).onClock(0, 'pick').build()).frame;
    const flex = frame.picks!.items.filter((item) => item.flexLanes);

    expect(flex.length).toBeGreaterThan(0);
    for (const item of flex) {
      expect(item.flexLanes!.length).toBeGreaterThanOrEqual(2);
      expect(item.flexLanes).toContain('top');
      const chip = item.reasons.find((r) => r.text.startsWith('Flex: '))!;
      expect(chip.tone).toBe('neutral');
      for (const lane of item.flexLanes!) expect(chip.text).toContain(LANE_NAMES[lane]!);
    }
  });

  it('drops a lane once a teammate there has locked', () => {
    const open = new DraftEngine(snapshot).update(new SessionBuilder().queue(420).localPlayer(0).onClock(0, 'pick').build()).frame;
    const withJungle = open.picks!.items.find((item) => item.flexLanes?.includes('jungle'));
    expect(withJungle).toBeDefined();

    // The jungler (cell 1) locks Lee Sin: jungle is no longer open for anyone else.
    const closed = new DraftEngine(snapshot).update(new SessionBuilder().queue(420).localPlayer(0).locked(1, 64).onClock(0, 'pick').build()).frame;
    expect(closed.picks!.items.some((item) => item.flexLanes?.includes('jungle'))).toBe(false);
  });
});
