// Ability combos inside a team, as a hint at a pick: why the champion fits the line-up. Amumu's
// ult holds them, Miss Fortune's ult hits them all; a knock-up opens Yasuo's ult; an enchanter
// stands behind a hypercarry.
//
// A hint and nothing more. Measured on 62.227 games that had never seen the lists
// (docs/modellpruefung.md, Messung 28; npm run measure:combos), no combo wins measurably more than
// the lane strengths of its champions say: the wombo +0,1 ± 0,6 points counted plainly. The narrow
// lists shown here say the same on all 207.968 games (wombo +0,2 ± 0,5, a knock-up for Yasuo
// +0,6 ± 0,7, enchanter with hypercarry +0,8 ± 0,8; build/measure-combos-shown.md). So the chip
// says how the comp is meant to be played and adds nothing to the percentage (user's wish,
// 15.11.2026: the reason at the pick, with the champion's name).
//
// The lists are judgement, clear cases only (absent = neutral): build/wombo-entwurf.md. The wide
// ones the user fixed on 15.11.2026 were measured and stay with the measurement; the app shows the
// narrow ones, which the measurement can read as well (--lists shown).
//
// They live in data/combos.json in the repository root (since 17.11.2026; before, in this file):
// built in from there, published by the collector with the numbers and downloaded like them, so a
// new champion gets its tag without a release. The built-in copy stands until the first download.

import bundled from '../../../../data/combos.json';
import { neutral, type Reason } from './reason';

const loose = (text: string): string => text.toLowerCase().replace(/[^a-z]/g, '');

/**
 * The lists the chips are shown from — narrow on purpose (user, 15.11.2026: "Listen enger fassen").
 * With the wide lists the measurement ran on (tools/measure/combos.ts, WIDE) nearly every second team
 * had a wombo and the tag stood at every engage champion; a tag at every row says nothing. Only the
 * kits a comp is built around:
 *   aoeEngage  — an ult that reliably holds several enemies in one place;
 *   aoeDamage  — an area ult that needs enemies standing together to be worth it;
 *   knockUp    — a reliable knock-up over an area: what the ult of Yasuo needs;
 *   hypercarry — decides fights alone once it is fed and protected.
 */
export const COMBO_NAMES = ['aoeEngage', 'aoeDamage', 'knockUp', 'yasuo', 'enchanter', 'hypercarry'] as const;

export type Combo = (typeof COMBO_NAMES)[number];
export type ComboSets = Record<Combo, ReadonlySet<number>>;
export type ComboLists = Readonly<Record<Combo, readonly string[]>>;

/**
 * data/combos.json as the app reads it: the lists, champions by name as Data Dragon spells them
 * (matched loosely: case and punctuation ignored, the key also counts), and the skill an AoE engage
 * holds them with, by loose name — "R" where the file says nothing else.
 */
export interface ComboFile {
  combos: ComboLists;
  engageSkill: Readonly<Record<string, string>>;
}

/** Reads combos.json; null when a list is missing or is no list of names — the copy at hand stays then. */
export function readComboFile(document: unknown): ComboFile | null {
  const file = document as { combos?: unknown; engageSkill?: unknown } | null;
  if (!file || typeof file !== 'object' || !file.combos || typeof file.combos !== 'object' || Array.isArray(file.combos)) return null;
  const lists = file.combos as Record<string, unknown>;
  const combos = {} as Record<Combo, readonly string[]>;
  for (const name of COMBO_NAMES) {
    const names = lists[name];
    if (!Array.isArray(names) || !names.every((entry) => typeof entry === 'string')) return null;
    combos[name] = names as string[];
  }
  const engageSkill: Record<string, string> = {};
  if (file.engageSkill && typeof file.engageSkill === 'object' && !Array.isArray(file.engageSkill)) {
    for (const [name, skill] of Object.entries(file.engageSkill as Record<string, unknown>)) {
      if (typeof skill === 'string' && skill.length > 0) engageSkill[loose(name)] = skill;
    }
  }
  return { combos, engageSkill };
}

/** The copy built into the app, data/combos.json at build time. */
export const BUNDLED_COMBOS: ComboFile = readComboFile(bundled) ?? { combos: { aoeEngage: [], aoeDamage: [], knockUp: [], yasuo: [], enchanter: [], hypercarry: [] }, engageSkill: {} };

/** The built-in lists, for the measurement (tools/measure/combos.ts, --lists shown). */
export const COMBOS: ComboLists = BUNDLED_COMBOS.combos;


interface Named {
  id: number;
  key: string;
  name: string;
}

/** Lists as champion ids — the shown ones unless others are given; names no champion of the roster carries come back as missing. */
export function comboSets(champions: readonly Named[], lists: ComboLists = COMBOS): { sets: ComboSets; missing: string[] } {
  const byName = new Map<string, number>();
  for (const c of champions) {
    byName.set(loose(c.name), c.id);
    byName.set(loose(c.key), c.id);
  }
  const missing: string[] = [];
  const sets = {} as Record<Combo, Set<number>>;
  for (const [combo, names] of Object.entries(lists) as [Combo, readonly string[]][]) {
    sets[combo] = new Set();
    for (const name of names) {
      const id = byName.get(loose(name));
      if (id === undefined) missing.push(name);
      else sets[combo].add(id);
    }
  }
  return { sets, missing };
}

