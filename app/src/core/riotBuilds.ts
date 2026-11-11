// DirkDraft's own build file (builds.json from the Riot collector, branch "data") read into one
// plan per champion and lane. Every slot takes the option with the best win rate that its games can
// vouch for. No matchup builds: the counts are per champion and lane, which is what "our build"
// means once the opponent is known too.
//
// "Can vouch for" is measured, not assumed (docs/modellpruefung.md, Messung 11 and 16):
//
// 1. The most played option leads. Smoothing alone does not protect it: the most played option IS the
//    slot's own rate, the target everything is pulled to, so any option a hair above the slot's rate
//    outranked it however thin — on the file of 11.11.2026 Jinx got Boots of Swiftness from 35 games
//    over Berserker's Greaves from 11.994, and the chosen core held a median 14 % of its slot's games.
// 2. Another option takes the lead only when it is clearly the better one: two standard errors on
//    the smoothed rates (LEAD_SIGMA), a 98 % chance — and only in a slot whose options truly spread
//    (κ finite, below).
// 3. Boots and items are bought with the game already under way, and whoever is ahead buys
//    differently and wins more anyway. There the two standard errors also have to hold between
//    purchases made in the same game state (situational.json counts them so), for the items that
//    tell the two options apart; without those counts the most played option stays. An item bought
//    from ahead is never clear: Mejai's is bought 4,6 times as often ahead as behind (the next item
//    1,5 times), and "ahead" by the team's gold still mixes the fed with the carried — 89 % against
//    Zhonya's 71 % for Twisted Fate within "ahead".
//
// κ per slot comes from the file itself: how far the options of that slot truly spread beyond
// sampling noise (moment estimate over all rows). It decides whether a slot may follow win rates at
// all and orders the alternatives shown.

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
  /**
   * When the pair of boots was bought: finished items before it, as text "0" to "3" (three or more).
   * Since 11.11.2026; absent in older files.
   */
  bootsAfter?: BuildCount[];
  /**
   * When the first boots of any kind were bought: what the items held were worth going into that shop
   * visit, in steps of 500 gold as text ("0", "500", … "4000"). Since 11.11.2026.
   */
  bootsWorth?: BuildCount[];
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
  /**
   * The support quest's final item: the best first, then the alternatives worth showing (from
   * Bounty of Worlds the player picks one of five; the choice is the decision, not the gold).
   */
  supportOptions: PlanOption[];
  /** "QEWQQRQEQERE…" (15 levels), empty when no order has games. */
  skillOrder: string;
  /** "QEW": which basic ability is maxed first, second, third. */
  skillMax: string;
  /**
   * How many finished items most players of the champion buy before the pair of boots (0 to 3); null
   * where the file does not count it or the row is too thin.
   */
  bootsAfterItems: number | null;
  /**
   * What the items held are worth when half the players have bought their first boots — the lower
   * bound of that 500-gold step, the worth going into the shop visit; null as above.
   */
  bootsWorth: number | null;
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
 *
 * The noise an option shows around its slot's rate is μ(1−μ)·(1/g − 1/G), not μ(1−μ)/g: the slot's
 * rate is counted from the same games (G of them, the option's g included), so the option is closer
 * to it than to the true rate. Subtracting the full 1/g took about μ(1−μ) too much per row and read
 * a true spread of more than a point as none (simulated on the file of 11.11.2026 with every option
 * at its slot's rate: −1,1² to −1,7² points instead of 0, −3,6² in the thin sixth slot; runes,
 * spells, starters, fifth, sixth and the skill order stood at "no spread" while they spread by 0,7
 * to 2,5 points).
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
        num += o.games * ((o.wins / o.games - mu) ** 2 - mu * (1 - mu) * (1 / o.games - 1 / total));
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
    bootsAfter: steps(rows.flatMap((row) => row.bootsAfter ?? [])),
    bootsWorth: steps(rows.flatMap((row) => row.bootsWorth ?? [])),
  };
}

/** The timing steps of several brackets, summed by their text. */
function steps(options: readonly BuildCount[]): BuildCount[] {
  const map = new Map<string, BuildCount>();
  for (const option of options) {
    const key = option.text ?? '';
    if (!/^\d+$/.test(key) || !(option.games > 0)) continue;
    const entry = map.get(key);
    if (entry) {
      entry.games += option.games;
      entry.wins += option.wins;
    } else {
      map.set(key, { ids: [], text: key, games: option.games, wins: option.wins });
    }
  }
  return [...map.values()];
}

/** Games a timing needs before it is taken at its word, as a row needs for a plan of its own. */
export const MINIMUM_TIMING_GAMES = MINIMUM_ROW_GAMES;

