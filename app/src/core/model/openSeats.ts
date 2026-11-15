// What a line-up will be once it is complete, while seats are still open (own data, 15.11.2026).
//
// The measured findings (draftModel COMP_WEIGHT, AGAINST_WEIGHT) are measured on complete teams.
// Until 15.11.2026 a finding on the champions shown so far counted by the share of the team that was
// fixed: three of five without physical damage cost 60 % of "AD-Schaden fehlt" — with the ADC's seat
// still open. On real teams with the bot and jungle seats hidden that rule fired in 18,5 % of them,
// and 1,9 % of those ended without physical damage. In the user's recorded drafts it put Poppy and
// Sion above Thresh and Braum. What counts instead is how likely a finding is to stand at the end,
// with the picks to come taken as what players pick on those lanes:
//
//   no frontline, no engage, little waveclear — nobody shown brings it, and none of the open seats
//     does either: the product over the open lanes of (1 − share of that lane's picks that bring it).
//   AP or AD missing — how likely the damage of the picks to come leaves the mix below the rule's
//     share: counted over every combination of one pick per open lane, each by how often it is
//     picked — and then less, because teammates fill a gap they see (FILLED_BY_TEAMMATES).
//   assassins, tank shredders — those shown plus the share of each open lane's picks.
//   two frontliners or more — from those shown and each open lane's share of frontliners.
//
// A complete team reads exactly as the analyzer says, so the measured weights stand as measured.
// Checked on 103.984 real teams with seats hidden (docs/modellpruefung.md, Messung 29): where this
// says 32 % for "kein Frontline", 31 % of the teams ended without one.

import { DAMAGE_IMBALANCE, hasIssue, STRONG_WAVECLEAR, type CompAnalyzer, type CompIssue } from './compAnalyzer';
import type { DraftSeat } from './draftModel';
import { ALL_LANES, Lane } from './lane';
import { hasStaticData, isTagged, type MetaLookup } from './metaLookup';
import { TankShred } from './tankShred';
import type { TraitTable } from './traits';

/** How likely a finding is to stand when the team is complete, 0..1; absent = 0. */
export type FindingOdds = Partial<Record<CompIssue, number>>;

/** A line-up as it will be at the end, as far as the champions shown and the open lanes tell. */
export interface LineUpOutlook {
  odds: FindingOdds;
  /** Champions with the Assassin tag, the picks to come counted by their lane's share. */
  assassins: number;
  /** Champions that build against tanks on their lane (assassins left out), likewise. */
  shredders: number;
  /** How likely the team ends with two frontliners or more. */
  wall: number;
}

/** What an average pick on a lane brings: shares of the lane's picks, and its mean damage. */
export interface SeatAverage {
  frontline: number;
  engage: number;
  waveclear: number;
  assassin: number;
  shredder: number;
  physical: number;
  magic: number;
  /** Share of the lane's picks the file knows tags and traits of: the rules need that many to judge. */
  hasStatic: number;
  hasTraits: number;
}

const TEAM_SIZE = 5;
/** As on a complete team (compAnalyzer): champions that must be known before a rule judges. */
const NEEDED_FOR_ROLES = 3;
const NEEDED_FOR_WAVECLEAR = 4;
const FIELDS = ['frontline', 'engage', 'waveclear', 'assassin', 'shredder', 'physical', 'magic', 'hasStatic', 'hasTraits'] as const;
const NOTHING: SeatAverage = Object.freeze({ frontline: 0, engage: 0, waveclear: 0, assassin: 0, shredder: 0, physical: 0, magic: 0, hasStatic: 0, hasTraits: 0 });

/**
 * Teammates fill a missing damage type more often than the lane's pick rates say: where the picks
 * of the open lanes alone leave AP or AD missing with some odds, real teams ended that way in 72 %
 * of those odds with one seat open, 56 % with two and 47 % with three — close to 1 / (1 + 0,4 per
 * open seat). The other findings need no such correction (93–99 %).
 */
const FILLED_BY_TEAMMATES = 0.4;

/**
 * A damage type is missing when its share is below the rule's (compAnalyzer DAMAGE_IMBALANCE): magic
 * is, when (1 − share) · magic − share · physical is below zero — a sum over the champions, so every
 * pick adds its own part. One pick's part for either type, and how often it is picked on the lane.
 */
interface DamagePick {
  weight: number;
  magic: number;
  physical: number;
}

/** What the picks of some open lanes add up to, each sum with how likely it is: sorted, with running weight. */
interface DamageSums {
  values: Float64Array;
  /** Weight of all sums up to and including this one. */
  upTo: Float64Array;
}

const edge = (own: number, other: number): number => (1 - DAMAGE_IMBALANCE) * own - DAMAGE_IMBALANCE * other;

/** Beyond this many distinct sums they are gathered in steps: three open seats are 200.000 combinations. */
const MOST_SUMS = 4000;
const SUM_STEP = 50;

