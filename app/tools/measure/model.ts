// Phase 2 of docs/draft-konzept.md, the model comparison: how well does today's model (A) predict
// the winner, and how well the same parts with measured weights, split by kind of pair (B)? Every rate
// is counted on training games only — out of fold for the games B is fitted on — and read through the
// app's own MetaLookup, so shrinkage and baselines are the app's. Checked on held-out games, with the
// calibration and what the best pick is worth against the average one. Then every lane on its own
// through the gold after ten minutes (laneGold.ts): champion or matchup, and what a lead is worth.
// Last the mechanic counters (mechanics.ts): a kit against a kit, beyond the lane strength.
// Changes no weight: the result goes into docs/modellpruefung.md first.
//
//   npm run measure:model -- <games.json.gz> <meta.json> [builds.json] [--out bericht.md] [--patch 16.22] [--sample 1000]

import { mkdirSync, readFileSync, writeFileSync } from 'node:fs';
import path from 'node:path';
import { CompAnalyzer } from '../../src/core/model/compAnalyzer';
import { ALL_LANES, Lane } from '../../src/core/model/lane';
import { MetaLookup, type MetaSnapshot } from '../../src/core/model/metaLookup';
import { logit, sigmoid } from '../../src/core/model/scoreModel';
import { shrink, SYNERGY_PRIOR } from '../../src/core/model/shrinkage';
import { TankShred } from '../../src/core/model/tankShred';
import { TraitTable } from '../../src/core/model/traits';
import type { RiotBuildFile } from '../../src/core/riotBuilds';
import { FEATURES, MINIMUM_DIFFERING_GAMES, sideOf } from './comp';
import { gamesOf, isHeldOut, readGamesFile, type Game, type Seat } from './games';
import { DEFAULT_FACTOR, FactorModel, type FactorGame, type FactorOptions } from './factor';
import { conversion, edgeOf, goldDuels, LANE_GOLD_MINIMUM, matchupGold, splitLane } from './laneGold';
import { fitLogistic, predict, type LogisticFit } from './logistic';
import { encountersOf, MECHANIC_RULES, mechanicFeatures, mechanicSets } from './mechanics';
import { tally, versusKey, VERSUS_MINIMUM_GAMES, type Tally } from './tally';

const FOLDS = 5;

/** Training games with gold after ten minutes before the lane-gold section reports anything. */
const LANE_GOLD_GAMES = 500;

const LANE_NAMES: Record<number, string> = { [Lane.Top]: 'Top', [Lane.Jungle]: 'Jungle', [Lane.Mid]: 'Mid', [Lane.Adc]: 'Bot', [Lane.Support]: 'Support' };

export const SYNERGY_TYPES = ['Bot-Duo (ADC + Support)', 'Jungle + Mid', 'Jungle + Top', 'Jungle + ADC/Support', 'Mid + Support', 'übrige Paare'] as const;

/** The kind of a pair in one team, by its two lanes (concept §3). */
export function synergyType(a: Lane, b: Lane): number {
  const has = (lane: Lane) => a === lane || b === lane;
  if (has(Lane.Adc) && has(Lane.Support)) return 0;
  if (has(Lane.Jungle) && has(Lane.Mid)) return 1;
  if (has(Lane.Jungle) && has(Lane.Top)) return 2;
  if (has(Lane.Jungle) && (has(Lane.Adc) || has(Lane.Support))) return 3;
  if (has(Lane.Mid) && has(Lane.Support)) return 4;
  return 5;
}

export const VERSUS_TYPES = ['Botlane-Kreuz (ADC ↔ Support)', 'Jungle ↔ Laner', 'übrige Gegner-Paare'] as const;

/** The kind of a pair across the teams on different lanes. */
export function versusType(mine: Lane, theirs: Lane): number {
  if ((mine === Lane.Adc && theirs === Lane.Support) || (mine === Lane.Support && theirs === Lane.Adc)) return 0;
  if (mine === Lane.Jungle || theirs === Lane.Jungle) return 1;
  return 2;
}

/** The numbers one set of games yields, as the app reads them. */
interface Numbers {
  meta: MetaLookup;
  versus: Tally['versus'];
}

const numbersOf = (games: readonly Game[], base: MetaSnapshot): Numbers => {
  const counted = tally(games, base);
  return { meta: new MetaLookup(counted.snapshot), versus: counted.versus };
};

/** The model's parts for one game, from blue's side. */
export interface Parts {
  /** Today's draft chance in log-odds (advisors.estimateBalance): mean lane strength edge + mean duel edge. */
  a: number;
  /** Sum of the lane strengths, blue minus red, in log-odds. */
  strength: number;
  /** Duel edge per lane over what both lane strengths predict (Top … Support). */
  duels: number[];
  /** Synergy edges per kind of pair, blue's minus red's. */
  synergy: number[];
  /** Edges against enemies on other lanes, per kind of pair. */
  versus: number[];
}

function laneRate(n: Numbers, seat: Seat): number | null {
  return seat.lane === Lane.Unknown ? null : (n.meta.laneStat(seat.championId, seat.lane)?.winRate ?? null);
}

