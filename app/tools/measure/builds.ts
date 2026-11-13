// Phase 2, the build side (docs/modellpruefung.md, Messung 16, 19 and 20): what the build rules of
// 11.11.2026 do on a file, and whether they hold on games the choice has not seen.
//
// - Messung 16: κ per slot, where the plan leaves the most played option, whether those departures
//   hold on fresh games (the counts of a later file minus those of the earlier one), and what a
//   higher price is worth inside a slot, with and without the game state.
// - Messung 19: when boots are bought (bootsAfter, bootsWorth) per lane and per champion-lane.
// - Messung 20: situational items in the build — how many take a place, and whether a situation made
//   of the enemies' items says more than "later in the game" (the lift there against the lift in the
//   other two such situations, which are just as late).
//
// It changes no rule; what it finds goes into docs/modellpruefung.md first.
//
//   npm run measure:builds -- <builds.json> <situational.json> <meta.json> <item.json>
//       [--before-builds alt.json --before-situational alt.json] [--out bericht.md]
//
// item.json is Data Dragon's (cdn/<version>/data/en_US/item.json), for names and prices.

import { mkdirSync, readFileSync, writeFileSync } from 'node:fs';
import path from 'node:path';
import { ALL_LANES, Lane } from '../../src/core/model/lane';
import { MetaLookup, type MetaSnapshot } from '../../src/core/model/metaLookup';
import { LEAD_SIGMA, optionKey, riotPlan, slotWeights, type BuildCount, type ChampionBuildStats, type RiotBuildFile, type RiotBuildPlan, type SameStateCheck } from '../../src/core/riotBuilds';
import {
  ITEM_SITUATIONS,
  MINIMUM_RATIO,
  SITUATION,
  SITUATION_TEXT,
  SLOT,
  SwapTracker,
  sameStateEdge,
  situationalBuild,
  type RiotSituationalFile,
  type Situation,
} from '../../src/core/situational';

const LANE_NAMES: Record<number, string> = { [Lane.Top]: 'Top', [Lane.Jungle]: 'Jungle', [Lane.Mid]: 'Mid', [Lane.Adc]: 'Bot', [Lane.Support]: 'Support' };

/** Champion-lanes below this are left out of the departures, as on 11.11.2026. */
const MINIMUM_ROW = 300;
/** And below this out of the situational count, as on 11.11.2026. */
const MINIMUM_SITUATIONAL_ROW = 2000;
/** Games an option needs before its win rate enters the price regression. */
const MINIMUM_PRICE_GAMES = 30;

type SlotName = 'runes' | 'spells' | 'starters' | 'boots' | 'core' | 'fourth' | 'fifth' | 'sixth' | 'supportChoice' | 'skillOrder' | 'skillMax';
const SLOT_LABEL: Record<SlotName, string> = {
  runes: 'Runen',
  spells: 'Zauber',
  starters: 'Start',
  boots: 'Schuhe',
  core: 'Kern',
  fourth: 'Item 4',
  fifth: 'Item 5',
  sixth: 'Item 6',
  supportChoice: 'Support-Item',
  skillOrder: 'Skill-Reihenfolge',
  skillMax: 'Skill-Priorität',
};
const SLOTS = Object.keys(SLOT_LABEL) as SlotName[];

const num = (value: number, digits = 1): string => value.toFixed(digits).replace('.', ',');
const signed = (value: number, digits = 1): string => `${value >= 0 ? '+' : '−'}${num(Math.abs(value), digits)}`;
const pct = (value: number, digits = 1): string => `${num(value * 100, digits)} %`;
const count = (value: number): string => Math.round(value).toLocaleString('de-DE');
const load = <T>(file: string): T => JSON.parse(readFileSync(file, 'utf8').replace(/^﻿/, '')) as T;

interface Item {
  name: string;
  total: number;
}

function readItems(file: string): Map<number, Item> {
  const raw = load<{ data: Record<string, { name: string; gold?: { total?: number } }> }>(file);
  return new Map(Object.entries(raw.data).map(([id, item]) => [Number(id), { name: item.name, total: item.gold?.total ?? 0 }]));
}

/** Equal builds summed under one key, as the plan does (riotBuilds combine). */
function combined(slot: SlotName, options: readonly BuildCount[] | undefined): Map<string, BuildCount> {
  const map = new Map<string, BuildCount>();
  for (const option of options ?? []) {
    if (slot === 'runes' && option.ids.length !== 11) continue;
    const key = optionKey(slot, option);
    const entry = map.get(key);
    if (entry) {
      entry.games += option.games;
      entry.wins += option.wins;
    } else {
      map.set(key, { ids: [...option.ids], text: option.text, games: option.games, wins: option.wins });
    }
  }
  return map;
}

const mostPlayed = (options: Iterable<BuildCount>): BuildCount | null => {
  let best: BuildCount | null = null;
  for (const o of options) if (o.games > 0 && (!best || o.games > best.games)) best = o;
  return best;
};

const stateCheck =
  (file: RiotSituationalFile): SameStateCheck =>
  (championId, lane, slot, items, base) =>
    sameStateEdge(file, championId, lane, slot === 'boots' ? SLOT.Boots : slot === 'core' ? SLOT.Core : SLOT.Late, items, base);

