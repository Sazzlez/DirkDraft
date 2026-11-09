// The settings the page shows and changes, kept by the shell in 1.5's settings.json (shell.rs,
// settings.rs). Reactive: the settings sheet and the title bar's pin read the same state.
// Outside the shell (browser preview) the defaults apply and changes live until reload.

import { invoke } from '@tauri-apps/api/core';
import { inShell } from '../platform/shell';

export interface AppSettings {
  autoShowOnChampSelect: boolean;
  autoShowOnGameStart: boolean;
  autoHideAfterChampSelect: boolean;
  keepOnTop: boolean;
  /** 1.5: suggest champions the account does not own too. */
  showUnowned: boolean;
  /** The key Flash goes on when spells are taken over. */
  flashKey: 'D' | 'F';
  /** Mica on Windows 11; read by the shell at start. */
  glassEffect: boolean;
  /** The last drafts and games recorded locally (scrubbed) for finding faults. */
  diagnostics: boolean;
  /** 2.1: plan the own team's open seats together (Flex with a fixed five). */
  teamMode: boolean;
}

/** 1.5's defaults, and F for Flash (user's rule). */
export const DEFAULT_SETTINGS: AppSettings = {
  autoShowOnChampSelect: true,
  autoShowOnGameStart: true,
  autoHideAfterChampSelect: false,
  keepOnTop: false,
  showUnowned: false,
  flashKey: 'F',
  glassEffect: true,
  diagnostics: false,
  teamMode: false,
};

export const settings = $state<AppSettings>({ ...DEFAULT_SETTINGS });

const listeners = new Set<(s: AppSettings) => void>();

/** Called with the settings after loading and after every change. */
export function onSettings(listener: (s: AppSettings) => void): void {
  listeners.add(listener);
}

function notify(): void {
  const snapshot = { ...settings };
  for (const listener of listeners) listener(snapshot);
}

export async function loadSettings(): Promise<void> {
  if (inShell) {
    try {
      Object.assign(settings, await invoke<AppSettings>('settings_get'));
    } catch {
      // Defaults stay; the shell refuses nothing it could read.
    }
  }
  notify();
}

export async function changeSetting<K extends keyof AppSettings>(key: K, value: AppSettings[K]): Promise<void> {
  settings[key] = value;
  if (inShell) {
    // The pin also has to act on the window right away.
    if (key === 'keepOnTop') await invoke('shell_set_pinned', { on: value });
    else await invoke('settings_set', { key, value });
  }
  notify();
}