export function partsOf(n: Numbers, game: Game): Parts {
  let blueSum = 0;
  let redSum = 0;
  let blueCount = 0;
  let redCount = 0;
  for (const seat of game.blue) {
    const rate = laneRate(n, seat);
    if (rate !== null) {
      blueSum += logit(rate);
      blueCount++;
    }
  }
  for (const seat of game.red) {
    const rate = laneRate(n, seat);
    if (rate !== null) {
      redSum += logit(rate);
      redCount++;
    }
  }

  const duels = ALL_LANES.map(() => 0);
  let duelSum = 0;
  let contested = 0;
  for (const lane of ALL_LANES) {
    const blue = game.blue.filter((s) => s.lane === lane);
    const red = game.red.filter((s) => s.lane === lane);
    if (blue.length !== 1 || red.length !== 1) continue;
    const view = n.meta.matchup(blue[0]!.championId, red[0]!.championId, lane);
    if (!view) continue;
    const edge = logit(view.winRate) - n.meta.matchupBaseline(blue[0]!.championId, red[0]!.championId, lane);
    duels[lane] = edge;
    duelSum += edge;
    contested++;
  }

  const synergy = SYNERGY_TYPES.map(() => 0);
  for (const [seats, sign] of [
    [game.blue, 1],
    [game.red, -1],
  ] as const) {
    for (let i = 0; i < seats.length; i++)
      for (let j = i + 1; j < seats.length; j++) {
        const view = n.meta.synergy(seats[i]!.championId, seats[j]!.championId);
        if (view) synergy[synergyType(seats[i]!.lane, seats[j]!.lane)]! += sign * (logit(view.winRate) - logit(view.expected));
      }
  }

  const versus = VERSUS_TYPES.map(() => 0);
  for (const mine of game.blue)
    for (const theirs of game.red) {
      if (mine.lane === Lane.Unknown || theirs.lane === Lane.Unknown || mine.lane === theirs.lane || mine.championId === theirs.championId) continue;
      const low = mine.championId < theirs.championId;
      const count = n.versus.get(low ? versusKey(mine.championId, mine.lane, theirs.championId, theirs.lane) : versusKey(theirs.championId, theirs.lane, mine.championId, mine.lane));
      const rateMine = laneRate(n, mine);
      const rateTheirs = laneRate(n, theirs);
      if (!count || count.games < VERSUS_MINIMUM_GAMES || rateMine === null || rateTheirs === null) continue;
      // Shrunk like a duo towards what both lane strengths predict (VersusStat: duo limits).
      const expected = logit(rateMine) - logit(rateTheirs);
      const observed = low ? count.wins / count.games : 1 - count.wins / count.games;
      versus[versusType(mine.lane, theirs.lane)]! += logit(shrink(observed, count.games, SYNERGY_PRIOR, sigmoid(expected))) - expected;
    }

  const strengthEdge = blueCount > 0 && redCount > 0 ? blueSum / blueCount - redSum / redCount : 0;
  return { a: strengthEdge + (contested > 0 ? duelSum / contested : 0), strength: blueSum - redSum, duels, synergy, versus };
}

const pts = (logOdds: number, error: number): string =>
  `${logOdds >= 0 ? '+' : '−'}${Math.abs(logOdds).toFixed(3).replace('.', ',')} ± ${(1.96 * error).toFixed(3).replace('.', ',')}`;
const num = (value: number, digits = 4): string => value.toFixed(digits).replace('.', ',');
const pct = (value: number): string => `${(value * 100).toFixed(1).replace('.', ',')} %`;
const count = (value: number): string => value.toLocaleString('de-DE');

function brier(fit: (row: number) => number, y: readonly number[]): number {
  let total = 0;
  for (let i = 0; i < y.length; i++) total += (fit(i) - y[i]!) ** 2;
  return total / Math.max(1, y.length);
}

/** What the lane-gold section needs from the comparison: the split, the parts and the strength-only fit. */
interface LaneGoldInput {
  games: readonly Game[];
  trainIndex: readonly number[];
  testIndex: readonly number[];
  parts: ReadonlyMap<number, Parts>;
  xTrain: readonly number[][];
  yTrain: readonly number[];
  xTest: readonly number[][];
  strengthOnly: LogisticFit;
  evaluate: (predictRow: (k: number) => number) => { loss: number; hits: number; brier: number };
}

/**
 * Pick → lane lead → win, every lane on its own (laneGold.ts). Counted only on training games that
 * carry the gold after ten minutes; the per-lane weights are fitted on the training games without it,
 * so no game ever sees its own gold, and checked on the same held-out games as everything else.
 */
