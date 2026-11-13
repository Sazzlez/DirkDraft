// The check before the strength scale appears (docs/lane-vergleich.md, „Prüfung, bevor die Skala
// erscheint“; docs/modellpruefung.md, Messung 27). The collector keeps every duel between lane
// opponents of a sample of games: who won, and each player's level, item worth and resistances
// going into it. Asked here, on games the fit has never seen:
//
//   1. Does the side the scale sees ahead win the next duel more often — and as often as it says?
//   2. What is a level worth, what are 1.000 gold in items worth, per lane? Do resistances against
//      the opponent's kind of damage say more than their price? Do the weights change with the phase?
//   3. Does the variant with the champions' own strength (per phase, and per pair) beat the plain
//      one from level and items? Only then would it be shown.
//
// Levels: until 13.11.2026 the collector read the level the kill itself gave as the winner's level
// going into the duel. Lines counted since end in −2 (RiotGames.LevelsBeforeDuel). The tool measures
// on marked lines once there are enough of them (--levels marked, the default then), on all lines
// before that — and says which. The size of the error is measured on the duels right after 10:00,
// where the line's experience after ten minutes gives every player's true level.
//
//   npm run measure:duels -- <games.json.gz> <meta.json> [--levels all|marked] [--out bericht.md]

import { mkdirSync, readFileSync, writeFileSync } from 'node:fs';
import path from 'node:path';
import { isHeldOut, readGamesFile } from './games';
import { fitLogistic, predict, type LogisticFit } from './logistic';

const LANE_PART = 35;
const XP_AT_TEN = 25;
const NO_DUELS = -1;
const LEVELS_BEFORE_DUEL = -2;

/** Marked duels before the measurement trusts them alone. */
const ENOUGH_MARKED = 40_000;

const GROUPS = ['Top', 'Jungle', 'Mid', 'Bot (2 gegen 2)'];
const PHASES = ['Level 1–5', 'Level 6–10', 'Level 11+'];

/** Cumulative experience at which a level is reached (level 2 at 280, each level 100 more than the last). */
const XP_FOR_LEVEL = [0, 280, 660, 1140, 1720, 2400, 3180, 4060, 5040, 6120, 7300, 8580, 9960, 11440, 13020, 14700, 16480, 18360];
const levelOfXp = (xp: number): number => {
  let level = 1;
  while (level < 18 && xp >= XP_FOR_LEVEL[level]!) level++;
  return level;
};

const num = (value: number, digits = 1): string => value.toFixed(digits).replace('.', ',');
const signed = (value: number, digits = 1): string => `${value >= 0 ? '+' : '−'}${num(Math.abs(value), digits)}`;
const pct = (value: number, digits = 1): string => `${num(value * 100, digits)} %`;
const count = (value: number): string => Math.round(value).toLocaleString('de-DE');
const sigmoid = (z: number): number => 1 / (1 + Math.exp(-z));
const load = <T>(file: string): T => JSON.parse(readFileSync(file, 'utf8').replace(/^﻿/, '')) as T;

interface Fighter {
  place: number;
  championId: number;
  lane: number;
  level: number;
  gold: number;
  armor: number;
  magicResist: number;
  /** Experience after ten minutes, 0 where the line has none. */
  xpAtTen: number;
}

interface Duel {
  /** The line's index in its patch: the held-out split and the cluster for error bars. */
  game: number;
  /** The line's levels are the ones going into the duel. */
  marked: boolean;
  second: number;
  /** 0 top, 1 jungle, 2 mid, 3 bot. */
  group: number;
  /** 1 when blue's player made the kill. */
  y: number;
  winner: number;
  loser: number;
  blue: Fighter[];
  red: Fighter[];
}

function readDuels(lines: readonly number[][]): { duels: Duel[]; sampled: number; marked: number } {
  const duels: Duel[] = [];
  let sampled = 0;
  let markedLines = 0;
  lines.forEach((line, game) => {
    if (line.length < LANE_PART || (line.length > LANE_PART && line[LANE_PART] === NO_DUELS)) return;
    sampled++;
    const marked = line.length > LANE_PART && line[line.length - 1] === LEVELS_BEFORE_DUEL;
    if (marked) markedLines++;
    for (let at = LANE_PART; at < line.length; ) {
      const length = line[at]!;
      if (length <= 0 || at + 1 + length > line.length) break;
      const d = line.slice(at + 1, at + 1 + length);
      at += 1 + length;
      const [second, winner, loser] = d as [number, number, number];
      const fighters: Fighter[] = [];
      for (let i = 3; i + 4 < d.length; i += 5) {
        const place = d[i]!;
        const seat = line[place]!;
        fighters.push({
          place,
          championId: Math.floor(seat / 8),
          lane: (seat % 8) - 1,
          level: d[i + 1]!,
          gold: d[i + 2]!,
          armor: d[i + 3]!,
          magicResist: d[i + 4]!,
          xpAtTen: line[XP_AT_TEN + place] ?? 0,
        });
      }
      const blue = fighters.filter((f) => f.place < 5);
      const red = fighters.filter((f) => f.place >= 5);
      const lane = (line[winner]! % 8) - 1;
      const group = lane >= 3 ? 3 : lane;
      const size = group === 3 ? 2 : 1;
      if (group < 0 || blue.length !== size || red.length !== size || winner < 5 === loser < 5) continue;
      duels.push({ game, marked, second, group, y: winner < 5 ? 1 : 0, winner, loser, blue, red });
    }
  });
  return { duels, sampled, marked: markedLines };
}

