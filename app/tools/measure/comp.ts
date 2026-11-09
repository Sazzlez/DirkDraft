// Phase 2 of docs/draft-konzept.md, the composition part: does a comp feature predict the winner
// beyond the champions' own strength? Reads the collector's game lines (games.json.gz, an artifact of
// the run, never published), the meta.json of the same bracket and builds.json; computes both
// line-ups exactly as the app does (CompAnalyzer, traits, TankShred) and writes a report. It changes
// no weight: what it finds goes into docs/modellpruefung.md first, and only then into the model.
//
//   npm run measure -- <games.json.gz> <meta.json> <builds.json> [--out bericht.md] [--patch 16.22]

import { mkdirSync, readFileSync, writeFileSync } from 'node:fs';
import path from 'node:path';
import { CompAnalyzer, hasIssue, PLAN_NAMES, type CompIssue, type CompProfile, type Plan } from '../../src/core/model/compAnalyzer';
import { Lane } from '../../src/core/model/lane';
import { isTagged, MetaLookup, type MetaSnapshot } from '../../src/core/model/metaLookup';
import { logit } from '../../src/core/model/scoreModel';
import { TankShred } from '../../src/core/model/tankShred';
import { TraitTable } from '../../src/core/model/traits';
import type { RiotBuildFile } from '../../src/core/riotBuilds';
import { gamesOf, isHeldOut, readGamesFile, type Seat } from './games';
import { fitLogistic, logLoss, predict, type LogisticFit } from './logistic';

/** One side as the measurement reads it. */
export interface Side {
  profile: CompProfile;
  /** Sum of the poke trait (2.1). */
  poke: number;
  /** Champions that build against tanks on their lane, assassins left out (B4). */
  shredders: number;
}

export interface Feature {
  key: string;
  label: string;
  isNew: boolean;
  /** The side's value against the other side; the game's value is blue's minus red's. */
  of(side: Side, other: Side): number;
}

const flag = (issue: CompIssue) => (side: Side) => (hasIssue(side.profile, issue) ? 1 : 0);

export const FEATURES: readonly Feature[] = [
  { key: 'noMagicDamage', label: 'AP-Schaden fehlt', isNew: false, of: flag('noMagicDamage') },
  { key: 'noPhysicalDamage', label: 'AD-Schaden fehlt', isNew: false, of: flag('noPhysicalDamage') },
  { key: 'noFrontline', label: 'kein Frontline', isNew: false, of: flag('noFrontline') },
  { key: 'noEngage', label: 'kein Engage', isNew: false, of: flag('noEngage') },
  { key: 'noPeel', label: 'kein Peel', isNew: false, of: flag('noPeel') },
  { key: 'littleCrowdControl', label: 'wenig CC', isNew: false, of: flag('littleCrowdControl') },
  { key: 'allMelee', label: 'nur Melee', isNew: false, of: flag('allMelee') },
  { key: 'littleWaveclear', label: 'wenig Waveclear', isNew: true, of: flag('littleWaveclear') },
  { key: 'strongWaveclear', label: 'Champions mit gutem Waveclear (je einer)', isNew: true, of: (s) => s.profile.strongWaveclearCount },
  { key: 'ranged', label: 'Ranged-Champions (je einer)', isNew: true, of: (s) => s.profile.rangedCount },
  { key: 'poke', label: 'Poke (Summe des Traits, je Punkt)', isNew: true, of: (s) => s.poke },
  { key: 'shred', label: 'Tank-Shred gegen 2+ Frontliner (je Shredder)', isNew: true, of: (s, o) => (o.profile.frontlineCount >= 2 ? s.shredders : 0) },
  { key: 'early', label: 'Early-Game-Champions (je einer)', isNew: true, of: (s) => s.profile.earlyScalingCount },
  { key: 'late', label: 'Late-Game-Champions (je einer)', isNew: true, of: (s) => s.profile.lateScalingCount },
  { key: 'mixedTiming', label: 'gemischtes Timing', isNew: true, of: flag('mixedTiming') },
];

/** Games a feature must tell the sides apart in; rarer ones run away in the fit (separation). */
export const MINIMUM_DIFFERING_GAMES = 200;

/** A log-odds shift as points at 50 %, signed, with a 95 % error bar. */
const points = (value: number, error: number): string =>
  `${value >= 0 ? '+' : '−'}${Math.abs(value * 25).toFixed(2).replace('.', ',')} ± ${(1.96 * error * 25).toFixed(2).replace('.', ',')}`;

