<script lang="ts">
  import type { PickTileView } from '../../core/view';
  import Tile from '../Tile.svelte';
  import PickRow from './PickRow.svelte';
  import HoverRow from './HoverRow.svelte';

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
  {#if !isBan}
    <div class="legend">
      <span><i class="lane"></i>Lane</span>
      <span><i class="matchup"></i>Matchup</span>
      <span><i class="synergy"></i>Synergie</span>
      <span><i class="comp"></i>Team-Bedarf</span>
    </div>
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
          {#if isBan}
            Weitere Banns · {rest.length}
          {:else if tiedHidden > 0}
            {tiedHidden} weitere gleichauf · {rest.length - tiedHidden} dahinter
          {:else}
            {rest.length} weitere
          {/if}
          <svg viewBox="0 0 12 12"><path d="m3 4.5 3 3 3-3" /></svg>
        </button>
      {/if}
    {/if}
  </div>

  {#if tile.footnote}
    <p class="foot" class:bars={!isBan}>{tile.footnote}</p>
  {/if}
</Tile>

<style>
  .legend {
    display: flex;
    flex-wrap: wrap;
    gap: 14px;
    margin: -4px 0 12px;
    font: var(--t-caption);
    color: var(--fg2);
  }

  .legend i {
    display: inline-block;
    width: 8px;
    height: 8px;
    border-radius: 50%;
    margin-right: 6px;
    vertical-align: 0;
  }

  .lane { background: var(--blue); }
  .matchup { background: var(--teal); }
  .synergy { background: var(--purple); }
  .comp { background: var(--orange); }

  .group {
    margin: 14px 4px 6px;
    font: var(--t-caption);
    font-weight: 600;
    color: var(--green);
  }

  .more {
    display: flex;
    align-items: center;
    gap: 6px;
    width: 100%;
    padding: 12px 4px 4px;
    color: var(--blue);
    font-size: 13px;
    font-weight: 500;
    text-align: left;
  }

  .more svg {
    width: 12px;
    height: 12px;
    margin-left: auto;
    fill: none;
    stroke: var(--fg3);
    stroke-width: 1.5;
  }

  .foot {
    margin: 12px 4px 0;
    font: var(--t-caption);
    font-weight: 400;
    color: var(--fg3);
  }

  @container main (max-width: 469px) {
    .legend,
    .foot.bars {
      display: none;
    }
  }
</style>
