// The development and screenshot scenes: one per moment, each a real recording played through the
// TS model (core/engine) on the reference snapshot. The game has no recording yet; it uses sample
// data and says so on screen.

import type { ViewModel, Moment, ChampionRef, GameView, BuildView, StrengthView } from '../core/view';
import { LANE_NAMES } from '../core/view';
import { loadAssets, itemName } from './assets';
import { DraftEngine, stateFromFrame, type DraftFrame } from '../core/engine';
import type { AramStat } from '../core/model/advisors';
import { MetaLookup, type MetaSnapshot } from '../core/model/metaLookup';
import { laneFromKey } from '../core/model/lane';
import { gameModel, type LiveGameData } from '../core/game';
import { strengthOf } from '../core/strength';
import type { PlanOption, RiotBuildPlan } from '../core/riotBuilds';
import { gameView, strengthView } from './gameView';
import { SITUATION, SLOT, applySwaps, type RatedSuggestion, type Swap } from '../core/situational';
import { assets } from './assets';
import { GoldenAdapter, buildFromPlan, type GoldenFrame, type GoldenPlan } from './golden';
import { draftSteps } from '../core/steps';
import { LiveSession } from '../live/session';

export interface Scene {
  id: string;
  title: string;
  moment: Moment;
}

export const SCENES: Scene[] = [
  { id: 'lobby', title: 'Lobby', moment: 'lobby' },
  { id: 'planning', title: 'Planung und Hover', moment: 'planning' },
  { id: 'banning', title: 'Bannen', moment: 'banning' },
  { id: 'teambans', title: 'Bannen im Team', moment: 'banning' },
  { id: 'picking', title: 'Dein Pick', moment: 'picking' },
  { id: 'blind', title: 'Blind Pick mit offenen Countern', moment: 'picking' },
  { id: 'lastpick', title: 'Letzter Pick', moment: 'picking' },
  { id: 'teammate', title: 'Teammate am Zug', moment: 'teammate' },
  { id: 'mate', title: 'Teammate gelockt', moment: 'teammate' },
  { id: 'team', title: 'Team-Modus · 5er', moment: 'picking' },
  { id: 'teamduo', title: 'Team-Modus · Duo', moment: 'picking' },
  { id: 'teamtrio', title: 'Team-Modus · 3er', moment: 'picking' },
  { id: 'teambansmode', title: 'Team-Modus · Banns 5er', moment: 'banning' },
  { id: 'teambansduo', title: 'Team-Modus · Banns Duo', moment: 'banning' },
  { id: 'plan', title: 'Plan gewählt', moment: 'planning' },
  { id: 'locked', title: 'Gelockt', moment: 'locked' },
  { id: 'loading', title: 'Finalisierung', moment: 'loading' },
  { id: 'aram', title: 'ARAM-Bank', moment: 'aramBench' },
  { id: 'game', title: 'Im Spiel', moment: 'game' },
  { id: 'gamebehind', title: 'Im Spiel · hinten', moment: 'game' },
  { id: 'gamebot', title: 'Im Spiel · Botlane', moment: 'game' },
  { id: 'nodata', title: 'Ohne Daten', moment: 'lobby' },
  { id: 'gameloading', title: 'Spiel lädt', moment: 'game' },
];

const CONNECTED_STATUS = { leagueFound: true, clientRunning: true, socketConnected: true };

interface Sources {
  adapter: GoldenAdapter;
  plans: { matchupGuide: GoldenPlan; laneBuild: GoldenPlan };
  recordings: Map<string, { frames: { frame?: GoldenFrame }[] }>;
  drafts: Map<string, { frame: GoldenFrame }>;
  meta: MetaLookup;
}

let sources: Promise<Sources> | null = null;

/** The recorded session behind a played frame: the step line reads the draft's real order from it. */
const sessionOf = new WeakMap<object, unknown>();

async function json<T>(path: string): Promise<T> {
  const response = await fetch(path);
  if (!response.ok) throw new Error(`${path}: ${response.status}`);
  return (await response.json()) as T;
}

async function text(path: string): Promise<string> {
  const response = await fetch(path);
  if (!response.ok) throw new Error(`${path}: ${response.status}`);
  return (await response.text()).replace(/^﻿/, '');
}

