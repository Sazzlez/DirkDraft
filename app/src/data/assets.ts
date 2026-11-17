// Images and names from Data Dragon. The UI asks for "the picture of item 3031" and never builds a
// URL itself: one place decides.
//
// The tables (names, prices, recipes, runes) come through a source: in the browser (preview,
// screenshots) straight from the CDN, in the shell from the shell's own loader (ddragon.rs), which
// keeps every version on disk — a start without a network still has names. They are loaded for the
// version that is wanted now (the client's own patch, else the data's) and kept per version; a load
// that failed, or got an older stand-in, is tried again at the next occasion (the data check, a new
// patch of the client). Until 17.11.2026 there was one load per run: an offline start left the names
// empty until the next start, and a client that patched kept the old tables.

import type { ItemInfo } from '../core/game';

const CDN = 'https://ddragon.leagueoflegends.com/cdn';

export interface AssetNames {
  version: string;
  champions: Map<number, { key: string; name: string }>;
  items: Map<number, string>;
  /** Price, recipe and situation stats per item, for the game view. */
  itemInfo: Map<number, ItemInfo>;
  spells: Map<number, { key: string; name: string }>;
  runes: Map<number, { name: string; icon: string }>;
}

/** The four tables of one Data Dragon version, parsed. */
export interface AssetTables {
  /** The version they are of — not always the one asked for. */
  version: string;
  champion: unknown;
  item: unknown;
  summoner: unknown;
  runes: unknown;
}

/** Where the tables come from: `wanted` is a version ("16.23.1"), a client build or a patch ("16.22"). */
export type AssetSource = (wanted: string, language: string) => Promise<AssetTables>;

/** "16.23" out of "16.23.1" and "16.23.455.1234". */
const line = (version: string): string => version.split('.').slice(0, 2).join('.');

/**
 * The version to load for what is wanted, out of Data Dragon's list (newest first): the very one,
 * else the newest of its patch, else the newest of all (ddragon.rs pick_version).
 */
export function pickVersion(wanted: string, versions: readonly string[]): string | null {
  if (versions.includes(wanted)) return wanted;
  return versions.find((v) => line(v) === line(wanted)) ?? versions[0] ?? null;
}

/** The browser's source: the CDN itself. */
const fromCdn: AssetSource = async (wanted, language) => {
  const json = async (url: string): Promise<unknown> => {
    const response = await fetch(url);
    if (!response.ok) throw new Error(`HTTP ${response.status}`);
    return response.json();
  };
  const versions = (await json('https://ddragon.leagueoflegends.com/api/versions.json')) as string[];
  const version = pickVersion(wanted, versions);
  if (!version) throw new Error('keine Version');
  const [champion, item, summoner, runes] = await Promise.all(
    ['champion.json', 'item.json', 'summoner.json', 'runesReforged.json'].map((file) => json(`${CDN}/${version}/data/${language}/${file}`)),
  );
  return { version, champion, item, summoner, runes };
};

let source: AssetSource = fromCdn;
const loads = new Map<string, Promise<AssetNames>>();
let current: AssetNames | null = null;
let latest = '';

/** The shell puts its own loader here (live/bridge.ts); tests a fake one. Forgets what was loaded. */
export function setAssetSource(next: AssetSource | null): void {
  source = next ?? fromCdn;
  loads.clear();
  current = null;
  latest = '';
}

