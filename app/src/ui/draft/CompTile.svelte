<script lang="ts">
  import type { CompView } from '../../core/view';
  import GapSentence from './GapSentence.svelte';
  import { percent } from '../../core/format';
  import Tile from '../Tile.svelte';

  let { comp }: { comp: CompView } = $props();
</script>

<Tile title="Comp-Vergleich">
  {#if comp.gap}<p class="gap tone-{comp.gap.tone}"><GapSentence line={comp.gap} /></p>{/if}
  {#if comp.timing}<p class="timing" title={comp.timing.hint}>{comp.timing.text}</p>{/if}
  {#if comp.plans}
    <p class="plans" title="Grobe Einordnung aus den Champion-Merkmalen, noch nicht gemessen; zählt nicht in die Prozentzahl.">
      <b>{comp.plans.text}</b>
      {comp.plans.detail}
    </p>
  {/if}

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

  {#if comp.enemyGap}<p class="verdict tone-{comp.enemyGap.tone}"><GapSentence line={comp.enemyGap} /></p>{/if}
</Tile>

<style>
  .grid {
    display: grid;
    grid-template-columns: max-content auto auto;
    justify-content: space-between;
    /* Two side tiles share the wide side column (about 180 px inside each): label and two rows of
       five dots have to fit there without running over the tile's edge. */
    gap: 8px 8px;
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
    gap: 2px;
  }

  .dots i {
    width: 7px;
    height: 7px;
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

  /* The line the next pick answers: above the dots, larger than the enemy's line below. */
  .gap {
    margin: 0 0 12px;
    padding-bottom: 10px;
    border-bottom: 1px solid var(--separator);
    font-size: 13px;
    font-weight: 600;
    line-height: 1.35;
  }

  .plans {
    margin: -2px 0 12px;
    font-size: 12px;
    line-height: 1.4;
    color: var(--fg2);
  }

  .plans b {
    display: block;
    color: var(--fg);
    font-weight: 600;
  }

  .timing {
    margin: -4px 0 12px;
    font-size: 12px;
    color: var(--orange);
  }

  .verdict {
    margin: 12px 0 0;
    padding-top: 10px;
    border-top: 1px solid var(--separator);
    font-size: 12px;
    font-weight: 500;
  }
</style>
