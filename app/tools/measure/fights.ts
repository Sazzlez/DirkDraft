// The check before the teamfight scale appears (docs/teamfight-skala.md, „Rechnung und Prüfung“).
// The collector keeps, in a sample of games (Spiel-Liste Version 5, RiotGames.Format in C#), every
// teamfight from minute 14 on: the kills of both sides, who was dead at the start and who was seen in
// it, every player's level and item worth going in; and per game the dragons, Elder, Baron, Atakhan
// and souls. Asked here, on games the fit has never seen:
//
//   1. Does the team the scale sees ahead win the teamfight more often — and as often as it says, in
//      every phase (14–20, 20–30, from 30 minutes)?
//   2. Which factor earns its place: item worth and level, weights per phase or relative to what the
//      teams own, Baron, Elder, dragons and souls, where the gold lies, the champions' own strength
//      per phase? Each has to predict the held-out fights better than the step before it.
//
// Only fights both teams went into at full strength, with at least --min-side players of each team
// seen (default 4), without an AFK player and not drawn in kills.
//
//   npm run measure:fights -- <games.json.gz> [meta.json] [--min-side 4] [--out bericht.md]

import { mkdirSync, readFileSync, writeFileSync } from 'node:fs';
import path from 'node:path';
import { cancel, chooseEffects, compare, effectZ, Keys, options, TAUS, type EffectRow } from './duels';
import { isHeldOut, readGamesFile } from './games';
import { fitLogistic, type LogisticFit } from './logistic';

const LANE_PART = 35;
const NO_DUELS = -1;
const LEVELS_BEFORE_DUEL = -2;
const FIGHTS_FOLLOW = -3;

/** A fight's numbers: second, the two sides' kills, two place masks, level and item worth of ten (RiotGames.FightLength). */
export const FIGHT_LENGTH = 25;

/** How long Baron's and the Elder's buffs last; the measurement gives them to the whole team, as the app will. */
export const BARON_SECONDS = 180;
export const ELDER_SECONDS = 150;

/** A player this many levels below the fight's median is taken for AFK (one sat on level 3 until minute 22, 22.11.2026). */
export const AFK_LEVELS = 5;

export const PHASES = ['14–20 Min.', '20–30 Min.', 'ab 30 Min.'];
export const phaseOf = (second: number): number => (second < 1200 ? 0 : second < 1800 ? 1 : 2);

export interface FightPlayer {
  place: number;
  championId: number;
  /** 0 top … 4 support, −1 unknown. */
  lane: number;
  level: number;
  gold: number;
}

export interface SideBuffs {
  /** Elemental dragons taken (not the Elder). */
  dragons: number;
  soul: boolean;
  /** Baron taken in the last BARON_SECONDS. */
  baron: boolean;
  /** Elder taken in the last ELDER_SECONDS. */
  elder: boolean;
  atakhan: number;
}

export interface Fight {
  /** The line's index (offset per patch): the held-out split and the cluster for error bars. */
  game: number;
  second: number;
  blueKills: number;
  redKills: number;
  /** Places dead at the start, one bit each (place 0 = bit 0). */
  dead: number;
  /** Places seen in the fight, one bit each. */
  seen: number;
  /** Places 0–4 blue, 5–9 red. */
  players: FightPlayer[];
  blue: SideBuffs;
  red: SideBuffs;
  /** 1 when blue made more kills, 0 when red did, 0,5 for a draw. */
  y: number;
}

/** Where the fight part begins (its −3), or −1 without one — as RiotGames.FightPartAt in C#. */
export function fightPartAt(line: readonly number[]): number {
  if (line.length < LANE_PART) return -1;
  let at = LANE_PART;
  while (at < line.length && line[at]! > 0 && at + 1 + line[at]! <= line.length) at += 1 + line[at]!;
  if (at < line.length && (line[at] === NO_DUELS || line[at] === LEVELS_BEFORE_DUEL)) at++;
  return at < line.length && line[at] === FIGHTS_FOLLOW ? at : -1;
}

