// The game moment (core/game) as the game area shows it: names, pictures, sentences.

import type { GameModel } from '../core/game';
import { DamageType } from '../core/model/metaLookup';
import type { ChampionRef, GameView, ItemRef, Tone } from '../core/view';
import { itemName } from './assets';

const item = (id: number): ItemRef => ({ id, name: itemName(id) });

const DAMAGE: Record<number, GameView['enemies'][number]['damage']> = {
  [DamageType.Physical]: 'physisch',
  [DamageType.Magic]: 'magisch',
  [DamageType.Mixed]: 'gemischt',
};

/** A clear majority before a damage type is worth a hint; a 3:2 split is no reason to itemise. */
export const DAMAGE_HINT_SHARE = 0.6;

function clock(seconds: number): string {
  const total = Math.max(0, Math.floor(seconds));
  return `${Math.floor(total / 60)}:${String(total % 60).padStart(2, '0')}`;
}

export function gameView(
  model: GameModel,
  champion: (id: number) => ChampionRef | null,
  title: string,
  draftLine: string | null,
): GameView {
  const me = champion(model.championId) ?? { id: model.championId, key: '', name: `#${model.championId}` };

  const slots = ['Start', 'Schuhe', 'Kern', 'Spät'] as const;
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
  const share = (value: number) => `Etwa ${Math.round(value * 100)} % ihres Schadens ist`;
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
        }
      : null,
    path,
    nextSkill: model.nextSkill ? { key: model.nextSkill.key, level: model.nextSkill.level } : null,
    enemies: model.enemies.map((e) => ({
      champion: champion(e.championId) ?? { id: e.championId, key: '', name: `#${e.championId}` },
      items: e.items.map(item),
      damage: DAMAGE[e.damage] ?? 'gemischt',
    })),
    hints,
    draftLine,
    sample: null,
  };
}
