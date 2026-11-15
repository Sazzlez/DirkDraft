// Situational items: what the base build gives way to for this game's enemies, measured in our own
// Platinum games (situational.json) — never a rule of thumb. Since 11.11.2026 an item that the
// champion's players clearly buy instead of one of the plan's in the situation takes that item's
// place for the game, marked as situational (rateSuggestions, situationalBuild); the others stay
// alternatives beside the build (docs/modellpruefung.md, Messung 20). Since 13.11.2026 only a
// situation of the enemy line-up moves an item into the build: the situations made of the enemies'
// items turned out to measure mostly "later in the game" (same place, Nachmessung Phase 2).
//
// The signal is what players of the champion buy differently in this situation: an item bought
// clearly more often when the enemies stack armor than otherwise is what experience reacts with.
// That signal needs far fewer games than a win rate to stand. The win rate only guards: an item
// that does measurably worse than the slot's other choices — compared within the same game state
// (ahead, even, behind), since whoever is behind buys differently and loses more anyway — is never
// suggested.
//
// Like is compared with like (docs/modellpruefung.md, Messung 17): where a champion has few games in
// a situation, its buy share there leans on its class — and then the share outside the situation
// leans on the class just as much. Before, "in the situation" was nearly the class and "otherwise"
// nearly the champion alone, so a champion that never buys an item was told it buys it 125 times as
// often (Talon: Rabadon's Deathcap, because other assassins buy it against magic resist). And an
// item the champion's players have never bought is not suggested on the class's word alone.
//
// The situation definitions mirror DraftPilot.Core.Data.Situations, which the collector counts
// with; both must stay in step.

import type { MetaLookup } from './model/metaLookup';
import type { PlanOption, RiotBuildPlan } from './riotBuilds';
import { DamageType, isTagged } from './model/metaLookup';
import { hasHardCc, type TraitTable } from './model/traits';

export const SITUATION = {
  All: 0,
  Physical: 1,
  Magic: 2,
  Tanks: 3,
  HardCc: 4,
  EnemyArmor: 5,
  EnemyMagicResist: 6,
  EnemySustain: 7,
} as const;
export type Situation = (typeof SITUATION)[keyof typeof SITUATION];

export const DOMINANT_SHARE = 0.6;
export const FRONTLINE_DEFENSE = 6;
export const TANKS_NEEDED = 2;
export const HARD_CC_NEEDED = 3;
export const ARMOR_HEAVY = 150;
export const MAGIC_RESIST_HEAVY = 120;
export const SUSTAIN_NEEDED = 2;

/** The enemy team's item totals (core/game enemyItemsOf). */
export interface EnemyItemTotals {
  armor: number;
  magicResist: number;
  sustain: number;
}

/**
 * The collector's slots (RiotSituationalTally): boots, items four to six, and since 07.11.2026 the core,
 * items one to three — where most reactions happen (Mikael's against heavy crowd control), and for
 * supports nearly all of them. Files from before count no core: then simply nothing comes from it.
 */
export const SLOT = { Boots: 0, Late: 1, Core: 2 } as const;
export type Slot = (typeof SLOT)[keyof typeof SLOT];

/**
 * Boots answer the line-up, not the enemies' items: they are bought before anyone has 150 armor.
 * Measured on situational.json of 07.11.2026: of 57.533 boots bought, 0,2 % fell in "armor", 0,0 %
 * in "magic resist" and 2,9 % in "sustain" — late swaps and upgrades, whose buy share says nothing
 * about the situation (Jinx: Boots of Swiftness 13,5× as often "against sustain").
 */
export const BOOTS_SITUATIONS: readonly Situation[] = [SITUATION.Physical, SITUATION.Magic, SITUATION.Tanks, SITUATION.HardCc];

/** Game states the collector splits buys by (DraftPilot.Core.Data.GameStates). */
export const GAME_STATES = [0, 1, 2] as const;

export interface RiotSituationalFile {
  version: number;
  patch: string;
  platform: string;
  collectedAtUtc: string;
  situations: string[];
  classNames: string[];
  /** [champion, lane, slot, situation, state, item (0 = slot total), games, wins] */
  rows: number[][];
  /** [class index, lane, slot, situation, state, item, games, wins] */
  classes: number[][];
}