interface Champion {
  id: number;
  name: string;
  physicalDamage?: number | null;
  magicDamage?: number | null;
}

type LevelOf = (f: Fighter) => number;
const recorded: LevelOf = (f) => f.level;
const fromXp: LevelOf = (f) => levelOfXp(f.xpAtTen);

interface Features {
  dLevel: number;
  /** Item worth, blue minus red, in 1.000 gold. */
  dGold: number;
  /** Armor against the physical share of the other side, blue minus red, in 50 armor. */
  dArmor: number;
  /** Magic resist against the magic share of the other side, in 50. */
  dMagicResist: number;
  phase: number;
}

function featuresOf(d: Duel, level: LevelOf, physical: (championId: number) => number): Features {
  const sum = (side: Fighter[], pick: (f: Fighter) => number): number => side.reduce((total, f) => total + pick(f), 0);
  const share = (side: Fighter[]): number => sum(side, (f) => physical(f.championId)) / side.length;
  const bluePhysical = share(d.blue);
  const redPhysical = share(d.red);
  const levels = sum(d.blue, level) + sum(d.red, level);
  const mean = levels / (d.blue.length + d.red.length);
  return {
    dLevel: sum(d.blue, level) - sum(d.red, level),
    dGold: (sum(d.blue, (f) => f.gold) - sum(d.red, (f) => f.gold)) / 1000,
    dArmor: (sum(d.blue, (f) => f.armor) * redPhysical - sum(d.red, (f) => f.armor) * bluePhysical) / 50,
    dMagicResist: (sum(d.blue, (f) => f.magicResist) * (1 - redPhysical) - sum(d.red, (f) => f.magicResist) * (1 - bluePhysical)) / 50,
    phase: mean < 6 ? 0 : mean < 11 ? 1 : 2,
  };
}

interface Variant {
  name: string;
  columns: string[];
  row: (f: Features) => number[];
}

const byPhase = (value: number, phase: number): number[] => [0, 1, 2].map((p) => (p === phase ? value : 0));

const VARIANTS: Variant[] = [
  { name: 'nur Level', columns: ['Level'], row: (f) => [f.dLevel] },
  { name: 'nur Itemwert', columns: ['1.000 Gold'], row: (f) => [f.dGold] },
  { name: 'Level + Itemwert', columns: ['Level', '1.000 Gold'], row: (f) => [f.dLevel, f.dGold] },
  { name: '+ Resistenzen', columns: ['Level', '1.000 Gold', '50 Rüstung gegen AD', '50 MR gegen AP'], row: (f) => [f.dLevel, f.dGold, f.dArmor, f.dMagicResist] },
  {
    name: 'Level + Itemwert je Abschnitt',
    columns: [...PHASES.map((p) => `Level (${p})`), ...PHASES.map((p) => `1.000 Gold (${p})`)],
    row: (f) => [...byPhase(f.dLevel, f.phase), ...byPhase(f.dGold, f.phase)],
  },
];
const PLAIN = 2;
const BY_PHASE = 4;

/** Mean log-loss of two sets of predictions and their difference, the error from whole games (a fight's kills hang together). */
function compare(duels: readonly Duel[], a: readonly number[], b: readonly number[]): { a: number; b: number; gain: number; error: number } {
  const perGame = new Map<number, { sum: number; n: number }>();
  let lossA = 0;
  let lossB = 0;
  const loss = (p: number, y: number): number => -Math.log(Math.max(1e-12, y === 1 ? p : 1 - p));
  duels.forEach((d, i) => {
    const la = loss(a[i]!, d.y);
    const lb = loss(b[i]!, d.y);
    lossA += la;
    lossB += lb;
    const g = perGame.get(d.game) ?? { sum: 0, n: 0 };
    g.sum += la - lb;
    g.n++;
    perGame.set(d.game, g);
  });
  const n = duels.length;
  const gain = (lossA - lossB) / n;
  let squares = 0;
  for (const g of perGame.values()) squares += (g.sum - gain * g.n) ** 2;
  const games = perGame.size;
  const error = games > 1 ? Math.sqrt((games / (games - 1)) * squares) / n : 0;
  return { a: lossA / n, b: lossB / n, gain, error };
}

/** One duel for the champions' own effects: the keys that speak for blue, the ones for red, and what level and items already say. */
interface EffectRow {
  plus: number[];
  minus: number[];
  offset: number;
  y: number;
}

