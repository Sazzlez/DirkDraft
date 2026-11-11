// Situational items: alternatives to the base build for this game's enemies, measured in our own
// Platinum games (situational.json) — never a rule of thumb.
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
