// Phase 4 of docs/draft-konzept.md (§5), the points that need only what the collector already
// counts (docs/modellpruefung.md, Messung 22 to 25):
//
// - Point 7, the check on the model: do players whose champion the model would have ranked high in
//   their draft really win more often? Rates from the training games, checked on held-out games.
// - Point 5, the enemies' lanes: how often does the lane prediction (LanePredictor, as the app uses
//   it for the enemy team) hit the lanes Riot recorded — for a full team and while it is revealed?
// - Point 1, experience: what the mastery points of the sampled player (every fourth game, the
//   player on the game's rarest champion) say about the win beyond the champion's lane strength.
// - Point 3, loadout by matchup: is a keystone, secondary tree, spell or starting item taken clearly
//   more often against one lane opponent than against the others — and does that hold on the games
//   counted since an earlier file?
//
// - Point 2, looking ahead: what would it be worth to count the lane opponents still open — the
//   expected duel against the champions the enemy can still take, by pick rate — once the worst
//   counters are banned or taken? Riot gives no pick order, so this cannot be checked on outcomes;
//   measured is only how far the number could move.
//
// It changes nothing in the model; what it finds goes into docs/modellpruefung.md first.
//
//   npm run measure:phase4 -- <games.json.gz> <meta.json> <builds.json> <matchups.json.gz>
//       [--before-matchups alt.json.gz] [--sample 2000] [--out bericht.md]
//       [--skip-check 1]  leave out point 7 (most of the running time)

import { mkdirSync, readFileSync, writeFileSync } from 'node:fs';
import { gunzipSync } from 'node:zlib';
import path from 'node:path';
import { CompAnalyzer } from '../../src/core/model/compAnalyzer';
import { draftParts, type DraftSeat } from '../../src/core/model/draftModel';
import type { DraftSlot } from '../../src/core/model/draftState';
import { ALL_LANES, Lane } from '../../src/core/model/lane';
import { LanePredictor, LOW_CONFIDENCE } from '../../src/core/model/lanePredictor';
import { MetaLookup, type MetaSnapshot } from '../../src/core/model/metaLookup';
import { logit } from '../../src/core/model/scoreModel';
import { TankShred } from '../../src/core/model/tankShred';
import { TraitTable } from '../../src/core/model/traits';
import type { RiotBuildFile } from '../../src/core/riotBuilds';
import { gamesOf, isHeldOut, readGamesFile, type Game, type Seat } from './games';
import { fitLogistic } from './logistic';
import { tally } from './tally';

const LANE_NAMES: Record<number, string> = { [Lane.Top]: 'Top', [Lane.Jungle]: 'Jungle', [Lane.Mid]: 'Mid', [Lane.Adc]: 'Bot', [Lane.Support]: 'Support' };

const num = (value: number, digits = 1): string => value.toFixed(digits).replace('.', ',');
const signed = (value: number, digits = 1): string => `${value >= 0 ? '+' : '−'}${num(Math.abs(value), digits)}`;
const pct = (value: number, digits = 1): string => `${num(value * 100, digits)} %`;
const count = (value: number): string => Math.round(value).toLocaleString('de-DE');
const load = <T>(file: string): T => JSON.parse(readFileSync(file, 'utf8').replace(/^﻿/, '')) as T;
const loadGz = <T>(file: string): T => JSON.parse(gunzipSync(readFileSync(file)).toString('utf8')) as T;

/** A deterministic shuffle key, so every run samples the same games. */
const mix = (value: number): number => Math.imul(value + 1, 2654435761) >>> 0;

interface MatchupFile {
  patch: string;
  collectedAtUtc: string;
  /** [champion, lane, lane opponent, kind (0 keystone, 1 secondary tree, 2 spell beside Flash, 3 starting item), value, games, wins] */
  rows: number[][];
}

const KINDS = ['Keystone', 'Nebenbaum', 'Zauber neben Blitz', 'Startitem'];

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

// ---- Point 7 ---------------------------------------------------------------------------------------

