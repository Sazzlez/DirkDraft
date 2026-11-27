// The game moment (core/game) as the game area shows it: names, pictures, sentences.

import { DAMAGE_HINT_SHARE, type GameModel } from '../core/game';
import { SITUATION_TEXT, SLOT, type Move, type RatedSuggestion, type SituationalSuggestion } from '../core/situational';
import { DamageType } from '../core/model/metaLookup';
import { LANE_PHASE_SECONDS, STRENGTH_STEP, type Strength, type TeamStand, type TeamStrength } from '../core/strength';
import type { ChampionRef, GameView, ItemRef, StrengthView, TeamStandView, TeamStrengthView, Tone, VerdictLine } from '../core/view';
import { itemName } from './assets';

const item = (id: number): ItemRef => ({ id, name: itemName(id) });

const DAMAGE: Record<number, GameView['enemies'][number]['damage']> = {
  [DamageType.Physical]: 'physisch',
  [DamageType.Magic]: 'magisch',
  [DamageType.Mixed]: 'gemischt',
};

export { DAMAGE_HINT_SHARE };

function clock(seconds: number): string {
  const total = Math.max(0, Math.floor(seconds));
  return `${Math.floor(total / 60)}:${String(total % 60).padStart(2, '0')}`;
}

/** "Winrate damit 53 % (sonst 51 %) · 1.800 Spiele" */
export function situationalStats(s: SituationalSuggestion): string {
  const pct = (r: number) => `${Math.round(r * 100)} %`;
  return `Winrate damit ${pct(s.winRate)} (sonst ${pct(s.otherRate)}) · ${s.games.toLocaleString('de-DE')} Spiele`;
}

/** "Leicht stärker": the scale's step in words. It says how things stand, never what to do. */
export function strengthLabel(value: number): string {
  const size = Math.abs(value);
  if (size < 0.5) return 'Ausgeglichen';
  const word = value > 0 ? 'stärker' : 'schwächer';
  if (size < 1.5) return `Leicht ${word}`;
  return size < 2.5 ? word.charAt(0).toUpperCase() + word.slice(1) : `Deutlich ${word}`;
}

/** The phases of „Dieses Duell“, as the export counts them (mean level under 6, under 11, from 11). */
export const DUEL_PHASES = ['früh · Lvl 1–5', 'Mitte · 6–10', 'spät · 11+'];

/**
 * Below this difference in log-odds (about 54 to 46 at equal level and items) a phase of the duel is
 * „offen“. On the clean duels of 22.11.2026 that is about a third of the matchups in each phase; half
 * of them lie within 0,25.
 */
export const DUEL_EVEN = 0.15;

/** The strength scale (core/strength) as the tile shows it: the opponents' pictures, the step and what it is made of. */
export function strengthView(strength: Strength, champion: (id: number) => ChampionRef | null): StrengthView {
  const tone = (diff: number): 'up' | 'down' | 'even' => (diff > 0 ? 'up' : diff < 0 ? 'down' : 'even');
  const signed = (diff: number): string => (diff === 0 ? '±0' : `${diff > 0 ? '+' : '−'}${Math.abs(diff).toLocaleString('de-DE')}`);
  const nameOf = (id: number): string => champion(id)?.name ?? `#${id}`;
  // Who is stronger in each phase at equal level and items: the champion one against one, "ihr" and
  // "Gegner" on the bot lane. It says who, never what to do, and moves on without a flash.
  const bot = strength.mine.championIds.length > 1;
  const whoOf = (diff: number): string =>
    Math.abs(diff) < DUEL_EVEN ? 'offen' : diff > 0 ? (bot ? 'ihr' : nameOf(strength.mine.championIds[0]!)) : bot ? 'Gegner' : nameOf(strength.theirs.championIds[0]!);
  return {
    opponents: strength.theirs.championIds.map((id) => champion(id) ?? { id, key: '', name: `#${id}` }),
    value: strength.value,
    label: strengthLabel(strength.value),
    // Only the own lead (on the bot lane both players summed): "9 : 8 +1" and "4.350 : 4.533 −183"
    // left the reader to work it out. Gold is the item worth.
    rows: [
      { label: 'Level', value: signed(strength.levelDiff), tone: tone(strength.levelDiff) },
      { label: 'Gold', value: signed(strength.worthDiff), tone: tone(strength.worthDiff) },
    ],
    // How the champions do in duels by phase (Messung 35): only where meta.json brings their strengths.
    phases: strength.duel
      ? strength.duel.map((diff, phase) => ({
          label: DUEL_PHASES[phase]!,
          who: whoOf(diff),
          tone: Math.abs(diff) < DUEL_EVEN ? 'even' : diff > 0 ? 'up' : 'down',
          now: phase === strength.phase,
        }))
      : null,
  };
}

