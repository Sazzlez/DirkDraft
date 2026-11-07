// The update pill: one check at start (updater.rs), "Version X ist da", and a click that downloads,
// installs and restarts. Nothing happens without that click.

import { invoke } from '@tauri-apps/api/core';
import { inShell } from '../platform/shell';

export const update = $state<{ available: string | null; current: string | null; busy: boolean; error: string | null }>({
  available: null,
  current: null,
  busy: false,
  error: null,
});

export async function checkForUpdate(): Promise<void> {
  if (!inShell) return;
  try {
    const status = await invoke<{ installed: boolean; current: string | null; available: string | null }>('update_check');
    update.current = status.current;
    update.available = status.available;
  } catch {
    // Offline or GitHub down: no pill, the next start asks again.
  }
}

export async function applyUpdate(): Promise<void> {
  if (!inShell || update.busy) return;
  update.busy = true;
  update.error = null;
  try {
    // Returns only on failure: on success the app restarts into the new version.
    await invoke('update_apply');
  } catch (e) {
    update.error = String(e);
  } finally {
    update.busy = false;
  }
}
