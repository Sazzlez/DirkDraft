// The window around the app. In the Tauri shell the title bar's buttons act on the real window; in
// the browser (preview, screenshots) the same calls do nothing, so the UI never asks where it runs.

import { invoke } from '@tauri-apps/api/core';
import { listen } from '@tauri-apps/api/event';
import { getCurrentWindow } from '@tauri-apps/api/window';
import type { DataCache } from '../data/remote';

declare global {
  interface Window {
    /** Set by the shell before the page's first script (src-tauri/src/shell.rs). */
    __DIRKDRAFT_SHELL__?: { glass: 'mica' | 'none'; canGlass?: boolean };
  }
}

export const inShell = typeof window !== 'undefined' && '__TAURI_INTERNALS__' in window;

/**
 * Tells the stylesheet what is behind the page: Mica (Windows 11) shows through a transparent body,
 * Windows 10 gets the opaque fallback. Outside the shell nothing changes and the stage tint stays.
 */
export function markShell(root: HTMLElement = document.documentElement): void {
  if (!inShell) return;
  root.dataset.shell = 'tauri';
  root.dataset.glass = window.__DIRKDRAFT_SHELL__?.glass ?? 'none';
}

/** Windows 11: Mica can sit behind the window (the settings sheet offers to switch it off). */
export const canGlass = inShell && typeof window !== 'undefined' && window.__DIRKDRAFT_SHELL__?.canGlass === true;

/** Whether this start runs with Mica (the switch acts only at the next start). */
export const glassNow = inShell && typeof window !== 'undefined' && window.__DIRKDRAFT_SHELL__?.glass === 'mica';

/** Minimised by hand: nothing brings the window back on its own until the user does. */
export async function minimize(): Promise<void> {
  if (inShell) await invoke('shell_minimize');
}

/** X: the same way as Alt+F4 — into the tray, with a word the first time (shell.rs). */
export async function hideToTray(): Promise<void> {
  if (inShell) await getCurrentWindow().close();
}

/** The first close into the tray: the page shows the hint until the window goes. */
export function onTrayHint(show: () => void): void {
  if (inShell) void listen('shell://tray-hint', show);
}

/** The data files, kept by the shell (%LOCALAPPDATA%\DraftPilot\data\2.0). */
export const shellCache: DataCache = {
  async read(name) {
    try {
      return await invoke<string | null>('data_read', { name });
    } catch {
      return null;
    }
  },
  async write(name, text) {
    try {
      await invoke('data_write', { name, text });
    } catch {
      // A full disk costs the offline copy, not the data in hand.
    }
  },
};
