// Phase 2, open counters (docs/modellpruefung.md, Messung 18): does the file carry counters beyond
// what both lane strengths predict, or only as many as chance marks?
//
// - What the app's rule finds: the smoothed edge of every lane matchup and how many champion-lanes
//   have one or two open counters (recommender openCounters, COUNTER_MINIMUM_EDGE).
// - The comparison with chance: duel directions whose measured rate stands 2, 2,5 or 3 standard errors
//   above the pair's expectation, against how many a file without any counter would mark (the exact
//   binomial chance of each direction, summed).
// - With an earlier file: the directions marked there, checked on the games counted since. A real
//   counter keeps its edge on fresh games; a chance hit falls back to the expectation.
//
// It changes no threshold; what it finds goes into docs/modellpruefung.md first.
//
//   npm run measure:counters -- <meta.json> [--before alt.json] [--out bericht.md]

import { mkdirSync, readFileSync, writeFileSync } from 'node:fs';
import path from 'node:path';
import { ALL_LANES, Lane } from '../../src/core/model/lane';
import { MetaLookup, type MatchupStat, type MetaSnapshot } from '../../src/core/model/metaLookup';
import { COUNTER_MINIMUM_EDGE, openCounters } from '../../src/core/model/recommender';
import { logit, sigmoid } from '../../src/core/model/scoreModel';

const LANE_NAMES: Record<number, string> = { [Lane.Top]: 'Top', [Lane.Jungle]: 'Jungle', [Lane.Mid]: 'Mid', [Lane.Adc]: 'Bot', [Lane.Support]: 'Support' };
const THRESHOLDS = [2, 2.5, 3] as const;

const num = (value: number, digits = 1): string => value.toFixed(digits).replace('.', ',');
const signed = (value: number, digits = 1): string => `${value >= 0 ? '+' : '−'}${num(Math.abs(value), digits)}`;
const count = (value: number): string => Math.round(value).toLocaleString('de-DE');
const load = <T>(file: string): T => JSON.parse(readFileSync(file, 'utf8').replace(/^﻿/, '')) as T;

const logFactorials: number[] = [0];
const logFactorial = (n: number): number => {
  for (let i = logFactorials.length; i <= n; i++) logFactorials.push(logFactorials[i - 1]! + Math.log(i));
  return logFactorials[n]!;
};

/** The chance that n games at rate p show at least `least` wins. */
function tail(n: number, p: number, least: number): number {
  if (least <= 0) return 1;
  if (least > n) return 0;
  let total = 0;
  for (let k = least; k <= n; k++) total += Math.exp(logFactorial(n) - logFactorial(k) - logFactorial(n - k) + k * Math.log(p) + (n - k) * Math.log(1 - p));
  return Math.min(1, total);
}

interface Direction {
  stat: MatchupStat;
  wins: number;
  /** What both lane strengths predict for this direction (the duel line of the file). */
  expected: number;
  /** Measured rate above the expectation, in standard errors of the expectation. */
  z: number;
}

/** Every duel direction of the file with its expectation, read through the app's own lookup. */
function directions(snapshot: MetaSnapshot): { meta: MetaLookup; list: Direction[] } {
  const meta = new MetaLookup(snapshot);
  const list: Direction[] = [];
  for (const stat of snapshot.matchups) {
    if (!(stat.play > 0) || stat.lane === Lane.Unknown) continue;
    const expected = sigmoid(meta.matchupBaseline(stat.championId, stat.opponentId, stat.lane));
    if (!(expected > 0 && expected < 1)) continue;
    const error = Math.sqrt((expected * (1 - expected)) / stat.play);
    list.push({ stat, wins: Math.round(stat.winRate * stat.play), expected, z: (stat.winRate - expected) / error });
  }
  return { meta, list };
}

