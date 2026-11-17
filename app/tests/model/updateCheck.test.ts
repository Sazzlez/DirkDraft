// The update check's clock (core/updateCheck.ts): every six hours, and never while a draft or a
// game is on.

import { describe, expect, it } from 'vitest';
import { releaseUrl, UPDATE_INTERVAL_MS, UpdateChecker } from '../../src/core/updateCheck';

const HOUR = 60 * 60 * 1000;

function checker(): { updates: UpdateChecker; checks: () => number } {
  let count = 0;
  const updates = new UpdateChecker(async () => {
    count++;
  });
  return { updates, checks: () => count };
}

describe('the update check', () => {
  it('asks at the first idle moment and then every six hours', async () => {
    const { updates, checks } = checker();
    await updates.onPhase(null, 0);
    expect(checks()).toBe(1);

    await updates.tick(UPDATE_INTERVAL_MS - 1);
    expect(checks()).toBe(1);
    await updates.tick(UPDATE_INTERVAL_MS);
    expect(checks()).toBe(2);
    await updates.onPhase('Lobby', UPDATE_INTERVAL_MS + HOUR);
    expect(checks()).toBe(2);
    await updates.tick(2 * UPDATE_INTERVAL_MS);
    expect(checks()).toBe(3);
  });

  it('never asks in ready check, champ select, loading screen or game', async () => {
    for (const phase of ['ReadyCheck', 'ChampSelect', 'GameStart', 'InProgress', 'Reconnect', 'WaitingForStats', 'PreEndOfGame']) {
      const { updates, checks } = checker();
      // Started into the phase: not even the first check.
      await updates.onPhase(phase, 0);
      await updates.tick(HOUR);
      await updates.tick(10 * UPDATE_INTERVAL_MS);
      expect(checks(), phase).toBe(0);
    }
  });

  it('holds a check that came due during a game until the game is over', async () => {
    const { updates, checks } = checker();
    await updates.onPhase('Lobby', 0);
    expect(checks()).toBe(1);

    await updates.onPhase('ChampSelect', 5 * HOUR);
    await updates.tick(6 * HOUR);
    await updates.onPhase('InProgress', 6 * HOUR + 1);
    await updates.tick(7 * HOUR);
    expect(checks()).toBe(1);

    await updates.onPhase('EndOfGame', 7 * HOUR + 1);
    expect(checks()).toBe(2);
    // And the six hours count from that check.
    await updates.tick(12 * HOUR);
    expect(checks()).toBe(2);
    await updates.tick(13 * HOUR + 1);
    expect(checks()).toBe(3);
  });

  it('runs one check at a time and survives a failed one', async () => {
    let release!: () => void;
    let count = 0;
    const updates = new UpdateChecker(() => {
      count++;
      if (count === 1) return new Promise<void>((resolve) => (release = resolve));
      return Promise.reject(new Error('offline'));
    });

    const first = updates.onPhase(null, 0);
    void updates.tick(UPDATE_INTERVAL_MS);
    expect(count).toBe(1);
    release();
    await first;

    await expect(updates.tick(2 * UPDATE_INTERVAL_MS)).resolves.toBeUndefined();
    expect(count).toBe(2);
  });

  it('links to the release of that version in the repository', () => {
    expect(releaseUrl('2.3.0')).toBe('https://github.com/Sazzlez/DirkDraft/releases/tag/v2.3.0');
    expect(releaseUrl('2.3.0/../../x')).toMatch(/^https:\/\/github\.com\/Sazzlez\/DirkDraft\/releases\/tag\/v2\.3\.0%2F/);
  });
});
