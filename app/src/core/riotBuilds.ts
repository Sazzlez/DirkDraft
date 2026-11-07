// DirkDraft's own build file (builds.json from the Riot collector, branch "data") read into one
// plan per champion and lane. Every slot takes the option with the best win rate that its games can
// vouch for. No matchup builds: the counts are per champion and lane, which is what "our build"
// means once the opponent is known too.
//
// "Can vouch for" is measured, not assumed (docs/modellpruefung.md, Messung 11). Each option's rate
// is pulled towards its slot's own rate with κ pseudo games, and κ comes from the file itself: how far
// the options of that slot truly spread beyond sampling noise (moment estimate over all rows). On the
// first own file (6.185 games) that spread was zero in every slot — no option measurably better than
// another — so the most played option leads; the Wilson bound used before picked an 8-game, 8-win
// rune page for Caitlyn over pages with hundreds of games. As the count grows and real differences
// show, the same rule starts to follow them.

import { ALL_LANES, Lane } from './model/lane';

export interface BuildCount {
  ids: number[];
  text?: string;
  games: number;
  wins: number;
}

export interface ChampionBuildStats {
  championId: number;
  lane: Lane;
  tier: string;
  games: number;
  wins: number;
  runes: BuildCount[];
  spells: BuildCount[];
  starters: BuildCount[];
  boots: BuildCount[];
  core: BuildCount[];
  fourth: BuildCount[];
  fifth: BuildCount[];
  sixth: BuildCount[];
  supportChoice: BuildCount[];
  skillOrder: BuildCount[];
  skillMax: BuildCount[];
}

export interface RiotBuildFile {
  version: number;
  patch: string;
  platform: string;
  collectedAtUtc: string;
  matches: Record<string, number>;
  builds: ChampionBuildStats[];
}

/** One option with its record, as the plan hands it on. */
export interface PlanOption {
  ids: number[];
  text: string | null;
  games: number;
  winRate: number;
}

export interface RiotBuildPlan {
  championId: number;
  lane: Lane;
  /** The lane asked for when this plan is the champion's main lane instead (too few games there). */
  inPlaceOf: Lane | null;
  patch: string;
  games: number;
  winRate: number;
  /** [primary style, 4 primary runes, secondary style, 2 secondary runes, 3 shards]. */
  runes: PlanOption | null;
  spells: PlanOption | null;
  starters: PlanOption[];
  boots: PlanOption[];
  core: PlanOption[];
  /** Fourth to sixth item, each the slot's best not already in the plan. */
  late: PlanOption[];
  supportChoice: PlanOption | null;
  /** "QEWQQRQEQERE…" (15 levels), empty when no order has games. */
  skillOrder: string;
  /** "QEW": which basic ability is maxed first, second, third. */
  skillMax: string;
}

/** Alternatives below this are not shown; the best option needs only one game. */
export const MINIMUM_ALTERNATIVE_GAMES = 20;

/**
 * A lane row with fewer games than this, or without a core, gives way to the champion's main lane
 * (with the hint) — on the first own file such rows had a median of 3 games and no core.
 */
export const MINIMUM_ROW_GAMES = 30;

type Slot = 'runes' | 'spells' | 'starters' | 'boots' | 'core' | 'fourth' | 'fifth' | 'sixth' | 'supportChoice' | 'skillOrder' | 'skillMax';
const SLOTS: readonly Slot[] = ['runes', 'spells', 'starters', 'boots', 'core', 'fourth', 'fifth', 'sixth', 'supportChoice', 'skillOrder', 'skillMax'];

const asOption = (count: BuildCount): PlanOption => ({
  ids: [...count.ids],
  text: count.text ?? null,
  games: count.games,
  winRate: count.games > 0 ? count.wins / count.games : 0,
});

/**
 * One key per build that plays the same: a spell pair in either key order is one pair (the import
 * puts Flash on the player's key anyway), and a rune page is one page whatever order its two
 * secondary runes were picked in. Item orders stay apart — the core is a build path.
 */
export function optionKey(slot: Slot, option: BuildCount): string {
  if (option.text && option.text.length > 0) return option.text;
  const ids = option.ids;
  if (slot === 'spells') return [...ids].sort((a, b) => a - b).join(',');
  if (slot === 'runes' && ids.length === 11) return [...ids.slice(0, 6), ...ids.slice(6, 8).sort((a, b) => a - b), ...ids.slice(8)].join(',');
  return ids.join(',');
}

