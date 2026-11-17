// Wires the shell's events (src-tauri/src/client.rs) into the live session and hands the UI a fresh
// ViewModel after every change. Also owns the data refresh (core/refresh: every 30 min, never in
// champ select or a game) and the import button's round trip (Flash on the key from the settings).

import { getVersion } from '@tauri-apps/api/app';
import { invoke } from '@tauri-apps/api/core';
import { listen } from '@tauri-apps/api/event';
import { spellSlots } from '../core/loadout';
import type { Plan } from '../core/model/compAnalyzer';
import { DataRefresher } from '../core/refresh';
import type { Lane, ViewModel } from '../core/view';
import { loadAssets, setAssetSource, type AssetNames } from '../data/assets';
import { DataStore, type DataSet } from '../data/remote';
import { inShell, shellSource } from '../platform/shell';
import { LiveSession, type ClientStatus } from './session';
import { loadSettings, onSettings, settings } from './settings.svelte';
import { checkForUpdate, update } from './update.svelte';

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

let lastDiagnostic: string | null = null;

function render(): void {
  notify(session.view());
  // What the game view decided, once per change: situations seen and suggestions shown, item ids only.
  if (session.diagnosticLine && session.diagnosticLine !== lastDiagnostic) {
    lastDiagnostic = session.diagnosticLine;
    logLine('info', lastDiagnostic);
  }
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

let patchAsked = false;

/**
 * Asks the client which patch it runs, once per connection: data from an older patch widen the
 * error bars and get a word in the draft. A client still starting answers nothing; the next
 * status or phase event asks again.
 */
function askPatch(): void {
  if (!session.status?.clientRunning || session.hasClientPatch || patchAsked) return;
  patchAsked = true;
  invoke<string | null>('client_game_version')
    .then((version) => {
      if (version) {
        session.setClientPatch(version);
        render();
        void ensureAssets();
      }
    })
    .catch(() => undefined)
    .finally(() => {
      patchAsked = false;
    });
}

/** The patch of the numbers in hand: what the tables are loaded for while the client has not said its own. */
let dataPatch: string | null = null;
let shownAssets: AssetNames | null = null;

/** The version the names, prices and pictures should be of: the client's own, else the data's. */
const wantedAssets = (): string | null => session.clientVersion ?? dataPatch;

/**
 * Loads the tables for the wanted version where they are not loaded yet, and shows them. Called at
 * every occasion — new numbers, the client's patch, every data check — so a start without a network
 * gets its names once there is one, and a client that patched gets the new tables.
 */
function ensureAssets(): Promise<void> {
  const wanted = wantedAssets();
  if (!wanted) return Promise.resolve();
  return loadAssets(wanted).then(
    (names) => {
      if (names === shownAssets) return;
      shownAssets = names;
      render();
    },
    () => undefined,
  );
}

async function apply(set: DataSet): Promise<void> {
  dataPatch = set.meta.patch;
  await ensureAssets();
  session.setData(set);
  // Which numbers the suggestions came from: a later check needs the same file's date.
  logLine(
    'info',
    `Daten: Patch ${set.meta.patch}, meta ${set.meta.dataAsOfUtc ?? set.meta.builtAtUtc}, builds ${set.builds?.collectedAtUtc ?? '-'}, ` +
      `situational ${set.situational?.collectedAtUtc ?? '-'}, aram ${set.aram?.collectedAtUtc ?? '-'}`,
  );
  render();
}

/** One line into the shell's log file (logfile.rs); never names, never throws. */
function logLine(level: 'info' | 'warn' | 'error', message: string): void {
  void invoke('shell_log', { level, message }).catch(() => undefined);
}

/** Starts listening; `onView` receives every new screen. Only in the shell. */
export async function startLive(onView: (view: ViewModel) => void): Promise<void> {
  if (!inShell) return;
  notify = onView;
  // Script errors would otherwise vanish with the release build's closed developer tools.
  window.addEventListener('error', (e) => logLine('error', `${e.message} (${e.filename}:${e.lineno})`));
  window.addEventListener('unhandledrejection', (e) => logLine('error', `Unbehandelt: ${String(e.reason)}`));

  onSettings((s) => {
    session.setShowUnowned(s.showUnowned);
    session.setTeamMode(s.teamMode);
    askSelectable();
    render();
  });
  await loadSettings();

  // Data Dragon's tables come from the shell too (ddragon.rs): kept on disk per version.
  setAssetSource(async (wanted, language) => {
    const tables = await invoke<{ version: string; champion: string; item: string; summoner: string; runes: string }>('assets_load', { patch: wanted, language });
    return {
      version: tables.version,
      champion: JSON.parse(tables.champion) as unknown,
      item: JSON.parse(tables.item) as unknown,
      summoner: JSON.parse(tables.summoner) as unknown,
      runes: JSON.parse(tables.runes) as unknown,
    };
  });

  // The shell fetches and keeps the files; the store reads and parses only what is new.
  const store = new DataStore(shellSource);
  const refresher = new DataRefresher(
    async () => {
      // The same occasion for the tables: missing after an offline start, or of an older patch.
      void ensureAssets();
      // A failed download is only worth a word while there are no numbers at all.
      const set = await store.refresh().catch(() => null);
      session.setDataFailed(!set);
      if (!set) logLine('warn', 'Daten: keine brauchbare meta.json (offline?)');
      else for (const problem of set.problems) logLine('warn', `Daten: ${problem}`);
      if (!set) render();
      return set;
    },
    (set) => void apply(set),
  );
  refresher.start(await store.loadKept().catch(() => null));

  await listen<ClientStatus>('client://status', (e) => {
    session.onStatus(e.payload);
    if (!e.payload.clientRunning) {
      void refresher.onPhase(null, Date.now());
      // The client is gone and its patch with it: the tables of the numbers' patch again.
      void ensureAssets();
    }
    askPatch();
    render();
  });
  await listen<string>('client://phase', (e) => {
    session.onPhase(e.payload);
    void refresher.onPhase(e.payload, Date.now());
    askPatch();
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
  askPatch();
  render();

  // The version for the lobby; the title bar's pill reads the same check (live/update).
  void (async () => {
    await checkForUpdate();
    session.setVersion(update.current ?? (await getVersion().catch(() => null)), update.available);
    render();
  })();

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
export async function runImport(confirm: (pageName: string) => Promise<boolean>): Promise<string> {
  if (!inShell) return 'Übernehmen geht nur in der App mit laufendem Client.';
  const plan = session.importPlan();
  if (typeof plan === 'string') return plan;

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
  if (!request.page && !request.spells) {
    return runes ? 'Runen und Zauber sind schon so gesetzt.' : 'Für diesen Build liegen keine Runen vor; die Zauber sind schon so gesetzt.';
  }

  let outcome = await invoke<ImportOutcome>('client_import', { request });
  if (outcome.confirm) {
    // Asked inside the window (live/ImportButton), by the page's own name; nothing is written before.
    if (!(await confirm(outcome.confirm))) return 'Abgebrochen — keine Seite gelöscht.';
    outcome = await invoke<ImportOutcome>('client_import', { request: { ...request, replace: outcome.confirm } });
  }
  return outcome.message;
}

/** A click on an ally seat on the board. */
export function chooseSeat(cellId: number): void {
  if (!inShell) return;
  session.selectSeat(cellId);
  render();
}

/** D4: the plan for this draft from the comp tile; null gives it back to the detection. */
export function choosePlan(plan: Plan | null): void {
  if (!inShell) return;
  session.choosePlan(plan);
  render();
}

/** The lane picker on a seat; null gives the lane back to the prediction. */
export function chooseLane(cellId: number, lane: Lane | null): void {
  if (!inShell) return;
  session.setManualLane(cellId, lane);
  render();
}
