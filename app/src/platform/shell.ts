// The window around the app. In the Tauri shell the title bar's buttons act on the real window; in
// the browser (preview, screenshots) the same calls do nothing, so the UI never asks where it runs.

import { getCurrentWindow } from '@tauri-apps/api/window';

declare global {
  interface Window {
    /** Set by the shell before the page's first script (src-tauri/src/shell.rs). */
    __DIRKDRAFT_SHELL__?: { glass: 'mica' | 'none' };
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

export async function minimize(): Promise<void> {
  if (inShell) await getCurrentWindow().minimize();
}

/** X: into the tray, as in 1.5. Quitting is the tray's job. */
export async function hideToTray(): Promise<void> {
  if (inShell) await getCurrentWindow().hide();
}

export async function setPinned(on: boolean): Promise<void> {
  if (inShell) await getCurrentWindow().setAlwaysOnTop(on);
}