function reportLaneGold(report: string[], input: LaneGoldInput): void {
  const { games, trainIndex, testIndex, parts, xTrain, yTrain, xTest, strengthOnly, evaluate } = input;
  const goldTrain = trainIndex.map((i) => games[i]!).filter((g) => g.gold);
  report.push('## Lane-Gold nach 10 Minuten, je Lane', '');
  const converted = goldTrain.length >= LANE_GOLD_GAMES ? conversion(goldTrain) : null;
  if (!converted) {
    report.push(`Zu wenige Trainingsspiele mit Gold nach 10 Minuten: ${count(goldTrain.length)}, nötig ${count(LANE_GOLD_GAMES)}.`, '');
    return;
  }

  const perGold = (lane: Lane) => converted.fit.coefficients[lane + 1]! / 1000;
  const lanes = ALL_LANES.map((lane) => {
    const duels = goldDuels(goldTrain, lane);
    const split = splitLane(duels);
    return { lane, split, edges: matchupGold(duels, split) };
  });
  const termsOf = (game: Game): number[] =>
    lanes.map(({ lane, edges }) => {
      const blue = game.blue.filter((s) => s.lane === lane);
      const red = game.red.filter((s) => s.lane === lane);
      return blue.length === 1 && red.length === 1 ? perGold(lane) * edgeOf(edges, blue[0]!.championId, red[0]!.championId) : 0;
    });
  const used = lanes.filter((l) => l.edges.size > 0).map((l) => l.lane);
  const fitIndex = trainIndex.filter((i) => !games[i]!.gold);
  const terms = new Map<number, number[]>();
  for (const i of [...fitIndex, ...testIndex]) terms.set(i, termsOf(games[i]!));
  const total = (i: number) => terms.get(i)!.reduce((s, v) => s + v, 0);

  const duelColumns = 1 + ALL_LANES.length;
  const strengthDuels = fitLogistic(xTrain.map((v) => v.slice(0, duelColumns)), yTrain);
  const perLane = used.length > 0
    ? fitLogistic(
        fitIndex.map((i) => [parts.get(i)!.strength, ...used.map((l) => terms.get(i)![l]!)]),
        fitIndex.map((i) => games[i]!.blueWins),
      )
    : null;
  const checks: [string, ReturnType<typeof evaluate>][] = [
    ['Lane-Stärke allein, angepasst', evaluate((k) => predict(strengthOnly, [xTest[k]![0]!]))],
    ['dazu gezählte Duelle (über den Sieg), Gewichte angepasst', evaluate((k) => predict(strengthDuels, xTest[k]!.slice(0, duelColumns)))],
    ['dazu Matchups über das Lane-Gold, umgerechnet, nichts angepasst', evaluate((k) => predict(strengthOnly, [xTest[k]![0]!], total(testIndex[k]!)))],
  ];
  if (perLane)
    checks.push(['dazu Matchups über das Lane-Gold, Gewicht je Lane angepasst', evaluate((k) => predict(perLane, [xTest[k]![0]!, ...used.map((l) => terms.get(testIndex[k]!)![l]!)]))]);

  const gold = (value: number) => `±${count(Math.round(value))} Gold`;
  const win = (lane: Lane, value: number) => `${num(value * perGold(lane) * 25, 1)} Pkt`;
  const games50 = (value: number) => (Number.isFinite(value) ? count(Math.round(value)) : '—');
  const seen = (z: number) => (z >= 2 ? 'deutlich' : z >= 1 ? 'angedeutet' : 'nicht zu sehen');

  report.push(
    `${count(goldTrain.length)} Trainingsspiele tragen das Gold jedes Spielers nach 10 Minuten (Lane-Teil des Sammlers). ` +
      'Die Kette je Lane: Was bestimmt den Gold-Unterschied zwischen den beiden Lane-Gegnern — der Champion (sein Schnitt ' +
      'gegen alle) oder das Matchup (was das Paar darüber hinaus tut)? Und was ist ein Vorsprung nach 10 Minuten im Sieg ' +
      'wert? Streuungen sind die echten, der Zufallsanteil herausgerechnet (Momentenmethode).',
    '',
    '### Was ein Vorsprung nach 10 Minuten wert ist',
    '',
    `Alle fünf Lanes zugleich angepasst, ${count(converted.games)} Spiele. Winrate-Punkte bei 50 %.`,
    '',
    '| Lane | je 1.000 Gold | typischer Unterschied der Lane-Gegner |',
    '|---|---|---|',
    ...lanes.map(({ lane, split }) => {
      const c = converted.fit.coefficients[lane + 1]!;
      const e = converted.fit.standardErrors[lane + 1]!;
      return `| ${LANE_NAMES[lane]} | ${c >= 0 ? '+' : '−'}${num(Math.abs(c) * 25, 1)} ± ${num(1.96 * e * 25, 1)} Pkt | ${gold(split.spread)} |`;
    }),
    '',
    '### Was den Gold-Unterschied bestimmt',
    '',
    `Champions mit mindestens ${LANE_GOLD_MINIMUM} Spielen auf der Lane. „Halb zählt“: ab so vielen Spielen sagt das eigene Gold ` +
      'eines Paars mehr als der Schnitt — über das Gold und, bei gleicher Stärke des Matchups, über den Sieg.',
    '',
    '| Lane | Duelle | Champion | Matchup | höchstens (95 %) | Matchup-Signal | Paar zählt halb: Gold | über den Sieg |',
    '|---|---|---|---|---|---|---|---|',
    ...lanes.map(({ lane, split }) => {
      const effect = split.matchupSpread * perGold(lane) * 0.25;
      const viaWin = effect > 0 ? 0.25 / effect ** 2 : Infinity;
      return (
        `| ${LANE_NAMES[lane]} | ${count(split.duels)} | ${gold(split.championSpread)} (${win(lane, split.championSpread)}) | ` +
        `${gold(split.matchupSpread)} (${win(lane, split.matchupSpread)}) | ${gold(split.matchupUpper)} (${win(lane, split.matchupUpper)}) | ` +
        `${seen(split.matchupZ)} (${split.matchupZ < 0 ? '−' : ''}${num(Math.abs(split.matchupZ), 1)} σ) | ${games50(split.prior)} | ${games50(viaWin)} |`
      );
    }),
    '',
    '### Prüfung auf zurückgehaltenen Spielen',
    '',
    'Die Matchups über das Lane-Gold: das eigene Gold jedes Paars über die Schnitte beider Champions hinaus, geglättet mit ' +
      '„Paar zählt halb“, mit der Umrechnung seiner Lane in log-odds. Die Gewichte je Lane sind auf den Trainingsspielen ' +
      `ohne Gold angepasst (${count(fitIndex.length)}), damit kein Spiel sein eigenes Gold sieht.`,
    '',
    '| Modell | Log-Loss | Treffer |',
    '|---|---|---|',
    ...checks.map(([name, m]) => `| ${name} | ${num(m.loss)} | ${pct(m.hits)} |`),
    '',
  );
  if (perLane)
    report.push(
      'Gewicht je Lane, 1 hieße: das Matchup zählt so viel, wie Gold und Umrechnung sagen; 0: gar nicht.',
      '',
      '| Lane | Gewicht |',
      '|---|---|',
      ...ALL_LANES.map((lane) => {
        const j = used.indexOf(lane);
        return `| ${LANE_NAMES[lane]} | ${j < 0 ? '— (kein Matchup-Signal im Gold)' : pts(perLane.coefficients[j + 2]!, perLane.standardErrors[j + 2]!)} |`;
      }),
      '',
    );
}

