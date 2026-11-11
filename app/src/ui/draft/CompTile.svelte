<script lang="ts">
  import type { CompView } from '../../core/view';
  import GapSentence from './GapSentence.svelte';
  import { choosePlan } from '../../live/bridge';
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
  {#if comp.planChoice}
    <!-- Set apart until a plan is chosen, so it is not forgotten; calm once one is. -->
    <div class="choose" class:set={comp.planChoice.some((option) => option.active)} role="group" aria-label="Plan für diesen Draft wählen">
      <span class="lbl">{comp.planChoice.some((option) => option.active) ? 'Euer Plan für diesen Draft' : 'Plan für diesen Draft wählen'}</span>
      <span class="opts">
        {#each comp.planChoice as option (option.plan)}
          <button
            class:active={option.active}
            aria-pressed={option.active}
            title={option.active ? 'Wahl aufheben — dann gilt wieder der erkannte Plan' : `${option.label} als Plan für diesen Draft wählen`}
            onclick={() => choosePlan(option.active ? null : option.plan)}>{option.label}</button
          >
        {/each}
      </span>
    </div>
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
    {#if comp.allyDamage || comp.enemyDamage}
      <span></span>
      <span class="key"><i class="ad"></i>AD <i class="ap"></i>AP</span>
    {/if}
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

  /* The line the next pick answers: above the dots, larger than the enemy's line below. */
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
  .choose {
    margin: -2px 0 12px;
    padding: 10px;
    border-radius: var(--r-m);
    background: var(--blue-wash);
    box-shadow: inset 0 0 0 1px var(--blue-line);
  }

  .choose.set {
    background: var(--card);
    box-shadow: inset 0 0 0 1px var(--separator);
  }

  .choose .lbl {
    display: block;
    margin-bottom: 8px;
    font-size: 12.5px;
    font-weight: 600;
    color: var(--blue-text);
  }

  .choose.set .lbl {
    color: var(--fg2);
  }

  .opts {
    display: flex;
    flex-wrap: wrap;
    gap: 6px;
  }

  .choose button {
    min-height: 28px;
    padding: 4px 12px;
    border-radius: 999px;
    box-shadow: inset 0 0 0 1px color-mix(in srgb, var(--blue-text) 60%, transparent);
    color: var(--blue-text);
    font-size: 12px;
    font-weight: 600;
  }

  .choose button:hover {
    background: var(--blue-wash);
  }

  .choose.set button:not(.active) {
    box-shadow: inset 0 0 0 1px var(--separator);
    color: var(--fg2);
  }

  .choose.set button:not(.active):hover {
    background: var(--card-hover);
    color: var(--fg);
  }

  .choose button.active {
    background: var(--blue-strong);
    box-shadow: none;
    color: #fff;
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
