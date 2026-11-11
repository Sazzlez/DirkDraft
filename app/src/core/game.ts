// The game moment, from the game's own Live Client Data API (https://127.0.0.1:2999,
// /liveclientdata/allgamedata) and our build plan: what to buy next with the gold at hand, which
// skill point comes next, what the enemies have built and which damage they deal.
//
// Fixed rules apply here too: no other player's name ever reaches the view (names are used only to
// find the own row and are dropped right there), and no timers — no cooldowns, no ultimate or
// summoner timers, nothing the game does not show on its own scoreboard.

import { DamageType, type MetaLookup } from './model/metaLookup';
import type { RiotBuildPlan } from './riotBuilds';

// ---- the API's shape (only what is read) ------------------------------------------------------

export interface LiveItem {
  itemID: number;
  count?: number;
}

export interface LivePlayer {
  championName?: string;
  /** "game_character_displayname_MonkeyKing": the Data Dragon key, independent of the client language. */
  rawChampionName?: string;
  team?: string;
  level?: number;
  items?: LiveItem[];
  riotId?: string;
  summonerName?: string;
  position?: string;
}

export interface LiveGameData {
  activePlayer?: {
    riotId?: string;
    summonerName?: string;
    level?: number;
    currentGold?: number;
    abilities?: Record<string, { abilityLevel?: number } | undefined>;
  };
  allPlayers?: LivePlayer[];
  gameData?: { gameTime?: number; gameMode?: string };
}

/** Price, recipe and the stats the situations read (Data Dragon item.json). */
export interface ItemInfo {
  total: number;
  from: number[];
  into?: number[];
  purchasable?: boolean;
  tags?: string[];
  armor?: number;
  magicResist?: number;
  /** The item this one transforms from (Muramana from Manamune, Seraph's from Archangel's). */
  specialRecipe?: number;
}

/** Basic boots; every finished pair is built from them. */
export const BASIC_BOOTS = 1001;

/** Bounty of Worlds: the support quest done; each of the five final support items is built from it. */
export const SUPPORT_QUEST_DONE = 3867;

/** One of the support quest's final items (Celestial Opposition, Dream Maker, …). */
export const isSupportUpgrade = (id: number, items: ReadonlyMap<number, ItemInfo>): boolean =>
  items.get(id)?.from.includes(SUPPORT_QUEST_DONE) ?? false;

const hasTag = (info: ItemInfo | undefined, tag: string): boolean =>
  !!info?.tags?.some((t) => t.toLowerCase() === tag.toLowerCase());

/** World Atlas and Runic Compass, the support quest's first two steps (16.22.1). */
const SUPPORT_QUEST_STEPS: readonly number[] = [3865, 3866, SUPPORT_QUEST_DONE];

/**
 * What the items held are worth: their shop prices, without what is used up, free or handed to every
 * support (potions, wards, the trinket, the support quest) — ItemCatalog.CountsForWorth, the rule
 * the collector counts the boots' timing with; both must stay in step.
 */
export function itemWorth(owned: readonly number[], items: ReadonlyMap<number, ItemInfo>): number {
  let worth = 0;
  for (const id of owned) {
    const info = items.get(id);
    if (!info || hasTag(info, 'Consumable') || hasTag(info, 'Trinket')) continue;
    if (SUPPORT_QUEST_STEPS.includes(id) || isSupportUpgrade(id, items)) continue;
    worth += info.total;
  }
  return worth;
}

/**
 * A pair of boots past the basic ones: built from basic boots, directly or through another pair.
 * The tier-3 upgrades (Gunmetal Greaves from Berserker's, …) come from a pair, not from 1001, and
 * Data Dragon leaves some of them without the Boots tag — the recipe is what they share.
 */
export function isBoots(id: number, items: ReadonlyMap<number, ItemInfo>, depth = 0): boolean {
  if (id === BASIC_BOOTS || depth > 4) return false;
  const from = items.get(id)?.from ?? [];
  return from.includes(BASIC_BOOTS) || from.some((f) => isBoots(f, items, depth + 1));
}

/**
 * Whether the plan's item counts as owned: the item itself, what it transformed into (Muramana for
 * Manamune) or an upgrade built from it (the tier-3 boots for their pair).
 */
