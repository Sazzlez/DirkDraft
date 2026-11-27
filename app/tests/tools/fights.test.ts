// The teamfight measurement (tools/measure/fights.ts): the fight part of a game line (Spiel-Liste
// Version 5) is read as the collector writes it (RiotGames.Format, tests in RiotGamesTests.cs), the
// buffs are active for the right seconds, the right fights stay out — and on a synthetic world with a
// known truth the ladder takes the factor that matters and leaves out the one that does not.

import { mkdtempSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import path from 'node:path';
import { gzipSync } from 'node:zlib';
import { afterAll, describe, expect, it } from 'vitest';
import { buffsAt, exclusionOf, featuresOf, fightPartAt, main, readFights, type Fight } from '../../tools/measure/fights';

/** Ten seats: blue Top … Support with champions 1–5, red with 6–10. */
const seat = (championId: number, lane: number) => championId * 8 + lane + 1;
const SEATS = [seat(1, 0), seat(2, 1), seat(3, 2), seat(4, 3), seat(5, 4), seat(6, 0), seat(7, 1), seat(8, 2), seat(9, 3), seat(10, 4)];

/** The draft part and the lane part (35 numbers), then whatever follows. */
const line = (...rest: number[]): number[] => [...SEATS, 1, 30, 0, -1, 0, ...new Array<number>(20).fill(3000), ...rest];

// One duel at 200 s, as in RiotGamesTests.cs.
const DUEL = [13, 200, 0, 5, 0, 3, 750, 15, 0, 5, 2, 450, 0, 0];
const ALL_SEEN = 0b11111_11111;
const players = (level = 12, gold = 8000, change: Record<number, [number, number]> = {}): number[] =>
  Array.from({ length: 10 }, (_, place) => change[place] ?? [level, gold]).flat();
const fight = (second: number, blueKills: number, redKills: number, dead: number, seen: number, numbers: number[]): number[] => [25, second, blueKills, redKills, dead, seen, ...numbers];

describe('the fight part of a line', () => {
  // Objectives: Infernal drake to blue at 400 s, Baron to red at 1.200 s, soul to blue at 1.300 s.
  const objectives = [9, 400, 0, 1, 1200, 1, 8, 1300, 0, 21];
  const full = line(...DUEL, -2, -3, ...objectives, ...fight(1250, 3, 1, 0, ALL_SEEN, players(13, 9000, { 0: [15, 11000] })), ...fight(1500, 2, 2, 32, 463, players()));

  it('is found behind the duels and their −2, behind a −1, or right after the lane part', () => {
    expect(fightPartAt(full)).toBe(35 + DUEL.length + 1);
    expect(fightPartAt(line(-1, -3, 0))).toBe(36);
    expect(fightPartAt(line(-3, 0))).toBe(35);
    // Lines before version 5, or not sampled, have none.
    expect(fightPartAt(line(...DUEL, -2))).toBe(-1);
    expect(fightPartAt(line(-1))).toBe(-1);
    expect(fightPartAt(line())).toBe(-1);
    expect(fightPartAt(SEATS)).toBe(-1);
  });

  it('reads every fight with the seats, the masks and what each side brings', () => {
    const { fights, sampled } = readFights([full, line(...DUEL, -2), line(-1, -3, 0)], 100);
    expect(sampled).toBe(2);
    expect(fights).toHaveLength(2);
    const [first, second] = fights as [Fight, Fight];
    expect(first).toMatchObject({ game: 100, second: 1250, blueKills: 3, redKills: 1, dead: 0, seen: ALL_SEEN, y: 1 });
    expect(first.players[0]).toEqual({ place: 0, championId: 1, lane: 0, level: 15, gold: 11000 });
    expect(first.players[9]).toEqual({ place: 9, championId: 10, lane: 4, level: 13, gold: 9000 });
    // At 1.250 s blue has its drake (no soul yet), red has Baron since 50 s.
    expect(first.blue).toEqual({ dragons: 1, soul: false, baron: false, elder: false, atakhan: 0 });
    expect(first.red).toEqual({ dragons: 0, soul: false, baron: true, elder: false, atakhan: 0 });
    expect(second).toMatchObject({ second: 1500, dead: 32, seen: 463, y: 0.5 });
    expect(second.blue.soul).toBe(true);
    expect(second.red.baron).toBe(false); // 300 s after it was taken
  });
});

describe('buffs', () => {
  const objectives = [
    [1000, 0, 8], // Baron, blue
    [1100, 1, 7], // Elder, red
    [500, 1, 2],
    [700, 1, 3], // two drakes, red
    [900, 1, 9], // Atakhan, red
    [1400, 1, 22], // Mountain soul, red, later
  ];

  it('last as long as Riot gives them, and only count what was taken before', () => {
    expect(buffsAt(objectives, 0, 1180).baron).toBe(true);
    expect(buffsAt(objectives, 0, 1181).baron).toBe(false);
    expect(buffsAt(objectives, 0, 1000).baron).toBe(false); // taken in the same second: not yet
    expect(buffsAt(objectives, 1, 1250).elder).toBe(true);
    expect(buffsAt(objectives, 1, 1251).elder).toBe(false);
    expect(buffsAt(objectives, 1, 600)).toEqual({ dragons: 1, soul: false, baron: false, elder: false, atakhan: 0 });
    expect(buffsAt(objectives, 1, 1500)).toEqual({ dragons: 2, soul: true, baron: false, elder: false, atakhan: 1 });
    expect(buffsAt(objectives, 0, 1500).dragons).toBe(0);
  });
});

describe('which fights count', () => {
  const read = (...numbers: number[]): Fight => readFights([line(-3, 0, ...numbers)]).fights[0]!;

  it('leaves out a team that was missing someone, a skirmish, an AFK player and a draw', () => {
    expect(exclusionOf(read(...fight(1000, 2, 1, 0, ALL_SEEN, players())), 4)).toBeNull();
    expect(exclusionOf(read(...fight(1000, 2, 1, 1 << 7, ALL_SEEN, players())), 4)).toBe('dead');
    // Three of blue (places 0–2) and all of red seen: a teamfight for three a side, not for four.
    const three = read(...fight(1000, 2, 1, 0, 0b11111_00111, players()));
    expect(exclusionOf(three, 4)).toBe('small');
    expect(exclusionOf(three, 3)).toBeNull();
    expect(exclusionOf(read(...fight(1000, 2, 1, 0, ALL_SEEN, players(12, 8000, { 3: [7, 2000] }))), 4)).toBe('afk');
    expect(exclusionOf(read(...fight(1000, 2, 1, 0, ALL_SEEN, players(12, 8000, { 3: [8, 2000] }))), 4)).toBeNull(); // 4 below: behind, not gone
    expect(exclusionOf(read(...fight(1000, 2, 2, 0, ALL_SEEN, players())), 4)).toBe('draw');
  });

  it('measures the difference from blue’s side', () => {
    const f = read(...fight(1900, 2, 1, 0, ALL_SEEN, players(12, 8000, { 0: [14, 12000], 7: [11, 6000] })));
    const features = featuresOf(f);
    expect(features).toMatchObject({ dLevel: 3, dGold: 6, dCarry: 4, phase: 2, baron: 0 });
    // Place 0 is blue's top, place 7 red's mid: both carries; jungle and support even.
    expect(features).toMatchObject({ dGoldCarry: 6, dGoldRest: 0 });
    // Blue 44.000, red 38.000: 6.000 gold on a mean team worth of 41.000, 1,46 tenths.
    expect(features.dRelative).toBeCloseTo((10 * 6000) / 41000, 6);
  });
});

describe('the measurement on a synthetic world', () => {
  const dir = mkdtempSync(path.join(tmpdir(), 'fights-'));
  afterAll(() => rmSync(dir, { recursive: true, force: true }));

  function random(seed: number) {
    let s = seed;
    const next = () => {
      s = (s * 1664525 + 1013904223) >>> 0;
      return s / 2 ** 32;
    };
    const gauss = () => Math.sqrt(-2 * Math.log(next() + 1e-12)) * Math.cos(2 * Math.PI * next());
    return { next, gauss };
  }

  it('takes Baron, which decides fights here, and writes the report', async () => {
    // Truth: 0,25 log-odds per 1.000 gold of items, +1,2 for the team with Baron; levels and the
    // richest player say nothing beyond that.
    const { next, gauss } = random(7);
    const games: number[][] = [];
    for (let g = 0; g < 6000; g++) {
      const objectives: number[] = [];
      const baronAt = next() < 0.5 ? 1500 + Math.floor(next() * 300) : 0;
      const baronSide = next() < 0.5 ? 0 : 1;
      if (baronAt) objectives.push(baronAt, baronSide, 8);
      const blocks: number[] = [];
      for (let k = 0; k < 3; k++) {
        const second = 900 + k * 400 + Math.floor(next() * 200);
        const gold = Array.from({ length: 10 }, () => Math.max(1000, Math.round(6000 + second * 3 + 1500 * gauss())));
        const level = Array.from({ length: 10 }, () => 11 + Math.floor(next() * 3));
        const dGold = (gold.slice(0, 5).reduce((a, b) => a + b, 0) - gold.slice(5).reduce((a, b) => a + b, 0)) / 1000;
        const baron = baronAt && baronAt < second && second - baronAt <= 180 ? (baronSide === 0 ? 1 : -1) : 0;
        const blueWins = next() < 1 / (1 + Math.exp(-(0.25 * dGold + 1.2 * baron)));
        blocks.push(...fight(second, blueWins ? 3 : 1, blueWins ? 1 : 3, 0, ALL_SEEN, gold.flatMap((value, i) => [level[i]!, value])));
      }
      games.push(line(-1, -3, objectives.length, ...objectives, ...blocks));
    }
    const gamesPath = path.join(dir, 'games.json.gz');
    writeFileSync(gamesPath, gzipSync(JSON.stringify({ tier: 'platinum', platform: 'euw1', collectedAtUtc: '2026-11-25T12:00:00Z', patches: [{ patch: '16.23', games }] })));
    const out = path.join(dir, 'bericht.md');

    expect(await main([gamesPath, '--out', out])).toBe(0);
    const report = readFileSync(out, 'utf8');
    expect(report).toContain('| **gezählt** | 18.000 | 100,0 % |');
    expect(report).toMatch(/\*\*Gewählt: Itemwert[^*]*\+ Baron[^*]*\.\*\*/);
    // Only Baron ever differs in this world: the other buffs are left out, and named.
    expect(report).toContain('In keinem Kampf unterschiedlich und deshalb weggelassen: Elder, Drachen, Seele, Atakhan.');
    // The richest player says nothing here: that rung stays out.
    expect(report).not.toMatch(/Gewählt:[^*]*wo das Gold liegt/);
    // The weight of 1.000 gold comes out near the truth.
    const gold = /\| 1\.000 Gold \| \+(\d+),(\d+) ±/.exec(report);
    expect(gold).not.toBeNull();
    expect(Number(`${gold![1]}.${gold![2]}`)).toBeGreaterThan(0.2);
    expect(Number(`${gold![1]}.${gold![2]}`)).toBeLessThan(0.3);
    expect(report).toContain('## Hält die Skala, was sie sagt?');
  });
});
