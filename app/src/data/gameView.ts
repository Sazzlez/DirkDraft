// The game moment (core/game) as the game area shows it: names, pictures, sentences.

import type { GameModel } from '../core/game';
import { SITUATION_TEXT, SLOT, type SituationalSuggestion } from '../core/situational';
import { DamageType } from '../core/model/metaLookup';
import type { ChampionRef, GameView, ItemRef, Tone } from '../core/view';
import { itemName } from './assets';

const item = (id: number): ItemRef => ({ id, name: itemName(id) });

const DAMAGE: Record<number, GameView['enemies'][number]['damage']> = {
  [DamageType.Physical]: 'physisch',
  [DamageType.Magic]: 'magisch',
  [DamageType.Mixed]: 'gemischt',
};

/**
 * A clear majority before a damage type is worth a hint; a 3:2 split (0.6) is no reason to itemise.
 * 1.5's threshold, the same as the draft verdict's "Viel AD/AP beim Gegner".
 */
export const DAMAGE_HINT_SHARE = 0.65;

function clock(seconds: number): string {
  const total = Math.max(0, Math.floor(seconds));
  return `${Math.floor(total / 60)}:${String(total % 60).padStart(2, '0')}`;
}

/** "Winrate damit 53 % (sonst 51 %) · 1.800 Spiele" */
export function situationalStats(s: SituationalSuggestion): string {
  const pct = (r: number) => `${Math.round(r * 100)} %`;
  return `Winrate damit ${pct(s.winRate)} (sonst ${pct(s.otherRate)}) · ${s.games.toLocaleString('de-DE')} Spiele`;
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
  situational: SituationalSuggestion[] | null = null,
): GameView {
  const me = champion(model.championId) ?? { id: model.championId, key: '', name: `#${model.championId}` };

  const slots = ['Start', 'Schuhe', 'Support', 'Kern', 'Spät'] as const;
  const nextId = model.nextBuy?.towards ?? model.nextBuy?.itemId ?? null;
  const path = slots
    .map((slot) => ({
      slot,
      items: model.path
        .filter((s) => s.slot === slot)
        .map((s) => ({ ...item(s.itemId), owned: s.owned, next: !s.owned && s.itemId === nextId })),
    }))
    .filter((group) => group.items.length > 0);

  const hints: GameView['hints'] = [];
  // Mixed champions count half to each side, so the share is said as a share, not as a head count.
  const known = model.enemies.filter((e) => e.damage !== DamageType.Unknown).length;
  const share = (value: number) => `Etwa ${Math.round(value * 100)} % des Schadens im Gegnerteam ist`;
  if (known >= 3 && model.enemyPhysical >= DAMAGE_HINT_SHARE) {
    hints.push({ title: 'Rüstung lohnt sich', detail: `${share(model.enemyPhysical)} physisch.`, tone: 'info' as Tone });
  } else if (known >= 3 && model.enemyMagic >= DAMAGE_HINT_SHARE) {
    hints.push({ title: 'Magieresistenz lohnt sich', detail: `${share(model.enemyMagic)} magisch.`, tone: 'info' as Tone });
  }

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
        }
      : null,
    path,
    nextSkill: model.nextSkill ? { key: model.nextSkill.key, level: model.nextSkill.level, now: model.nextSkill.now } : null,
    skillPoints: model.skillPoints,
    enemies: model.enemies.map((e) => ({
      champion: champion(e.championId) ?? { id: e.championId, key: '', name: `#${e.championId}` },
      items: e.items.map(item),
      damage: DAMAGE[e.damage] ?? 'gemischt',
    })),
    hints,
    draftLine,
    sample: null,
    situational: situational
      ? situational.map((s) => {
          // The base choice it is an alternative to: the planned boots, or the next core or late item
          // not yet owned.
          const next = (slot: 'Kern' | 'Spät') =>
            model.path.find((p) => p.slot === slot && !p.owned) ?? model.path.filter((p) => p.slot === slot).at(-1);
          const base = s.slot === SLOT.Boots ? model.path.find((p) => p.slot === 'Schuhe') : next(s.slot === SLOT.Core ? 'Kern' : 'Spät');
          const text = SITUATION_TEXT[s.situation];
          return {
            item: item(s.itemId),
            instead: base ? item(base.itemId) : null,
            reason: text.charAt(0).toUpperCase() + text.slice(1),
            stats: situationalStats(s),
          };
        })
      : null,
  };
}
