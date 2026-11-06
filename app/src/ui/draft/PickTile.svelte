<script lang="ts">
  import type { PickTileView } from '../../core/view';
  import Tile from '../Tile.svelte';
  import PickRow from './PickRow.svelte';
  import HoverRow from './HoverRow.svelte';

  let { tile }: { tile: PickTileView } = $props();

  let pool = $state<'all' | 'pool'>('all');
  let showAll = $state(false);

  const isBan = $derived(tile.kind === 'bans');
  const items = $derived(pool === 'pool' ? tile.items.filter((i) => i.inPool) : tile.items);
  const head = $derived(items.slice(0, 3));
  const rest = $derived(items.slice(3));
  const tiedShown = $derived(Math.min(tile.tied, 3));
  const tiedHidden = $derived(Math.max(0, tile.tied - 3));
</script>

<Tile title={tile.title} main>
  {#snippet accessory()}
    {#if !isBan && tile.poolAvailable}
      <div class="seg" role="tablist" aria-label="Filter">
        <button class:on={pool === 'all'} onclick={() => (pool = 'all')}>Alle</button>
        <button class:on={pool === 'pool'} onclick={() => (pool = 'pool')}>Mein Pool</button>
      </div>
    {/if}
  {/snippet}

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

  {#if tile.tied >= 2}
    <div class="group">Vorne gleichauf · {tile.tied}</div>
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
  .seg {
    display: inline-flex;
    gap: 2px;
    padding: 2px;
    border-radius: var(--r-s);
    background: var(--card-strong);
  }

  .seg button {
    padding: 3px 10px;
    border-radius: 6px;
    font-size: 11.5px;
    font-weight: 500;
    color: var(--fg2);
  }

  .seg button.on {
    background: var(--card-strong);
    color: var(--fg);
    box-shadow: var(--shadow);
  }

  .legend {
    display: flex;
    flex-wrap: wrap;
    gap: 12px;
    margin: -4px 0 8px;
    font-size: 10.5px;
    color: var(--fg2);
  }

  .legend i {
    display: inline-block;
    width: 8px;
    height: 8px;
    border-radius: 2px;
    margin-right: 5px;
    vertical-align: -0.5px;
  }

  .lane { background: var(--blue); }
  .matchup { background: var(--teal); }
  .synergy { background: var(--purple); }
  .comp { background: var(--orange); }

  .group {
    margin: 10px 0 2px;
    font-size: 10.5px;
    font-weight: 600;
    color: var(--green);
    text-transform: uppercase;
    letter-spacing: 0.06em;
  }

  .more {
    display: flex;
    align-items: center;
    gap: 6px;
    width: 100%;
    padding: 10px 0 4px;
    border-top: 1px solid var(--separator);
    color: var(--blue);
    font-size: 12.5px;
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
    margin: 10px 0 0;
    font-size: 11px;
    color: var(--fg3);
  }

  @container main (max-width: 469px) {
    .legend,
    .foot.bars {
      display: none;
    }
  }
</style>
