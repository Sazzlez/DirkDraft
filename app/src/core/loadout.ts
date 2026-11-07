// What one click on "Runen und Zauber übernehmen" writes into the client besides the rune page:
// the two summoner spells. Writes happen only on that click, never on their own.
//
// Players keep Flash on one key (D or F) by habit, and a tool that swaps it costs them a Flash in
// the next fight. So the slots follow the player, not the data: a spell already chosen stays in its
// slot, Flash goes where the player last had it, and only then does the data's order decide.

export const FLASH = 4;

export type SpellPair = readonly [number, number];

/**
 * The two spell ids to write (slot D, slot F), or null when nothing is to change: the plan has no
 * clear pair, or the player already has exactly these two (in whatever order they put them).
 *
 * @param flashSlot where the player had Flash the last time we saw it (0 = D, 1 = F); used when the
 *   current selection has no Flash to go by.
 */
export function spellSlots(planned: readonly number[], current: SpellPair, flashSlot: 0 | 1 | null = null): SpellPair | null {
  const [a, b] = planned;
  if (planned.length < 2 || !a || !b || a === b) return null;

  const [d, f] = current;
  const same = (a === d && b === f) || (a === f && b === d);
  if (same) return null;

  // One of the two already chosen: it stays where it is, the other takes the free slot.
  if (d === a || d === b) return [d, d === a ? b : a];
  if (f === a || f === b) return [f === a ? b : a, f];

  if ((a === FLASH || b === FLASH) && flashSlot !== null) {
    const other = a === FLASH ? b : a;
    return flashSlot === 0 ? [FLASH, other] : [other, FLASH];
  }

  return [a, b];
}

/** The slot Flash sits in, when it is chosen: the hint `spellSlots` needs for the next time. */
export function flashSlotOf(current: SpellPair): 0 | 1 | null {
  if (current[0] === FLASH) return 0;
  if (current[1] === FLASH) return 1;
  return null;
}

/** The button's words, after what the click will write. */
export function importLabel(hasRunes: boolean, hasSpells: boolean): string {
  if (hasRunes && hasSpells) return 'Runen und Zauber übernehmen';
  if (hasSpells) return 'Zauber übernehmen';
  return 'Runen übernehmen';
}
