// What the window shows, as data. The UI renders exactly this and computes nothing of its own:
// every number, verdict and sentence is decided in the core, so a replay, a test and the live
// client all produce the same screen from the same draft.

export type Lane = 'top' | 'jungle' | 'mid' | 'adc' | 'support';

export const LANES: readonly Lane[] = ['top', 'jungle', 'mid', 'adc', 'support'];

export const LANE_NAMES: Record<Lane, string> = {
  top: 'Toplane',
  jungle: 'Jungle',
  mid: 'Midlane',
  adc: 'Botlane',
  support: 'Support',
};

/** The moments of a session; each has one main tile. */
export type Moment =
  | 'lobby'
  | 'planning'
  | 'banning'
  | 'picking'
  | 'teammate'
  | 'locked'
  | 'loading'
  | 'aramBench'
  | 'game'
  | 'postgame';

export type Area = 'draft' | 'build' | 'game';

/** Which area a moment opens; a manual switch holds until the next moment begins. */
export const AREA_OF: Record<Moment, Area> = {
  lobby: 'draft',
  planning: 'draft',
  banning: 'draft',
  picking: 'draft',
  teammate: 'draft',
  locked: 'draft',
  loading: 'draft',
  aramBench: 'draft',
  game: 'game',
  postgame: 'game',
};

export type Tone = 'good' | 'warn' | 'bad' | 'neutral' | 'info';

export interface ChampionRef {
  id: number;
  name: string;
  /** Data Dragon key, e.g. MonkeyKing for Wukong; addresses the portrait. */
  key: string;
}

export interface SlotView {
  cellId: number;
  side: 'ally' | 'enemy';
  /** "Du", "Teammate 3", "Gegner 2" — never a player name. */
  label: string;
  champion: ChampionRef | null;
  /** True while the champion is only hovered, not locked. */
  hovering: boolean;
  lane: Lane | null;
  /** Lane confidence 0..1, shown only when below the 60 % threshold. */
  laneConfidence: number | null;
  laneUncertain: boolean;
  isMe: boolean;
  /** The seat the main tile currently advises. */
  isTarget: boolean;
  /** Ally hover that cannot work: "gebannt", "schon gepickt". */
  warning: string | null;
}

export interface PhaseStep {
  label: string;
  state: 'done' | 'now' | 'next';
}

export interface BoardView {
  allies: SlotView[];
  enemies: SlotView[];
  allyBans: ChampionRef[];
  enemyBans: ChampionRef[];
  /** Seconds left in the current step; null when the client sends none. */
  seconds: number | null;
  /** "Du pickst", "Teammate 3 pickt", "Gegner bannen", "Finalisierung". */
  turnText: string;
  myTurn: boolean;
  steps: PhaseStep[];
  /** "Ranked Solo/Duo", "ARAM Mayhem*" … */
  mode: string;
  modeNote: string | null;
  usesLanes: boolean;
  /** ARAM and Mayhem: the bench takes the enemy column, which the client keeps hidden there. */
  bench: ChampionRef[] | null;
}

export interface TermView {
  label: string;
  /** Contribution in win-rate points; null means "keine Daten", never zero. */
  points: number | null;
  /** For the ban popularity gate: the multiplier, 0..1. */
  gate: number | null;
  verdict: string;
  hint: string;
  kind: 'lane' | 'matchup' | 'enemies' | 'synergy' | 'comp' | 'ban';
}

export interface RecommendationView {
  champion: ChampionRef;
  /** Picks: estimated win rate as text at the list's precision; bans: "+2,5 Pkt". */
  value: string;
  /** Raw value for bars and sorting tests. */
  score: number;
  errorPoints: number;
  verdict: string;
  tone: Tone;
  /** The one reason shown in the row. */
  reason: string;
  reasons: { text: string; tone: Tone }[];
  terms: TermView[];
  tied: boolean;
  /** Not choosable by the viewer (banned pool champion shown greyed). */
  unavailable: boolean;
  inPool: boolean;
}

export interface PickTileView {
  kind: 'picks' | 'bans';
  title: string;
  lane: Lane | null;
  /** Size of the leading tie group (≥ 2) or 0. */
  tied: number;
  items: RecommendationView[];
  /** Pinned hover row ("Dein Hover"). */
  hover: HoverView | null;
  footnote: string | null;
  poolAvailable: boolean;
}

export interface HoverView {
  champion: ChampionRef;
  value: string | null;
  /** "Platz 9 von 58 · knapp dahinter" */
  place: string;
  tone: Tone;
  note: string | null;
}