/** The enemy line-up's (and, given their items, build's) situations, by the collector's definitions. */
export function situationsOf(
  enemyIds: readonly number[],
  meta: MetaLookup,
  traits: TraitTable,
  items: EnemyItemTotals | null = null,
): Situation[] {
  const flags: Situation[] = [SITUATION.All];
  let physical = 0;
  let magic = 0;
  let known = 0;
  let frontline = 0;
  let hardCc = 0;
  for (const id of enemyIds) {
    const champion = meta.champion(id);
    switch (champion?.damage) {
      case DamageType.Physical:
        physical++;
        known++;
        break;
      case DamageType.Magic:
        magic++;
        known++;
        break;
      case DamageType.Mixed:
        physical += 0.5;
        magic += 0.5;
        known++;
        break;
    }
    if (champion && (isTagged(champion, 'Tank') || champion.defense >= FRONTLINE_DEFENSE)) frontline++;
    if (champion && hasHardCc(traits.for(champion.key))) hardCc++;
  }
  if (known >= 3 && physical / known >= DOMINANT_SHARE) flags.push(SITUATION.Physical);
  if (known >= 3 && magic / known >= DOMINANT_SHARE) flags.push(SITUATION.Magic);
  if (frontline >= TANKS_NEEDED) flags.push(SITUATION.Tanks);
  if (hardCc >= HARD_CC_NEEDED) flags.push(SITUATION.HardCc);
  if (items && items.armor >= ARMOR_HEAVY) flags.push(SITUATION.EnemyArmor);
  if (items && items.magicResist >= MAGIC_RESIST_HEAVY) flags.push(SITUATION.EnemyMagicResist);
  if (items && items.sustain >= SUSTAIN_NEEDED) flags.push(SITUATION.EnemySustain);
  return flags;
}

export const SITUATION_TEXT: Record<Situation, string> = {
  0: 'in allen Spielen',
  1: 'gegen viel physischen Schaden',
  2: 'gegen viel magischen Schaden',
  3: 'gegen zwei oder mehr Tanks',
  4: 'gegen viel harte Kontrolle',
  5: 'weil die Gegner viel Rüstung gebaut haben',
  6: 'weil die Gegner viel Magieresistenz gebaut haben',
  7: 'weil die Gegner sich über Items heilen',
};

/**
 * Keeps the situations of one game steady: a situation, once reached, stays for the rest of the
 * game. Items are rarely sold, but when one is, or an inventory reads short for a moment, the
 * suggestions should not jump back and forth. Reset at the start of every game.
 */
export class SituationTracker {
  private readonly seen = new Set<Situation>();

  update(current: readonly Situation[]): Situation[] {
    for (const situation of current) this.seen.add(situation);
    return [...this.seen].sort((a, b) => a - b);
  }

  reset(): void {
    this.seen.clear();
  }
}

export interface SituationalSuggestion {
  itemId: number;
  slot: Slot;
  situation: Situation;
  /** How many times more often the item is bought in this situation than otherwise. */
  timesMoreOften: number;
  /** Win rate with the item and of the slot's other choices, compared within each game state. */
  winRate: number;
  otherRate: number;
  /** Buys of the item in this situation, own champion and class. */
  games: number;
  /** The item's share of its slot's buys in this situation (the champion leaning on its class). */
  share: number;
}

/** Pseudo-games pulling a champion's buy share and win rate towards its class's. */
export const CLASS_PRIOR = 50;
/** And a class's win rate towards the slot's overall one in that state. */
export const SLOT_PRIOR = 100;

/** An item has to be bought this much more often in the situation, and in this share of the slot at least. */
export const MINIMUM_RATIO = 1.5;
export const MINIMUM_SHARE = 0.05;
/** The difference in buy share must stand this many standard errors clear. */
export const MINIMUM_ADOPTION_Z = 2;
/** The win-rate guard: measurably worse means this many standard errors below the alternatives. */
export const GUARD_Z = -1.5;
export const MINIMUM_GAMES = 30;
/**
 * Buys of the item by the champion's own players on that lane and in that slot, in any situation,
 * before it can be suggested: the class knows when an item is bought, not whether this champion
 * builds it at all (Fighter covers Irelia and Olaf, Assassin covers Talon and Akali).
 */
export const MINIMUM_OWN_BUYS = 5;

interface Count {
  games: number;
  wins: number;
}

const NONE: Count = { games: 0, wins: 0 };
const plus = (a: Count, b: Count): Count => ({ games: a.games + b.games, wins: a.wins + b.wins });
const minus = (a: Count, b: Count): Count => ({ games: Math.max(0, a.games - b.games), wins: Math.max(0, a.wins - b.wins) });

class Index {
  private readonly map = new Map<string, Count>();
  /** Items per owner:lane:slot:situation, so a lookup does not walk every row. */
  private readonly byPrefix = new Map<string, Set<number>>();
  constructor(rows: readonly number[][]) {
    for (const row of rows) {
      if (row.length < 8) continue;
      this.map.set(row.slice(0, 6).join(':'), { games: row[6]!, wins: row[7]! });
      const prefix = row.slice(0, 4).join(':');
      let set = this.byPrefix.get(prefix);
      if (!set) this.byPrefix.set(prefix, (set = new Set()));
      if (row[5] !== 0) set.add(row[5]!);
    }
  }