export function ownsItem(id: number, owned: readonly number[], items: ReadonlyMap<number, ItemInfo>): boolean {
  if (owned.includes(id)) return true;
  return owned.some((o) => {
    const info = items.get(o);
    return info?.specialRecipe === id || (isBoots(o, items) && isBoots(id, items) && (info?.from.includes(id) ?? false));
  });
}

/** ItemCatalog.IsFinished: an end point of the shop, not a component, boots or consumable. */
export function isFinished(id: number, items: ReadonlyMap<number, ItemInfo>): boolean {
  const info = items.get(id);
  if (!info || info.total < 1000 || id === 1001 || isBoots(id, items)) return false;
  if (info.from.some((f) => isBoots(f, items)) || info.from.includes(3867)) return false;
  if (hasTag(info, 'Consumable') || hasTag(info, 'Trinket')) return false;
  return !(info.into ?? []).some((next) => items.get(next)?.purchasable !== false && items.has(next));
}

/** What the enemies have built, summed — finished items and boots only, as the collector counts. */
export function enemyItemsOf(itemIds: readonly number[], items: ReadonlyMap<number, ItemInfo>): { armor: number; magicResist: number; sustain: number } {
  let armor = 0;
  let magicResist = 0;
  let sustain = 0;
  for (const id of itemIds) {
    if (!isFinished(id, items) && !isBoots(id, items)) continue;
    const info = items.get(id)!;
    armor += info.armor ?? 0;
    magicResist += info.magicResist ?? 0;
    if (hasTag(info, 'LifeSteal') || hasTag(info, 'SpellVamp')) sustain++;
  }
  return { armor, magicResist, sustain };
}

export type Skill = 'Q' | 'W' | 'E' | 'R';

// ---- what the game moment says ------------------------------------------------------------------

export interface PathStep {
  slot: 'Start' | 'Schuhe' | 'Support' | 'Kern' | 'Spät';
  itemId: number;
  owned: boolean;
}

export interface NextBuy {
  /** What to buy now: the target item if affordable, else its most expensive affordable component. */
  itemId: number;
  /** What it costs now, with the components already owned counted in. */
  cost: number;
  affordable: boolean;
  /** The item this buy works towards, when the buy is a component of it. */
  towards: number | null;
  /** The support quest is done: the planned upgrade is waiting to be picked in the shop. */
  supportChoice: boolean;
}

/** One purchase of the shopping list. */
export interface Buy {
  itemId: number;
  /** What it costs at that point of the list, with what is owned by then counted in. */
  cost: number;
  /** The plan's item this buy works towards, when it is a part of it; null for the item itself. */
  towards: number | null;
}

export interface EnemyView {
  championId: number;
  items: number[];
  damage: DamageType;
}

export interface GameModel {
  /** Seconds since the game started. */
  gameTime: number;
  level: number;
  gold: number;
  championId: number;
  path: PathStep[];
  nextBuy: NextBuy | null;
  /**
   * Everything the gold at hand buys right now, in order: the plan's next item or its parts, the
   * boots when they are due, then on with what is left. Empty when nothing is affordable.
   */
  basket: Buy[];
  /** Gold left after the basket. */
  goldLeft: number;
  /** The next skill point by the plan, and whether it can be spent now. */
  nextSkill: { key: Skill; level: number; now: boolean } | null;
  /** Skill points already spent (Q + W + E + R levels). */
  skillPoints: number;
  enemies: EnemyView[];
  /** Share of the enemy champions dealing mainly physical (magic) damage. */
  enemyPhysical: number;
  enemyMagic: number;
}

/** A starting item is only worth suggesting in the first minutes. */
export const START_WINDOW_SECONDS = 90;

/** Data Dragon key out of rawChampionName, else the display name compared as written. */
export function championIdOf(player: LivePlayer, meta: MetaLookup): number {
  const raw = player.rawChampionName ?? '';
  const prefix = 'game_character_displayname_';
  const key = raw.startsWith(prefix) ? raw.slice(prefix.length) : null;
  for (const champion of meta.champions) {
    if (key && champion.key.toLowerCase() === key.toLowerCase()) return champion.id;
  }
  const name = player.championName?.toLowerCase();
  return name ? (meta.champions.find((c) => c.name.toLowerCase() === name)?.id ?? 0) : 0;
}

