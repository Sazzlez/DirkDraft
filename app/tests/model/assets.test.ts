// Data Dragon's tables (data/assets.ts): loaded for the version that is wanted now, kept per
// version, and tried again after a failure. Until 17.11.2026 there was one load per run: an offline
// start left the names empty until the next start, and after the client went from 16.22 to 16.23
// the app still said 16.22.1.

import { afterEach, describe, expect, it } from 'vitest';
import { buyable, buyablePlan, MAP_ARAM, MAP_RIFT, type ItemInfo } from '../../src/core/game';
import { Lane } from '../../src/core/model/lane';
import type { PlanOption, RiotBuildPlan } from '../../src/core/riotBuilds';
import { assets, itemName, loadAssets, pickVersion, setAssetSource, type AssetSource, type AssetTables } from '../../src/data/assets';

/** The tables of a version: one champion, three items, as Data Dragon writes them. */
const tables = (version: string): AssetTables => ({
  version,
  champion: { data: { Ahri: { key: '103', id: 'Ahri', name: 'Ahri' } } },
  item: {
    data: {
      '3031': { name: `Klinge der Unendlichkeit (${version})`, gold: { total: 3500, purchasable: true }, maps: { '11': true, '12': true, '30': false }, from: ['1038'] },
      '3181': { name: 'Wächterhorn', gold: { total: 950, purchasable: true }, maps: { '11': false, '12': true } },
      '7000': { name: 'Ornn-Aufwertung', gold: { total: 0, purchasable: true }, inStore: false, maps: { '11': true, '12': true } },
      '3599': { name: 'Nicht zu kaufen', gold: { total: 0, purchasable: false }, maps: { '11': true, '12': true } },
    },
  },
  summoner: { data: { SummonerFlash: { key: '4', id: 'SummonerFlash', name: 'Blitz' } } },
  runes: [{ id: 8000, name: 'Präzision', icon: 'a.png', slots: [{ runes: [{ id: 8005, name: 'Angriffsserie', icon: 'b.png' }] }] }],
});

/** A source that answers from a list of versions and counts what it was asked. */
function source(versions: string[]): { load: AssetSource; asked: string[]; online: boolean; standIn: string | null } {
  const state = {
    asked: [] as string[],
    online: true,
    /** Offline with a copy on disk: the shell hands out this version whatever was asked. */
    standIn: null as string | null,
    load: (async (wanted) => {
      state.asked.push(wanted);
      if (!state.online) {
        if (state.standIn) return tables(state.standIn);
        throw new Error('Data Dragon nicht erreichbar und keine Kopie');
      }
      const version = pickVersion(wanted, versions);
      if (!version) throw new Error('keine Version');
      return tables(version);
    }) as AssetSource,
  };
  return state;
}

afterEach(() => setAssetSource(null));

describe('the version to load', () => {
  it('is the very one, else the newest of its patch, else the newest of all', () => {
    const versions = ['16.23.2', '16.23.1', '16.22.1'];
    expect(pickVersion('16.23.1', versions)).toBe('16.23.1');
    expect(pickVersion('16.22', versions)).toBe('16.22.1');
    expect(pickVersion('16.23.455.1234', versions)).toBe('16.23.2');
    expect(pickVersion('16.24.1', versions)).toBe('16.23.2');
    expect(pickVersion('16.22', [])).toBeNull();
  });
});

