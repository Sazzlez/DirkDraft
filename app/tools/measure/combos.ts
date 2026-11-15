// Ability combos inside a team: a kit with a kit. Amumu's ult holds them, Miss Fortune's ult hits
// them all; a knock-up opens Yasuo's ult; an enchanter stands behind a hypercarry. A combo like that
// works through the skills, not through either champion's strength — so it shows, if anywhere, as
// an edge of the team beyond the lane strengths of its five.
//
// The lists are judgement, clear cases only (absent = neutral). They were drafted as
// build/wombo-entwurf.md and fixed by the user on 15.11.2026 ("mit Ergänzungen"). A first look with
// narrower lists had been taken on the games up to 13.11.2026, so what counts is the measurement
// on games collected after that (--before): those never saw a list. Like the mechanic counters
// the lists stood here, in the measurement; since the user wanted the combos as a hint at the pick
// they live in the app (src/core/model/kits.ts) and are read from there — still without a weight.
//
//   npm run measure:combos -- <games.json.gz> <meta.json> [--before alt-games.json.gz] [--lists shown] [--out bericht.md]

import { mkdirSync, readFileSync, writeFileSync } from 'node:fs';
import path from 'node:path';
import { readGamesFile, decode, type Game, type Seat } from './games';
import { COMBOS, comboSets as setsOf, type Combo, type ComboLists, type ComboSets } from '../../src/core/model/kits';
import { fitLogistic } from './logistic';

/**
 * The lists the measurement of 15.11.2026 ran on (docs/modellpruefung.md, Messung 28), as the user
 * fixed them ("mit Ergänzungen"). Kept as they were so the numbers can be counted again; the app
 * shows narrower ones (kits.ts COMBOS), measured with `--lists shown`.
 */
export const WIDE: ComboLists = {
  aoeEngage: [
    'Amumu', 'Malphite', 'Sejuani', 'Zac', 'Rell', 'Jarvan IV', 'Wukong', 'Diana', 'Leona', 'Nautilus', 'Ornn', 'Maokai', 'Galio',
    'Gragas', 'Alistar', 'Rakan', 'Skarner', 'Hecarim', 'Neeko', 'Lissandra', 'Kennen', 'Annie', 'Seraphine', 'Sona', 'Orianna',
    'Fiddlesticks', 'Nunu & Willump', 'Sett', 'Gnar', 'Qiyana', 'Lillia', 'Sion', 'Yone',
  ],
  aoeDamage: [
    'Orianna', 'Miss Fortune', 'Yasuo', 'Katarina', 'Kennen', 'Brand', 'Karthus', 'Fiddlesticks', 'Rumble', 'Hwei', 'Vladimir',
    'Swain', 'Samira', 'Anivia', 'Veigar', 'Ziggs', 'Xerath', 'Lux', 'Seraphine', 'Viktor', 'Zyra', 'Aurelion Sol', 'Smolder',
    'Gangplank', 'Morgana', 'Nilah', 'Lillia', "Vel'Koz", 'Mel', 'Jinx', 'Twitch', 'Annie', 'Diana', 'Yone', 'Vex', 'Corki',
    'Sivir', 'Varus', 'Cassiopeia',
  ],
  knockUp: [
    'Malphite', 'Alistar', 'Gragas', 'Nautilus', 'Ornn', 'Zac', 'Wukong', 'Vi', 'Jarvan IV', 'Rakan', 'Rell', 'Janna', "Cho'Gath",
    "Rek'Sai", 'Aatrox', 'Riven', 'Xin Zhao', 'Lee Sin', 'Blitzcrank', 'Nami', 'Zyra', 'Lulu', 'Poppy', 'Sion', 'Maokai', 'Singed',
    'Pyke', 'Hecarim', 'Orianna', 'Azir', 'Gnar', 'Sejuani', 'Skarner', 'Nunu & Willump', 'Neeko', 'Braum', 'Diana', 'Tristana',
    "Vel'Koz", 'Thresh', 'Kled', 'Shyvana', 'Taliyah', 'Syndra',
  ],
  yasuo: ['Yasuo'],
  enchanter: ['Lulu', 'Yuumi', 'Milio', 'Janna', 'Nami', 'Soraka', 'Sona', 'Karma', 'Renata Glasc', 'Seraphine', 'Taric', 'Zilean', 'Ivern'],
  hypercarry: [
    "Kog'Maw", 'Jinx', 'Vayne', 'Twitch', 'Zeri', 'Aphelios', "Kai'Sa", 'Master Yi', "Bel'Veth", 'Kayle', 'Kindred', 'Smolder',
    'Tristana', 'Sivir', 'Xayah', 'Kalista', 'Nilah',
  ],
};

