// Port of tests/DraftPilot.Core.Tests/DraftTrackerTests.cs: the tracker's draft-boundary detection.
// Consumers reset their per-draft state on isNewDraft, so a wrong boundary either leaks the previous
// draft's overrides into the next one or wipes state mid-draft.

import { describe, expect, it } from 'vitest';
import {
  CLIENT_OFFLINE,
  DraftTracker,
  type ClientStatus,
  type DraftSnapshot,
  type SessionSource,
} from '../../src/core/model/draftTracker';
import { SessionBuilder } from './helpers';

/** Feeds payloads by hand; run is never used. */
class ManualSource implements SessionSource {
  private readonly sessionListeners = new Set<(json: string | null) => void>();
  private readonly statusListeners = new Set<(status: ClientStatus) => void>();

  onSessionJson(listener: (json: string | null) => void): () => void {
    this.sessionListeners.add(listener);
    return () => this.sessionListeners.delete(listener);
  }

  onStatusChanged(listener: (status: ClientStatus) => void): () => void {
    this.statusListeners.add(listener);
    return () => this.statusListeners.delete(listener);
  }

  push(session: Record<string, unknown>): void {
    for (const listener of this.sessionListeners) listener(JSON.stringify(session));
  }

  pushEnd(): void {
    for (const listener of this.sessionListeners) listener(null);
  }

  pushStatus(status: ClientStatus): void {
    for (const listener of this.statusListeners) listener(status);
  }

  run(): Promise<void> {
    return Promise.resolve();
  }

  dispose(): Promise<void> {
    return Promise.resolve();
  }
}

function session(...locks: [cell: number, champion: number][]): Record<string, unknown> {
  const builder = new SessionBuilder().localPlayer(2).onClock(2, 'pick');
  for (const [cell, champion] of locks) builder.locked(cell, champion);
  return builder.build();
}

function track(): { source: ManualSource; tracker: DraftTracker; last: () => DraftSnapshot | null } {
  const source = new ManualSource();
  const tracker = new DraftTracker(source, 0);
  let last: DraftSnapshot | null = null;
  tracker.onChanged((snapshot) => (last = snapshot));
  return { source, tracker, last: () => last };
}

describe('DraftTrackerTests', () => {
  it('FirstFrameOfADraft_IsMarkedNew', async () => {
    const { source, tracker, last } = track();

    source.push(session([0, 122]));

    expect(last()).not.toBeNull();
    expect(last()!.isNewDraft).toBe(true);

    // The second frame of the same draft is not new.
    source.push(session([0, 122], [1, 64]));
    expect(last()!.isNewDraft).toBe(false);

    await tracker.dispose();
  });

  it('DraftToDraftWithoutAnInactiveFrame_IsMarkedNew', async () => {
    const { source, tracker, last } = track();

    // A well-progressed first draft…
    source.push(session([0, 122], [1, 64], [3, 51]));
    expect(last()!.isNewDraft).toBe(true);

    // …then the client jumps straight into a fresh champion select: fewer locked picks than
    // before, no null in between. Consumers must get the reset signal anyway.
    source.push(session([0, 266]));
    expect(last()!.isNewDraft).toBe(true);

    await tracker.dispose();
  });

  it('StatusChanges_DoNotLookLikeANewDraft', async () => {
    const { source, tracker, last } = track();

    source.push(session([0, 122]));
    source.pushStatus(CLIENT_OFFLINE);

    expect(last()).not.toBeNull();
    expect(last()!.isNewDraft).toBe(false);
    expect(last()!.state.isActive).toBe(true);

    await tracker.dispose();
  });
});