function ownRow(data: LiveGameData): LivePlayer | null {
  const me = data.activePlayer;
  const players = data.allPlayers ?? [];
  if (!me) return null;
  return (
    players.find((p) => !!me.riotId && p.riotId === me.riotId) ??
    players.find((p) => !!me.summonerName && p.summonerName === me.summonerName) ??
    null
  );
}

/** Every item owned, components included once per instance (two Long Swords are two). */
function ownedItems(player: LivePlayer | null): number[] {
  const owned: number[] = [];
  for (const item of player?.items ?? []) {
    for (let i = 0; i < Math.max(1, item.count ?? 1); i++) owned.push(item.itemID);
  }
  return owned;
}

/**
 * What an item still costs: its price minus the components already owned, each owned instance used
 * once — the shop's own arithmetic. `owned` is consumed.
 */
export function remainingCost(itemId: number, owned: number[], items: ReadonlyMap<number, ItemInfo>): number {
  const info = items.get(itemId);
  if (!info) return 0;
  let cost = info.total;
  for (const component of info.from) {
    const index = owned.indexOf(component);
    if (index >= 0) {
      owned.splice(index, 1);
      cost -= items.get(component)?.total ?? 0;
    } else {
      // A component not owned may itself be half built.
      const own = items.get(component)?.total ?? 0;
      cost -= own - remainingCost(component, owned, items);
    }
  }
  return Math.max(0, cost);
}

function pathOf(plan: RiotBuildPlan | null, owned: readonly number[], gameTime: number, items: ReadonlyMap<number, ItemInfo>): PathStep[] {
  if (!plan) return [];
  const has = (id: number) => ownsItem(id, owned, items);
  // Any pair past the basic boots fills the boots slot: whoever chose other boots is not told to buy
  // a second pair.
  const hasBoots = owned.some((id) => isBoots(id, items));
  const steps: PathStep[] = [];
  if (gameTime <= START_WINDOW_SECONDS) for (const id of plan.starters[0]?.ids ?? []) steps.push({ slot: 'Start', itemId: id, owned: has(id) });
  for (const id of plan.boots[0]?.ids ?? []) steps.push({ slot: 'Schuhe', itemId: id, owned: has(id) || hasBoots });
  // Any final support item fills the slot, like any pair fills the boots'.
  const hasSupportUpgrade = owned.some((id) => isSupportUpgrade(id, items));
  for (const id of plan.supportChoice?.ids ?? []) steps.push({ slot: 'Support', itemId: id, owned: has(id) || hasSupportUpgrade });
  for (const id of plan.core[0]?.ids ?? []) steps.push({ slot: 'Kern', itemId: id, owned: has(id) });
  for (const option of plan.late) for (const id of option.ids) steps.push({ slot: 'Spät', itemId: id, owned: has(id) });
  return steps;
}

/** Finished items before the pair of boots where the build file does not count it: one. */
export const BOOTS_AFTER_ITEMS = 1;

/**
 * The order things are bought in, which is not the order the path shows them in: the pair of boots
 * comes after as many finished items as most players of the champion buy before it (the plan's
 * bootsAfterItems, counted by the collector since 11.11.2026) — after the first where the file does
 * not say. The path lists boots first because they are a slot of their own; as the next buy they
 * sent every first recall to a finished pair (Jinx at six minutes with 1.300 gold: Boots of
 * Swiftness, no part of her first item).
 */
export function buyOrder(steps: readonly PathStep[], bootsAfter: number | null = null): PathStep[] {
  const boots = steps.filter((s) => s.slot === 'Schuhe');
  const rest = steps.filter((s) => s.slot !== 'Schuhe');
  const isItem = (s: PathStep) => s.slot === 'Kern' || s.slot === 'Spät';
  if (boots.length === 0 || !rest.some(isItem)) return [...steps];
  const after = Math.max(0, bootsAfter ?? BOOTS_AFTER_ITEMS);
  // Behind the item with that number; with fewer items in the plan, at the end.
  let at = after === 0 ? rest.findIndex(isItem) : rest.length;
  let seen = 0;
  for (let i = 0; i < rest.length && after > 0; i++) {
    if (isItem(rest[i]!) && ++seen === after) {
      at = i + 1;
      break;
    }
  }
  return [...rest.slice(0, at), ...boots, ...rest.slice(at)];
}

