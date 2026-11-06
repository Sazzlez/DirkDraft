// The development and screenshot scenes: one per moment, each a real recording played through the
// TS model (core/engine) on the reference snapshot. Game and postgame have no recording yet; they
// use sample data and say so on screen.

import type { ViewModel, Moment, ChampionRef, GameView, PostgameView, BuildView } from '../core/view';
import { LANE_NAMES } from '../core/view';
import { loadAssets, itemName } from './assets';
import { DraftEngine, stateFromFrame, type DraftFrame } from '../core/engine';
import type { AramStat } from '../core/model/advisors';
import { MetaLookup, type MetaSnapshot } from '../core/model/metaLookup';
import { laneFromKey } from '../core/model/lane';
import { gameModel, type LiveGameData } from '../core/game';
import type { PlanOption, RiotBuildPlan } from '../core/riotBuilds';
import { gameView } from './gameView';
import { SITUATION, SLOT } from '../core/situational';
import { postgameModel } from '../core/postgame';
import { postgameView } from './postgameView';
import { assets } from './assets';
import { GoldenAdapter, buildFromPlan, type GoldenFrame, type GoldenPlan } from './golden';

export interface Scene {
  id: string;
  title: string;
  moment: Moment;
}

export const SCENES: Scene[] = [
  { id: 'lobby', title: 'Lobby', moment: 'lobby' },
  { id: 'planning', title: 'Planung und Hover', moment: 'planning' },
  { id: 'banning', title: 'Bannen', moment: 'banning' },
  { id: 'picking', title: 'Dein Pick', moment: 'picking' },
  { id: 'teammate', title: 'Teammate am Zug', moment: 'teammate' },
  { id: 'locked', title: 'Gelockt', moment: 'locked' },
  { id: 'loading', title: 'Finalisierung', moment: 'loading' },
  { id: 'aram', title: 'ARAM-Bank', moment: 'aramBench' },
  { id: 'game', title: 'Im Spiel', moment: 'game' },
  { id: 'postgame', title: 'Nachspiel', moment: 'postgame' },
];

interface Sources {
  adapter: GoldenAdapter;
  plans: { matchupGuide: GoldenPlan; laneBuild: GoldenPlan };
  recordings: Map<string, { frames: { frame?: GoldenFrame }[] }>;
  drafts: Map<string, { frame: GoldenFrame }>;
  meta: MetaLookup;
}

let sources: Promise<Sources> | null = null;

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
function play(engine: DraftEngine, recording: string): { frames: { frame?: GoldenFrame }[] } {
  const frames: { frame?: GoldenFrame }[] = [];
  for (const line of recording.split(/\r?\n/)) {
    if (line.trim() === '') continue;
    let record: Record<string, unknown>;
    try {
      record = (JSON.parse(line) ?? {}) as Record<string, unknown>;
    } catch {
      continue;
    }
    if (typeof record.phase === 'string') continue;
    frames.push({ frame: engine.update(record.session ?? null).frame });
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
    // A sample pool, so the "Mein Pool" switch has something to show without a client.
    const samplePool = new Set([122, 86, 875, 82, 516, 54, 24, 266, 103, 134, 412, 89, 64, 222, 51]);
    const options = { aramStats: new Map(aram.map((s) => [s.championId, s])), pool: () => samplePool };
    const recordings = new Map<string, { frames: { frame?: GoldenFrame }[] }>();
    for (const name of ['live-tournament-draft', 'state-myban', 'build-demo', 'state-nachlock', 'state-final', 'state-aram-bench'])
      recordings.set(name, play(new DraftEngine(snapshot, options), await text(`/tests/recordings/${name}.jsonl`)));
    // A stored draft (README example) has no session to replay; its state is evaluated directly.
    const drafts = new Map<string, { frame: GoldenFrame }>();
    const stored = await json<{ input: { hover: number }; frame: DraftFrame }>('/tests/golden/drafts/readme-top-vs-jax.json');
    const engine = new DraftEngine(snapshot, options);
    const state = stateFromFrame(stored.frame.state);
    drafts.set('readme-top-vs-jax', { frame: engine.evaluate(state, engine.resolveTarget(state), stored.input.hover) });
    const plans = await json<{ matchupGuide: GoldenPlan; laneBuild: GoldenPlan }>('/tests/golden/builds/plans.json');
    return { adapter: new GoldenAdapter(snapshot.champions), plans, recordings, drafts, meta: new MetaLookup(snapshot) };
  })();
  return sources;
}