/** A recording played through the TS model, one frame per session line, like the window would. */
function play(engine: DraftEngine, recording: string): { frames: { frame?: GoldenFrame; session?: unknown }[] } {
  const frames: { frame?: GoldenFrame; session?: unknown }[] = [];
  for (const line of recording.split(/\r?\n/)) {
    if (line.trim() === '') continue;
    let record: Record<string, unknown>;
    try {
      record = (JSON.parse(line) ?? {}) as Record<string, unknown>;
    } catch {
      continue;
    }
    if (typeof record.phase === 'string') continue;
    const frame = engine.update(record.session ?? null).frame;
    sessionOf.set(frame, record.session ?? null);
    frames.push({ frame });
  }
  return { frames };
}

function load(): Promise<Sources> {
  sources ??= (async () => {
    // ?data=own: our own meta.json from a local collector run (copied to public/dev-data, not in git);
    // otherwise the 1.5 reference snapshot.
    const own = new URLSearchParams(location.search).get('data') === 'own';
    const snapshot = await json<MetaSnapshot>(own ? '/dev-data/meta.json' : '/tests/golden/snapshot.json');
    const aram = await json<AramStat[]>('/tests/golden/aram-stats.json');
    await loadAssets(snapshot.patch);
    const options = { aramStats: new Map(aram.map((s) => [s.championId, s])) };
    const recordings = new Map<string, { frames: { frame?: GoldenFrame }[] }>();
    for (const name of ['live-tournament-draft', 'state-myban', 'live-ranked', 'build-demo', 'state-nachlock', 'state-final', 'state-aram-bench'])
      recordings.set(name, play(new DraftEngine(snapshot, options), await text(`/tests/recordings/${name}.jsonl`)));
    // A stored draft (README example) has no session to replay; its state is evaluated directly.
    const drafts = new Map<string, { frame: GoldenFrame }>();
    const stored = await json<{ input: { hover: number }; frame: DraftFrame }>('/tests/golden/drafts/readme-top-vs-jax.json');
    const engine = new DraftEngine(snapshot, options);
    const state = stateFromFrame(stored.frame.state);
    drafts.set('readme-top-vs-jax', { frame: engine.evaluate(state, engine.resolveTarget(state), stored.input.hover) });
    // Top first, nobody shown yet, Malphite hovered: two open counters (B1).
    const blind = await json<{ frame: DraftFrame }>('/tests/golden/drafts/blind-top.json');
    const blindState = stateFromFrame(blind.frame.state);
    drafts.set('blind-top', { frame: engine.evaluate(blindState, engine.resolveTarget(blindState), 54) });
    // Top last, everyone else shown: the comp gap above the list, mixed timing in the comp tile.
    const last = await json<{ input: { hover: number }; frame: DraftFrame }>('/tests/golden/drafts/last-pick-top.json');
    const lastState = stateFromFrame(last.frame.state);
    drafts.set('last-pick-top', { frame: engine.evaluate(lastState, engine.resolveTarget(lastState), last.input.hover) });
    const plans = await json<{ matchupGuide: GoldenPlan; laneBuild: GoldenPlan }>('/tests/golden/builds/plans.json');
    return { adapter: new GoldenAdapter(snapshot.champions), plans, recordings, drafts, meta: new MetaLookup(snapshot) };
  })();
  return sources;
}

/**
 * A recorded session with the first `mates` teammates marked as the local player's party, as the
 * shell marks them (lcu/scrub.rs). The recordings themselves know no party.
 */
function withParty(session: unknown, mates: number): Record<string, unknown> | null {
  if (!session || typeof session !== 'object') return null;
  const copy = JSON.parse(JSON.stringify(session)) as Record<string, unknown>;
  const team = Array.isArray(copy.myTeam) ? (copy.myTeam as Record<string, unknown>[]) : [];
  let left = mates;
  for (const player of team) {
    if (!player || player.cellId === copy.localPlayerCellId || left <= 0) continue;
    player.inParty = true;
    left--;
  }
  return copy;
}

const lastActive = (r: { frames: { frame?: GoldenFrame }[] }, predicate: (f: GoldenFrame) => boolean = () => true): GoldenFrame => {
  const frames = r.frames.map((f) => f.frame).filter((f): f is GoldenFrame => !!f && f.state.isActive && predicate(f));
  const frame = frames[frames.length - 1];
  if (!frame) throw new Error('Kein passender Frame');
  return frame;
};