/**
 * The most expensive part of an item that the gold buys and that is not there yet — recursively, so
 * a half-built part is offered rather than its parent, and by instance: of two Long Swords in a
 * recipe, one owned leaves one to buy.
 */
function affordablePart(target: number, inventory: readonly number[], gold: number, items: ReadonlyMap<number, ItemInfo>): number | null {
  let best: { id: number; cost: number } | null = null;
  const pool = [...inventory];
  const visit = (id: number, depth: number) => {
    if (depth > 6) return;
    for (const part of items.get(id)?.from ?? []) {
      const at = pool.indexOf(part);
      if (at >= 0) {
        pool.splice(at, 1);
        continue;
      }
      if (!items.has(part)) continue;
      const price = remainingCost(part, [...pool], items);
      if (price <= gold && (!best || price > best.cost)) best = { id: part, cost: price };
      visit(part, depth + 1);
    }
  };
  visit(target, 0);
  const chosen = best as { id: number; cost: number } | null;
  return chosen ? chosen.id : null;
}

/**
 * The shopping list for the gold at hand. Step by step: the next item of the buy order if the gold
 * covers it, else its most expensive affordable part, then on with what is left — a recall with 650
 * gold is a Long Sword and Boots, not one of them and 300 gold carried back to lane.
 *
 * When the basic Boots join the list is the plan's bootsWorth, counted by the collector:
 * - `bootsDue` true: the items held are worth as much as when half the champion's players have
 *   bought their first boots. Then Boots come first, before any part — at this point of the build
 *   players have them.
 * - false: not yet. Gold that buys nothing else stays unspent.
 * - null: the file does not say (before 11.11.2026, or too few games). Then Boots are bought with
 *   what is left once no part fits — a convention, the way the fixture game's players shop.
 * Never before the starting items, and never on top of boots of any kind.
 */
function basketOf(order: readonly PathStep[], owned: readonly number[], gold: number, items: ReadonlyMap<number, ItemInfo>, bootsDue: boolean | null): Buy[] {
  const basket: Buy[] = [];
  let inventory = [...owned];
  let left = gold;
  const bootsStep = order.find((s) => s.slot === 'Schuhe');
  const basicBoots = items.get(BASIC_BOOTS)?.total ?? 0;
  const done = (s: PathStep) => ownsItem(s.itemId, inventory, items) || (s.slot === 'Schuhe' && inventory.some((id) => isBoots(id, items)));
  const take = (itemId: number, towards: number | null) => {
    const after = [...inventory];
    const cost = remainingCost(itemId, after, items);
    after.push(itemId);
    inventory = after;
    left -= cost;
    basket.push({ itemId, cost, towards });
  };
  /** Basic Boots fit: the plan has boots, none are there, the start is bought and the gold covers them. */
  const bootsFit = (target: PathStep | undefined) =>
    !!bootsStep && target?.slot !== 'Start' && basicBoots > 0 && left >= basicBoots && !inventory.some((id) => id === BASIC_BOOTS || isBoots(id, items));

  // A dozen buys are more than six slots hold; the bound only keeps a broken recipe from looping.
  for (let round = 0; round < 12; round++) {
    const target = order.find((s) => s.slot !== 'Support' && !done(s));
    if (bootsDue === true && bootsFit(target)) {
      take(BASIC_BOOTS, bootsStep!.itemId);
      continue;
    }
    if (target && items.has(target.itemId)) {
      if (remainingCost(target.itemId, [...inventory], items) <= left) {
        take(target.itemId, null);
        continue;
      }
      const part = affordablePart(target.itemId, inventory, left, items);
      if (part !== null) {
        take(part, target.itemId);
        continue;
      }
    }
    // Nothing of the plan fits any more.
    if (bootsDue === null && bootsFit(target)) take(BASIC_BOOTS, bootsStep!.itemId);
    break;
  }
  return basket;
}

