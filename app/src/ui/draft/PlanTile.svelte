<script lang="ts">
  import type { PlanChoiceView } from '../../core/view';
  import { choosePlan } from '../../live/bridge';
  import Tile from '../Tile.svelte';

  // D4: the plan a fixed five chooses for this draft. A tile of its own, at the top of the side
  // column: asked as a question until a plan is chosen, calm once one is.
  let { plan }: { plan: PlanChoiceView } = $props();
</script>

<Tile title={plan.chosen ? `Euer Plan · ${plan.chosen}` : 'Euer Plan'}>
  <div class="choose" class:set={!!plan.chosen}>
    {#if !plan.chosen}
      <p class="ask">
        <b>Was wollt ihr spielen?</b>
        Klickt einen Plan an. Picks, die dazu passen, werden markiert und leicht bevorzugt.
      </p>
    {/if}
    <div class="opts" role="group" aria-label="Plan für diesen Draft wählen">
      {#each plan.options as option (option.plan)}
        <button
          class:active={option.active}
          aria-pressed={option.active}
          title={option.active ? 'Wahl aufheben — dann gilt wieder der erkannte Plan' : `${option.label} als Plan für diesen Draft wählen`}
          onclick={() => choosePlan(option.active ? null : option.plan)}
        >
          <b>{option.label}</b>
          <small>{option.wants}</small>
        </button>
      {/each}
    </div>
  </div>
  {#if plan.chosen}
    <p class="note">Passende Picks tragen „passt zum gewählten Plan“. Ein Klick auf einen anderen Plan wechselt, ein Klick auf den gewählten hebt die Wahl auf.</p>
  {:else}
    <p class="note">Ohne Wahl bleibt alles wie bisher: Der erkannte Plan steht nur als Hinweis im Comp-Vergleich.</p>
  {/if}
</Tile>

<style>
  /* Set apart until a plan is chosen, so nobody misses that there is something to click. */
  .choose {
    padding: 12px;
    border-radius: var(--r-m);
    background: var(--blue-wash);
    box-shadow: inset 0 0 0 1px var(--blue-line);
  }

  .choose.set {
    padding: 0;
    background: none;
    box-shadow: none;
  }

  .ask {
    margin: 0 0 10px;
    font-size: 12.5px;
    line-height: 1.4;
    color: var(--fg);
  }

  .ask b {
    display: block;
    font-size: 14px;
    font-weight: 600;
    color: var(--blue-text);
  }

  .opts {
    display: grid;
    grid-template-columns: repeat(auto-fit, minmax(128px, 1fr));
    gap: 8px;
  }

  button {
    display: flex;
    flex-direction: column;
    gap: 2px;
    min-height: 52px;
    padding: 8px 10px;
    border-radius: var(--r-m);
    background: var(--card);
    box-shadow: inset 0 0 0 1px color-mix(in srgb, var(--blue-text) 60%, transparent);
    text-align: left;
    cursor: pointer;
  }

  button b {
    font-size: 13px;
    font-weight: 600;
    color: var(--blue-text);
  }

  button small {
    font: var(--t-caption);
    font-size: 11px;
    line-height: 1.3;
    color: var(--fg2);
  }

  button:hover {
    background: var(--card-hover);
  }

  .choose.set button:not(.active) {
    box-shadow: inset 0 0 0 1px var(--separator);
  }

  .choose.set button:not(.active) b {
    color: var(--fg2);
  }

  button.active,
  button.active:hover {
    background: var(--blue-strong);
    box-shadow: none;
  }

  button.active b,
  button.active small {
    color: #fff;
  }

  .note {
    margin: 8px 0 0;
    font: var(--t-caption);
    color: var(--fg3);
  }
</style>
