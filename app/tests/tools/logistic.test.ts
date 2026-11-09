// The regression behind the measurement tool: it has to find a known effect again, with an honest
// error bar, and leave alone what is already in the offset.

import { describe, expect, it } from 'vitest';
import { fitLogistic, invert, logLoss, predict } from '../../tools/measure/logistic';

/** Deterministic noise: the same data on every run. */
function random(seed: number): () => number {
  let s = seed >>> 0;
  return () => {
    s = (s * 1664525 + 1013904223) >>> 0;
    return s / 2 ** 32;
  };
}

function sample(n: number, beta: [number, number, number], offsetOf: (i: number) => number = () => 0) {
  const next = random(7);
  const x: number[][] = [];
  const y: number[] = [];
  const offset: number[] = [];
  for (let i = 0; i < n; i++) {
    const a = next() < 0.5 ? -1 : 1;
    const b = Math.floor(next() * 5) - 2;
    const o = offsetOf(i);
    const p = 1 / (1 + Math.exp(-(o + beta[0] + beta[1] * a + beta[2] * b)));
    x.push([a, b]);
    y.push(next() < p ? 1 : 0);
    offset.push(o);
  }
  return { x, y, offset };
}

describe('logistic regression', () => {
  it('inverts a matrix', () => {
    const m = [
      [4, 1, 0],
      [1, 3, 1],
      [0, 1, 2],
    ];
    const inv = invert(m);
    for (let i = 0; i < 3; i++)
      for (let j = 0; j < 3; j++) expect(m[i]!.reduce((s, v, k) => s + v * inv[k]![j]!, 0)).toBeCloseTo(i === j ? 1 : 0, 12);
  });

  it('finds known effects again within their error bars', () => {
    const { x, y } = sample(20_000, [0.1, 0.3, -0.15]);
    const fit = fitLogistic(x, y);

    expect(fit.converged).toBe(true);
    [0.1, 0.3, -0.15].forEach((truth, j) => expect(Math.abs(fit.coefficients[j]! - truth)).toBeLessThan(3 * fit.standardErrors[j]!));
    // 20.000 games pin an effect to about ±0,015 log-odds, ±0,4 points.
    expect(fit.standardErrors[1]!).toBeLessThan(0.02);
  });

  it('measures only what the offset does not already explain', () => {
    const { x, y, offset } = sample(20_000, [0, 0.2, 0], (i) => ((i % 7) - 3) * 0.2);
    const withOffset = fitLogistic(x, y, offset);

    expect(Math.abs(withOffset.coefficients[1]! - 0.2)).toBeLessThan(3 * withOffset.standardErrors[1]!);
    expect(Math.abs(withOffset.coefficients[0]!)).toBeLessThan(3 * withOffset.standardErrors[0]!);
  });

  it('scores a better model with a lower log-loss', () => {
    const { x, y } = sample(10_000, [0, 0.8, 0]);
    const full = fitLogistic(x, y);
    const empty = fitLogistic(x.map(() => []), y);

    expect(logLoss(full, x, y)).toBeLessThan(logLoss(empty, x.map(() => []), y));
    expect(predict(full, [1, 0])).toBeGreaterThan(predict(full, [-1, 0]));
  });
});