function namesFrom(tables: AssetTables): AssetNames {
  const names: AssetNames = {
    version: tables.version,
    champions: new Map(),
    items: new Map(),
    itemInfo: new Map(),
    spells: new Map(),
    runes: new Map(),
  };

  const data = (table: unknown): Record<string, unknown> => ((table as { data?: Record<string, unknown> } | null)?.data ?? {}) as Record<string, unknown>;

  for (const c of Object.values(data(tables.champion)) as { key: string; id: string; name: string }[])
    names.champions.set(Number(c.key), { key: c.id, name: c.name });
  type RawItem = {
    name: string;
    gold?: { total?: number; purchasable?: boolean };
    from?: string[];
    into?: string[];
    tags?: string[];
    stats?: Record<string, number>;
    specialRecipe?: number;
    inStore?: boolean;
    maps?: Record<string, boolean>;
  };
  for (const [id, item] of Object.entries(data(tables.item)) as [string, RawItem][]) {
    names.items.set(Number(id), item.name);
    names.itemInfo.set(Number(id), {
      total: item.gold?.total ?? 0,
      from: (item.from ?? []).map(Number),
      into: (item.into ?? []).map(Number),
      // Not for sale at all, or kept out of the shop (an Ornn upgrade, a transformed item).
      purchasable: item.gold?.purchasable !== false && item.inStore !== false,
      tags: item.tags ?? [],
      armor: Math.round(item.stats?.FlatArmorMod ?? 0),
      magicResist: Math.round(item.stats?.FlatSpellBlockMod ?? 0),
      specialRecipe: item.specialRecipe ? Number(item.specialRecipe) : undefined,
      maps: item.maps
        ? Object.entries(item.maps)
            .filter(([, sold]) => sold)
            .map(([map]) => Number(map))
        : undefined,
    });
  }
  for (const s of Object.values(data(tables.summoner)) as { key: string; id: string; name: string }[])
    names.spells.set(Number(s.key), { key: s.id, name: s.name });
  const styles = Array.isArray(tables.runes) ? tables.runes : [];
  for (const style of styles as { id: number; name: string; icon: string; slots: { runes: { id: number; name: string; icon: string }[] }[] }[]) {
    names.runes.set(style.id, { name: style.name, icon: style.icon });
    for (const slot of style.slots) for (const rune of slot.runes) names.runes.set(rune.id, { name: rune.name, icon: rune.icon });
  }
  return names;
}

/**
 * The localised tables for the wanted version, loaded once per version. Rejects when nothing could
 * be loaded — and the next call tries again, as does one after a stand-in of another patch (the
 * shell's kept copy while offline). The newest call decides which tables `assets()` hands out.
 */
export function loadAssets(wanted: string, language = 'de_DE'): Promise<AssetNames> {
  const key = `${wanted}|${language}`;
  latest = key;
  const known = loads.get(key);
  if (known) {
    // Already loaded for this version: it is the current one again (the client went back a patch).
    void known.then((names) => {
      if (latest === key) current = names;
    }, () => undefined);
    return known;
  }

  const load = source(wanted, language).then((tables) => {
    const names = namesFrom(tables);
    if (line(names.version) !== line(wanted)) loads.delete(key);
    if (latest === key || current === null) current = names;
    return names;
  });
  loads.set(key, load);
  load.catch(() => {
    if (loads.get(key) === load) loads.delete(key);
  });
  return load;
}

export function assets(): AssetNames | null {
  return current;
}

export function championIcon(key: string): string {
  const version = current?.version ?? '16.22.1';
  return `${CDN}/${version}/img/champion/${key}.png`;
}

export function itemIcon(id: number): string {
  const version = current?.version ?? '16.22.1';
  return `${CDN}/${version}/img/item/${id}.png`;
}

export function spellIcon(id: number): string | null {
  const spell = current?.spells.get(id);
  return spell ? `${CDN}/${current!.version}/img/spell/${spell.key}.png` : null;
}

export function runeIcon(id: number): string | null {
  const rune = current?.runes.get(id);
  return rune ? `https://ddragon.leagueoflegends.com/cdn/img/${rune.icon}` : null;
}

export function itemName(id: number): string {
  return current?.items.get(id) ?? `#${id}`;
}

export function runeName(id: number): string {
  return current?.runes.get(id)?.name ?? `#${id}`;
}

export function spellName(id: number): string {
  return current?.spells.get(id)?.name ?? `#${id}`;
}