/** What one side brings into a fight at this second, from the line's objectives (second, side, kind). */
export function buffsAt(objectives: readonly (readonly number[])[], side: number, second: number): SideBuffs {
  const own = objectives.filter((o) => o[1] === side && o[0]! < second);
  const kind = (k: number): (readonly number[])[] => own.filter((o) => o[2] === k);
  return {
    dragons: own.filter((o) => o[2]! >= 1 && o[2]! <= 6).length,
    soul: own.some((o) => o[2]! >= 21 && o[2]! <= 26),
    baron: kind(8).some((o) => second - o[0]! <= BARON_SECONDS),
    elder: kind(7).some((o) => second - o[0]! <= ELDER_SECONDS),
    atakhan: kind(9).length,
  };
}

/** The teamfights of a patch's lines; `sampled` counts the lines that carry the fight part. */
export function readFights(lines: readonly (readonly number[])[], offset = 0): { fights: Fight[]; sampled: number } {
  const fights: Fight[] = [];
  let sampled = 0;
  lines.forEach((line, index) => {
    const at = fightPartAt(line);
    if (at < 0) return;
    sampled++;
    const count = Math.max(0, line[at + 1] ?? 0);
    const objectives: number[][] = [];
    for (let i = at + 2; i + 2 < line.length && i + 2 <= at + 1 + count; i += 3) objectives.push([line[i]!, line[i + 1]!, line[i + 2]!]);
    for (let f = at + 2 + count; f < line.length; ) {
      const length = line[f]!;
      if (length <= 0 || f + 1 + length > line.length) break;
      const d = line.slice(f + 1, f + 1 + length);
      f += 1 + length;
      if (length < FIGHT_LENGTH) continue;
      const [second, blueKills, redKills, dead, seen] = d as [number, number, number, number, number];
      const players = Array.from({ length: 10 }, (_, place) => ({
        place,
        championId: Math.floor(line[place]! / 8),
        lane: (line[place]! % 8) - 1,
        level: d[5 + 2 * place]!,
        gold: d[6 + 2 * place]!,
      }));
      fights.push({
        game: index + offset,
        second,
        blueKills,
        redKills,
        dead,
        seen,
        players,
        blue: buffsAt(objectives, 0, second),
        red: buffsAt(objectives, 1, second),
        y: blueKills > redKills ? 1 : blueKills < redKills ? 0 : 0.5,
      });
    }
  });
  return { fights, sampled };
}

const seenOf = (mask: number, from: number): number => {
  let n = 0;
  for (let place = from; place < from + 5; place++) if (mask & (1 << place)) n++;
  return n;
};

export type Exclusion = 'dead' | 'small' | 'afk' | 'draw';

/** Why a fight stays out of the measurement, or null when it counts. */
export function exclusionOf(f: Fight, minSide: number): Exclusion | null {
  if (f.dead !== 0) return 'dead';
  if (seenOf(f.seen, 0) < minSide || seenOf(f.seen, 5) < minSide) return 'small';
  const levels = f.players.map((p) => p.level).sort((a, b) => a - b);
  if (levels[0]! <= (levels[4]! + levels[5]!) / 2 - AFK_LEVELS) return 'afk';
  if (f.y === 0.5) return 'draw';
  return null;
}

export interface Features {
  /** Sum of levels, blue minus red. */
  dLevel: number;
  /** Item worth, blue minus red, in 1.000 gold. */
  dGold: number;
  /** Item worth, blue minus red, in tenths of what a team owns on average in this fight. */
  dRelative: number;
  /** The richest player's item worth, blue minus red, in 1.000 gold. */
  dCarry: number;
  baron: number;
  elder: number;
  dragons: number;
  soul: number;
  atakhan: number;
  phase: number;
}

export function featuresOf(f: Fight): Features {
  const blue = f.players.slice(0, 5);
  const red = f.players.slice(5);
  const sum = (side: FightPlayer[], pick: (p: FightPlayer) => number): number => side.reduce((total, p) => total + pick(p), 0);
  const worthBlue = sum(blue, (p) => p.gold);
  const worthRed = sum(red, (p) => p.gold);
  const mean = (worthBlue + worthRed) / 2;
  const flag = (value: boolean): number => (value ? 1 : 0);
  return {
    dLevel: sum(blue, (p) => p.level) - sum(red, (p) => p.level),
    dGold: (worthBlue - worthRed) / 1000,
    dRelative: mean > 0 ? (10 * (worthBlue - worthRed)) / mean : 0,
    dCarry: (Math.max(...blue.map((p) => p.gold)) - Math.max(...red.map((p) => p.gold))) / 1000,
    baron: flag(f.blue.baron) - flag(f.red.baron),
    elder: flag(f.blue.elder) - flag(f.red.elder),
    dragons: f.blue.dragons - f.red.dragons,
    soul: flag(f.blue.soul) - flag(f.red.soul),
    atakhan: f.blue.atakhan - f.red.atakhan,
    phase: phaseOf(f.second),
  };
}