  /** One index per file: the game view asks every second, the file changes every few hours. */
  static of(rows: readonly number[][]): Index {
    let index = indexCache.get(rows);
    if (!index) indexCache.set(rows, (index = new Index(rows)));
    return index;
  }
  get(owner: number, lane: number, slot: number, situation: number, state: number, item: number): Count {
    return this.map.get(`${owner}:${lane}:${slot}:${situation}:${state}:${item}`) ?? NONE;
  }
  /** Summed over the game states. */
  all(owner: number, lane: number, slot: number, situation: number, item: number): Count {
    return GAME_STATES.reduce((sum, state) => plus(sum, this.get(owner, lane, slot, situation, state, item)), NONE);
  }
  items(owner: number, lane: number, slot: number, situation: number): number[] {
    return [...(this.byPrefix.get(`${owner}:${lane}:${slot}:${situation}`) ?? [])];
  }
}

const indexCache = new WeakMap<readonly number[][], Index>();

const share = (item: Count, total: Count, prior: number, target: number): number =>
  (item.games + prior * target) / (total.games + prior);
const rate = (c: Count, prior: number, target: number): number => (c.wins + prior * target) / (c.games + prior);

export function situationalSuggestions(
  file: RiotSituationalFile,
  meta: MetaLookup,
  championId: number,
  lane: number,
  situations: readonly Situation[],
  exclude: ReadonlySet<number>,
  take = 3,
  /** The slots still open: boots drop out once a pair is bought. */
  slots: readonly Slot[] = [SLOT.Boots, SLOT.Core, SLOT.Late],
): SituationalSuggestion[] {
  const champion = meta.champion(championId);
  const classIndex = champion?.tags[0] ? file.classNames.indexOf(champion.tags[0]) : -1;
  const own = Index.of(file.rows);
  const cls = Index.of(file.classes);
  const classOf = (fn: (index: Index, owner: number) => Count): Count => (classIndex >= 0 ? fn(cls, classIndex) : NONE);

  /**
   * How often the item is bought in the situation and otherwise. The champion leans on its class by
   * the weight its games in the situation leave to it — CLASS_PRIOR pseudo games against them — and
   * by the same weight on both sides, so the ratio compares like with like: a champion without games
   * in the situation shows its class's ratio, one with many its own.
   */
  const adoption = (slot: number, situation: number, item: number) => {
    const inTotal = own.all(championId, lane, slot, situation, 0);
    const inItem = own.all(championId, lane, slot, situation, item);
    const allTotal = own.all(championId, lane, slot, SITUATION.All, 0);
    const allItem = own.all(championId, lane, slot, SITUATION.All, item);
    const outTotal = minus(allTotal, inTotal);
    const outItem = minus(allItem, inItem);
    const cInTotal = classOf((i, o) => i.all(o, lane, slot, situation, 0));
    const cInItem = classOf((i, o) => i.all(o, lane, slot, situation, item));
    const cOutTotal = minus(classOf((i, o) => i.all(o, lane, slot, SITUATION.All, 0)), cInTotal);
    const cOutItem = minus(classOf((i, o) => i.all(o, lane, slot, SITUATION.All, item)), cInItem);

    const classIn = cInTotal.games > 0 ? cInItem.games / cInTotal.games : 0;
    const classOut = cOutTotal.games > 0 ? cOutItem.games / cOutTotal.games : 0;
    const shareIn = share(inItem, inTotal, CLASS_PRIOR, classIn);
    // The class's weight in the situation, taken over for the other side.
    const weight = CLASS_PRIOR / (inTotal.games + CLASS_PRIOR);
    const ownOut = outTotal.games > 0 ? outItem.games / outTotal.games : classOut;
    const shareOut = outTotal.games > 0 ? (1 - weight) * ownOut + weight * classOut : classOut;

    // Games behind each share: the class counts as CLASS_PRIOR games at most, as on the other side.
    const classGamesIn = Math.min(CLASS_PRIOR, cInTotal.games);
    const classGamesOut = Math.min(CLASS_PRIOR, cOutTotal.games);
    const n1 = inTotal.games + classGamesIn;
    const spreadOut =
      outTotal.games > 0
        ? (1 - weight) ** 2 / outTotal.games + (classGamesOut > 0 ? weight ** 2 / classGamesOut : 0)
        : classGamesOut > 0
          ? 1 / classGamesOut
          : Number.POSITIVE_INFINITY;
    const n2 = spreadOut > 0 && Number.isFinite(spreadOut) ? 1 / spreadOut : 0;
    const pooled = (shareIn * n1 + shareOut * n2) / Math.max(1, n1 + n2);
    const se = n1 > 0 && n2 > 0 ? Math.sqrt(Math.max(1e-9, pooled * (1 - pooled)) * (1 / n1 + 1 / n2)) : Number.POSITIVE_INFINITY;
    return { shareIn, shareOut, z: (shareIn - shareOut) / se, games: inItem.games + cInItem.games, ownBuys: allItem.games };
  };

  /** Win rate with the item against the slot's other choices, within each game state, weighted by the item's buys. */
  const guard = (slot: number, situation: number, item: number) => {
    let weight = 0;
    let withItem = 0;
    let without = 0;
    let variance = 0;
    for (const state of GAME_STATES) {
      const cTotal = classOf((i, o) => i.get(o, lane, slot, situation, state, 0));
      const cItem = classOf((i, o) => i.get(o, lane, slot, situation, state, item));
      const slotRate = cTotal.games > 0 ? cTotal.wins / cTotal.games : 0.5;
      const classItemRate = rate(cItem, SLOT_PRIOR, slotRate);
      const classRestRate = rate(minus(cTotal, cItem), SLOT_PRIOR, slotRate);

      const total = own.get(championId, lane, slot, situation, state, 0);
      const mine = own.get(championId, lane, slot, situation, state, item);
      const rest = minus(total, mine);
      const n1 = mine.games + Math.min(CLASS_PRIOR, cItem.games);
      const n2 = rest.games + Math.min(CLASS_PRIOR, cTotal.games - cItem.games);
      if (n1 <= 0 || n2 <= 0) continue;

      const p1 = rate(mine, CLASS_PRIOR, classItemRate);
      const p2 = rate(rest, CLASS_PRIOR, classRestRate);
      const w = n1;
      weight += w;
      withItem += w * p1;
      without += w * p2;
      variance += w * w * (p1 * (1 - p1) / n1 + p2 * (1 - p2) / n2);
    }
    if (weight <= 0) return null;
    const diff = (withItem - without) / weight;
    const se = Math.sqrt(variance) / weight;
    return { winRate: withItem / weight, otherRate: without / weight, z: se > 0 ? diff / se : 0 };
  };

  const found: (SituationalSuggestion & { score: number })[] = [];
  for (const slot of slots) {
    for (const situation of situations) {
      if (situation === SITUATION.All) continue;
      if (slot === SLOT.Boots && !BOOTS_SITUATIONS.includes(situation)) continue;
      const candidates = new Set([
        ...own.items(championId, lane, slot, situation),
        ...(classIndex >= 0 ? cls.items(classIndex, lane, slot, situation) : []),
      ]);
      for (const item of candidates) {
        if (exclude.has(item)) continue;
        const a = adoption(slot, situation, item);
        if (a.ownBuys < MINIMUM_OWN_BUYS || a.games < MINIMUM_GAMES || a.shareIn < MINIMUM_SHARE || a.shareOut <= 0) continue;
        const ratio = a.shareIn / a.shareOut;
        if (ratio < MINIMUM_RATIO || a.z < MINIMUM_ADOPTION_Z) continue;
        const g = guard(slot, situation, item);
        if (!g || g.z <= GUARD_Z) continue;
        found.push({
          itemId: item,
          slot,
          situation,
          timesMoreOften: ratio,
          winRate: g.winRate,
          otherRate: g.otherRate,
          games: a.games,
          share: a.shareIn,
          score: a.z,
        });
      }
    }
  }

  // One suggestion per item (its clearest situation), clearest first.
  const best = new Map<number, (typeof found)[number]>();
  for (const s of found) if (!best.has(s.itemId) || best.get(s.itemId)!.score < s.score) best.set(s.itemId, s);
  return [...best.values()]
    .sort((a, b) => b.score - a.score || a.itemId - b.itemId)
    .slice(0, take)
    .map(({ score: _score, ...rest }) => rest);
}

