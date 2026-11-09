// Model C of docs/draft-konzept.md §4: one model learned on every game at once.
//
//   logit(blue wins) = side
//     + Σ blue strength(champion, lane) − Σ red strength
//     + Σ blue pairs ⟨u_a ∘ s_kind, u_b⟩ − the same for red          (synergy, by kind of pair)
//     + Σ blue a, red b  ⟨r_kind, p_a ∘ q_b − p_b ∘ q_a⟩              (counter, by kind of pair)
//     + w · comp (blue minus red)
//
// Every champion on a lane gets a few learned traits (u, p, q). A pair with few games inherits what
// similar pairs show — Blitzcrank against a rare ADC without escape looks like Blitzcrank against the
// others — where counting falls back to neutral. Swapping the sides turns the logit around exactly:
// strength and synergy are the same function for both teams, the counter is antisymmetric by build.
// Fitted with Adam on the log-loss, L2 on everything but the side, stopped on held-back games.

import { Lane } from '../../src/core/model/lane';

/** Kinds of pair within a team (model.ts SYNERGY_TYPES). */
export const SYNERGY_KINDS = 6;
/** Kinds across the teams: duel per lane (0–4), bot cross, jungle ↔ laner, other. */
export const COUNTER_KINDS = 8;

export function counterKind(mine: Lane, theirs: Lane): number {
  if (mine === Lane.Unknown || theirs === Lane.Unknown) return 7;
  if (mine === theirs) return mine;
  if ((mine === Lane.Adc && theirs === Lane.Support) || (mine === Lane.Support && theirs === Lane.Adc)) return 5;
  if (mine === Lane.Jungle || theirs === Lane.Jungle) return 6;
  return 7;
}

export function synergyKind(a: Lane, b: Lane): number {
  const has = (lane: Lane) => a === lane || b === lane;
  if (has(Lane.Adc) && has(Lane.Support)) return 0;
  if (has(Lane.Jungle) && has(Lane.Mid)) return 1;
  if (has(Lane.Jungle) && has(Lane.Top)) return 2;
  if (has(Lane.Jungle) && (has(Lane.Adc) || has(Lane.Support))) return 3;
  if (has(Lane.Mid) && has(Lane.Support)) return 4;
  return 5;
}

/** One game for the model: entity per seat (−1 unknown), lanes, comp features blue minus red. */
export interface FactorGame {
  blue: Int32Array;
  red: Int32Array;
  blueLanes: Lane[];
  redLanes: Lane[];
  comp: readonly number[];
  y: number;
}

export interface FactorOptions {
  dims: number;
  /** L2 on the strengths and on the traits. */
  l2Strength: number;
  l2Traits: number;
  rate: number;
  batch: number;
  epochs: number;
  seed: number;
}

export const DEFAULT_FACTOR: FactorOptions = { dims: 4, l2Strength: 1e-3, l2Traits: 1e-3, rate: 0.01, batch: 256, epochs: 40, seed: 7 };

/** Deterministic noise for the start values and the order of the games. */
function random(seed: number): () => number {
  let s = seed >>> 0 || 1;
  return () => {
    s ^= s << 13;
    s ^= s >>> 17;
    s ^= s << 5;
    return (s >>> 0) / 2 ** 32;
  };
}

export class FactorModel {
  readonly params: Float64Array;
  private readonly k: number;
  private readonly o: { side: number; strength: number; u: number; p: number; q: number; s: number; r: number; w: number; end: number };

  constructor(
    readonly entities: number,
    readonly compSize: number,
    readonly options: FactorOptions = DEFAULT_FACTOR,
  ) {
    const k = (this.k = options.dims);
    const side = 0;
    const strength = 1;
    const u = strength + entities;
    const p = u + entities * k;
    const q = p + entities * k;
    const s = q + entities * k;
    const r = s + SYNERGY_KINDS * k;
    const w = r + COUNTER_KINDS * k;
    this.o = { side, strength, u, p, q, s, r, w, end: w + compSize };
    this.params = new Float64Array(this.o.end);
    const next = random(options.seed);
    const gauss = () => Math.sqrt(-2 * Math.log(next() + 1e-12)) * Math.cos(2 * Math.PI * next());
    for (let i = u; i < s; i++) this.params[i] = 0.05 * gauss();
    for (let i = s; i < w; i++) this.params[i] = 1;
  }

