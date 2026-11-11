// The live session: what the client and the game report, turned into the same ViewModel the scenes
// show. Pure state and computation — the events come in through live/bridge.ts, so a test can feed
// recorded sessions and get the screen the window would show.
//
// Left out on the user's word (07.11.2026): a postgame and "Mein Pool".

import { DraftEngine } from '../core/engine';
import { gameModel, championIdOf, enemyItemsOf, isBoots, isFinished, ownsItem, type LiveGameData } from '../core/game';
import { aramBuildFile, aramStatsFrom } from '../core/aram';
import { Lane, laneFromKey } from '../core/model/lane';
import { MetaLookup } from '../core/model/metaLookup';
import { TraitTable } from '../core/model/traits';
import { momentOf, type GameflowPhase } from '../core/moment';
import { aramPlan, riotPlan, type RiotBuildPlan } from '../core/riotBuilds';
import { SLOT, SituationTracker, SwapTracker, sameStateEdge, situationalBuild, situationalSuggestions, situationsOf, type RatedSuggestion, type Slot } from '../core/situational';
import { LANE_NAMES, type BuildView, type ChampionRef, type DataStatus, type Lane as ViewLane, type ViewModel } from '../core/view';
import { assets } from '../data/assets';
import { aramBuild, buildFromRiot } from '../data/builds';
import { gameView } from '../data/gameView';
import { GoldenAdapter, type GoldenFrame } from '../data/golden';
import { dataLine, dataLineShort, type DataSet } from '../data/remote';
import { draftStateFrom } from '../core/model/draftState';
import { patchesBetween, patchLine } from '../core/model/patchVersion';
import type { Plan } from '../core/model/compAnalyzer';
import { TankShred } from '../core/model/tankShred';
import { draftSteps, simultaneousTurn } from '../core/steps';

export interface ClientStatus {
  leagueFound: boolean;
  clientRunning: boolean;
  socketConnected: boolean;
}

/** What the import button needs from the plan on screen. */
export interface ImportPlan {
  title: string;
  runes: number[] | null;
  spells: number[];
  /** The spells the player has chosen right now (slot D, slot F); [0, 0] when unknown. */
  currentSpells: [number, number];
}

/** Which builds apply: lane builds (builds.json) or the ARAM count (aram.json, also for Mayhem). */
type Mode = 'lanes' | 'aram' | 'mayhem';

const modeOf = (queue: string): Mode => (queue === 'aramMayhem' ? 'mayhem' : queue === 'aram' ? 'aram' : 'lanes');

interface Picked {
  championId: number;
  lane: Lane;
  opponentId: number;
  mode: Mode;
}

export class LiveSession {
  status: ClientStatus | null = null;
  phase: GameflowPhase | null = null;

  private data: DataSet | null = null;
  private meta: MetaLookup | null = null;
  private engine: DraftEngine | null = null;
  private adapter = new GoldenAdapter([]);
  private readonly traits = TraitTable.bundled();

  private session: Record<string, unknown> | null = null;
  private sessionAt = 0;
  private frame: GoldenFrame | null = null;
  /** The last frame of a running draft: the loading screen still shows the matchup. */
  private lastActive: GoldenFrame | null = null;
  /** The session behind lastActive: the step line reads the draft's order from it. */
  private lastSession: Record<string, unknown> | null = null;
  /** The patch the client runs ("16.23.1"), for the data's lag; null until the client said. */
  private clientPatch: string | null = null;
  /** This app's version and a newer one on GitHub, for the lobby (live/update). */
  private version: { current: string | null; available: string | null } = { current: null, available: null };
  private game: LiveGameData | null = null;
  private readonly situations = new SituationTracker();
  /** The situational items put into this game's build; they stay for the game. */
  private readonly swaps = new SwapTracker();
  /** The own pick of the last draft, for the build and the game after it. */
  private picked: Picked | null = null;
  /** 1.5's showUnowned: suggest champions the account does not own too. */
  private showUnowned = false;
  private selectable: readonly number[] | null = null;