function checkOnModel(report: string[], games: readonly Game[], base: MetaSnapshot, builds: RiotBuildFile | null, sample: number): void {
  const trainIndex = games.map((_, i) => i).filter((i) => !isHeldOut(i));
  const testIndex = games.map((_, i) => i).filter((i) => isHeldOut(i));
  const meta = new MetaLookup(tally(trainIndex.map((i) => games[i]!), base).snapshot);
  const analyzer = new CompAnalyzer(new MetaLookup(base), TraitTable.bundled(), TankShred.fromBuilds(builds));

  const bands = [0.1, 0.25, 0.5, 0.75, 1.0001];
  const labels = ['oberstes Zehntel', 'bis zum obersten Viertel', 'obere Hälfte darunter', 'untere Hälfte, oberes Viertel', 'unterstes Viertel'];
  const rows = bands.map(() => ({ seats: 0, wins: 0, value: 0 }));
  const perLane = ALL_LANES.map(() => ({ top: { seats: 0, wins: 0 }, bottom: { seats: 0, wins: 0 } }));
  const picked = testIndex.slice(0, Math.max(0, sample));

  for (const i of picked) {
    const game = games[i]!;
    const taken = new Set([...game.blue, ...game.red].map((s) => s.championId));
    for (const side of ['blue', 'red'] as const) {
      const mine = game[side];
      const theirs = side === 'blue' ? game.red : game.blue;
      const won = side === 'blue' ? game.blueWins === 1 : game.blueWins === 0;
      const enemyProfile = analyzer.analyze(theirs.map((s) => s.championId));
      mine.forEach((seat, index) => {
        if (seat.lane === Lane.Unknown || !meta.laneStat(seat.championId, seat.lane)) return;
        const valueOf = (championId: number): number => {
          const team: DraftSeat[] = mine.map((s, k) => (k === index ? { championId, lane: seat.lane } : s));
          return draftParts(meta, team, theirs, { allies: analyzer.analyze(team.map((s) => s.championId)), enemies: enemyProfile }).total;
        };
        const actual = valueOf(seat.championId);
        let better = 0;
        let candidates = 0;
        for (const candidate of meta.roster(seat.lane)) {
          if (candidate !== seat.championId && taken.has(candidate)) continue;
          if (!meta.laneStat(candidate, seat.lane)) continue;
          candidates++;
          if (candidate !== seat.championId && valueOf(candidate) > actual) better++;
        }
        if (candidates < 10) return;
        const rank = better / candidates;
        const band = bands.findIndex((limit) => rank < limit);
        const row = rows[band]!;
        row.seats++;
        row.value += actual;
        if (won) row.wins++;
        const lane = perLane[seat.lane]!;
        const half = rank < 0.25 ? lane.top : rank >= 0.75 ? lane.bottom : null;
        if (half) {
          half.seats++;
          if (won) half.wins++;
        }
      });
    }
  }

  const error = (wins: number, seats: number) => (seats > 0 ? 1.96 * Math.sqrt(((wins / seats) * (1 - wins / seats)) / seats) : 0);
  report.push(
    '## Messung 22: Erfolgskontrolle (Konzept §5, Punkt 7)',
    '',
    `Für ${count(picked.length)} zurückgehaltene Spiele (Raten nur aus den ${count(trainIndex.length)} Trainingsspielen): jeder Platz mit jedem freien Champion seiner Lane ` +
      'besetzt, der Rest des Drafts bleibt — genau die Rechnung der Pick-Liste (`draftModel.ts`), mit allen zehn Champions bekannt. Wo stand der Champion, der ' +
      'wirklich gespielt wurde, in dieser Liste, und wie oft hat sein Team gewonnen?',
    '',
    '| Platz in der Liste des Modells | Spieler | gewonnen | Chance des Teams laut Modell |',
    '|---|---:|---|---:|',
    ...rows.map((row, k) => `| ${labels[k]} | ${count(row.seats)} | ${row.seats ? `${pct(row.wins / row.seats)} ± ${num(error(row.wins, row.seats) * 100)}` : '—'} | ${row.seats ? pct(1 / (1 + Math.exp(-row.value / row.seats))) : '—'} |`),
    '',
    'Die Chance des Teams gilt für den ganzen Draft, nicht nur für diesen Platz; sie steht daneben, weil ein Spieler mit gutem Pick im Mittel auch in einem besseren Draft sitzt.',
    '',
    '| Lane | oberstes Viertel: Spieler | gewonnen | unterstes Viertel: Spieler | gewonnen | Unterschied |',
    '|---|---:|---:|---:|---:|---:|',
    ...ALL_LANES.map((lane) => {
      const { top, bottom } = perLane[lane]!;
      const diff = top.seats && bottom.seats ? top.wins / top.seats - bottom.wins / bottom.seats : 0;
      const spread = Math.sqrt((error(top.wins, top.seats) / 1.96) ** 2 + (error(bottom.wins, bottom.seats) / 1.96) ** 2) * 1.96;
      return `| ${LANE_NAMES[lane]} | ${count(top.seats)} | ${top.seats ? pct(top.wins / top.seats) : '—'} | ${count(bottom.seats)} | ${bottom.seats ? pct(bottom.wins / bottom.seats) : '—'} | ${signed(diff * 100)} ± ${num(spread * 100)} Pkt |`;
    }),
    '',
  );
}