/**
 * Mechanic counters (mechanics.ts): per rule what one encounter is worth in the win beyond the lane
 * strength — fitted on the training games with the same out-of-fold strengths as everything else,
 * checked on the held-out games — and, for a rule on one lane, the gold after ten minutes beyond both
 * champions' gold strength on that lane.
 */
function reportMechanics(report: string[], input: LaneGoldInput, champions: readonly { id: number; key: string; name: string }[]): void {
  const { games, trainIndex, testIndex, parts, yTrain, xTest, strengthOnly, evaluate } = input;
  const { sets, missing } = mechanicSets(champions);
  const features = new Map<number, number[]>();
  for (const i of [...trainIndex, ...testIndex]) features.set(i, mechanicFeatures(games[i]!, sets));
  const fit = fitLogistic(trainIndex.map((i) => [parts.get(i)!.strength, ...features.get(i)!]), yTrain);
  const encounters = MECHANIC_RULES.map((rule) => trainIndex.reduce((sum, i) => sum + encountersOf(games[i]!, sets, rule).length, 0));

  // Gold after ten minutes in the encounter, beyond what both champions' gold on the lane expects.
  const goldTrain = trainIndex.map((i) => games[i]!).filter((g) => g.gold);
  const strengths = new Map(ALL_LANES.map((lane) => [lane, splitLane(goldDuels(goldTrain, lane)).strength]));
  const goldOf = (rule: (typeof MECHANIC_RULES)[number]): string => {
    if (!rule.sameLane) return '— (zwei Lanes)';
    const values: number[] = [];
    for (const game of goldTrain) {
      const seats = [...game.blue, ...game.red];
      for (const e of encountersOf(game, sets, rule)) {
        const lane = seats[e.mine]!.lane;
        const means = strengths.get(lane);
        const mine = means?.get(seats[e.mine]!.championId);
        const theirs = means?.get(seats[e.theirs]!.championId);
        if (mine === undefined || theirs === undefined) continue;
        values.push(game.gold![e.mine]! - game.gold![e.theirs]! - (mine - theirs));
      }
    }
    if (values.length < 20) return `— (${count(values.length)} Spiele mit Gold)`;
    const mean = values.reduce((s, v) => s + v, 0) / values.length;
    const sd = Math.sqrt(values.reduce((s, v) => s + (v - mean) ** 2, 0) / (values.length - 1));
    return `${mean >= 0 ? '+' : '−'}${count(Math.round(Math.abs(mean)))} ± ${count(Math.round((1.96 * sd) / Math.sqrt(values.length)))} Gold (${count(values.length)} Spiele)`;
  };

  const winPoints = (j: number) => {
    const c = fit.coefficients[j]!;
    const e = fit.standardErrors[j]!;
    return `${pts(c, e)} (${c >= 0 ? '+' : '−'}${num(Math.abs(c) * 25, 1)} ± ${num(1.96 * e * 25, 1)} Pkt)`;
  };
  const checks: [string, ReturnType<typeof evaluate>][] = [
    ['Lane-Stärke allein, angepasst', evaluate((k) => predict(strengthOnly, [xTest[k]![0]!]))],
    ['dazu die Mechanik-Regeln', evaluate((k) => predict(fit, [xTest[k]![0]!, ...features.get(testIndex[k]!)!]))],
  ];

  report.push(
    '## Mechanik-Regeln',
    '',
    'Counter über die Skills (`tools/measure/mechanics.ts`): Listen nach Einschätzung, nur eindeutige Fälle, vom Nutzer ' +
      'am 09.11.2026 geprüft, bevor irgendetwas darauf gemessen wurde. Gewicht einer Begegnung im Sieg über die Lane-Stärke ' +
      'hinaus, zusammen mit ihr auf den Trainingsspielen angepasst (95-%-Fehlerbalken, Punkte bei 50 %); bei Regeln auf ' +
      'einer Lane dazu das Gold nach 10 Minuten in der Begegnung über die Gold-Stärke beider Champions hinaus.' +
      (missing.length > 0 ? ` Nicht im Bestand gefunden: ${missing.join(', ')}.` : ''),
    '',
    '| Regel | Begegnungen (Training) | je Begegnung im Sieg | Gold nach 10 Min. |',
    '|---|---|---|---|',
    ...MECHANIC_RULES.map((rule, r) => `| ${rule.label} | ${count(encounters[r]!)} | ${winPoints(r + 2)} | ${goldOf(rule)} |`),
    '',
    '| Modell (zurückgehaltene Spiele) | Log-Loss | Treffer |',
    '|---|---|---|',
    ...checks.map(([name, m]) => `| ${name} | ${num(m.loss)} | ${pct(m.hits)} |`),
    '',
  );
}