  setData(set: DataSet): void {
    this.data = set;
    this.meta = new MetaLookup(set.meta);
    this.adapter = new GoldenAdapter(set.meta.champions);
    this.engine = new DraftEngine(set.meta, {
      traits: this.traits,
      aramStats: set.aram ? aramStatsFrom(set.aram) : undefined,
      aramRounded: !set.aram,
      tankShred: TankShred.fromBuilds(set.builds),
    });
    this.engine.setTeamMode(this.teamMode);
    this.applyPatchLag();
    // New numbers arrive only while idle (core/refresh), but a kept session is re-evaluated anyway —
    // the first start may swap them in during a draft, and the new engine must keep its filter.
    if (this.session) {
      this.onSession(this.session, this.sessionAt);
      if (!this.showUnowned && this.selectable && this.frame?.state.isActive) {
        this.engine.setSelectable(new Set(this.selectable));
        this.onSession(this.session, this.sessionAt);
      }
    }
  }

  get hasData(): boolean {
    return this.data !== null;
  }

  /**
   * What the game view decided last, as one plain line (situations seen, suggestions shown): the
   * bridge writes it to the log whenever it changes, so a game can be checked afterwards.
   */
  diagnosticLine: string | null = null;

  /** The last download found no usable numbers (offline, GitHub down): said while there are none. */
  private dataFailed = false;

  setDataFailed(failed: boolean): void {
    this.dataFailed = failed;
  }

  onStatus(status: ClientStatus): void {
    this.status = status;
    if (!status.clientRunning) {
      this.phase = null;
      // A client that comes back may have patched in the meantime: asked again on reconnect.
      this.clientPatch = null;
      this.applyPatchLag();
    }
  }

  onPhase(phase: string): void {
    // A new game: also when the app starts in the middle of one (no GameStart seen).
    const playing = (p: string | null) => p === 'GameStart' || p === 'InProgress' || p === 'Reconnect';
    if (playing(phase) && !playing(this.phase)) {
      this.situations.reset();
      this.swaps.reset();
    }
    // Back in the lobby or the queue: the last draft is over for good.
    if (phase === 'Lobby' || phase === 'Matchmaking' || phase === 'None') {
      this.lastActive = null;
      this.lastSession = null;
    }
    this.phase = phase;
  }

  /** The scrubbed session (already parsed or as JSON), or null when champion select ended. */
  onSession(session: string | Record<string, unknown> | null, now = Date.now()): void {
    const parsed = typeof session === 'string' ? (JSON.parse(session) as Record<string, unknown>) : session;
    this.session = parsed;
    this.sessionAt = now;
    if (!this.engine) return;
    const { frame } = this.engine.update(parsed);
    this.frame = frame as unknown as GoldenFrame;
    if (parsed && frame.state.isActive) {
      this.lastActive = this.frame;
      this.lastSession = parsed;
      this.rememberPick();
    }
  }

  /** The settings switch "auch nicht gekaufte Champions vorschlagen". */
  setShowUnowned(on: boolean): void {
    if (this.showUnowned === on) return;
    this.showUnowned = on;
    this.engine?.setSelectable(on || !this.selectable ? null : new Set(this.selectable));
    if (this.session) this.onSession(this.session, this.sessionAt);
  }

  /** Whether the champions the player may pick are still unknown for the running draft. */
  get needsSelectable(): boolean {
    return !this.showUnowned && !!this.engine && !!this.frame?.state.isActive && !this.engine.hasSelectable;
  }

  /** The client's pickable-champion-ids; the own seat's picks are filtered to them from now on. */
  setSelectable(ids: readonly number[]): void {
    if (!this.engine || ids.length === 0) return;
    this.selectable = ids;
    if (this.showUnowned) return;
    this.engine.setSelectable(new Set(ids));
    if (this.session) this.onSession(this.session, this.sessionAt);
  }

  onGame(game: string | LiveGameData | null): void {
    this.game = typeof game === 'string' ? (JSON.parse(game) as LiveGameData) : game;
  }