const lastActive = (r: { frames: { frame?: GoldenFrame }[] }, predicate: (f: GoldenFrame) => boolean = () => true): GoldenFrame => {
  const frames = r.frames.map((f) => f.frame).filter((f): f is GoldenFrame => !!f && f.state.isActive && predicate(f));
  const frame = frames[frames.length - 1];
  if (!frame) throw new Error('Kein passender Frame');
  return frame;
};

const DATA = { text: 'Patch 16.22 · alle Ränge · vor 1 Tag', stale: false, chip: null };
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
        postgame: null,
        emptyText: 'Das Fenster füllt sich von selbst, sobald ein Champ Select beginnt.',
        sampleNote: null,
      };

    case 'planning': {
      const frame = lastActive(s.recordings.get('live-tournament-draft')!, (f) => !!f.hover);
      return draftScene(a, frame, 'planning', 24, s.plans, 'Planung · Turnier-Draft');
    }
    case 'banning': {
      const frame = lastActive(s.recordings.get('state-myban')!);
      return draftScene(a, frame, 'banning', 18, s.plans, 'Banns für dich');
    }
    case 'picking': {
      const frame = s.drafts.get('readme-top-vs-jax')!.frame;
      return draftScene(a, frame, 'picking', 24, s.plans, 'Picks für dich');
    }
    case 'teammate': {
      const frame = lastActive(s.recordings.get('build-demo')!);
      return draftScene(a, frame, 'teammate', 21, s.plans, 'Picks für Teammate 4');
    }
    case 'locked': {
      const frame = lastActive(s.recordings.get('state-nachlock')!);
      return draftScene(a, frame, 'locked', 27, s.plans, 'Dein Matchup');
    }
    case 'loading': {
      const frame = lastActive(s.recordings.get('state-final')!);
      return draftScene(a, frame, 'loading', null, s.plans, 'Dein Matchup');
    }
    case 'aram': {
      const frame = lastActive(s.recordings.get('state-aram-bench')!);
      const view = draftScene(a, frame, 'aramBench', 41, s.plans, 'Behalten oder tauschen?');
      return view;
    }
    case 'game':
      return gameScene(a, s.plans, s.meta);
    case 'postgame':
      return postgameScene(a);
    default:
      throw new Error(`Unbekannte Szene ${id}`);
  }
}

function draftScene(
  a: GoldenAdapter,
  frame: GoldenFrame,
  moment: Moment,
  seconds: number | null,
  plans: { matchupGuide: GoldenPlan; laneBuild: GoldenPlan },
  title: string,
): ViewModel {
  const board = a.board(frame, seconds);
  const me = board.allies.find((slot) => slot.isMe)!;
  const myChampion = me.champion;
  const side: ViewModel['side'] = [];
  let main: ViewModel['main'];
  let build: BuildView | null = null;

  const darius = a.champion(122)!;
  const jax = a.champion(24)!;

  if (moment === 'aramBench') {
    const aram = a.aram(frame);
    main = aram ? { kind: 'aram', aram, build: null } : { kind: 'none' };
    side.push({ kind: 'comp', comp: a.comp(frame) });
  } else if (moment === 'locked' || moment === 'loading') {
    const lane = me.lane ?? 'top';
    build = buildFromPlan(plans.matchupGuide, darius, jax);
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
    // Before the own lock, Build shows the hovered champion's build — here the lane build sample.
    build = buildFromPlan(plans.laneBuild, darius, null);
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
    postgame: null,
    emptyText: null,
    sampleNote: SAMPLE,
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
    skillOrder: p.skillOrder.join(''),
    skillMax: p.skillPriority.replace(/[^QWE]/g, ''),
  };
}