// ---- Point 5 ---------------------------------------------------------------------------------------

function checkLanes(report: string[], games: readonly Game[], base: MetaSnapshot): void {
  const meta = new MetaLookup(base);
  const predictor = new LanePredictor(meta);
  const slots = (seats: readonly Seat[]): DraftSlot[] => seats.map((s, k) => ({ cellId: k + 5, index: k, isAlly: false, lockedChampionId: s.championId, hoverChampionId: 0, assignedLane: Lane.Unknown }));
  const complete = (seats: readonly Seat[]) => seats.every((s) => s.lane !== Lane.Unknown) && new Set(seats.map((s) => s.lane)).size === 5;

  const byKnown = [1, 2, 3, 4, 5].map(() => ({ seats: 0, right: 0, sure: 0, sureRight: 0, unsure: 0, unsureRight: 0 }));
  let teams = 0;
  let teamsRight = 0;
  const wrong = new Map<string, number>();
  const perLane = ALL_LANES.map(() => ({ seats: 0, right: 0 }));

  games.forEach((game, g) => {
    for (const [seats, salt] of [
      [game.blue, 0],
      [game.red, 1],
    ] as const) {
      if (!complete(seats)) continue;
      // The order the team is revealed in: Riot gives none, so a fixed shuffle per team.
      const order = seats.map((_, k) => k).sort((a, b) => mix(g * 10 + salt * 5 + a) - mix(g * 10 + salt * 5 + b));
      for (let known = 1; known <= 5; known++) {
        const shown = order.slice(0, known).map((k) => seats[k]!);
        const result = predictor.predict(slots(shown));
        const tallyRow = byKnown[known - 1]!;
        let allRight = true;
        shown.forEach((seat, k) => {
          const prediction = result.forCell(k + 5);
          const right = prediction?.lane === seat.lane;
          tallyRow.seats++;
          if (right) tallyRow.right++;
          else allRight = false;
          const sure = (prediction?.confidence ?? 0) >= LOW_CONFIDENCE;
          if (sure) {
            tallyRow.sure++;
            if (right) tallyRow.sureRight++;
          } else {
            tallyRow.unsure++;
            if (right) tallyRow.unsureRight++;
          }
          if (known === 5) {
            perLane[seat.lane]!.seats++;
            if (right) perLane[seat.lane]!.right++;
            else {
              const key = `${meta.championName(seat.championId)}: gespielt ${LANE_NAMES[seat.lane]}, vorhergesagt ${LANE_NAMES[prediction?.lane ?? -1] ?? 'keine'}`;
              wrong.set(key, (wrong.get(key) ?? 0) + 1);
            }
          }
        });
        if (known === 5) {
          teams++;
          if (allRight) teamsRight++;
        }
      }
    }
  });

  const worst = [...wrong.entries()].sort((a, b) => b[1] - a[1]).slice(0, 12);
  const wrongTotal = [...wrong.values()].reduce((n, v) => n + v, 0);
  report.push(
    '## Messung 23: Lane-Zuordnung der Gegner (Konzept §5, Punkt 5)',
    '',
    `${count(teams)} Teams mit fünf verschiedenen Lanes in Riots Spieldaten. Die App ordnet Gegner ohne Lane-Angabe mit dem LanePredictor zu (Rollenraten aus \`meta.json\`, ` +
      'je Lane ein Champion); hier dieselbe Rechnung, erst mit einem aufgedeckten Champion, dann mit zwei, bis zum ganzen Team. Die Reihenfolge des Aufdeckens ist zufällig, ' +
      'weil Riot die Pick-Reihenfolge nicht herausgibt; die Platz-Priors der App (`pick_order_priors.json`) zählen deshalb hier nicht mit.',
    '',
    `| aufgedeckt | Lane richtig | als sicher gezeigt (ab ${pct(LOW_CONFIDENCE, 0)}) | davon richtig | als unsicher gezeigt | davon richtig |`,
    '|---:|---:|---:|---:|---:|---:|',
    ...byKnown.map((row, k) => `| ${k + 1} | ${pct(row.right / Math.max(1, row.seats))} | ${pct(row.sure / Math.max(1, row.seats))} | ${row.sure ? pct(row.sureRight / row.sure) : '—'} | ${pct(row.unsure / Math.max(1, row.seats))} | ${row.unsure ? pct(row.unsureRight / row.unsure) : '—'} |`),
    '',
    `Ganzes Team: alle fünf Lanes richtig bei ${pct(teamsRight / Math.max(1, teams))} der Teams. Je Lane: ${ALL_LANES.map((lane) => `${LANE_NAMES[lane]} ${pct(perLane[lane]!.right / Math.max(1, perLane[lane]!.seats))}`).join(' · ')}.`,
    '',
    `Die häufigsten Fehler im ganzen Team (von ${count(wrongTotal)} falschen Plätzen):`,
    '',
    ...worst.map(([key, n]) => `- ${key}: ${count(n)}`),
    '',
  );
}

