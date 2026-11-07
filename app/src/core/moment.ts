// Which moment a session is in. One function, fed by the client's two facts — the draft state and
// the gameflow phase — so the window can never show a pick list over a running game or a build
// card in the lobby, which 1.5 had to prevent with a dozen visibility flags.

import type { Moment } from './view';

export type GameflowPhase =
  | 'None'
  | 'Lobby'
  | 'Matchmaking'
  | 'ReadyCheck'
  | 'ChampSelect'
  | 'GameStart'
  | 'InProgress'
  | 'Reconnect'
  | 'WaitingForStats'
  | 'PreEndOfGame'
  | 'EndOfGame'
  | (string & {});

export interface DraftFacts {
  isActive: boolean;
  phase: 'none' | 'planning' | 'banPick' | 'finalization' | 'unknown';
  /** False in ARAM, Mayhem, Arena and rotating modes. */
  usesLanes: boolean;
  /** ARAM and Mayhem: the bench question replaces the pick list. */
  isAram: boolean;
  myLocked: boolean;
  turn: { isAlly: boolean; isLocalPlayer: boolean; action: 'pick' | 'ban' | 'none' } | null;
  /** The action the advised seat is shown for when nobody of ours is on the clock. */
  targetAction: 'pick' | 'ban' | 'none' | null;
}

/**
 * GameStart is the loading screen; InProgress and Reconnect a running game (1.5: all three
 * "running"). After the game there is no moment of its own (the user did not want a postgame,
 * 07.11.2026): the window is back to waiting for the next champion select.
 */
const RUNNING = new Set(['InProgress', 'Reconnect']);

export function momentOf(draft: DraftFacts | null, gameflow: GameflowPhase | null): Moment {
  if (gameflow === 'GameStart') return 'loading';
  if (gameflow && RUNNING.has(gameflow)) return 'game';

  if (!draft || !draft.isActive) return 'lobby';

  if (draft.isAram) return 'aramBench';
  if (draft.phase === 'planning') return 'planning';
  if (draft.phase === 'finalization' || draft.myLocked) return 'locked';

  const turn = draft.turn;
  if (turn?.isLocalPlayer) return turn.action === 'ban' ? 'banning' : 'picking';
  if (turn?.isAlly && turn.action === 'pick') return 'teammate';

  // Enemies on the clock or a pause: show what the advised seat will need next.
  return draft.targetAction === 'ban' ? 'banning' : 'picking';
}

/**
 * The area a moment opens, and whether a manual choice still holds: it holds until the next
 * moment begins, exactly like a click on a slot holds until the next turn.
 */
export function nextArea(
  previousMoment: Moment | null,
  moment: Moment,
  manual: 'draft' | 'build' | 'game' | null,
  automatic: 'draft' | 'build' | 'game',
): { area: 'draft' | 'build' | 'game'; manual: 'draft' | 'build' | 'game' | null } {
  if (previousMoment !== moment) return { area: automatic, manual: null };
  return { area: manual ?? automatic, manual };
}