  /** The parts of blue's logit, for the report: which part moves the prediction how much. */
  parts(game: FactorGame): { side: number; strength: number; synergy: number; counter: number; comp: number } {
    const t = this.params;
    const { k, o } = this;
    let strength = 0;
    for (const e of game.blue) if (e >= 0) strength += t[o.strength + e]!;
    for (const e of game.red) if (e >= 0) strength -= t[o.strength + e]!;

    let synergy = 0;
    for (const [ids, lanes, sign] of [
      [game.blue, game.blueLanes, 1],
      [game.red, game.redLanes, -1],
    ] as const) {
      for (let i = 0; i < 5; i++) {
        const a = ids[i]!;
        if (a < 0) continue;
        for (let j = i + 1; j < 5; j++) {
          const b = ids[j]!;
          if (b < 0) continue;
          const kind = synergyKind(lanes[i]!, lanes[j]!);
          let dot = 0;
          for (let d = 0; d < k; d++) dot += t[o.u + a * k + d]! * t[o.u + b * k + d]! * t[o.s + kind * k + d]!;
          synergy += sign * dot;
        }
      }
    }

    let counter = 0;
    for (let i = 0; i < 5; i++) {
      const a = game.blue[i]!;
      if (a < 0) continue;
      for (let j = 0; j < 5; j++) {
        const b = game.red[j]!;
        if (b < 0) continue;
        const kind = counterKind(game.blueLanes[i]!, game.redLanes[j]!);
        for (let d = 0; d < k; d++)
          counter += t[o.r + kind * k + d]! * (t[o.p + a * k + d]! * t[o.q + b * k + d]! - t[o.p + b * k + d]! * t[o.q + a * k + d]!);
      }
    }

    let comp = 0;
    for (let m = 0; m < this.compSize; m++) comp += t[o.w + m]! * (game.comp[m] ?? 0);
    return { side: t[o.side]!, strength, synergy, counter, comp };
  }

  logit(game: FactorGame): number {
    const p = this.parts(game);
    return p.side + p.strength + p.synergy + p.counter + p.comp;
  }

  probability(game: FactorGame): number {
    return 1 / (1 + Math.exp(-this.logit(game)));
  }

  /** Adds d(logit)/d(param) · scale into `grad`. */
  private backward(game: FactorGame, scale: number, grad: Float64Array): void {
    const t = this.params;
    const { k, o } = this;
    grad[o.side]! += scale;
    for (const e of game.blue) if (e >= 0) grad[o.strength + e]! += scale;
    for (const e of game.red) if (e >= 0) grad[o.strength + e]! -= scale;

    for (const [ids, lanes, sign] of [
      [game.blue, game.blueLanes, 1],
      [game.red, game.redLanes, -1],
    ] as const) {
      const g = scale * sign;
      for (let i = 0; i < 5; i++) {
        const a = ids[i]!;
        if (a < 0) continue;
        for (let j = i + 1; j < 5; j++) {
          const b = ids[j]!;
          if (b < 0) continue;
          const kind = synergyKind(lanes[i]!, lanes[j]!);
          for (let d = 0; d < k; d++) {
            const ua = t[o.u + a * k + d]!;
            const ub = t[o.u + b * k + d]!;
            const sk = t[o.s + kind * k + d]!;
            grad[o.u + a * k + d]! += g * ub * sk;
            grad[o.u + b * k + d]! += g * ua * sk;
            grad[o.s + kind * k + d]! += g * ua * ub;
          }
        }
      }
    }

    for (let i = 0; i < 5; i++) {
      const a = game.blue[i]!;
      if (a < 0) continue;
      for (let j = 0; j < 5; j++) {
        const b = game.red[j]!;
        if (b < 0) continue;
        const kind = counterKind(game.blueLanes[i]!, game.redLanes[j]!);
        for (let d = 0; d < k; d++) {
          const pa = t[o.p + a * k + d]!;
          const qa = t[o.q + a * k + d]!;
          const pb = t[o.p + b * k + d]!;
          const qb = t[o.q + b * k + d]!;
          const rk = t[o.r + kind * k + d]!;
          grad[o.p + a * k + d]! += scale * rk * qb;
          grad[o.q + b * k + d]! += scale * rk * pa;
          grad[o.p + b * k + d]! -= scale * rk * qa;
          grad[o.q + a * k + d]! -= scale * rk * pb;
          grad[o.r + kind * k + d]! += scale * (pa * qb - pb * qa);
        }
      }
    }

    for (let m = 0; m < this.compSize; m++) grad[o.w + m]! += scale * (game.comp[m] ?? 0);
  }