interface Departure {
  championId: number;
  lane: Lane;
  slot: SlotName;
  /** The plan's option and the most played one, by key and with their counts. */
  pickKey: string;
  mostKey: string;
  pick: BuildCount;
  most: BuildCount;
}

/** The plan of every champion-lane with enough games, and where it leaves the most played option. */
function departures(file: RiotBuildFile, situational: RiotSituationalFile | null): { rows: number; found: Departure[]; plans: Map<string, RiotBuildPlan> } {
  const check = situational ? stateCheck(situational) : null;
  const found: Departure[] = [];
  const plans = new Map<string, RiotBuildPlan>();
  let rows = 0;
  for (const row of file.builds) {
    if (row.games < MINIMUM_ROW || row.lane === Lane.Unknown) continue;
    const plan = riotPlan(file, row.championId, row.lane, check);
    if (!plan || plan.inPlaceOf !== null || plan.lane !== row.lane) continue;
    rows++;
    plans.set(`${row.championId}:${row.lane}`, plan);
    const note = (slot: SlotName, options: Map<string, BuildCount>, pickKey: string | null) => {
      const most = mostPlayed(options.values());
      if (!most || pickKey === null) return;
      const mostKey = optionKey(slot, most);
      const pick = options.get(pickKey);
      if (pick && pickKey !== mostKey) found.push({ championId: row.championId, lane: row.lane, slot, pickKey, mostKey, pick, most });
    };
    const keyOf = (slot: SlotName, option: { ids: number[]; text: string | null } | null | undefined): string | null =>
      option ? optionKey(slot, { ids: option.ids, text: option.text ?? undefined, games: 0, wins: 0 }) : null;

    note('runes', combined('runes', row.runes), keyOf('runes', plan.runes));
    note('spells', combined('spells', row.spells), keyOf('spells', plan.spells));
    note('starters', combined('starters', row.starters), keyOf('starters', plan.starters[0]));
    note('boots', combined('boots', row.boots), keyOf('boots', plan.boots[0]));
    note('core', combined('core', row.core), keyOf('core', plan.core[0]));
    note('supportChoice', combined('supportChoice', row.supportChoice), keyOf('supportChoice', plan.supportChoice));
    note('skillOrder', combined('skillOrder', row.skillOrder), plan.skillOrder || null);
    note('skillMax', combined('skillMax', row.skillMax), plan.skillMax || null);

    // Items four to six: the most played among what the earlier slots left free, as the plan picks.
    const taken = new Set(plan.core[0]?.ids ?? []);
    let at = 0;
    for (const slot of ['fourth', 'fifth', 'sixth'] as const) {
      const free = new Map([...combined(slot, row[slot])].filter(([, o]) => o.ids.length > 0 && o.ids.every((id) => !taken.has(id))));
      if (free.size === 0) continue;
      const pick = plan.late[at++];
      if (!pick) break;
      note(slot, free, keyOf(slot, pick));
      pick.ids.forEach((id) => taken.add(id));
    }
  }
  return { rows, found, plans };
}

/** κ as the true spread it stands for, in points at 50 %. */
const spreadOf = (kappa: number): string => (Number.isFinite(kappa) ? `${num(Math.sqrt(0.25 / (kappa + 1)) * 100)} Pkt` : '0');

interface Slope {
  /** Points of win rate per 1.000 gold, and its standard error. */
  slope: number;
  error: number;
  options: number;
  games: number;
}

/**
 * Win rate against price inside a group of options that compete for one place: each option's rate
 * against the group's own, on its price against the group's mean price, games-weighted and pooled
 * over all groups. The error is the binomial one of the rates.
 */
function priceSlope(groups: Iterable<readonly { price: number; games: number; wins: number }[]>): Slope {
  let sxy = 0;
  let sxx = 0;
  let noise = 0;
  let options = 0;
  let games = 0;
  for (const group of groups) {
    const usable = group.filter((o) => o.games >= MINIMUM_PRICE_GAMES && o.price > 0);
    if (usable.length < 2) continue;
    const total = usable.reduce((n, o) => n + o.games, 0);
    const mu = usable.reduce((n, o) => n + o.wins, 0) / total;
    const mean = usable.reduce((n, o) => n + o.games * o.price, 0) / total;
    for (const o of usable) {
      const x = (o.price - mean) / 1000;
      sxy += o.games * x * (o.wins / o.games - mu);
      sxx += o.games * x * x;
      noise += o.games * x * x * mu * (1 - mu);
      options++;
      games += o.games;
    }
  }
  return sxx > 0 ? { slope: (sxy / sxx) * 100, error: (Math.sqrt(noise) / sxx) * 100, options, games } : { slope: 0, error: Infinity, options, games };
}

const slopeText = (s: Slope): string => (Number.isFinite(s.error) ? `${signed(s.slope)} ± ${num(1.96 * s.error)} Pkt (${count(s.options)} Optionen)` : '—');

/** Counts of situational.json by owner, lane, slot, situation and item, summed over the game states or per state. */
class Buys {
  private readonly perState = new Map<string, { games: number; wins: number }>();
  private readonly summed = new Map<string, { games: number; wins: number }>();
  readonly items = new Map<string, Set<number>>();