/** Wins needed in n games at expectation p to stand `threshold` standard errors above it. */
const winsNeeded = (n: number, p: number, threshold: number): number => Math.ceil(n * (p + threshold * Math.sqrt((p * (1 - p)) / n)) - 1e-9);

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
  if (files.length < 1) {
    console.error('Aufruf: npm run measure:counters -- <meta.json> [--before alt.json] [--out bericht.md]');
    return 2;
  }
  const snapshot = load<MetaSnapshot>(files[0]!);
  const before = options.before ? load<MetaSnapshot>(options.before) : null;
  const { meta, list } = directions(snapshot);

  const report: string[] = [];
  report.push('# Offene Counter (Phase 2, Werkzeug tools/measure/counters.ts)', '');
  report.push(
    `Daten: meta.json aus ${count(snapshot.games ?? 0)} Spielen, Patch ${snapshot.patch}.` +
      (before ? ` Zum Vergleich der Stand aus ${count(before.games ?? 0)} Spielen (${(before.dataAsOfUtc ?? '').slice(0, 16).replace('T', ' ')} UTC).` : ''),
    '',
  );

  // What the app shows: the smoothed edge, and the champion-lanes with open counters.
  report.push('## Was die Regel der App findet', '', '| Lane | Duell-Richtungen | Median Spiele | geglättete Abweichung: Median | 95 % unter | größte | Champion-Lanes | mit 1 offenem Counter | mit 2 oder mehr |', '|---|---:|---:|---:|---:|---:|---:|---:|---:|');
  const none = new Set<number>();
  const median = (values: number[], q = 0.5): number => {
    const sorted = [...values].sort((a, b) => a - b);
    return sorted.length > 0 ? sorted[Math.min(sorted.length - 1, Math.floor(q * sorted.length))]! : 0;
  };
  let largest: { edge: number; stat: MatchupStat } | null = null;
  for (const lane of [...ALL_LANES, null]) {
    const rows = list.filter((d) => lane === null || d.stat.lane === lane);
    const edges: number[] = [];
    for (const d of rows) {
      const view = meta.matchup(d.stat.championId, d.stat.opponentId, d.stat.lane);
      if (!view) continue;
      const edge = logit(view.winRate) - meta.matchupBaseline(d.stat.championId, d.stat.opponentId, d.stat.lane);
      edges.push(Math.abs(edge) * 25);
      if (lane === null && (!largest || edge > largest.edge)) largest = { edge, stat: d.stat };
    }
    let lanes = 0;
    let one = 0;
    let more = 0;
    for (const l of lane === null ? ALL_LANES : [lane]) {
      for (const championId of meta.roster(l)) {
        if (!meta.laneStat(championId, l)) continue;
        lanes++;
        const open = openCounters(meta, championId, l, none, 0).length;
        if (open === 1) one++;
        else if (open >= 2) more++;
      }
    }
    report.push(
      `| ${lane === null ? 'alle' : LANE_NAMES[lane]} | ${count(rows.length)} | ${count(median(rows.map((d) => d.stat.play)))} | ${num(median(edges))} Pkt | ${num(median(edges, 0.95))} Pkt | ${num(Math.max(0, ...edges))} Pkt | ${lanes} | ${one} | ${more} |`,
    );
  }
  if (largest)
    report.push(
      '',
      `Schwelle: ${signed(COUNTER_MINIMUM_EDGE, 2)} log-odds (${num(COUNTER_MINIMUM_EDGE * 25)} Punkte) nach Glättung. Größte Abweichung: ${meta.championName(largest.stat.championId)} gegen ` +
        `${meta.championName(largest.stat.opponentId)} (${LANE_NAMES[largest.stat.lane]}, ${count(largest.stat.play)} Spiele, gemessen ${num(largest.stat.winRate * 100)} %, geglättet ${signed(largest.edge * 25)} Punkte).`,
    );
  report.push('');

  // Marked against expected by chance.
  report.push('## Markierte Counter gegen die Zufallserwartung', '', 'Gemessene Rate über der Erwartung des Paars, in Standardfehlern. „Durch Zufall erwartet“: so viele markierte eine Datei, in der jedes Duell genau seiner Erwartung folgt (exakte Binomialverteilung je Richtung).', '');
  report.push('| Lane | Richtungen | ' + THRESHOLDS.map((t) => `ab ${num(t)}: markiert | erwartet`).join(' | ') + ' |', '|---|---:|' + THRESHOLDS.map(() => '---:|---:|').join(''));
  for (const lane of [...ALL_LANES, null]) {
    const rows = list.filter((d) => lane === null || d.stat.lane === lane);
    const cells = THRESHOLDS.map((t) => {
      const marked = rows.filter((d) => d.z >= t).length;
      const expected = rows.reduce((sum, d) => sum + tail(d.stat.play, d.expected, winsNeeded(d.stat.play, d.expected, t)), 0);
      return `${count(marked)} | ${num(expected, 0)} ± ${num(1.96 * Math.sqrt(expected), 0)}`;
    });
    report.push(`| ${lane === null ? 'alle' : LANE_NAMES[lane]} | ${count(rows.length)} | ${cells.join(' | ')} |`);
  }
  report.push('', 'Der Fehlerbalken der Erwartung ist grob (Poisson); die beiden Richtungen eines Paars sind Spiegelbilder und zählen hier einzeln.', '');

  // The same for well counted pairs only: there a counter has room to show.
  report.push('### Nur gut gezählte Paare', '', '| ab Spielen | Richtungen | ' + THRESHOLDS.map((t) => `ab ${num(t)}: markiert | erwartet`).join(' | ') + ' |', '|---:|---:|' + THRESHOLDS.map(() => '---:|---:|').join(''));
  for (const least of [100, 200, 400]) {
    const rows = list.filter((d) => d.stat.play >= least);
    const cells = THRESHOLDS.map((t) => {
      const marked = rows.filter((d) => d.z >= t).length;
      const expected = rows.reduce((sum, d) => sum + tail(d.stat.play, d.expected, winsNeeded(d.stat.play, d.expected, t)), 0);
      return `${count(marked)} | ${num(expected, 0)} ± ${num(1.96 * Math.sqrt(expected), 0)}`;
    });
    report.push(`| ${count(least)} | ${count(rows.length)} | ${cells.join(' | ')} |`);
  }
  report.push('');

  // Fresh games: what was marked on the earlier file, on the games counted since.
  if (before) {
    const then = directions(before);
    const current = new Map(list.map((d) => [`${d.stat.lane}:${d.stat.championId}:${d.stat.opponentId}`, d]));
    report.push(
      '## Halten die markierten Counter auf neuen Spielen?',
      '',
      `Markiert auf dem älteren Stand, geprüft auf den Spielen, die seither dazukamen (Zählung jetzt minus Zählung damals; ${count((snapshot.games ?? 0) - (before.games ?? 0))} Spiele). ` +
        'Ein echter Counter behält seinen Vorsprung, ein Zufallstreffer fällt auf die Erwartung zurück. „Geglättet erwartet“: was die Glättung der App (je Lane) vom alten Vorsprung übrig lässt.',
      '',
      '| markiert ab | Richtungen | neue Spiele | Vorsprung damals | Vorsprung auf neuen Spielen | davon wieder | geglättet erwartet |',
      '|---|---:|---:|---:|---:|---:|---:|',
    );
    for (const t of THRESHOLDS) {
      let n = 0;
      let games = 0;
      let wins = 0;
      let expectedWins = 0;
      let oldEdge = 0;
      let kept = 0;
      for (const d of then.list) {
        if (d.z < t) continue;
        const now = current.get(`${d.stat.lane}:${d.stat.championId}:${d.stat.opponentId}`);
        if (!now) continue;
        const fresh = now.stat.play - d.stat.play;
        if (fresh <= 0) continue;
        n++;
        games += fresh;
        wins += now.wins - d.wins;
        // The expectation of the earlier file: the fresh games have not moved it.
        expectedWins += fresh * d.expected;
        oldEdge += fresh * (d.stat.winRate - d.expected);
        kept += fresh * (d.stat.winRate - d.expected) * (d.stat.play / (d.stat.play + then.meta.matchupPrior(d.stat.lane)));
      }
      if (games === 0) continue;
      const edge = (wins - expectedWins) / games;
      const error = Math.sqrt(0.25 / games);
      report.push(
        `| ${num(t)} Standardfehlern | ${count(n)} | ${count(games)} | ${signed((oldEdge / games) * 100)} Pkt | ${signed(edge * 100)} ± ${num(1.96 * error * 100)} Pkt | ${num((edge / (oldEdge / games)) * 100, 0)} % | ${signed((kept / games) * 100)} Pkt |`,
      );
    }
    report.push('');
  }

  const out = path.resolve(options.out ?? path.join('..', 'build', 'measure-counters.md'));
  mkdirSync(path.dirname(out), { recursive: true });
  writeFileSync(out, report.join('\n') + '\n');
  console.log(`${count(list.length)} Duell-Richtungen gemessen → ${out}`);
  return 0;
}