/**
 * An item's share of its slot's buys in a situation — the champion leaning on its class with
 * CLASS_PRIOR pseudo games, as the suggestions count it — and the buys that share rests on. What a
 * suggestion's own share is compared with before it takes a plan item's place.
 */
export function situationShare(
  file: RiotSituationalFile,
  meta: MetaLookup,
  championId: number,
  lane: number,
  slot: Slot,
  situation: Situation,
  item: number,
): { share: number; games: number } {
  const champion = meta.champion(championId);
  const classIndex = champion?.tags[0] ? file.classNames.indexOf(champion.tags[0]) : -1;
  const own = Index.of(file.rows);
  const cls = Index.of(file.classes);
  const total = own.all(championId, lane, slot, situation, 0);
  const bought = own.all(championId, lane, slot, situation, item);
  const classTotal = classIndex >= 0 ? cls.all(classIndex, lane, slot, situation, 0) : NONE;
  const classBought = classIndex >= 0 ? cls.all(classIndex, lane, slot, situation, item) : NONE;
  return {
    share: share(bought, total, CLASS_PRIOR, classTotal.games > 0 ? classBought.games / classTotal.games : 0),
    games: total.games + Math.min(CLASS_PRIOR, classTotal.games),
  };
}

/**
 * The situations made of what the enemies have built; they exist only once a game is under way.
 *
 * They suggest, but they never put an item into the build (measured 13.11.2026 on 158.087 games):
 * how much more often an item is bought "against armor" goes hand in hand with how much more often
 * it is bought "against magic resist" (correlation +0,88 over 3.317 items; armor/sustain +0,92,
 * magic resist/sustain +0,87) — two situations that call for opposite items lift the same ones, so
 * what they share is the minute of the game. The line-up situations do not (heavy AD against heavy
 * AP: −0,95). Of the 17 takeovers these situations made, 5 stood out against the other two.
 */
