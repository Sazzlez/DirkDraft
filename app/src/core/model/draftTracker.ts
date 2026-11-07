// Port of DraftPilot.Core.Draft.DraftTracker (1.5), with the two pieces of DraftPilot.Core.Lcu it
// needs (ClientStatus, ISessionSource). Turns raw session payloads into a DraftState and resolves
// which seat to advise. The client sends bursts of events as a ban round resolves; they are
// coalesced so the UI updates once instead of flickering. JavaScript is single-threaded, so the
// C# locks have no counterpart here.

import { draftStateFrom, INACTIVE, type DraftState } from './draftState';
import { TurnTracker, type RecommendationTarget } from './turnTracker';

/** What we can tell the user about the connection. */
export interface ClientStatus {
  /** Whether a League install was located at all. */
  leagueFound: boolean;
  /** Whether the client is up (lockfile present and parsed). */
  clientRunning: boolean;
  /** Whether the event socket is subscribed. */
  socketConnected: boolean;
  /** Note for the status line, if any. */
  detail: string | null;
}

export const CLIENT_NOT_FOUND: ClientStatus = Object.freeze({
  leagueFound: false,
  clientRunning: false,
  socketConnected: false,
  detail: 'League-Installation nicht gefunden',
});

export const CLIENT_OFFLINE: ClientStatus = Object.freeze({
  leagueFound: true,
  clientRunning: false,
  socketConnected: false,
  detail: 'Client nicht gestartet',
});

/**
 * A source of champ-select sessions: once against the live client, once against a recording, so
 * everything downstream works without League running. The subscribe calls return their unsubscribe.
 */
export interface SessionSource {
  /** Raw /lol-champ-select/v1/session JSON, or null when champion select ended. */
  onSessionJson(listener: (json: string | null) => void): () => void;
  onStatusChanged(listener: (status: ClientStatus) => void): () => void;
  /** Runs until the signal fires. */
  run(signal: AbortSignal): Promise<void>;
  dispose(): Promise<void>;
}

/** Everything the UI needs after one change. */
export interface DraftSnapshot {
  state: DraftState;
  target: RecommendationTarget | null;
  status: ClientStatus;
  /**
   * True on the first snapshot of a fresh champion select — including the client jumping straight
   * from one draft into the next without an inactive frame in between. Consumers reset their
   * per-draft state on this rather than on their own active-flag bookkeeping, which missed that jump.
   */
  isNewDraft: boolean;
}

export class DraftTracker {
  readonly turns = new TurnTracker();

  private currentState: DraftState = INACTIVE;
  private currentTarget: RecommendationTarget | null = null;
  private currentStatus: ClientStatus = CLIENT_OFFLINE;

  private readonly listeners = new Set<(snapshot: DraftSnapshot) => void>();
  private readonly unsubscribe: (() => void)[];

  private pending: string | null = null;
  private flushTimer: ReturnType<typeof setTimeout> | null = null;

  private wasActive = false;
  private lastLocalCellId = Number.NEGATIVE_INFINITY;
  private lastProgress = 0;

  /** `debounceMs`: 100 keeps the panel calm during fast ban rounds; the tests use 0. */
  constructor(
    private readonly source: SessionSource,
    private readonly debounceMs = 100,
  ) {
    this.unsubscribe = [
      source.onSessionJson((json) => this.onSessionJson(json)),
      source.onStatusChanged((status) => this.onStatusChanged(status)),
    ];
  }

  get state(): DraftState {
    return this.currentState;
  }

  get target(): RecommendationTarget | null {
    return this.currentTarget;
  }

  get status(): ClientStatus {
    return this.currentStatus;
  }

  /** Raised after every coalesced change; returns the unsubscribe. */
  onChanged(listener: (snapshot: DraftSnapshot) => void): () => void {
    this.listeners.add(listener);
    return () => this.listeners.delete(listener);
  }

  run(signal: AbortSignal): Promise<void> {
    return this.source.run(signal);
  }

  /** Re-resolves the advised seat after the user clicked a slot. */
  refreshTarget(): void {
    this.currentTarget = this.turns.resolve(this.currentState);
    this.emit({ state: this.currentState, target: this.currentTarget, status: this.currentStatus, isNewDraft: false });
  }

  async dispose(): Promise<void> {
    for (const unsubscribe of this.unsubscribe) unsubscribe();
    this.cancelFlush();
    await this.source.dispose();
  }

  private onStatusChanged(status: ClientStatus): void {
    this.currentStatus = status;
    this.emit({ state: this.currentState, target: this.currentTarget, status, isNewDraft: false });
  }

  private onSessionJson(json: string | null): void {
    // The end of champion select should close the panel at once, not after the coalescing window.
    if (json === null) {
      this.pending = null;
      this.cancelFlush();
      this.apply(null);
      return;
    }

    this.pending = json;
    if (this.flushTimer !== null) return;

    if (this.debounceMs <= 0) {
      this.flush();
      return;
    }
    this.flushTimer = setTimeout(() => this.flush(), this.debounceMs);
  }

  private flush(): void {
    const pending = this.pending;
    this.pending = null;
    this.flushTimer = null;
    if (pending === null) return;
    this.apply(pending);
  }

  private cancelFlush(): void {
    if (this.flushTimer !== null) clearTimeout(this.flushTimer);
    this.flushTimer = null;
  }

  private apply(json: string | null): void {
    let state: DraftState;
    try {
      state = draftStateFrom(deserialize(json));
    } catch {
      // Safety net for a payload the reader cannot digest. Escaping here would kill the socket's
      // receive loop; keeping the last good state on screen is strictly better.
      return;
    }

    // A fresh champion select must not inherit the previous draft's manual selection — also when
    // the client jumps from one draft into the next without a null in between. Within one draft
    // the ban/lock count only grows, so a shrink means a new session; so does a different own seat.
    const progress = state.unavailable.size;
    const isNewDraft =
      state.isActive && (!this.wasActive || progress < this.lastProgress || state.localCellId !== this.lastLocalCellId);

    if (isNewDraft) this.turns.reset();

    this.wasActive = state.isActive;
    this.lastProgress = progress;
    this.lastLocalCellId = state.localCellId;

    this.currentState = state;
    this.currentTarget = this.turns.resolve(state);
    this.emit({ state, target: this.currentTarget, status: this.currentStatus, isNewDraft });
  }

  private emit(snapshot: DraftSnapshot): void {
    for (const listener of this.listeners) listener(snapshot);
  }
}

/** A shape change on Riot's side degrades the display instead of taking the tool down. */
function deserialize(json: string | null): unknown {
  if (json === null || json.trim() === '') return null;
  try {
    return JSON.parse(json) as unknown;
  } catch {
    return null;
  }
}