  constructor(rows: readonly number[][]) {
    for (const row of rows) {
      if (row.length < 8) continue;
      const [owner, lane, slot, situation, state, item, games, wins] = row as [number, number, number, number, number, number, number, number];
      this.perState.set(`${owner}:${lane}:${slot}:${situation}:${state}:${item}`, { games, wins });
      const key = `${owner}:${lane}:${slot}:${situation}:${item}`;
      const entry = this.summed.get(key);
      if (entry) {
        entry.games += games;
        entry.wins += wins;
      } else {
        this.summed.set(key, { games, wins });
      }
      if (item !== 0) {
        const prefix = `${owner}:${lane}:${slot}`;
        let set = this.items.get(prefix);
        if (!set) this.items.set(prefix, (set = new Set()));
        set.add(item);
      }
    }
  }

  all(owner: number, lane: number, slot: number, situation: number, item: number): { games: number; wins: number } {
    return this.summed.get(`${owner}:${lane}:${slot}:${situation}:${item}`) ?? { games: 0, wins: 0 };
  }

  state(owner: number, lane: number, slot: number, situation: number, state: number, item: number): { games: number; wins: number } {
    return this.perState.get(`${owner}:${lane}:${slot}:${situation}:${state}:${item}`) ?? { games: 0, wins: 0 };
  }
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
  if (files.length < 4) {
    console.error('Aufruf: npm run measure:builds -- <builds.json> <situational.json> <meta.json> <item.json> [--before-builds alt.json --before-situational alt.json] [--out bericht.md]');
    return 2;
  }
  const builds = load<RiotBuildFile>(files[0]!);
  const situational = load<RiotSituationalFile>(files[1]!);
  const meta = new MetaLookup(load<MetaSnapshot>(files[2]!));
  const items = readItems(files[3]!);
  const beforeBuilds = options['before-builds'] ? load<RiotBuildFile>(options['before-builds']) : null;
  const beforeSituational = options['before-situational'] ? load<RiotSituationalFile>(options['before-situational']) : null;

  const gamesOfFile = (file: RiotBuildFile) => Object.values(file.matches).reduce((n, v) => n + v, 0);
  const itemName = (id: number) => items.get(id)?.name ?? `#${id}`;
  const optionName = (slot: SlotName, option: BuildCount): string =>
    option.text ? option.text : slot === 'runes' ? `Seite ${option.ids[1]}/${option.ids[5]}` : option.ids.map(itemName).join(', ');
  const who = (championId: number, lane: Lane) => `${meta.championName(championId)} ${LANE_NAMES[lane]}`;
  const rateOf = (o: { games: number; wins: number }) => (o.games > 0 ? o.wins / o.games : 0);
  const record = (o: { games: number; wins: number }) => `${count(o.games)} mit ${pct(rateOf(o), 0)}`;

  const report: string[] = [];
  report.push('# Build-Messung (Phase 2, Werkzeug tools/measure/builds.ts)', '');
  report.push(
    `Daten: builds.json und situational.json aus ${count(gamesOfFile(builds))} Spielen, Patch ${builds.patch}, gesammelt bis ${builds.collectedAtUtc.slice(0, 16).replace('T', ' ')} UTC.` +
      (beforeBuilds ? ` Zum Vergleich der Stand aus ${count(gamesOfFile(beforeBuilds))} Spielen (${beforeBuilds.collectedAtUtc.slice(0, 16).replace('T', ' ')} UTC).` : ''),
    '',
  );

  // ---- Messung 16 -------------------------------------------------------------------------------
  const now = departures(builds, situational);
  const weights = slotWeights(builds);
  const weightsBefore = beforeBuilds ? slotWeights(beforeBuilds) : null;
  report.push('## Messung 16: Build-Wahl', '', '### κ je Slot', '');
  report.push(`| Slot | κ${weightsBefore ? ' vorher | κ jetzt' : ''} | wahre Streuung | Plan ≠ meistgespielt (von ${count(now.rows)}) |`, weightsBefore ? '|---|---:|---:|---:|---:|' : '|---|---:|---:|---:|');
  for (const slot of SLOTS) {
    const k = weights[slot];
    const kappa = (value: number) => (Number.isFinite(value) ? count(value) : '∞');
    report.push(`| ${SLOT_LABEL[slot]} | ${weightsBefore ? `${kappa(weightsBefore[slot])} | ` : ''}${kappa(k)} | ${spreadOf(k)} | ${now.found.filter((d) => d.slot === slot).length} |`);
  }
  report.push('');

  const smoothed = (d: Departure, file: RiotBuildFile, row: ChampionBuildStats): string => {
    const options = [...combined(d.slot, row[d.slot]).values()];
    const total = options.reduce((n, o) => n + o.games, 0);
    const mu = total > 0 ? options.reduce((n, o) => n + o.wins, 0) / total : 0.5;
    const k = slotWeights(file)[d.slot];
    const estimate = (o: BuildCount) => (o.wins + k * mu) / (o.games + k);
    return Number.isFinite(k) ? signed((estimate(d.pick) - estimate(d.most)) * 100) : '—';
  };
  const price = (ids: readonly number[]) => ids.reduce((n, id) => n + (items.get(id)?.total ?? 0), 0);
  const isItemSlot = (slot: SlotName) => slot === 'boots' || slot === 'core' || slot === 'fourth' || slot === 'fifth' || slot === 'sixth';