function gameScene(a: GoldenAdapter, plans: { matchupGuide: GoldenPlan }, meta: MetaLookup): ViewModel {
  const darius = ref(a, 122);
  const build = buildFromPlan(plans.matchupGuide, darius, ref(a, 24));
  const plan = planFromGolden(plans.matchupGuide);

  // A sample answer of /liveclientdata/allgamedata at 14:32: boots and the first core item done,
  // level 9 with one skill point still open. Names are placeholders, as the scrubber writes them.
  const owned = [...(plan.boots[0]?.ids ?? []), plan.core[0]?.ids[0] ?? 0, 1036];
  const order = plan.skillOrder.slice(0, 8);
  const abilities = Object.fromEntries((['Q', 'W', 'E', 'R'] as const).map((k) => [k, { abilityLevel: [...order].filter((c) => c === k).length }]));
  const enemy = (raw: string, items: number[]) => ({ rawChampionName: `game_character_displayname_${raw}`, team: 'CHAOS', items: items.map((itemID) => ({ itemID })) });
  const sample: LiveGameData = {
    activePlayer: { riotId: 'Spieler 1', level: 9, currentGold: 1240, abilities },
    allPlayers: [
      { riotId: 'Spieler 1', rawChampionName: 'game_character_displayname_Darius', team: 'ORDER', items: owned.map((itemID) => ({ itemID })) },
      enemy('Jax', [3078, 3047]),
      enemy('Warwick', [3153, 3111]),
      enemy('Zac', [3068, 3047]),
      enemy('Twitch', [3153, 3006]),
      enemy('Morgana', [3190]),
    ],
    gameData: { gameTime: 14 * 60 + 32 },
  };

  const model = gameModel(sample, meta, plan, assets()?.itemInfo ?? new Map());
  const game: GameView = {
    // Sample numbers: situational.json comes with the collector on GitHub.
    ...gameView(model!, (id) => a.champion(id), 'Darius vs Jax · Toplane', 'Draft 51 : 49', [
      { itemId: 3065, slot: SLOT.Late, situation: SITUATION.Magic, timesMoreOften: 2.8, winRate: 0.532, otherRate: 0.511, games: 2410 },
      { itemId: 3111, slot: SLOT.Boots, situation: SITUATION.HardCc, timesMoreOften: 1.9, winRate: 0.524, otherRate: 0.509, games: 5120 },
      { itemId: 3036, slot: SLOT.Late, situation: SITUATION.EnemyArmor, timesMoreOften: 3.4, winRate: 0.518, otherRate: 0.507, games: 1380 },
    ]),
    sample: 'Beispieldaten — die Spielansicht liest ab M3 die Live-Client-Schnittstelle.',
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
    postgame: null,
    emptyText: null,
    sampleNote: game.sample,
  };
}

function postgameScene(a: GoldenAdapter): ViewModel {
  // Sample history around a fixed evening, through the real postgame logic.
  const now = new Date(2026, 10, 6, 22, 15);
  const at = (daysAgo: number, h: number, m: number) => new Date(2026, 10, 6 - daysAgo, h, m).getTime();
  const record = { championId: 122, rank: 2, candidates: 58, tiedWithFirst: true, fromTop3: true, at: at(0, 21, 40) };
  const model = postgameModel(122, record, { win: true, items: [3071, 3047, 6333] }, [3071, 6333, 3053], (id) => ref(a, id).name);
  const history = [
    { championId: 122, win: true, fromTop3: true, at: at(0, 21, 40) },
    { championId: 86, win: false, fromTop3: true, at: at(0, 20, 55) },
    { championId: 17, win: false, fromTop3: false, at: at(1, 22, 10) },
    { championId: 122, win: true, fromTop3: true, at: at(1, 21, 5) },
  ];
  const postgame: PostgameView = postgameView(model, history, (id) => ref(a, id), now);
  return {
    moment: 'postgame',
    connection: CONNECTED,
    data: DATA,
    board: null,
    main: { kind: 'none' },
    side: [],
    build: null,
    game: null,
    postgame,
    emptyText: null,
    sampleNote: 'Beispieldaten — das Nachspiel speichert ab M5 lokal mit.',
  };
}
