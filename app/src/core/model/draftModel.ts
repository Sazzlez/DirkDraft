// The draft model for our own data: what ten champions on their lanes say about the winner, with
// every part weighted as the games weigh it (docs/modellpruefung.md, Messung 13, 14 and 21; concept
// docs/draft-konzept.md, stage B).
//
//   chance of the own team = strength of every champion on its lane
//     + the duel on every lane (shrunk per lane, shrinkage.ts OWN_MATCHUP_PRIOR)
//     + the botlane duo of both sides
//     + every pair across the teams on different lanes, by its kind
//     + what the line-up lacks on either side
//
// Measured on 116.598 Platinum games and checked on 29.143 held back (13.11.2026): this predicts
// the winner better than the draft chance of 2.0 (8,3 σ), which averaged strength and duels over the
// lanes and so stood 4,6 times too close to 50 %, and better than the factor model (2,8 σ). Parts
// the games cannot tell from nothing carry no weight: duos other than the botlane's, and the comp
// findings that showed no effect.
//
// Only for files with source "riot". OP.GG files keep the 1.5 model, which the golden tests hold.
// Every weight here is refitted with `npm run measure:model`; none is an estimate.

import { hasIssue, type CompIssue, type CompProfile } from './compAnalyzer';
import { ALL_LANES, Lane } from './lane';
import type { MetaLookup } from './metaLookup';
import { logit } from './scoreModel';

/** A champion on a lane, as the model counts it. */
export interface DraftSeat {
  championId: number;
  lane: Lane;
}

/**
 * The lane strengths of a side, summed, count as the lane win rates say. Fitted together with all
 * other parts the sum stands at 0,905 ± 0,068; with the fixed weights below the chance is calibrated
 * at 1 (slope 1,04 ± 0,11 on the held-out games, every band within 1,7 points) and at 0,9 a little
 * too close to 50 % (1,11 ± 0,12). So the rates stay as they are.
 */
export const STRENGTH_WEIGHT = 1;

/**
 * The botlane duo (ADC with support) beyond what both lane strengths predict: 0,41 ± 0,27 of the
 * shrunk difference. The only duo with a weight — jungle with mid (−0,06 ± 0,33), jungle with top
 * (+0,21 ± 0,36), jungle with the botlane (−0,01 ± 0,21), mid with support (+0,16 ± 0,31) and all
 * other pairs (+0,09 ± 0,15) cannot be told from nothing.
 */
export const BOT_DUO_WEIGHT = 0.41;

/** The kinds of pairs across the teams on different lanes. */
export type VersusKind = 'botCross' | 'jungle' | 'other';

/**
 * Pairs across the teams on different lanes, beyond both lane strengths: the botlane cross (ADC
 * against the enemy support and the other way round) 0,55 ± 0,27 and every pair without a jungler
 * 0,20 ± 0,15. A jungler against a laner stands at 0,17 ± 0,18 — not two standard errors from
 * nothing, so it carries no weight yet.
 */
export const VERSUS_WEIGHT: Readonly<Record<VersusKind, number>> = { botCross: 0.55, jungle: 0, other: 0.2 };

export function versusKind(mine: Lane, theirs: Lane): VersusKind {
  if ((mine === Lane.Adc && theirs === Lane.Support) || (mine === Lane.Support && theirs === Lane.Adc)) return 'botCross';
  if (mine === Lane.Jungle || theirs === Lane.Jungle) return 'jungle';
  return 'other';
}

/**
 * What a complete line-up loses with a finding, in log-odds, fitted together with every other part
 * that stands two standard errors clear of nothing (alone, in points at 50 %, in brackets): no magic
 * damage 0,260 ± 0,064 (7,0 ± 1,4), no physical damage 0,295 ± 0,123 (7,2 ± 2,8), no engage
 * 0,221 ± 0,134 (5,5 ± 3,0), little waveclear 0,050 ± 0,024 (1,2 ± 0,5), no frontline 0,035 ± 0,028
 * (0,5 ± 0,6 alone, clear only together with the others). No peel and all melee are too rare to
 * measure (0,7 % of the games each) and stay where they were, under the comp cap (COMP_SCALE);
 * mixed timing, tank shred, poke and the count of ranged, waveclear, early or late champions showed
 * no effect within a third of a point and carry nothing.
 */
export const COMP_WEIGHT: Readonly<Partial<Record<CompIssue, number>>> = {
  noMagicDamage: 0.26,
  noPhysicalDamage: 0.295,
  noEngage: 0.22,
  littleWaveclear: 0.05,
  noFrontline: 0.035,
};

/** The findings with a measured weight. */
export const MEASURED_ISSUES = Object.keys(COMP_WEIGHT) as CompIssue[];

const TEAM_SIZE = 5;

/**
 * What a line-up is worth by its measured findings, in log-odds (zero or below). Measured on
 * complete teams; while seats are open a finding counts by the share of the team that is fixed — the
 * picks to come can still close it. Never more than measured.
 */