  view(now = Date.now()): ViewModel {
    const active = this.frame?.state.isActive ? this.frame : null;
    let moment = momentOf(active ? this.adapter.facts(active) : null, this.phase);
    // The client says "InProgress" as soon as the game process starts — the loading screen is still
    // up then. Until the game's own API has the players, it is the loading screen.
    const gameAnswers = !!this.game?.activePlayer && (this.game.allPlayers?.length ?? 0) > 0;
    if (moment === 'game' && !gameAnswers && this.lastActive) moment = 'loading';
    // After the own lock the matchup leads; a click on a teammate asks for that seat's list instead,
    // until the clock moves on (1.5's slot click).
    const manual = this.engine?.manualSeat ?? null;
    if (moment === 'locked' && active && active.state.phase !== 'finalization' && manual !== null && manual !== active.state.localCellId) moment = 'teammate';
    // The session ends when the loading screen starts; the matchup stays on screen through it.
    const frame = active ?? (moment === 'loading' ? this.lastActive : null);
    const base: ViewModel = {
      moment,
      connection: this.connection(),
      data: this.dataStatus(now),
      board: null,
      main: { kind: 'none' },
      side: [],
      build: null,
      game: null,
      emptyText: null,
      sampleNote: null,
      canImport: !!active,
    };

    if (!this.data) {
      return {
        ...base,
        main: { kind: 'none' },
        emptyTitle: 'Noch keine Zahlen',
        emptyText: this.dataFailed
          ? 'Die Zahlen ließen sich nicht laden — keine Verbindung zu GitHub? DirkDraft versucht es jede Minute neu.'
          : 'Die Zahlen werden geladen — beim allerersten Start dauert das einen Moment.',
      };
    }

    if (moment === 'game') return this.gameMoment(base);

    if (!frame) {
      return {
        ...base,
        main: { kind: 'lobby', title: 'Wartet auf das nächste Champ Select', lines: this.lobbyLines() },
        build: this.pickedBuild(),
        emptyText: 'Das Fenster füllt sich von selbst, sobald ein Champ Select beginnt.',
      };
    }

    const board = this.adapter.board(frame);
    // The draft's own order (tournament, ranked/draft, blind); after the session, every step is done.
    board.steps = frame.state.usesLanes ? draftSteps(active ? this.session : this.lastSession, !active) : [];
    if (active && frame.state.phase !== 'planning') board.turnText = simultaneousTurn(this.session) ?? board.turnText;
    // Seats can be chosen only while the draft runs; the loading screen is a look back.
    if (!active) {
      for (const slot of board.allies) {
        slot.canTarget = false;
        slot.laneEditable = false;
      }
      board.turnText = 'Ladebildschirm';
      board.myTurn = false;
    }
    const me = board.allies.find((slot) => slot.isMe) ?? null;
    // Whose list the main tile shows: the clock's seat or a seat the user clicked (TurnTracker).
    const target = frame.target ? board.allies.find((slot) => slot.cellId === frame.target!.cellId) ?? null : null;
    const forMe = !target || target.isMe;
    const myChampion = me?.champion ?? null;
    const lane = me?.lane ?? null;
    const opponent = lane ? (board.enemies.find((slot) => slot.lane === lane)?.champion ?? null) : null;
    const mode = modeOf(frame.state.queue);
    const build = myChampion ? this.buildFor(myChampion, lane, opponent, mode) : null;
    const side: ViewModel['side'] = [];
    let main: ViewModel['main'];

    let emptyText: string | null = null;
    if (moment === 'aramBench') {
      const aram = this.adapter.aram(frame);
      const note = this.data.aram
        ? 'Winrates aus unserer Zählung normaler ARAM-Spiele — Mayhem-Spiele gibt Riot nicht heraus.'
        : 'Noch keine ARAM-Zahlen geladen.';
      if (aram) {
        main = { kind: 'aram', aram: { ...aram, note }, build };
      } else {
        // 1.5's rule: without a number for the own champion there is nothing to compare the bench to.
        main = { kind: 'lobby', title: 'Behalten oder tauschen?', lines: [] };
        emptyText = this.data.aram
          ? `Für ${myChampion?.name ?? 'deinen Champion'} gibt es noch keine ARAM-Zahlen — ohne sie lässt sich die Bank nicht vergleichen.`
          : 'Die ARAM-Zahlen sind noch nicht geladen.';
      }
      side.push({ kind: 'comp', comp: this.adapter.comp(frame) });
    } else if (!frame.state.usesLanes) {
      // Arena and rotating modes: no lanes, so no list and no build (spec, Momente).
      main = { kind: 'lobby', title: board.mode || 'Kein Modus mit Lanes', lines: [] };
      emptyText = `${board.mode || 'Dieser Modus'} hat keine Lanes. Unsere Zahlen stammen von Summoner’s Rift — deshalb gibt es hier keine Vorschläge und keinen Build.`;
    } else if (moment === 'locked' || moment === 'loading') {
      main = myChampion ? { kind: 'matchup', matchup: this.adapter.matchup(frame, myChampion, lane ?? 'top'), build } : { kind: 'none' };
      side.push({ kind: 'verdict', lines: this.adapter.verdictLines(frame) });
      side.push({ kind: 'chance', chance: this.adapter.chance(frame) });
      side.push({ kind: 'comp', comp: this.adapter.comp(frame) });
    } else if (target && !target.isMe && target.champion && !target.hovering && target.lane) {
      // A teammate who has locked in: his lane duel, like the own after the lock. No build — the
      // import button would write his runes into the own client.
      main = { kind: 'matchup', matchup: this.adapter.matchup(frame, target.champion, target.lane, target.label), build: null };
      side.push({ kind: 'chance', chance: this.adapter.chance(frame) });
      side.push({ kind: 'comp', comp: this.adapter.comp(frame) });
    } else if (moment === 'banning' && frame.bans) {
      main = { kind: 'bans', tile: this.adapter.pickTile(frame.bans, frame, forMe ? 'Banns für dich' : `Banns für ${target!.label}`) };
      const teamBans = this.adapter.teamBans(frame);
      if (teamBans) side.push({ kind: 'teamBans', bans: teamBans });
      const team = this.adapter.teamPlan(frame);
      if (team) side.push({ kind: 'team', team });
      side.push({ kind: 'openRoles', roles: this.adapter.openRoles(frame) });
      side.push({ kind: 'chance', chance: this.adapter.chance(frame) });
      side.push({ kind: 'comp', comp: this.adapter.comp(frame) });
    } else if (frame.picks) {
      const set = frame.picks;
      const who = moment === 'planning' && forMe ? 'Planung' : forMe ? 'Picks für dich' : `Picks für ${target!.label}`;
      const laneName = set.lane in LANE_NAMES ? ` · ${LANE_NAMES[set.lane as ViewLane]}` : '';
      const tile = this.adapter.pickTile(set, frame, `${who}${laneName}`);
      // "Dein Hover" ranks the own hover on the own lane; over a teammate's list it would mislead.
      if (!forMe) tile.hover = null;
      main = { kind: 'picks', tile };
      const team = this.adapter.teamPlan(frame);
      if (team) side.push({ kind: 'team', team });
      side.push({ kind: 'chance', chance: this.adapter.chance(frame) });
      side.push({ kind: 'comp', comp: this.adapter.comp(frame) });
    } else {
      main = { kind: 'none' };
    }

    return { ...base, board, main, side, build, emptyText };
  }