  report.push('### Abweichungen vom Meistgespielten', '', '| Champion-Lane | Slot | Plan | meistgespielt | Vorsprung geglättet | Preis |', '|---|---|---|---|---:|---:|');
  for (const d of now.found) {
    const row = builds.builds.find((b) => b.championId === d.championId && b.lane === d.lane)!;
    const gold = isItemSlot(d.slot) ? `${signed(price(d.pick.ids) - price(d.most.ids), 0)}` : '—';
    report.push(`| ${who(d.championId, d.lane)} | ${SLOT_LABEL[d.slot]} | ${optionName(d.slot, d.pick)} (${record(d.pick)}) | ${optionName(d.slot, d.most)} (${record(d.most)}) | ${smoothed(d, builds, row)} | ${gold} |`);
  }
  report.push('', `${now.found.length} Abweichungen auf ${count(now.rows)} Champion-Lanes ab ${MINIMUM_ROW} Spielen.`, '');

  // Fresh games: the choice made on the earlier file, checked on the games counted since.
  if (beforeBuilds) {
    const then = departures(beforeBuilds, beforeSituational);
    const rowOf = (file: RiotBuildFile, d: Departure) => file.builds.find((b) => b.championId === d.championId && b.lane === d.lane);
    report.push(
      '### Halten die Abweichungen auf neuen Spielen?',
      '',
      `Gewählt auf dem älteren Stand (${count(gamesOfFile(beforeBuilds))} Spiele, ${then.found.length} Abweichungen auf ${count(then.rows)} Champion-Lanes), geprüft auf den Spielen, ` +
        `die seither dazukamen (${count(gamesOfFile(builds) - gamesOfFile(beforeBuilds))}): Zählung jetzt minus Zählung damals, je Option.`,
      '',
      '| Champion-Lane | Slot | Plan damals | Vorsprung damals (roh) | neue Spiele Plan | neue Spiele meistgespielt | Vorsprung neu | heute noch im Plan |',
      '|---|---|---|---:|---|---|---:|---|',
    );
    let weighted = 0;
    let weight = 0;
    let held = 0;
    let checked = 0;
    let claimed = 0;
    for (const d of then.found) {
      const row = rowOf(builds, d);
      const fresh = (key: string, old: BuildCount) => {
        const current = row ? combined(d.slot, row[d.slot]).get(key) : undefined;
        return current && current.games >= old.games ? { games: current.games - old.games, wins: current.wins - old.wins } : null;
      };
      const pick = fresh(d.pickKey, d.pick);
      const most = fresh(d.mostKey, d.most);
      const still = now.found.some((n) => n.championId === d.championId && n.lane === d.lane && n.slot === d.slot && n.pickKey === d.pickKey);
      const oldEdge = (rateOf(d.pick) - rateOf(d.most)) * 100;
      if (!pick || !most || pick.games < 1 || most.games < 1) {
        report.push(`| ${who(d.championId, d.lane)} | ${SLOT_LABEL[d.slot]} | ${optionName(d.slot, d.pick)} | ${signed(oldEdge)} | — | — | — | ${still ? 'ja' : 'nein'} |`);
        continue;
      }
      const edge = rateOf(pick) - rateOf(most);
      const pooled = (pick.wins + most.wins) / (pick.games + most.games);
      const variance = Math.max(1e-9, pooled * (1 - pooled)) * (1 / pick.games + 1 / most.games);
      weighted += edge / variance;
      weight += 1 / variance;
      claimed += (oldEdge / 100) / variance;
      checked++;
      if (edge > 0) held++;
      report.push(
        `| ${who(d.championId, d.lane)} | ${SLOT_LABEL[d.slot]} | ${optionName(d.slot, d.pick)} | ${signed(oldEdge)} | ${record(pick)} | ${record(most)} | ${signed(edge * 100)} ± ${num(1.96 * Math.sqrt(variance) * 100)} | ${still ? 'ja' : 'nein'} |`,
      );
    }
    if (weight > 0)
      report.push(
        '',
        `Zusammen (gewichtet nach Genauigkeit): Vorsprung auf den neuen Spielen ${signed((weighted / weight) * 100)} ± ${num((1.96 / Math.sqrt(weight)) * 100)} Punkte, ` +
          `auf dem alten Stand waren es bei gleicher Gewichtung ${signed((claimed / weight) * 100)} (roh). ${held} von ${checked} liegen auf den neuen Spielen vorn.`,
        '',
      );
  }