// ---- Point 1 ---------------------------------------------------------------------------------------

function checkMastery(report: string[], gameLines: readonly number[][], base: MetaSnapshot): void {
  const meta = new MetaLookup(base);
  interface Sample {
    championId: number;
    lane: Lane;
    points: number;
    won: number;
    strength: number;
  }
  const samples: Sample[] = [];
  for (const line of gameLines) {
    if (line.length < 15) continue;
    const place = line[13]!;
    const points = line[14]!;
    if (place < 0 || place > 9 || !(points > 0)) continue;
    const value = line[place]!;
    const championId = Math.floor(value / 8);
    const lane = ((value % 8) - 1) as Lane;
    const stat = lane === Lane.Unknown ? null : meta.laneStat(championId, lane);
    if (!stat) continue;
    const blueWins = line[10] === 1;
    samples.push({ championId, lane, points, won: (place < 5) === blueWins ? 1 : 0, strength: logit(stat.winRate) });
  }
  if (samples.length < 500) {
    report.push('## Messung 24: Erfahrung auf dem Champion (Konzept §5, Punkt 1)', '', `Zu wenige Stichproben: ${count(samples.length)}.`, '');
    return;
  }

  const logs = samples.map((s) => Math.log10(s.points));
  const meanLog = logs.reduce((n, v) => n + v, 0) / logs.length;
  const sorted = [...samples].sort((a, b) => a.points - b.points);
  const median = sorted[sorted.length >> 1]!.points;
  const fit = fitLogistic(samples.map((s, k) => [s.strength, logs[k]! - meanLog]), samples.map((s) => s.won));
  const beta = fit.coefficients[2]!;
  const betaError = fit.standardErrors[2]!;

  const limits = [10_000, 30_000, 100_000, 300_000, Infinity];
  const names = ['unter 10.000', '10.000–30.000', '30.000–100.000', '100.000–300.000', 'ab 300.000'];
  const bands = limits.map(() => ({ n: 0, wins: 0, expected: 0 }));
  for (const s of samples) {
    const band = bands[limits.findIndex((limit) => s.points < limit)]!;
    band.n++;
    band.wins += s.won;
    band.expected += 1 / (1 + Math.exp(-s.strength));
  }

  // Per champion-lane: how experienced its sampled players are, and what that is worth by the fit.
  const byChampion = new Map<string, { championId: number; lane: Lane; logs: number[]; wins: number }>();
  samples.forEach((s, k) => {
    const key = `${s.championId}:${s.lane}`;
    let entry = byChampion.get(key);
    if (!entry) byChampion.set(key, (entry = { championId: s.championId, lane: s.lane, logs: [], wins: 0 }));
    entry.logs.push(logs[k]!);
    entry.wins += s.won;
  });
  const champions = [...byChampion.values()]
    .filter((c) => c.logs.length >= 100)
    .map((c) => {
      const ordered = [...c.logs].sort((a, b) => a - b);
      const medianLog = ordered[ordered.length >> 1]!;
      return { ...c, medianPoints: 10 ** medianLog, worth: beta * (medianLog - Math.log10(median)) * 25 };
    })
    .sort((a, b) => b.medianPoints - a.medianPoints);
  const worths = champions.map((c) => c.worth).sort((a, b) => a - b);

  report.push(
    '## Messung 24: Erfahrung auf dem Champion (Konzept §5, Punkt 1)',
    '',
    `${count(samples.length)} Stichproben: in jedem vierten Spiel die Mastery-Punkte eines Spielers auf seinem Champion, und zwar des Spielers mit dem seltensten Champion des Spiels. ` +
      `Median ${count(median)} Punkte. Die Stichprobe ist mit Absicht schief: Sie trifft seltene Champions, populäre fast nie.`,
    '',
    '| Mastery-Punkte | Spieler | gewonnen | nach Lane-Stärke des Champions erwartet | Unterschied |',
    '|---|---:|---:|---:|---:|',
    ...bands.map((band, k) => {
      const rate = band.n ? band.wins / band.n : 0;
      const expected = band.n ? band.expected / band.n : 0;
      const error = band.n ? 1.96 * Math.sqrt((rate * (1 - rate)) / band.n) : 0;
      return `| ${names[k]} | ${count(band.n)} | ${pct(rate)} | ${pct(expected)} | ${signed((rate - expected) * 100)} ± ${num(error * 100)} Pkt |`;
    }),
    '',
    `Angepasst (Sieg auf Lane-Stärke und Mastery): je Verzehnfachung der Mastery-Punkte ${signed(beta * 25)} ± ${num(1.96 * betaError * 25)} Punkte Winrate ` +
      `(log-odds ${signed(beta, 3)} ± ${num(1.96 * betaError, 3)}); die Lane-Stärke zählt daneben mit ${num(fit.coefficients[1]!, 2)} ± ${num(1.96 * fit.standardErrors[1]!, 2)}.`,
    '',
    `Wie erfahren die Spieler je Champion-Lane sind (ab 100 Stichproben, ${champions.length} Champion-Lanes) und was das nach der Anpassung an Winrate ausmacht, ` +
      `gegenüber dem Median aller Stichproben: von ${signed(worths[0] ?? 0)} bis ${signed(worths[worths.length - 1] ?? 0)} Punkte, die mittlere Hälfte zwischen ` +
      `${signed(worths[Math.floor(worths.length / 4)] ?? 0)} und ${signed(worths[Math.floor((worths.length * 3) / 4)] ?? 0)}.`,
    '',
    '| Champion-Lane | Stichproben | Median Mastery | Erfahrungs-Anteil an der Winrate |',
    '|---|---:|---:|---:|',
    ...[...champions.slice(0, 8), ...champions.slice(-4)].map((c) => `| ${meta.championName(c.championId)} ${LANE_NAMES[c.lane]} | ${count(c.logs.length)} | ${count(c.medianPoints)} | ${signed(c.worth)} Pkt |`),
    '',
  );
}

