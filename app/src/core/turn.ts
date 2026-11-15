// What the clock says, read from the session's action groups. The step line that stood under it
// ("Banns · Picks · Finalisierung") went on 15.11.2026: the client shows the phase itself.

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