/** The measured lists as champion ids, or the ones the app shows. */
export const comboSets = (champions: readonly { id: number; key: string; name: string }[], lists: ComboLists = WIDE): { sets: ComboSets; missing: string[] } =>
  setsOf(champions, lists);

export { COMBOS, type Combo, type ComboSets };

const countOf = (team: readonly Seat[], set: ReadonlySet<number>): number => team.filter((s) => set.has(s.championId)).length;

/** Two different champions, one from each list: Orianna alone is no wombo. */
export function hasPair(team: readonly Seat[], a: ReadonlySet<number>, b: ReadonlySet<number>): boolean {
  return team.some((x) => a.has(x.championId) && team.some((y) => y.championId !== x.championId && b.has(y.championId)));
}

export interface ComboRule {
  label: string;
  /** A plain count that keeps the rule below it honest: what the kits are worth without the combo. */
  control: boolean;
  of: (team: readonly Seat[], sets: ComboSets) => number;
}

/** The first rule that is not a control is the one the lists were written for. */
export const COMBO_RULES: readonly ComboRule[] = [
  { label: 'Zahl AoE-Engage', control: true, of: (t, s) => countOf(t, s.aoeEngage) },
  { label: 'Zahl AoE-Schaden', control: true, of: (t, s) => countOf(t, s.aoeDamage) },
  { label: 'Wombo: AoE-Engage + AoE-Schaden im Team', control: false, of: (t, s) => (hasPair(t, s.aoeEngage, s.aoeDamage) ? 1 : 0) },
  { label: 'zwei oder mehr AoE-Engage', control: false, of: (t, s) => (countOf(t, s.aoeEngage) >= 2 ? 1 : 0) },
  { label: 'Zahl Knock-ups', control: true, of: (t, s) => countOf(t, s.knockUp) },
  { label: 'Yasuo: je Knock-up im Team', control: false, of: (t, s) => (countOf(t, s.yasuo) > 0 ? countOf(t, s.knockUp) : 0) },
  { label: 'Zahl Enchanter', control: true, of: (t, s) => countOf(t, s.enchanter) },
  { label: 'Zahl Hypercarrys', control: true, of: (t, s) => countOf(t, s.hypercarry) },
  { label: 'Enchanter + Hypercarry im Team', control: false, of: (t, s) => (hasPair(t, s.enchanter, s.hypercarry) ? 1 : 0) },
];

/** Per rule, blue's value minus red's: the feature the fit weighs. */
export function comboFeatures(game: Game, sets: ComboSets, rules: readonly ComboRule[] = COMBO_RULES): number[] {
  return rules.map((rule) => rule.of(game.blue, sets) - rule.of(game.red, sets));
}

// ---- the measurement ------------------------------------------------------------------------------

const FOLDS = 5;
/** As the app shrinks its own lane rates (shrinkage.ts OWN_LANE_PRIOR). */
const LANE_PRIOR = 800;

const num = (value: number, digits = 1): string => value.toFixed(digits).replace('.', ',');
const signed = (value: number, digits = 1): string => `${value >= 0 ? '+' : '−'}${num(Math.abs(value), digits)}`;
const count = (value: number): string => Math.round(value).toLocaleString('de-DE');

/** What identifies a game line across two files: the ten players, the winner and the length. */
const lineKey = (line: readonly number[]): string => line.slice(0, 12).join(',');

interface Row {
  rule: ComboRule;
  /** Share of teams the rule holds for. */
  teams: number;
  /** In points of win rate at 50 %. */
  effect: number;
  /** Half the 95 % interval, in points. */
  margin: number;
  sigma: number;
}

/**
 * Win on the summed lane strengths and the combo features, on the games of `index`. The lane
 * strengths are counted on the other four fifths of all games, so no game rates its own champions.
 */
function measure(games: readonly Game[], strengths: readonly number[], sets: ComboSets, index: readonly number[]): { rows: Row[]; strength: number; strengthMargin: number } {
  const x: number[][] = [];
  const y: number[] = [];
  const teams = COMBO_RULES.map(() => 0);
  for (const i of index) {
    const game = games[i]!;
    COMBO_RULES.forEach((rule, j) => {
      if (rule.of(game.blue, sets) > 0) teams[j]!++;
      if (rule.of(game.red, sets) > 0) teams[j]!++;
    });
    x.push([strengths[i]!, ...comboFeatures(game, sets)]);
    y.push(game.blueWins);
  }
  const fit = fitLogistic(x, y);
  const rows = COMBO_RULES.map((rule, j) => {
    const weight = fit.coefficients[j + 2]!;
    const error = fit.standardErrors[j + 2]!;
    return { rule, teams: teams[j]! / Math.max(1, 2 * index.length), effect: weight * 25, margin: 1.96 * error * 25, sigma: error > 0 ? weight / error : 0 };
  });
  return { rows, strength: fit.coefficients[1]!, strengthMargin: 1.96 * fit.standardErrors[1]! };
}