  // The price inside a slot, from the build file (whole options) and within the game state (single items).
  const bought = new Buys(situational.rows);
  const fromBuilds = (slot: SlotName) =>
    priceSlope(
      builds.builds
        .filter((row) => row.games >= MINIMUM_ROW && row.lane !== Lane.Unknown)
        .map((row) => [...combined(slot, row[slot]).values()].map((o) => ({ price: price(o.ids), games: o.games, wins: o.wins }))),
    );
  const rowsForState = builds.builds.filter((row) => row.games >= MINIMUM_ROW && row.lane !== Lane.Unknown);
  const fromState = (slot: number, perState: boolean) => {
    const groups: { price: number; games: number; wins: number }[][] = [];
    for (const row of rowsForState) {
      const ids = [...(bought.items.get(`${row.championId}:${row.lane}:${slot}`) ?? [])];
      if (perState) for (const state of [0, 1, 2]) groups.push(ids.map((id) => ({ price: items.get(id)?.total ?? 0, ...bought.state(row.championId, row.lane, slot, SITUATION.All, state, id) })));
      else groups.push(ids.map((id) => ({ price: items.get(id)?.total ?? 0, ...bought.all(row.championId, row.lane, slot, SITUATION.All, id) })));
    }
    return priceSlope(groups);
  };
  report.push(
    '### Der Preis als eigene Größe',
    '',
    `Winrate einer Option gegen ihren Preis, innerhalb des Slots einer Champion-Lane (ab ${MINIMUM_ROW} Spielen, Optionen ab ${MINIMUM_PRICE_GAMES}), ` +
      'in Punkten je 1.000 Gold mit 95-%-Fehlerbalken. Oben ganze Optionen aus builds.json, unten einzelne Items aus situational.json — einmal über alle Käufe, ' +
      'einmal nur zwischen Käufen im selben Spielstand (ausgeglichen, vorn, hinten).',
    '',
    '| Slot | je 1.000 Gold |',
    '|---|---|',
    ...(['boots', 'core', 'fourth', 'fifth', 'sixth'] as const).map((slot) => `| ${SLOT_LABEL[slot]} (builds.json) | ${slopeText(fromBuilds(slot))} |`),
    '',
    '| Platz (situational.json) | alle Käufe | im selben Spielstand |',
    '|---|---|---|',
    `| Schuhe | ${slopeText(fromState(SLOT.Boots, false))} | ${slopeText(fromState(SLOT.Boots, true))} |`,
    `| Kern (Items 1–3) | ${slopeText(fromState(SLOT.Core, false))} | ${slopeText(fromState(SLOT.Core, true))} |`,
    `| Items 4–6 | ${slopeText(fromState(SLOT.Late, false))} | ${slopeText(fromState(SLOT.Late, true))} |`,
    '',
  );

  // ---- Messung 19 -------------------------------------------------------------------------------
  report.push('## Messung 19: Kaufzeitpunkt der Schuhe', '');
  const timed = builds.builds.filter((row) => row.lane !== Lane.Unknown && (row.bootsAfter ?? []).reduce((n, o) => n + o.games, 0) >= 30);
  const steps = (list: readonly BuildCount[] | undefined) => new Map((list ?? []).map((o) => [Number(o.text), o.games]));
  const afterSteps = [0, 1, 2, 3];
  const worthSteps = [0, 500, 1000, 1500, 2000, 2500, 3000, 3500, 4000];
  const quantile = (counts: Map<number, number>, order: readonly number[], q: number): number => {
    const total = order.reduce((n, s) => n + (counts.get(s) ?? 0), 0);
    let seen = 0;
    for (const s of order) {
      seen += counts.get(s) ?? 0;
      if (seen >= q * total) return s;
    }
    return order[order.length - 1]!;
  };
  report.push(
    `${count(timed.length)} Champion-Lanes haben mindestens 30 gezählte Spiele mit Schuhen (${count(timed.reduce((n, r) => n + (r.bootsAfter ?? []).reduce((m, o) => m + o.games, 0), 0))} Spieler-Spiele).`,
    '',
    '### Fertige Items vor dem Paar (`bootsAfter`)',
    '',
    '| Lane | Spieler-Spiele | 0 | 1 | 2 | 3+ | Champion-Lanes | meistgezählt: 0 | 1 | 2 | 3+ |',
    '|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|',
  );
  const laneRows = (lane: Lane | null) => timed.filter((r) => lane === null || r.lane === lane);
  for (const lane of [...ALL_LANES, null]) {
    const rows = laneRows(lane);
    const sum = new Map<number, number>();
    const modes = new Map<number, number>();
    for (const row of rows) {
      for (const [step, games] of steps(row.bootsAfter)) sum.set(step, (sum.get(step) ?? 0) + games);
      const plan = now.plans.get(`${row.championId}:${row.lane}`) ?? riotPlan(builds, row.championId, row.lane, null);
      const mode = plan && plan.lane === row.lane ? plan.bootsAfterItems : null;
      if (mode !== null) modes.set(mode, (modes.get(mode) ?? 0) + 1);
    }
    const total = afterSteps.reduce((n, s) => n + (sum.get(s) ?? 0), 0);
    report.push(
      `| ${lane === null ? 'alle' : LANE_NAMES[lane]} | ${count(total)} | ${afterSteps.map((s) => pct((sum.get(s) ?? 0) / Math.max(1, total), 0)).join(' | ')} | ${rows.length} | ${afterSteps.map((s) => modes.get(s) ?? 0).join(' | ')} |`,
    );
  }
  report.push('', '### Itemwert beim Kauf der ersten Schuhe (`bootsWorth`)', '', '| Lane | nur Startitems | bis 1.000 | bis 2.000 | bis 3.000 | darüber | Viertel / Hälfte / drei Viertel der Spieler | Champion-Lanes: Hälfte bei 0 | 500–1.000 | 1.500–2.000 | ab 2.500 |', '|---|---:|---:|---:|---:|---:|---|---:|---:|---:|---:|');
  let wide = 0;
  let narrow = 0;
  for (const lane of [...ALL_LANES, null]) {
    const rows = laneRows(lane);
    const sum = new Map<number, number>();
    const halves = [0, 0, 0, 0];
    for (const row of rows) {
      const own = steps(row.bootsWorth);
      for (const [step, games] of own) sum.set(step, (sum.get(step) ?? 0) + games);
      const half = quantile(own, worthSteps, 0.5);
      halves[half === 0 ? 0 : half <= 1000 ? 1 : half <= 2000 ? 2 : 3]!++;
      if (lane === null) {
        // How sharp the habit is: the distance between the quarter and the three quarters of one champion-lane.
        if (quantile(own, worthSteps, 0.75) - quantile(own, worthSteps, 0.25) >= 1500) wide++;
        else narrow++;
      }
    }
    const total = worthSteps.reduce((n, s) => n + (sum.get(s) ?? 0), 0);
    const share = (from: number, to: number) => pct(worthSteps.filter((s) => s >= from && s <= to).reduce((n, s) => n + (sum.get(s) ?? 0), 0) / Math.max(1, total), 0);
    report.push(
      `| ${lane === null ? 'alle' : LANE_NAMES[lane]} | ${share(0, 0)} | ${share(500, 1000)} | ${share(1500, 2000)} | ${share(2500, 3000)} | ${share(3500, 4000)} | ` +
        `${count(quantile(sum, worthSteps, 0.25))} / ${count(quantile(sum, worthSteps, 0.5))} / ${count(quantile(sum, worthSteps, 0.75))} | ${halves.join(' | ')} |`,
    );
  }
  report.push(
    '',
    `Wie scharf ist die Gewohnheit je Champion-Lane? Zwischen dem Viertel und den drei Vierteln der Spieler liegen bei ${wide} von ${wide + narrow} Champion-Lanes ` +
      'mindestens 1.500 Gold Itemwert.',
    '',
  );

