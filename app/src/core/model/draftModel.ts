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

import type { CompIssue } from './compAnalyzer';
import { ALL_LANES, Lane } from './lane';
import type { MetaLookup } from './metaLookup';
import type { FindingOdds, LineUpOutlook } from './openSeats';
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
 * that stands two standard errors clear of nothing — on 166.374 games, checked on 41.594 held back
 * (15.11.2026, docs/modellpruefung.md Messung 29): no magic damage 0,249 ± 0,054, no physical damage
 * 0,299 ± 0,102, no engage 0,175 ± 0,111, little waveclear 0,041 ± 0,019.
 *
 * "No frontline" carries no weight of its own any more. With the frontline read from the Tank tag
 * alone (compAnalyzer isFrontline) the finding stands at +0,013 ± 0,036 beside the assassins of the
 * other side: a team without a frontline loses where the enemy has someone to go for its back line,
 * and not otherwise (AGAINST_WEIGHT below). No peel and all melee are too rare to measure (0,7 % of
 * the games each) and stay where they were, under the comp cap (COMP_SCALE); peel against hard
 * engage (+0,006 ± 0,013 a champion), mixed timing, poke and the count of ranged, waveclear, early
 * or late champions showed no effect and carry nothing.
 */
export const COMP_WEIGHT: Readonly<Partial<Record<CompIssue, number>>> = {
  noMagicDamage: 0.25,
  noPhysicalDamage: 0.3,
  noEngage: 0.175,
  littleWaveclear: 0.04,
};

/** The findings with a measured weight. */
export const MEASURED_ISSUES = Object.keys(COMP_WEIGHT) as CompIssue[];

/**
 * What a champion brings against the other line-up, in log-odds each, from the same fit:
 *   backline — an assassin against a team without a frontline: +0,026 ± 0,011 (0,65 points);
 *   wall — an assassin against two frontliners or more: −0,010 ± 0,008;
 *   shred — a champion that builds against tanks, against two frontliners or more: +0,017 ± 0,011.
 * Assassins by themselves (−0,000 ± 0,014) carry nothing. Until 15.11.2026 the first two ran as
 * rules under the comp cap (+0,048 and −0,032) and tank shred was a hint; with the old frontline
 * rule none of them could be told from nothing.
 */
export const AGAINST_WEIGHT = { backline: 0.026, wall: -0.01, shred: 0.017 } as const;

/** Who reads a line-up as it will be once it is complete (openSeats.ts OpenSeats). */
export interface LineUps {
  outlook(seats: readonly DraftSeat[]): LineUpOutlook;
}

/**
 * What a line-up is worth by its measured findings, in log-odds (zero or below): every finding by
 * how likely it is to stand at the end. Measured on complete teams, where a finding stands or does
 * not; while seats are open the picks to come can still close it, and they count as what players
 * pick on those lanes (openSeats.ts). Never more than measured.
 */
export function compCost(odds: FindingOdds): number {
  let lost = 0;
  for (const issue of MEASURED_ISSUES) lost += COMP_WEIGHT[issue]! * Math.min(1, Math.max(0, odds[issue] ?? 0));
  return lost === 0 ? 0 : -lost;
}

/** What the champions of one side bring against the other line-up, in log-odds. */
function against(mine: LineUpOutlook, theirs: LineUpOutlook): number {
  return AGAINST_WEIGHT.backline * mine.assassins * (theirs.odds.noFrontline ?? 0) + (AGAINST_WEIGHT.wall * mine.assassins + AGAINST_WEIGHT.shred * mine.shredders) * theirs.wall;
}

/** The own line-up against theirs: what either lacks, and what either brings against the other. */
export function lineUpEdge(mine: LineUpOutlook, theirs: LineUpOutlook): number {
  return compCost(mine.odds) - compCost(theirs.odds) + against(mine, theirs) - against(theirs, mine);
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
 * `comp`: who reads both line-ups for what they will lack (OpenSeats); without it the comp counts nothing.
 */
export function draftParts(
  meta: MetaLookup,
  allies: readonly DraftSeat[],
  enemies: readonly DraftSeat[],
  comp: LineUps | null = null,
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

  const compEdge = comp ? lineUpEdge(comp.outlook(allies), comp.outlook(enemies)) : 0;
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
