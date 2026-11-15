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
    {#if comp.allyDamage || comp.enemyDamage}
      <span></span>
      <span class="key"><i class="ad"></i>AD <i class="ap"></i>AP</span>
    {/if}
  </div>

  {#if comp.enemyGap}<p class="verdict tone-{comp.enemyGap.tone}"><GapSentence line={comp.enemyGap} /></p>{/if}
</Tile>

<style>
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

  .key {
    display: flex;
    align-items: center;
    gap: 5px;
    font-size: 11px;
  }

  .key i {
    width: 8px;
    height: 8px;
    border-radius: 50%;
  }

  .key i.ap {
    margin-left: 6px;
  }

  .none {
    color: var(--fg3);
  }

  /* The line the next pick answers: on top, larger than the enemy's line below. */
  .gap {
    margin: 0 0 12px;
    padding-bottom: 10px;
    border-bottom: 1px solid var(--separator);
    font-size: 13px;
    font-weight: 600;
    line-height: 1.35;
  }

  /* Open: a blue box, blue being what can be clicked (label 5,0:1 dark, 4,7:1 light). Chosen: calm,
     only the chosen plan stays blue. */
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
    color: var(--orange-text);
  }

  .verdict {
    margin: 12px 0 0;
    padding-top: 10px;
    border-top: 1px solid var(--separator);
    font-size: 12px;
    font-weight: 500;
  }
</style>