/**
 * Equal builds summed under one key; the most played spelling keeps its ids (the order the client
 * gets). Runes only as complete pages of 11 ids.
 */
function combine(slot: Slot, options: readonly BuildCount[]): BuildCount[] {
  const map = new Map<string, { sum: BuildCount; best: number }>();
  for (const option of options) {
    if (slot === 'runes' && option.ids.length !== 11) continue;
    const key = optionKey(slot, option);
    const entry = map.get(key);
    if (!entry) {
      map.set(key, { sum: { ids: [...option.ids], text: option.text, games: option.games, wins: option.wins }, best: option.games });
      continue;
    }
    if (option.games > entry.best) {
      entry.sum.ids = [...option.ids];
      entry.best = option.games;
    }
    entry.sum.games += option.games;
    entry.sum.wins += option.wins;
  }
  return [...map.values()].map((e) => e.sum);
}

/** Options needed before a slot's spread counts as measured. */
const MINIMUM_SPREAD_OPTIONS = 100;

const weightCache = new WeakMap<RiotBuildFile, Record<Slot, number>>();

/**
 * κ per slot for one file: μ(1−μ)/τ² − 1 with τ² the options' spread around their slot's own rate
 * beyond the binomial noise, games-weighted over every row with 30+ games (options with 5+).
 * Infinity when the file shows no spread, or has too few options to tell (fewer than 100) — then
 * only the sample size tells options apart; a handful of lucky records must not set the weight.
 */
export function slotWeights(file: RiotBuildFile): Readonly<Record<Slot, number>> {
  const cached = weightCache.get(file);
  if (cached) return cached;
  const weights = {} as Record<Slot, number>;
  for (const slot of SLOTS) {
    let num = 0;
    let den = 0;
    let counted = 0;
    for (const row of file.builds) {
      const options = combine(slot, row[slot] ?? []);
      const total = options.reduce((n, o) => n + o.games, 0);
      if (total < MINIMUM_ROW_GAMES || options.length < 2) continue;
      const mu = options.reduce((n, o) => n + o.wins, 0) / total;
      for (const o of options) {
        if (o.games < 5) continue;
        num += o.games * ((o.wins / o.games - mu) ** 2 - (mu * (1 - mu)) / o.games);
        den += o.games;
        counted++;
      }
    }
    const tau2 = den > 0 ? num / den : 0;
    weights[slot] = counted >= MINIMUM_SPREAD_OPTIONS && tau2 > 1e-6 ? Math.max(0, 0.25 / tau2 - 1) : Number.POSITIVE_INFINITY;
  }
  weightCache.set(file, weights);
  return weights;
}

/** All tiers of one champion and lane, summed (the file may hold several brackets), equal builds as one. */
function merged(file: RiotBuildFile, championId: number, lane: Lane): ChampionBuildStats | null {
  const rows = file.builds.filter((b) => b.championId === championId && b.lane === lane);
  if (rows.length === 0) return null;
  const sum = (slot: Slot): BuildCount[] => combine(slot, rows.flatMap((row) => row[slot] ?? []));
  return {
    championId,
    lane,
    tier: rows.map((r) => r.tier).join('+'),
    games: rows.reduce((n, r) => n + r.games, 0),
    wins: rows.reduce((n, r) => n + r.wins, 0),
    runes: sum('runes'),
    spells: sum('spells'),
    starters: sum('starters'),
    boots: sum('boots'),
    core: sum('core'),
    fourth: sum('fourth'),
    fifth: sum('fifth'),
    sixth: sum('sixth'),
    supportChoice: sum('supportChoice'),
    skillOrder: sum('skillOrder'),
    skillMax: sum('skillMax'),
  };
}

/**
 * Best first: the rate pulled towards the slot's own rate with κ pseudo games; equal estimates
 * (always so at κ = ∞) by games, then by raw rate.
 */
function ranked(options: readonly BuildCount[], kappa: number): PlanOption[] {
  const games = options.reduce((n, o) => n + o.games, 0);
  const mu = games > 0 ? options.reduce((n, o) => n + o.wins, 0) / games : 0.5;
  const estimate = (o: BuildCount) => (Number.isFinite(kappa) ? (o.wins + kappa * mu) / (o.games + kappa) : mu);
  return options
    .filter((o) => o.games > 0)
    .sort((a, b) => estimate(b) - estimate(a) || b.games - a.games || b.wins / b.games - a.wins / a.games)
    .map(asOption);
}

