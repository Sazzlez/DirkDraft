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
// narrow ones below, which the measurement can read as well (--lists shown).

import { neutral, type Reason } from './reason';

/**
 * The lists the chips are shown from — narrow on purpose (user, 15.11.2026: "Listen enger fassen").
 * With the wide lists the measurement ran on (tools/measure/combos.ts, WIDE) nearly every second team
 * had a wombo and the tag stood at every engage champion; a tag at every row says nothing. Here only
 * the kits a comp is built around. Champions by name as Data Dragon spells them (matched loosely:
 * case and punctuation ignored, the key also counts).
 */
export const COMBOS = {
  /** An ult that reliably holds several enemies in one place. */
  aoeEngage: [
    'Amumu', 'Malphite', 'Sejuani', 'Zac', 'Rell', 'Jarvan IV', 'Wukong', 'Diana', 'Leona', 'Ornn', 'Galio', 'Neeko', 'Kennen',
    'Annie', 'Seraphine', 'Sona', 'Orianna', 'Gnar',
  ],
  /** An area ult that needs enemies standing together to be worth it. */
  aoeDamage: [
    'Miss Fortune', 'Orianna', 'Yasuo', 'Katarina', 'Kennen', 'Fiddlesticks', 'Rumble', 'Brand', 'Samira', 'Nilah', 'Seraphine',
    'Vladimir', 'Aurelion Sol', 'Zyra',
  ],
  /** A reliable knock-up over an area: what the ult of Yasuo needs. */
  knockUp: [
    'Malphite', 'Alistar', 'Gragas', 'Ornn', 'Zac', 'Wukong', 'Jarvan IV', 'Rakan', 'Rell', 'Diana', 'Orianna', 'Sejuani', 'Nami', 'Janna',
    "Cho'Gath", 'Gnar',
  ],
  yasuo: ['Yasuo'],
  enchanter: ['Lulu', 'Yuumi', 'Milio', 'Janna', 'Soraka', 'Nami', 'Sona', 'Renata Glasc'],
  /** Decides fights alone once it is fed and protected. */
  hypercarry: ["Kog'Maw", 'Jinx', 'Vayne', 'Twitch', 'Zeri', 'Aphelios', 'Master Yi', "Bel'Veth", 'Kayle'],
} as const;

export type Combo = keyof typeof COMBOS;
export type ComboSets = Record<Combo, ReadonlySet<number>>;
export type ComboLists = Readonly<Record<Combo, readonly string[]>>;

/** The skill an AoE engage holds them with, for the hint; "R" where the list says nothing else. */
const ENGAGE_SKILL: Readonly<Record<string, string>> = {
  zac: 'E und R',
  rell: 'W und R',
  jarvaniv: 'E-Q und R',
  galio: 'W und R',
};

const loose = (text: string): string => text.toLowerCase().replace(/[^a-z]/g, '');

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
  private readonly names = new Map<number, string>();

  constructor(champions: readonly Named[]) {
    this.sets = comboSets(champions).sets;
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
    return `${name} (${ENGAGE_SKILL[loose(name)] ?? 'R'})`;
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
          `Dieser Pick hält mit ${ENGAGE_SKILL[loose(this.name(candidate))] ?? 'R'} mehrere Gegner an einer Stelle fest (AoE-CC), ${list(damagers.map((id) => this.name(id)))} ${hits} sie dort alle mit AoE-Schaden. ${wombo}`,
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