export const ITEM_SITUATIONS: readonly Situation[] = [SITUATION.EnemyArmor, SITUATION.EnemyMagicResist, SITUATION.EnemySustain];

/** A suggestion with the plan's item it stands against, and whether it takes that place. */
export type RatedSuggestion = SituationalSuggestion & { instead: number | null; adopt: boolean };

/** A situational item in the place of one of the plan's: the suggestion that took it. */
export type Swap = RatedSuggestion & { instead: number };

/**
 * Which of the plan's items each suggestion stands against, and whether it takes its place
 * (rule of 11.11.2026: situational items join the build by themselves, marked as such).
 *
 * A suggestion stands against the item of its slot — the boots, the core, the late items — that the
 * champion's players buy least in this situation, among those still open. It takes that place only
 * when it is itself bought clearly more often there (MINIMUM_ADOPTION_Z standard errors): "bought
 * 1,6 times as often against magic resist" at 6 % of the slot is an alternative, not what players
 * do; Mercury's Treads at 55 % against Plated Steelcaps' 39 % when the enemies deal magic damage is
 * (Irelia, file of 11.11.2026).
 *
 * Only a situation of the line-up takes a place (ITEM_SITUATIONS): one made of the enemies' items
 * leaves its suggestion an alternative, with the plan item it would stand against.
 *
 * Two guards, both against the same thing — a situation made of the enemies' items exists only late
 * in a game, so an item bought early has few buys in it or none:
 * - Only plan items with a share of their own in the situation can be replaced (MINIMUM_SHARE, as a
 *   suggestion needs). Stridebreaker stood at 0 % "against magic resist" and would have lost its
 *   place as Garen's first item to anything.
 * - The first item never gives way to such a situation (`firstItem`): Blade of the Ruined King, 8 %
 *   of Vayne's core buys once the enemies heal, is what the few who buy it that late have — not a
 *   sign that players drop it.
 * One suggestion per plan item, the clearest first.
 */
export function rateSuggestions(
  file: RiotSituationalFile,
  meta: MetaLookup,
  championId: number,
  lane: number,
  suggestions: readonly SituationalSuggestion[],
  /** The plan's items of a slot that can still be replaced: not owned, not situational themselves. */
  open: (slot: Slot) => readonly number[],
  /** The plan's first item. */
  firstItem: number | null = null,
): RatedSuggestion[] {
  const taken = new Set<number>();
  return suggestions.map((suggestion) => {
    let instead: number | null = null;
    let least = { share: Number.POSITIVE_INFINITY, games: 0 };
    for (const item of open(suggestion.slot)) {
      if (taken.has(item) || (item === firstItem && ITEM_SITUATIONS.includes(suggestion.situation))) continue;
      const bought = situationShare(file, meta, championId, lane, suggestion.slot, suggestion.situation, item);
      if (bought.share >= MINIMUM_SHARE && bought.share < least.share) {
        least = bought;
        instead = item;
      }
    }
    let adopt = false;
    if (instead !== null && least.games > 0 && !ITEM_SITUATIONS.includes(suggestion.situation)) {
      // Both shares are of the same buys; the difference of two shares of n buys.
      const pooled = (suggestion.share + least.share) / 2;
      const error = Math.sqrt((Math.max(1e-9, pooled * (1 - pooled)) * 2) / least.games);
      adopt = (suggestion.share - least.share) / error >= MINIMUM_ADOPTION_Z;
    }
    if (adopt) taken.add(instead!);
    return { ...suggestion, instead, adopt };
  });
}

/**
 * Keeps the swaps of one game: a situational item, once in the build, stays in it. The suggestions
 * forget an item the moment it is bought (it is no alternative any more), and a later situation may
 * push it out of their list — the build must not fall back to the item it replaced, least of all
 * after the purchase. Reset at the start of every game.
 */
export class SwapTracker {
  private readonly swaps = new Map<number, Swap>();

  /** Adds the new swaps; an item replaced or brought in once is not swapped again. */
  update(found: readonly Swap[]): Swap[] {
    for (const swap of found) {
      const known = [...this.swaps.values()];
      if (this.swaps.has(swap.instead) || known.some((s) => s.itemId === swap.itemId || s.itemId === swap.instead || s.instead === swap.itemId)) continue;
      this.swaps.set(swap.instead, swap);
    }
    return this.current;
  }

