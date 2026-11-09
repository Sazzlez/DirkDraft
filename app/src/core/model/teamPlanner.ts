// D1, the team mode: the own team's open seats planned together instead of one by one — for a fixed
// five (Flex). Each open seat brings its best few picks by the usual score; a beam search picks the
// combination with the most win in total, without a champion twice, and counts the synergy between
// the new picks, damped as everywhere. What the new picks do to each other's comp is left out: the
// comp term stays the one each seat got against the line-up as it stands.
//
// The order: last the seat most exposed (open counters while its lane opponent is open), first the
// one least so (fewest open counters, a flex pick one fewer); with nobody exposed, no order. Without
// a measured blind risk (concept §4, after Phase 2) the open counters of B1 are the measure.

import { isLocked, type DraftState } from './draftState';
import { Lane } from './lane';
import type { LanePredictionResult } from './lanePredictor';
import type { MetaLookup } from './metaLookup';
import type { Recommendation, Recommender } from './recommender';
import { logit, SYNERGY_DAMPING } from './scoreModel';

/** Picks per open seat the search looks at, and combinations it keeps per step. */
export const TEAM_CANDIDATES = 6;
export const TEAM_BEAM = 40;

export interface TeamPick {
  cellId: number;
  lane: Lane;
  item: Recommendation;
}

export interface TeamPlan {
  picks: TeamPick[];
  /** The seat best picking first, and last; null with a single open seat or no difference. */
  first: number | null;
  last: number | null;
}

/** How exposed a pick is while its lane opponent is open: open counters, one fewer for a flex pick. */
export const blindRisk = (item: Recommendation): number => (item.openCounters ?? 0) - (item.flexLanes ? 1 : 0);

export function planTeam(
  recommender: Recommender,
  meta: MetaLookup,
  state: DraftState,
  enemyLanes: LanePredictionResult,
  allyLanes: LanePredictionResult,
  selectable: ReadonlySet<number> | null,
): TeamPlan | null {
  const lists = state.allies
    .filter((slot) => !isLocked(slot))
    .map((slot) => {
      const own = slot.cellId === state.localCellId ? selectable : null;
      const set = recommender.recommend(state, { slot, action: 'pick', isFollowingTurn: false }, enemyLanes, allyLanes, own, TEAM_CANDIDATES);
      return { cellId: slot.cellId, lane: set.lane, items: set.items };
    })
    .filter((list) => list.items.length > 0);
  if (lists.length === 0) return null;

  type Node = { chosen: Recommendation[]; total: number };
  let beam: Node[] = [{ chosen: [], total: 0 }];
  for (const list of lists) {
    const next: Node[] = [];
    for (const node of beam) {
      for (const item of list.items) {
        if (node.chosen.some((c) => c.championId === item.championId)) continue;
        let total = node.total + logit(item.score);
        // Each pair sits in two seats' synergy, each a mean over four teammates: half the damped edge.
        for (const other of node.chosen) {
          const synergy = meta.synergy(item.championId, other.championId);
          if (synergy) total += (SYNERGY_DAMPING / 2) * (logit(synergy.winRate) - logit(synergy.expected));
        }
        next.push({ chosen: [...node.chosen, item], total });
      }
    }
    if (next.length === 0) break;
    next.sort((a, b) => b.total - a.total);
    beam = next.slice(0, TEAM_BEAM);
  }
  const best = beam[0]!;
  const picks = best.chosen.map((item, i) => ({ cellId: lists[i]!.cellId, lane: lists[i]!.lane, item }));

  // Last: the seat really exposed — open counters while its lane opponent is still open. Without one
  // the order does not matter, and nothing is said.
  let last: TeamPick | null = null;
  for (const pick of picks) if ((pick.item.openCounters ?? 0) >= 1 && (!last || blindRisk(pick.item) > blindRisk(last.item))) last = pick;
  let first: TeamPick | null = null;
  if (last) for (const pick of picks) if (pick !== last && blindRisk(pick.item) < blindRisk(last.item) && (!first || blindRisk(pick.item) < blindRisk(first.item))) first = pick;
  return { picks, first: first && last ? first.cellId : null, last: first && last ? last.cellId : null };
}
