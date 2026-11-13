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
  import PlanTile from './PlanTile.svelte';
  import TeamPlanTile from './TeamPlanTile.svelte';
  import TeamBansTile from './TeamBansTile.svelte';

  let { view }: { view: ViewModel } = $props();
  const main = $derived(view.main);

  // What the party picks and bans belongs with the picks, in the main column: the chosen plan above
  // the list, the team plan and the team bans under it. The side column keeps the comparison of the
  // two comps, and a chance that cannot be reckoned yet takes no tile.
  const plan = $derived(view.side.find((tile) => tile.kind === 'plan'));
  const party = $derived(view.side.filter((tile) => tile.kind === 'team' || tile.kind === 'teamBans'));
  const side = $derived(
    view.side.filter(
      (tile) => tile.kind !== 'plan' && tile.kind !== 'team' && tile.kind !== 'teamBans' && !(tile.kind === 'chance' && tile.chance.ally === null),
    ),
  );
</script>

{#if view.board}
  <Board board={view.board} />
{/if}

{#if view.board && view.data.chip}
  <p class="chip" role="note">{view.data.chip}</p>
{/if}

<div class="tiles" class:solo={side.length === 0}>
  <div class="main">
    {#if plan?.kind === 'plan'}
      <PlanTile plan={plan.plan} />
    {/if}
    {#if main.kind === 'picks' || main.kind === 'bans'}
      <PickTile tile={main.tile} />
    {:else if main.kind === 'matchup'}
      <MatchupTile matchup={main.matchup} build={main.build} canImport={view.canImport} />
    {:else if main.kind === 'aram'}
      <AramTile aram={main.aram} build={main.build} />
    {:else if main.kind === 'lobby'}
      <LobbyTile title={main.title} lines={main.lines} text={view.emptyText} />
    {/if}
    {#each party as tile, i (i)}
      {#if tile.kind === 'team'}
        <TeamPlanTile team={tile.team} />
      {:else if tile.kind === 'teamBans'}
        <TeamBansTile bans={tile.bans} />
      {/if}
    {/each}
  </div>

  {#if side.length > 0}
    <div class="side" class:single={side.length === 1}>
      {#each side as tile, i (i)}
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

  .main {
    display: flex;
    flex-direction: column;
    gap: 12px;
    min-width: 0;
  }

  .chip {
    margin: 0 20px 12px;
    padding: 6px 12px;
    border-radius: var(--r-m);
    background: var(--orange-wash);
    color: var(--orange-text);
    font: var(--t-caption);
  }

  .tiles.solo {
    grid-template-columns: minmax(0, 1fr);
    max-width: 640px;
    margin: 0 auto;
  }

  /* Two columns of at least 210 px, filled top to bottom and balanced: a grid row
     stretched to its taller tile and left a hole under the shorter one. */
  .side {
    columns: 210px 2;
    column-gap: 12px;
    margin-bottom: -12px;
  }

  .side > :global(*) {
    break-inside: avoid;
    margin-bottom: 12px;
  }

  /* A tile alone takes the whole width. */
  .side.single {
    columns: auto;
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

    .main {
      gap: 10px;
    }

    .side {
      columns: 1;
      margin-bottom: -10px;
    }

    .side > :global(*) {
      margin-bottom: 10px;
    }
  }
</style>
