// Port of DraftPilot.Core.Draft.DraftState (1.5): one champ-select session reduced to what the model
// needs. Read from the LCU's /lol-champ-select/v1/session JSON as it arrives — leniently, like the
// C# reader (property names case-insensitive, numbers also as strings, null lists as empty).

import { Lane, laneFromLcu } from './lane';
import { queueFromId, type QueueKind } from './queue';

export type DraftPhase = 'none' | 'planning' | 'banPick' | 'finalization' | 'unknown';
export type TurnAction = 'none' | 'pick' | 'ban';

export interface DraftSlot {
  cellId: number;
  /** The slot's position among the non-null players; SeatPriors addresses seats by it. */
  index: number;
  isAlly: boolean;
  lockedChampionId: number;
  hoverChampionId: number;
  assignedLane: Lane;
}

export const effectiveChampionId = (slot: DraftSlot): number =>
  slot.lockedChampionId !== 0 ? slot.lockedChampionId : slot.hoverChampionId;

export const isLocked = (slot: DraftSlot): boolean => slot.lockedChampionId !== 0;

export interface ActiveTurn {
  cellId: number;
  isAlly: boolean;
  isLocalPlayer: boolean;
  action: TurnAction;
}

/** A ban the own team has hovered but not locked yet. */
export interface BanIntent {
  cellId: number;
  championId: number;
}

export interface DraftState {
  isActive: boolean;
  localCellId: number;
  allies: readonly DraftSlot[];
  enemies: readonly DraftSlot[];
  allyBans: readonly number[];
  enemyBans: readonly number[];
  /**
   * Ban hovers of the own team, not locked yet: in ranked the whole team bans at once, and the
   * client shows each other's choice. The enemy's never arrive.
   */
  allyBanIntents: readonly BanIntent[];
  /** The own team's pick actions in draft order (cell ids); empty where the session has none. */
  allyPickOrder: readonly number[];
  phase: DraftPhase;
  turn: ActiveTurn | null;
  /** Banned or locked anywhere in the draft. */
  unavailable: ReadonlySet<number>;
  queue: QueueKind;
  bench: readonly number[];
  benchEnabled: boolean;
  rerollsRemaining: number;
}

export const INACTIVE: DraftState = Object.freeze({
  isActive: false,
  localCellId: -1,
  allies: [],
  enemies: [],
  allyBans: [],
  enemyBans: [],
  allyBanIntents: [],
  allyPickOrder: [],
  phase: 'none',
  turn: null,
  unavailable: new Set<number>(),
  queue: 'unknown',
  bench: [],
  benchEnabled: false,
  rerollsRemaining: 0,
});

export function localSlot(state: DraftState): DraftSlot | null {
  return state.allies.find((slot) => slot.cellId === state.localCellId) ?? null;
}

export function findSlot(state: DraftState, cellId: number): DraftSlot | null {
  return state.allies.find((s) => s.cellId === cellId) ?? state.enemies.find((s) => s.cellId === cellId) ?? null;
}

// ---- lenient JSON access -------------------------------------------------------------------------

type Json = Record<string, unknown>;

function prop(obj: unknown, name: string): unknown {
  if (obj === null || typeof obj !== 'object' || Array.isArray(obj)) return undefined;
  const record = obj as Json;
  if (name in record) return record[name];
  const lower = name.toLowerCase();
  for (const key of Object.keys(record)) if (key.toLowerCase() === lower) return record[key];
  return undefined;
}

/** Thrown where System.Text.Json would throw: the whole session then reads as no session. */
class SessionFormatError extends Error {}

function num(obj: unknown, name: string, fallback = 0): number {
  const value = prop(obj, name);
  if (value === undefined || value === null) {
    if (value === null) throw new SessionFormatError(name);
    return fallback;
  }
  if (typeof value === 'number') return Math.trunc(value) === value ? value : fail(name);
  if (typeof value === 'string' && /^-?\d+$/.test(value.trim())) return Number(value.trim());
  return fail(name);
}

function bool(obj: unknown, name: string): boolean {
  const value = prop(obj, name);
  if (value === undefined) return false;
  if (typeof value === 'boolean') return value;
  return fail(name);
}

function str(obj: unknown, name: string): string | null {
  const value = prop(obj, name);
  if (value === undefined || value === null) return null;
  if (typeof value === 'string') return value;
  return fail(name);
}

function list(obj: unknown, name: string): unknown[] {
  const value = prop(obj, name);
  if (value === undefined || value === null) return [];
  if (Array.isArray(value)) return value;
  return fail(name);
}

function intList(obj: unknown, name: string): number[] {
  return list(obj, name).map((v) => num({ v }, 'v'));
}

function object(obj: unknown, name: string): unknown {
  const value = prop(obj, name);
  if (value === undefined || value === null) return {};
  if (typeof value === 'object' && !Array.isArray(value)) return value;
  return fail(name);
}

function fail(name: string): never {
  throw new SessionFormatError(name);
}

interface Action {
  actorCellId: number;
  championId: number;
  type: string | null;
  completed: boolean;
  isAllyAction: boolean;
  isInProgress: boolean;
}

// ---- DraftState.From -------------------------------------------------------------------------------

/** Parses a session object (already JSON.parse'd). Malformed sessions read as inactive. */
export function draftStateFrom(session: unknown): DraftState {
  if (session === null || session === undefined || typeof session !== 'object' || Array.isArray(session)) return INACTIVE;
  try {
    return fromSession(session);
  } catch (error) {
    if (error instanceof SessionFormatError) return INACTIVE;
    throw error;
  }
}