  get current(): Swap[] {
    return [...this.swaps.values()];
  }

  reset(): void {
    this.swaps.clear();
  }
}

/** The plan with the swaps made: each replaced item gives its place to the situational one. */
export function applySwaps(plan: RiotBuildPlan, swaps: readonly Swap[]): RiotBuildPlan {
  if (swaps.length === 0) return plan;
  const to = new Map(swaps.map((swap) => [swap.instead, swap.itemId]));
  const swapped = (option: PlanOption): PlanOption => (option.ids.some((id) => to.has(id)) ? { ...option, ids: option.ids.map((id) => to.get(id) ?? id) } : option);
  const first = (options: PlanOption[]): PlanOption[] => options.map((option, index) => (index === 0 ? swapped(option) : option));
  return { ...plan, boots: first(plan.boots), core: first(plan.core), late: plan.late.map(swapped) };
}

/** A late item of the plan brought forward for this game's enemies. */
export interface Move {
  itemId: number;
  /** The plan item it now stands before. */
  before: number;
  situation: Situation;
  /** Shares of the late slot's buys in that situation: its own and that of the item it passed. */
  share: number;
  otherShare: number;
  /** The late buys those shares are of. */
  games: number;
}

/**
 * The plan's late items in the order the champion's players buy them against this line-up (user's
 * wish, 15.11.2026: Braum was led to Thornmail against three magic damage dealers, with Kaenic
 * Rookern one place behind it in his own plan).
 *
 * The plan lists items four to six as the build file counted them over all games. A later one moves
 * in front of an earlier one only on the same evidence that lets a situational item into the build
 * (rateSuggestions): in a situation of the line-up its share of the late buys stands
 * MINIMUM_ADOPTION_Z standard errors clear of the other's. Counted over all 348 plans with late
 * items on the file of 15.11.2026, that alone moved items in 46 of them, most for reasons that had
 * nothing to do with the enemies. So the move must be the line-up's doing:
 * - The champion's own players bought the item in that situation MINIMUM_GAMES times at least. A
 *   share that rests on the class alone moved items for champions without a single late buy there.
 * - Outside the situation it is not the one bought more often. Where it is ahead everywhere
 *   (Katarina's Zhonya's before Void Staff), the plan's order is not this game's to change.
 * - The item passed is not bought earlier against this line-up: Darius' players buy Force of Nature
 *   among their first three items against magic damage — it is missing from the late buys there
 *   because they already have it, not because Sterak's answers magic damage better.
 * Where another situation at hand says the opposite just as clearly, nothing moves. Never a rule of
 * thumb about resistances: Leona's players buy Thornmail before Rookern against magic damage too,
 * and her plan stays as it is.
 *
 * `open`: not owned yet — owned items keep their places and are stepped over. `fixed`: an item that
 * neither moves nor is passed: one a situational swap brought in, or one with a part already bought.
 */