/** Every combination of one pick per lane, summed; exact for one and two seats, in steps of 50 damage beyond. */
function sumsOf(lanes: readonly DamagePick[][], kind: 'magic' | 'physical'): DamageSums | null {
  if (lanes.some((picks) => picks.length === 0)) return null;
  let sums = new Map<number, number>([[0, 1]]);
  for (const picks of lanes) {
    const coarse = sums.size * picks.length > MOST_SUMS;
    const next = new Map<number, number>();
    for (const [value, weight] of sums) {
      for (const pick of picks) {
        const sum = coarse ? Math.round((value + pick[kind]) / SUM_STEP) * SUM_STEP : value + pick[kind];
        next.set(sum, (next.get(sum) ?? 0) + weight * pick.weight);
      }
    }
    sums = next;
  }
  const sorted = [...sums].sort((a, b) => a[0] - b[0]);
  const values = Float64Array.from(sorted, ([value]) => value);
  const upTo = new Float64Array(sorted.length);
  let running = 0;
  sorted.forEach(([, weight], i) => {
    running += weight;
    upTo[i] = running;
  });
  return { values, upTo };
}

/** The weight of all sums below a limit. */
function below(sums: DamageSums, limit: number): number {
  let low = 0;
  let high = sums.values.length;
  while (low < high) {
    const mid = (low + high) >> 1;
    if (sums.values[mid]! < limit) low = mid + 1;
    else high = mid;
  }
  return low === 0 ? 0 : Math.min(1, sums.upTo[low - 1]!);
}

/** How likely `have` plus one yes-or-no per open seat reaches `need`. */
function atLeast(need: number, have: number, shares: readonly number[]): number {
  // odds[k]: exactly k of the seats seen so far say yes.
  let odds = [1];
  for (const share of shares) {
    const next = new Array<number>(odds.length + 1).fill(0);
    odds.forEach((p, k) => {
      next[k]! += p * (1 - share);
      next[k + 1]! += p * share;
    });
    odds = next;
  }
  return Math.min(
    1,
    odds.reduce((sum, p, k) => sum + (have + k >= need ? p : 0), 0),
  );
}

export class OpenSeats {
  private readonly byLane = new Map<Lane, SeatAverage>();
  /** A seat whose lane is not known: the mean of the five lanes. */
  private readonly anyLane: SeatAverage;
  private readonly damage = new Map<Lane, DamagePick[]>();
  private readonly anyDamage: DamagePick[];
  /** The sums for a set of open lanes, counted once: they do not depend on who is shown. */
  private readonly sums = new Map<string, DamageSums | null>();

  constructor(
    private readonly analyzer: CompAnalyzer,
    private readonly meta: MetaLookup,
    traits: TraitTable,
    private readonly shred: TankShred = TankShred.none,
  ) {
    for (const lane of ALL_LANES) {
      let weight = 0;
      const sum = { ...NOTHING };
      const picks: DamagePick[] = [];
      for (const id of meta.roster(lane)) {
        const stat = meta.laneStat(id, lane);
        const champion = meta.champion(id);
        if (!stat || !champion) continue;
        // By how often the champion is picked there; a file without pick rates counts its games.
        const w = stat.pickRate > 0 ? stat.pickRate : stat.play;
        if (!(w > 0)) continue;
        const trait = traits.for(champion.key);
        weight += w;
        if (hasStaticData(champion)) sum.hasStatic += w;
        if (trait.isKnown) sum.hasTraits += w;
        if (analyzer.isFrontline(champion)) sum.frontline += w;
        if (trait.isKnown && trait.engage > 0) sum.engage += w;
        if (trait.isKnown && trait.waveclear >= STRONG_WAVECLEAR) sum.waveclear += w;
        if (isTagged(champion, 'Assassin')) sum.assassin += w;
        else if (shred.has(id, lane)) sum.shredder += w;
        const physical = champion.physicalDamage ?? 0;
        const magic = champion.magicDamage ?? 0;
        sum.physical += w * physical;
        sum.magic += w * magic;
        picks.push({ weight: w, magic: edge(magic, physical), physical: edge(physical, magic) });
      }
      this.byLane.set(lane, weight > 0 ? scale(sum, 1 / weight) : NOTHING);
      this.damage.set(
        lane,
        picks.map((pick) => ({ ...pick, weight: pick.weight / weight })),
      );
    }
    const lanes = [...this.byLane.values()];
    this.anyLane = scale(
      lanes.reduce((total, lane) => add(total, lane), { ...NOTHING }),
      1 / Math.max(1, lanes.length),
    );
    this.anyDamage = [...this.damage.values()].flatMap((lane) => lane.map((pick) => ({ ...pick, weight: pick.weight / ALL_LANES.length })));
  }

  average(lane: Lane): SeatAverage {
    return this.byLane.get(lane) ?? this.anyLane;
  }

  /**
   * The lanes still to be picked: the five without those of the champions shown. Where the lanes
   * shown do not add up — a lane unknown, or two champions read onto one — the open seats count as
   * seats on any lane.
   */
  openLanes(seats: readonly DraftSeat[]): Lane[] {
    const shown = seats.filter((seat) => seat.championId !== 0);
    const open = Math.max(0, TEAM_SIZE - new Set(shown.map((seat) => seat.championId)).size);
    const taken = new Set<Lane>(shown.map((seat) => seat.lane).filter((lane) => lane !== Lane.Unknown));
    const free = ALL_LANES.filter((lane) => !taken.has(lane));
    return free.length === open ? free : new Array<Lane>(open).fill(Lane.Unknown);
  }