/** Team gegen Team as the tile shows it: the two sums of each row, their difference and the own side's part. */
export function teamStandView(stand: TeamStand): TeamStandView {
  const tone = (diff: number): 'up' | 'down' | 'even' => (diff > 0 ? 'up' : diff < 0 ? 'down' : 'even');
  const signed = (diff: number): string => (diff === 0 ? '±0' : `${diff > 0 ? '+' : '−'}${Math.abs(diff).toLocaleString('de-DE')}`);
  const share = (mine: number, theirs: number): number => (mine + theirs > 0 ? mine / (mine + theirs) : 0.5);
  return {
    rows: [
      { label: 'Level', mine: String(stand.mine.level), theirs: String(stand.theirs.level), diff: signed(stand.levelDiff), tone: tone(stand.levelDiff), share: share(stand.mine.level, stand.theirs.level) },
      {
        label: 'Items',
        mine: stand.mine.worth.toLocaleString('de-DE'),
        theirs: stand.theirs.worth.toLocaleString('de-DE'),
        diff: signed(stand.worthDiff),
        tone: tone(stand.worthDiff),
        share: share(stand.mine.worth, stand.theirs.worth),
      },
    ],
  };
}

/**
 * How often the team the teamfight scale sees ahead won, per step (Ausgeglichen, Leicht, —, Deutlich):
 * measured on the held-out fights of 27.11.2026 (docs/modellpruefung.md, Messung 36).
 */
export const TEAMFIGHT_ODDS = [0.555, 0.654, 0.773, 0.886];

/** "Teams in dieser Lage gewinnen rund 7 von 10 Teamfights" — the step's measured share, not the model's own number. */
export function teamfightOdds(value: number): string {
  const size = Math.abs(value);
  const step = size < 0.5 ? 0 : size < 1.5 ? 1 : size < 2.5 ? 2 : 3;
  const share = step === 0 ? 0.5 : value > 0 ? TEAMFIGHT_ODDS[step]! : 1 - TEAMFIGHT_ODDS[step]!;
  return `Teams in dieser Lage gewinnen rund ${Math.round(share * 10)} von 10 Teamfights`;
}

/** The teamfight scale (core/strength teamStrengthOf) as the tile shows it: the step, its odds and its parts, the largest first. */
export function teamStrengthView(team: TeamStrength): TeamStrengthView {
  const signed = (diff: number): string => (diff === 0 ? '±0' : `${diff > 0 ? '+' : '−'}${Math.abs(diff).toLocaleString('de-DE')}`);
  const steps = (part: number): number => part / STRENGTH_STEP;
  const arrows = (part: number): string => (Math.abs(steps(part)) < 0.5 ? '·' : (part > 0 ? '▲' : '▼').repeat(Math.abs(steps(part)) < 1.5 ? 1 : 2));
  const tone = (part: number): 'up' | 'down' | 'even' => (Math.abs(steps(part)) < 0.5 ? 'even' : part > 0 ? 'up' : 'down');
  const who = (mine: boolean, theirs: boolean): string => (mine && theirs ? 'beide' : mine ? 'ihr' : 'Gegner');
  const { stand, buffs, parts } = team;
  // Only the difference: two team sums ("38.550 : 41.733") left the reader to do the sum himself.
  // Gold is the item worth, as everywhere in the game view; Level the five levels summed.
  const rows: { part: number; label: string; text: string; diff: string }[] = [
    { part: parts.gold, label: 'Gold', text: signed(stand.worthDiff), diff: '' },
    { part: parts.level, label: 'Level', text: signed(stand.levelDiff), diff: '' },
  ];
  if (buffs.mine.baron || buffs.theirs.baron) rows.push({ part: parts.baron, label: 'Baron', text: who(buffs.mine.baron, buffs.theirs.baron), diff: '' });
  if (buffs.mine.elder || buffs.theirs.elder) rows.push({ part: parts.elder, label: 'Elder', text: who(buffs.mine.elder, buffs.theirs.elder), diff: '' });
  if (buffs.mine.dragons + buffs.theirs.dragons > 0) rows.push({ part: parts.dragons, label: 'Drachen', text: `${buffs.mine.dragons} : ${buffs.theirs.dragons}`, diff: '' });
  if (buffs.mine.soul || buffs.theirs.soul) rows.push({ part: parts.soul, label: 'Seele', text: who(buffs.mine.soul, buffs.theirs.soul), diff: '' });
  rows.push({ part: parts.champions, label: 'Champions', text: Math.abs(parts.champions) < 0.05 ? 'ausgeglichen' : parts.champions > 0 ? 'ihr' : 'Gegner', diff: '' });
  return {
    value: team.value,
    label: strengthLabel(team.value),
    odds: teamfightOdds(team.value),
    rows: rows
      .map((row, index) => ({ row, index }))
      .sort((a, b) => Math.abs(b.row.part) - Math.abs(a.row.part) || a.index - b.index)
      .map(({ row }) => ({ label: row.label, text: row.text, diff: row.diff, arrows: arrows(row.part), tone: tone(row.part) })),
  };
}