export function lateOrder(
  file: RiotSituationalFile,
  meta: MetaLookup,
  championId: number,
  lane: number,
  late: readonly PlanOption[],
  situations: readonly Situation[],
  open: (itemId: number) => boolean,
  fixed: (itemId: number) => boolean = () => false,
): { late: PlanOption[]; moves: Move[] } {
  const lineUp = situations.filter((s) => s !== SITUATION.All && !ITEM_SITUATIONS.includes(s));
  const at = late.map((option, index) => ({ option, index })).filter(({ option }) => option.ids.length === 1 && open(option.ids[0]!));
  if (lineUp.length === 0 || at.length < 2) return { late: [...late], moves: [] };

  const shareOf = (situation: Situation, item: number) => situationShare(file, meta, championId, lane, SLOT.Late, situation, item);
  // The champion's own buys, without the class: in a situation, and in all games outside it.
  const own = Index.of(file.rows);
  const bought = (slot: Slot, situation: Situation, item: number) => own.all(championId, lane, slot, situation, item).games;
  const outside = (slot: Slot, situation: Situation, item: number) => Math.max(0, bought(slot, SITUATION.All, item) - bought(slot, situation, item));
  /** The item is bought among the first three more often against this line-up than otherwise. */
  const earlier = (situation: Situation, item: number) => {
    const inTotal = bought(SLOT.Core, situation, 0);
    const outTotal = outside(SLOT.Core, situation, 0);
    return inTotal > 0 && outTotal > 0 && bought(SLOT.Core, situation, item) / inTotal > outside(SLOT.Core, situation, item) / outTotal;
  };
  /** How clearly `item` is bought more often than `other` in a situation, in standard errors. */
  const edge = (situation: Situation, item: number, other: number) => {
    const mine = shareOf(situation, item);
    const theirs = shareOf(situation, other);
    if (mine.games <= 0) return null;
    // Both shares are of the same buys; the difference of two shares of n buys.
    const pooled = (mine.share + theirs.share) / 2;
    const error = Math.sqrt((Math.max(1e-9, pooled * (1 - pooled)) * 2) / mine.games);
    return { z: (mine.share - theirs.share) / error, share: mine.share, otherShare: theirs.share, games: mine.games };
  };
  const passes = (item: number, other: number): Omit<Move, 'itemId' | 'before'> | null => {
    let best: (Omit<Move, 'itemId' | 'before'> & { z: number }) | null = null;
    for (const situation of lineUp) {
      if ((edge(situation, other, item)?.z ?? 0) >= MINIMUM_ADOPTION_Z) return null;
      const found = edge(situation, item, other);
      if (!found || found.z < MINIMUM_ADOPTION_Z || found.share < MINIMUM_SHARE) continue;
      if (found.share <= shareOf(SITUATION.All, item).share) continue;
      if (bought(SLOT.Late, situation, item) < MINIMUM_GAMES) continue;
      if (outside(SLOT.Late, situation, item) > outside(SLOT.Late, situation, other) || earlier(situation, other)) continue;
      if (!best || found.z > best.z) best = { situation, ...found };
    }
    if (!best) return null;
    const { z: _z, ...move } = best;
    return move;
  };

  const order = at.map(({ option }) => option);
  const moves: Move[] = [];
  for (let i = 1; i < order.length; i++) {
    let j = i;
    let reason: Omit<Move, 'itemId' | 'before'> | null = null;
    while (j > 0) {
      const item = order[j]!.ids[0]!;
      const other = order[j - 1]!.ids[0]!;
      if (fixed(item) || fixed(other)) break;
      const found = passes(item, other);
      if (!found) break;
      reason = found;
      [order[j - 1], order[j]] = [order[j]!, order[j - 1]!];
      j--;
    }
    if (reason) moves.push({ itemId: order[j]!.ids[0]!, before: order[j + 1]!.ids[0]!, ...reason });
  }

  const result = [...late];
  at.forEach(({ index }, k) => (result[index] = order[k]!));
  return { late: result, moves };
}

/** How many suggestions are looked at for a place in the build; the tile shows fewer. */
const RATED_SUGGESTIONS = 12;

export interface SituationalBuild {
  /** The plan for this game: the base plan with the situational items in their places. */
  plan: RiotBuildPlan;
  /** The situational items now in the plan, bought or not. */
  swaps: Swap[];
  /** For the tile: the swaps still to buy first, then the alternatives that stay beside the build. */
  suggestions: RatedSuggestion[];
  /** The plan's own late items brought forward for this line-up (lateOrder). */
  moves: Move[];
}

/**
 * The build for this game's enemies: the suggestions for the situations at hand, each rated against
 * the plan (rateSuggestions), the clear ones put in the plan's place and kept there (the tracker),
 * the others left as alternatives.
 *
 * `owned`: the inventory's item ids. `has`: whether a plan item counts as owned (a transformed or
 * upgraded item stands for its original, core/game ownsItem). `slots`: the slots still open — boots
 * drop out once a pair is bought. `begun`: a plan item with a part already bought; the late order
 * leaves it where it is.
 */
export function situationalBuild(
  file: RiotSituationalFile,
  meta: MetaLookup,
  plan: RiotBuildPlan,
  championId: number,
  situations: readonly Situation[],
  owned: readonly number[],
  has: (itemId: number) => boolean,
  slots: readonly Slot[],
  tracker: SwapTracker,
  take = 3,
  begun: (itemId: number) => boolean = () => false,
): SituationalBuild {
  const base = [...(plan.boots[0]?.ids ?? []), ...(plan.core[0]?.ids ?? []), ...plan.late.flatMap((o) => o.ids)];
  const before = tracker.current;
  const brought = new Set(before.map((swap) => swap.itemId));
  const current = applySwaps(plan, before);
  // Neither what is owned nor what the plan holds — its own items or the ones brought in — is an alternative to it.
  const exclude = new Set([...owned, ...base, ...brought]);
  const found = situationalSuggestions(file, meta, championId, plan.lane, situations, exclude, RATED_SUGGESTIONS, slots);

  const open = (slot: Slot): number[] => {
    if (!slots.includes(slot)) return [];
    const group = slot === SLOT.Boots ? (current.boots[0]?.ids ?? []) : slot === SLOT.Core ? (current.core[0]?.ids ?? []) : current.late.flatMap((o) => o.ids);
    return group.filter((id) => !brought.has(id) && !has(id));
  };
  const rated = rateSuggestions(file, meta, championId, plan.lane, found, open, plan.core[0]?.ids[0] ?? null);
  const swaps = tracker.update(rated.filter((r): r is Swap => r.adopt && r.instead !== null));

  const inPlan = new Set(swaps.map((swap) => swap.itemId));
  // Still to buy: not owned, and its slot not filled otherwise (another pair of boots, three other items).
  const toBuy = swaps.filter((swap) => !has(swap.itemId) && slots.includes(swap.slot));
  const beside = rated.filter((r) => !inPlan.has(r.itemId)).map((r) => ({ ...r, adopt: false }));
  const swapped = applySwaps(plan, swaps);
  const ordered = slots.includes(SLOT.Late)
    ? lateOrder(file, meta, championId, plan.lane, swapped.late, situations, (id) => !has(id), (id) => inPlan.has(id) || begun(id))
    : { late: swapped.late, moves: [] };
  return {
    plan: ordered.moves.length > 0 ? { ...swapped, late: ordered.late } : swapped,
    swaps,
    suggestions: [...toBuy, ...beside].slice(0, Math.max(take, toBuy.length)),
    moves: ordered.moves,
  };
}