interface Variant {
  name: string;
  columns: string[];
  row: (f: Features) => number[];
}

const byPhase = (value: number, phase: number): number[] => [0, 1, 2].map((p) => (p === phase ? value : 0));
const perPhase = (name: string): string[] => PHASES.map((p) => `${name} (${p})`);
const BUFF_COLUMNS = ['Baron', 'Elder', 'Drache', 'Seele', 'Atakhan'];
const buffs = (f: Features): number[] => [f.baron, f.elder, f.dragons, f.soul, f.atakhan];

/** The first rung and its alternatives: the plain weights, per phase, relative to what the teams own. */
const BASES: Variant[] = [
  { name: 'Itemwert + Level', columns: ['Level', '1.000 Gold'], row: (f) => [f.dLevel, f.dGold] },
  { name: 'Itemwert + Level je Phase', columns: [...perPhase('Level'), ...perPhase('1.000 Gold')], row: (f) => [...byPhase(f.dLevel, f.phase), ...byPhase(f.dGold, f.phase)] },
  { name: 'Itemwert relativ + Level', columns: ['Level', '10 % Itemwert'], row: (f) => [f.dLevel, f.dRelative] },
  { name: 'Itemwert relativ + Level je Phase', columns: [...perPhase('Level'), ...perPhase('10 % Itemwert')], row: (f) => [...byPhase(f.dLevel, f.phase), ...byPhase(f.dRelative, f.phase)] },
];

const withBuffs = (v: Variant): Variant => ({ name: `${v.name} + Baron, Elder, Drachen, Seele, Atakhan`, columns: [...v.columns, ...BUFF_COLUMNS], row: (f) => [...v.row(f), ...buffs(f)] });
const withCarry = (v: Variant): Variant => ({ name: `${v.name} + wo das Gold liegt`, columns: [...v.columns, 'reichster Spieler, 1.000 Gold'], row: (f) => [...v.row(f), f.dCarry] });

/**
 * A rung needs this many standard errors of gain on the held-out games to replace the simpler one.
 * Any gain at all would let a factor that says nothing in by chance in about every third run.
 */
export const REQUIRED_ERRORS = 2;

/**
 * Fitted without a side: every fight also mirrored, as the scale knows no blue and red. The error
 * bars are widened by √2 for it (each fight counted twice).
 */
function fitMirrored(rows: readonly number[][], y: readonly number[]): LogisticFit {
  return fitLogistic(
    rows.flatMap((r) => [r, r.map((v) => -v)]),
    y.flatMap((v) => [v, 1 - v]),
  );
}

/** Log-odds without the intercept (which the mirroring keeps at 0). */
const linear = (fit: LogisticFit, row: readonly number[]): number => row.reduce((z, value, j) => z + fit.coefficients[j + 1]! * value, 0);

const num = (value: number, digits = 1): string => value.toFixed(digits).replace('.', ',');
const signed = (value: number, digits = 1): string => `${value >= 0 ? '+' : '−'}${num(Math.abs(value), digits)}`;
const pct = (value: number, digits = 1): string => `${num(value * 100, digits)} %`;
const count = (value: number): string => Math.round(value).toLocaleString('de-DE');
const sigmoid = (z: number): number => 1 / (1 + Math.exp(-z));

interface Champion {
  id: number;
  name: string;
}

