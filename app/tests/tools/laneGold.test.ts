// The lane-gold measurement (tools/measure/laneGold.ts): the gold after ten minutes is read from the
// right places, the split finds a known champion and matchup spread again — and none where there is
// none — and the conversion finds what a lead on one lane is worth.

import { describe, expect, it } from 'vitest';
import { Lane } from '../../src/core/model/lane';
import { decode, type Game } from '../../tools/measure/games';
import { conversion, edgeOf, goldDuels, laneGoldDiffs, matchupGold, splitLane, type GoldDuel } from '../../tools/measure/laneGold';

function random(seed: number) {
  let s = seed;
  const next = () => {
    s = (s * 1664525 + 1013904223) >>> 0;
    return s / 2 ** 32;
  };
  const gauss = () => Math.sqrt(-2 * Math.log(next() + 1e-12)) * Math.cos(2 * Math.PI * next());
  return { next, gauss };
}

const sd = (values: readonly number[]) => {
  const mean = values.reduce((s, v) => s + v, 0) / values.length;
  return Math.sqrt(values.reduce((s, v) => s + (v - mean) ** 2, 0) / values.length);
};

/** Ten seats: blue Top … Support with champions 1–5, red with 6–10. */
const seat = (championId: number, lane: Lane) => championId * 8 + lane + 1;
const LINE_SEATS = [seat(1, Lane.Top), seat(2, Lane.Jungle), seat(3, Lane.Mid), seat(4, Lane.Adc), seat(5, Lane.Support), seat(6, Lane.Top), seat(7, Lane.Jungle), seat(8, Lane.Mid), seat(9, Lane.Adc), seat(10, Lane.Support)];

/** Synthetic duels on one lane: 20 champions, a strength each, an effect per pair, noise 900 gold. */
function world(seed: number, championSpread: number, matchupSpread: number, duels = 60_000) {
  const { next, gauss } = random(seed);
  const strength = Array.from({ length: 21 }, () => championSpread * gauss());
  const pair = new Map<string, number>();
  for (let a = 1; a <= 20; a++) for (let b = a + 1; b <= 20; b++) pair.set(`${a}|${b}`, matchupSpread * gauss());
  const list: GoldDuel[] = [];
  for (let i = 0; i < duels; i++) {
    const a = 1 + Math.floor(next() * 20);
    let b = 1 + Math.floor(next() * 19);
    if (b >= a) b++;
    const m = a < b ? pair.get(`${a}|${b}`)! : -pair.get(`${b}|${a}`)!;
    list.push({ a, b, gold: strength[a]! - strength[b]! + m + 900 * gauss() });
  }
  return { list, strength: strength.slice(1), pair };
}

describe('lane gold', () => {
  it('reads the gold after ten minutes only from a complete lane part', () => {
    const gold = [3000, 3100, 3200, 3300, 3400, 3500, 3600, 3700, 3800, 3900];
    const full = [...LINE_SEATS, 1, 30, 0, -1, 0, ...gold, ...gold.map((g) => g * 2)];
    expect(decode(full)!.gold).toEqual(gold);
    // Format 3: a line without its duels, closed by −1.
    expect(decode([...full, -1])!.gold).toEqual(gold);
    expect(decode(full.slice(0, 15))!.gold).toBeUndefined();
    const missing = [...full];
    missing[17] = 0;
    expect(decode(missing)!.gold).toBeUndefined();
  });

  it('takes the difference between the lane opponents, blue minus red', () => {
    const gold = [4000, 3100, 3200, 3300, 3400, 3500, 3600, 3700, 3800, 3900];
    const game = decode([...LINE_SEATS, 1, 30, 0, -1, 0, ...gold, ...gold])!;
    expect(laneGoldDiffs(game)).toEqual([500, -500, -500, -500, -500]);
    expect(goldDuels([game], Lane.Top)).toEqual([{ a: 1, b: 6, gold: 500 }]);
  });

  it('finds a known champion and matchup spread again', () => {
    const { list, strength, pair } = world(3, 150, 150);
    const split = splitLane(list);

    expect(split.champions).toBe(20);
    expect(split.championSpread).toBeGreaterThan(0.75 * sd(strength));
    expect(split.championSpread).toBeLessThan(1.25 * sd(strength));
    expect(split.matchupSpread).toBeGreaterThan(0.75 * sd([...pair.values()]));
    expect(split.matchupSpread).toBeLessThan(1.25 * sd([...pair.values()]));
    expect(split.matchupZ).toBeGreaterThan(5);
    expect(split.prior).toBeGreaterThan(15);
    expect(split.prior).toBeLessThan(80);
  });

  it('finds no matchup where there is none', () => {
    const { list } = world(5, 150, 0);
    const split = splitLane(list);

    expect(split.matchupZ).toBeLessThan(2.5);
    expect(split.matchupUpper).toBeLessThan(60);
    // An empty or near-empty table: no pair moves more than a few gold.
    const edges = matchupGold(list, split);
    for (const edge of edges.values()) expect(Math.abs(edge)).toBeLessThan(80);
  });

  it('keeps the sign of a strong pair, both ways round', () => {
    const { list } = world(7, 150, 100);
    // Champion 1 beats champion 2 by 600 gold more than both strengths say.
    const extra: GoldDuel[] = list.map((d) => (d.a === 1 && d.b === 2 ? { ...d, gold: d.gold + 600 } : d.a === 2 && d.b === 1 ? { ...d, gold: d.gold - 600 } : d));
    const split = splitLane(extra);
    const edges = matchupGold(extra, split);

    expect(edgeOf(edges, 1, 2)).toBeGreaterThan(300);
    expect(edgeOf(edges, 2, 1)).toBeLessThan(-300);
    expect(edgeOf(edges, 1, 1)).toBe(0);
  });

  it('finds what a lead on one lane is worth', () => {
    const { next, gauss } = random(11);
    const games: Game[] = [];
    const lanes = [Lane.Top, Lane.Jungle, Lane.Mid, Lane.Adc, Lane.Support];
    for (let i = 0; i < 20_000; i++) {
      const gold = Array.from({ length: 10 }, () => 3500 + 600 * gauss());
      // Only the top lane decides: 0,5 log-odds per 1.000 gold.
      const z = 0.5 * ((gold[0]! - gold[5]!) / 1000);
      games.push({
        blue: lanes.map((lane, k) => ({ championId: k + 1, lane })),
        red: lanes.map((lane, k) => ({ championId: k + 6, lane })),
        blueWins: next() < 1 / (1 + Math.exp(-z)) ? 1 : 0,
        minutes: 30,
        gold,
      });
    }
    const result = conversion(games)!;

    expect(result.games).toBe(20_000);
    expect(result.fit.coefficients[1]).toBeGreaterThan(0.4);
    expect(result.fit.coefficients[1]).toBeLessThan(0.6);
    for (const j of [2, 3, 4, 5]) expect(Math.abs(result.fit.coefficients[j]!)).toBeLessThan(0.1);
  });
});