  // What players without boots do with the gold at hand (counted since 13.11.2026).
  const visits = new Map<string, { shop: number; bought: number }>();
  let visitRows = 0;
  for (const row of builds.builds) {
    if (row.lane === Lane.Unknown || !(row.bootsShop && row.bootsShop.length > 0)) continue;
    visitRows++;
    const add = (list: readonly BuildCount[] | undefined, field: 'shop' | 'bought') => {
      for (const o of list ?? []) {
        const [worth, spent] = (o.text ?? '').split('/');
        if (worth === undefined || spent === undefined) continue;
        for (const key of [`${row.lane}:${worth}:${spent}`, `all:${worth}:${spent}`, `${row.lane}:any:${spent}`, `all:any:${spent}`]) {
          const entry = visits.get(key) ?? { shop: 0, bought: 0 };
          entry[field] += o.games;
          visits.set(key, entry);
        }
      }
    };
    add(row.bootsShop, 'shop');
    add(row.bootsBought, 'bought');
  }
  report.push('### Was Spieler ohne Stiefel mit ihrem Gold kaufen (`bootsShop`, `bootsBought`)', '');
  if (visitRows === 0) {
    report.push('Die Datei zählt die Shop-Besuche noch nicht (der Sammler zählt sie ab dem ersten Lauf mit dem Stand vom 13.11.2026).', '');
  } else {
    const spends = ['300', '650', '1300'];
    const spendName: Record<string, string> = { '300': '300–649 Gold', '650': '650–1.299 Gold', '1300': 'ab 1.300 Gold' };
    const cell = (key: string) => {
      const v = visits.get(key);
      return v && v.shop >= 30 ? `${pct(v.bought / v.shop, 0)} (${count(v.shop)})` : v ? `— (${count(v.shop)})` : '—';
    };
    report.push(
      `${count(visitRows)} Champion-Lanes. Gezählt ist jeder Shop-Besuch nach dem Start, bei dem der Spieler noch keine Stiefel hatte und mindestens den Preis der Stiefel ausgegeben hat ` +
        '— Stiefel waren also möglich. Spieler mit „Magisches Schuhwerk“ zählen nicht. In den Zellen: Anteil der Besuche, in denen Stiefel gekauft wurden (Besuche in Klammern; unter 30 kein Anteil).',
      '',
      '**Je Lane und ausgegebenem Gold:**',
      '',
      `| Lane | ${spends.map((s) => spendName[s]).join(' | ')} |`,
      '|---|---:|---:|---:|',
      ...[...ALL_LANES, null].map((lane) => `| ${lane === null ? 'alle' : LANE_NAMES[lane]} | ${spends.map((s) => cell(`${lane ?? 'all'}:any:${s}`)).join(' | ')} |`),
      '',
      '**Je Itemwert beim Betreten des Shops (alle Lanes):**',
      '',
      `| Itemwert | ${spends.map((s) => spendName[s]).join(' | ')} |`,
      '|---|---:|---:|---:|',
      ...worthSteps.map((w) => `| ab ${count(w)} | ${spends.map((s) => cell(`all:${w}:${s}`)).join(' | ')} |`),
      '',
      'Lesart: Mit 300–649 Gold reicht es für Stiefel oder ein kleines Teil, nicht für beides — dort steht, was Spieler mit knappem Gold wählen. ' +
        'Die Stufe, ab der mehr als die Hälfte Stiefel kauft, ist die, ab der „Stiefel zuerst“ dem entspricht, was Spieler tun.',
      '',
    );
  }

