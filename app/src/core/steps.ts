// The step line under the clock, read from the draft's own order: the session's action groups.
// Riot runs three orders, and a fixed list fits only one of them —
//   tournament draft (Clash, custom "Turnierdraft"): 3 bans each, picks 1–3, 2 bans each, picks 4–5;
//   ranked, normal draft and custom "Abwechselnde Wahl": one round of ten bans, then the picks;
//   blind pick ("Freie Wahl"): picks only.
// Consecutive actions of one kind form a block (group borders do not matter: a group is one turn,
// a block one phase); the block with an open action is the current one.

import type { PhaseStep } from './view';

type Kind = 'ban' | 'pick';

interface Block {
  kind: Kind;
  done: boolean;
  /** Actions per side in the block: "Pick 1–3" counts one team's picks, not both. */
  ally: number;
  enemy: number;
}

function blocksOf(actions: unknown): Block[] {
  if (!Array.isArray(actions)) return [];
  const blocks: Block[] = [];
  for (const group of actions) {
    if (!Array.isArray(group)) continue;
    for (const action of group as (Record<string, unknown> | null)[]) {
      const kind = String(action?.type ?? '').toLowerCase();
      // ten_bans_reveal and other bookkeeping actions are not steps of their own.
      if (!action || (kind !== 'ban' && kind !== 'pick')) continue;
      let block = blocks[blocks.length - 1];
      if (!block || block.kind !== kind) blocks.push((block = { kind, done: true, ally: 0, enemy: 0 }));
      if (action.completed !== true) block.done = false;
      if (action.isAllyAction === true) block.ally++;
      else block.enemy++;
    }
  }
  return blocks;
}

/**
 * What the clock says while several seats act at once and the own seat is not among them — ranked
 * bans all ten together, picks go two at a time: "Teammate 3 bannt" read as if only one did
 * (live test 07.11.2026). Null when one seat acts, or the own seat is still on the clock.
 */
export function simultaneousTurn(session: unknown): string | null {
  const s = session as { actions?: unknown; localPlayerCellId?: unknown } | null;
  if (!s || !Array.isArray(s.actions)) return null;
  const open: Record<string, unknown>[] = [];
  for (const group of s.actions) {
    if (!Array.isArray(group)) continue;
    for (const a of group as (Record<string, unknown> | null)[]) {
      const type = String(a?.type ?? '').toLowerCase();
      if (a && a.isInProgress === true && a.completed !== true && (type === 'ban' || type === 'pick')) open.push(a);
    }
  }
  if (open.length < 2 || open.some((a) => a.actorCellId === s.localPlayerCellId)) return null;
  const bans = open.every((a) => String(a.type).toLowerCase() === 'ban');
  const allies = open.every((a) => a.isAllyAction === true);
  const enemies = open.every((a) => a.isAllyAction !== true);
  if (bans) return allies ? 'Dein Team bannt' : enemies ? 'Gegner bannen' : 'Alle bannen';
  return allies ? 'Dein Team pickt' : enemies ? 'Gegner picken' : 'Beide Teams picken';
}

/**
 * The steps of a champion select session (the client's JSON, parsed), or [] when it has no
 * pick or ban actions — ARAM and other modes without an order show only the clock.
 * `over`: the session already ended (loading screen), every step is done.
 */
export function draftSteps(session: unknown, over = false): PhaseStep[] {
  const s = session as { actions?: unknown; timer?: { phase?: unknown } } | null;
  if (!s || typeof s !== 'object') return [];
  const blocks = blocksOf(s.actions);
  if (blocks.length === 0) return [];

  const bans = blocks.filter((b) => b.kind === 'ban').length;
  const picks = blocks.filter((b) => b.kind === 'pick').length;
  let banNo = 0;
  let picked = 0;
  const labels = blocks.map((b) => {
    if (b.kind === 'ban') {
      banNo++;
      return bans > 1 ? `Bann ${banNo}` : 'Banns';
    }
    const per = Math.max(b.ally, b.enemy);
    const from = picked + 1;
    picked += per;
    if (picks < 2 || per === 0) return 'Picks';
    return per === 1 ? `Pick ${from}` : `Pick ${from}–${picked}`;
  });
  labels.push('Finalisierung');

  const phase = String(s.timer?.phase ?? '').toUpperCase();
  let now: number;
  if (over) now = labels.length;
  else if (phase === 'PLANNING') now = -1;
  else if (phase === 'FINALIZATION') now = labels.length - 1;
  else {
    const open = blocks.findIndex((b) => !b.done);
    now = open === -1 ? labels.length - 1 : open;
  }
  return labels.map((label, i) => ({ label, state: i < now ? 'done' : i === now ? 'now' : 'next' }));
}