  /** D1: plan the open seats together (setting "Team-Modus"). */
  private teamMode = false;

  setTeamMode(on: boolean): void {
    if (this.teamMode === on) return;
    this.teamMode = on;
    this.engine?.setTeamMode(on);
    if (this.session) this.onSession(this.session, this.sessionAt);
  }

  /** D4: the plan for this draft, or null to leave it to the detection; gone with the draft. */
  choosePlan(plan: Plan | null): void {
    if (!this.engine || !this.session || !this.frame?.state.isActive) return;
    this.engine.choosePlan(plan);
    this.onSession(this.session, this.sessionAt);
  }

  /** A click on an ally seat: its list until the clock passes to someone else (1.5's TurnTracker). */
  selectSeat(cellId: number): void {
    if (!this.engine || !this.session || !this.frame?.state.isActive) return;
    this.engine.selectSeat(cellId, draftStateFrom(this.session));
    this.onSession(this.session, this.sessionAt);
  }

  /** The lane picker on a seat: a lane by hand, or null to give it back to the prediction. */
  setManualLane(cellId: number, lane: ViewLane | null): void {
    if (!this.engine || !this.session || !this.frame?.state.isActive) return;
    this.engine.setManualLane(cellId, lane ? laneFromKey(lane) : Lane.Unknown, draftStateFrom(this.session));
    this.onSession(this.session, this.sessionAt);
  }