// ---- Point 3 ---------------------------------------------------------------------------------------

interface Choice {
  championId: number;
  lane: number;
  opponentId: number;
  kind: number;
  value: number;
  shareIn: number;
  shareOut: number;
  z: number;
  games: number;
}

class Loadouts {
  /** champion:lane:opponent:kind → games against that opponent (sum over the values of the kind is per pick, not per game). */
  readonly rows = new Map<string, number>();
  /** champion:lane:kind:value → games against everyone. */
  readonly totals = new Map<string, number>();
  /** champion:lane:opponent → games of the pair (the keystone rows sum to it: one keystone a game). */
  readonly pairs = new Map<string, number>();
  readonly all = new Map<string, number>();

  constructor(file: MatchupFile) {
    for (const row of file.rows) {
      if (row.length < 7) continue;
      const [champion, lane, opponent, kind, value, games] = row as [number, number, number, number, number, number, number];
      this.rows.set(`${champion}:${lane}:${opponent}:${kind}:${value}`, games);
      const total = `${champion}:${lane}:${kind}:${value}`;
      this.totals.set(total, (this.totals.get(total) ?? 0) + games);
      if (kind === 0) {
        const pair = `${champion}:${lane}:${opponent}`;
        this.pairs.set(pair, (this.pairs.get(pair) ?? 0) + games);
        const all = `${champion}:${lane}`;
        this.all.set(all, (this.all.get(all) ?? 0) + games);
      }
    }
  }

