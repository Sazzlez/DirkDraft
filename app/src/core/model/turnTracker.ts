// Port of DraftPilot.Core.Draft.TurnTracker (1.5): which ally seat the recommendations are for.
// Follows the clock; a manual click holds until the clock passes to someone else.

import { isLocked, localSlot, type ActiveTurn, type DraftSlot, type DraftState, type TurnAction } from './draftState';

export interface RecommendationTarget {
  slot: DraftSlot;
  action: TurnAction;
  isFollowingTurn: boolean;
}

export const isSettled = (target: RecommendationTarget): boolean => !target.isFollowingTurn && isLocked(target.slot);

export class TurnTracker {
  private manualCellId: number | null = null;
  private manualTurnCellId: number | null = null;
  private lastResolvedCellId: number | null = null;

  selectManually(cellId: number, state: DraftState): void {
    this.manualCellId = cellId;
    this.manualTurnCellId = state.turn?.cellId ?? null;
  }

  reset(): void {
    this.manualCellId = null;
    this.manualTurnCellId = null;
    this.lastResolvedCellId = null;
  }

  resolve(state: DraftState): RecommendationTarget | null {
    if (!state.isActive || state.allies.length === 0) return null;

    const turn = state.turn;
    const isAllyTurn = turn?.isAlly === true;

    if (this.manualCellId !== null && (turn?.cellId ?? null) === this.manualTurnCellId) {
      const manualSlot = findAlly(state, this.manualCellId);
      if (manualSlot) return build(manualSlot, turn, isAllyTurn && turn!.cellId === this.manualCellId);
    }
    this.manualCellId = null;

    if (isAllyTurn) {
      const activeSlot = findAlly(state, turn!.cellId);
      if (activeSlot) {
        this.lastResolvedCellId = activeSlot.cellId;
        return { slot: activeSlot, action: turn!.action, isFollowingTurn: true };
      }
    }

    const fallback =
      (this.lastResolvedCellId !== null ? findAlly(state, this.lastResolvedCellId) : null) ??
      localSlot(state) ??
      state.allies[0]!;
    this.lastResolvedCellId = fallback.cellId;
    return build(fallback, turn, false);
  }
}

function build(slot: DraftSlot, turn: ActiveTurn | null, isFollowingTurn: boolean): RecommendationTarget {
  // Outside a live turn picks are the useful default; during a ban round mirror the bans.
  const action: TurnAction =
    isFollowingTurn && turn ? turn.action : turn?.action === 'ban' ? 'ban' : 'pick';
  return { slot, action, isFollowingTurn };
}

const findAlly = (state: DraftState, cellId: number): DraftSlot | null =>
  state.allies.find((slot) => slot.cellId === cellId) ?? null;