  /** The client's game version (lol-patch), asked once per connection. */
  setClientPatch(version: string | null): void {
    if (this.clientPatch === version) return;
    this.clientPatch = version;
    this.applyPatchLag();
    if (this.session) this.onSession(this.session, this.sessionAt);
  }

  get hasClientPatch(): boolean {
    return this.clientPatch !== null;
  }

  /** This app's version and a newer release, for the lobby. */
  setVersion(current: string | null, available: string | null): void {
    this.version = { current, available };
  }

  /** The import button's input for the build on screen, or null when there is nothing to write. */
  importPlan(): ImportPlan | string {
    // Spells exist only in champion select, and the page is named after this draft's matchup.
    const frame = this.frame?.state.isActive ? this.frame : null;
    if (!frame) return 'Übernehmen geht nur im Champ Select.';
    const board = this.adapter.board(frame);
    const me = board.allies.find((slot) => slot.isMe);
    if (!me?.champion) return 'Erst einen Champion hovern oder locken.';
    const lane = me.lane ? laneFromKey(me.lane) : Lane.Unknown;
    const plan = this.planFor(me.champion.id, lane, modeOf(frame.state.queue));
    if (!plan) return `Für ${me.champion.name} gibt es noch keinen Build.`;
    const opponent = me.lane ? board.enemies.find((slot) => slot.lane === me.lane)?.champion : null;
    return {
      title: opponent ? `${me.champion.name} vs ${opponent.name}` : me.champion.name,
      runes: plan.runes?.ids.length === 11 ? plan.runes.ids : null,
      spells: plan.spells?.ids ?? [],
      currentSpells: this.currentSpells(),
    };
  }

  // ---- parts ----------------------------------------------------------------------------------

  private connection(): ViewModel['connection'] {
    const s = this.status;
    if (!s) return { state: 'off', text: 'startet …' };
    if (!s.leagueFound) return { state: 'off', text: 'League nicht gefunden' };
    if (!s.clientRunning) return { state: 'off', text: 'Client aus' };
    if (!s.socketConnected) return { state: 'warn', text: 'verbindet …' };
    return { state: 'ok', text: this.phase === 'InProgress' ? 'im Spiel' : 'verbunden' };
  }

  private dataStatus(now: number): DataStatus {
    if (!this.data) return { text: 'Noch keine Daten', short: 'Noch keine Daten', stale: true, chip: null };
    const behind = this.patchesBehind();
    // Right after a patch the collector keeps the old numbers until the new patch has enough games.
    const chip =
      behind > 0
        ? `Zahlen von Patch ${patchLine(this.data.meta.patch) || this.data.meta.patch}, der Client spielt ${patchLine(this.clientPatch)} — die Fehlerbalken sind entsprechend breiter.`
        : null;
    return { text: dataLine(this.data.meta, new Date(now)), short: dataLineShort(this.data.meta, new Date(now)), stale: (!this.data.fresh && this.data.problems.length > 0) || behind > 0, chip };
  }

  private patchesBehind(): number {
    return this.data && this.clientPatch ? patchesBetween(this.data.meta.patch, this.clientPatch) : 0;
  }

  /** The lane error bars widen with every patch the data trails the client (1.5, Messung 7). */
  private applyPatchLag(): void {
    const behind = this.patchesBehind();
    if (this.engine) this.engine.meta.patchesBehind = behind;
    if (this.meta) this.meta.patchesBehind = behind;
  }