  /** How often the value is taken against the opponent and against everyone else, with the pair's games. */
  shares(championId: number, lane: number, opponentId: number, kind: number, value: number): { shareIn: number; shareOut: number; inGames: number; outGames: number } | null {
    const inGames = this.pairs.get(`${championId}:${lane}:${opponentId}`) ?? 0;
    const outGames = (this.all.get(`${championId}:${lane}`) ?? 0) - inGames;
    if (inGames <= 0 || outGames <= 0) return null;
    const taken = this.rows.get(`${championId}:${lane}:${opponentId}:${kind}:${value}`) ?? 0;
    const elsewhere = (this.totals.get(`${championId}:${lane}:${kind}:${value}`) ?? 0) - taken;
    return { shareIn: taken / inGames, shareOut: elsewhere / outGames, inGames, outGames };
  }
}

/** A choice needs this many games of the pair, this share against the opponent, this ratio and this many standard errors. */
const LOADOUT_MINIMUM_GAMES = 50;
const LOADOUT_MINIMUM_SHARE = 0.15;
const LOADOUT_MINIMUM_RATIO = 1.5;
const LOADOUT_MINIMUM_Z = 3;

function clearChoices(file: MatchupFile): { found: Choice[]; tested: number } {
  const loadouts = new Loadouts(file);
  const found: Choice[] = [];
  let tested = 0;
  for (const [key, taken] of loadouts.rows) {
    const [championId, lane, opponentId, kind, value] = key.split(':').map(Number) as [number, number, number, number, number];
    const s = loadouts.shares(championId, lane, opponentId, kind, value);
    if (!s || s.inGames < LOADOUT_MINIMUM_GAMES || taken < 5) continue;
    tested++;
    const pooled = (s.shareIn * s.inGames + s.shareOut * s.outGames) / (s.inGames + s.outGames);
    const error = Math.sqrt(Math.max(1e-9, pooled * (1 - pooled)) * (1 / s.inGames + 1 / s.outGames));
    const z = (s.shareIn - s.shareOut) / error;
    if (s.shareIn >= LOADOUT_MINIMUM_SHARE && s.shareIn >= LOADOUT_MINIMUM_RATIO * s.shareOut && z >= LOADOUT_MINIMUM_Z)
      found.push({ championId, lane, opponentId, kind, value, shareIn: s.shareIn, shareOut: s.shareOut, z, games: s.inGames });
  }
  return { found, tested };
}

