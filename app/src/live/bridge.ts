// Wires the shell's events (src-tauri/src/client.rs) into the live session and hands the UI a fresh
// ViewModel after every change. Also owns the data refresh (core/refresh: every 30 min, never in
// champ select or a game) and the import button's round trip (Flash on the key from the settings).

import { invoke } from '@tauri-apps/api/core';
import { listen } from '@tauri-apps/api/event';
import { spellSlots } from '../core/loadout';
import { DataRefresher } from '../core/refresh';
import type { ViewModel } from '../core/view';
import { loadAssets } from '../data/assets';
import { browserFetcher, loadDataSet, loadKept, type DataSet } from '../data/remote';
import { inShell, shellCache } from '../platform/shell';
import { LiveSession, type ClientStatus } from './session';
import { loadSettings, onSettings, settings } from './settings.svelte';

interface Snapshot {
  status: ClientStatus;
  phase: string | null;
  session: string | null;
  game: string | null;
}

interface ImportOutcome {
  ok: boolean;
  message: string;
  confirm: string | null;
}

const session = new LiveSession();
let notify: (view: ViewModel) => void = () => {};
let ticker: ReturnType<typeof setInterval> | null = null;

function render(): void {
  notify(session.view());
}

let selectableAsked = false;

/**
 * Asks the client which champions the player may pick, once per draft. The list can come back empty
 * at the very start of champion select; then the next session event asks again (as 1.5 does).
 */
function askSelectable(): void {
  if (!session.needsSelectable || selectableAsked) return;
  selectableAsked = true;
  invoke<number[]>('client_pickable')
    .then((ids) => {
      if (ids.length > 0) {
        session.setSelectable(ids);
        render();
      }
    })
    .catch(() => undefined)
    .finally(() => {
      selectableAsked = false;
    });
}

async function apply(set: DataSet): Promise<void> {
  await loadAssets(set.meta.patch).catch(() => undefined);
  session.setData(set);
  render();
}

/** Starts listening; `onView` receives every new screen. Only in the shell. */
export async function startLive(onView: (view: ViewModel) => void): Promise<void> {
  if (!inShell) return;
  notify = onView;

  onSettings((s) => {
    session.setShowUnowned(s.showUnowned);
    askSelectable();
    render();
  });
  await loadSettings();

  // The files live with the shell; the webview's fetch revalidates with its own HTTP cache.
  const refresher = new DataRefresher(
    () => loadDataSet(browserFetcher, shellCache),
    (set) => void apply(set),
  );
  refresher.start(await loadKept(shellCache));

  await listen<ClientStatus>('client://status', (e) => {
    session.onStatus(e.payload);
    if (!e.payload.clientRunning) void refresher.onPhase(null, Date.now());
    render();
  });
  await listen<string>('client://phase', (e) => {
    session.onPhase(e.payload);
    void refresher.onPhase(e.payload, Date.now());
    render();
  });
  await listen<string | null>('client://session', (e) => {
    session.onSession(e.payload);
    askSelectable();
    render();
  });
  await listen<string | null>('game://live', (e) => {
    session.onGame(e.payload);
    render();
  });

  // Whatever happened before the page loaded.
  const snapshot = await invoke<Snapshot>('client_snapshot');
  session.onStatus(snapshot.status);
  if (snapshot.phase) session.onPhase(snapshot.phase);
  if (snapshot.session) {
    session.onSession(snapshot.session);
    askSelectable();
  }
  if (snapshot.game) session.onGame(snapshot.game);
  void refresher.onPhase(snapshot.status.clientRunning ? snapshot.phase : null, Date.now());
  render();

  // The champion select clock counts down between events; the refresher checks whether it is due.
  ticker ??= setInterval(() => {
    void refresher.tick(Date.now());
    if (session.view().board) render();
  }, 1000);
}

/**
 * The import button: rune page and spells for the build on screen. Asks before a foreign rune page
 * has to go (by its name) and writes nothing until the user agreed.
 */
export async function runImport(): Promise<string> {
  if (!inShell) return 'Übernehmen geht nur in der App mit laufendem Client.';
  const plan = session.importPlan();
  if (!plan) return 'Für diesen Champion gibt es noch keinen Build zum Übernehmen.';

  const spells = spellSlots(plan.spells, plan.currentSpells, settings.flashKey === 'D' ? 0 : 1);
  const runes = plan.runes;
  const request = {
    page: runes
      ? {
          title: plan.title,
          primaryStyleId: runes[0],
          subStyleId: runes[5],
          selectedPerkIds: [...runes.slice(1, 5), ...runes.slice(6, 8), ...runes.slice(8, 11)],
        }
      : null,
    spells,
    replace: null as string | null,
  };
  if (!request.page && !request.spells) return 'Runen und Zauber sind schon so gesetzt.';

  let outcome = await invoke<ImportOutcome>('client_import', { request });
  if (outcome.confirm) {
    const agreed = window.confirm(
      `Alle Runenseiten-Plätze sind belegt. Für die neue Seite müsste deine Seite „${outcome.confirm}“ gelöscht werden.\n\nLöschen und Runen übernehmen?`,
    );
    if (!agreed) return 'Abgebrochen — keine Seite gelöscht.';
    outcome = await invoke<ImportOutcome>('client_import', { request: { ...request, replace: outcome.confirm } });
  }
  return outcome.message;
}
