<script lang="ts">
  import type { TeamBansView } from '../../core/view';
  import { LANE_NAMES } from '../../core/view';
  import Icon from '../Icon.svelte';
  import Tile from '../Tile.svelte';

  // D3, team mode: the team's bans as one set, spread over the lanes.
  let { bans }: { bans: TeamBansView } = $props();
</script>

<Tile title={bans.scope ? `Team-Banns · ${bans.scope}` : 'Team-Banns'}>
  <div class="rows">
    {#each bans.rows as row (row.champion.id)}
      <div class="row" class:mine={row.mine} class:set={!!row.by}>
        <Icon kind="champion" champion={row.champion} size={30} dim={!!row.by} />
        <span class="text">
          <span class="name">
            {row.champion.name}
            {#if row.mine}<span class="tag">deiner</span>{/if}
          </span>
          <span class="who">{row.lane ? LANE_NAMES[row.lane] : 'Lane offen'}{row.by ? ` · ${row.by}` : ''}</span>
        </span>
        <b class="value num">{row.value}</b>
      </div>
    {/each}
  </div>
  <p class="note">{bans.note}</p>
</Tile>

<style>
  .rows {
    display: flex;
    flex-direction: column;
    gap: 2px;
  }

  .row {
    display: grid;
    grid-template-columns: 30px minmax(0, 1fr) auto;
    gap: 10px;
    align-items: center;
    padding: 5px 6px;
    border-radius: var(--r-m);
  }

  .row.mine {
    background: var(--blue-wash);
  }

  .row.set .name,
  .row.set .value {
    color: var(--fg3);
  }

  .text {
    display: flex;
    flex-direction: column;
    min-width: 0;
  }

  .name {
    font-size: 13px;
    font-weight: 600;
    white-space: nowrap;
    overflow: hidden;
    text-overflow: ellipsis;
  }

  .who {
    font: var(--t-caption);
    font-size: 11px;
    color: var(--fg2);
    white-space: nowrap;
    overflow: hidden;
    text-overflow: ellipsis;
  }

  .tag {
    margin-left: 6px;
    padding: 0 6px;
    border-radius: 999px;
    background: var(--blue-strong);
    color: #fff;
    font-size: 10.5px;
    font-weight: 600;
    vertical-align: 1px;
  }

  .value {
    font-size: 14px;
    font-weight: 700;
  }

  .note {
    margin: 8px 0 0;
    font: var(--t-caption);
    color: var(--fg3);
  }
</style>