const DATA = { text: 'Patch 16.22 · alle Ränge · vor 1 Tag', short: 'Patch 16.22 · vor 1 Tag', stale: false, chip: null };
const CONNECTED = { state: 'ok' as const, text: 'verbunden' };
const SAMPLE = 'Beispiel aus einer Aufzeichnung (1.5-Modell, Patch 16.22) — keine Live-Daten.';

export async function sceneView(id: string): Promise<ViewModel> {
  const s = await load();
  const a = s.adapter;

  switch (id) {
    case 'lobby':
      return {
        moment: 'lobby',
        connection: CONNECTED,
        data: DATA,
        board: null,
        main: {
          kind: 'lobby',
          title: 'Wartet auf das nächste Champ Select',
          lines: [
            { label: 'League-Client', value: 'verbunden', tone: 'good' },
            { label: 'Daten', value: 'Patch 16.22 · alle Ränge · 173 Champions', tone: 'good' },
            { label: 'Version', value: '2.0.0 · auf dem neuesten Stand', tone: 'neutral' },
          ],
        },
        side: [],
        build: null,
        game: null,
        emptyText: 'Das Fenster füllt sich von selbst, sobald ein Champ Select beginnt.',
        sampleNote: null,
        canImport: false,
      };

    case 'planning': {
      const frame = lastActive(s.recordings.get('live-tournament-draft')!, (f) => !!f.hover);
      return draftScene(a, frame, 'planning', s.plans, 'Planung');
    }
    case 'banning': {
      const frame = lastActive(s.recordings.get('state-myban')!);
      return draftScene(a, frame, 'banning', s.plans, 'Banns für dich');
    }
    // Ranked: the whole team bans at once; the teammates' hovers stand faded in the ban row.
    case 'teambans': {
      const frame = lastActive(s.recordings.get('live-ranked')!, (f) => f.state.turn?.action === 'ban' && (f.state.allyBanIntents?.length ?? 0) > 1);
      return draftScene(a, frame, 'banning', s.plans, 'Banns für dich');
    }
    case 'picking': {
      const frame = s.drafts.get('readme-top-vs-jax')!.frame;
      return draftScene(a, frame, 'picking', s.plans, 'Picks für dich');
    }
    case 'blind': {
      const frame = s.drafts.get('blind-top')!.frame;
      return draftScene(a, frame, 'picking', s.plans, 'Picks für dich');
    }
    case 'lastpick': {
      const frame = s.drafts.get('last-pick-top')!.frame;
      return draftScene(a, frame, 'picking', s.plans, 'Picks für dich');
    }
    case 'teammate': {
      const frame = lastActive(s.recordings.get('build-demo')!);
      return draftScene(a, frame, 'teammate', s.plans, 'Picks für Teammate 4');
    }
    // A click on a teammate who has locked in: his matchup, from the live session itself.
    case 'mate': {
      const live = new LiveSession();
      live.setData({ meta: s.meta.snapshot, builds: null, aram: null, situational: null, fresh: true, changed: false, problems: [] });
      live.onStatus(CONNECTED_STATUS);
      live.onPhase('ChampSelect');
      live.onSession((sessionOf.get(lastActive(s.recordings.get('build-demo')!)) as Record<string, unknown> | undefined) ?? null);
      const mate = live.view().board?.allies.find((slot) => !slot.isMe && slot.canTarget && slot.champion && !slot.hovering && slot.lane);
      if (mate) live.selectSeat(mate.cellId);
      return { ...live.view(), sampleNote: SAMPLE };
    }
    // Team mode (D1): the open seats of the party planned together, from the live session itself.
    // The recording knows no party: the scene marks the teammates as the shell would.
    case 'team':
    case 'teamduo':
    case 'teamtrio': {
      const live = new LiveSession();
      live.setData({ meta: s.meta.snapshot, builds: null, aram: null, situational: null, fresh: true, changed: false, problems: [] });
      live.setTeamMode(true);
      live.onStatus(CONNECTED_STATUS);
      live.onPhase('ChampSelect');
      const frame = lastActive(s.recordings.get('live-ranked')!, (f) => f.state.turn?.action === 'pick' && f.state.turn.isLocalPlayer && f.state.allies.every((slot) => !slot.lockedChampionId));
      live.onSession(withParty(sessionOf.get(frame), id === 'team' ? 4 : id === 'teamtrio' ? 2 : 1));
      return { ...live.view(), sampleNote: SAMPLE };
    }
    // Team mode in the ban phase (D3): one set of bans for the team, teammates' hovers set.
    case 'teambansmode':
    case 'teambansduo': {
      const live = new LiveSession();
      live.setData({ meta: s.meta.snapshot, builds: null, aram: null, situational: null, fresh: true, changed: false, problems: [] });
      live.setTeamMode(true);
      live.onStatus(CONNECTED_STATUS);
      live.onPhase('ChampSelect');
      const frame = lastActive(s.recordings.get('live-ranked')!, (f) => f.state.turn?.action === 'ban' && !!f.state.turn.isLocalPlayer && (f.state.allyBanIntents?.length ?? 0) >= 1);
      live.onSession(withParty(sessionOf.get(frame), id === 'teambansmode' ? 4 : 1));
      return { ...live.view(), sampleNote: SAMPLE };
    }
    // D4: Teamfight chosen as the plan in the planning phase.
    case 'plan': {
      const live = new LiveSession();
      live.setData({ meta: s.meta.snapshot, builds: null, aram: null, situational: null, fresh: true, changed: false, problems: [] });
      live.onStatus(CONNECTED_STATUS);
      live.onPhase('ChampSelect');
      const frame = lastActive(s.recordings.get('live-ranked')!, (f) => f.state.phase === 'planning');
      live.onSession((sessionOf.get(frame) as Record<string, unknown> | undefined) ?? null);
      live.choosePlan('teamfight');
      return { ...live.view(), sampleNote: SAMPLE };
    }
    case 'locked': {
      const frame = lastActive(s.recordings.get('state-nachlock')!);
      return draftScene(a, frame, 'locked', s.plans, 'Dein Matchup');
    }
    case 'loading': {
      const frame = lastActive(s.recordings.get('state-final')!);
      return draftScene(a, frame, 'loading', s.plans, 'Dein Matchup');
    }
    case 'aram': {
      const frame = lastActive(s.recordings.get('state-aram-bench')!);
      const view = draftScene(a, frame, 'aramBench', s.plans, 'Behalten oder tauschen?');
      return view;
    }
    case 'game':
      return gameScene(a, s.plans, s.meta);
    case 'gamebehind':
      return gameScene(a, s.plans, s.meta, 'behind');
    case 'gamebot':
      return gameScene(a, s.plans, s.meta, 'bot');
    // The empty states, from the live session itself: first start offline, and a game whose own API
    // does not answer yet.
    case 'nodata': {
      const live = new LiveSession();
      live.onStatus(CONNECTED_STATUS);
      live.setDataFailed(true);
      return live.view();
    }
    case 'gameloading': {
      const live = new LiveSession();
      live.setData({ meta: s.meta.snapshot, builds: null, aram: null, situational: null, fresh: true, changed: false, problems: [] });
      live.onStatus(CONNECTED_STATUS);
      live.onPhase('InProgress');
      return live.view();
    }
    default:
      throw new Error(`Unbekannte Szene ${id}`);
  }
}