export interface OpenRolesView {
  /** "Dem Gegner fehlt noch Botlane und Support." */
  text: string;
  lanes: Lane[];
}

export interface DraftChanceView {
  ally: number | null;
  allyText: string;
  enemyText: string;
  verdict: string;
  note: string;
}

export interface CompRow {
  label: string;
  ally: number;
  enemy: number;
  max: number;
}

export interface CompView {
  rows: CompRow[];
  allyDamage: { physical: number; magic: number } | null;
  enemyDamage: { physical: number; magic: number } | null;
  verdict: string;
  verdictTone: Tone;
}

export interface MatchupView {
  me: ChampionRef;
  opponent: ChampionRef | null;
  lane: Lane;
  value: string | null;
  sample: string;
  verdict: string;
  tone: Tone;
}

export interface VerdictLine {
  icon: 'engage' | 'scaling' | 'heal' | 'damage' | 'info';
  tone: Tone;
  title: string;
  detail: string;
}

export interface ItemRef {
  id: number;
  name: string;
}

export interface ItemOption {
  items: ItemRef[];
  /** "52 % · 1.240 Spiele" or "dünne Datenlage · 31 Spiele". */
  stats: string;
}

export interface RunePageView {
  keystone: number;
  primaryStyle: number;
  primary: number[];
  secondaryStyle: number;
  secondary: number[];
  shards: string[];
  stats: string;
}

export interface BuildView {
  champion: ChampionRef;
  lane: Lane | null;
  title: string;
  source: string;
  caveat: string | null;
  runes: RunePageView | null;
  spells: number[];
  start: ItemOption | null;
  boots: ItemOption | null;
  core: ItemOption | null;
  late: ItemRef[];
  alsoStart: ItemOption[];
  alsoBoots: ItemOption[];
  alsoCore: ItemOption[];
  skills: { level: number; key: 'Q' | 'W' | 'E' | 'R'; derived: boolean }[];
  skillPriority: string;
}

export interface GameView {
  clock: string;
  level: number;
  gold: number;
  me: ChampionRef;
  title: string;
  nextBuy: { item: ItemRef; cost: number; affordable: boolean; then: ItemRef | null } | null;
  path: { slot: string; items: (ItemRef & { owned: boolean; next: boolean })[] }[];
  nextSkill: { key: 'Q' | 'W' | 'E' | 'R'; level: number } | null;
  enemies: { champion: ChampionRef; items: ItemRef[]; damage: 'physisch' | 'magisch' | 'gemischt' }[];
  hints: { title: string; detail: string; tone: Tone }[];
  draftLine: string | null;
  sample: string | null;
}

export interface PostgameView {
  result: 'Sieg' | 'Niederlage';
  champion: ChampionRef;
  pickedFromTop3: boolean | null;
  pickNote: string;
  buildNote: string;
  history: { champion: ChampionRef; result: 'S' | 'N'; fromTop3: boolean | null; when: string }[];
  topThreeRate: string;
}

export interface AramRowView extends RecommendationView {
  own: boolean;
}

export interface AramView {
  rows: AramRowView[];
  rerolls: number;
  tied: number;
  note: string;
}

export interface DataStatus {
  /** "Patch 16.22 · Platin · vor 2 h" */
  text: string;
  stale: boolean;
  /** Present when the data needs a word: older patch, other queue. */
  chip: string | null;
}

/** Everything one frame of the app shows. */
export interface ViewModel {
  moment: Moment;
  connection: { state: 'off' | 'warn' | 'ok'; text: string };
  data: DataStatus;
  board: BoardView | null;
  main: MainTile;
  side: SideTile[];
  build: BuildView | null;
  game: GameView | null;
  postgame: PostgameView | null;
  /** What the empty area of a moment says ("Sobald das Spiel startet …"). */
  emptyText: string | null;
  /** Shown above everything when the scene is sample data, never in a live session. */
  sampleNote: string | null;
}

export type MainTile =
  | { kind: 'picks'; tile: PickTileView }
  | { kind: 'bans'; tile: PickTileView }
  | { kind: 'matchup'; matchup: MatchupView; build: BuildView | null }
  | { kind: 'aram'; aram: AramView; build: BuildView | null }
  | { kind: 'lobby'; title: string; lines: { label: string; value: string; tone: Tone }[] }
  | { kind: 'none' };

export type SideTile =
  | { kind: 'chance'; chance: DraftChanceView }
  | { kind: 'comp'; comp: CompView }
  | { kind: 'openRoles'; roles: OpenRolesView }
  | { kind: 'verdict'; lines: VerdictLine[] };
