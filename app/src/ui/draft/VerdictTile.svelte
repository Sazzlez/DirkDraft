<script lang="ts">
  import type { VerdictLine } from '../../core/view';
  import Tile from '../Tile.svelte';

  let { lines }: { lines: VerdictLine[] } = $props();

  const glyph: Record<VerdictLine['icon'], string> = {
    engage: 'M8 1.5 9.8 6l4.7.4-3.6 3 1.1 4.6L8 11.5 3.9 14l1.1-4.6-3.6-3L6.2 6z',
    scaling: 'M2 12.5h12v1H2zm1-2 3.2-3.2 2.3 2.3L13 5l.7.7-5.2 5.2-2.3-2.3-2.5 2.5z',
    heal: 'M7 2h2v5h5v2H9v5H7V9H2V7h5z',
    damage: 'M9 1 3 9h4l-1 6 6-8H8z',
    info: 'M7.25 4h1.5v1.5h-1.5zm0 3h1.5v5h-1.5z',
  };
</script>

<Tile title="Draft-Urteil">
  {#if lines.length === 0}
    <p class="empty">Keine Seite hat einen klaren Vorteil in der Comp.</p>
  {/if}
  {#each lines as line (line.title)}
    <div class="line">
      <span class="g tone-{line.tone}"><svg viewBox="0 0 16 16"><path d={glyph[line.icon]} /></svg></span>
      <span>
        <b>{line.title}</b>
        <span class="detail">{line.detail}</span>
      </span>
    </div>
  {/each}
</Tile>

<style>
  .line {
    display: grid;
    grid-template-columns: 24px minmax(0, 1fr);
    gap: 10px;
    padding: 6px 0;
    font-size: 12px;
    color: var(--fg2);
  }

  .line + .line {
    border-top: 1px solid var(--separator);
  }

  b {
    display: block;
    color: var(--fg);
    font-weight: 600;
    font-size: 12.5px;
  }

  .g {
    display: grid;
    place-items: center;
    width: 24px;
    height: 24px;
    border-radius: 50%;
    background: var(--card-strong);
  }

  .g svg {
    width: 12px;
    height: 12px;
    fill: currentColor;
  }

  .empty {
    margin: 0;
    font-size: 12px;
    color: var(--fg2);
  }
</style>