function draftScene(
  a: GoldenAdapter,
  frame: GoldenFrame,
  moment: Moment,
  plans: { matchupGuide: GoldenPlan; laneBuild: GoldenPlan },
  title: string,
): ViewModel {
  const board = a.board(frame);
  if (sessionOf.has(frame) && board.usesLanes) board.steps = draftSteps(sessionOf.get(frame));
  const me = board.allies.find((slot) => slot.isMe)!;
  const myChampion = me.champion;
  const side: ViewModel['side'] = [];
  let main: ViewModel['main'];
  let build: BuildView | null = null;

  const darius = a.champion(122)!;

  if (moment === 'aramBench') {
    const aram = a.aram(frame);
    main = aram ? { kind: 'aram', aram, build: null } : { kind: 'none' };
    side.push({ kind: 'comp', comp: a.comp(frame) });
  } else if (moment === 'locked' || moment === 'loading') {
    const lane = me.lane ?? 'top';
    build = buildFromPlan(plans.matchupGuide, darius);
    main = { kind: 'matchup', matchup: a.matchup(frame, myChampion ?? darius, lane), build };
    side.push({ kind: 'verdict', lines: a.verdictLines(frame) });
    side.push({ kind: 'chance', chance: a.chance(frame) });
    side.push({ kind: 'comp', comp: a.comp(frame) });
  } else if (moment === 'banning') {
    main = { kind: 'bans', tile: a.pickTile(frame.bans!, frame, title) };
    side.push({ kind: 'openRoles', roles: a.openRoles(frame) });
    side.push({ kind: 'chance', chance: a.chance(frame) });
    side.push({ kind: 'comp', comp: a.comp(frame) });
  } else {
    const set = frame.picks!;
    const laneName = set.lane in LANE_NAMES ? ` · ${LANE_NAMES[set.lane as keyof typeof LANE_NAMES]}` : '';
    main = { kind: 'picks', tile: a.pickTile(set, frame, `${title}${laneName}`) };
    side.push({ kind: 'chance', chance: a.chance(frame) });
    side.push({ kind: 'comp', comp: a.comp(frame) });
  }

  return {
    moment,
    connection: CONNECTED,
    data: DATA,
    board,
    main,
    side,
    build,
    game: null,
    emptyText: null,
    sampleNote: SAMPLE,
    canImport: moment !== 'loading',
  };
}