function args(list: readonly string[]): { files: string[]; options: Record<string, string> } {
  const files: string[] = [];
  const options: Record<string, string> = {};
  for (let i = 0; i < list.length; i++) {
    const a = list[i]!;
    if (a.startsWith('--')) options[a.slice(2)] = list[++i] ?? '';
    else files.push(a);
  }
  return { files, options };
}

export async function main(argv: string[]): Promise<number> {
  const { files, options } = args(argv);
  if (files.length < 2) {
    console.error('Aufruf: npm run measure:model -- <games.json.gz> <meta.json> [builds.json] [--out bericht.md] [--patch 16.22] [--sample 1000]');
    return 2;
  }
  const started = Date.now();
  const gamesFile = readGamesFile(files[0]!);
  const base = JSON.parse(readFileSync(files[1]!, 'utf8').replace(/^﻿/, '')) as MetaSnapshot;
  const builds = files[2] ? (JSON.parse(readFileSync(files[2], 'utf8').replace(/^﻿/, '')) as RiotBuildFile) : null;
  const patch = options.patch ?? base.patch;
  const games = gamesOf(gamesFile, patch);
  if (games.length < 1000) {
    console.error(`Zu wenige Spiele für Patch ${patch}: ${games.length}.`);
    return 1;
  }

  // The comp needs no win counts — damage types, tags and traits — so one reading serves every game.
  const baseMeta = new MetaLookup(base);
  const traits = TraitTable.bundled();
  const shred = TankShred.fromBuilds(builds);
  const analyzer = new CompAnalyzer(baseMeta, traits, shred);
  const compOf = (game: Game): number[] => {
    const blue = sideOf(baseMeta, traits, shred, analyzer, game.blue);
    const red = sideOf(baseMeta, traits, shred, analyzer, game.red);
    return FEATURES.map((f) => f.of(blue, red) - f.of(red, blue));
  };
  const comp = games.map(compOf);
  const compUsed = FEATURES.map((_, j) => j).filter((j) => comp.filter((c) => c[j] !== 0).length >= MINIMUM_DIFFERING_GAMES);

  // Training games get their parts out of fold: counted without the game itself and its fold.
  const trainIndex = games.map((_, i) => i).filter((i) => !isHeldOut(i));
  const testIndex = games.map((_, i) => i).filter((i) => isHeldOut(i));
  const parts = new Map<number, Parts>();
  for (let fold = 0; fold < FOLDS; fold++) {
    const numbers = numbersOf(trainIndex.filter((_, k) => k % FOLDS !== fold).map((i) => games[i]!), base);
    trainIndex.forEach((i, k) => {
      if (k % FOLDS === fold) parts.set(i, partsOf(numbers, games[i]!));
    });
  }
  const full = numbersOf(trainIndex.map((i) => games[i]!), base);
  for (const i of testIndex) parts.set(i, partsOf(full, games[i]!));

  const names = [
    'Lane-Stärke (Summe je Team)',
    ...ALL_LANES.map((l) => `Duell ${LANE_NAMES[l]}`),
    ...SYNERGY_TYPES.map((t) => `Synergie ${t}`),
    ...VERSUS_TYPES.map((t) => `Gegner ${t}`),
    ...compUsed.map((j) => `Comp: ${FEATURES[j]!.label}`),
  ];
  const PART_COLUMNS = 1 + ALL_LANES.length + SYNERGY_TYPES.length + VERSUS_TYPES.length;
  const vector = (i: number): number[] => {
    const p = parts.get(i)!;
    return [p.strength, ...p.duels, ...p.synergy, ...p.versus, ...compUsed.map((j) => comp[i]![j]!)];
  };

  const yTrain = trainIndex.map((i) => games[i]!.blueWins);
  const yTest = testIndex.map((i) => games[i]!.blueWins);
  const xTrain = trainIndex.map(vector);
  const xTest = testIndex.map(vector);

  const aCal = fitLogistic(trainIndex.map((i) => [parts.get(i)!.a]), yTrain);
  const strengthOnly = fitLogistic(xTrain.map((v) => [v[0]!]), yTrain);
  const bParts = fitLogistic(xTrain.map((v) => v.slice(0, PART_COLUMNS)), yTrain);
  const b = fitLogistic(xTrain, yTrain);

  const evaluate = (predictRow: (k: number) => number) => {
    let loss = 0;
    let hits = 0;
    testIndex.forEach((_, k) => {
      const p = Math.min(1 - 1e-12, Math.max(1e-12, predictRow(k)));
      loss -= yTest[k] === 1 ? Math.log(p) : Math.log(1 - p);
      if ((p >= 0.5 ? 1 : 0) === yTest[k]) hits++;
    });
    return { loss: loss / testIndex.length, hits: hits / testIndex.length, brier: brier(predictRow, yTest) };
  };
  // Model C: learned on the training games at once (factor.ts), a tenth of them held back to choose
  // the regularisation and when to stop; checked on the same held-out games as A and B.
  const vocab = new Map<string, number>();
  for (const i of trainIndex) for (const s of [...games[i]!.blue, ...games[i]!.red]) if (!vocab.has(`${s.championId}:${s.lane}`)) vocab.set(`${s.championId}:${s.lane}`, vocab.size);
  const entity = (s: Seat) => vocab.get(`${s.championId}:${s.lane}`) ?? -1;
  const factorGame = (g: Game, compRow: readonly number[]): FactorGame => ({
    blue: Int32Array.from(g.blue.map(entity)),
    red: Int32Array.from(g.red.map(entity)),
    blueLanes: g.blue.map((s) => s.lane),
    redLanes: g.red.map((s) => s.lane),
    comp: compUsed.map((j) => compRow[j]!),
    y: g.blueWins,
  });
  const factorTrain = trainIndex.map((i) => factorGame(games[i]!, comp[i]!));
  const fitGames = factorTrain.filter((_, k) => k % 10 !== 0);
  const validGames = factorTrain.filter((_, k) => k % 10 === 0);
  const factorTest = testIndex.map((i) => factorGame(games[i]!, comp[i]!));
  const trained: { options: FactorOptions; model: FactorModel; epochs: number; validationLoss: number }[] = [];
  const trainC = (options: FactorOptions) => {
    const model = new FactorModel(vocab.size, compUsed.length, options);
    const result = model.train(fitGames, validGames);
    trained.push({ options, model, ...result });
    return trained[trained.length - 1]!;
  };
  // Strengths alone first (no traits), to choose their regularisation; then the pairs on top.
  const mainOnly = [1e-3, 3e-3, 1e-2, 3e-2].map((l2Strength) => trainC({ ...DEFAULT_FACTOR, rate: 0.005, dims: 0, l2Strength })).sort((x, y) => x.validationLoss - y.validationLoss)[0]!;
  const withPairs = [2, 4]
    .flatMap((dims) => [1e-3, 1e-2, 3e-2].map((l2Traits) => trainC({ ...DEFAULT_FACTOR, rate: 0.005, dims, l2Strength: mainOnly.options.l2Strength, l2Traits })))
    .sort((x, y) => x.validationLoss - y.validationLoss)[0]!;
  const c = withPairs.model;

  const models: [string, ReturnType<typeof evaluate>][] = [
    ['Münzwurf (50 %)', evaluate(() => 0.5)],
    ['A: Draft-Chance wie heute', evaluate((k) => sigmoid(parts.get(testIndex[k]!)!.a))],
    ['A, kalibriert (Achsenabschnitt und Steigung angepasst)', evaluate((k) => predict(aCal, [parts.get(testIndex[k]!)!.a]))],
    ['Lane-Stärke allein, angepasst', evaluate((k) => predict(strengthOnly, [xTest[k]![0]!]))],
    ['B ohne Comp: Stärke, Duelle, Synergie, Gegner-Paare', evaluate((k) => predict(bParts, xTest[k]!.slice(0, PART_COLUMNS)))],
    ['B: dazu die Comp-Merkmale', evaluate((k) => predict(b, xTest[k]!))],
    ['C ohne Paare: Stärken und Comp gemeinsam gelernt', evaluate((k) => mainOnly.model.probability(factorTest[k]!))],
    ['C: dazu Synergie und Counter aus gelernten Merkmalen', evaluate((k) => c.probability(factorTest[k]!))],
  ];

  const report: string[] = [];
  report.push('# Modellvergleich (Phase 2, Werkzeug tools/measure/model.ts)', '');
  report.push(
    `Daten: ${count(games.length)} Spiele, Patch ${patch}, ${gamesFile.tier} · ${gamesFile.platform}, gesammelt bis ${gamesFile.collectedAtUtc.slice(0, 16).replace('T', ' ')} UTC. ` +
      `${count(trainIndex.length)} zum Anpassen, ${count(testIndex.length)} zurückgehalten zum Prüfen.`,
    '',
    'Alle Raten (Lanes, Duelle, Duos, Gegner-Paare) werden nur aus den Trainingsspielen gezählt, wie der Sammler zählt ' +
      'und mit seinen Mindestzahlen, und durch die MetaLookup der App gelesen — Glättung und Grundlinien wie im Fenster. ' +
      `Für die Trainingsspiele selbst ohne das eigene Fünftel (${FOLDS} Teile), sonst sähe jedes Spiel seine eigene Rate. ` +
      'Lanes sind die tatsächlichen aus Riots Spieldaten, nicht vorhergesagt.',
    '',
  );

  report.push('## Vorhersage auf zurückgehaltenen Spielen', '', '| Modell | Log-Loss | Brier | Treffer |', '|---|---|---|---|');
  for (const [name, m] of models) report.push(`| ${name} | ${num(m.loss)} | ${num(m.brier)} | ${pct(m.hits)} |`);
  const coin = models[0]![1].loss;
  const best = models[5]![1];
  report.push(
    '',
    `Lesart: kleiner ist besser. B gegenüber A kalibriert: ${num(models[2]![1].loss - best.loss)} im Log-Loss ` +
      `(positiv = B besser); gegenüber dem Münzwurf erklärt B ${pct((coin - best.loss) / coin)} der Unsicherheit. ` +
      `C gegenüber B: ${num(best.loss - models[7]![1].loss)} (positiv = C besser), C ohne Paare gegenüber B: ${num(best.loss - models[6]![1].loss)}. ` +
      `Die Draft-Chance A passt am besten mit Steigung ${num(aCal.coefficients[1]!, 2)} ± ${num(1.96 * aCal.standardErrors[1]!, 2)}: ` +
      'über 1 heißt, sie liegt zu nah an 50 %.',
    '',
  );

  report.push('## Gemessene Gewichte (B)', '', 'In log-odds je Einheit des Teils, 95-%-Fehlerbalken. 1 hieße: der Teil zählt genau so, wie die gemessene Rate sagt.', '', '| Teil | Gewicht |', '|---|---|');
  names.forEach((name, j) => report.push(`| ${name} | ${pts(b.coefficients[j + 1]!, b.standardErrors[j + 1]!)} |`));
  report.push(
    '',
    `Seitenvorteil Blau: ${pts(b.coefficients[0]!, b.standardErrors[0]!)}. Zum Vergleich A: Die Draft-Chance mittelt Stärke und ` +
      'Duelle über die Lanes (je etwa 0,2 je Platz bzw. Lane) und kennt weder Synergie noch Gegner-Paare noch Comp; die ' +
      'Pick-Liste zählt je Platz Stärke und Duell voll, übrige Gegner mit 0,35 und Synergie mit 0,6 (jeweils gemittelt), ' +
      'Comp bis 0,16.',
    '',
  );

  // What drives C: how far each part moves the prediction across the held-out games.
  const spread = (pick: (p: ReturnType<FactorModel['parts']>) => number) => {
    const values = factorTest.map((g) => pick(c.parts(g)));
    const mean = values.reduce((s, v) => s + v, 0) / values.length;
    return Math.sqrt(values.reduce((s, v) => s + (v - mean) ** 2, 0) / values.length);
  };
  report.push(
    '## Modell C',
    '',
    `Gewählt auf dem Validierungsteil: ${withPairs.options.dims} gelernte Merkmale je Champion und Lane, L2 ${withPairs.options.l2Traits} auf den ` +
      `Merkmalen und ${withPairs.options.l2Strength} auf den Stärken, ${withPairs.epochs} Durchgänge. ${count(vocab.size)} Champion-Lanes. ` +
      'Wie weit jeder Teil die Vorhersage über die zurückgehaltenen Spiele bewegt (Standardabweichung, log-odds und Punkte bei 50 %):',
    '',
    '| Teil | Streuung |',
    '|---|---|',
    ...(
      [
        ['Stärke (Champion und Lane)', spread((p) => p.strength)],
        ['Synergie im Team', spread((p) => p.synergy)],
        ['Counter über die Teams', spread((p) => p.counter)],
        ['Comp-Merkmale', spread((p) => p.comp)],
      ] as const
    ).map(([name, value]) => `| ${name} | ${num(value, 3)} (${num(value * 25, 1)} Pkt) |`),
    '',
    'Alle Läufe (Validierung, Log-Loss): ' +
      trained.map((t) => `${t.options.dims} Merkmale, L2 ${t.options.l2Strength}/${t.options.l2Traits}: ${num(t.validationLoss)} nach ${t.epochs}`).join(' · ') +
      '.',
    '',
  );

  // Calibration of B: does "55 %" win 55 %?
  const bins: [number, number][] = [
    [0, 0.4],
    [0.4, 0.45],
    [0.45, 0.5],
    [0.5, 0.55],
    [0.55, 0.6],
    [0.6, 1.01],
  ];
  const cTest = factorTest.map((g) => c.probability(g));
  report.push(
    '## Kalibrierung (zurückgehaltene Spiele)',
    '',
    '| vorhergesagt | Spiele B | B vorhergesagt | B tatsächlich | Spiele C | C vorhergesagt | C tatsächlich |',
    '|---|---|---|---|---|---|---|',
  );
  for (const [lo, hi] of bins) {
    const cells: string[] = [];
    for (const p of [(k: number) => predict(b, xTest[k]!), (k: number) => cTest[k]!]) {
      let n = 0;
      let predicted = 0;
      let won = 0;
      testIndex.forEach((_, k) => {
        const value = p(k);
        if (value < lo || value >= hi) return;
        n++;
        predicted += value;
        won += yTest[k]!;
      });
      cells.push(count(n), n ? pct(predicted / n) : '—', n ? pct(won / n) : '—');
    }
    report.push(`| ${pct(lo)}–${hi > 1 ? '100 %' : pct(hi)} | ${cells.join(' | ')} |`);
  }
  report.push('');

  // What the best pick is worth: each seat's champion swapped for every one of its lane, the rest kept.
  const sample = testIndex.slice(0, Math.max(0, Number(options.sample ?? 1000)));
  const byB = (game: Game) => {
    const p = partsOf(full, game);
    const row = compOf(game);
    return predict(b, [p.strength, ...p.duels, ...p.synergy, ...p.versus, ...compUsed.map((j) => row[j]!)]);
  };
  const byC = (game: Game) => c.probability(factorGame(game, compOf(game)));
  const bestPick = (probability: (game: Game) => number) => {
    const perLane = ALL_LANES.map(() => ({ seats: 0, overAverage: 0, overActual: 0 }));
    for (const i of sample) {
      const game = games[i]!;
      const taken = new Set([...game.blue, ...game.red].map((s) => s.championId));
      for (const side of ['blue', 'red'] as const) {
        const seats = game[side];
        seats.forEach((seat, index) => {
          if (seat.lane === Lane.Unknown || !full.meta.laneStat(seat.championId, seat.lane)) return;
          const own = (p: number) => (side === 'blue' ? p : 1 - p);
          const actual = own(probability(game));
          let bestValue = -Infinity;
          let weighted = 0;
          let weights = 0;
          for (const candidate of full.meta.roster(seat.lane)) {
            if (taken.has(candidate)) continue;
            const stat = full.meta.laneStat(candidate, seat.lane);
            if (!stat) continue;
            const swapped = [...seats];
            swapped[index] = { championId: candidate, lane: seat.lane };
            const value = own(probability({ ...game, [side]: swapped }));
            bestValue = Math.max(bestValue, value);
            weighted += stat.pickRate * value;
            weights += stat.pickRate;
          }
          if (weights === 0) return;
          const lane = perLane[seat.lane]!;
          lane.seats++;
          lane.overAverage += bestValue - weighted / weights;
          lane.overActual += bestValue - actual;
        });
      }
    }
    return perLane;
  };
  const valueB = bestPick(byB);
  const valueC = bestPick(byC);
  report.push(
    '## Wert des besten Picks',
    '',
    `Für ${count(sample.length)} zurückgehaltene Spiele: jeder Platz mit jedem Champion seiner Lane getauscht, der Rest ` +
      'bleibt. Vorhergesagte Winrate des besten Picks gegenüber dem Durchschnitt (gewichtet nach Pickrate) und gegenüber ' +
      'dem tatsächlich gespielten, nach B und nach C. Nachträglich gerechnet, mit allen zehn Champions bekannt — im Draft ist ' +
      'weniger bekannt, der Wert also eine Obergrenze.',
    '',
    '| Lane | Plätze | B: gegen Durchschnitt | B: gegen gespielten | C: gegen Durchschnitt | C: gegen gespielten |',
    '|---|---|---|---|---|---|',
  );
  const cell = (sum: number, seats: number) => (seats ? `+${num((sum / seats) * 100, 1)} Pkt` : '—');
  const total = (rows: typeof valueB) => rows.reduce((t, r) => ({ seats: t.seats + r.seats, overAverage: t.overAverage + r.overAverage, overActual: t.overActual + r.overActual }), { seats: 0, overAverage: 0, overActual: 0 });
  for (const lane of ALL_LANES) {
    const rb = valueB[lane]!;
    const rc = valueC[lane]!;
    report.push(`| ${LANE_NAMES[lane]} | ${count(rb.seats)} | ${cell(rb.overAverage, rb.seats)} | ${cell(rb.overActual, rb.seats)} | ${cell(rc.overAverage, rc.seats)} | ${cell(rc.overActual, rc.seats)} |`);
  }
  const tb = total(valueB);
  const tc = total(valueC);
  report.push(`| alle | ${count(tb.seats)} | ${cell(tb.overAverage, tb.seats)} | ${cell(tb.overActual, tb.seats)} | ${cell(tc.overAverage, tc.seats)} | ${cell(tc.overActual, tc.seats)} |`, '');

  const fitted: LaneGoldInput = { games, trainIndex, testIndex, parts, xTrain, yTrain, xTest, strengthOnly, evaluate };
  reportLaneGold(report, fitted);
  reportMechanics(report, fitted, base.champions);

  report.push(
    '## Noch nicht drin',
    '',
    '- Glättungen durchprobieren (Duell- und Duo-Gewicht); die Lane-Glättung misst `Tools -- priors`.',
    '- Mastery, Patch-Vorwissen, Synergien über drei Patches (Konzept §4/§5).',
    '',
    `Rechenzeit: ${Math.round((Date.now() - started) / 1000)} s.`,
  );

  const out = path.resolve(options.out ?? path.join('..', 'build', 'measure-model.md'));
  mkdirSync(path.dirname(out), { recursive: true });
  writeFileSync(out, report.join('\n') + '\n');
  console.log(`${count(games.length)} Spiele gemessen → ${out}`);
  return 0;
}
