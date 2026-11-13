// What the window shows, as data. The UI renders exactly this and computes nothing of its own:
// every number, verdict and sentence is decided in the core, so a replay, a test and the live
// client all produce the same screen from the same draft.

import type { Plan } from './model/compAnalyzer';

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
  | 'game';

export type Area = 'draft' | 'game';

export const AREAS: { id: Area; label: string }[] = [
  { id: 'draft', label: 'Draft' },
  { id: 'game', label: 'Spiel' },
];

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
  /** Set by hand (lane picker); a manual lane is no guess. */
  laneManual: boolean;
  isMe: boolean;
  /** A teammate who queued with the local player: "dein Duo" or "Party"; null for everyone else. */
  party: string | null;
  /** The seat the main tile currently advises. */
  isTarget: boolean;
  /** An ally seat a click can make the target (until the clock moves on), as in 1.5. */
  canTarget: boolean;
  /** The lane can be set by hand: enemies, and allies without an assigned position (blind, custom). */
  laneEditable: boolean;
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
  /** Bans the own team has hovered but not locked yet; `by` is the seat label ("Teammate 2"). */
  allyBanIntents: { champion: ChampionRef; by: string }[];
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
  /** Not choosable by the viewer (shown greyed). */
  unavailable: boolean;
  /** A short tag beside the name: "sicherer Blind Pick". */
  badge: string | null;
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
  /** One line above the list about the pick position (B1). */
  note: PickNote | null;
  footnote: string | null;
}

/** The comp gap for a counter or last pick. */
export type PickNote = { kind: 'gap'; line: GapLine };

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
  /** Own data (2.1): what moves the chance, in points; absent where nothing does or on OP.GG files. */
  parts?: { label: string; text: string; tone: 'good' | 'bad' }[];
  /** Own data (2.1): the chance with the first pick of the list on the seat that picks next. */
  withPick?: { name: string; text: string };
}

export interface CompRow {
  label: string;
  ally: number;
  enemy: number;
  max: number;
}

/** "Eurem Team fehlt noch: Frontline, AP-Schaden und Engage", with the names kept apart (no break inside one). */
export interface GapLine {
  /** "Eurem Team fehlt noch", "Dem Gegnerteam fehlt", "Eurem Team fehlt nichts". */
  lead: string;
  /** What is missing, most severe first, in the names players use; empty for "nichts". */
  items: string[];
  tone: Tone;
}

export interface CompView {
  /** The own team's gaps for the seat about to pick. */
  gap: GapLine | null;
  rows: CompRow[];
  allyDamage: { physical: number; magic: number } | null;
  enemyDamage: { physical: number; magic: number } | null;
  /** The enemy's gaps; null while they lack nothing the rules see. */
  enemyGap: GapLine | null;
  /** "Euer Plan: Teamfight · Ihr Plan: Poke" and what each side wants (B3); null while neither is clear. */
  plans: { text: string; detail: string } | null;
  /** "Gemischtes Timing: 1× Early Game, 2× Late Game" — a hint, the traits are coarse. */
  timing: { text: string; hint: string } | null;
}

export interface MatchupView {
  /** "Dein Matchup", or "Matchup von Teammate 2" for a teammate who has locked in. */
  title: string;
  /** The own seat (ring around the portrait). */
  mine: boolean;
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
  caveat: string | null;
  runes: RunePageView | null;
  spells: number[];
  start: ItemOption | null;
  boots: ItemOption | null;
  core: ItemOption | null;
  /** The support item upgrade to pick when the quest is done (supports only). */
  support: ItemOption | null;
  skills: { level: number; key: 'Q' | 'W' | 'E' | 'R'; derived: boolean }[];
  skillPriority: string;
}

export interface GameView {
  clock: string;
  level: number;
  gold: number;
  me: ChampionRef;
  title: string;
  nextBuy: { item: ItemRef; cost: number; affordable: boolean; then: ItemRef | null; supportChoice: boolean; situational: boolean } | null;
  /**
   * Everything the gold at hand buys right now, in order (core/game basketOf); `then`: the plan's
   * item a part is for. Empty when nothing is affordable.
   */
  basket: { item: ItemRef; cost: number; then: ItemRef | null; situational: boolean }[];
  /** Gold left after the basket. */
  goldLeft: number;
  /** `situational`: an item that stands in the plan's place for this game's enemies. */
  path: { slot: string; items: (ItemRef & { owned: boolean; next: boolean; situational: boolean })[] }[];
  /** The plan's next point; `now`: a point is open right now (level above the points spent). */
  nextSkill: { key: 'Q' | 'W' | 'E' | 'R'; level: number; now: boolean } | null;
  /** Points spent so far: the skill grid marks that many levels as taken. */
  skillPoints: number;
  enemies: { champion: ChampionRef; items: ItemRef[]; damage: 'physisch' | 'magisch' | 'gemischt' }[];
  hints: { title: string; detail: string; tone: Tone }[];
  draftLine: string | null;
  sample: string | null;
  /**
   * Alternatives for this game's enemy line-up, measured (core/situational). null without data;
   * an empty list means nothing beats the base build clearly here.
   */
  situational: SituationalView[] | null;
  /** Du gegen den Lane-Gegner (docs/lane-vergleich.md); null without a known opponent. */
  strength: StrengthView | null;
}