  // ---- Messung 20 -------------------------------------------------------------------------------
  report.push('## Messung 20: Situative Items im Build', '');
  const situations = [SITUATION.Physical, SITUATION.Magic, SITUATION.Tanks, SITUATION.HardCc, SITUATION.EnemyArmor, SITUATION.EnemyMagicResist, SITUATION.EnemySustain] as const;
  const check = stateCheck(situational);
  const slotName = ['Schuhe', 'Items 4–6', 'Kern'];
  /** How many times as often an item is bought in a situation as outside it; null where either side is too thin. */
  const lift = (championId: number, lane: number, slot: number, situation: number, item: number): number | null => {
    const inTotal = bought.all(championId, lane, slot, situation, 0).games;
    const inItem = bought.all(championId, lane, slot, situation, item).games;
    const outTotal = bought.all(championId, lane, slot, SITUATION.All, 0).games - inTotal;
    const outItem = bought.all(championId, lane, slot, SITUATION.All, item).games - inItem;
    if (inTotal < 50 || outTotal < 50) return null;
    return ((inItem + 0.5) / (inTotal + 1)) / ((outItem + 0.5) / (outTotal + 1));
  };
  interface Taken {
    championId: number;
    lane: Lane;
    situation: Situation;
    slot: number;
    itemId: number;
    instead: number;
    own: number;
    others: (number | null)[];
  }
  const takeovers: Taken[] = [];
  const perSlot = [0, 1, 2].map(() => ({ suggestions: 0, adopted: 0 }));
  const perSituation = new Map<number, { suggestions: number; adopted: number }>();
  let lanes = 0;
  for (const row of builds.builds) {
    if (row.games < MINIMUM_SITUATIONAL_ROW || row.lane === Lane.Unknown) continue;
    const plan = riotPlan(builds, row.championId, row.lane, check);
    if (!plan || plan.lane !== row.lane || plan.inPlaceOf !== null) continue;
    lanes++;
    for (const situation of situations) {
      const result = situationalBuild(situational, meta, plan, row.championId, [SITUATION.All, situation], [], () => false, [SLOT.Boots, SLOT.Core, SLOT.Late], new SwapTracker(), 12);
      const tally = perSituation.get(situation) ?? { suggestions: 0, adopted: 0 };
      perSituation.set(situation, tally);
      for (const s of result.suggestions) {
        perSlot[s.slot]!.suggestions++;
        tally.suggestions++;
      }
      for (const swap of result.swaps) {
        perSlot[swap.slot]!.adopted++;
        tally.adopted++;
        const own = lift(row.championId, row.lane, swap.slot, situation, swap.itemId) ?? swap.timesMoreOften;
        const others = ITEM_SITUATIONS.filter((s) => s !== situation).map((s) => lift(row.championId, row.lane, swap.slot, s, swap.itemId));
        takeovers.push({ championId: row.championId, lane: row.lane, situation, slot: swap.slot, itemId: swap.itemId, instead: swap.instead, own, others });
      }
    }
  }
  report.push(
    `${count(lanes)} Champion-Lanes ab ${count(MINIMUM_SITUATIONAL_ROW)} Spielen, jede Lage einzeln, alle Plan-Items offen (die Obergrenze).`,
    '',
    '| Platz | Vorschläge | übernommen |',
    '|---|---:|---:|',
    ...[SLOT.Boots, SLOT.Core, SLOT.Late].map((slot) => `| ${slotName[slot]} | ${perSlot[slot]!.suggestions} | ${perSlot[slot]!.adopted} |`),
    '',
    '| Lage | Vorschläge | übernommen |',
    '|---|---:|---:|',
    ...situations.map((s) => `| ${SITUATION_TEXT[s]} | ${perSituation.get(s)?.suggestions ?? 0} | ${perSituation.get(s)?.adopted ?? 0} |`),
    '',
  );

