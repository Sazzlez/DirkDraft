// The live session: what the client and the game report, turned into the same ViewModel the scenes
// show. Pure state and computation — the events come in through live/bridge.ts, so a test can feed
// recorded sessions and get the screen the window would show.
//
// Not yet here (each says so on screen instead of guessing): "Mein Pool" (needs masteries and match
// history from the client), the postgame (needs the end-of-game result), Mayhem builds.

import { DraftEngine } from '../core/engine';
import { gameModel, championIdOf, enemyItemsOf, type LiveGameData } from '../core/game';
import { aramStatsFrom } from '../core/mayhem';
import { poolFor, type PoolInput } from '../core/pool';
import { queueFromId, usesLanes } from '../core/model/queue';
import { Lane, laneFromKey } from '../core/model/lane';
import { MetaLookup } from '../core/model/metaLookup';
import { TraitTable } from '../core/model/traits';
import { momentOf, type GameflowPhase } from '../core/moment';
import { PickRecorder, addToHistory, postgameModel, type HistoryEntry, type PickRecord, type PostgameModel } from '../core/postgame';
import { postgameView } from '../data/postgameView';
import { riotPlan, type RiotBuildPlan } from '../core/riotBuilds';
import { SituationTracker, situationalSuggestions, situationsOf } from '../core/situational';
import { LANE_NAMES, type BuildView, type ChampionRef, type DataStatus, type Lane as ViewLane, type ViewModel } from '../core/view';
import { assets } from '../data/assets';
import { buildFromRiot } from '../data/builds';
import { gameView } from '../data/gameView';
import { GoldenAdapter, type GoldenFrame } from '../data/golden';
import { dataLine, type DataSet } from '../data/remote';

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

interface Picked {
  championId: number;
  lane: Lane;
  opponentId: number;
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
  private noBans = false;
  private game: LiveGameData | null = null;
  private readonly situations = new SituationTracker();
  /** The own pick of the last draft, for the build and the game after it. */
  private picked: Picked | null = null;

  private poolInput: PoolInput | null = null;
  private readonly recorder = new PickRecorder();
  /** Where the own pick stood in the suggestions when it was locked. */
  private pickRecord: PickRecord | null = null;
  private history: HistoryEntry[] = [];
  private postgame: PostgameModel | null = null;
  private endRecorded = true;

  /** The stored history (newest first), from the last sessions. */
  setHistory(history: HistoryEntry[]): void {
    this.history = history;
  }

  /** Whether the client's result for the game just played is still to be read. */
  get needsEndOfGame(): boolean {
    return !this.endRecorded && (this.phase === 'WaitingForStats' || this.phase === 'PreEndOfGame' || this.phase === 'EndOfGame');
  }

  /** The game's result: postgame on screen, one more history entry. Returns the new history to keep. */
  onEndOfGame(end: { championId: number; win: boolean; items: number[] }, now = Date.now()): HistoryEntry[] {
    if (this.endRecorded) return this.history;
    this.endRecorded = true;
    const record = this.pickRecord?.championId === end.championId ? this.pickRecord : null;
    const lane = this.picked?.championId === end.championId ? this.picked.lane : Lane.Unknown;
    const plannedCore = this.planFor(end.championId, lane)?.core[0]?.ids ?? [];
    this.postgame = postgameModel(end.championId, record, { win: end.win, items: end.items }, plannedCore, (id) => this.adapter.champion(id)?.name ?? `#${id}`);
    this.history = addToHistory(this.history, { championId: end.championId, win: end.win, fromTop3: record?.fromTop3 ?? null, at: now });
    return this.history;
  }

  setData(set: DataSet): void {
    this.data = set;
    this.meta = new MetaLookup(set.meta);
    this.adapter = new GoldenAdapter(set.meta.champions);
    const meta = this.meta;
    this.engine = new DraftEngine(set.meta, {
      traits: this.traits,
      aramStats: set.mayhem ? aramStatsFrom(set.mayhem) : undefined,
      aramRounded: !set.mayhem,
      pool: (lane) => (this.poolInput ? poolFor(lane, this.poolInput, meta).champions : null),
    });
    // New numbers arrive only while idle (core/refresh), but a kept session is re-evaluated anyway.
    if (this.session) this.onSession(this.session);
  }

  get hasData(): boolean {
    return this.data !== null;
  }

  onStatus(status: ClientStatus): void {
    this.status = status;
    if (!status.clientRunning) this.phase = null;
  }

