// Golden parity for the build helpers that survive into 2.0: variant choice (Wilson lower bound)
// and the Mayhem augment filter. plans.json covers OP.GG's parsers, which 2.0 no longer has.

import { describe, expect, it } from 'vitest';
import { byWinRate, isCoarseAugment, rankAugments, wilsonLowerBound, type AugmentOption } from '../../src/core/model/advisors';
import { compare, readJson } from './harness';

interface ChoiceCase {
  input: { items: string[]; play: number; winRate: number }[];
  take: number;
  lowerBounds: number[];
  output: string[];
}

describe('golden build choice', () => {
  const golden = readJson<{ cases: ChoiceCase[] }>('./builds/choice.json');
  it.each(golden.cases.map((c, i) => [i, c] as const))('case %i', (_, c) => {
    expect(compare(c.lowerBounds, c.input.map(wilsonLowerBound), 1e-12)).toEqual([]);
    expect(byWinRate(c.input, c.take).map((s) => s.items[0])).toEqual(c.output);
  });
});

describe('golden augments', () => {
  const golden = readJson<{ input: AugmentOption[]; coarse: boolean[]; output: AugmentOption[] }>('./builds/augments.json');
  it('flags coarse figures', () => {
    expect(golden.input.map((a) => isCoarseAugment(a.performance))).toEqual(golden.coarse);
  });
  it('ranks', () => {
    expect(compare(golden.output, rankAugments(golden.input, 8), 1e-12)).toEqual([]);
  });
});
