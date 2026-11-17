// When the app asks GitHub for a newer version: at the first idle moment and then every six hours,
// by the same rule as the data (core/refresh.ts) — never in ready check, champ select, loading
// screen or game. Until 17.11.2026 there was one check at start: a window left open for days never
// heard of a release.

import { isIdle } from './refresh';

export const UPDATE_INTERVAL_MS = 6 * 60 * 60 * 1000;

export class UpdateChecker {
  private phase: string | null = null;
  private lastCheck = Number.NEGATIVE_INFINITY;
  private running: Promise<void> | null = null;

  constructor(
    private readonly check: () => Promise<void>,
    private readonly intervalMs = UPDATE_INTERVAL_MS,
  ) {}

  /** The client's gameflow phase changed (null: client not running). */
  onPhase(phase: string | null, now: number): Promise<void> {
    this.phase = phase;
    return this.tick(now);
  }

  /** Called on a timer; checks when idle and due. A check that was due during a game waits for its end. */
  tick(now: number): Promise<void> {
    if (this.running) return this.running;
    if (!isIdle(this.phase) || now - this.lastCheck < this.intervalMs) return Promise.resolve();
    this.lastCheck = now;
    this.running = this.check()
      .catch(() => undefined)
      .finally(() => {
        this.running = null;
      });
    return this.running;
  }
}

/** The page of a release on GitHub, for "Was ist neu". The shell opens nothing outside this repository. */
export const releaseUrl = (version: string): string => `https://github.com/Sazzlez/DirkDraft/releases/tag/v${encodeURIComponent(version)}`;