describe('loading the tables', () => {
  it('tries again after a start without a network', async () => {
    const s = source(['16.22.1']);
    setAssetSource(s.load);

    s.online = false;
    await expect(loadAssets('16.22')).rejects.toThrow();
    expect(assets()).toBeNull();
    expect(itemName(3031)).toBe('#3031');

    // The next occasion — the data check half an hour later — asks again, and now there is a network.
    s.online = true;
    const names = await loadAssets('16.22');
    expect(names.version).toBe('16.22.1');
    expect(assets()).toBe(names);
    expect(itemName(3031)).toBe('Klinge der Unendlichkeit (16.22.1)');
    expect(s.asked).toEqual(['16.22', '16.22']);
  });

  it('loads a version once, however often it is asked for', async () => {
    const s = source(['16.22.1']);
    setAssetSource(s.load);
    const [a, b] = await Promise.all([loadAssets('16.22'), loadAssets('16.22')]);
    await loadAssets('16.22');
    expect(a).toBe(b);
    expect(s.asked).toEqual(['16.22']);
  });

  it("loads the new version when the client's patch changes", async () => {
    const s = source(['16.23.1', '16.22.1']);
    setAssetSource(s.load);

    await loadAssets('16.22.1');
    expect(assets()?.version).toBe('16.22.1');

    // The client patched: its version is what is wanted now.
    await loadAssets('16.23.1');
    expect(assets()?.version).toBe('16.23.1');
    expect(itemName(3031)).toBe('Klinge der Unendlichkeit (16.23.1)');

    // And back (another account on an older client): kept, not fetched again.
    await loadAssets('16.22.1');
    expect(assets()?.version).toBe('16.22.1');
    expect(s.asked).toEqual(['16.22.1', '16.23.1']);
  });

  it('uses an older kept copy while offline, and asks again for the wanted patch', async () => {
    const s = source(['16.23.1', '16.22.1']);
    setAssetSource(s.load);
    s.online = false;
    s.standIn = '16.22.1';

    // Names rather than none — but not the end of it.
    expect((await loadAssets('16.23.1')).version).toBe('16.22.1');
    expect(assets()?.version).toBe('16.22.1');

    s.online = true;
    expect((await loadAssets('16.23.1')).version).toBe('16.23.1');
    expect(assets()?.version).toBe('16.23.1');
    expect(s.asked).toEqual(['16.23.1', '16.23.1']);
  });

  it('lets the newest request decide, whichever answer comes last', async () => {
    let releaseOld!: () => void;
    const held = new Promise<void>((resolve) => (releaseOld = resolve));
    setAssetSource(async (wanted) => {
      if (wanted === '16.22.1') await held;
      return tables(wanted);
    });

    const old = loadAssets('16.22.1');
    await loadAssets('16.23.1');
    releaseOld();
    await old;
    expect(assets()?.version).toBe('16.23.1');
  });
});

describe('what may be suggested as a buy', () => {
  const catalog = async (): Promise<ReadonlyMap<number, ItemInfo>> => {
    setAssetSource(source(['16.22.1']).load);
    return (await loadAssets('16.22')).itemInfo;
  };

  it('reads from the catalog where an item is sold', async () => {
    const items = await catalog();
    expect(items.get(3031)).toMatchObject({ total: 3500, purchasable: true, maps: [11, 12], from: [1038] });
    expect(items.get(3181)?.maps).toEqual([12]);
    // Kept out of the shop, or not for sale at all.
    expect(items.get(7000)?.purchasable).toBe(false);
    expect(items.get(3599)?.purchasable).toBe(false);
  });

  it('drops an item the catalog does not know or the map does not sell', async () => {
    const items = await catalog();
    expect(buyable(3031, items)).toBe(true);
    // Counted on the data's patch, gone from the client's.
    expect(buyable(9999, items)).toBe(false);
    expect(buyable(7000, items)).toBe(false);
    expect(buyable(3599, items)).toBe(false);
    // The Howling Abyss sells it, the Rift does not.
    expect(buyable(3181, items, MAP_RIFT)).toBe(false);
    expect(buyable(3181, items, MAP_ARAM)).toBe(true);
  });

  it('filters nothing without a catalog', () => {
    const none = new Map<number, ItemInfo>();
    expect(buyable(9999, none)).toBe(true);
    expect(buyable(3181, none, MAP_RIFT)).toBe(true);
  });

  it('takes such items out of the plan, and leaves a plan alone that has none', async () => {
    const items = await catalog();
    const option = (ids: number[]): PlanOption => ({ ids, text: null, games: 100, winRate: 0.5 });
    const plan: RiotBuildPlan = {
      championId: 103,
      lane: Lane.Mid,
      inPlaceOf: null,
      patch: '16.21',
      games: 1000,
      winRate: 0.5,
      runes: null,
      spells: null,
      starters: [option([3031])],
      boots: [option([9999]), option([3031])],
      core: [option([3031, 9999, 3181])],
      late: [option([3181]), option([3031])],
      supportChoice: null,
      supportOptions: [],
      skillOrder: '',
      skillMax: '',
      bootsAfterItems: null,
      bootsWorth: null,
    };

    const rift = buyablePlan(plan, items, MAP_RIFT);
    expect(rift.boots.map((o) => o.ids)).toEqual([[3031]]);
    expect(rift.core.map((o) => o.ids)).toEqual([[3031]]);
    expect(rift.late.map((o) => o.ids)).toEqual([[3031]]);
    // On the Howling Abyss the horn stays.
    expect(buyablePlan(plan, items, MAP_ARAM).core.map((o) => o.ids)).toEqual([[3031, 3181]]);

    // Nothing to drop, or nothing to judge by: the very same plan.
    const clean = { ...plan, boots: [option([3031])], core: [option([3031])], late: [option([3031])] };
    expect(buyablePlan(clean, items, MAP_RIFT)).toBe(clean);
    expect(buyablePlan(plan, new Map(), MAP_RIFT)).toBe(plan);
  });
});