  onPhase(phase: string): void {
    // A new game: also when the app starts in the middle of one (no GameStart seen).
    const playing = (p: string | null) => p === 'GameStart' || p === 'InProgress' || p === 'Reconnect';
    if (playing(phase) && !playing(this.phase)) {
      this.situations.reset();
      // A new game: its result is the one to read at the end.
      this.endRecorded = false;
      this.postgame = null;
    }
    // Back in the lobby or the queue: the last draft is over for good.
    if (phase === 'Lobby' || phase === 'Matchmaking' || phase === 'None') this.lastActive = null;
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
      // The session says so itself: bans.numBans is 0 in blind pick.
      this.noBans = (parsed.bans as { numBans?: number } | undefined)?.numBans === 0;
      this.rememberPick();
      const record = this.recorder.observe(frame, now);
      if (record) this.pickRecord = record;
    } else {
      this.recorder.reset();
    }
  }

  /** Whether "Mein Pool" still lacks the player's mastery and games. */
  get needsPool(): boolean {
    return this.poolInput === null;
  }

  /**
   * Mastery and recent games from the client. Games of modes without lanes count for no lane, so
   * ARAM does not fill the top pool; the pool is a filter and never changes a score.
   */
  setPoolInput(input: { masteries: { championId: number; points: number }[]; games: { championId: number; lane: string; queueId: number }[] }): void {
    this.poolInput = {
      masteries: input.masteries,
      games: input.games
        .filter((g) => usesLanes(queueFromId(g.queueId)))
        .map((g) => ({ championId: g.championId, lane: laneFromKey(g.lane) })),
    };
    if (this.session) this.onSession(this.session, this.sessionAt);
  }

  /** Whether the champions the player may pick are still unknown for the running draft. */
  get needsSelectable(): boolean {
    return !!this.engine && !!this.frame?.state.isActive && !this.engine.hasSelectable;
  }

  /** The client's pickable-champion-ids; the own seat's picks are filtered to them from now on. */
  setSelectable(ids: readonly number[]): void {
    if (!this.engine || ids.length === 0) return;
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
      postgame: null,
      emptyText: null,
      sampleNote: null,
    };

    if (!this.data) {
      return { ...base, main: { kind: 'none' }, emptyText: 'Die Zahlen werden geladen — beim allerersten Start dauert das einen Moment.' };
    }

    if (moment === 'game' || moment === 'postgame') return this.gameMoment(base);

    if (!frame) {
      return {
        ...base,
        main: { kind: 'lobby', title: 'Wartet auf das nächste Champ Select', lines: this.lobbyLines() },
        build: this.pickedBuild(),
        // The last game's result stays in the game area until the next one.
        postgame: this.postgame ? this.postgameView(now) : null,
        emptyText: 'Das Fenster füllt sich von selbst, sobald ein Champ Select beginnt.',
      };
    }

    const board = this.adapter.board(frame, active ? this.secondsLeft(now) : null);
    if (this.withoutBans()) board.steps = [
      { label: 'Picks', state: frame.state.phase === 'finalization' || !active ? 'done' : 'now' },
      { label: 'Finalisierung', state: !active ? 'done' : frame.state.phase === 'finalization' ? 'now' : 'next' },
    ];
    const me = board.allies.find((slot) => slot.isMe) ?? null;
    const myChampion = me?.champion ?? null;
    const lane = me?.lane ?? null;
    const opponent = lane ? (board.enemies.find((slot) => slot.lane === lane)?.champion ?? null) : null;
    const build = myChampion ? this.buildFor(myChampion, lane, opponent) : null;
    const side: ViewModel['side'] = [];
    let main: ViewModel['main'];

    if (moment === 'aramBench') {
      const aram = this.adapter.aram(frame);
      main = aram ? { kind: 'aram', aram, build: null } : { kind: 'none' };
      side.push({ kind: 'comp', comp: this.adapter.comp(frame) });
    } else if (moment === 'locked' || moment === 'loading') {
      main = myChampion ? { kind: 'matchup', matchup: this.adapter.matchup(frame, myChampion, lane ?? 'top'), build } : { kind: 'none' };
      side.push({ kind: 'verdict', lines: this.adapter.verdictLines(frame) });
      side.push({ kind: 'chance', chance: this.adapter.chance(frame) });
      side.push({ kind: 'comp', comp: this.adapter.comp(frame) });
    } else if (moment === 'banning' && frame.bans) {
      main = { kind: 'bans', tile: this.adapter.pickTile(frame.bans, frame, 'Banns für dich') };
      side.push({ kind: 'openRoles', roles: this.adapter.openRoles(frame) });
      side.push({ kind: 'chance', chance: this.adapter.chance(frame) });
      side.push({ kind: 'comp', comp: this.adapter.comp(frame) });
    } else if (frame.picks) {
      const set = frame.picks;
      const who = moment === 'teammate' ? 'Picks für deinen Teammate' : moment === 'planning' ? 'Planung' : 'Picks für dich';
      const laneName = set.lane in LANE_NAMES ? ` · ${LANE_NAMES[set.lane as ViewLane]}` : '';
      main = { kind: 'picks', tile: this.adapter.pickTile(set, frame, `${who}${laneName}`) };
      side.push({ kind: 'chance', chance: this.adapter.chance(frame) });
      side.push({ kind: 'comp', comp: this.adapter.comp(frame) });
    } else {
      main = { kind: 'none' };
    }

    return { ...base, board, main, side, build };
  }

  /** The import button's input for the build on screen, or null when there is nothing to write. */
  importPlan(): ImportPlan | null {
    const frame = this.frame?.state.isActive ? this.frame : null;
    if (!frame || !this.data?.builds) return null;
    const board = this.adapter.board(frame, null);
    const me = board.allies.find((slot) => slot.isMe);
    if (!me?.champion) return null;
    const lane = me.lane ? laneFromKey(me.lane) : Lane.Unknown;
    const plan = this.planFor(me.champion.id, lane);
    if (!plan) return null;
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
    if (!this.data) return { text: 'Noch keine Daten', stale: true, chip: null };
    return { text: dataLine(this.data.meta, new Date(now)), stale: !this.data.fresh && this.data.problems.length > 0, chip: null };
  }

  private lobbyLines(): { label: string; value: string; tone: 'good' | 'warn' | 'bad' | 'neutral' }[] {
    const c = this.connection();
    const meta = this.data?.meta;
    return [
      { label: 'League-Client', value: c.text, tone: c.state === 'ok' ? 'good' : c.state === 'warn' ? 'warn' : 'neutral' },
      { label: 'Daten', value: meta ? `Patch ${meta.patch} · ${meta.champions.length} Champions` : 'noch keine', tone: meta ? 'good' : 'warn' },
    ];
  }

  private postgameView(now: number): ViewModel['postgame'] {
    if (!this.postgame) return null;
    const ref = (id: number) => this.adapter.champion(id) ?? { id, key: '', name: `#${id}` };
    return postgameView(this.postgame, this.history, ref, new Date(now));
  }

  /** Blind pick and other modes without bans (kept through the loading screen). */
  private withoutBans(): boolean {
    return this.noBans;
  }

  /** Seconds left in the current champion select step, counted down from the session's timer. */
  private secondsLeft(now: number): number | null {
    const timer = this.session?.timer as { adjustedTimeLeftInPhase?: number } | undefined;
    const left = timer?.adjustedTimeLeftInPhase;
    if (typeof left !== 'number' || left <= 0) return null;
    return Math.max(0, Math.round((left - (now - this.sessionAt)) / 1000));
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
    const board = this.adapter.board(frame, null);
    const me = board.allies.find((slot) => slot.isMe);
    const locked = frame.state.allies.find((slot) => slot.cellId === frame.state.localCellId)?.lockedChampionId ?? 0;
    if (!me || !locked) return;
    const opponent = me.lane ? board.enemies.find((slot) => slot.lane === me.lane)?.champion?.id ?? 0 : 0;
    this.picked = { championId: locked, lane: me.lane ? laneFromKey(me.lane) : Lane.Unknown, opponentId: opponent };
  }

  private planFor(championId: number, lane: Lane): RiotBuildPlan | null {
    const builds = this.data?.builds;
    if (!builds || !this.meta) return null;
    const asked = lane === Lane.Unknown ? this.meta.mainLane(championId) : lane;
    return riotPlan(builds, championId, asked);
  }

  private buildFor(champion: ChampionRef, lane: ViewLane | null, opponent: ChampionRef | null): BuildView | null {
    const plan = this.planFor(champion.id, lane ? laneFromKey(lane) : Lane.Unknown);
    return plan ? buildFromRiot(plan, champion, opponent) : null;
  }

  private pickedBuild(): BuildView | null {
    const p = this.picked;
    if (!p) return null;
    const champion = this.adapter.champion(p.championId);
    if (!champion) return null;
    const plan = this.planFor(p.championId, p.lane);
    return plan ? buildFromRiot(plan, champion, this.adapter.champion(p.opponentId)) : null;
  }

  private gameMoment(base: ViewModel): ViewModel {
    const meta = this.meta!;
    const game = this.game;
    const build = this.pickedBuild();
    if (base.moment === 'postgame' && this.postgame) {
      return { ...base, build, postgame: this.postgameView(Date.now()) };
    }
    if (!game) {
      return {
        ...base,
        build,
        emptyText:
          base.moment === 'postgame'
            ? 'Das Nachspiel (Ergebnis und Platz deines Picks) kommt in einem der nächsten Schritte.'
            : 'Sobald das Spiel läuft: nächster Kauf gegen dein Gold, der nächste Skillpunkt und was die Gegner bauen.',
      };
    }

    const items = assets()?.itemInfo ?? new Map();
    const me = game.allPlayers?.find((p) => p.riotId && p.riotId === game.activePlayer?.riotId);
    const championId = me ? championIdOf(me, meta) : (this.picked?.championId ?? 0);
    const lane = this.picked?.championId === championId ? this.picked.lane : Lane.Unknown;
    const plan = championId ? this.planFor(championId, lane) : null;
    const model = gameModel(game, meta, plan, items);
    if (!model) {
      return { ...base, build, emptyText: 'Sobald das Spiel läuft: nächster Kauf gegen dein Gold, der nächste Skillpunkt und was die Gegner bauen.' };
    }

    const enemyIds = model.enemies.map((e) => e.championId).filter((id) => id > 0);
    const totals = enemyItemsOf(model.enemies.flatMap((e) => e.items), items);
    const situations = this.situations.update(situationsOf(enemyIds, meta, this.traits, totals));
    const owned = new Set((me?.items ?? []).map((i) => i.itemID));
    const situational = this.data?.situational
      ? situationalSuggestions(this.data.situational, meta, championId, plan?.lane ?? lane, situations, owned, 3)
      : null;

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