/** Ridge-penalised effects on top of an offset: coordinate-wise Newton steps, each effect drawn to 0 with spread tau. */
function fitEffects(rows: readonly EffectRow[], keys: number, tau: number, sweeps = 10): Float64Array {
  const theta = new Float64Array(keys);
  const z = Float64Array.from(rows, (r) => r.offset);
  const rowsOf: number[][] = Array.from({ length: keys }, () => []);
  const signsOf: number[][] = Array.from({ length: keys }, () => []);
  rows.forEach((row, i) => {
    for (const k of row.plus) {
      rowsOf[k]!.push(i);
      signsOf[k]!.push(1);
    }
    for (const k of row.minus) {
      rowsOf[k]!.push(i);
      signsOf[k]!.push(-1);
    }
  });
  const penalty = 1 / (tau * tau);
  for (let sweep = 0; sweep < sweeps; sweep++) {
    for (let k = 0; k < keys; k++) {
      const list = rowsOf[k]!;
      if (list.length === 0) continue;
      const signs = signsOf[k]!;
      let gradient = -theta[k]! * penalty;
      let hessian = penalty;
      for (let j = 0; j < list.length; j++) {
        const i = list[j]!;
        const p = sigmoid(z[i]!);
        gradient += signs[j]! * (rows[i]!.y - p);
        hessian += p * (1 - p);
      }
      const step = gradient / hessian;
      theta[k]! += step;
      for (let j = 0; j < list.length; j++) z[list[j]!]! += signs[j]! * step;
    }
  }
  return theta;
}

const effectZ = (row: EffectRow, theta: Float64Array): number => {
  let z = row.offset;
  for (const k of row.plus) z += theta[k]!;
  for (const k of row.minus) z -= theta[k]!;
  return z;
};

const TAUS = [0.03, 0.06, 0.1, 0.15, 0.2, 0.3, 0.45];

/**
 * The inner split of the training games, for choosing how far effects are drawn to 0. From the high
 * bits of the hash: the duels sit in every eighth line, so the low bits of the index are the same
 * for all of them.
 */
const isInner = (game: number): boolean => ((Math.imul(game + 13, 2246822519) >>> 16) & 3) === 0;

interface EffectFit {
  tau: number;
  theta: Float64Array;
  /** Log-loss on the inner split per tau, the offset alone first. */
  inner: number[];
}

function chooseEffects(train: readonly Duel[], rows: readonly EffectRow[], keys: number): EffectFit {
  const fitRows = rows.filter((_, i) => !isInner(train[i]!.game));
  const checkRows = rows.filter((_, i) => isInner(train[i]!.game));
  const lossOf = (theta: Float64Array | null): number => {
    let total = 0;
    for (const row of checkRows) {
      const p = sigmoid(theta ? effectZ(row, theta) : row.offset);
      total -= Math.log(Math.max(1e-12, row.y === 1 ? p : 1 - p));
    }
    return checkRows.length > 0 ? total / checkRows.length : 0;
  };
  const inner = [lossOf(null)];
  let best = 0;
  let bestLoss = inner[0]!;
  for (const tau of TAUS) {
    const loss = lossOf(fitEffects(fitRows, keys, tau));
    inner.push(loss);
    if (loss < bestLoss - 1e-9) {
      bestLoss = loss;
      best = tau;
    }
  }
  return { tau: best, theta: best > 0 ? fitEffects(rows, keys, best) : new Float64Array(keys), inner };
}

class Keys {
  private readonly ids = new Map<string, number>();
  readonly names: string[] = [];
  of(key: string, create: boolean): number {
    let id = this.ids.get(key);
    if (id === undefined && create) {
      id = this.names.length;
      this.ids.set(key, id);
      this.names.push(key);
    }
    return id ?? -1;
  }
  get size(): number {
    return this.names.length;
  }
}

/** Removes keys that stand on both sides (a mirror duel): they say nothing. */
function cancel(plus: number[], minus: number[]): { plus: number[]; minus: number[] } {
  const both = new Set(plus.filter((k) => minus.includes(k)));
  return both.size === 0 ? { plus, minus } : { plus: plus.filter((k) => !both.has(k)), minus: minus.filter((k) => !both.has(k)) };
}

function options(args: string[]): { positional: string[]; named: Record<string, string> } {
  const positional: string[] = [];
  const named: Record<string, string> = {};
  for (let i = 0; i < args.length; i++) {
    const arg = args[i]!;
    if (arg.startsWith('--')) named[arg.slice(2)] = args[++i] ?? '';
    else positional.push(arg);
  }
  return { positional, named };
}

