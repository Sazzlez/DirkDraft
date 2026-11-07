// Port of DraftPilot.Core.Draft.SeatPriors and LanePredictor (1.5): which lane each seat of a team
// plays, as the most likely one-to-one assignment over all 120 permutations, with the marginal
// probability of every (seat, lane) pair as the confidence.

import { effectiveChampionId, type DraftSlot } from './draftState';
import { Lane, LANE_COUNT } from './lane';
import type { MetaLookup } from './metaLookup';

export class SeatPriors {
  private constructor(private readonly rows: readonly number[]) {}

  static readonly uniform = new SeatPriors(new Array<number>(LANE_COUNT * LANE_COUNT).fill(1 / LANE_COUNT));

  get(seatIndex: number, lane: Lane): number {
    if (lane === Lane.Unknown || seatIndex < 0 || seatIndex >= LANE_COUNT) return 1 / LANE_COUNT;
    return this.rows[seatIndex * LANE_COUNT + lane]!;
  }

  /** Reads pick_order_priors.json's `rows`; anything malformed falls back to uniform. */
  static fromJson(document: unknown): SeatPriors {
    const rows = (document as { rows?: unknown } | null)?.rows;
    if (!Array.isArray(rows)) return SeatPriors.uniform;
    const values = new Array<number>(LANE_COUNT * LANE_COUNT).fill(0);
    let seat = 0;
    for (const row of rows) {
      if (seat >= LANE_COUNT || !Array.isArray(row)) break;
      let lane = 0;
      let total = 0;
      for (const cell of row) {
        if (lane >= LANE_COUNT) break;
        if (typeof cell !== 'number' || Number.isNaN(cell)) return SeatPriors.uniform;
        const value = Math.max(0, cell);
        values[seat * LANE_COUNT + lane] = value;
        total += value;
        lane++;
      }
      if (total <= 0) return SeatPriors.uniform;
      for (let i = 0; i < LANE_COUNT; i++) values[seat * LANE_COUNT + i]! /= total;
      seat++;
    }
    return seat === LANE_COUNT ? new SeatPriors(values) : SeatPriors.uniform;
  }
}

/** Below this the lane is shown as a guess. */
export const LOW_CONFIDENCE = 0.6;

export interface LanePrediction {
  cellId: number;
  championId: number;
  lane: Lane;
  confidence: number;
  probabilities: number[];
  isManual: boolean;
}

export const isUncertain = (p: LanePrediction): boolean => !p.isManual && p.confidence < LOW_CONFIDENCE;

export class LanePredictionResult {
  private readonly byCell = new Map<number, LanePrediction>();
  private readonly byLane = new Map<Lane, LanePrediction>();

  constructor(readonly predictions: readonly LanePrediction[]) {
    for (const p of predictions) if (!this.byCell.has(p.cellId)) this.byCell.set(p.cellId, p);
    for (const p of predictions) if (p.lane !== Lane.Unknown && !this.byLane.has(p.lane)) this.byLane.set(p.lane, p);
  }

  static readonly empty = new LanePredictionResult([]);

  forCell(cellId: number): LanePrediction | null {
    return this.byCell.get(cellId) ?? null;
  }

  championOnLane(lane: Lane): number {
    return this.byLane.get(lane)?.championId ?? 0;
  }
}

/** A champion never seen on a lane still gets a sliver, so a surprise pick does not break the draft. */
const OFF_META_FLOOR = 0.01;

export class LanePredictor {
  constructor(
    private readonly meta: MetaLookup,
    private readonly seatPriors: SeatPriors = SeatPriors.uniform,
  ) {}

  predict(slots: readonly DraftSlot[], manualLanes: ReadonlyMap<number, Lane> | null = null): LanePredictionResult {
    if (slots.length === 0) return LanePredictionResult.empty;

    const seatCount = Math.min(slots.length, LANE_COUNT);
    const weights = this.buildWeights(slots, seatCount, manualLanes);

    const marginals = new Float64Array(seatCount * LANE_COUNT);
    let total = 0;
    let bestLikelihood = -1;
    const bestAssignment = new Array<number>(seatCount).fill(0);
    const assignment = new Array<number>(seatCount).fill(0);

    const enumerate = (depth: number, used: number, likelihood: number): void => {
      if (depth === seatCount) {
        if (likelihood <= 0) return;
        total += likelihood;
        for (let i = 0; i < seatCount; i++) marginals[i * LANE_COUNT + assignment[i]!]! += likelihood;
        if (likelihood > bestLikelihood) {
          bestLikelihood = likelihood;
          for (let i = 0; i < seatCount; i++) bestAssignment[i] = assignment[i]!;
        }
        return;
      }
      for (let candidate = 0; candidate < LANE_COUNT; candidate++) {
        if ((used & (1 << candidate)) !== 0) continue;
        const weight = weights[depth * LANE_COUNT + candidate]!;
        if (weight <= 0) continue;
        assignment[depth] = candidate;
        enumerate(depth + 1, used | (1 << candidate), likelihood * weight);
      }
    };
    enumerate(0, 0, 1);

    // Two seats forced onto the same lane zero out everything; predicting without the manual
    // constraints is more useful than predicting nothing.
    if (total <= 0 && manualLanes && manualLanes.size > 0) return this.predict(slots, null);

    const predictions: LanePrediction[] = [];
    slots.forEach((slot, seat) => {
      if (seat >= seatCount) {
        predictions.push({
          cellId: slot.cellId,
          championId: effectiveChampionId(slot),
          lane: Lane.Unknown,
          confidence: 0,
          probabilities: new Array<number>(LANE_COUNT).fill(0),
          isManual: false,
        });
        return;
      }
      const probabilities = new Array<number>(LANE_COUNT).fill(0);
      if (total > 0) for (let lane = 0; lane < LANE_COUNT; lane++) probabilities[lane] = marginals[seat * LANE_COUNT + lane]! / total;
      const fixed = manualLanes?.get(slot.cellId);
      const isManual = fixed !== undefined && fixed !== Lane.Unknown;
      const lane = total > 0 ? (bestAssignment[seat]! as Lane) : Lane.Unknown;
      const confidence = lane === Lane.Unknown ? 0 : probabilities[lane]!;
      predictions.push({ cellId: slot.cellId, championId: effectiveChampionId(slot), lane, confidence, probabilities, isManual });
    });
    return new LanePredictionResult(predictions);
  }

  private buildWeights(slots: readonly DraftSlot[], seatCount: number, manualLanes: ReadonlyMap<number, Lane> | null): Float64Array {
    const weights = new Float64Array(seatCount * LANE_COUNT);
    for (let seat = 0; seat < seatCount; seat++) {
      const slot = slots[seat]!;
      const fixed = manualLanes?.get(slot.cellId);
      if (fixed !== undefined && fixed !== Lane.Unknown) {
        weights[seat * LANE_COUNT + fixed] = 1;
        continue;
      }
      if (slot.assignedLane !== Lane.Unknown) {
        weights[seat * LANE_COUNT + slot.assignedLane] = 1;
        continue;
      }
      const championId = effectiveChampionId(slot);
      for (let lane = 0; lane < LANE_COUNT; lane++) {
        const seatPrior = this.seatPriors.get(seat, lane as Lane);
        if (championId === 0) {
          weights[seat * LANE_COUNT + lane] = seatPrior;
          continue;
        }
        const rolePrior = Math.max(this.meta.rolePrior(championId, lane as Lane), OFF_META_FLOOR);
        weights[seat * LANE_COUNT + lane] = rolePrior * seatPrior;
      }
    }
    return weights;
  }
}