  private lobbyLines(): { label: string; value: string; tone: 'good' | 'warn' | 'bad' | 'neutral' }[] {
    const c = this.connection();
    const meta = this.data?.meta;
    const lines: { label: string; value: string; tone: 'good' | 'warn' | 'bad' | 'neutral' }[] = [
      { label: 'League-Client', value: c.text, tone: c.state === 'ok' ? 'good' : c.state === 'warn' ? 'warn' : 'neutral' },
      { label: 'Daten', value: meta ? `Patch ${meta.patch} · ${meta.champions.length} Champions` : 'noch keine', tone: meta ? 'good' : 'warn' },
    ];
    const { current, available } = this.version;
    if (current) {
      lines.push(
        available
          ? { label: 'Version', value: `${current} · ${available} ist da`, tone: 'warn' }
          : { label: 'Version', value: current, tone: 'neutral' },
      );
    }
    return lines;
  }

  private currentSpells(): [number, number] {
    const s = this.session;
    const cell = s?.localPlayerCellId;
    const team = Array.isArray(s?.myTeam) ? (s.myTeam as Record<string, unknown>[]) : [];
    const me = team.find((p) => p.cellId === cell);
    return [Number(me?.spell1Id ?? 0), Number(me?.spell2Id ?? 0)];
  }

  private rememberPick(): void {
    const frame = this.frame;
    if (!frame) return;
    const board = this.adapter.board(frame);
    const me = board.allies.find((slot) => slot.isMe);
    const locked = frame.state.allies.find((slot) => slot.cellId === frame.state.localCellId)?.lockedChampionId ?? 0;
    if (!me || !locked) return;
    const opponent = me.lane ? board.enemies.find((slot) => slot.lane === me.lane)?.champion?.id ?? 0 : 0;
    this.picked = { championId: locked, lane: me.lane ? laneFromKey(me.lane) : Lane.Unknown, opponentId: opponent, mode: modeOf(frame.state.queue) };
  }

  private planFor(championId: number, lane: Lane, mode: Mode = 'lanes'): RiotBuildPlan | null {
    if (mode !== 'lanes') return this.data?.aram ? aramPlan(aramBuildFile(this.data.aram), championId) : null;
    const builds = this.data?.builds;
    if (!builds || !this.meta) return null;
    const asked = lane === Lane.Unknown ? this.meta.mainLane(championId) : lane;
    // Boots and items follow a win rate only where it holds within the game state.
    const situational = this.data?.situational;
    const slotOf = { boots: SLOT.Boots, late: SLOT.Late, core: SLOT.Core } as const;
    const sameState = situational
      ? (champion: number, on: Lane, slot: 'boots' | 'late' | 'core', items: readonly number[], base: readonly number[]) =>
          sameStateEdge(situational, champion, on, slotOf[slot], items, base)
      : null;
    return riotPlan(builds, championId, asked, sameState);
  }

  private buildFor(champion: ChampionRef, lane: ViewLane | null, opponent: ChampionRef | null, mode: Mode): BuildView | null {
    const plan = this.planFor(champion.id, lane ? laneFromKey(lane) : Lane.Unknown, mode);
    if (!plan) return null;
    return mode === 'lanes' ? buildFromRiot(plan, champion, opponent) : aramBuild(plan, champion, mode === 'mayhem');
  }

  private pickedBuild(): BuildView | null {
    const p = this.picked;
    if (!p) return null;
    const champion = this.adapter.champion(p.championId);
    if (!champion) return null;
    const plan = this.planFor(p.championId, p.lane, p.mode);
    if (!plan) return null;
    return p.mode === 'lanes' ? buildFromRiot(plan, champion, this.adapter.champion(p.opponentId)) : aramBuild(plan, champion, p.mode === 'mayhem');
  }

