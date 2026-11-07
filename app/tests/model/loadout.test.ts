// Summoner spells on import: Flash always on the player's Flash key (F by default), other spells
// stay where the player put them.

import { describe, expect, it } from 'vitest';
import { FLASH, importLabel, spellSlots } from '../../src/core/loadout';

const IGNITE = 14;
const TELEPORT = 12;
const GHOST = 6;
const HEAL = 7;

describe('spellSlots', () => {
  it('puts Flash on F by default, whatever the order of the data', () => {
    expect(spellSlots([FLASH, HEAL], [HEAL, GHOST])).toEqual([HEAL, FLASH]);
    expect(spellSlots([IGNITE, FLASH], [HEAL, GHOST])).toEqual([IGNITE, FLASH]);
  });

  it('moves Flash from D to F', () => {
    expect(spellSlots([FLASH, HEAL], [FLASH, HEAL])).toEqual([HEAL, FLASH]);
    expect(spellSlots([FLASH, IGNITE], [FLASH, TELEPORT])).toEqual([IGNITE, FLASH]);
  });

  it('changes nothing when Flash is already on F with the planned partner', () => {
    expect(spellSlots([FLASH, IGNITE], [IGNITE, FLASH])).toBeNull();
  });

  it('follows another Flash key when the player set one', () => {
    expect(spellSlots([HEAL, FLASH], [GHOST, FLASH], 0)).toEqual([FLASH, HEAL]);
    expect(spellSlots([FLASH, HEAL], [FLASH, HEAL], 0)).toBeNull();
  });

  it('keeps any other spell already chosen in its slot', () => {
    expect(spellSlots([GHOST, TELEPORT], [IGNITE, TELEPORT])).toEqual([GHOST, TELEPORT]);
    expect(spellSlots([GHOST, TELEPORT], [TELEPORT, IGNITE])).toEqual([TELEPORT, GHOST]);
  });

  it('leaves a pair without Flash alone when both are chosen, in either order', () => {
    expect(spellSlots([GHOST, IGNITE], [GHOST, IGNITE])).toBeNull();
    expect(spellSlots([GHOST, IGNITE], [IGNITE, GHOST])).toBeNull();
  });

  it("falls back to the data's order without Flash and without a match", () => {
    expect(spellSlots([GHOST, IGNITE], [HEAL, TELEPORT])).toEqual([GHOST, IGNITE]);
  });

  it('writes nothing for an unclear plan', () => {
    expect(spellSlots([], [HEAL, GHOST])).toBeNull();
    expect(spellSlots([FLASH], [HEAL, GHOST])).toBeNull();
    expect(spellSlots([FLASH, FLASH], [HEAL, GHOST])).toBeNull();
    expect(spellSlots([0, FLASH], [HEAL, GHOST])).toBeNull();
  });
});

describe('importLabel', () => {
  it('names what the click writes', () => {
    expect(importLabel(true, true)).toBe('Runen und Zauber übernehmen');
    expect(importLabel(true, false)).toBe('Runen übernehmen');
    expect(importLabel(false, true)).toBe('Zauber übernehmen');
  });
});