/**
 * `situational`: suggestions from core/situational, or null when there is no situational data —
 * the tile is then left out rather than saying "nothing better" on no evidence.
 */
export function gameView(
  model: GameModel,
  champion: (id: number) => ChampionRef | null,
  title: string,
  draftLine: string | null,
  situational: (SituationalSuggestion | RatedSuggestion)[] | null = null,
  strength: Strength | null = null,
  /** The plan's own late items brought forward for this line-up (core/situational lateOrder). */
  moves: readonly Move[] = [],
  /**
   * `team`: both teams' levels and items (core/strength teamStandOf). `teamStrength`: the teamfight
   * scale (teamStrengthOf), where meta.json brings it. `verdict`: what the draft said about the two
   * line-ups.
   */
  extra: { team?: TeamStand | null; teamStrength?: TeamStrength | null; verdict?: readonly VerdictLine[] } = {},
): GameView {
  const me = champion(model.championId) ?? { id: model.championId, key: '', name: `#${model.championId}` };

  const slots = ['Start', 'Schuhe', 'Support', 'Kern', 'Spät'] as const;
  const nextId = model.nextBuy?.towards ?? model.nextBuy?.itemId ?? null;
  const path = slots
    .map((slot) => ({
      slot,
      items: model.path
        .filter((s) => s.slot === slot)
        .map((s) => ({ ...item(s.itemId), owned: s.owned, next: !s.owned && s.itemId === nextId, situational: s.situational === true })),
    }))
    .filter((group) => group.items.length > 0);

  const hints: GameView['hints'] = [];
  // By amount: what these champions deal per game on average (own data). By heads (OP.GG files): how
  // many of them deal mainly that type — said as that, not as a share of the damage.
  const share = (value: number, kind: string) =>
    model.enemyDamageBy === 'amount'
      ? `Etwa ${Math.round(value * 100)} % des Schadens, den diese Gegner im Schnitt machen, ist ${kind}.`
      : `Etwa ${Math.round(value * 100)} % der Gegner machen vor allem ${kind}en Schaden.`;
  // The plan's own item against it, by name: the hint stands beside the build and leaves its order
  // alone, but "Magieresistenz lohnt sich" beside a list that builds Thornmail gave nothing to act on.
  const answer = model.answerItem ? ` In deinem Build steht dafür: ${itemName(model.answerItem)}.` : '';
  if (model.enemyDamage === 'physical') {
    hints.push({ title: 'Rüstung lohnt sich', detail: `${share(model.enemyPhysical, 'physisch')}${answer}`, tone: 'info' as Tone });
  } else if (model.enemyDamage === 'magic') {
    hints.push({ title: 'Magieresistenz lohnt sich', detail: `${share(model.enemyMagic, 'magisch')}${answer}`, tone: 'info' as Tone });
  }

  // What the list is buying towards has a measured alternative for this game's enemies
  // (core/situational) that did not take its place: said where the buy is, not only further down.
  const buying = model.nextBuy ? (model.nextBuy.towards ?? model.nextBuy.itemId) : null;
  const other = buying === null ? undefined : situational?.find((s) => 'instead' in s && s.instead === buying && !s.adopt);
  const buyAlternative = other && buying !== null ? { item: item(other.itemId), instead: item(buying), reason: SITUATION_TEXT[other.situation] } : null;

  // First in the tile: what moved forward in the build, then what was brought in and the alternatives.
  const pct = (r: number) => `${Math.round(r * 100)} %`;
  const moved = moves.map((m) => {
    const text = SITUATION_TEXT[m.situation];
    return {
      item: item(m.itemId),
      instead: item(m.before),
      adopted: true,
      moved: true,
      reason: text.charAt(0).toUpperCase() + text.slice(1),
      stats: `${pct(m.share)} der späten Käufe, ${itemName(m.before)} ${pct(m.otherShare)} · ${m.games.toLocaleString('de-DE')} Käufe`,
    };
  });

  // The lane scale holds for the lane phase; after it, and where there is no scale, the team stand.
  const lanePhase = model.gameTime < LANE_PHASE_SECONDS;
  const scale = strength && lanePhase ? strengthView(strength, champion) : null;
  const team = extra.team ?? null;

  return {
    clock: clock(model.gameTime),
    level: model.level,
    gold: model.gold,
    me,
    title,
    nextBuy: model.nextBuy
      ? {
          item: item(model.nextBuy.itemId),
          cost: model.nextBuy.cost,
          affordable: model.nextBuy.affordable,
          then: model.nextBuy.towards ? item(model.nextBuy.towards) : null,
          supportChoice: model.nextBuy.supportChoice,
          situational: model.nextBuy.situational === true,
        }
      : null,
    basket: model.basket.map((buy) => ({ item: item(buy.itemId), cost: buy.cost, then: buy.towards ? item(buy.towards) : null, situational: buy.situational === true })),
    goldLeft: model.goldLeft,
    buyAlternative,
    path,
    nextSkill: model.nextSkill ? { key: model.nextSkill.key, level: model.nextSkill.level, now: model.nextSkill.now } : null,
    skillPoints: model.skillPoints,
    enemies: model.enemies.map((e, index) => ({
      // Same players in the same order as the team stand reads them.
      stand: team?.enemies[index]?.championId === e.championId ? { level: team.enemies[index]!.level, worth: team.enemies[index]!.worth } : null,
      lead: !lanePhase && !!team && team.leaderId !== 0 && team.leaderId === e.championId,
      champion: champion(e.championId) ?? { id: e.championId, key: '', name: `#${e.championId}` },
      items: e.items.map(item),
      damage: DAMAGE[e.damage] ?? 'gemischt',
    })),
    hints,
    draftLine,
    sample: null,
    strength: scale,
    team: !scale && team ? teamStandView(team) : null,
    teamStrength: !scale && extra.teamStrength ? teamStrengthView(extra.teamStrength) : null,
    // The damage line is the hint above, with the share and the build's own item.
    verdict: (extra.verdict ?? []).filter((line) => line.icon !== 'damage'),
    situational: situational
      ? [...moved, ...situational.map((s) => {
          // The base choice it is an alternative to: the planned boots, or the next core or late item
          // not yet owned.
          const next = (slot: 'Kern' | 'Spät') =>
            model.path.find((p) => p.slot === slot && !p.owned) ?? model.path.filter((p) => p.slot === slot).at(-1);
          const base = s.slot === SLOT.Boots ? model.path.find((p) => p.slot === 'Schuhe') : next(s.slot === SLOT.Core ? 'Kern' : 'Spät');
          // A rated suggestion names the plan item it stands against itself.
          const instead = 'instead' in s ? s.instead : (base?.itemId ?? null);
          const text = SITUATION_TEXT[s.situation];
          return {
            item: item(s.itemId),
            instead: instead ? item(instead) : null,
            adopted: 'adopt' in s && s.adopt,
            moved: false,
            reason: text.charAt(0).toUpperCase() + text.slice(1),
            stats: situationalStats(s),
          };
        })]
      : moved.length > 0
        ? moved
        : null,
  };
}