/** The most counted step ("after one item"); the earlier one on a tie. Null below the minimum. */
function mostCounted(options: readonly BuildCount[] | undefined): number | null {
  const list = options ?? [];
  if (list.reduce((n, o) => n + o.games, 0) < MINIMUM_TIMING_GAMES) return null;
  let best: BuildCount | null = null;
  for (const o of list) if (!best || o.games > best.games || (o.games === best.games && Number(o.text) < Number(best.text))) best = o;
  return best ? Number(best.text) : null;
}

/** The step by which half the games are counted (the median), by its lower bound. Null below the minimum. */
function halfwayStep(options: readonly BuildCount[] | undefined): number | null {
  const list = [...(options ?? [])].sort((a, b) => Number(a.text) - Number(b.text));
  const games = list.reduce((n, o) => n + o.games, 0);
  if (games < MINIMUM_TIMING_GAMES) return null;
  let seen = 0;
  for (const o of list) {
    seen += o.games;
    if (seen * 2 >= games) return Number(o.text);
  }
  return null;
}

/**
 * How clearly the items of one option win more often than the items of another when both are
 * compared between purchases made in the same game state (even, ahead, behind), in standard errors.
 * `items` are those only the option has, `base` those only the most played one has. Null without the
 * counts, and for an item bought from ahead (Mejai's), whose win rate is the lead it was bought with.
 * Built from situational.json (core/situational sameStateEdge).
 */
export type SameStateCheck = (
  championId: number,
  lane: Lane,
  slot: 'boots' | 'late' | 'core',
  items: readonly number[],
  base: readonly number[],
) => number | null;

/** The slots bought with the game under way: an option there has to hold within the game state. */
const STATE_SLOT: Partial<Record<Slot, 'boots' | 'late' | 'core'>> = { boots: 'boots', core: 'core', fourth: 'late', fifth: 'late', sixth: 'late' };

/**
 * Standard errors before another option replaces the most played one: two, a 98 % chance that it is
 * truly the better one. Counted on the smoothed rates, so the chance holds however many options are
 * compared — that is what the smoothing is for. (Three standard errors on the raw rates let 13 games
 * with 12 wins through: with some 20.000 options in a file, chance alone clears that bar 27 times.)
 */
export const LEAD_SIGMA = 2;

/**
 * How clearly `a` is truly better than `b`, in standard errors: both rates pulled towards the slot's
 * own rate μ with κ pseudo games, and each known as well as its games plus κ allow — μ(1−μ)/(n+κ) is
 * what is left of the options' true spread once n games are counted. Zero when the slot shows no
 * spread (κ = ∞): then nothing can be clear.
 */
export function clearance(a: BuildCount, b: BuildCount, mu: number, kappa: number): number {
  if (!Number.isFinite(kappa) || a.games <= 0 || b.games <= 0 || !(mu > 0 && mu < 1)) return 0;
  const estimate = (o: BuildCount) => (o.wins + kappa * mu) / (o.games + kappa);
  const spread = Math.sqrt(mu * (1 - mu) * (1 / (a.games + kappa) + 1 / (b.games + kappa)));
  return (estimate(a) - estimate(b)) / spread;
}

interface Choice {
  slot: Slot;
  kappa: number;
  championId: number;
  lane: Lane;
  sameState: SameStateCheck | null;
}

/**
 * Best first. The most played option leads, unless another one is clearly better (header, 1 to 3);
 * the rest follow by their rate pulled towards the slot's own rate with κ pseudo games — equal
 * estimates (always so at κ = ∞) by games, then by raw rate.
 */
function ranked(options: readonly BuildCount[], choice: Choice): PlanOption[] {
  const played = options.filter((o) => o.games > 0);
  if (played.length === 0) return [];
  const games = played.reduce((n, o) => n + o.games, 0);
  const mu = played.reduce((n, o) => n + o.wins, 0) / games;
  const kappa = choice.kappa;
  const estimate = (o: BuildCount) => (Number.isFinite(kappa) ? (o.wins + kappa * mu) / (o.games + kappa) : mu);
  const byEstimate = (a: BuildCount, b: BuildCount) => estimate(b) - estimate(a) || b.games - a.games || b.wins / b.games - a.wins / a.games;

  const most = played.reduce((best, o) => (o.games > best.games ? o : best));
  const state = STATE_SLOT[choice.slot];
  const holdsInState = (o: BuildCount): boolean => {
    if (!state) return true;
    // Only what tells the two apart is compared; the same items in another order have no counts
    // by game state, and without counts nothing is clear.
    const items = o.ids.filter((id) => !most.ids.includes(id));
    const base = most.ids.filter((id) => !o.ids.includes(id));
    if (items.length === 0 || base.length === 0) return false;
    const edge = choice.sameState?.(choice.championId, choice.lane, state, items, base) ?? null;
    return edge !== null && edge >= LEAD_SIGMA;
  };
  const clear = played.filter((o) => o !== most && clearance(o, most, mu, kappa) >= LEAD_SIGMA && holdsInState(o)).sort(byEstimate);
  const lead = clear[0] ?? most;
  return [lead, ...played.filter((o) => o !== lead).sort(byEstimate)].map(asOption);
}