function ref(a: GoldenAdapter, id: number): ChampionRef {
  return a.champion(id)!;
}

/** A stored OP.GG plan in the shape of our own plan, until the scenes run on builds.json. */
function planFromGolden(p: GoldenPlan): RiotBuildPlan {
  const option = (s: { itemIds: number[]; play: number; winRate: number }): PlanOption => ({ ids: s.itemIds, text: null, games: s.play, winRate: s.winRate });
  return {
    championId: p.championId,
    lane: laneFromKey(p.lane),
    inPlaceOf: null,
    patch: p.patch,
    games: 0,
    winRate: 0,
    runes: null,
    spells: null,
    starters: p.starters.map(option),
    boots: p.boots.map(option),
    core: p.coreItems.map(option),
    late: p.lateItems.slice(0, 3).map(option),
    supportChoice: null,
    supportOptions: [],
    skillOrder: p.skillOrder.join(''),
    skillMax: p.skillPriority.replace(/[^QWE]/g, ''),
    bootsAfterItems: null,
    bootsWorth: null,
  };
}

/**
 * The strength scale's three looks, with sample numbers until the collector's duels are measured:
 * ahead in mid game on top, behind against a level-6 spike, and two against two on the bot lane.
 */
function sampleStrength(a: GoldenAdapter, variant: 'ahead' | 'behind' | 'bot'): StrengthView {
  if (variant === 'behind')
    return {
      opponents: [ref(a, 24)],
      value: -1.9,
      label: 'Schwächer',
      rows: [
        { label: 'Level', mine: '5', theirs: '6', diff: '−1', tone: 'down' },
        { label: 'Items', mine: '1.650', theirs: '2.300', diff: '−650', tone: 'down' },
      ],
      phases: [
        { label: 'früh · Lvl 1–5', who: 'Darius', tone: 'up', now: false },
        { label: 'Mitte · 6–10', who: 'offen', tone: 'even', now: true },
        { label: 'spät · 11+', who: 'Jax', tone: 'down', now: false },
      ],
    };
  if (variant === 'bot')
    return {
      opponents: [ref(a, 29), ref(a, 25)],
      value: 0.2,
      label: 'Ausgeglichen',
      rows: [
        { label: 'Level', mine: '8 + 7', theirs: '8 + 7', diff: '±0', tone: 'even' },
        { label: 'Items', mine: '5.100', theirs: '4.850', diff: '+250', tone: 'up' },
      ],
      phases: null,
    };
  return {
    opponents: [ref(a, 24)],
    value: 1.3,
    label: 'Leicht stärker',
    rows: [
      { label: 'Level', mine: '9', theirs: '8', diff: '+1', tone: 'up' },
      { label: 'Items', mine: '3.450', theirs: '2.800', diff: '+650', tone: 'up' },
    ],
    phases: [
      { label: 'früh · Lvl 1–5', who: 'Darius', tone: 'up', now: false },
      { label: 'Mitte · 6–10', who: 'offen', tone: 'even', now: true },
      { label: 'spät · 11+', who: 'Jax', tone: 'down', now: false },
    ],
  };
}

