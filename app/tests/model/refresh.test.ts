// Automatic data refresh: only while idle, never a swap under a running draft or game.

import { describe, expect, it } from 'vitest';
import { DataRefresher, REFRESH_INTERVAL_MS } from '../../src/core/refresh';
import { emptySnapshot } from '../../src/core/model/metaLookup';
import type { DataSet } from '../../src/data/remote';

const set = (patch: string, changed = true): DataSet => ({
  meta: { ...emptySnapshot(), patch },
  builds: null,
  mayhem: null,
  situational: null,
  fresh: true,
  changed,
  problems: [],
});

function harness(answer: () => DataSet | null = () => set('new')) {
  const applied: string[] = [];
  let downloads = 0;
  let release: (() => void) | null = null;
  let gate: Promise<void> | null = null;
  const refresher = new DataRefresher(
    async () => {
      downloads++;
      if (gate) await gate;
      return answer();
    },
    (s) => applied.push(s.meta.patch),
  );
  return {
    refresher,
    applied,
    downloads: () => downloads,
    hold() {
      gate = new Promise((resolve) => (release = resolve));
    },
    release() {
      release?.();
      gate = null;
    },
  };
}

describe('automatic data refresh', () => {
  it('shows the kept copy at once and downloads when idle', async () => {
    const h = harness();
    h.refresher.start(set('kept'));
    await h.refresher.onPhase('Lobby', 0);
    expect(h.applied).toEqual(['kept', 'new']);
  });

  it('sends no request during champ select or a game', async () => {
    const h = harness();
    h.refresher.start(set('kept'));
    for (const phase of ['ReadyCheck', 'ChampSelect', 'GameStart', 'InProgress', 'WaitingForStats', 'Reconnect']) {
      await h.refresher.onPhase(phase, 0);
      await h.refresher.tick(REFRESH_INTERVAL_MS * 10);
    }
    expect(h.downloads()).toBe(0);
    expect(h.applied).toEqual(['kept']);
  });

  it('holds a download that finishes after the queue popped, and swaps it in after the game', async () => {
    const h = harness();
    h.refresher.start(set('kept'));
    h.hold();
    const running = h.refresher.onPhase('Matchmaking', 0);
    await h.refresher.onPhase('ChampSelect', 1);
    h.release();
    await running;
    expect(h.applied).toEqual(['kept']);
    expect(h.refresher.isHolding).toBe(true);

    await h.refresher.onPhase('InProgress', 2);
    expect(h.applied).toEqual(['kept']);

    await h.refresher.onPhase('EndOfGame', 3);
    expect(h.applied).toEqual(['kept', 'new']);
  });

  it('looks again only every half hour', async () => {
    const h = harness();
    h.refresher.start(set('kept'));
    await h.refresher.onPhase('Lobby', 0);
    await h.refresher.tick(REFRESH_INTERVAL_MS - 1);
    expect(h.downloads()).toBe(1);
    await h.refresher.tick(REFRESH_INTERVAL_MS);
    expect(h.downloads()).toBe(2);
  });

  it('catches up as soon as the client is idle again when the half hour ran out in a game', async () => {
    const h = harness();
    h.refresher.start(set('kept'));
    await h.refresher.onPhase('Lobby', 0);
    await h.refresher.onPhase('ChampSelect', 60_000);
    await h.refresher.onPhase('InProgress', 5 * 60_000);
    await h.refresher.tick(31 * 60_000); // due, but in game
    expect(h.downloads()).toBe(1);

    await h.refresher.onPhase('EndOfGame', 40 * 60_000);
    expect(h.downloads()).toBe(2);
  });

  it('does not re-apply unchanged data', async () => {
    const h = harness(() => set('kept', false));
    h.refresher.start(set('kept'));
    await h.refresher.onPhase(null, 0);
    expect(h.applied).toEqual(['kept']);
  });

  it('with no data at all, loads even in champ select rather than show nothing', async () => {
    const h = harness();
    h.refresher.start(null);
    await h.refresher.onPhase('ChampSelect', 0);
    await h.refresher.tick(0);
    expect(h.applied).toEqual(['new']);
  });

  it('survives a failed download', async () => {
    const h = harness(() => {
      throw new Error('offline');
    });
    h.refresher.start(set('kept'));
    await h.refresher.onPhase('Lobby', 0);
    expect(h.applied).toEqual(['kept']);
  });
});
