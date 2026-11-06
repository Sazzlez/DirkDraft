<script lang="ts">
  import type { CompView } from '../../core/view';
  import { percent } from '../../core/format';
  import Tile from '../Tile.svelte';

  let { comp }: { comp: CompView } = $props();
</script>

<Tile title="Comp-Vergleich">
  <div class="grid">
    <span class="h"></span>
    <span class="h">Ihr</span>
    <span class="h">Gegner</span>
    {#each comp.rows as row (row.label)}
      <span>{row.label}</span>
      <span class="dots ally" aria-label="{row.ally} von {row.max}">
        {#each Array(row.max) as _, i (i)}<i class:f={i < row.ally}></i>{/each}
      </span>
      <span class="dots enemy" aria-label="{row.enemy} von {row.max}">
        {#each Array(row.max) as _, i (i)}<i class:f={i < row.enemy}></i>{/each}
      </span>
    {/each}
  </div>

  <div class="dmg">
    {#each [{ label: 'Ihr', d: comp.allyDamage }, { label: 'Gegner', d: comp.enemyDamage }] as side (side.label)}
      <span>{side.label}</span>
      {#if side.d}
        <span class="dbar" title="{percent(side.d.physical, 0)} AD · {percent(side.d.magic, 0)} AP">
          <i class="ad" style:width="{side.d.physical * 100}%"></i>
          <i class="ap" style:width="{side.d.magic * 100}%"></i>
        </span>
      {:else}
        <span class="none">noch nichts aufgedeckt</span>
      {/if}
    {/each}
  </div>

  <p class="verdict tone-{comp.verdictTone}">{comp.verdict}</p>
</Tile>

<style>
  .grid {
    display: grid;
    grid-template-columns: max-content auto auto;
    justify-content: space-between;
    gap: 8px 14px;
    align-items: center;
    font-size: 12px;
  }

  .grid > span:nth-child(3n + 1) {
    white-space: nowrap;
  }

  .h {
    font: var(--t-caption);
    color: var(--fg3);
  }

  .dots {
    display: flex;
    gap: 3px;
  }

  .dots i {
    width: 8px;
    height: 8px;
    border-radius: 50%;
    background: var(--track);
  }

  .ally i.f {
    background: var(--blue);
  }

  .enemy i.f {
    background: var(--red);
  }

  .dmg {
    display: grid;
    grid-template-columns: 48px minmax(0, 1fr);
    gap: 6px 8px;
    align-items: center;
    margin-top: 12px;
    font-size: 11px;
    color: var(--fg2);
  }

  .dbar {
    display: flex;
    height: 6px;
    border-radius: 3px;
    overflow: hidden;
    background: var(--track);
  }

  .ad {
    background: var(--orange);
  }

  .ap {
    background: var(--purple);
  }

  .none {
    color: var(--fg3);
  }

  .verdict {
    margin: 12px 0 0;
    padding-top: 10px;
    border-top: 1px solid var(--separator);
    font-size: 12px;
    font-weight: 500;
  }
</style>
