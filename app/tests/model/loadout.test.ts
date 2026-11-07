// Summoner spells on import: slots follow the player's habit, never the data's order.

import { describe, expect, it } from 'vitest';
import { FLASH, flashSlotOf, importLabel, spellSlots } from '../../src/core/loadout';

const IGNITE = 14;
const TELEPORT = 12;
const GHOST = 6;
const HEAL = 7;

describe('spellSlots', () => {
  it('changes nothing when the pair is already chosen, in either order', () => {
    expect(spellSlots([FLASH, IGNITE], [FLASH, IGNITE])).toBeNull();
    expect(spellSlots([FLASH, IGNITE], [IGNITE, FLASH])).toBeNull();
  });

  it('keeps Flash on F when the player has it there', () => {
    expect(spellSlots([FLASH, IGNITE], [TELEPORT, FLASH])).toEqual([IGNITE, FLASH]);
  });

  it('keeps Flash on D when the player has it there', () => {
    expect(spellSlots([IGNITE, FLASH], [FLASH, HEAL])).toEqual([FLASH, IGNITE]);
  });

  it('keeps any spell already chosen in its slot', () => {
    expect(spellSlots([FLASH, TELEPORT], [GHOST, TELEPORT])).toEqual([FLASH, TELEPORT]);
    expect(spellSlots([FLASH, TELEPORT], [TELEPORT, GHOST])).toEqual([TELEPORT, FLASH]);
  });

  it('puts Flash where the player last had it when the current pair has none', () => {
    expect(spellSlots([FLASH, IGNITE], [HEAL, GHOST], 1)).toEqual([IGNITE, FLASH]);
    expect(spellSlots([IGNITE, FLASH], [HEAL, GHOST], 0)).toEqual([FLASH, IGNITE]);
  });

  it("falls back to the data's order without any hint", () => {
    expect(spellSlots([FLASH, IGNITE], [HEAL, GHOST])).toEqual([FLASH, IGNITE]);
    expect(spellSlots([GHOST, IGNITE], [HEAL, TELEPORT], 1)).toEqual([GHOST, IGNITE]);
  });

  it('writes nothing for an unclear plan', () => {
    expect(spellSlots([], [HEAL, GHOST])).toBeNull();
    expect(spellSlots([FLASH], [HEAL, GHOST])).toBeNull();
    expect(spellSlots([FLASH, FLASH], [HEAL, GHOST])).toBeNull();
    expect(spellSlots([0, FLASH], [HEAL, GHOST])).toBeNull();
  });
});

describe('flashSlotOf', () => {
  it('finds Flash on D, on F or nowhere', () => {
    expect(flashSlotOf([FLASH, IGNITE])).toBe(0);
    expect(flashSlotOf([IGNITE, FLASH])).toBe(1);
    expect(flashSlotOf([IGNITE, GHOST])).toBeNull();
  });
});

describe('importLabel', () => {
  it('names what the click writes', () => {
    expect(importLabel(true, true)).toBe('Runen und Zauber übernehmen');
    expect(importLabel(true, false)).toBe('Runen übernehmen');
    expect(importLabel(false, true)).toBe('Zauber übernehmen');
  });
});
