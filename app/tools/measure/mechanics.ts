// Mechanic counters: a kit against a kit. Poppy's W stops dashes, Morgana's E blocks a hook, a hook
// catches an ADC without a dash, Jax's E blocks autoattacks. A counter like that works through the
// skills, not through the champion's general strength — so it shows, if anywhere, as an edge in the
// encounter beyond both champions' strength.
//
// The lists are judgement, clear cases only, like champion_traits.json (absent = neutral). They were
// drafted as build/mechanik-entwurf.md and checked by the user on 09.11.2026 — fixed BEFORE anything
// was measured on them, so they cannot have been bent towards a result. They live here, in the
// measurement, not in the app's trait file: nothing unmeasured gets a weight or a chip.

import { Lane } from '../../src/core/model/lane';
import type { Game } from './games';

/** Champions by name as Data Dragon spells them (matched loosely: case and punctuation ignored, the key also counts). */
export const MECHANICS = {
  /** The kit lives on dashes or blinks. */
  dash: [
    'Yasuo', 'Yone', 'Irelia', 'Riven', 'Fiora', 'Camille', 'Jax', 'Gnar', 'Renekton', 'Aatrox', 'Tryndamere', 'Kled', 'Gwen',
    'Akali', 'Sylas', 'Quinn', 'Vayne', 'Pantheon', 'Rengar', 'Kayn', 'Nidalee', 'Lee Sin', "Kha'Zix", 'Graves', 'Kindred',
    'Kalista', 'Lucian', 'Ezreal', 'Tristana', 'Samira', 'Corki', 'Akshan', 'Zeri', 'Nilah', 'Fizz', 'Ekko', 'Katarina',
    'LeBlanc', 'Qiyana', 'Zed', 'Talon', 'Ahri', 'Kassadin', 'Vi', 'Wukong', 'Xin Zhao', 'Jarvan IV', 'Viego', "Bel'Veth",
    'Diana', 'Hecarim', 'Caitlyn',
  ],
  /** ADCs without a dash. */
  noDash: ['Jhin', 'Ashe', 'Varus', 'Miss Fortune', 'Aphelios', "Kog'Maw", 'Twitch', 'Jinx', 'Draven', 'Sivir', 'Senna', 'Xayah'],
  /** Stops or punishes dashes: Poppy W, Cassiopeia W, Singed W (grounded), Taliyah E. */
  stopsDash: ['Poppy', 'Cassiopeia', 'Singed', 'Taliyah'],
  /** Blocks the first crowd control: Morgana E, Sivir E, Nocturne W, Malzahar's passive. */
  spellShield: ['Morgana', 'Sivir', 'Nocturne', 'Malzahar'],
  /** A catch by skillshot. */
  hook: ['Blitzcrank', 'Thresh', 'Nautilus', 'Pyke', 'Amumu'],
  /** Blocks, blinds or slows autoattacks. */
  blocksAutos: ['Jax', 'Shen', 'Teemo', 'Quinn', 'Rammus', 'Malphite', 'Nasus'],
  /** The damage comes mostly from autoattacks. */
  autoReliant: [
    'Tryndamere', 'Master Yi', 'Jax', 'Fiora', 'Yasuo', 'Yone', 'Irelia', 'Trundle', 'Xin Zhao', 'Udyr', 'Olaf', 'Viego',
    "Bel'Veth", 'Kindred', 'Kayle', 'Vayne', "Kog'Maw", 'Jinx', 'Twitch', "Kai'Sa", 'Draven', 'Ashe', 'Caitlyn', 'Nilah',
    'Samira', 'Zeri', 'Aphelios', 'Lucian', 'Tristana',
  ],
} as const;

export type Mechanic = keyof typeof MECHANICS;

export interface MechanicRule {
  label: string;
  /** What the countering champion has, and what its opponent has. */
  mine: Mechanic;
  theirs: Mechanic;
  /** Which two lanes meet in the rule: the duel, and where it says so the bot cross (ADC ↔ support). */
  meets: (mine: Lane, theirs: Lane) => boolean;
  /** Both on the same lane: the lane's gold after ten minutes tells the laning phase. */
  sameLane: boolean;
}

const duelOrBotCross = (mine: Lane, theirs: Lane): boolean =>
  (mine === theirs && mine !== Lane.Unknown) || (mine === Lane.Adc && theirs === Lane.Support) || (mine === Lane.Support && theirs === Lane.Adc);

export const MECHANIC_RULES: readonly MechanicRule[] = [
  { label: 'Dash-Stopper gegen Dash-Champ', mine: 'stopsDash', theirs: 'dash', meets: duelOrBotCross, sameLane: false },
  { label: 'Spell Shield gegen Hook', mine: 'spellShield', theirs: 'hook', meets: duelOrBotCross, sameLane: false },
  { label: 'Hook gegen ADC ohne Dash', mine: 'hook', theirs: 'noDash', meets: (mine, theirs) => mine === Lane.Support && theirs === Lane.Adc, sameLane: false },
  { label: 'Autoattack-Blocker gegen Autoattack-Champ', mine: 'blocksAutos', theirs: 'autoReliant', meets: (mine, theirs) => mine === theirs && mine !== Lane.Unknown, sameLane: true },
];

export type MechanicSets = Record<Mechanic, ReadonlySet<number>>;

const loose = (text: string): string => text.toLowerCase().replace(/[^a-z]/g, '');

/** The lists as champion ids; names no champion of the roster carries come back as missing. */
export function mechanicSets(champions: readonly { id: number; key: string; name: string }[]): { sets: MechanicSets; missing: string[] } {
  const byName = new Map<string, number>();
  for (const c of champions) {
    byName.set(loose(c.name), c.id);
    byName.set(loose(c.key), c.id);
  }
  const missing: string[] = [];
  const sets = {} as Record<Mechanic, Set<number>>;
  for (const [mechanic, names] of Object.entries(MECHANICS) as [Mechanic, readonly string[]][]) {
    sets[mechanic] = new Set();
    for (const name of names) {
      const id = byName.get(loose(name));
      if (id === undefined) missing.push(name);
      else sets[mechanic].add(id);
    }
  }
  return { sets, missing };
}

/** One encounter of a rule: the countering seat and its opponent, places 0–9 (blue first). */
export interface Encounter {
  mine: number;
  theirs: number;
  /** +1 when the countering champion is blue's, −1 when it is red's. */
  side: 1 | -1;
}

/** Every encounter of a rule in a game; one where both would counter each other cancels out and is left out. */
export function encountersOf(game: Game, sets: MechanicSets, rule: MechanicRule): Encounter[] {
  const found: Encounter[] = [];
  const counters = (a: { championId: number; lane: Lane }, b: { championId: number; lane: Lane }) =>
    sets[rule.mine].has(a.championId) && sets[rule.theirs].has(b.championId) && rule.meets(a.lane, b.lane);
  for (let i = 0; i < 5; i++) {
    for (let j = 0; j < 5; j++) {
      const blue = game.blue[i]!;
      const red = game.red[j]!;
      const forBlue = counters(blue, red);
      const forRed = counters(red, blue);
      if (forBlue === forRed) continue;
      found.push(forBlue ? { mine: i, theirs: 5 + j, side: 1 } : { mine: 5 + j, theirs: i, side: -1 });
    }
  }
  return found;
}

/** Per rule, blue's encounters minus red's: the feature the fit weighs. */
export function mechanicFeatures(game: Game, sets: MechanicSets, rules: readonly MechanicRule[] = MECHANIC_RULES): number[] {
  return rules.map((rule) => encountersOf(game, sets, rule).reduce((sum, e) => sum + e.side, 0));
}
