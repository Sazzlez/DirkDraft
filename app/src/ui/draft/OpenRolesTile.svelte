<script lang="ts">
  import type { OpenRolesView } from '../../core/view';
  import { LANE_NAMES } from '../../core/view';
  import Tile from '../Tile.svelte';

  let { roles }: { roles: OpenRolesView } = $props();
</script>

<Tile title="Dem Gegner fehlt noch">
  {#if roles.lanes.length > 0}
    <div class="chips">
      {#each roles.lanes as lane (lane)}
        <span class="chip">{LANE_NAMES[lane]}</span>
      {/each}
    </div>
  {/if}
  <p>{roles.text} Banns auf diese Rollen treffen, was noch kommt.</p>
</Tile>

<style>
  .chips {
    display: flex;
    flex-wrap: wrap;
    gap: 6px;
  }

  .chip {
    display: inline-flex;
    align-items: center;
    height: 24px;
    padding: 0 10px;
    border-radius: 12px;
    background: var(--orange-wash);
    color: var(--orange);
    font-size: 12px;
    font-weight: 600;
  }

  p {
    margin: 10px 0 0;
    font-size: 12px;
    color: var(--fg2);
  }
</style>
