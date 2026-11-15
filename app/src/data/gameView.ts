// The game moment (core/game) as the game area shows it: names, pictures, sentences.

import { DAMAGE_HINT_SHARE, type GameModel } from '../core/game';
import { SITUATION_TEXT, SLOT, type Move, type RatedSuggestion, type SituationalSuggestion } from '../core/situational';
import { DamageType } from '../core/model/metaLookup';
import { LANE_PHASE_SECONDS, type Strength, type TeamStand } from '../core/strength';
import type { ChampionRef, GameView, ItemRef, StrengthView, TeamStandView, Tone, VerdictLine } from '../core/view';
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

/** The strength scale (core/strength) as the tile shows it: the opponents' pictures, the step and what it is made of. */
export function strengthView(strength: Strength, champion: (id: number) => ChampionRef | null): StrengthView {
  const tone = (diff: number): 'up' | 'down' | 'even' => (diff > 0 ? 'up' : diff < 0 ? 'down' : 'even');
  const signed = (diff: number): string => (diff === 0 ? '±0' : `${diff > 0 ? '+' : '−'}${Math.abs(diff).toLocaleString('de-DE')}`);
  return {
    opponents: strength.theirs.championIds.map((id) => champion(id) ?? { id, key: '', name: `#${id}` }),
    value: strength.value,
    label: strengthLabel(strength.value),
    rows: [
      { label: 'Level', mine: strength.mine.levels.join(' + '), theirs: strength.theirs.levels.join(' + '), diff: signed(strength.levelDiff), tone: tone(strength.levelDiff) },
      {
        label: 'Items',
        mine: strength.mine.worth.toLocaleString('de-DE'),
        theirs: strength.theirs.worth.toLocaleString('de-DE'),
        diff: signed(strength.worthDiff),
        tone: tone(strength.worthDiff),
      },
    ],
    // How the two champions do in duels by phase: only once the collector exports it (Messung 27).
    phases: null,
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
   * `team`: both teams' levels and items (core/strength teamStandOf). `verdict`: what the draft said
   * about the two line-ups.
   */
  extra: { team?: TeamStand | null; verdict?: readonly VerdictLine[] } = {},
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
  // Mixed champions count half to each side, so the share is said as a share, not as a head count.
  const share = (value: number) => `Etwa ${Math.round(value * 100)} % des Schadens im Gegnerteam ist`;
  // The plan's own item against it, by name: the hint stands beside the build and leaves its order
  // alone, but "Magieresistenz lohnt sich" beside a list that builds Thornmail gave nothing to act on.
  const answer = model.answerItem ? ` In deinem Build steht dafür: ${itemName(model.answerItem)}.` : '';
  if (model.enemyDamage === 'physical') {
    hints.push({ title: 'Rüstung lohnt sich', detail: `${share(model.enemyPhysical)} physisch.${answer}`, tone: 'info' as Tone });
  } else if (model.enemyDamage === 'magic') {
    hints.push({ title: 'Magieresistenz lohnt sich', detail: `${share(model.enemyMagic)} magisch.${answer}`, tone: 'info' as Tone });
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
