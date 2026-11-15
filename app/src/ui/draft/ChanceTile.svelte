<script lang="ts">
  import type { DraftChanceView } from '../../core/view';
  import Tile from '../Tile.svelte';

  let { chance }: { chance: DraftChanceView } = $props();

  // One line: the number beside the title, who is ahead under it. The dial and the open list of
  // parts are gone (15.11.2026) — the model calls about 54 % of games, and in the loading screen
  // nothing can be changed any more. What the chance is made of stays one click away.
  let open = $state(false);
</script>

<Tile title="Draft-Chance">
  {#snippet accessory()}
    {#if chance.ally !== null}<b class="value num">{chance.allyText}</b>{/if}
  {/snippet}
  <p class="line">
    {chance.verdict}.{#if chance.withPick}{' '}<span class="with">Mit {chance.withPick.name} <b class="num">{chance.withPick.text}</b>.</span>{/if}
  </p>
  {#if chance.parts}
    <button class="more" aria-expanded={open} onclick={() => (open = !open)}>
      Woraus?
      <svg viewBox="0 0 12 12" class:up={open}><path d="m3 4.5 3 3 3-3" /></svg>
    </button>
    {#if open}
      <ul class="parts">
        {#each chance.parts as part (part.label)}
          <li><span>{part.label}</span><b class="num" class:good={part.tone === 'good'} class:bad={part.tone === 'bad'}>{part.text}</b></li>
        {/each}
      </ul>
    {/if}
  {/if}
</Tile>

<style>
  .value {
    font-size: 17px;
    font-weight: 700;
    letter-spacing: -0.02em;
  }

  .line {
    margin: 0;
    font-size: 12.5px;
    color: var(--fg2);
  }

  /* Own data: the chance with the first pick of the list. */
  .with b {
    color: var(--fg);
    font-weight: 600;
  }

  .more {
    display: inline-flex;
    align-items: center;
    gap: 4px;
    min-height: 24px;
    margin-top: 4px;
    font-size: 11.5px;
    font-weight: 600;
    color: var(--blue-text);
  }

  .more svg {
    width: 12px;
    height: 12px;
    fill: none;
    stroke: currentColor;
    stroke-width: 1.5;
    stroke-linecap: round;
    stroke-linejoin: round;
  }

  .more svg.up {
    transform: rotate(180deg);
  }

  /* What the chance is made of, in points. */
  .parts {
    list-style: none;
    margin: 4px 0 0;
    padding: 0;
    display: grid;
    gap: 2px;
    font-size: 11.5px;
    color: var(--fg2);
  }

  .parts li {
    display: flex;
    justify-content: space-between;
    gap: 8px;
  }

  .parts b {
    font-weight: 600;
  }

  .parts .good {
    color: var(--green-text);
  }

  .parts .bad {
    color: var(--red-text);
  }
</style>