/** The best option first, then the alternatives worth showing. */
function slot(options: readonly BuildCount[], kappa: number): PlanOption[] {
  return ranked(options, kappa).filter((option, index) => index === 0 || option.games >= MINIMUM_ALTERNATIVE_GAMES);
}

/** A row worth a plan of its own: enough games and a core. */
// A plain boolean on purpose: a type guard would make a thin row read as "no row" afterwards.
const usable = (stats: ChampionBuildStats | null): boolean => !!stats && stats.games >= MINIMUM_ROW_GAMES && stats.core.length > 0;

/**
 * The plan for a champion on a lane. Too few games there (or none), and the champion's main lane
 * stands in — a build is always shown rather than none (1.5 rule) — and the plan says which lane it
 * is from.
 */
export function riotPlan(file: RiotBuildFile, championId: number, lane: Lane): RiotBuildPlan | null {
  const asked = lane === Lane.Unknown ? null : merged(file, championId, lane);
  if (asked && usable(asked)) return planOf(file, championId, asked, null);

  // The main lane: the usable row with most games, else the row with most games at all.
  let best: ChampionBuildStats | null = null;
  for (const other of ALL_LANES) {
    const candidate = merged(file, championId, other);
    if (!candidate) continue;
    const better = !best || (usable(candidate) && !usable(best)) || (usable(candidate) === usable(best) && candidate.games > best.games);
    if (better) best = candidate;
  }
  // Without a usable row anywhere, the asked lane's own thin row is still better than another lane's.
  if (asked && (!best || !usable(best) || best.lane === asked.lane)) return planOf(file, championId, asked, null);
  if (!best) return null;
  return planOf(file, championId, best, lane === Lane.Unknown || best.lane === lane ? null : lane);
}

/** The ARAM plan: aram.json counts its builds without a lane (lane -1, tier "aram"). */
export function aramPlan(file: RiotBuildFile, championId: number): RiotBuildPlan | null {
  const stats = merged(file, championId, Lane.Unknown);
  return stats ? planOf(file, championId, stats, null) : null;
}

function planOf(file: RiotBuildFile, championId: number, stats: ChampionBuildStats, inPlaceOf: Lane | null): RiotBuildPlan {
  const k = slotWeights(file);
  const core = slot(stats.core, k.core);
  // Items four to six: each slot's best that is not already in the core or an earlier slot — players
  // buy the same item at different times, and the counts list it in several slots.
  const taken = new Set(core[0]?.ids ?? []);
  const late: PlanOption[] = [];
  for (const name of ['fourth', 'fifth', 'sixth'] as const) {
    const pick = ranked(stats[name], k[name]).find((o) => o.ids.length > 0 && o.ids.every((id) => !taken.has(id)));
    if (!pick) continue;
    pick.ids.forEach((id) => taken.add(id));
    late.push(pick);
  }

  return {
    championId,
    lane: stats.lane,
    inPlaceOf,
    patch: file.patch,
    games: stats.games,
    winRate: stats.games > 0 ? stats.wins / stats.games : 0,
    runes: ranked(stats.runes, k.runes)[0] ?? null,
    spells: ranked(stats.spells, k.spells)[0] ?? null,
    starters: slot(stats.starters, k.starters),
    boots: slot(stats.boots, k.boots),
    core,
    late,
    supportChoice: ranked(stats.supportChoice, k.supportChoice)[0] ?? null,
    skillOrder: ranked(stats.skillOrder, k.skillOrder)[0]?.text ?? '',
    skillMax: ranked(stats.skillMax, k.skillMax)[0]?.text ?? '',
  };
}

/** Stat shards by id, as the client names them. */
export const SHARD_NAMES: Readonly<Record<number, string>> = {
  5001: 'Leben (skalierend)',
  5005: 'Angriffstempo',
  5007: 'Fähigkeitstempo',
  5008: 'Adaptive Kraft',
  5010: 'Lauftempo',
  5011: 'Leben',
  5013: 'Zähigkeit und Verlangsamungsresistenz',
};