  /** Mean log-loss over the games. */
  loss(games: readonly FactorGame[]): number {
    let total = 0;
    for (const g of games) {
      const p = Math.min(1 - 1e-12, Math.max(1e-12, this.probability(g)));
      total -= g.y === 1 ? Math.log(p) : Math.log(1 - p);
    }
    return games.length > 0 ? total / games.length : 0;
  }

  /** Adam on the log-loss; keeps the parameters of the epoch best on `validation`. */
  train(games: readonly FactorGame[], validation: readonly FactorGame[]): { epochs: number; validationLoss: number } {
    const { o, options } = this;
    const n = this.params.length;
    const m = new Float64Array(n);
    const v = new Float64Array(n);
    const grad = new Float64Array(n);
    const order = games.map((_, i) => i);
    const next = random(options.seed + 1);
    const beta1 = 0.9;
    const beta2 = 0.999;
    let step = 0;
    let best = this.loss(validation);
    let bestParams = Float64Array.from(this.params);
    let bestEpoch = 0;
    let worse = 0;

    for (let epoch = 1; epoch <= options.epochs; epoch++) {
      for (let i = order.length - 1; i > 0; i--) {
        const j = Math.floor(next() * (i + 1));
        [order[i], order[j]] = [order[j]!, order[i]!];
      }
      for (let start = 0; start < order.length; start += options.batch) {
        grad.fill(0);
        const end = Math.min(order.length, start + options.batch);
        const size = end - start;
        for (let b = start; b < end; b++) {
          const game = games[order[b]!]!;
          this.backward(game, (this.probability(game) - game.y) / size, grad);
        }
        // L2: strengths and traits as set, the kind weights and comp lightly, the side not at all.
        for (let i = o.strength; i < o.u; i++) grad[i]! += options.l2Strength * this.params[i]!;
        for (let i = o.u; i < o.s; i++) grad[i]! += options.l2Traits * this.params[i]!;
        for (let i = o.s; i < o.end; i++) grad[i]! += 1e-4 * this.params[i]!;
        step++;
        const c1 = 1 - beta1 ** step;
        const c2 = 1 - beta2 ** step;
        for (let i = 0; i < n; i++) {
          const g = grad[i]!;
          if (g === 0 && m[i] === 0) continue;
          m[i] = beta1 * m[i]! + (1 - beta1) * g;
          v[i] = beta2 * v[i]! + (1 - beta2) * g * g;
          this.params[i]! -= (options.rate * (m[i]! / c1)) / (Math.sqrt(v[i]! / c2) + 1e-8);
        }
      }
      const current = this.loss(validation);
      if (current < best - 1e-6) {
        best = current;
        bestParams = Float64Array.from(this.params);
        bestEpoch = epoch;
        worse = 0;
      } else if (++worse >= 4) {
        break;
      }
    }
    this.params.set(bestParams);
    return { epochs: bestEpoch, validationLoss: best };
  }
}