function checkLoadouts(report: string[], file: MatchupFile, before: MatchupFile | null, base: MetaSnapshot): void {
  const meta = new MetaLookup(base);
  const now = clearChoices(file);
  const pairs = new Set(now.found.map((c) => `${c.championId}:${c.lane}:${c.opponentId}`));
  const expectedByChance = now.tested * 0.00135;
  report.push(
    '## Messung 25: Runen, Zauber und Start passend zum Matchup (Konzept §5, Punkt 3)',
    '',
    `\`matchups.json.gz\` vom ${file.collectedAtUtc.slice(0, 16).replace('T', ' ')} UTC: je Champion, Lane und Lane-Gegner, wie oft welcher Keystone, Nebenbaum, Zauber neben Blitz und ` +
      'welches Startitem genommen wurde. „Deutlich öfter gegen diesen Gegner“ heißt hier: das Matchup hat mindestens ' +
      `${LOADOUT_MINIMUM_GAMES} Spiele, die Wahl mindestens ${pct(LOADOUT_MINIMUM_SHARE, 0)} Anteil gegen ihn, mindestens ${num(LOADOUT_MINIMUM_RATIO)}-mal so viel wie gegen alle anderen, ` +
      `und ${LOADOUT_MINIMUM_Z} Standardfehler Abstand.`,
    '',
    `Geprüft: ${count(now.tested)} Wahlen in Matchups ab ${LOADOUT_MINIMUM_GAMES} Spielen. Gefunden: **${count(now.found.length)}** in ${count(pairs.size)} Matchups ` +
      `(allein durch Zufall wären bei ${LOADOUT_MINIMUM_Z} Standardfehlern rund ${num(expectedByChance, 0)} zu erwarten, vor den anderen Bedingungen).`,
    '',
    '| Art | gefunden |',
    '|---|---:|',
    ...KINDS.map((name, kind) => `| ${name} | ${count(now.found.filter((c) => c.kind === kind).length)} |`),
    '',
  );

  if (before) {
    const then = clearChoices(before);
    const fresh = new Loadouts(file);
    const old = new Loadouts(before);
    let n = 0;
    let weight = 0;
    let oldLift = 0;
    let newLift = 0;
    let stillClear = 0;
    const perKind = KINDS.map(() => ({ n: 0, weight: 0, oldLift: 0, newLift: 0 }));
    for (const c of then.found) {
      const a = fresh.shares(c.championId, c.lane, c.opponentId, c.kind, c.value);
      const b = old.shares(c.championId, c.lane, c.opponentId, c.kind, c.value);
      if (!a || !b) continue;
      // The games counted since: now minus then, on both sides of the comparison.
      const inGames = a.inGames - b.inGames;
      const outGames = a.outGames - b.outGames;
      if (inGames < 5 || outGames < 5) continue;
      const shareIn = (a.shareIn * a.inGames - b.shareIn * b.inGames) / inGames;
      const shareOut = (a.shareOut * a.outGames - b.shareOut * b.outGames) / outGames;
      n++;
      weight += inGames;
      oldLift += inGames * (c.shareIn - c.shareOut);
      newLift += inGames * (shareIn - shareOut);
      if (shareIn >= LOADOUT_MINIMUM_RATIO * shareOut) stillClear++;
      const k = perKind[c.kind]!;
      k.n++;
      k.weight += inGames;
      k.oldLift += inGames * (c.shareIn - c.shareOut);
      k.newLift += inGames * (shareIn - shareOut);
    }
    report.push(
      '### Auf neuen Spielen',
      '',
      `Gefunden auf dem Stand vom ${before.collectedAtUtc.slice(0, 16).replace('T', ' ')} UTC (${count(then.found.length)} Wahlen), geprüft auf den Spielen, die seither dazukamen ` +
        '(Zählung jetzt minus Zählung damals, auf beiden Seiten des Vergleichs).',
      '',
      '| Art | Wahlen | Anteil gegen den Gegner minus Anteil sonst: damals | auf neuen Spielen | davon wieder |',
      '|---|---:|---:|---:|---:|',
      ...perKind.map((k, kind) =>
        k.weight > 0 ? `| ${KINDS[kind]} | ${count(k.n)} | ${signed((k.oldLift / k.weight) * 100)} Pkt | ${signed((k.newLift / k.weight) * 100)} Pkt | ${pct(k.newLift / k.oldLift, 0)} |` : `| ${KINDS[kind]} | 0 | — | — | — |`,
      ),
      weight > 0 ? `| alle | ${count(n)} | ${signed((oldLift / weight) * 100)} Pkt | ${signed((newLift / weight) * 100)} Pkt | ${pct(newLift / oldLift, 0)} |` : '| alle | 0 | — | — | — |',
      '',
      `Auf den neuen Spielen allein erfüllen ${count(stillClear)} von ${count(n)} wieder das Verhältnis von ${num(LOADOUT_MINIMUM_RATIO)}.`,
      '',
    );
  }

  const top = [...now.found].sort((a, b) => b.z - a.z).slice(0, 15);
  report.push(
    '### Die deutlichsten Fälle',
    '',
    '| Champion-Lane | gegen | Art | Wert (Riot-ID) | gegen ihn | sonst | Spiele |',
    '|---|---|---|---:|---:|---:|---:|',
    ...top.map((c) => `| ${meta.championName(c.championId)} ${LANE_NAMES[c.lane]} | ${meta.championName(c.opponentId)} | ${KINDS[c.kind]} | ${c.value} | ${pct(c.shareIn, 0)} | ${pct(c.shareOut, 0)} | ${count(c.games)} |`),
    '',
  );
}

// ---- Point 2 ---------------------------------------------------------------------------------------