  // Does a situation made of enemy items say more than "late"? The other two are just as late.
  const fromItems = takeovers.filter((t) => ITEM_SITUATIONS.includes(t.situation));
  const lateOf = (t: Taken): number | null => {
    const known = t.others.filter((v): v is number => v !== null);
    return known.length > 0 ? Math.exp(known.reduce((n, v) => n + Math.log(v), 0) / known.length) : null;
  };
  const specific = fromItems.filter((t) => {
    const late = lateOf(t);
    return late !== null && t.own / late >= MINIMUM_RATIO;
  });
  const sameSwap = new Map<string, Set<number>>();
  for (const t of fromItems) {
    const key = `${t.championId}:${t.lane}:${t.itemId}`;
    if (!sameSwap.has(key)) sameSwap.set(key, new Set());
    sameSwap.get(key)!.add(t.situation);
  }
  const repeated = [...sameSwap.values()].filter((set) => set.size >= 2).length;
  report.push(
    '### Lage oder nur „später im Spiel“?',
    '',
    'Die Spielzeilen tragen keine Käufe; die Spielzeit eines Kaufs steht in keiner Datei. Als Vergleich „zur selben Spielzeit ohne die Lage“ dienen die ' +
      'beiden anderen Lagen aus Gegner-Items: Sie entstehen genauso spät. Ein Item, das zur Lage gehört, wird in ihr deutlich öfter gekauft als in den beiden ' +
      `anderen (mindestens ${num(MINIMUM_RATIO)}-mal so oft wie deren Mittel, dieselbe Grenze wie für einen Vorschlag). Die Lagen überschneiden sich in späten ` +
      'Spielen; das drückt das Verhältnis, der Vergleich ist also eher zu streng als zu milde.',
    '',
    `- Übernahmen aus Lagen der Gegner-Items: ${fromItems.length}, aus Lagen der Aufstellung: ${takeovers.length - fromItems.length}.`,
    `- Davon gehören nach diesem Vergleich zur Lage: ${specific.length} von ${fromItems.length}.`,
    `- Dasselbe Item übernimmt bei derselben Champion-Lane in zwei oder drei dieser Lagen: ${repeated} von ${sameSwap.size} Items.`,
    '',
    '| Champion-Lane | Lage | Platz | Item | statt | in der Lage | in den anderen beiden | Verhältnis |',
    '|---|---|---|---|---|---:|---:|---:|',
    ...fromItems
      .sort((a, b) => a.own / (lateOf(a) ?? 1) - b.own / (lateOf(b) ?? 1))
      .map((t) => {
        const late = lateOf(t);
        return `| ${who(t.championId, t.lane)} | ${SITUATION_TEXT[t.situation]} | ${slotName[t.slot]} | ${itemName(t.itemId)} | ${itemName(t.instead)} | ${num(t.own)}× | ${late === null ? '—' : `${num(late)}×`} | ${late === null ? '—' : num(t.own / late)} |`;
      }),
    '',
  );

  // The same over every item, not only the takeovers: do the three item situations lift the same items?
  const correlation = (a: Situation, b: Situation): { r: number; n: number } => {
    const xs: number[] = [];
    const ys: number[] = [];
    const ws: number[] = [];
    for (const row of builds.builds) {
      if (row.games < MINIMUM_SITUATIONAL_ROW || row.lane === Lane.Unknown) continue;
      for (const slot of [SLOT.Core, SLOT.Late]) {
        for (const item of bought.items.get(`${row.championId}:${row.lane}:${slot}`) ?? []) {
          const buys = bought.all(row.championId, row.lane, slot, SITUATION.All, item).games;
          if (buys < 100) continue;
          const la = lift(row.championId, row.lane, slot, a, item);
          const lb = lift(row.championId, row.lane, slot, b, item);
          if (la === null || lb === null) continue;
          xs.push(Math.log(la));
          ys.push(Math.log(lb));
          ws.push(buys);
        }
      }
    }
    const w = ws.reduce((n, v) => n + v, 0);
    if (w === 0) return { r: 0, n: 0 };
    const mx = xs.reduce((n, v, i) => n + v * ws[i]!, 0) / w;
    const my = ys.reduce((n, v, i) => n + v * ws[i]!, 0) / w;
    let sxy = 0;
    let sxx = 0;
    let syy = 0;
    xs.forEach((x, i) => {
      sxy += ws[i]! * (x - mx) * (ys[i]! - my);
      sxx += ws[i]! * (x - mx) ** 2;
      syy += ws[i]! * (ys[i]! - my) ** 2;
    });
    return { r: sxx > 0 && syy > 0 ? sxy / Math.sqrt(sxx * syy) : 0, n: xs.length };
  };
  const pairs: [Situation, Situation][] = [
    [SITUATION.EnemyArmor, SITUATION.EnemyMagicResist],
    [SITUATION.EnemyArmor, SITUATION.EnemySustain],
    [SITUATION.EnemyMagicResist, SITUATION.EnemySustain],
    [SITUATION.Physical, SITUATION.Magic],
    [SITUATION.Tanks, SITUATION.HardCc],
    [SITUATION.Physical, SITUATION.EnemyArmor],
  ];
  report.push(
    '### Heben die Lagen dieselben Items?',
    '',
    'Über alle Items der Plätze Kern und Items 4–6 mit mindestens 100 Käufen des Champions: Wie eng hängt zusammen, wie viel öfter ein Item in der einen und ' +
      'in der anderen Lage gekauft wird (Korrelation der logarithmierten Verhältnisse, gewichtet nach Käufen)? Nahe 1 heißt: Beide Lagen heben dieselben Items, ' +
      'messen also dasselbe.',
    '',
    '| Lage A | Lage B | Korrelation | Items |',
    '|---|---|---:|---:|',
    ...pairs.map(([a, b]) => {
      const c = correlation(a, b);
      return `| ${SITUATION_TEXT[a]} | ${SITUATION_TEXT[b]} | ${signed(c.r, 2)} | ${count(c.n)} |`;
    }),
    '',
  );

  report.push(`Grenzen: Abweichung nur mit ${LEAD_SIGMA} Standardfehlern (LEAD_SIGMA); Champion-Lanes ab ${MINIMUM_ROW} bzw. ${count(MINIMUM_SITUATIONAL_ROW)} Spielen.`, '');

  const out = path.resolve(options.out ?? path.join('..', 'build', 'measure-builds.md'));
  mkdirSync(path.dirname(out), { recursive: true });
  writeFileSync(out, report.join('\n') + '\n');
  console.log(`${count(gamesOfFile(builds))} Spiele gemessen → ${out}`);
  return 0;
}
