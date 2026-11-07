// Port of tests/DraftPilot.Core.Tests/TraitFileTests.cs.
//
// Guards the shipped champion_traits.json. Champions missing from it silently lose every
// composition check — which is exactly what happened when seven post-release champions were
// absent — so the file itself is under test, not just the code that reads it. The port bundles
// the file at src/core/model/data/, so it is imported rather than located on disk.

import { describe, expect, it } from 'vitest';
import file from '../../src/core/model/data/champion_traits.json';
import { TraitTable } from '../../src/core/model/traits';

const champions = (file as unknown as { champions: Record<string, { scaling?: unknown }> }).champions;

describe('TraitFileTests', () => {
  it('EveryEntryInTheFileParses', () => {
    const table = TraitTable.fromJson(file);

    const declared = Object.keys(champions).length;

    // A malformed entry would be skipped silently; the counts diverging is the tell.
    expect(table.count).toBe(declared);
  });

  it('CoversTheFullCurrentRoster', () => {
    // 173 champions as of patch 16.17. The count only ever grows; shrinking means entries were
    // lost, not that Riot deleted champions.
    const count = TraitTable.fromJson(file).count;
    expect(count, `Nur ${count} Champions — Einträge verloren?`).toBeGreaterThanOrEqual(173);
  });

  it.each(['Belveth', 'Ambessa', 'Mel', 'Aurora', 'Yunara', 'Locke', 'Zaahen'])('RecentChampionsAreKnown(%s)', (key) => {
    // The seven that were missing. IsKnown is what makes the composition rules fire for them.
    expect(TraitTable.fromJson(file).for(key).isKnown).toBe(true);
  });

  it('ScalingValuesAreAllValid', () => {
    for (const [name, entry] of Object.entries(champions)) {
      const scaling = entry.scaling;
      expect(['early', 'mid', 'late'], `${name}: scaling '${String(scaling)}'`).toContain(scaling);
    }
  });
});