function checkLookAhead(report: string[], base: MetaSnapshot): void {
  const meta = new MetaLookup(base);
  const shifts: { name: string; lane: Lane; shift: number; open: number }[] = [];
  const means: number[] = [];
  for (const lane of ALL_LANES) {
    const roster = meta.roster(lane);
    for (const championId of roster) {
      if (!meta.laneStat(championId, lane)) continue;
      // The duel edge against every lane opponent with a row, weighted by how often he is picked there.
      const edges: { edge: number; weight: number }[] = [];
      for (const opponentId of roster) {
        if (opponentId === championId) continue;
        const view = meta.matchup(championId, opponentId, lane);
        const stat = meta.laneStat(opponentId, lane);
        if (!view || !stat || !(stat.pickRate > 0)) continue;
        edges.push({ edge: logit(view.winRate) - meta.matchupBaseline(championId, opponentId, lane), weight: stat.pickRate });
      }
      if (edges.length < 10) continue;
      const expected = (list: typeof edges) => {
        const weight = list.reduce((n, e) => n + e.weight, 0);
        return weight > 0 ? list.reduce((n, e) => n + e.weight * e.edge, 0) / weight : 0;
      };
      const all = expected(edges);
      // The three worst counters gone (banned or picked elsewhere): the best case for the champion.
      const rest = [...edges].sort((a, b) => a.edge - b.edge).slice(3);
      means.push(all * 25);
      shifts.push({ name: meta.championName(championId), lane, shift: (expected(rest) - all) * 25, open: edges.length });
    }
  }
  const sorted = shifts.map((s) => s.shift).sort((a, b) => a - b);
  const at = (q: number) => sorted[Math.min(sorted.length - 1, Math.floor(q * sorted.length))] ?? 0;
  const spread = Math.sqrt(means.reduce((n, v) => n + v * v, 0) / Math.max(1, means.length));
  const top = [...shifts].sort((a, b) => b.shift - a.shift).slice(0, 5);
  report.push(
    '## Messung 26: Vorausschau im Draft (Konzept §5, Punkt 2)',
    '',
    'Prüfen lässt sich die Vorausschau nicht: Die Spieldaten kennen weder Pick-Reihenfolge noch Banns. Messbar ist, wie weit sie die Zahl höchstens bewegen könnte. ' +
      'Für jede Champion-Lane das erwartete Duell gegen alle Lane-Gegner, gewichtet nach Pickrate, und dasselbe, wenn die drei schlimmsten Counter gebannt oder vergeben sind ' +
      '— der günstigste Fall.',
    '',
    `- ${count(shifts.length)} Champion-Lanes. Das erwartete Duell gegen das ganze Feld liegt im Mittel ${num(spread, 2)} Punkte neben null (es steckt in der Lane-Stärke).`,
    `- Ohne die drei schlimmsten Counter steigt die Zahl im Median um ${num(at(0.5), 2)} Punkte, bei 95 % um höchstens ${num(at(0.95), 2)}, im größten Fall um ${num(sorted[sorted.length - 1] ?? 0, 2)}.`,
    `- Die größten: ${top.map((s) => `${s.name} ${LANE_NAMES[s.lane]} ${signed(s.shift, 2)}`).join(', ')}.`,
    '',
  );
}

export async function main(argv: string[]): Promise<number> {
  const { files, options } = args(argv);
  if (files.length < 4) {
    console.error('Aufruf: npm run measure:phase4 -- <games.json.gz> <meta.json> <builds.json> <matchups.json.gz> [--before-matchups alt.json.gz] [--sample 2000] [--out bericht.md]');
    return 2;
  }
  const started = Date.now();
  const gamesFile = readGamesFile(files[0]!);
  const base = load<MetaSnapshot>(files[1]!);
  const builds = load<RiotBuildFile>(files[2]!);
  const matchups = loadGz<MatchupFile>(files[3]!);
  const before = options['before-matchups'] ? loadGz<MatchupFile>(options['before-matchups']) : null;
  const games = gamesOf(gamesFile, base.patch);
  const lines = gamesFile.patches.find((p) => p.patch === base.patch)?.games ?? [];

  const report: string[] = [];
  report.push('# Phase 4 (Werkzeug tools/measure/phase4.ts)', '', `Daten: ${count(games.length)} Spiele, Patch ${base.patch}, gesammelt bis ${gamesFile.collectedAtUtc.slice(0, 16).replace('T', ' ')} UTC.`, '');
  if (!options['skip-check']) checkOnModel(report, games, base, builds, Number(options.sample ?? 2000));
  checkLanes(report, games, base);
  checkMastery(report, lines, base);
  checkLoadouts(report, matchups, before, base);
  checkLookAhead(report, base);
  report.push(`Rechenzeit: ${Math.round((Date.now() - started) / 1000)} s.`);

  const out = path.resolve(options.out ?? path.join('..', 'build', 'measure-phase4.md'));
  mkdirSync(path.dirname(out), { recursive: true });
  writeFileSync(out, report.join('\n') + '\n');
  console.log(`${count(games.length)} Spiele gemessen → ${out}`);
  return 0;
}
