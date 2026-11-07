<script lang="ts">
  import type { ViewModel } from '../../core/view';
  import Board from './Board.svelte';
  import PickTile from './PickTile.svelte';
  import MatchupTile from './MatchupTile.svelte';
  import AramTile from './AramTile.svelte';
  import LobbyTile from './LobbyTile.svelte';
  import ChanceTile from './ChanceTile.svelte';
  import CompTile from './CompTile.svelte';
  import OpenRolesTile from './OpenRolesTile.svelte';
  import VerdictTile from './VerdictTile.svelte';

  let { view }: { view: ViewModel } = $props();
  const main = $derived(view.main);
</script>

{#if view.board}
  <Board board={view.board} />
{/if}

{#if view.board && view.data.chip}
  <p class="chip" role="note">{view.data.chip}</p>
{/if}

<div class="tiles" class:solo={view.side.length === 0}>
  <div class="main">
    {#if main.kind === 'picks' || main.kind === 'bans'}
      <PickTile tile={main.tile} />
    {:else if main.kind === 'matchup'}
      <MatchupTile matchup={main.matchup} build={main.build} canImport={view.canImport} />
    {:else if main.kind === 'aram'}
      <AramTile aram={main.aram} build={main.build} />
    {:else if main.kind === 'lobby'}
      <LobbyTile title={main.title} lines={main.lines} text={view.emptyText} />
    {/if}
  </div>

  {#if view.side.length > 0}
    <div class="side">
      {#each view.side as tile, i (i)}
        {#if tile.kind === 'chance'}
          <ChanceTile chance={tile.chance} />
        {:else if tile.kind === 'comp'}
          <CompTile comp={tile.comp} />
        {:else if tile.kind === 'openRoles'}
          <OpenRolesTile roles={tile.roles} />
        {:else if tile.kind === 'verdict'}
          <VerdictTile lines={tile.lines} />
        {/if}
      {/each}
    </div>
  {/if}
</div>

<style>
  .tiles {
    display: grid;
    grid-template-columns: minmax(0, 1.7fr) minmax(0, 1fr);
    gap: 14px;
    padding: 0 20px 20px;
    align-items: start;
  }

  .chip {
    margin: 0 20px 12px;
    padding: 6px 12px;
    border-radius: var(--r-m);
    background: var(--orange-wash);
    color: var(--orange);
    font: var(--t-caption);
  }

  .tiles.solo {
    grid-template-columns: minmax(0, 1fr);
    max-width: 640px;
    margin: 0 auto;
  }

  /* auto-fit: two tiles share the row instead of leaving a third column empty (medium width). */
  .side {
    display: grid;
    grid-template-columns: repeat(auto-fit, minmax(210px, 1fr));
    gap: 12px;
  }

  @container app (max-width: 1099px) {
    .tiles {
      grid-template-columns: minmax(0, 1fr);
    }
  }

  @container app (max-width: 719px) {
    .chip {
      margin: 0 12px 10px;
    }

    .tiles {
      padding: 0 12px 12px;
      gap: 10px;
    }

    .side {
      grid-template-columns: minmax(0, 1fr);
      gap: 10px;
    }
  }
</style>
