// B4: which champions shred tanks — read from what players actually build, not judged. A champion
// counts on a lane when at least half of its core builds there hold an item whose point is damage
// against armour, magic resist or health: percent penetration, resist reduction, damage by maximum or
// current health. Measured on the own build file (09.11.2026, 58.623 Platinum games): 85 of 263
// champion-lanes with 300+ games reach half.

import type { RiotBuildFile } from '../riotBuilds';
import { Lane } from './lane';

/** Item ids (Data Dragon, patch 16.22) of the anti-tank items. */
export const SHRED_ITEMS: ReadonlySet<number> = new Set([
  3036, // Lord Dominik's Regards — armour penetration in percent
  3033, // Mortal Reminder — armour penetration in percent
  6694, // Serylda's Grudge — armour penetration in percent
  3302, // Terminus — penetration stacking per hit
  3135, // Void Staff — magic penetration in percent
  3137, // Cryptbloom — magic penetration in percent
  3071, // Black Cleaver — armour reduction
  8010, // Bloodletter's Curse — magic resist reduction
  8020, // Abyssal Mask — magic resist reduction around the wearer
  6653, // Liandry's Torment — burn by maximum health
  3153, // Blade of the Ruined King — damage by current health
  6692, // Eclipse — damage by maximum health
]);

/** Share of the core builds that must hold a shred item. */
export const SHRED_SHARE = 0.5;

/** Core-build games a champion-lane needs before its share means anything. */
export const SHRED_MINIMUM_GAMES = 30;

export class TankShred {
  private constructor(private readonly pairs: ReadonlySet<string>) {}

  static readonly none = new TankShred(new Set());

  /** From the collector's builds.json; every tier of a champion-lane counted together. */
  static fromBuilds(file: RiotBuildFile | null | undefined): TankShred {
    if (!file || !Array.isArray(file.builds)) return TankShred.none;
    const counts = new Map<string, { games: number; shred: number }>();
    for (const row of file.builds) {
      if (!row || !Array.isArray(row.core)) continue;
      const key = `${row.championId}:${row.lane}`;
      const count = counts.get(key) ?? { games: 0, shred: 0 };
      for (const option of row.core) {
        if (!option || !Array.isArray(option.ids) || !(option.games > 0)) continue;
        count.games += option.games;
        if (option.ids.some((id) => SHRED_ITEMS.has(id))) count.shred += option.games;
      }
      counts.set(key, count);
    }
    const pairs = new Set<string>();
    for (const [key, count] of counts) if (count.games >= SHRED_MINIMUM_GAMES && count.shred >= SHRED_SHARE * count.games) pairs.add(key);
    return new TankShred(pairs);
  }

  get count(): number {
    return this.pairs.size;
  }

  /** On that lane; without a lane, on any lane the champion is counted on. */
  has(championId: number, lane: Lane): boolean {
    if (lane !== Lane.Unknown) return this.pairs.has(`${championId}:${lane}`);
    for (const key of this.pairs) if (key.startsWith(`${championId}:`)) return true;
    return false;
  }
}
