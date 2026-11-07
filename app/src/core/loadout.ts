// What one click on "Runen und Zauber übernehmen" writes into the client besides the rune page:
// the two summoner spells. Writes happen only on that click, never on their own.
//
// Players keep Flash on one key by habit, and a tool that moves it costs them a Flash in the next
// fight. So Flash always goes on the player's Flash key — F unless set otherwise (user's rule,
// 07.11.2026: "Blitz immer auf F") — even if it sat on the other key before. Any other spell already
// chosen stays where it is; only then does the data's order decide.

export const FLASH = 4;

export type SpellPair = readonly [number, number];

/** 0 = D, 1 = F. */
export type SpellSlot = 0 | 1;

/** Flash's key unless the player chose another. */
export const DEFAULT_FLASH_SLOT: SpellSlot = 1;

/**
 * The two spell ids to write (slot D, slot F), or null when nothing is to change: the plan has no
 * clear pair, or the player already has exactly these two in these slots.
 */
export function spellSlots(planned: readonly number[], current: SpellPair, flashSlot: SpellSlot = DEFAULT_FLASH_SLOT): SpellPair | null {
  const [a, b] = planned;
  if (planned.length < 2 || !a || !b || a === b) return null;

  const [d, f] = current;
  let next: SpellPair;

  if (a === FLASH || b === FLASH) {
    const other = a === FLASH ? b : a;
    next = flashSlot === 0 ? [FLASH, other] : [other, FLASH];
  } else if ((a === d && b === f) || (a === f && b === d)) {
    // Both already chosen, in whatever order the player put them.
    return null;
  } else if (d === a || d === b) {
    next = [d, d === a ? b : a];
  } else if (f === a || f === b) {
    next = [f === a ? b : a, f];
  } else {
    next = [a, b];
  }

  return next[0] === d && next[1] === f ? null : next;
}

/** The button's words, after what the click will write. */
export function importLabel(hasRunes: boolean, hasSpells: boolean): string {
  if (hasRunes && hasSpells) return 'Runen und Zauber übernehmen';
  if (hasSpells) return 'Zauber übernehmen';
  return 'Runen übernehmen';
}