function nextBuyOf(order: readonly PathStep[], owned: readonly number[], basket: readonly Buy[], items: ReadonlyMap<number, ItemInfo>): NextBuy | null {
  // The support upgrade can be picked only once the quest is done (Bounty of Worlds in the
  // inventory) — then it comes first, at no cost; before that it is no buy at all.
  const questDone = owned.includes(SUPPORT_QUEST_DONE);
  const choice = order.find((s) => s.slot === 'Support' && !s.owned);
  if (choice && questDone) return { itemId: choice.itemId, cost: remainingCost(choice.itemId, [...owned], items), affordable: true, towards: null, supportChoice: true };
  // The first buy of the list, or the target the gold does not reach yet.
  const first = basket[0];
  if (first) return { itemId: first.itemId, cost: first.cost, affordable: true, towards: first.towards, supportChoice: false };
  const target = order.find((s) => !s.owned && s.slot !== 'Support');
  if (!target) return null;
  return { itemId: target.itemId, cost: remainingCost(target.itemId, [...owned], items), affordable: false, towards: null, supportChoice: false };
}

function nextSkillOf(order: string, data: LiveGameData): GameModel['nextSkill'] {
  const abilities = data.activePlayer?.abilities ?? {};
  const current: Record<Skill, number> = { Q: 0, W: 0, E: 0, R: 0 };
  for (const key of ['Q', 'W', 'E', 'R'] as const) current[key] = abilities[key]?.abilityLevel ?? 0;
  const spent = current.Q + current.W + current.E + current.R;
  const level = data.activePlayer?.level ?? 0;

  const planned: Record<Skill, number> = { Q: 0, W: 0, E: 0, R: 0 };
  for (let i = 0; i < order.length && i < 18; i++) {
    const key = order[i] as Skill;
    if (!(key in planned)) continue;
    planned[key]++;
    // The first point the plan has that the game does not: that is the next one to take.
    if (planned[key] > current[key]) return { key, level: i + 1, now: level > spent };
  }
  return null;
}

/** Physical / magic share of the enemy line-up, from each champion's damage type. */
function damageShares(enemies: EnemyView[]): { physical: number; magic: number } {
  let physical = 0;
  let magic = 0;
  let known = 0;
  for (const enemy of enemies) {
    if (enemy.damage === DamageType.Physical) physical += 1;
    else if (enemy.damage === DamageType.Magic) magic += 1;
    else if (enemy.damage === DamageType.Mixed) {
      physical += 0.5;
      magic += 0.5;
    } else continue;
    known++;
  }
  return known === 0 ? { physical: 0, magic: 0 } : { physical: physical / known, magic: magic / known };
}

export function gameModel(
  data: LiveGameData,
  meta: MetaLookup,
  plan: RiotBuildPlan | null,
  items: ReadonlyMap<number, ItemInfo>,
): GameModel | null {
  const me = ownRow(data);
  if (!me || !data.activePlayer) return null;

  const gameTime = data.gameData?.gameTime ?? 0;
  const gold = Math.floor(data.activePlayer.currentGold ?? 0);
  const owned = ownedItems(me);
  const steps = pathOf(plan, owned, gameTime, items);
  const order = buyOrder(steps, plan?.bootsAfterItems ?? null);
  const bootsWorth = plan?.bootsWorth ?? null;
  const basket = basketOf(order, owned, gold, items, bootsWorth === null ? null : itemWorth(owned, items) >= bootsWorth);

  const enemies: EnemyView[] = (data.allPlayers ?? [])
    .filter((p) => p.team && me.team && p.team !== me.team)
    .map((p) => {
      const championId = championIdOf(p, meta);
      return {
        championId,
        items: (p.items ?? []).map((i) => i.itemID).filter((id) => id > 0),
        damage: meta.champion(championId)?.damage ?? DamageType.Unknown,
      };
    });
  const shares = damageShares(enemies);

  return {
    gameTime,
    level: data.activePlayer.level ?? me.level ?? 0,
    gold,
    championId: championIdOf(me, meta),
    path: steps,
    nextBuy: nextBuyOf(order, owned, basket, items),
    basket,
    goldLeft: gold - basket.reduce((sum, buy) => sum + buy.cost, 0),
    nextSkill: plan ? nextSkillOf(plan.skillOrder, data) : null,
    skillPoints: (['Q', 'W', 'E', 'R'] as const).reduce((n, k) => n + (data.activePlayer?.abilities?.[k]?.abilityLevel ?? 0), 0),
    enemies,
    enemyPhysical: shares.physical,
    enemyMagic: shares.magic,
  };
}
