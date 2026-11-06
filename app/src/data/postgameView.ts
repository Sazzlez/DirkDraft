// The postgame (core/postgame) as the postgame area shows it.

import { topThreeLine, type HistoryEntry, type PostgameModel } from '../core/postgame';
import type { ChampionRef, PostgameView } from '../core/view';

/** "heute 21:40", "gestern", "3.11." — how long ago, as people say it. */
export function when(at: number, now: Date): string {
  const then = new Date(at);
  const day = (d: Date) => new Date(d.getFullYear(), d.getMonth(), d.getDate()).getTime();
  const days = Math.round((day(now) - day(then)) / 86_400_000);
  if (days <= 0) return `heute ${String(then.getHours()).padStart(2, '0')}:${String(then.getMinutes()).padStart(2, '0')}`;
  if (days === 1) return 'gestern';
  return `${then.getDate()}.${then.getMonth() + 1}.`;
}

export function postgameView(
  model: PostgameModel,
  history: readonly HistoryEntry[],
  champion: (id: number) => ChampionRef,
  now: Date,
): PostgameView {
  return {
    result: model.win ? 'Sieg' : 'Niederlage',
    champion: champion(model.championId),
    pickedFromTop3: model.fromTop3,
    pickNote: model.pickNote,
    buildNote: model.buildNote,
    history: history.map((h) => ({ champion: champion(h.championId), result: h.win ? 'S' : 'N', fromTop3: h.fromTop3, when: when(h.at, now) })),
    topThreeRate: topThreeLine(history),
  };
}