export async function main(args: string[]): Promise<number> {
  const { positional, named } = options(args);
  const [gamesPath, metaPath] = positional;
  if (!gamesPath) {
    console.error('Aufruf: npm run measure:fights -- <games.json.gz> [meta.json] [--min-side 4] [--out bericht.md]');
    return 2;
  }
  const minSide = Number(named['min-side'] ?? 4);
  const file = readGamesFile(gamesPath);
  const names = new Map<number, string>(
    metaPath ? (JSON.parse(readFileSync(metaPath, 'utf8').replace(/^﻿/, '')) as { champions: Champion[] }).champions.map((c): [number, string] => [c.id, c.name]) : [],
  );

  // Each patch is its own list of lines; the index is offset so that games stay apart.
  const all: Fight[] = [];
  let sampled = 0;
  let lines = 0;
  for (const patch of file.patches) {
    const read = readFights(patch.games, lines);
    all.push(...read.fights);
    sampled += read.sampled;
    lines += patch.games.length;
  }

  const reasons = new Map<Exclusion | 'counted', number>();
  const fights = all.filter((f) => {
    const reason = exclusionOf(f, minSide);
    reasons.set(reason ?? 'counted', (reasons.get(reason ?? 'counted') ?? 0) + 1);
    return reason === null;
  });
  const games = new Set(fights.map((f) => f.game)).size;

  const report: string[] = [];
  report.push(
    '# Messung: die Teamfight-Skala',
    '',
    `Datei vom ${file.collectedAtUtc.slice(0, 16).replace('T', ' ')} UTC · ${file.tier} · ${file.platform} · ${file.patches.map((p) => `Patch ${p.patch}: ${count(p.games.length)} Zeilen`).join(', ')}.`,
    `${count(sampled)} Zeilen mit Teamfights (seit 22.11.2026), ${count(all.length)} Teamfights mit drei oder mehr je Team (${num(all.length / Math.max(1, sampled))} je Spiel).`,
    '',
    '## Welche Kämpfe zählen',
    '',
    '| | Kämpfe | Anteil |',
    '|---|---:|---:|',
    ...(
      [
        ['dead', 'ein Team ging nicht vollzählig hinein (Respawn-Zeit)'],
        ['small', `weniger als ${minSide} je Team gesehen`],
        ['afk', `ein Spieler ${AFK_LEVELS} oder mehr Level unter dem Median (AFK)`],
        ['draw', 'gleich viele Kills'],
        ['counted', '**gezählt**'],
      ] as const
    ).map(([key, label]) => `| ${label} | ${count(reasons.get(key) ?? 0)} | ${pct((reasons.get(key) ?? 0) / Math.max(1, all.length))} |`),
    '',
    `Gezählt: ${count(fights.length)} Kämpfe aus ${count(games)} Spielen; ${PHASES.map((p, i) => `${p} ${count(fights.filter((f) => phaseOf(f.second) === i).length)}`).join(', ')}. Ausschlüsse in dieser Reihenfolge geprüft. Vorhergesagt wird, welches Team mehr Kills im Kampf macht.`,
    '',
  );

  if (fights.length < 200) {
    report.push('**Zu wenige Kämpfe für eine Messung.**', '');
    return finish(report, named.out, fights.length);
  }

  // ── Plain facts first ────────────────────────────────────────────────────────────────────────
  {
    report.push('## Wer mehr Itemwert hat', '', '| Phase | Kämpfe | Team mit mehr Itemwert gewinnt | bei 2.000 Gold und mehr | ± |', '|---|---:|---:|---:|---:|');
    for (let phase = 0; phase <= PHASES.length; phase++) {
      const sub = fights.filter((f) => phase === PHASES.length || phaseOf(f.second) === phase);
      const ahead = sub.filter((f) => featuresOf(f).dGold !== 0);
      const won = ahead.filter((f) => featuresOf(f).dGold > 0 === (f.y === 1)).length;
      const clear = sub.filter((f) => Math.abs(featuresOf(f).dGold) >= 2);
      const wonClear = clear.filter((f) => featuresOf(f).dGold > 0 === (f.y === 1)).length;
      if (ahead.length === 0) continue;
      report.push(`| ${phase === PHASES.length ? 'alle' : PHASES[phase]} | ${count(sub.length)} | ${pct(won / ahead.length)} | ${clear.length > 0 ? pct(wonClear / clear.length) : '–'} | ${num(196 * Math.sqrt(0.25 / ahead.length))} |`);
    }
    report.push('');
  }

  // ── The ladder on held-out games ─────────────────────────────────────────────────────────────
  const train = fights.filter((f) => !isHeldOut(f.game));
  const held = fights.filter((f) => isHeldOut(f.game));
  const trainFeatures = train.map(featuresOf);
  const heldFeatures = held.map(featuresOf);
  const trainY = train.map((f) => f.y);

  interface Rung {
    variant: Variant;
    fit: LogisticFit;
    trainZ: number[];
    heldZ: number[];
  }
  const rungOf = (variant: Variant): Rung => {
    const fit = fitMirrored(trainFeatures.map(variant.row), trainY);
    return { variant, fit, trainZ: trainFeatures.map((f) => linear(fit, variant.row(f))), heldZ: heldFeatures.map((f) => linear(fit, variant.row(f))) };
  };
  const predictions = (z: readonly number[]): number[] => z.map(sigmoid);
  const versus = (a: readonly number[], b: readonly number[]) => compare(held, predictions(a), predictions(b));
  const better = (simpler: readonly number[], richer: readonly number[]): boolean => {
    const c = versus(simpler, richer);
    return c.error > 0 && c.gain / c.error >= REQUIRED_ERRORS;
  };

  const bases = BASES.map(rungOf);
  // The plain base, unless an alternative beats it clearly; among those that do, the best.
  const base = bases.slice(1).reduce((best, rung) => (better(bases[0]!.heldZ, rung.heldZ) && versus(best.heldZ, rung.heldZ).gain > 0 ? rung : best), bases[0]!);
  const buffed = rungOf(withBuffs(base.variant));
  const afterBuffs = better(base.heldZ, buffed.heldZ) ? buffed : base;
  const carried = rungOf(withCarry(afterBuffs.variant));
  const linearBest = better(afterBuffs.heldZ, carried.heldZ) ? carried : afterBuffs;

  // The champions' own strength per phase, drawn to 0, on top of the best linear model.
  const keys = new Keys();
  const keysOf = (f: Fight, create: boolean): { plus: number[]; minus: number[] } => {
    const phase = phaseOf(f.second);
    const key = (p: FightPlayer): number => keys.of(`${p.championId}:${phase}`, create);
    return cancel(
      f.players.slice(0, 5).map(key).filter((k) => k >= 0),
      f.players.slice(5).map(key).filter((k) => k >= 0),
    );
  };
  const trainRows: EffectRow[] = train.map((f, i) => ({ ...keysOf(f, true), offset: linearBest.trainZ[i]!, y: f.y }));
  const effects = chooseEffects(train, trainRows, keys.size);
  const heldRows: EffectRow[] = held.map((f, i) => ({ ...keysOf(f, false), offset: linearBest.heldZ[i]!, y: f.y }));
  const championZ = heldRows.map((row) => effectZ(row, effects.theta));
  const final = better(linearBest.heldZ, championZ) ? championZ : linearBest.heldZ;
  const finalName = final === championZ ? `${linearBest.variant.name} + Champion-Stärke je Phase` : linearBest.variant.name;

  report.push(
    '## Die Leiter auf zurückgehaltenen Spielen',
    '',
    `${count(train.length)} Kämpfe zum Anpassen, ${count(held.length)} aus zurückgehaltenen Spielen (2 von 10, dieselbe Teilung wie bei den Duellen). Log-Loss, kleiner ist besser; 0,6931 ist Münzwurf. „Gewinn“ gegen die genannte Stufe, in Standardfehlern aus ganzen Spielen (die Kämpfe eines Spiels hängen zusammen). Eine Stufe ersetzt die einfachere nur mit mindestens ${REQUIRED_ERRORS} Standardfehlern Gewinn.`,
    '',
    '| Stufe | Log-Loss | gegen | Gewinn | in Standardfehlern | Trefferquote |',
    '|---|---:|---|---:|---:|---:|',
  );
  const coin = held.map(() => 0);
  const hit = (z: readonly number[]): string => pct(held.filter((f, i) => (z[i]! > 0 ? 1 : 0) === f.y).length / Math.max(1, held.length));
  const line = (name: string, z: readonly number[], against: readonly number[] | null, againstName: string): void => {
    const own = versus(coin, z).b;
    const c = against ? versus(against, z) : null;
    report.push(`| ${name} | ${num(own, 4)} | ${c ? againstName : '–'} | ${c ? signed(c.gain, 4) : '–'} | ${c && c.error > 0 ? signed(c.gain / c.error, 1) : '–'} | ${hit(z)} |`);
  };
  line('Münzwurf', coin, null, '');
  line(bases[0]!.variant.name, bases[0]!.heldZ, coin, 'Münzwurf');
  for (const rung of bases.slice(1)) line(rung.variant.name, rung.heldZ, bases[0]!.heldZ, bases[0]!.variant.name);
  line(buffed.variant.name, buffed.heldZ, base.heldZ, base.variant.name);
  line(carried.variant.name, carried.heldZ, afterBuffs.heldZ, afterBuffs.variant.name);
  line(`${linearBest.variant.name} + Champion-Stärke je Phase`, championZ, linearBest.heldZ, linearBest.variant.name);
  report.push('', `**Gewählt: ${finalName}.**`, '');

  // ── The weights ──────────────────────────────────────────────────────────────────────────────
  report.push(
    '## Die Gewichte',
    '',
    'Log-Odds je Einheit, angepasst an den Kämpfen zum Anpassen, ohne Seite (jeder Kampf auch gespiegelt); ± ist das 95-%-Intervall, um √2 geweitet, ohne Rücksicht auf zusammenhängende Kämpfe (also etwas zu eng). Drache: je Drache mehr; Baron, Elder: wirkt bei einem Team; Seele, Atakhan: hat ein Team.',
    '',
  );
  for (const rung of [bases[0]!, base, buffed, carried].filter((r, i, list) => list.indexOf(r) === i)) {
    const cells = rung.variant.columns.map((column, j) => `| ${column} | ${signed(rung.fit.coefficients[j + 1]!, 3)} ± ${num(1.96 * Math.SQRT2 * rung.fit.standardErrors[j + 1]!, 3)} |`);
    report.push(`### ${rung.variant.name}`, '', '| Faktor | Gewicht |', '|---|---:|', ...cells, '');
  }
  {
    // Which side: the same plain model with an intercept, not mirrored.
    const sided = fitLogistic(trainFeatures.map(BASES[0]!.row), trainY);
    report.push(`Vorteil der blauen Seite bei Gleichstand: ${signed(sided.coefficients[0]!, 3)} ± ${num(1.96 * sided.standardErrors[0]!, 3)} Log-Odds — die Skala rechnet ohne Seite.`, '');
  }

  // ── The champions ────────────────────────────────────────────────────────────────────────────
  report.push(
    '## Champion-Stärke je Phase',
    '',
    `${count(keys.size)} Effekte (Champion × Phase), jeder zur 0 gezogen; die Streuung τ ist auf einem inneren Viertel der Anpass-Spiele gewählt. „keine“ heißt: Jede Stärke größer 0 sagt dort schlechter vorher als gar keine.`,
    '',
    `| Log-Loss ohne | ${TAUS.map((t) => `τ ${num(t, 2)}`).join(' | ')} | gewählt |`,
    `|---:|${TAUS.map(() => '---:').join('|')}|---|`,
    `| ${effects.inner.map((l) => num(l, 4)).join(' | ')} | ${effects.tau > 0 ? `τ ${num(effects.tau, 2)}` : 'keine'} |`,
    '',
  );
  if (effects.tau > 0) {
    const seen = new Map<number, number>();
    for (const row of trainRows) for (const k of [...row.plus, ...row.minus]) seen.set(k, (seen.get(k) ?? 0) + 1);
    const ranked = keys.names.map((name, k) => ({ name, value: effects.theta[k]!, n: seen.get(k) ?? 0 })).sort((a, b) => b.value - a.value);
    const show = (e: (typeof ranked)[number]): string => {
      const [id, phase] = e.name.split(':').map(Number) as [number, number];
      return `| ${names.get(id) ?? `#${id}`} | ${PHASES[phase]} | ${count(e.n)} | ${signed(e.value, 3)} |`;
    };
    report.push('| Champion | Phase | Kämpfe | Effekt |', '|---|---|---:|---:|', ...ranked.slice(0, 15).map(show), '| … | | | |', ...ranked.slice(-15).map(show), '');
  }

  // ── Does the scale keep its word, in every phase ─────────────────────────────────────────────
  report.push(
    '## Hält die Skala, was sie sagt?',
    '',
    `Zurückgehaltene Kämpfe, nach der vorhergesagten Chance des vorn gesehenen Teams. Modell: ${finalName}.`,
    '',
    '| Phase | vorhergesagt | Kämpfe | im Mittel vorhergesagt | so oft gewonnen | ± |',
    '|---|---|---:|---:|---:|---:|',
  );
  const EDGES = [0.5, 0.55, 0.6, 0.65, 0.7, 0.8, 0.9, 1.0001];
  for (let phase = 0; phase <= PHASES.length; phase++) {
    for (let b = 0; b + 1 < EDGES.length; b++) {
      let n = 0;
      let said = 0;
      let won = 0;
      held.forEach((f, i) => {
        if (phase < PHASES.length && phaseOf(f.second) !== phase) return;
        const p = sigmoid(final[i]!);
        const favourite = Math.max(p, 1 - p);
        if (favourite < EDGES[b]! || favourite >= EDGES[b + 1]!) return;
        n++;
        said += favourite;
        won += p >= 0.5 === (f.y === 1) ? 1 : 0;
      });
      if (n < 50) continue;
      report.push(`| ${phase === PHASES.length ? 'alle' : PHASES[phase]} | ${pct(EDGES[b]!, 0)} bis ${pct(Math.min(1, EDGES[b + 1]!), 0)} | ${count(n)} | ${pct(said / n)} | ${pct(won / n)} | ${num(196 * Math.sqrt(0.25 / n))} |`);
    }
  }
  report.push('');

  // As the tile would show it, with the lane scale's step of 0,6 log-odds, and how far it reaches.
  {
    report.push('### Stufen wie bei der Lane-Skala (ein Schritt = 0,6 Log-Odds)', '', '| Stufe des vorn gesehenen Teams | Kämpfe | im Mittel vorhergesagt | so oft gewonnen | ± |', '|---|---:|---:|---:|---:|');
    const labels = ['unter 0,5 (Ausgeglichen)', '0,5 bis 1,5 (Leicht stärker)', '1,5 bis 2,5 (Stärker)', 'ab 2,5 (Deutlich stärker)'];
    const bins = labels.map(() => ({ n: 0, said: 0, won: 0 }));
    held.forEach((f, i) => {
      const z = final[i]!;
      if (z === 0) return;
      const favourite = sigmoid(Math.abs(z));
      const bin = bins[Math.abs(z) / 0.6 < 0.5 ? 0 : Math.abs(z) / 0.6 < 1.5 ? 1 : Math.abs(z) / 0.6 < 2.5 ? 2 : 3]!;
      bin.n++;
      bin.said += favourite;
      bin.won += z > 0 === (f.y === 1) ? 1 : 0;
    });
    bins.forEach((bin, k) => {
      if (bin.n > 0) report.push(`| ${labels[k]} | ${count(bin.n)} | ${pct(bin.said / bin.n)} | ${pct(bin.won / bin.n)} | ${num(196 * Math.sqrt(0.25 / bin.n))} |`);
    });
    const spread = final.map(Math.abs).sort((a, b) => a - b);
    const at = (q: number): number => spread[Math.min(spread.length - 1, Math.floor(q * spread.length))] ?? 0;
    report.push(
      '',
      '| Anteil der Kämpfe | Abstand zur Mitte bis (Log-Odds) | als Chance |',
      '|---:|---:|---:|',
      ...[0.25, 0.5, 0.75, 0.9, 0.95, 0.99].map((q) => `| ${pct(q, 0)} | ${num(at(q), 2)} | ${pct(sigmoid(at(q)))} |`),
      '',
    );
  }

  return finish(report, named.out, fights.length);
}

function finish(report: string[], out: string | undefined, fights: number): number {
  const target = path.resolve(out ?? path.join('..', 'build', 'measure-fights.md'));
  mkdirSync(path.dirname(target), { recursive: true });
  writeFileSync(target, report.join('\n') + '\n');
  console.log(`${count(fights)} Teamfights gemessen → ${target}`);
  return 0;
}