/** The best option first, then the alternatives worth showing. */
function slot(options: readonly BuildCount[], choice: Choice): PlanOption[] {
  return ranked(options, choice).filter((option, index) => index === 0 || option.games >= MINIMUM_ALTERNATIVE_GAMES);
}

/** A row worth a plan of its own: enough games and a core. */
// A plain boolean on purpose: a type guard would make a thin row read as "no row" afterwards.
const usable = (stats: ChampionBuildStats | null): boolean => !!stats && stats.games >= MINIMUM_ROW_GAMES && stats.core.length > 0;

/**
 * The plan for a champion on a lane. Too few games there (or none), and the champion's main lane
 * stands in — a build is always shown rather than none (1.5 rule) — and the plan says which lane it
 * is from. `sameState`: the comparison within the game state for boots and items four to six;
 * without it those slots keep their most played option.
 */
export function riotPlan(file: RiotBuildFile, championId: number, lane: Lane, sameState: SameStateCheck | null = null): RiotBuildPlan | null {
  const asked = lane === Lane.Unknown ? null : merged(file, championId, lane);
  if (asked && usable(asked)) return planOf(file, championId, asked, null, sameState);

  // The main lane: the usable row with most games, else the row with most games at all.
  let best: ChampionBuildStats | null = null;
  for (const other of ALL_LANES) {
    const candidate = merged(file, championId, other);
    if (!candidate) continue;
    const better = !best || (usable(candidate) && !usable(best)) || (usable(candidate) === usable(best) && candidate.games > best.games);
    if (better) best = candidate;
  }
  // Without a usable row anywhere, the asked lane's own thin row is still better than another lane's.
  if (asked && (!best || !usable(best) || best.lane === asked.lane)) return planOf(file, championId, asked, null, sameState);
  if (!best) return null;
  return planOf(file, championId, best, lane === Lane.Unknown || best.lane === lane ? null : lane, sameState);
}

/** The ARAM plan: aram.json counts its builds without a lane (lane -1, tier "aram"). */
export function aramPlan(file: RiotBuildFile, championId: number): RiotBuildPlan | null {
  const stats = merged(file, championId, Lane.Unknown);
  return stats ? planOf(file, championId, stats, null, null) : null;
}

function planOf(file: RiotBuildFile, championId: number, stats: ChampionBuildStats, inPlaceOf: Lane | null, sameState: SameStateCheck | null): RiotBuildPlan {
  const k = slotWeights(file);
  const choice = (name: Slot): Choice => ({ slot: name, kappa: k[name], championId, lane: stats.lane, sameState });
  const core = slot(stats.core, choice('core'));
  // Items four to six: each slot's best that is not already in the core or an earlier slot — players
  // buy the same item at different times, and the counts list it in several slots.
  const taken = new Set(core[0]?.ids ?? []);
  const late: PlanOption[] = [];
  for (const name of ['fourth', 'fifth', 'sixth'] as const) {
    const free = stats[name].filter((o) => o.ids.length > 0 && o.ids.every((id) => !taken.has(id)));
    const pick = ranked(free, choice(name))[0];
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
    runes: ranked(stats.runes, choice('runes'))[0] ?? null,
    spells: ranked(stats.spells, choice('spells'))[0] ?? null,
    starters: slot(stats.starters, choice('starters')),
    boots: slot(stats.boots, choice('boots')),
    core,
    late,
    supportChoice: ranked(stats.supportChoice, choice('supportChoice'))[0] ?? null,
    supportOptions: slot(stats.supportChoice, choice('supportChoice')),
    skillOrder: ranked(stats.skillOrder, choice('skillOrder'))[0]?.text ?? '',
    skillMax: ranked(stats.skillMax, choice('skillMax'))[0]?.text ?? '',
    bootsAfterItems: mostCounted(stats.bootsAfter),
    bootsWorth: halfwayStep(stats.bootsWorth),
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
