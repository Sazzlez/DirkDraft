// Situational items: alternatives to the base build for this game's enemy line-up, measured in our
// own Platinum games (situational.json) — never a rule of thumb. An item is suggested only when, in
// games with the same enemy situation, it clearly beat the other choices for its slot, and beat
// them by more than it does in general: an item that is simply good belongs in the base build.
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

export const SLOT = { Boots: 0, Late: 1 } as const;

export interface RiotSituationalFile {
  version: number;
  patch: string;
  platform: string;
  collectedAtUtc: string;
  situations: string[];
  classNames: string[];
  /** [champion, lane, slot, situation, item (0 = slot total), games, wins] */
  rows: number[][];
  /** [class index, lane, slot, situation, item, games, wins] */
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
  slot: 0 | 1;
  situation: Situation;
  /** Shrunk win rate with the item, and of the slot's other choices, in this situation. */
  winRate: number;
  otherRate: number;
  games: number;
}

/**
 * Pseudo-games pulling a champion's rate towards its class: Darius's own games decide once there
 * are a few hundred, the class speaks for him before. Same idea as the draft model's priors.
 */
export const CLASS_PRIOR = 200;
/** And the class towards the slot's overall rate in this situation. */
export const SLOT_PRIOR = 100;

/** Minimum advantage over the slot's other choices, in win rate, and over the item's general edge. */
export const MINIMUM_LIFT = 0.02;
export const MINIMUM_SITUATIONAL_EDGE = 0.01;
/** The advantage must stand this many standard errors clear of zero. */
export const MINIMUM_Z = 1.5;
export const MINIMUM_GAMES = 30;

interface Count {
  games: number;
  wins: number;
}

class Index {
  private readonly map = new Map<string, Count>();
  constructor(rows: readonly number[][]) {
    for (const row of rows) if (row.length >= 7) this.map.set(row.slice(0, 5).join(':'), { games: row[5]!, wins: row[6]! });
  }
  get(owner: number, lane: number, slot: number, situation: number, item: number): Count {
    return this.map.get(`${owner}:${lane}:${slot}:${situation}:${item}`) ?? { games: 0, wins: 0 };
  }
  items(owner: number, lane: number, slot: number, situation: number): number[] {
    const prefix = `${owner}:${lane}:${slot}:${situation}:`;
    const ids: number[] = [];
    for (const key of this.map.keys()) if (key.startsWith(prefix)) ids.push(Number(key.slice(prefix.length)));
    return ids.filter((id) => id !== 0);
  }
}

const rate = (c: Count, prior: number, target: number): number => (c.wins + prior * target) / (c.games + prior);
const minus = (a: Count, b: Count): Count => ({ games: Math.max(0, a.games - b.games), wins: Math.max(0, a.wins - b.wins) });

export function situationalSuggestions(
  file: RiotSituationalFile,
  meta: MetaLookup,
  championId: number,
  lane: number,
  situations: readonly Situation[],
  exclude: ReadonlySet<number>,
  take = 3,
): SituationalSuggestion[] {
  const champion = meta.champion(championId);
  const classIndex = champion?.tags[0] ? file.classNames.indexOf(champion.tags[0]) : -1;
  const own = new Index(file.rows);
  const cls = new Index(file.classes);

  /** Item and rest rates for one situation, the champion's shrunk towards its class. */
  const edge = (slot: number, situation: number, item: number) => {
    const classTotal = classIndex >= 0 ? cls.get(classIndex, lane, slot, situation, 0) : { games: 0, wins: 0 };
    const classItem = classIndex >= 0 ? cls.get(classIndex, lane, slot, situation, item) : { games: 0, wins: 0 };
    const slotRate = classTotal.games > 0 ? classTotal.wins / classTotal.games : 0.5;
    const classItemRate = rate(classItem, SLOT_PRIOR, slotRate);
    const classRestRate = rate(minus(classTotal, classItem), SLOT_PRIOR, slotRate);

    const total = own.get(championId, lane, slot, situation, 0);
    const mine = own.get(championId, lane, slot, situation, item);
    const rest = minus(total, mine);
    const itemRate = rate(mine, CLASS_PRIOR, classItemRate);
    const restRate = rate(rest, CLASS_PRIOR, classRestRate);

    // Effective games: own plus what the class contributed through the prior.
    const n1 = mine.games + Math.min(CLASS_PRIOR, classItem.games);
    const n2 = rest.games + Math.min(CLASS_PRIOR, classTotal.games - classItem.games);
    const p = (itemRate + restRate) / 2;
    const se = n1 > 0 && n2 > 0 ? Math.sqrt(p * (1 - p) * (1 / n1 + 1 / n2)) : Number.POSITIVE_INFINITY;
    return { itemRate, restRate, lift: itemRate - restRate, se, games: mine.games + classItem.games };
  };

  const found: (SituationalSuggestion & { score: number })[] = [];
  for (const slot of [SLOT.Boots, SLOT.Late] as const) {
    for (const situation of situations) {
      if (situation === SITUATION.All) continue;
      const candidates = new Set([...own.items(championId, lane, slot, situation), ...(classIndex >= 0 ? cls.items(classIndex, lane, slot, situation) : [])]);
      for (const item of candidates) {
        if (exclude.has(item)) continue;
        const here = edge(slot, situation, item);
        if (here.games < MINIMUM_GAMES || here.lift < MINIMUM_LIFT || here.lift / here.se < MINIMUM_Z) continue;
        const general = edge(slot, SITUATION.All, item);
        if (here.lift - general.lift < MINIMUM_SITUATIONAL_EDGE) continue;
        found.push({ itemId: item, slot, situation, winRate: here.itemRate, otherRate: here.restRate, games: here.games, score: here.lift / here.se });
      }
    }
  }

  // One suggestion per item (its strongest situation), best first.
  const best = new Map<number, (typeof found)[number]>();
  for (const s of found) if (!best.has(s.itemId) || best.get(s.itemId)!.score < s.score) best.set(s.itemId, s);
  return [...best.values()]
    .sort((a, b) => b.score - a.score || a.itemId - b.itemId)
    .slice(0, take)
    .map(({ score: _score, ...rest }) => rest);
}