/** The strength scale: up = stronger than the lane opponent, down = weaker. Describes, never advises. */
export interface StrengthView {
  /** The lane opponent, or the enemy duo on the bot lane. */
  opponents: ChampionRef[];
  /** −3 … +3, positive = stronger. */
  value: number;
  /** "Leicht stärker" */
  label: string;
  /** What the scale is made of: "Level 9 : 8 +1", "Items 3.450 : 2.800 +650". */
  rows: { label: string; mine: string; theirs: string; diff: string; tone: 'up' | 'down' | 'even' }[];
  /** How this duel usually goes by phase, from the own count; null while too few duels are counted. */
  phases: { label: string; who: string; tone: 'up' | 'down' | 'even'; now: boolean }[] | null;
}

export interface SituationalView {
  item: ItemRef;
  /** The base build's choice the item stands against: replaced by it, or kept with it as the alternative. */
  instead: ItemRef | null;
  /** In the build for this game, in the place of `instead`; else an alternative beside the build. */
  adopted: boolean;
  /** "Gegen viel magischen Schaden" */
  reason: string;
  /** "58 % statt 51 % · 2.400 Spiele" */
  stats: string;
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
  /** "Patch 16.22 · Platin · 6.185 Spiele · vor 2 Std." */
  text: string;
  /** "Patch 16.22 · vor 2 Std." — the pill where the title bar is tight (medium width). */
  short: string;
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
  /** What the empty area of a moment says ("Sobald das Spiel startet …"). */
  emptyText: string | null;
  /** Its heading where the moment's own ("Kein Draft") would mislead, e.g. before any numbers. */
  emptyTitle?: string | null;
  /** Shown above everything when the scene is sample data, never in a live session. */
  sampleNote: string | null;
  /** Runes and spells can be taken over now: champion select is running (spells exist only there). */
  canImport: boolean;
}

export type MainTile =
  | { kind: 'picks'; tile: PickTileView }
  | { kind: 'bans'; tile: PickTileView }
  | { kind: 'matchup'; matchup: MatchupView; build: BuildView | null }
  | { kind: 'aram'; aram: AramView; build: BuildView | null }
  | { kind: 'lobby'; title: string; lines: { label: string; value: string; tone: Tone }[] }
  | { kind: 'none' };

/**
 * D4: the plan for this draft, chosen by hand — offered to a fixed five only, where the whole team
 * can follow it. A second click on the chosen plan clears it.
 */
export interface PlanChoiceView {
  options: {
    plan: Plan;
    label: string;
    /** "Engage und der große Kampf um Objectives" */
    wants: string;
    active: boolean;
  }[];
  /** The chosen plan's name; null while none is chosen. */
  chosen: string | null;
}

/** D1: the open seats of the own team planned together (team mode). */
export interface TeamPlanView {
  rows: {
    cellId: number;
    /** "Du", "Teammate 3". */
    label: string;
    lane: Lane | null;
    champion: ChampionRef;
    value: string;
    /** "Flex: Top oder Mid". */
    flex: string | null;
    order: 'first' | 'last' | null;
    isMe: boolean;
  }[];
  /** "Zuerst Teammate 5 mit Rakan (Flex), zuletzt Du mit Darius (3 offene Counter)." */
  order: string | null;
  /** "geplant für euch 3" — whom the plan is for; null for a full five. */
  scope: string | null;
  note: string;
}

/** D3: the team's bans as one set (team mode). */
export interface TeamBansView {
  rows: {
    champion: ChampionRef;
    lane: Lane | null;
    /** "+3,8 Pkt" */
    value: string;
    /** "Teammate 3 hovert ihn" — already set. */
    by: string | null;
    /** The first ban of the set nobody hovers yet: the own one. */
    mine: boolean;
  }[];
  /** "für euch 2" — whose bans the set is; null for a full five. */
  scope: string | null;
  note: string;
}

export type SideTile =
  | { kind: 'team'; team: TeamPlanView }
  | { kind: 'plan'; plan: PlanChoiceView }
  | { kind: 'teamBans'; bans: TeamBansView }
  | { kind: 'chance'; chance: DraftChanceView }
  | { kind: 'comp'; comp: CompView }
  | { kind: 'openRoles'; roles: OpenRolesView }
  | { kind: 'verdict'; lines: VerdictLine[] };