function gameScene(a: GoldenAdapter, plans: { matchupGuide: GoldenPlan }, meta: MetaLookup, variant: 'ahead' | 'behind' | 'bot' = 'ahead'): ViewModel {
  const darius = ref(a, 122);
  const build = buildFromPlan(plans.matchupGuide, darius);
  const plan = planFromGolden(plans.matchupGuide);

  // A sample answer of /liveclientdata/allgamedata at 14:32: boots and the first core item done,
  // level 9 with one skill point still open. Names are placeholders, as the scrubber writes them.
  const owned = [...(plan.boots[0]?.ids ?? []), plan.core[0]?.ids[0] ?? 0, 1036];
  const order = plan.skillOrder.slice(0, 8);
  const abilities = Object.fromEntries((['Q', 'W', 'E', 'R'] as const).map((k) => [k, { abilityLevel: [...order].filter((c) => c === k).length }]));
  const enemy = (raw: string, items: number[], position: string, level: number) => ({
    rawChampionName: `game_character_displayname_${raw}`,
    team: 'CHAOS',
    position,
    level,
    items: items.map((itemID) => ({ itemID })),
  });
  const sample: LiveGameData = {
    activePlayer: { riotId: 'Spieler 1', level: 9, currentGold: 1240, abilities },
    allPlayers: [
      { riotId: 'Spieler 1', rawChampionName: 'game_character_displayname_Darius', team: 'ORDER', position: 'TOP', level: 9, items: owned.map((itemID) => ({ itemID })) },
      enemy('Jax', [3078, 3047], 'TOP', 8),
      enemy('Warwick', [3153, 3111], 'JUNGLE', 8),
      enemy('Zac', [3068, 3047], 'MIDDLE', 9),
      enemy('Twitch', [3153, 3006], 'BOTTOM', 8),
      enemy('Morgana', [3190], 'UTILITY', 7),
    ],
    gameData: { gameTime: 14 * 60 + 32 },
  };

  // Sample numbers: situational.json comes with the collector on GitHub. As live/session hands them
  // over: Spirit Visage has taken Stridebreaker's place for this game (core/situational
  // situationalBuild), the other two stay alternatives beside the build.
  const situational: RatedSuggestion[] = [
    { itemId: 3065, slot: SLOT.Late, situation: SITUATION.Magic, timesMoreOften: 2.8, winRate: 0.532, otherRate: 0.511, games: 2410, share: 0.21, instead: 6631, adopt: true },
    { itemId: 3111, slot: SLOT.Boots, situation: SITUATION.HardCc, timesMoreOften: 1.9, winRate: 0.524, otherRate: 0.509, games: 5120, share: 0.34, instead: 3047, adopt: false },
    { itemId: 3036, slot: SLOT.Late, situation: SITUATION.EnemyArmor, timesMoreOften: 3.4, winRate: 0.518, otherRate: 0.507, games: 1380, share: 0.12, instead: 3742, adopt: false },
  ];
  const swaps = situational.filter((s): s is Swap => s.adopt && s.instead !== null);
  const items = assets()?.itemInfo ?? new Map();
  const model = gameModel(sample, meta, applySwaps(plan, swaps), items, new Set(swaps.map((s) => s.itemId)));
  // The first look is the scale as the app reckons it from this sample game (core/strength); the
  // phases under it are still sample text, and so are the two other looks.
  const reckoned = variant === 'ahead' ? strengthOf(sample, meta, items) : null;
  const game: GameView = {
    ...gameView(model!, (id) => a.champion(id), 'Darius vs Jax · Toplane', 'Draft 51 : 49', situational),
    sample: 'Beispieldaten — im Spiel liest die Ansicht Riots Live-Client-Schnittstelle.',
    strength: reckoned ? { ...strengthView(reckoned, (id) => a.champion(id)), phases: sampleStrength(a, 'ahead').phases } : sampleStrength(a, variant),
  };
  return {
    moment: 'game',
    connection: { state: 'ok', text: 'im Spiel' },
    data: DATA,
    board: null,
    main: { kind: 'none' },
    side: [],
    build,
    game,
    emptyText: null,
    sampleNote: game.sample,
    canImport: false,
  };
}