/**
 * An item bought this many times as often from ahead as from behind is bought because of the lead.
 * Counted over all champions, items one to six, on the file of 11.11.2026: Mejai's Soulstealer 4,6,
 * then Endless Hunger and Guardian Angel 1,5 — nothing in between; no pair of boots above 1,2.
 */
export const WIN_MORE_RATIO = 2;
/** Buys an item needs over all champions before that ratio is taken at its word. */
export const WIN_MORE_MINIMUM_BUYS = 300;

const winMoreCache = new WeakMap<RiotSituationalFile, ReadonlySet<number>>();

/**
 * The items bought from ahead (WIN_MORE_RATIO), over every champion's items one to six. Counted over
 * all champions because one champion's late slot is too thin to tell: Evelynn's Mejai's stands at 1,8
 * there, and still wins 88 % against Void Staff's 80 % within "ahead".
 */
export function boughtFromAhead(file: RiotSituationalFile): ReadonlySet<number> {
  const cached = winMoreCache.get(file);
  if (cached) return cached;
  const AHEAD = 1;
  const BEHIND = 2;
  // Per item (0 = the slots' total): buys even, ahead and behind.
  const buys = new Map<number, { even: number; ahead: number; behind: number }>();
  for (const row of file.rows) {
    if (row.length < 8 || row[3] !== SITUATION.All || (row[2] !== SLOT.Core && row[2] !== SLOT.Late)) continue;
    let entry = buys.get(row[5]!);
    if (!entry) buys.set(row[5]!, (entry = { even: 0, ahead: 0, behind: 0 }));
    if (row[4] === AHEAD) entry.ahead += row[6]!;
    else if (row[4] === BEHIND) entry.behind += row[6]!;
    else entry.even += row[6]!;
  }
  const found = new Set<number>();
  const total = buys.get(0);
  if (total && total.ahead > 0 && total.behind > 0) {
    for (const [item, n] of buys) {
      if (item === 0 || n.even + n.ahead + n.behind < WIN_MORE_MINIMUM_BUYS) continue;
      if (n.ahead / total.ahead > (WIN_MORE_RATIO * n.behind) / total.behind) found.add(item);
    }
  }
  winMoreCache.set(file, found);
  return found;
}

/**
 * How clearly `items` win more often than `base` for this champion and lane when both are compared
 * between purchases made in the same game state (even, ahead, behind): the difference in each state
 * weighted by how well it is known, in standard errors. Several items on a side are counted together
 * (the items that tell two cores apart). The build plan asks this before boots or items replace the
 * most played ones (core/riotBuilds).
 *
 * Null when no state holds both sides, and when one of `items` is bought from ahead: the team's gold
 * lead does not tell who on the team is ahead, so within "ahead" such an item still wins with the
 * players who were — its rate is the lead, not the item.
 */
export function sameStateEdge(
  file: RiotSituationalFile,
  championId: number,
  lane: number,
  slot: Slot,
  items: readonly number[],
  base: readonly number[],
): number | null {
  const fromAhead = boughtFromAhead(file);
  if (items.some((id) => fromAhead.has(id))) return null;

  const own = Index.of(file.rows);
  const count = (state: number, ids: readonly number[]): Count => ids.reduce((sum, id) => plus(sum, own.get(championId, lane, slot, SITUATION.All, state, id)), NONE);
  let sum = 0;
  let weight = 0;
  for (const state of GAME_STATES) {
    const a = count(state, items);
    const b = count(state, base);
    if (a.games <= 0 || b.games <= 0) continue;
    const pooled = (a.wins + b.wins) / (a.games + b.games);
    const variance = pooled * (1 - pooled) * (1 / a.games + 1 / b.games);
    if (variance <= 0) continue;
    sum += (a.wins / a.games - b.wins / b.games) / variance;
    weight += 1 / variance;
  }
  return weight > 0 ? sum / Math.sqrt(weight) : null;
}
