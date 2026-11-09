// Model C (tools/measure/factor.ts): swapping the sides must turn the prediction around exactly, and
// the fit must find a known strength and a known counter again.

import { describe, expect, it } from 'vitest';
import { Lane } from '../../src/core/model/lane';
import { counterKind, FactorModel, synergyKind, type FactorGame } from '../../tools/measure/factor';

const LANES = [Lane.Top, Lane.Jungle, Lane.Mid, Lane.Adc, Lane.Support];

/** Ten entities: 0–4 and 5–9 play the lanes in order; entity 10 is a second top. */
function game(blue: number[], red: number[], y: number, comp: number[] = []): FactorGame {
  return { blue: Int32Array.from(blue), red: Int32Array.from(red), blueLanes: LANES, redLanes: LANES, comp, y };
}

const swap = (g: FactorGame): FactorGame => ({ ...g, blue: g.red, red: g.blue, blueLanes: g.redLanes, redLanes: g.blueLanes, comp: g.comp.map((c) => -c), y: 1 - g.y });

function random(seed: number) {
  let s = seed;
  return () => {
    s = (s * 1664525 + 1013904223) >>> 0;
    return s / 2 ** 32;
  };
}

describe('model C', () => {
  it('turns the logit around when the sides swap', () => {
    const model = new FactorModel(11, 2);
    // Give every parameter some value, the side excluded.
    model.params.forEach((_, i) => (model.params[i] = i === 0 ? 0 : Math.sin(i * 1.7) * 0.3));
    const g = game([0, 1, 2, 3, 4], [10, 6, 7, 8, 9], 1, [1, -2]);

    expect(model.logit(swap(g))).toBeCloseTo(-model.logit(g), 12);
  });

  it('finds a known strength and a known counter again', () => {
    // Truth: entity 0 is strong on top (+0.4); entity 10 counters entity 0 in the top duel (+0.8).
    const next = random(3);
    const games: FactorGame[] = [];
    const truth: number[] = [];
    for (let i = 0; i < 12_000; i++) {
      const blueTop = next() < 0.5 ? 0 : 10;
      const redTop = next() < 0.5 ? 5 : blueTop === 0 ? 10 : 0;
      let z = 0;
      if (blueTop === 0) z += 0.4;
      if (redTop === 0) z -= 0.4;
      if (blueTop === 10 && redTop === 0) z += 0.8;
      if (blueTop === 0 && redTop === 10) z -= 0.8;
      const p = 1 / (1 + Math.exp(-z));
      truth.push(p);
      const y = next() < p ? 1 : 0;
      games.push(game([blueTop, 1, 2, 3, 4], [redTop, 6, 7, 8, 9], y));
    }
    const model = new FactorModel(11, 0, { dims: 2, l2Strength: 1e-4, l2Traits: 1e-4, rate: 0.02, batch: 256, epochs: 30, seed: 5 });
    model.train(games.slice(0, 9_000), games.slice(9_000, 10_000));

    const test = games.slice(10_000);
    // As good as the true probabilities, give or take sampling.
    const best = -test.reduce((sum, g, i) => sum + (g.y === 1 ? Math.log(truth[10_000 + i]!) : Math.log(1 - truth[10_000 + i]!)), 0) / test.length;
    expect(model.loss(test)).toBeLessThan(best + 0.005);
    expect(model.loss(test)).toBeLessThan(Math.log(2) - 0.01);
    // Strong top against the plain one, and the counter on top of that.
    expect(model.probability(game([0, 1, 2, 3, 4], [5, 6, 7, 8, 9], 1))).toBeGreaterThan(0.55);
    expect(model.probability(game([10, 1, 2, 3, 4], [0, 6, 7, 8, 9], 1))).toBeGreaterThan(0.55);
  });

  it('sorts pairs by kind', () => {
    expect(counterKind(Lane.Top, Lane.Top)).toBe(0);
    expect(counterKind(Lane.Support, Lane.Support)).toBe(4);
    expect(counterKind(Lane.Adc, Lane.Support)).toBe(5);
    expect(counterKind(Lane.Jungle, Lane.Mid)).toBe(6);
    expect(counterKind(Lane.Top, Lane.Mid)).toBe(7);
    expect(counterKind(Lane.Unknown, Lane.Mid)).toBe(7);
    expect(synergyKind(Lane.Adc, Lane.Support)).toBe(0);
  });
});