function fromSession(session: unknown): DraftState {
  // Read every field first, as the deserializer would, so a malformed one fails the whole session.
  const localCellId = num(session, 'localPlayerCellId', -1);
  const queueId = num(session, 'queueId');
  const isCustomGame = bool(session, 'isCustomGame');
  const isSpectating = bool(session, 'isSpectating');
  const benchEnabled = bool(session, 'benchEnabled');
  const rerollsRemaining = num(session, 'rerollsRemaining');
  const myTeam = list(session, 'myTeam');
  const theirTeam = list(session, 'theirTeam');
  const actions = readActions(session);
  const bans = object(session, 'bans');
  const myTeamBans = intList(bans, 'myTeamBans');
  const theirTeamBans = intList(bans, 'theirTeamBans');
  const timerPhase = str(object(session, 'timer'), 'phase');
  const benchChampions = list(session, 'benchChampions').map((entry) =>
    entry === null ? null : { championId: num(entry, 'championId') },
  );

  const allies = buildSlots(myTeam, true);
  const enemies = buildSlots(theirTeam, false);

  if (isSpectating) return INACTIVE;
  if (allies.length === 0 && enemies.length === 0) return INACTIVE;

  const ally = myTeamBans.filter((id) => id !== 0);
  const enemy = theirTeamBans.filter((id) => id !== 0);
  for (const action of actions) {
    if (action.type?.toLowerCase() !== 'ban') continue;
    if (!action.completed || action.championId === 0) continue;
    const target = action.isAllyAction ? ally : enemy;
    if (!target.includes(action.championId)) target.push(action.championId);
  }

  const allyBanIntents: BanIntent[] = [];
  for (const action of actions) {
    if (action.type?.toLowerCase() !== 'ban' || !action.isAllyAction) continue;
    if (action.completed || action.championId === 0) continue;
    allyBanIntents.push({ cellId: action.actorCellId, championId: action.championId });
  }

  const allyPickOrder: number[] = [];
  for (const action of actions) {
    if (action.type?.toLowerCase() !== 'pick' || !action.isAllyAction) continue;
    if (!allyPickOrder.includes(action.actorCellId)) allyPickOrder.push(action.actorCellId);
  }

  const unavailable = new Set<number>([...ally, ...enemy]);
  for (const slot of [...allies, ...enemies]) if (isLocked(slot)) unavailable.add(slot.lockedChampionId);

  // Zero ids and duplicates dropped: the client repeats an entry while a swap is in flight.
  const bench: number[] = [];
  for (const entry of benchChampions) {
    if (!entry || entry.championId === 0 || bench.includes(entry.championId)) continue;
    bench.push(entry.championId);
  }

  return {
    isActive: true,
    localCellId,
    queue: queueFromId(queueId, isCustomGame),
    allies,
    enemies,
    allyBans: ally,
    enemyBans: enemy,
    allyBanIntents,
    allyPickOrder,
    phase: parsePhase(timerPhase),
    turn: findActiveTurn(actions, localCellId),
    unavailable,
    benchEnabled,
    bench,
    rerollsRemaining: Math.max(0, rerollsRemaining),
  };
}

function readActions(session: unknown): Action[] {
  const result: Action[] = [];
  for (const group of list(session, 'actions')) {
    if (group === null) continue;
    if (!Array.isArray(group)) fail('actions');
    for (const action of group) {
      if (action === null) continue;
      result.push({
        actorCellId: num(action, 'actorCellId'),
        championId: num(action, 'championId'),
        type: str(action, 'type'),
        completed: bool(action, 'completed'),
        isAllyAction: bool(action, 'isAllyAction'),
        isInProgress: bool(action, 'isInProgress'),
      });
      num(action, 'id');
    }
  }
  return result;
}

function buildSlots(players: unknown[], isAlly: boolean): DraftSlot[] {
  const slots: DraftSlot[] = [];
  for (const player of players) {
    if (player === null) continue;
    const assigned = str(player, 'assignedPosition');
    num(player, 'team');
    slots.push({
      cellId: num(player, 'cellId'),
      index: slots.length,
      isAlly,
      lockedChampionId: num(player, 'championId'),
      hoverChampionId: num(player, 'championPickIntent'),
      // Riot never exposes enemy positions; guessing them is the LanePredictor's job.
      assignedLane: isAlly ? laneFromLcu(assigned) : Lane.Unknown,
    });
  }
  return slots;
}

function findActiveTurn(actions: Action[], localCellId: number): ActiveTurn | null {
  let best: ActiveTurn | null = null;
  let bestRank = Number.MAX_SAFE_INTEGER;
  for (const action of actions) {
    if (!action.isInProgress || action.completed) continue;
    const type = parseAction(action.type);
    if (type === 'none') continue;
    const isLocal = action.actorCellId === localCellId;
    const rank = isLocal ? -1 : action.isAllyAction ? action.actorCellId : 1000 + action.actorCellId;
    if (rank >= bestRank) continue;
    bestRank = rank;
    best = { cellId: action.actorCellId, isAlly: action.isAllyAction, isLocalPlayer: isLocal, action: type };
  }
  return best;
}

function parseAction(type: string | null): TurnAction {
  switch (type?.toLowerCase()) {
    case 'pick':
      return 'pick';
    case 'ban':
      return 'ban';
    default:
      return 'none';
  }
}

function parsePhase(phase: string | null): DraftPhase {
  if (phase === null || phase === '') return 'none';
  switch (phase.toUpperCase()) {
    case 'PLANNING':
      return 'planning';
    case 'BAN_PICK':
      return 'banPick';
    case 'FINALIZATION':
      return 'finalization';
    default:
      return 'unknown';
  }
}