export function compValue(profile: CompProfile): number {
  let lost = 0;
  for (const issue of MEASURED_ISSUES) if (hasIssue(profile, issue)) lost += COMP_WEIGHT[issue]!;
  return lost === 0 ? 0 : -lost * Math.min(1, profile.count / TEAM_SIZE);
}

/** The champion on its lane against one enemy on another lane: its weighted edge, null without a row. */
export function versusEdge(meta: MetaLookup, mine: DraftSeat, theirs: DraftSeat): number | null {
  if (mine.lane === Lane.Unknown || theirs.lane === Lane.Unknown || mine.lane === theirs.lane || mine.championId === theirs.championId) return null;
  const view = meta.versus(mine.championId, mine.lane, theirs.championId, theirs.lane);
  if (!view) return null;
  return VERSUS_WEIGHT[versusKind(mine.lane, theirs.lane)] * (logit(view.winRate) - logit(view.expected));
}

export const isBotDuo = (a: Lane, b: Lane): boolean => (a === Lane.Adc && b === Lane.Support) || (a === Lane.Support && b === Lane.Adc);

/** Two champions of one team as the botlane duo: the weighted edge, null for other lanes or without a row. */
export function botDuoEdge(meta: MetaLookup, a: DraftSeat, b: DraftSeat): number | null {
  if (!isBotDuo(a.lane, b.lane)) return null;
  const view = meta.synergy(a.championId, b.championId);
  if (!view) return null;
  return BOT_DUO_WEIGHT * (logit(view.winRate) - logit(view.expected));
}

/** The duel on a lane beyond both lane strengths, as the lane's own weight shrinks it; null without a row. */
export function duelEdge(meta: MetaLookup, championId: number, opponentId: number, lane: Lane): number | null {
  const view = meta.matchup(championId, opponentId, lane);
  return view ? logit(view.winRate) - meta.matchupBaseline(championId, opponentId, lane) : null;
}

/** The lane strength of one seat in log-odds, weighted; null without a lane row. */
export function strengthOf(meta: MetaLookup, seat: DraftSeat): number | null {
  const stat = seat.lane === Lane.Unknown ? null : meta.laneStat(seat.championId, seat.lane);
  return stat ? STRENGTH_WEIGHT * logit(stat.winRate) : null;
}

/** The parts of the own team's chance, each in log-odds from the own side. */
export interface DraftParts {
  strength: number;
  duels: number;
  duo: number;
  versus: number;
  comp: number;
  total: number;
  /** Champions with a lane strength, both sides. */
  rated: number;
  /** Lanes with both champions known and a duel between them. */
  contested: number;
  /** Both sides have at least one rated champion. */
  comparable: boolean;
}

/**
 * The own team's chance from everything the draft has fixed. An open seat counts as an average
 * champion — nothing — so the number is usable from the first pick and moves with every one.
 * `comp`: the line-up profiles of both sides (CompAnalyzer.analyze); without them the comp counts nothing.
 */
export function draftParts(
  meta: MetaLookup,
  allies: readonly DraftSeat[],
  enemies: readonly DraftSeat[],
  comp: { allies: CompProfile; enemies: CompProfile } | null = null,
): DraftParts {
  let strength = 0;
  let allyRated = 0;
  let enemyRated = 0;
  for (const seat of allies) {
    const value = strengthOf(meta, seat);
    if (value === null) continue;
    strength += value;
    allyRated++;
  }
  for (const seat of enemies) {
    const value = strengthOf(meta, seat);
    if (value === null) continue;
    strength -= value;
    enemyRated++;
  }

  let duels = 0;
  let contested = 0;
  for (const lane of ALL_LANES) {
    const mine = allies.filter((s) => s.lane === lane);
    const theirs = enemies.filter((s) => s.lane === lane);
    if (mine.length !== 1 || theirs.length !== 1) continue;
    const edge = duelEdge(meta, mine[0]!.championId, theirs[0]!.championId, lane);
    if (edge === null) continue;
    duels += edge;
    contested++;
  }

  let duo = 0;
  for (const [seats, sign] of [
    [allies, 1],
    [enemies, -1],
  ] as const) {
    for (let i = 0; i < seats.length; i++) for (let j = i + 1; j < seats.length; j++) duo += sign * (botDuoEdge(meta, seats[i]!, seats[j]!) ?? 0);
  }

  let versus = 0;
  for (const mine of allies) for (const theirs of enemies) versus += versusEdge(meta, mine, theirs) ?? 0;

  const compEdge = comp ? compValue(comp.allies) - compValue(comp.enemies) : 0;
  return {
    strength,
    duels,
    duo,
    versus,
    comp: compEdge,
    total: strength + duels + duo + versus + compEdge,
    rated: allyRated + enemyRated,
    contested,
    comparable: allyRated > 0 && enemyRated > 0,
  };
}