export async function main(args: string[]): Promise<number> {
  const { positional, named } = options(args);
  const [gamesPath, metaPath] = positional;
  if (!gamesPath || !metaPath) {
    console.error('Aufruf: npm run measure:duels -- <games.json.gz> <meta.json> [--levels all|marked] [--out bericht.md]');
    return 2;
  }

  const file = readGamesFile(gamesPath);
  const meta = load<{ champions: Champion[] }>(metaPath);
  const names = new Map(meta.champions.map((c) => [c.id, c.name]));
  const physicalOf = new Map(
    meta.champions.map((c) => {
      const total = (c.physicalDamage ?? 0) + (c.magicDamage ?? 0);
      return [c.id, total > 0 ? (c.physicalDamage ?? 0) / total : 0.5] as const;
    }),
  );
  const physical = (id: number): number => physicalOf.get(id) ?? 0.5;

  // Each patch is its own list of lines; the index is offset so that games stay apart.
  const all: Duel[] = [];
  let sampled = 0;
  let markedLines = 0;
  let offset = 0;
  for (const patch of file.patches) {
    const read = readDuels(patch.games);
    for (const d of read.duels) all.push({ ...d, game: d.game + offset });
    sampled += read.sampled;
    markedLines += read.marked;
    offset += patch.games.length;
  }
  const markedDuels = all.filter((d) => d.marked).length;
  const useMarked = named.levels ? named.levels === 'marked' : markedDuels >= ENOUGH_MARKED;
  const duels = useMarked ? all.filter((d) => d.marked) : all;

  const report: string[] = [];
  report.push(
    '# Messung: die Stärke-Skala im Spiel',
    '',
    `Datei vom ${file.collectedAtUtc.slice(0, 16).replace('T', ' ')} UTC · ${file.tier} · ${file.platform} · ${file.patches.map((p) => `Patch ${p.patch}: ${count(p.games.length)} Zeilen`).join(', ')}.`,
    `${count(sampled)} Zeilen mit Duellen, ${count(all.length)} Duelle (${num(all.length / Math.max(1, sampled))} je Spiel); davon ${count(markedLines)} Zeilen und ${count(markedDuels)} Duelle mit dem Level vor dem Kill (seit 13.11.2026).`,
    '',
    useMarked
      ? '**Gemessen an den Zeilen mit dem Level vor dem Kill.**'
      : '**Gemessen an allen Zeilen — das Level des Siegers enthält dort in rund jedem fünften Duell den Aufstieg durch den Kill selbst; das Gewicht des Levels ist zu hoch gemessen (Abschnitt „Der Fehler im Level“). Vorläufig.**',
    '',
  );

  // ── The error in the level ───────────────────────────────────────────────────────────────────
  {
    const unmarked = all.filter((d) => !d.marked);
    report.push('## Der Fehler im Level', '');
    report.push(
      'Duelle in den ersten Sekunden nach 10:00, Zeilen bis zum 13.11.2026: Das Level laut Erfahrung nach genau zehn Minuten ist das wahre Level unmittelbar davor. Wie oft steht in der Zeile ein Level mehr?',
      '',
      '| Sekunden nach 10:00 | Duelle | Sieger: 1 Level zu viel | Verlierer | die anderen zwei (Bot) |',
      '|---|---:|---:|---:|---:|',
    );
    for (const until of [603, 610, 630]) {
      const tallyOf = { winner: [0, 0], loser: [0, 0], other: [0, 0] };
      let n = 0;
      for (const d of unmarked) {
        if (d.second < 600 || d.second > until) continue;
        n++;
        for (const f of [...d.blue, ...d.red]) {
          if (f.xpAtTen <= 0) continue;
          const who = f.place === d.winner ? 'winner' : f.place === d.loser ? 'loser' : 'other';
          tallyOf[who][1]!++;
          if (f.level > levelOfXp(f.xpAtTen)) tallyOf[who][0]!++;
        }
      }
      const share = (t: number[]): string => (t[1]! > 0 ? pct(t[0]! / t[1]!) : '–');
      report.push(`| 0–${until - 600} | ${count(n)} | ${share(tallyOf.winner)} | ${share(tallyOf.loser)} | ${share(tallyOf.other)} |`);
    }
    report.push(
      '',
      'Der Aufstieg durch den Kill trägt in Riots Zeitleiste dieselbe Millisekunde wie der Kill und steht direkt dahinter (in vier nachgelesenen Zeitleisten bei 54 von 335 Kills für den Sieger, bei 50 für einen Helfer). Der Sammler nahm bis zum 13.11.2026 alles bis einschließlich dieser Millisekunde.',
      '',
    );

    // The same duels fitted with the recorded and with the true level: how far the weight is off.
    report.push(
      'Dieselben Duelle (10:00 bis 11:00) einmal mit dem Level der Zeile, einmal mit dem Level laut Erfahrung nach zehn Minuten:',
      '',
      '| Lane | Duelle | Level-Gewicht, Level der Zeile | Level-Gewicht, wahres Level | Gold-Gewicht, Level der Zeile | Gold-Gewicht, wahres Level |',
      '|---|---:|---:|---:|---:|---:|',
    );
    for (let group = 0; group <= GROUPS.length; group++) {
      const window = unmarked.filter((d) => (group === GROUPS.length || d.group === group) && d.second >= 600 && d.second < 660 && [...d.blue, ...d.red].every((f) => f.xpAtTen > 0));
      if (window.length < 300) continue;
      const y = window.map((d) => d.y);
      const fitWith = (level: LevelOf): LogisticFit => fitLogistic(window.map((d) => VARIANTS[PLAIN]!.row(featuresOf(d, level, physical))), y);
      const a = fitWith(recorded);
      const b = fitWith(fromXp);
      const cell = (fit: LogisticFit, j: number): string => `${signed(fit.coefficients[j]!, 3)} ± ${num(1.96 * fit.standardErrors[j]!, 3)}`;
      report.push(`| ${group === GROUPS.length ? 'alle' : GROUPS[group]} | ${count(window.length)} | ${cell(a, 1)} | ${cell(b, 1)} | ${cell(a, 2)} | ${cell(b, 2)} |`);
    }
    report.push('', 'Gewichte in Log-Odds; ± ist das 95-%-Intervall. Das wahre Level ist hier bis zu 60 Sekunden alt — für beide Seiten gleich, ohne Blick auf den Ausgang.', '');
  }

  // ── The scale from the duels whose true level is known ───────────────────────────────────────
  // Until marked lines are there, the only clean levels are the ones right after 10:00. Fitted on
  // the training games' duels of that minute, checked on the held-out games' — per lane, with the
  // solo lanes pooled, and as the scale's steps would show it.
  {
    const clean = all.filter((d) => !d.marked && d.second >= 600 && d.second < 660 && [...d.blue, ...d.red].every((f) => f.xpAtTen > 0));
    const fitOn = clean.filter((d) => !isHeldOut(d.game));
    const check = clean.filter((d) => isHeldOut(d.game));
    const rowOf = (d: Duel): number[] => VARIANTS[PLAIN]!.row(featuresOf(d, fromXp, physical));
    const fitGroup = (groups: number[]): LogisticFit => {
      const rows = fitOn.filter((d) => groups.includes(d.group));
      // Mirrored as well: which side is called blue says nothing, the scale has no side.
      const x = rows.flatMap((d) => [rowOf(d), rowOf(d).map((v) => -v)]);
      const y = rows.flatMap((d) => [d.y, 1 - d.y]);
      return fitLogistic(x, y);
    };
    const perLane = [0, 1, 2, 3].map((g) => fitGroup([g]));
    const solo = fitGroup([0, 1, 2]);
    const zOf = (fit: LogisticFit, d: Duel): number => {
      const row = rowOf(d);
      return fit.coefficients[1]! * row[0]! + fit.coefficients[2]! * row[1]!;
    };
    const predictWith = (pick: (d: Duel) => LogisticFit): number[] => check.map((d) => sigmoid(zOf(pick(d), d)));
    const lanePredictions = predictWith((d) => perLane[d.group]!);
    const pooledPredictions = predictWith((d) => (d.group === 3 ? perLane[3]! : solo));
    const coinFlip = check.map(() => 0.5);
    const a = compare(check, coinFlip, lanePredictions);
    const b = compare(check, coinFlip, pooledPredictions);
    const c = compare(check, lanePredictions, pooledPredictions);
    const hitOf = (p: number[]): string => pct(check.filter((d, i) => p[i]! !== 0.5 && (p[i]! > 0.5 ? 1 : 0) === d.y).length / Math.max(1, check.filter((_, i) => p[i]! !== 0.5).length));
    const weight = (fit: LogisticFit, j: number): string => `${signed(fit.coefficients[j]!, 2)} ± ${num(1.96 * Math.SQRT2 * fit.standardErrors[j]!, 2)}`;
    report.push(
      '## Die Skala aus den Duellen mit wahrem Level (10:00 bis 11:00)',
      '',
      `${count(fitOn.length)} Duelle zum Anpassen, ${count(check.length)} aus zurückgehaltenen Spielen. Level laut Erfahrung nach zehn Minuten; ohne Seitenvorteil angepasst (jedes Duell auch gespiegelt, die Fehlerbalken dafür um √2 geweitet).`,
      '',
      '| Gewichte | Level | 1.000 Gold | Duelle (Anpassen) |',
      '|---|---:|---:|---:|',
      ...[0, 1, 2, 3].map((g) => `| ${GROUPS[g]} | ${weight(perLane[g]!, 1)} | ${weight(perLane[g]!, 2)} | ${count(fitOn.filter((d) => d.group === g).length)} |`),
      `| Top, Jungle und Mid zusammen | ${weight(solo, 1)} | ${weight(solo, 2)} | ${count(fitOn.filter((d) => d.group < 3).length)} |`,
      '',
      '| Variante | Log-Loss (zurückgehalten) | Gewinn gegen Münzwurf | in Standardfehlern | Trefferquote, wo sie eine Seite vorn sieht |',
      '|---|---:|---:|---:|---:|',
      `| je Lane | ${num(a.b, 4)} | ${signed(a.gain, 4)} | ${a.error > 0 ? signed(a.gain / a.error, 1) : '–'} | ${hitOf(lanePredictions)} |`,
      `| Top, Jungle, Mid zusammen; Bot eigen | ${num(b.b, 4)} | ${signed(b.gain, 4)} | ${b.error > 0 ? signed(b.gain / b.error, 1) : '–'} | ${hitOf(pooledPredictions)} |`,
      '',
      `„Zusammen“ gegen „je Lane“: ${signed(c.gain, 4)} Log-Loss (${c.error > 0 ? signed(c.gain / c.error, 1) : '–'} Standardfehler).`,
      '',
    );

    // As the tile would show it: the step of the side seen ahead, and how often that side made the kill.
    for (const [name, predictions] of [['je Lane', lanePredictions], ['Top, Jungle, Mid zusammen; Bot eigen', pooledPredictions]] as const) {
      report.push(`### Stufen der Skala (${name}), ein Schritt = 0,6 Log-Odds`, '', '| Stufe der vorn gesehenen Seite | Duelle | im Mittel vorhergesagt | so oft gewonnen | ± |', '|---|---:|---:|---:|---:|');
      const labels = ['unter 0,5 (Ausgeglichen)', '0,5 bis 1,5 (Leicht stärker)', '1,5 bis 2,5 (Stärker)', 'ab 2,5 (Deutlich stärker)'];
      const bins = labels.map(() => ({ n: 0, said: 0, won: 0 }));
      check.forEach((d, i) => {
        const p = predictions[i]!;
        if (p === 0.5) return;
        const favourite = Math.max(p, 1 - p);
        const step = Math.log(favourite / (1 - favourite)) / 0.6;
        const bin = bins[step < 0.5 ? 0 : step < 1.5 ? 1 : step < 2.5 ? 2 : 3]!;
        bin.n++;
        bin.said += favourite;
        bin.won += p > 0.5 === (d.y === 1) ? 1 : 0;
      });
      bins.forEach((bin, k) => {
        if (bin.n > 0) report.push(`| ${labels[k]} | ${count(bin.n)} | ${pct(bin.said / bin.n)} | ${pct(bin.won / bin.n)} | ${num(196 * Math.sqrt(0.25 / bin.n))} |`);
      });
      report.push('');
    }
  }

  // ── The models on held-out games ─────────────────────────────────────────────────────────────
  const train = duels.filter((d) => !isHeldOut(d.game));
  const held = duels.filter((d) => isHeldOut(d.game));
  report.push(
    '## Vorhersage auf zurückgehaltenen Spielen',
    '',
    `${count(train.length)} Duelle zum Anpassen, ${count(held.length)} aus zurückgehaltenen Spielen (2 von 10, dieselbe Teilung wie in Messung 13). Vorhergesagt wird, welche Seite den Kill macht. Log-Loss, kleiner ist besser; 0,6931 ist Münzwurf. „Gewinn“ gegen die Zeile darüber in Standardfehlern (aus ganzen Spielen gerechnet, weil die Kills eines Kampfes zusammenhängen).`,
    '',
  );

  const heldPredictions: number[][] = [];
  const fits: LogisticFit[][] = [];
  for (const variant of VARIANTS) {
    const predictions = new Array<number>(held.length).fill(0.5);
    const perGroup: LogisticFit[] = [];
    for (let group = 0; group < GROUPS.length; group++) {
      const rows = train.filter((d) => d.group === group);
      const fit = fitLogistic(rows.map((d) => variant.row(featuresOf(d, recorded, physical))), rows.map((d) => d.y));
      perGroup.push(fit);
      held.forEach((d, i) => {
        if (d.group === group) predictions[i] = predict(fit, variant.row(featuresOf(d, recorded, physical)));
      });
    }
    fits.push(perGroup);
    heldPredictions.push(predictions);
  }

  // The champions' own strength, on top of the plain variant by phase or not — whichever predicts better.
  const base = compare(held, heldPredictions[PLAIN]!, heldPredictions[BY_PHASE]!).gain > 0 ? BY_PHASE : PLAIN;
  const offsetOf = (d: Duel): number => {
    const fit = fits[base]![d.group]!;
    const row = VARIANTS[base]!.row(featuresOf(d, recorded, physical));
    let z = fit.coefficients[0]!;
    row.forEach((value, j) => (z += fit.coefficients[j + 1]! * value));
    return z;
  };

  interface Layer {
    name: string;
    keysOf: (d: Duel, f: Features, keys: Keys, create: boolean) => { plus: number[]; minus: number[] };
  }
  const championKey = (f: Fighter, phase: number | null): string => `${f.championId}:${f.lane}${phase === null ? '' : `:${phase}`}`;
  const layers: Layer[] = [
    {
      name: 'Champion-Stärke je Lane',
      keysOf: (d, _f, keys, create) =>
        cancel(d.blue.map((x) => keys.of(championKey(x, null), create)).filter((k) => k >= 0), d.red.map((x) => keys.of(championKey(x, null), create)).filter((k) => k >= 0)),
    },
    {
      name: 'Champion-Stärke je Lane und Abschnitt',
      keysOf: (d, f, keys, create) =>
        cancel(d.blue.map((x) => keys.of(championKey(x, f.phase), create)).filter((k) => k >= 0), d.red.map((x) => keys.of(championKey(x, f.phase), create)).filter((k) => k >= 0)),
    },
    {
      name: 'dazu das Paar (Champion gegen Champion, gleiche Lane)',
      keysOf: (d, _f, keys, create) => {
        const plus: number[] = [];
        const minus: number[] = [];
        for (const b of d.blue) {
          const r = d.red.find((x) => x.lane === b.lane);
          if (!r || r.championId === b.championId) continue;
          const low = Math.min(b.championId, r.championId);
          const high = Math.max(b.championId, r.championId);
          const k = keys.of(`${low}>${high}:${b.lane}`, create);
          if (k >= 0) (b.championId === low ? plus : minus).push(k);
        }
        return { plus, minus };
      },
    },
  ];

  interface LayerResult {
    name: string;
    predictions: number[];
    fit: EffectFit;
    keys: Keys;
  }
  const layerResults: LayerResult[] = [];
  const trainFeatures = train.map((d) => featuresOf(d, recorded, physical));
  const heldFeatures = held.map((d) => featuresOf(d, recorded, physical));
  const trainOffset = train.map(offsetOf);
  const heldOffset = held.map(offsetOf);

  const runLayer = (layer: Layer, offsetsTrain: number[], offsetsHeld: number[]): LayerResult & { trainZ: number[]; heldZ: number[] } => {
    const keys = new Keys();
    const rows: EffectRow[] = train.map((d, i) => ({ ...layer.keysOf(d, trainFeatures[i]!, keys, true), offset: offsetsTrain[i]!, y: d.y }));
    const fit = chooseEffects(train, rows, keys.size);
    const heldRows: EffectRow[] = held.map((d, i) => ({ ...layer.keysOf(d, heldFeatures[i]!, keys, false), offset: offsetsHeld[i]!, y: d.y }));
    const trainZ = rows.map((r) => effectZ(r, fit.theta));
    const heldZ = heldRows.map((r) => effectZ(r, fit.theta));
    return { name: layer.name, predictions: heldZ.map(sigmoid), fit, keys, trainZ, heldZ };
  };

  const plainChampion = runLayer(layers[0]!, trainOffset, heldOffset);
  const phaseChampion = runLayer(layers[1]!, trainOffset, heldOffset);
  const betterChampion = compare(held, plainChampion.predictions, phaseChampion.predictions).gain > 0 ? phaseChampion : plainChampion;
  const pair = runLayer(layers[2]!, betterChampion.trainZ, betterChampion.heldZ);
  layerResults.push(plainChampion, phaseChampion, pair);

  const coin = held.map(() => 0.5);
  report.push('| Variante | Log-Loss | Gewinn gegen | um | in Standardfehlern | Trefferquote |', '|---|---:|---|---:|---:|---:|');
  const hit = (predictions: readonly number[]): string => pct(held.filter((d, i) => (predictions[i]! > 0.5 ? 1 : 0) === d.y).length / Math.max(1, held.length));
  const line = (name: string, predictions: readonly number[], against: readonly number[] | null, againstName: string): void => {
    const c = against ? compare(held, against, predictions) : null;
    const own = compare(held, coin, predictions).b;
    report.push(`| ${name} | ${num(own, 4)} | ${c ? againstName : '–'} | ${c ? signed(c.gain, 4) : '–'} | ${c && c.error > 0 ? signed(c.gain / c.error, 1) : '–'} | ${hit(predictions)} |`);
  };
  line('Münzwurf', coin, null, '');
  line(VARIANTS[0]!.name, heldPredictions[0]!, coin, 'Münzwurf');
  line(VARIANTS[1]!.name, heldPredictions[1]!, coin, 'Münzwurf');
  line(VARIANTS[PLAIN]!.name, heldPredictions[PLAIN]!, heldPredictions[0]!, VARIANTS[0]!.name);
  line(VARIANTS[3]!.name, heldPredictions[3]!, heldPredictions[PLAIN]!, VARIANTS[PLAIN]!.name);
  line(VARIANTS[BY_PHASE]!.name, heldPredictions[BY_PHASE]!, heldPredictions[PLAIN]!, VARIANTS[PLAIN]!.name);
  const baseName = VARIANTS[base]!.name;
  line(`${baseName} + ${plainChampion.name}`, plainChampion.predictions, heldPredictions[base]!, baseName);
  line(`${baseName} + ${phaseChampion.name}`, phaseChampion.predictions, heldPredictions[base]!, baseName);
  line(`… ${pair.name}`, pair.predictions, betterChampion.predictions, betterChampion.name);
  report.push('');

  report.push('### Je Lane', '', `| Lane | Duelle (zurückgehalten) | ${baseName} | + Champion-Stärke | Gewinn in Standardfehlern | + Paar | Gewinn in Standardfehlern |`, '|---|---:|---:|---:|---:|---:|---:|');
  for (let group = 0; group < GROUPS.length; group++) {
    const index = held.map((d, i) => (d.group === group ? i : -1)).filter((i) => i >= 0);
    const sub = index.map((i) => held[i]!);
    const pick = (p: readonly number[]): number[] => index.map((i) => p[i]!);
    const a = compare(sub, pick(heldPredictions[base]!), pick(betterChampion.predictions));
    const b = compare(sub, pick(betterChampion.predictions), pick(pair.predictions));
    report.push(`| ${GROUPS[group]} | ${count(sub.length)} | ${num(a.a, 4)} | ${num(a.b, 4)} | ${a.error > 0 ? signed(a.gain / a.error, 1) : '–'} | ${num(b.b, 4)} | ${b.error > 0 ? signed(b.gain / b.error, 1) : '–'} |`);
  }
  report.push('');

  // ── The weights ──────────────────────────────────────────────────────────────────────────────
  report.push('## Die Gewichte', '', 'Log-Odds je Einheit, angepasst an den Duellen zum Anpassen; ± ist das 95-%-Intervall (ohne Rücksicht auf zusammenhängende Kills, also etwas zu eng). „Seite“ ist der Vorteil von Blau bei Gleichstand.', '');
  for (const v of [PLAIN, 3, BY_PHASE]) {
    const variant = VARIANTS[v]!;
    report.push(`### ${variant.name}`, '', `| Lane | Duelle | Seite | ${variant.columns.join(' | ')} |`, `|---|---:|---:|${variant.columns.map(() => '---:').join('|')}|`);
    for (let group = 0; group < GROUPS.length; group++) {
      const fit = fits[v]![group]!;
      const cells = fit.coefficients.map((c, j) => `${signed(c, 3)} ± ${num(1.96 * fit.standardErrors[j]!, 3)}`);
      report.push(`| ${GROUPS[group]} | ${count(train.filter((d) => d.group === group).length)} | ${cells.join(' | ')} |`);
    }
    report.push('');
  }
  report.push(
    'Lesehilfe: +0,40 Log-Odds bei Gleichstand heißt 60 % statt 50 %. Auf der Botlane sind Level und Itemwert die Summe beider Spieler.',
    '',
  );

  // ── How far the champions' effects are drawn in, and who stands where ────────────────────────
  report.push('## Champion-Stärke: Glättung und Größe', '', `Jeder Effekt wird zur 0 gezogen (Ridge); die Streuung τ ist auf einem inneren Viertel der Anpass-Spiele gewählt. „keine“ heißt: Jede Stärke größer 0 sagt dort schlechter vorher als gar keine.`, '');
  report.push(`| Schicht | Effekte | Log-Loss ohne | ${TAUS.map((t) => `τ ${num(t, 2)}`).join(' | ')} | gewählt |`, `|---|---:|---:|${TAUS.map(() => '---:').join('|')}|---|`);
  for (const r of layerResults) report.push(`| ${r.name} | ${count(r.keys.size)} | ${r.fit.inner.map((l) => num(l, 4)).join(' | ')} | ${r.fit.tau > 0 ? `τ ${num(r.fit.tau, 2)}` : 'keine'} |`);
  report.push('');

  if (betterChampion.fit.tau > 0) {
    const withPhase = betterChampion === phaseChampion;
    report.push(`### Die größten Effekte (${betterChampion.name})`, '', 'Geglättet, in Log-Odds; Lane 0 Top, 1 Jungle, 2 Mid, 3 ADC, 4 Support.', '');
    const games = new Map<number, number>();
    train.forEach((d, i) => {
      const k = layers[withPhase ? 1 : 0]!.keysOf(d, trainFeatures[i]!, betterChampion.keys, false);
      for (const key of [...k.plus, ...k.minus]) games.set(key, (games.get(key) ?? 0) + 1);
    });
    const ranked = betterChampion.keys.names.map((name, k) => ({ name, k, value: betterChampion.fit.theta[k]!, n: games.get(k) ?? 0 })).sort((a, b) => b.value - a.value);
    const show = (e: (typeof ranked)[number]): string => {
      const [id, lane, phase] = e.name.split(':').map(Number) as [number, number, number | undefined];
      return `| ${names.get(id) ?? `#${id}`} | ${lane} | ${phase === undefined ? '–' : PHASES[phase]} | ${count(e.n)} | ${signed(e.value, 3)} |`;
    };
    report.push('| Champion | Lane | Abschnitt | Duelle | Effekt |', '|---|---:|---|---:|---:|', ...ranked.slice(0, 12).map(show), '| … | | | | |', ...ranked.slice(-12).map(show), '');
  }

  // ── Does the scale keep its word ─────────────────────────────────────────────────────────────
  report.push(
    '## Hält die Skala, was sie sagt?',
    '',
    `Zurückgehaltene Duelle, nach der vorhergesagten Chance der vorn gesehenen Seite. Variante: ${baseName}.`,
    '',
    '| Lane | vorhergesagt | Duelle | im Mittel vorhergesagt | so oft gewonnen | ± |',
    '|---|---|---:|---:|---:|---:|',
  );
  const EDGES = [0.5, 0.55, 0.6, 0.65, 0.7, 0.8, 0.9, 1.0001];
  for (let group = 0; group <= GROUPS.length; group++) {
    for (let b = 0; b + 1 < EDGES.length; b++) {
      let n = 0;
      let said = 0;
      let won = 0;
      held.forEach((d, i) => {
        if (group < GROUPS.length && d.group !== group) return;
        const p = heldPredictions[base]![i]!;
        const favourite = Math.max(p, 1 - p);
        if (favourite < EDGES[b]! || favourite >= EDGES[b + 1]!) return;
        n++;
        said += favourite;
        won += p >= 0.5 === (d.y === 1) ? 1 : 0;
      });
      if (n < 50) continue;
      report.push(`| ${group === GROUPS.length ? 'alle' : GROUPS[group]} | ${pct(EDGES[b]!, 0)} bis ${pct(Math.min(1, EDGES[b + 1]!), 0)} | ${count(n)} | ${pct(said / n)} | ${pct(won / n)} | ${num(196 * Math.sqrt(0.25 / n))} |`);
    }
  }
  report.push('');

  // How the predictions spread: what the seven steps of the scale would stand for.
  {
    const spread = held.map((_, i) => Math.abs(Math.log(heldPredictions[base]![i]! / (1 - heldPredictions[base]![i]!)))).sort((a, b) => a - b);
    const at = (q: number): number => spread[Math.min(spread.length - 1, Math.floor(q * spread.length))] ?? 0;
    report.push(
      '### Wie weit die Skala ausschlägt',
      '',
      '| Anteil der Duelle | Abstand zur Mitte bis (Log-Odds) | als Chance |',
      '|---:|---:|---:|',
      ...[0.25, 0.5, 0.75, 0.9, 0.95, 0.99].map((q) => `| ${pct(q, 0)} | ${num(at(q), 2)} | ${pct(sigmoid(at(q)))} |`),
      '',
    );
  }

  const out = path.resolve(named.out ?? path.join('..', 'build', 'measure-duels.md'));
  mkdirSync(path.dirname(out), { recursive: true });
  writeFileSync(out, report.join('\n') + '\n');
  console.log(`${count(duels.length)} Duelle gemessen (${useMarked ? 'Level vor dem Kill' : 'alle Zeilen, Level mit Fehler'}) → ${out}`);
  return 0;
}