/** How every combo chip starts: the row shows the first reason that begins like this as its tag. */
const COMBO_STARTS = ['Wombo mit ', 'Engage für ', 'Knock-up für ', 'Knock-up von ', 'Enchanter für ', 'Hypercarry mit '];
export const isComboReason = (text: string): boolean => COMBO_STARTS.some((start) => text.startsWith(start));

/** Chips of this kind at one pick; more would bury the reasons that count. */
const MOST = 2;

const NOT_COUNTED = 'Ein Hinweis, wie die Comp gedacht ist — zählt nicht in die Prozentzahl:';

const list = (names: readonly string[]): string => (names.length <= 1 ? (names[0] ?? '') : `${names.slice(0, -1).join(', ')} und ${names[names.length - 1]}`);

export class KitTable {
  private readonly sets: ComboSets;
  private readonly skills: Readonly<Record<string, string>>;
  private readonly names = new Map<number, string>();

  /** `file`: the lists as downloaded with the numbers; the built-in copy without them. */
  constructor(champions: readonly Named[], file: ComboFile = BUNDLED_COMBOS) {
    this.sets = comboSets(champions, file.combos).sets;
    this.skills = file.engageSkill;
    for (const c of champions) this.names.set(c.id, c.name);
  }

  has(combo: Combo, championId: number): boolean {
    return this.sets[combo].has(championId);
  }

  private name(id: number): string {
    return this.names.get(id) ?? '';
  }

  private withSkill(id: number): string {
    const name = this.name(id);
    return `${name} (${this.skills[loose(name)] ?? 'R'})`;
  }

  /** The teammates in a list, the candidate itself left out, at most two by name. */
  private partners(combo: Combo, candidate: number, allies: readonly number[]): number[] {
    return [...new Set(allies)].filter((id) => id !== 0 && id !== candidate && this.has(combo, id)).slice(0, 2);
  }

  /**
   * The combos the candidate would close with the teammates already shown, as neutral reasons —
   * at most two, the wombo first.
   */
  reasons(candidate: number, allies: readonly number[]): Reason[] {
    const found: Reason[] = [];

    const engagers = this.has('aoeDamage', candidate) ? this.partners('aoeEngage', candidate, allies) : [];
    const damagers = this.has('aoeEngage', candidate) ? this.partners('aoeDamage', candidate, allies) : [];
    const wombo = `${NOT_COUNTED} In unseren Spielen gewinnt ein Team mit Wombo nicht messbar öfter (+0,2 ± 0,5 Punkte auf 207.968 Spielen).`;
    if (engagers.length > 0) {
      const holds = engagers.length > 1 ? 'halten' : 'hält';
      found.push(
        neutral(
          `Wombo mit ${list(engagers.map((id) => this.name(id)))}`,
          `${list(engagers.map((id) => this.withSkill(id)))} ${holds} mehrere Gegner an einer Stelle fest (AoE-CC), dieser Pick trifft sie dort alle mit AoE-Schaden. ${wombo}`,
        ),
      );
    } else if (damagers.length > 0) {
      const hits = damagers.length > 1 ? 'treffen' : 'trifft';
      found.push(
        neutral(
          `Engage für ${list(damagers.map((id) => this.name(id)))}`,
          `Dieser Pick hält mit ${this.skills[loose(this.name(candidate))] ?? 'R'} mehrere Gegner an einer Stelle fest (AoE-CC), ${list(damagers.map((id) => this.name(id)))} ${hits} sie dort alle mit AoE-Schaden. ${wombo}`,
        ),
      );
    }

    const knockUp = `${NOT_COUNTED} je Knock-up im Team mit Yasuo +0,6 ± 0,7 Punkte, nicht von null zu trennen.`;
    if (this.has('knockUp', candidate) && this.partners('yasuo', candidate, allies).length > 0) {
      found.push(neutral('Knock-up für Yasuo', `Yasuos Ult (R) geht nur auf Gegner in der Luft — dieser Pick wirft sie hoch. ${knockUp}`));
    } else if (this.has('yasuo', candidate)) {
      const throwers = this.partners('knockUp', candidate, allies);
      if (throwers.length > 0) {
        const throws = throwers.length > 1 ? 'werfen' : 'wirft';
        found.push(neutral(`Knock-up von ${list(throwers.map((id) => this.name(id)))}`, `Yasuos Ult (R) geht nur auf Gegner in der Luft — ${list(throwers.map((id) => this.name(id)))} ${throws} sie hoch. ${knockUp}`));
      }
    }

    const protect = `${NOT_COUNTED} Enchanter und Hypercarry im selben Team +0,8 ± 0,8 Punkte, nicht sicher von null zu trennen.`;
    const carries = this.has('enchanter', candidate) ? this.partners('hypercarry', candidate, allies) : [];
    const enchanters = this.has('hypercarry', candidate) ? this.partners('enchanter', candidate, allies) : [];
    if (carries.length > 0) {
      const carry = this.name(carries[0]!);
      found.push(neutral(`Enchanter für ${carry}`, `${carry} entscheidet als Hypercarry Kämpfe allein, solange jemand dahintersteht — dieser Pick shieldet, heilt oder bufft. ${protect}`));
    } else if (enchanters.length > 0) {
      const enchanter = this.name(enchanters[0]!);
      found.push(neutral(`Hypercarry mit ${enchanter}`, `Dieser Pick entscheidet als Hypercarry Kämpfe allein, solange jemand dahintersteht — ${enchanter} shieldet, heilt oder bufft. ${protect}`));
    }

    return found.slice(0, MOST);
  }
}
