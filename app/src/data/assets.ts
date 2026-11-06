// Images and names from Data Dragon. The UI asks for "the picture of item 3031" and never builds a
// URL itself: in the browser the answer is the CDN, in the Tauri shell a file from the local cache
// (as 1.5 keeps it under %LOCALAPPDATA%\DraftPilot\data) — one place decides.

const CDN = 'https://ddragon.leagueoflegends.com/cdn';

export interface AssetNames {
  version: string;
  champions: Map<number, { key: string; name: string }>;
  items: Map<number, string>;
  spells: Map<number, { key: string; name: string }>;
  runes: Map<number, { name: string; icon: string }>;
}

let loaded: Promise<AssetNames> | null = null;
let current: AssetNames | null = null;

/** Loads the localised tables once per session. */
export function loadAssets(version: string, language = 'de_DE'): Promise<AssetNames> {
  loaded ??= (async () => {
    const get = async (file: string) => (await fetch(`${CDN}/${version}/data/${language}/${file}`)).json();
    const [champions, items, spells, runes] = await Promise.all([
      get('champion.json'),
      get('item.json'),
      get('summoner.json'),
      get('runesReforged.json'),
    ]);

    const names: AssetNames = {
      version,
      champions: new Map(),
      items: new Map(),
      spells: new Map(),
      runes: new Map(),
    };

    for (const c of Object.values(champions.data) as { key: string; id: string; name: string }[])
      names.champions.set(Number(c.key), { key: c.id, name: c.name });
    for (const [id, item] of Object.entries(items.data) as [string, { name: string }][])
      names.items.set(Number(id), item.name);
    for (const s of Object.values(spells.data) as { key: string; id: string; name: string }[])
      names.spells.set(Number(s.key), { key: s.id, name: s.name });
    for (const style of runes as { id: number; name: string; icon: string; slots: { runes: { id: number; name: string; icon: string }[] }[] }[]) {
      names.runes.set(style.id, { name: style.name, icon: style.icon });
      for (const slot of style.slots)
        for (const rune of slot.runes) names.runes.set(rune.id, { name: rune.name, icon: rune.icon });
    }

    current = names;
    return names;
  })();
  return loaded;
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
