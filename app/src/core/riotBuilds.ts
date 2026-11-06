// DirkDraft's own build file (builds.json from the Riot collector, branch "data") read into one
// plan per champion and lane. Every slot takes the option with the best win rate that its games can
// vouch for — the Wilson lower bound, so a 13-game 62 % option does not beat a 113-game 49 % one
// (BuildChoice). No matchup builds: the counts are per champion and lane, which is what "our build"
// means once the opponent is known too.

import { byWinRate } from './model/advisors';
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
  /** The lane asked for when this plan is the champion's main lane instead (no games there). */
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
  /** Fourth to sixth item, each the slot's best. */
  late: PlanOption[];
  supportChoice: PlanOption | null;
  /** "QEWQQRQEQERE…" (15 levels), empty when no order has games. */
  skillOrder: string;
  /** "QEW": which basic ability is maxed first, second, third. */
  skillMax: string;
}

/** Alternatives below this are not shown; the best option needs only one game. */
export const MINIMUM_ALTERNATIVE_GAMES = 20;

const asOption = (count: BuildCount): PlanOption => ({
  ids: [...count.ids],
  text: count.text ?? null,
  games: count.games,
  winRate: count.games > 0 ? count.wins / count.games : 0,
});

/** All tiers of one champion and lane, summed: the file may hold several brackets. */
function merged(file: RiotBuildFile, championId: number, lane: Lane): ChampionBuildStats | null {
  const rows = file.builds.filter((b) => b.championId === championId && b.lane === lane);
  if (rows.length === 0) return null;
  if (rows.length === 1) return rows[0]!;

  const sum = (pick: (s: ChampionBuildStats) => BuildCount[]): BuildCount[] => {
    const map = new Map<string, BuildCount>();
    for (const row of rows) {
      for (const option of pick(row)) {
        const key = option.text && option.text.length > 0 ? option.text : option.ids.join(',');
        const existing = map.get(key);
        if (existing) {
          existing.games += option.games;
          existing.wins += option.wins;
        } else {
          map.set(key, { ids: [...option.ids], text: option.text, games: option.games, wins: option.wins });
        }
      }
    }
    return [...map.values()];
  };

  return {
    championId,
    lane,
    tier: rows.map((r) => r.tier).join('+'),
    games: rows.reduce((n, r) => n + r.games, 0),
    wins: rows.reduce((n, r) => n + r.wins, 0),
    runes: sum((s) => s.runes),
    spells: sum((s) => s.spells),
    starters: sum((s) => s.starters),
    boots: sum((s) => s.boots),
    core: sum((s) => s.core),
    fourth: sum((s) => s.fourth),
    fifth: sum((s) => s.fifth),
    sixth: sum((s) => s.sixth),
    supportChoice: sum((s) => s.supportChoice),
    skillOrder: sum((s) => s.skillOrder),
    skillMax: sum((s) => s.skillMax),
  };
}

const ranked = (options: BuildCount[]): PlanOption[] =>
  byWinRate(
    options.map((o) => ({ ...o, play: o.games, winRate: o.games > 0 ? o.wins / o.games : 0 })),
    options.length,
  ).map((o) => asOption(o));

/** The best option first, then the alternatives worth showing. */
function slot(options: BuildCount[]): PlanOption[] {
  const list = ranked(options);
  return list.filter((option, index) => index === 0 || option.games >= MINIMUM_ALTERNATIVE_GAMES);
}

/**
 * The plan for a champion on a lane. Without games there, the champion's most played lane stands in
 * — a build is always shown rather than none (1.5 rule) — and the plan says which lane it is from.
 */
export function riotPlan(file: RiotBuildFile, championId: number, lane: Lane): RiotBuildPlan | null {
  let stats = lane === Lane.Unknown ? null : merged(file, championId, lane);
  let inPlaceOf: Lane | null = null;

  if (!stats) {
    let best: ChampionBuildStats | null = null;
    for (const other of ALL_LANES) {
      const candidate = merged(file, championId, other);
      if (candidate && (!best || candidate.games > best.games)) best = candidate;
    }
    if (!best) return null;
    inPlaceOf = lane === Lane.Unknown ? null : lane;
    stats = best;
  }

  const late = [stats.fourth, stats.fifth, stats.sixth].map((options) => ranked(options)[0]).filter((o): o is PlanOption => !!o);

  return {
    championId,
    lane: stats.lane,
    inPlaceOf,
    patch: file.patch,
    games: stats.games,
    winRate: stats.games > 0 ? stats.wins / stats.games : 0,
    runes: ranked(stats.runes.filter((r) => r.ids.length === 11))[0] ?? null,
    spells: ranked(stats.spells)[0] ?? null,
    starters: slot(stats.starters),
    boots: slot(stats.boots),
    core: slot(stats.core),
    late,
    supportChoice: ranked(stats.supportChoice)[0] ?? null,
    skillOrder: ranked(stats.skillOrder)[0]?.text ?? '',
    skillMax: ranked(stats.skillMax)[0]?.text ?? '',
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
