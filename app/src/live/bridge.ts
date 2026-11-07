// Wires the shell's events (src-tauri/src/client.rs) into the live session and hands the UI a fresh
// ViewModel after every change. Also owns the data refresh (core/refresh: every 30 min, never in
// champ select or a game) and the import button's round trip (Flash always on F).

import { invoke } from '@tauri-apps/api/core';
import { listen } from '@tauri-apps/api/event';
import { DEFAULT_FLASH_SLOT, spellSlots } from '../core/loadout';
import type { HistoryEntry } from '../core/postgame';
import { DataRefresher } from '../core/refresh';
import type { ViewModel } from '../core/view';
import { loadAssets } from '../data/assets';
import { browserFetcher, loadDataSet, loadKept, type DataSet } from '../data/remote';
import { inShell, shellCache } from '../platform/shell';
import { LiveSession, type ClientStatus } from './session';

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

const HISTORY_FILE = 'history.json';

async function loadHistory(): Promise<HistoryEntry[]> {
  try {
    const parsed = JSON.parse((await shellCache.read(HISTORY_FILE)) ?? '[]') as unknown;
    return Array.isArray(parsed) ? (parsed as HistoryEntry[]) : [];
  } catch {
    return [];
  }
}

function saveHistory(history: HistoryEntry[]): void {
  // A failed write costs the list of earlier games, never the postgame on screen.
  void shellCache.write(HISTORY_FILE, JSON.stringify(history));
}

let endAsked = false;

/**
 * Reads the result once the end-of-game screen has it. It takes the client a few seconds after the
 * game closes to fill the block, so this asks every two seconds for half a minute.
 */
async function askEndOfGame(): Promise<void> {
  if (!session.needsEndOfGame || endAsked) return;
  endAsked = true;
  try {
    for (let attempt = 0; attempt < 15 && session.needsEndOfGame; attempt++) {
      const end = await invoke<{ championId: number; win: boolean; items: number[] } | null>('client_end_of_game').catch(() => null);
      if (end) {
        saveHistory(session.onEndOfGame(end));
        render();
        // The game just played belongs in the pool's recent games now.
        askPool(true);
        return;
      }
      await new Promise((resolve) => setTimeout(resolve, 2000));
    }
  } finally {
    endAsked = false;
  }
}

let poolAsked = false;

/** Mastery and recent games for "Mein Pool": once per connection, and again after each game. */
function askPool(force = false): void {
  if ((!force && !session.needsPool) || poolAsked) return;
  poolAsked = true;
  invoke<{ masteries: { championId: number; points: number }[]; games: { championId: number; lane: string; queueId: number }[] }>('client_pool_input')
    .then((input) => {
      if (input.masteries.length > 0 || input.games.length > 0) {
        session.setPoolInput(input);
        render();
      }
    })
    .catch(() => undefined)
    .finally(() => {
      poolAsked = false;
    });
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

  // The files live with the shell; the webview's fetch revalidates with its own HTTP cache.
  const refresher = new DataRefresher(
    () => loadDataSet(browserFetcher, shellCache),
    (set) => void apply(set),
  );
  refresher.start(await loadKept(shellCache));
  session.setHistory(await loadHistory());

  await listen<ClientStatus>('client://status', (e) => {
    session.onStatus(e.payload);
    if (!e.payload.clientRunning) void refresher.onPhase(null, Date.now());
    if (e.payload.socketConnected) askPool();
    render();
  });
  await listen<string>('client://phase', (e) => {
    session.onPhase(e.payload);
    void refresher.onPhase(e.payload, Date.now());
    void askEndOfGame();
    render();
  });
  await listen<string | null>('client://session', (e) => {
    session.onSession(e.payload);
    askSelectable();
    askPool();
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
  if (snapshot.status.clientRunning) askPool();
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

  // Flash on F (user's rule); a settings switch for D comes with the settings page.
  const spells = spellSlots(plan.spells, plan.currentSpells, DEFAULT_FLASH_SLOT);
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