/** Blue's summed lane strength minus red's for every game, each from the folds the game is not in. */
function crossFittedStrengths(games: readonly Game[]): number[] {
  const key = (seat: Seat): number => seat.championId * 8 + seat.lane + 1;
  const tallies = Array.from({ length: FOLDS }, () => new Map<number, [number, number]>());
  games.forEach((game, i) => {
    const tally = tallies[i % FOLDS]!;
    for (const [team, won] of [
      [game.blue, game.blueWins],
      [game.red, 1 - game.blueWins],
    ] as const) {
      for (const seat of team) {
        const entry = tally.get(key(seat)) ?? [0, 0];
        entry[0] += won;
        entry[1] += 1;
        tally.set(key(seat), entry);
      }
    }
  });
  const strength = (seat: Seat, fold: number): number => {
    let wins = 0;
    let plays = 0;
    for (let f = 0; f < FOLDS; f++) {
      if (f === fold) continue;
      const entry = tallies[f]!.get(key(seat));
      if (!entry) continue;
      wins += entry[0];
      plays += entry[1];
    }
    const rate = (wins + LANE_PRIOR * 0.5) / (plays + LANE_PRIOR);
    return Math.log(rate / (1 - rate));
  };
  return games.map((game, i) => game.blue.reduce((sum, seat) => sum + strength(seat, i % FOLDS), 0) - game.red.reduce((sum, seat) => sum + strength(seat, i % FOLDS), 0));
}

/** A team by what it has of the wombo: nothing, only the engage, only the damage, or both. */
export type WomboKind = 'none' | 'engage' | 'damage' | 'wombo';

export function womboKind(team: readonly Seat[], sets: ComboSets): WomboKind {
  if (hasPair(team, sets.aoeEngage, sets.aoeDamage)) return 'wombo';
  if (countOf(team, sets.aoeEngage) > 0) return 'engage';
  return countOf(team, sets.aoeDamage) > 0 ? 'damage' : 'none';
}

const WOMBO_AGAINST: readonly { label: string; others: readonly WomboKind[] }[] = [
  { label: 'alle anderen Teams', others: ['none', 'engage', 'damage'] },
  { label: 'Teams nur mit AoE-Engage (der Pick bringt den Schaden dazu)', others: ['engage'] },
  { label: 'Teams nur mit AoE-Schaden (der Pick bringt den Engage dazu)', others: ['damage'] },
];

/**
 * The plain question behind a pick: does a team with the wombo win more than a team without, beyond
 * the lane strengths? No counts as controls — a wombo team against the kind of team it would have
 * been without the pick. The other kinds of teams get their own weight, so they do not blur the line.
 */
function plainWombo(report: string[], games: readonly Game[], strengths: readonly number[], sets: ComboSets, index: readonly number[]): void {
  const kinds: WomboKind[] = ['none', 'engage', 'damage', 'wombo'];
  const share = new Map<WomboKind, number>(kinds.map((kind) => [kind, 0]));
  for (const i of index) for (const team of [games[i]!.blue, games[i]!.red]) share.set(womboKind(team, sets), share.get(womboKind(team, sets))! + 1);
  const teams = (kind: WomboKind): string => `${num((share.get(kind)! / Math.max(1, 2 * index.length)) * 100)} %`;
  report.push(
    `Einfach gezählt — Teams mit Wombo ${teams('wombo')}, nur AoE-Engage ${teams('engage')}, nur AoE-Schaden ${teams('damage')}, weder noch ${teams('none')}:`,
    '',
    '| Team mit Wombo gegen … | im Sieg (Punkte, 95 %) | Abstand |',
    '|---|---|---:|',
  );
  for (const against of WOMBO_AGAINST) {
    // The reference is what `against` names; every other kind but the wombo gets a column of its own.
    const rest = kinds.filter((kind) => kind !== 'wombo' && !against.others.includes(kind));
    const columns: WomboKind[] = ['wombo', ...rest];
    const x = index.map((i) => [strengths[i]!, ...columns.map((kind) => (womboKind(games[i]!.blue, sets) === kind ? 1 : 0) - (womboKind(games[i]!.red, sets) === kind ? 1 : 0))]);
    const fit = fitLogistic(
      x,
      index.map((i) => games[i]!.blueWins),
    );
    const weight = fit.coefficients[2]!;
    const error = fit.standardErrors[2]!;
    report.push(`| ${against.label} | ${signed(weight * 25, 2)} ± ${num(1.96 * error * 25, 2)} | ${signed(error > 0 ? weight / error : 0)} σ |`);
  }
  report.push('');
}