  private gameMoment(base: ViewModel): ViewModel {
    const meta = this.meta!;
    const game = this.game;
    const build = this.pickedBuild();
    if (!game) {
      return { ...base, build, emptyText: 'Sobald das Spiel läuft: nächster Kauf gegen dein Gold, der nächste Skillpunkt und was die Gegner bauen.' };
    }

    const items = assets()?.itemInfo ?? new Map();
    const me = game.allPlayers?.find((p) => p.riotId && p.riotId === game.activePlayer?.riotId);
    const championId = me ? championIdOf(me, meta) : (this.picked?.championId ?? 0);
    const lane = this.picked?.championId === championId ? this.picked.lane : Lane.Unknown;
    // The game's own word for the mode: ARAM, or KIWI for Mayhem.
    const gameMode = game.gameData?.gameMode;
    const mode: Mode = gameMode === 'KIWI' ? 'mayhem' : gameMode === 'ARAM' ? 'aram' : 'lanes';
    const basePlan = championId ? this.planFor(championId, lane, mode) : null;
    // Read once with the base plan: the enemies decide which situations are on.
    const first = gameModel(game, meta, basePlan, items);
    if (!first) {
      return { ...base, build, emptyText: 'Sobald das Spiel läuft: nächster Kauf gegen dein Gold, der nächste Skillpunkt und was die Gegner bauen.' };
    }

    const enemyIds = first.enemies.map((e) => e.championId).filter((id) => id > 0);
    const totals = enemyItemsOf(first.enemies.flatMap((e) => e.items), items);
    const situations = this.situations.update(situationsOf(enemyIds, meta, this.traits, totals));
    const ownedIds = (me?.items ?? []).map((i) => i.itemID);
    // Boots drop out once a pair is bought, the core once three items are finished.
    const slots: Slot[] = [];
    if (!ownedIds.some((id) => isBoots(id, items))) slots.push(SLOT.Boots);
    if (ownedIds.filter((id) => isFinished(id, items)).length < 3) slots.push(SLOT.Core);
    slots.push(SLOT.Late);
    // Situational items are counted per lane on the Rift; on the Howling Abyss there is nothing to say.
    // With a plan, the clear ones take their place in it for this game (core/situational); without
    // one there is nothing to take a place in, and they stay a list.
    const file = mode === 'lanes' ? (this.data?.situational ?? null) : null;
    const built = file && basePlan ? situationalBuild(file, meta, basePlan, championId, situations, ownedIds, (id) => ownsItem(id, ownedIds, items), slots, this.swaps) : null;
    const situational: RatedSuggestion[] | null = built
      ? built.suggestions
      : file
        ? situationalSuggestions(file, meta, championId, lane, situations, new Set(ownedIds), 3, slots).map((s) => ({ ...s, instead: null, adopt: false }))
        : null;
    const plan = built?.plan ?? basePlan;
    const model = built && built.swaps.length > 0 ? (gameModel(game, meta, plan, items, new Set(built.swaps.map((swap) => swap.itemId))) ?? first) : first;
    this.diagnosticLine =
      `Spiel: Champion ${championId}, Lane ${plan?.lane ?? lane}, Slots ${slots.join('/')} · Lagen ${situations.join(',')} · ` +
      (situational === null
        ? 'keine situativen Daten'
        : situational.length === 0
          ? 'kein situativer Vorschlag'
          : `Vorschläge ${situational.map((s) => `${s.itemId}@${s.slot}/${s.situation} (${s.timesMoreOften.toFixed(1)}×, ${s.games}${s.adopt ? `, statt ${s.instead}` : ''})`).join(', ')}`);

    const champion = this.adapter.champion(championId);
    const opponent = this.picked?.championId === championId ? this.adapter.champion(this.picked.opponentId) : null;
    const where = plan && plan.lane !== Lane.Unknown ? LANE_NAMES[(['top', 'jungle', 'mid', 'adc', 'support'] as const)[plan.lane]!] : '';
    const title = champion ? (opponent ? `${champion.name} vs ${opponent.name}` : `${champion.name}${where ? ` · ${where}` : ''}`) : 'Im Spiel';

    return {
      ...base,
      build,
      game: gameView(model, (id) => this.adapter.champion(id), title, null, situational),
    };
  }
}
