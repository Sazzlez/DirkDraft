// Port of DraftPilot.Core.Data.ChampionTraits / TraitTable (1.5): the judgement-only traits no API
// reports (engage, peel, cc, scaling, waveclear; since 2.1 poke, split, pick), keyed by Data Dragon
// key, case-insensitive.

import bundled from './data/champion_traits.json';

export type ScalingCurve = 'unknown' | 'early' | 'mid' | 'late';

export interface ChampionTraits {
  engage: number;
  peel: number;
  cc: number;
  scaling: ScalingCurve;
  waveclear: number;
  /** 2.1, only where the profile is clear; 0 = neutral. */
  poke: number;
  split: number;
  pick: number;
  isKnown: boolean;
}

/** What a table entry is built from; the 2.1 fields may be left out. */
export type TraitInput = Omit<ChampionTraits, 'isKnown' | 'poke' | 'split' | 'pick'> & Partial<Pick<ChampionTraits, 'poke' | 'split' | 'pick'>>;

export const UNKNOWN_TRAITS: ChampionTraits = Object.freeze({
  engage: 0,
  peel: 0,
  cc: 0,
  scaling: 'unknown',
  waveclear: 0,
  poke: 0,
  split: 0,
  pick: 0,
  isKnown: false,
});

export const hasHardEngage = (t: ChampionTraits): boolean => t.isKnown && t.engage >= 2;
export const hasPeel = (t: ChampionTraits): boolean => t.isKnown && t.peel >= 2;
export const hasHardCc = (t: ChampionTraits): boolean => t.isKnown && t.cc >= 2;

export class TraitTable {
  private constructor(private readonly byKey: ReadonlyMap<string, ChampionTraits>) {}

  static readonly empty = new TraitTable(new Map());

  get count(): number {
    return this.byKey.size;
  }

  static fromEntries(entries: Iterable<[string, TraitInput]>): TraitTable {
    const map = new Map<string, ChampionTraits>();
    for (const [key, traits] of entries) map.set(key.toLowerCase(), { poke: 0, split: 0, pick: 0, ...traits, isKnown: true });
    return new TraitTable(map);
  }

  /** Reads champion_traits.json; anything malformed reads as empty, or as 0 for that one field. */
  static fromJson(document: unknown): TraitTable {
    const champions = (document as { champions?: unknown } | null)?.champions;
    if (champions === null || typeof champions !== 'object' || Array.isArray(champions)) return TraitTable.empty;
    const map = new Map<string, ChampionTraits>();
    for (const [name, value] of Object.entries(champions as Record<string, unknown>)) {
      if (value === null || typeof value !== 'object' || Array.isArray(value)) continue;
      const entry = value as Record<string, unknown>;
      map.set(name.toLowerCase(), {
        engage: readInt(entry.engage),
        peel: readInt(entry.peel),
        cc: readInt(entry.cc),
        scaling: readScaling(entry.scaling),
        waveclear: readInt(entry.waveclear),
        poke: readInt(entry.poke),
        split: readInt(entry.split),
        pick: readInt(entry.pick),
        isKnown: true,
      });
    }
    return new TraitTable(map);
  }

  static bundled(): TraitTable {
    return TraitTable.fromJson(bundled);
  }

  for(championKey: string | null | undefined): ChampionTraits {
    if (!championKey) return UNKNOWN_TRAITS;
    return this.byKey.get(championKey.toLowerCase()) ?? UNKNOWN_TRAITS;
  }
}

// TryGetInt32: "2.5" in a hand-edited file degrades to 0 rather than throwing.
function readInt(value: unknown): number {
  if (typeof value !== 'number' || !Number.isInteger(value) || Math.abs(value) > 2147483647) return 0;
  return Math.min(3, Math.max(0, value));
}

function readScaling(value: unknown): ScalingCurve {
  if (typeof value !== 'string') return 'unknown';
  switch (value.toLowerCase()) {
    case 'early':
      return 'early';
    case 'mid':
      return 'mid';
    case 'late':
      return 'late';
    default:
      return 'unknown';
  }
}
