// When the data may be downloaded and when new numbers may replace the ones on screen. Never during
// champ select or a game: no request goes out there, and a download that was already running when
// the queue popped is held back and swapped in only once the client is idle again — numbers never
// change under a draft in progress.

import type { DataSet } from '../data/remote';

/**
 * Client states in which downloading and swapping are fine: client closed (null), lobby, queue,
 * end of game. Ready check, champ select, loading screen, game and reconnect are not.
 */
const IDLE = new Set(['None', 'Lobby', 'Matchmaking', 'CheckedIntoTournament', 'EndOfGame', 'TerminatedInError', 'FailedToLaunch']);

export const isIdle = (phase: string | null): boolean => phase === null || IDLE.has(phase);

/** How often an idle client looks for new data; the collector publishes every six hours. */
export const REFRESH_INTERVAL_MS = 30 * 60 * 1000;

/**
 * Without any numbers yet (first start, offline): try again after a minute, not after half an hour —
 * the window has nothing to show until a download works.
 */
export const FIRST_RETRY_MS = 60 * 1000;

export class DataRefresher {
  private phase: string | null = null;
  private pending: DataSet | null = null;
  private lastAttempt = Number.NEGATIVE_INFINITY;
  private running: Promise<void> | null = null;
  private hasData = false;

  constructor(
    private readonly download: () => Promise<DataSet | null>,
    private readonly apply: (set: DataSet) => void,
    private readonly intervalMs = REFRESH_INTERVAL_MS,
  ) {}

  /** The kept copy at start-up (no network), shown at once whatever the client is doing. */
  start(kept: DataSet | null): void {
    if (!kept) return;
    this.hasData = true;
    this.apply(kept);
  }

  /** The client's gameflow phase changed (null: client not running). */
  onPhase(phase: string | null, now: number): Promise<void> {
    this.phase = phase;
    if (!isIdle(phase)) return Promise.resolve();
    this.flush();
    return this.tick(now);
  }

  /** Called on a timer; downloads when idle and due. */
  tick(now: number): Promise<void> {
    if (this.running) return this.running;
    // The one exception: with no numbers at all there is nothing a download could change under the
    // draft — without it a first start in champ select would show nothing for the whole game.
    const allowed = isIdle(this.phase) || !this.hasData;
    const wait = this.hasData ? this.intervalMs : Math.min(this.intervalMs, FIRST_RETRY_MS);
    if (!allowed || now - this.lastAttempt < wait) return Promise.resolve();
    this.lastAttempt = now;
    this.running = this.refresh().finally(() => {
      this.running = null;
    });
    return this.running;
  }

  /** Whether new numbers are waiting for the draft or game to end. */
  get isHolding(): boolean {
    return this.pending !== null;
  }

  private async refresh(): Promise<void> {
    let set: DataSet | null = null;
    try {
      set = await this.download();
    } catch {
      return;
    }
    if (!set || (this.hasData && !set.changed)) return;
    if (isIdle(this.phase) || !this.hasData) {
      this.hasData = true;
      this.pending = null;
      this.apply(set);
    } else {
      this.pending = set;
    }
  }

  private flush(): void {
    if (!this.pending) return;
    const set = this.pending;
    this.pending = null;
    this.hasData = true;
    this.apply(set);
  }
}
