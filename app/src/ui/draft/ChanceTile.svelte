<script lang="ts">
  import type { DraftChanceView } from '../../core/view';
  import Tile from '../Tile.svelte';

  let { chance }: { chance: DraftChanceView } = $props();

  // Half circle from 40 % (left) to 60 % (right): drafts live within a few points of even, and a
  // 0–100 dial would show every one of them as "the middle".
  const RANGE = 0.1;
  const angle = $derived(chance.ally === null ? 0 : Math.max(-1, Math.min(1, (chance.ally - 0.5) / RANGE)) * 90);
  const arc = 'M 14 76 A 61 61 0 0 1 136 76';
</script>

<Tile title="Draft-Chance">
  <div class="gauge">
    <svg viewBox="0 0 150 84" aria-hidden="true">
      <path d={arc} class="track" />
      {#if chance.ally !== null}
        <path d={arc} class="fill" pathLength="100" stroke-dasharray="{50 + angle / 1.8} 100" />
        <line x1="75" y1="76" x2="75" y2="22" class="needle" transform="rotate({angle} 75 76)" />
      {/if}
      <text x="75" y="64" class="big num">{chance.allyText}</text>
      <text x="75" y="80" class="small">Gegner {chance.enemyText}</text>
    </svg>
    <p class="verdict">{chance.verdict}.</p>
  </div>
</Tile>

<style>
  .gauge {
    display: flex;
    flex-direction: column;
    align-items: center;
  }

  svg {
    width: 160px;
    height: 90px;
  }

  path {
    fill: none;
    stroke-width: 8;
    stroke-linecap: round;
  }

  .track {
    stroke: var(--track);
  }

  .fill {
    stroke: var(--blue);
  }

  .needle {
    stroke: var(--fg4);
    stroke-width: 1.5;
    opacity: 0;
  }

  .big {
    font: 700 21px var(--font);
    fill: var(--fg);
    text-anchor: middle;
  }

  .small {
    font: 500 10.5px var(--font);
    fill: var(--fg2);
    text-anchor: middle;
  }

  .verdict {
    margin: 6px 0 0;
    font-size: 12.5px;
    font-weight: 600;
  }
</style>
