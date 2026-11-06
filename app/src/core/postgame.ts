// The postgame moment: was the pick one of the first three suggestions, how did it go, how close was
// the build to the plan — and a short local history of exactly that. Stored on this machine only.

import type { DraftFrame, FrameItem } from './engine';
import { standing } from './model/scoreError';

export interface PickRecord {
  championId: number;
  /** 1-based place among the pick suggestions for the own seat; 0 = not a candidate there. */
  rank: number;
  candidates: number;
  /** Within one error bar of the first place — as good a choice as the first. */
  tiedWithFirst: boolean;
  /** null where the mode had no pick suggestions (ARAM, Arena, blind without lane). */
  fromTop3: boolean | null;
  at: number;
}

/**
 * Watches the frames of a draft and records the own pick the moment it is locked, from the frame
 * before: that is the list the player saw when choosing.
 */
export class PickRecorder {
  private previous: DraftFrame | null = null;

  observe(frame: DraftFrame, now: number): PickRecord | null {
    const before = this.previous;
    this.previous = frame;
    const mine = (f: DraftFrame | null) => f?.state.allies.find((s) => s.cellId === f.state.localCellId) ?? null;
    const own = mine(frame);
    const was = mine(before);
    if (!frame.state.isActive || !own?.isLocked || !before || was?.isLocked) return null;
    return recordFrom(before, own.lockedChampionId, now);
  }

  reset(): void {
    this.previous = null;
  }
}

function recordFrom(frame: DraftFrame, championId: number, at: number): PickRecord {
  if (!frame.state.usesLanes) return { championId, rank: 0, candidates: 0, tiedWithFirst: false, fromTop3: null, at };

  // The hover row knows the place among all candidates; the list knows only the first eight.
  const hover = frame.hover?.championId === championId ? frame.hover : null;
  if (hover) {
    const leader = hover.leader ?? hover.item;
    const tied = hover.rank === 1 || standing(leader.score, leader.uncertainty, hover.item.score, hover.item.uncertainty) === 'tied';
    return { championId, rank: hover.rank, candidates: hover.candidates, tiedWithFirst: tied, fromTop3: (hover.rank >= 1 && hover.rank <= 3) || tied, at };
  }

  const picks = frame.picks;
  if (!picks || picks.action !== 'pick' || picks.items.length === 0) return { championId, rank: 0, candidates: 0, tiedWithFirst: false, fromTop3: null, at };
  const index = picks.items.findIndex((i: FrameItem) => i.championId === championId);
  const tied = index >= 0 && index < picks.tied;
  return {
    championId,
    rank: index + 1,
    candidates: 0,
    tiedWithFirst: tied || index === 0,
    fromTop3: index >= 0 && (index < 3 || tied),
    at,
  };
}

export interface EndOfGame {
  win: boolean;
  /** The final inventory. */
  items: readonly number[];
}

export interface PostgameModel {
  win: boolean;
  championId: number;
  fromTop3: boolean | null;
  pickNote: string;
  buildNote: string;
}

export function postgameModel(
  championId: number,
  record: PickRecord | null,
  end: EndOfGame,
  plannedCore: readonly number[],
  name: (id: number) => string,
): PostgameModel {
  const who = name(championId);
  let pickNote: string;
  if (!record || record.fromTop3 === null) pickNote = 'Für diesen Pick gab es keine Vorschläge.';
  else if (record.rank === 1) pickNote = `${who} war der beste Vorschlag.`;
  else if (record.rank > 1 && record.tiedWithFirst) pickNote = `${who} lag auf Platz ${record.rank}, gleichauf mit Platz 1.`;
  else if (record.rank > 1 && record.rank <= 3) pickNote = `${who} lag auf Platz ${record.rank} der Vorschläge.`;
  else if (record.rank > 3) pickNote = record.candidates > 0 ? `${who} lag auf Platz ${record.rank} von ${record.candidates}.` : `${who} lag auf Platz ${record.rank}.`;
  else pickNote = `${who} stand nicht in den Vorschlägen für diese Lane.`;

  let buildNote = '';
  if (plannedCore.length > 0) {
    const built = plannedCore.filter((id) => end.items.includes(id));
    buildNote =
      built.length === plannedCore.length
        ? 'Kern wie empfohlen.'
        : built.length === 0
          ? 'Anderer Kern als empfohlen.'
          : `Vom empfohlenen Kern gebaut: ${built.length} von ${plannedCore.length}.`;
  }

  return { win: end.win, championId, fromTop3: record?.fromTop3 ?? null, pickNote, buildNote };
}

// ---- local history --------------------------------------------------------------------------------

export interface HistoryEntry {
  championId: number;
  win: boolean;
  fromTop3: boolean | null;
  at: number;
}

export const HISTORY_LENGTH = 20;

/** Newest first, at most twenty. */
export function addToHistory(history: readonly HistoryEntry[], entry: HistoryEntry): HistoryEntry[] {
  return [entry, ...history].slice(0, HISTORY_LENGTH);
}

/** "3 von 4 Picks aus den ersten drei", over the games that had suggestions. */
export function topThreeLine(history: readonly HistoryEntry[]): string {
  const judged = history.filter((h) => h.fromTop3 !== null);
  if (judged.length === 0) return '';
  const top = judged.filter((h) => h.fromTop3).length;
  return `${top} von ${judged.length} Picks aus den ersten drei`;
}