function table(report: string[], title: string, note: string, result: ReturnType<typeof measure>, games: number): void {
  report.push(
    `## ${title}`,
    '',
    `${count(games)} Spiele. ${note} Lane-Stärke (Summe): ${num(result.strength, 3)} ± ${num(result.strengthMargin, 3)}.`,
    '',
    '| Merkmal | Teams | im Sieg (Punkte, 95 %) | Abstand |',
    '|---|---:|---|---:|',
    ...result.rows.map((row) => `| ${row.rule.control ? `_${row.rule.label} (Kontrolle, je Champion)_` : `**${row.rule.label}**`} | ${num(row.teams * 100)} % | ${signed(row.effect, 2)} ± ${num(row.margin, 2)} | ${signed(row.sigma)} σ |`),
    '',
  );
}

export async function main(args: string[]): Promise<number> {
  const paths: string[] = [];
  const named: Record<string, string> = {};
  for (let i = 0; i < args.length; i++) {
    if (args[i]!.startsWith('--')) named[args[i]!.slice(2)] = args[++i] ?? '';
    else paths.push(args[i]!);
  }
  const [gamesPath, metaPath] = paths;
  if (!gamesPath || !metaPath) {
    console.error('Aufruf: npm run measure:combos -- <games.json.gz> <meta.json> [--before alt-games.json.gz] [--lists shown] [--out bericht.md]');
    return 2;
  }

  const meta = JSON.parse(readFileSync(metaPath, 'utf8').replace(/^﻿/, '')) as { champions: { id: number; key: string; name: string }[] };
  const shown = named.lists === 'shown';
  const { sets, missing } = comboSets(meta.champions, shown ? COMBOS : WIDE);
  const file = readGamesFile(gamesPath);
  const before = named.before ? new Set(readGamesFile(named.before).patches.flatMap((p) => p.games.map(lineKey))) : null;

  const games: Game[] = [];
  const isNew: boolean[] = [];
  for (const patch of file.patches) {
    for (const line of patch.games) {
      const game = decode(line);
      if (!game) continue;
      games.push(game);
      isNew.push(before ? !before.has(lineKey(line)) : false);
    }
  }
  const strengths = crossFittedStrengths(games);
  const all = games.map((_, i) => i);

  const report: string[] = [
    '# Fähigkeiten-Combos im Team',
    '',
    `Stand ${file.collectedAtUtc.slice(0, 16)} UTC, ${file.tier}, ${file.platform}, Patch ${file.patches.map((p) => p.patch).join(', ')}. ` +
      (shown
        ? 'Die engen Listen, aus denen die App ihre Hinweise zeigt (`src/core/model/kits.ts`); gewählt nach der Messung der breiten. '
        : 'Listen nach Einschätzung (`tools/measure/combos.ts`, WIDE), vom Nutzer am 15.11.2026 festgelegt. ') +
      'Gemessen ist, was ein Merkmal im Sieg ' +
      'wert ist über die Lane-Stärke der zehn Champions hinaus; die Kontrollen zählen die Kits ohne die Combo, damit die Combo nicht ' +
      'bekommt, was die Champions einzeln bringen.',
    '',
  ];
  if (missing.length > 0) report.push(`Nicht im Roster: ${missing.join(', ')}.`, '');

  let verdict = '';
  if (before) {
    const fresh = all.filter((i) => isNew[i]);
    const old = all.filter((i) => !isNew[i]);
    const onFresh = measure(games, strengths, sets, fresh);
    table(report, 'Neue Spiele — die Prüfung', 'Nicht in der früheren Datei: Diese Spiele haben die Listen nie gesehen.', onFresh, fresh.length);
    plainWombo(report, games, strengths, sets, fresh);
    table(report, 'Frühere Spiele', 'Schon in der früheren Datei; auf ihnen wurde mit engeren Listen vorab geschaut.', measure(games, strengths, sets, old), old.length);
    plainWombo(report, games, strengths, sets, old);
    const first = onFresh.rows.find((row) => !row.rule.control)!;
    verdict = `${first.rule.label}, neue Spiele: ${signed(first.effect, 2)} ± ${num(first.margin, 2)} Punkte (${signed(first.sigma)} σ)`;
  }
  const onAll = measure(games, strengths, sets, all);
  table(report, 'Alle Spiele', before ? 'Beide Teile zusammen.' : 'Ohne `--before` gibt es keine Spiele, die die Listen nie gesehen haben.', onAll, all.length);
  plainWombo(report, games, strengths, sets, all);

  const out = path.resolve(named.out ?? path.join('..', 'build', shown ? 'measure-combos-shown.md' : 'measure-combos.md'));
  mkdirSync(path.dirname(out), { recursive: true });
  writeFileSync(out, report.join('\n') + '\n');
  console.log(`${count(games.length)} Spiele${before ? `, davon ${count(isNew.filter(Boolean).length)} neu` : ''} → ${out}`);
  if (verdict) console.log(verdict);
  return 0;
}