  /**
   * How likely a damage type stays missing: the part of the champions shown, plus what the picks on
   * the open lanes add, stays below zero. Without picks to count on a lane the champions shown decide.
   */
  private staysMissing(shown: number, lanes: readonly Lane[], kind: 'magic' | 'physical'): number {
    const key = `${kind}:${[...lanes].sort().join(',')}`;
    let sums = this.sums.get(key);
    if (sums === undefined) {
      sums = sumsOf(
        lanes.map((lane) => this.damage.get(lane) ?? this.anyDamage),
        kind,
      );
      this.sums.set(key, sums);
    }
    if (!sums) return shown < 0 ? 1 : 0;
    return below(sums, -shown) / (1 + FILLED_BY_TEAMMATES * lanes.length);
  }

  /** How likely each measured finding stands at the end, from the champions shown on their lanes. */
  odds(seats: readonly DraftSeat[]): FindingOdds {
    return this.outlook(seats).odds;
  }

  /** The line-up at the end: its findings by their odds, and what it brings against the other side. */
  outlook(seats: readonly DraftSeat[]): LineUpOutlook {
    const shown = seats.filter((seat) => seat.championId !== 0).filter((seat, i, all) => all.findIndex((other) => other.championId === seat.championId) === i);
    const ids = shown.map((seat) => seat.championId);
    const profile = this.analyzer.analyze(ids);
    const lanes = this.openLanes(seats);
    const open = lanes.map((lane) => this.average(lane));
    const flag = (issue: CompIssue): number => (hasIssue(profile, issue) ? 1 : 0);
    const plus = (pick: (seat: SeatAverage) => number): number => open.reduce((sum, seat) => sum + pick(seat), 0);

    let assassins = 0;
    let shredders = 0;
    let staticKnown = 0;
    for (const seat of shown) {
      const champion = this.meta.champion(seat.championId);
      if (!champion) continue;
      if (hasStaticData(champion)) staticKnown++;
      if (isTagged(champion, 'Assassin')) assassins++;
      else if (this.shred.has(seat.championId, seat.lane)) shredders++;
    }
    assassins += plus((seat) => seat.assassin);
    shredders += plus((seat) => seat.shredder);
    const wall = atLeast(
      2,
      profile.frontlineCount,
      open.map((seat) => seat.frontline),
    );

    if (open.length === 0) {
      return {
        odds: {
          noMagicDamage: flag('noMagicDamage'),
          noPhysicalDamage: flag('noPhysicalDamage'),
          noFrontline: flag('noFrontline'),
          noEngage: flag('noEngage'),
          littleWaveclear: flag('littleWaveclear'),
        },
        assassins,
        shredders,
        wall,
      };
    }

    const nobody = (pick: (seat: SeatAverage) => number): number => open.reduce((odds, seat) => odds * (1 - pick(seat)), 1);
    // The rules judge a complete team only with enough champions known; the same holds for the team to come.
    staticKnown += plus((seat) => seat.hasStatic);
    const traitsKnown = profile.traitsKnown + plus((seat) => seat.hasTraits);

    // The damage shown, in the file's own amounts; one champion without them and the mix cannot be
    // filled up — then the finding on the champions shown counts by the share that is fixed, as before.
    let physical = 0;
    let magic = 0;
    let amounts = true;
    for (const id of ids) {
      const champion = this.meta.champion(id);
      if (typeof champion?.physicalDamage !== 'number' || typeof champion.magicDamage !== 'number') {
        amounts = false;
        break;
      }
      physical += champion.physicalDamage;
      magic += champion.magicDamage;
    }
    const fixed = Math.min(1, profile.count / TEAM_SIZE);

    return {
      odds: {
        noMagicDamage: amounts ? this.staysMissing(edge(magic, physical), lanes, 'magic') : flag('noMagicDamage') * fixed,
        noPhysicalDamage: amounts ? this.staysMissing(edge(physical, magic), lanes, 'physical') : flag('noPhysicalDamage') * fixed,
        noFrontline: staticKnown >= NEEDED_FOR_ROLES && profile.frontlineCount === 0 ? nobody((seat) => seat.frontline) : 0,
        noEngage: traitsKnown >= NEEDED_FOR_ROLES && profile.maxEngage === 0 ? nobody((seat) => seat.engage) : 0,
        littleWaveclear: traitsKnown >= NEEDED_FOR_WAVECLEAR && profile.strongWaveclearCount === 0 ? nobody((seat) => seat.waveclear) : 0,
      },
      assassins,
      shredders,
      wall,
    };
  }
}

function add(a: SeatAverage, b: SeatAverage): SeatAverage {
  const sum = { ...a };
  for (const field of FIELDS) sum[field] += b[field];
  return sum;
}

function scale(a: SeatAverage, factor: number): SeatAverage {
  const scaled = { ...a };
  for (const field of FIELDS) scaled[field] *= factor;
  return scaled;
}
