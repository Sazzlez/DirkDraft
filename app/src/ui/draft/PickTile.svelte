<script lang="ts">
  import type { PickTileView } from '../../core/view';
  import Tile from '../Tile.svelte';
  import PickRow from './PickRow.svelte';
  import HoverRow from './HoverRow.svelte';
  import GapSentence from './GapSentence.svelte';

  let { tile }: { tile: PickTileView } = $props();

  let showAll = $state(false);

  const isBan = $derived(tile.kind === 'bans');
  const items = $derived(tile.items);
  const tied = $derived(tile.tied);
  const head = $derived(items.slice(0, 3));
  const rest = $derived(items.slice(3));
  const tiedShown = $derived(Math.min(tied, 3));
  const tiedHidden = $derived(Math.max(0, tied - 3));
</script>

<Tile title={tile.title} main>
  {#if tile.note?.kind === 'gap'}
    <p class="note gap tone-{tile.note.line.tone}"><GapSentence line={tile.note.line} /></p>
  {/if}

  {#if tile.hover}
    <HoverRow hover={tile.hover} />
  {/if}

  {#if tied >= 2}
    <div class="group">Vorne gleichauf · {tied}</div>
  {/if}

  <div class="rows">
    {#each head as item, i (item.champion.id)}
      <PickRow {item} ban={isBan} grouped={i < tiedShown} />
    {/each}

    {#if rest.length > 0}
      {#if showAll}
        {#each rest as item (item.champion.id)}
          <PickRow {item} ban={isBan} grouped={false} />
        {/each}
        <button class="more" onclick={() => (showAll = false)}>Weniger zeigen</button>
      {:else}
        <button class="more" onclick={() => (showAll = true)}>
          {#if tiedHidden > 0}
            {tiedHidden} weitere gleichauf · {rest.length - tiedHidden} dahinter
          {:else}
            {rest.length} weitere
          {/if}
          <svg viewBox="0 0 12 12"><path d="m3 4.5 3 3 3-3" /></svg>
        </button>
      {/if}
    {/if}
  </div>

  {#if tile.even}
    <p class="even">{tile.even}</p>
  {/if}

  {#if tile.footnote}
    <p class="foot">{tile.footnote}</p>
  {/if}
</Tile>

<style>
  /* The pick position (B1): one line above the list, before the hover. */
  .note {
    margin: 0 0 12px;
    padding: 9px 12px;
    border-radius: var(--r-m);
    background: var(--card);
    font-size: 12.5px;
    line-height: 1.4;
  }

  .note.gap {
    font-size: 13px;
    font-weight: 600;
  }

  .group {
    margin: 14px 4px 6px;
    font: var(--t-caption);
    font-weight: 600;
    color: var(--green-text);
  }

  .more {
    display: flex;
    align-items: center;
    gap: 6px;
    width: 100%;
    padding: 12px 4px 4px;
    color: var(--blue-text);
    font-size: 13px;
    font-weight: 500;
    text-align: left;
  }

  .more svg {
    width: 12px;
    height: 12px;
    margin-left: auto;
    fill: none;
    stroke: var(--fg4);
    stroke-width: 1.5;
  }

  /* Bans that hardly differ: said plainly under the three rows. */
  .even {
    margin: 12px 4px 0;
    font-size: 13px;
    font-weight: 500;
    color: var(--fg2);
  }

  .foot {
    margin: 12px 4px 0;
    font: var(--t-caption);
    font-weight: 400;
    color: var(--fg3);
  }
</style>