const pct = (value: number): string => `${(value * 100).toFixed(1).replace('.', ',')} %`;

const count = (value: number): string => value.toLocaleString('de-DE');

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

/** One side's comp as the measurement reads it, with the app's rules. */
export function sideOf(meta: MetaLookup, traits: TraitTable, shred: TankShred, analyzer: CompAnalyzer, seats: readonly Seat[]): Side {
  let poke = 0;
  let shredders = 0;
  for (const s of seats) {
    const champion = meta.champion(s.championId);
    poke += traits.for(champion?.key).poke;
    if (champion && !isTagged(champion, 'Assassin') && shred.has(s.championId, s.lane)) shredders++;
  }
  return { profile: analyzer.analyze(seats.map((s) => s.championId)), poke, shredders };
}

export async function main(argv: string[]): Promise<number> {
  const { files, options } = args(argv);
  if (files.length < 3) {
    console.error('Aufruf: npm run measure -- <games.json.gz> <meta.json> <builds.json> [--out bericht.md] [--patch 16.22]');
    return 2;
  }
  const [gamesPath, metaPath, buildsPath] = files as [string, string, string];
  const gamesFile = readGamesFile(gamesPath);
  const snapshot = JSON.parse(readFileSync(metaPath, 'utf8').replace(/^﻿/, '')) as MetaSnapshot;
  const builds = JSON.parse(readFileSync(buildsPath, 'utf8').replace(/^﻿/, '')) as RiotBuildFile;
  const patch = options.patch ?? snapshot.patch;

  const meta = new MetaLookup(snapshot);
  const traits = TraitTable.bundled();
  const shred = TankShred.fromBuilds(builds);
  const analyzer = new CompAnalyzer(meta, traits, shred);

  const games = gamesOf(gamesFile, patch);
  if (games.length === 0) {
    console.error(`Keine Spiele für Patch ${patch}.`);
    return 1;
  }

  // The champions' own strength on their lanes, as the model reads it (shrunk lane win rates).
  const strength = (seats: Seat[]): number =>
    seats.reduce((sum, s) => {
      const lane = s.lane === Lane.Unknown ? meta.mainLane(s.championId) : s.lane;
      return sum + logit(meta.laneStat(s.championId, lane)?.winRate ?? 0.5);
    }, 0);

  const side = (seats: Seat[]): Side => sideOf(meta, traits, shred, analyzer, seats);

  const rows: { baseline: number; features: number[]; y: number; blue: Side; red: Side; minutes: number }[] = [];
  for (const g of games) {
    const blue = side(g.blue);
    const red = side(g.red);
    rows.push({
      baseline: strength(g.blue) - strength(g.red),
      features: FEATURES.map((f) => f.of(blue, red) - f.of(red, blue)),
      y: g.blueWins,
      blue,
      red,
      minutes: g.minutes,
    });
  }

  const y = rows.map((r) => r.y);
  const base = rows.map((r) => [r.baseline]);
  const modelA = fitLogistic(base, y);
  const expected = rows.map((r) => predict(modelA, [r.baseline]));

  // Each feature alone on top of strength, then all together — only those that differ often enough.
  const differing = FEATURES.map((_, j) => rows.filter((r) => r.features[j] !== 0).length);
  const used = FEATURES.map((_, j) => j).filter((j) => differing[j]! >= MINIMUM_DIFFERING_GAMES);
  const wide = (r: (typeof rows)[number]) => [r.baseline, ...used.map((j) => r.features[j]!)];
  const alone = FEATURES.map((_, j) => (used.includes(j) ? fitLogistic(rows.map((r) => [r.baseline, r.features[j]!]), y) : null));
  const joint = fitLogistic(rows.map(wide), y);

  // Held-out games: does the comp predict the winner of games it was not fitted on?
  const train = rows.filter((_, i) => !isHeldOut(i));
  const test = rows.filter((_, i) => isHeldOut(i));
  const holdA = fitLogistic(train.map((r) => [r.baseline]), train.map((r) => r.y));
  const holdB = fitLogistic(train.map(wide), train.map((r) => r.y));
  const accuracy = (fit: LogisticFit, withComp: boolean) =>
    test.filter((r) => (predict(fit, withComp ? wide(r) : [r.baseline]) >= 0.5 ? 1 : 0) === r.y).length / Math.max(1, test.length);
  const lossA = logLoss(holdA, test.map((r) => [r.baseline]), test.map((r) => r.y));
  const lossB = logLoss(holdB, test.map(wide), test.map((r) => r.y));

  const report: string[] = [];
  report.push(`# Comp-Messung (Phase 2, Werkzeug tools/measure/comp.ts)`, '');
  report.push(
    `Daten: ${count(games.length)} Spiele, Patch ${patch}, ${gamesFile.tier} · ${gamesFile.platform}, gesammelt bis ${gamesFile.collectedAtUtc.slice(0, 16).replace('T', ' ')} UTC. ` +
      `Stärke aus meta.json (${count(snapshot.games ?? 0)} Spiele), Tank-Shred aus builds.json (${count(shred.count)} Champion-Lanes).`,
    '',
    'Gemessen wird, was ein Merkmal über die Stärke der zehn Champions hinaus erklärt: logistische Regression ' +
      'auf den Sieg von Blau mit der Stärke-Differenz (Summe der Lane-Winrates in log-odds) und dem Merkmal als ' +
      'Differenz Blau minus Rot. Angegeben in Prozentpunkten je Einheit bei 50 %, mit 95-%-Fehlerbalken. ' +
      'Die Lane-Winrates stammen aus denselben Spielen; für die Comp-Frage reicht das, für die endgültige Messung ' +
      'zählt die Prüfung auf zurückgehaltenen Spielen unten.',
    '',
  );
  report.push(
    `Stärke allein: Koeffizient ${modelA.coefficients[1]!.toFixed(3).replace('.', ',')} (1 = Lane-Winrates passen genau), ` +
      `Seitenvorteil Blau ${points(modelA.coefficients[0]!, modelA.standardErrors[0]!)} Punkte.`,
    '',
  );

  report.push('## Merkmale', '', '| Merkmal | seit | Spiele mit Unterschied | einzeln (Pkt) | gemeinsam (Pkt) |', '|---|---|---|---|---|');
  FEATURES.forEach((f, j) => {
    const fit = alone[j];
    const k = used.indexOf(j);
    report.push(
      `| ${f.label} | ${f.isNew ? '2.1' : '1.5'} | ${pct(differing[j]! / rows.length)} | ` +
        (fit && k >= 0
          ? `${points(fit.coefficients[2]!, fit.standardErrors[2]!)} | ${points(joint.coefficients[k + 2]!, joint.standardErrors[k + 2]!)} |`
          : `zu selten (${count(differing[j]!)} Spiele) | — |`),
    );
  });
  report.push(
    '',
    'Lesart: Ein Merkmal zählt erst, wenn der Fehlerbalken die Null nicht mehr einschließt — und das Vorzeichen ' +
      'zur Regel passt (ein Befund wie „kein Frontline“ sollte negativ sein). „gemeinsam“ rechnet alle Merkmale ' +
      'zugleich; dort teilen sich verwandte Merkmale (Waveclear-Zahl und „wenig Waveclear“) die Wirkung. ' +
      `Merkmale, die in weniger als ${count(MINIMUM_DIFFERING_GAMES)} Spielen die Seiten unterscheiden, bleiben draußen: ` +
      'so selten läuft die Schätzung davon.',
    '',
  );

  report.push(
    '## Vorhersage auf zurückgehaltenen Spielen',
    '',
    `${count(train.length)} Spiele zum Anpassen, ${count(test.length)} zum Prüfen.`,
    '',
    '| Modell | Log-Loss | Treffer |',
    '|---|---|---|',
    `| Stärke allein | ${lossA.toFixed(4).replace('.', ',')} | ${pct(accuracy(holdA, false))} |`,
    `| Stärke + Comp-Merkmale | ${lossB.toFixed(4).replace('.', ',')} | ${pct(accuracy(holdB, true))} |`,
    '',
    `Unterschied im Log-Loss: ${(lossA - lossB).toFixed(4).replace('.', ',')} (positiv = Comp hilft).`,
    '',
  );

  // Archetype duels: observed minus expected win rate of a plan against another, mirror games left out.
  const plans: (Plan | null)[] = ['teamfight', 'pick', 'poke', 'protect', 'split', null];
  const planName = (p: Plan | null) => (p ? PLAN_NAMES[p] : 'offen');
  const cell = new Map<string, { n: number; obs: number; exp: number; variance: number }>();
  const frequency = new Map<string, number>();
  rows.forEach((r, i) => {
    const pb = r.blue.profile.plan;
    const pr = r.red.profile.plan;
    frequency.set(planName(pb), (frequency.get(planName(pb)) ?? 0) + 1);
    frequency.set(planName(pr), (frequency.get(planName(pr)) ?? 0) + 1);
    if (pb === pr) return;
    const p = expected[i]!;
    for (const [own, enemy, won, exp] of [
      [pb, pr, r.y, p],
      [pr, pb, 1 - r.y, 1 - p],
    ] as const) {
      const key = `${planName(own)}|${planName(enemy)}`;
      const c = cell.get(key) ?? { n: 0, obs: 0, exp: 0, variance: 0 };
      c.n++;
      c.obs += won;
      c.exp += exp;
      c.variance += exp * (1 - exp);
      cell.set(key, c);
    }
  });
  report.push(
    '## Archetyp-Duelle',
    '',
    `Plan je Team (beide Teams gezählt): ${plans.map((p) => `${planName(p)} ${pct((frequency.get(planName(p)) ?? 0) / (2 * rows.length))}`).join(' · ')}.`,
    '',
    'Zeile gegen Spalte: tatsächliche minus erwartete Winrate der Zeile in Punkten (95 %), Spiele in Klammern; unter 200 Spielen leer.',
    '',
    `| eigener Plan | ${plans.map(planName).join(' | ')} |`,
    `|---|${plans.map(() => '---').join('|')}|`,
  );
  for (const own of plans) {
    const cells = plans.map((enemy) => {
      if (own === enemy) return '—';
      const c = cell.get(`${planName(own)}|${planName(enemy)}`);
      if (!c || c.n < 200) return '';
      return `${points((c.obs - c.exp) / c.n / 0.25, Math.sqrt(c.variance) / c.n / 0.25)} (${count(c.n)})`;
    });
    report.push(`| ${planName(own)} | ${cells.join(' | ')} |`);
  }
  report.push('');

  // Timing against game length: does the side with more late-game champions win the long games?
  const buckets: [string, (m: number) => boolean][] = [
    ['unter 25 Min.', (m) => m > 0 && m < 25],
    ['25–29 Min.', (m) => m >= 25 && m < 30],
    ['30–34 Min.', (m) => m >= 30 && m < 35],
    ['ab 35 Min.', (m) => m >= 35],
  ];
  report.push(
    '## Timing und Spieldauer',
    '',
    'Seite mit mehr Late-Game-Champions: tatsächliche minus erwartete Winrate je Spieldauer. Nur beschreibend — die ' +
      'Dauer steht erst nach dem Spiel fest und taugt nicht als Vorhersage, aber sie zeigt, ob die Einordnung stimmt.',
    '',
    '| Dauer | Spiele | später skalierende Seite (Pkt) |',
    '|---|---|---|',
  );
  for (const [label, inBucket] of buckets) {
    let n = 0;
    let obs = 0;
    let exp = 0;
    let variance = 0;
    rows.forEach((r, i) => {
      const diff = r.blue.profile.lateScalingCount - r.red.profile.lateScalingCount;
      if (diff === 0 || !inBucket(r.minutes)) return;
      const p = diff > 0 ? expected[i]! : 1 - expected[i]!;
      n++;
      obs += diff > 0 ? r.y : 1 - r.y;
      exp += p;
      variance += p * (1 - p);
    });
    report.push(`| ${label} | ${count(n)} | ${n > 0 ? points((obs - exp) / n / 0.25, Math.sqrt(variance) / n / 0.25) : '—'} |`);
  }
  report.push(
    '',
    '## Was das nicht ist',
    '',
    '- Kein Gewicht: Das Werkzeug ändert am Modell nichts. Ein Merkmal bekommt erst nach dieser Messung ein Gewicht, ' +
      'eingetragen in docs/modellpruefung.md.',
    '- Ein Patch, eine Rangstufe. Merkmale aus Einschätzung (Traits) messen auch, wie gut die Einschätzung ist.',
    '- Bewusstes Kontern zeigt sich hier nicht: Riot gibt die Pick-Reihenfolge nicht heraus.',
  );

  const out = path.resolve(options.out ?? path.join('..', 'build', 'measure-comp.md'));
  mkdirSync(path.dirname(out), { recursive: true });
  writeFileSync(out, report.join('\n') + '\n');
  console.log(`${count(games.length)} Spiele gemessen → ${out}`);
  return 0;
}
