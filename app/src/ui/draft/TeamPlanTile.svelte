<script lang="ts">
  import type { TeamPlanView } from '../../core/view';
  import { LANE_NAMES } from '../../core/view';
  import Icon from '../Icon.svelte';
  import Tile from '../Tile.svelte';

  // D1, team mode: the open seats planned together, with who best picks first and last.
  let { team }: { team: TeamPlanView } = $props();
</script>

<Tile title={team.scope ? `Team-Plan · ${team.scope}` : 'Team-Plan'}>
  <div class="rows">
    {#each team.rows as row (row.cellId)}
      <div class="row" class:me={row.isMe}>
        <Icon kind="champion" champion={row.champion} size={30} />
        <span class="text">
          <span class="name">
            {row.champion.name}
            {#if row.order === 'first'}<span class="tag first">zuerst</span>{:else if row.order === 'last'}<span class="tag last">zuletzt</span>{/if}
          </span>
          <span class="who">{row.label}{row.lane ? ` · ${LANE_NAMES[row.lane]}` : ''}{row.flex ? ` · ${row.flex}` : ''}</span>
        </span>
        <b class="value num">{row.value}</b>
      </div>
    {/each}
  </div>
  {#if team.order}<p class="order">{team.order}</p>{/if}
  <p class="note">{team.note}</p>
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

  .row.me {
    background: var(--blue-wash);
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

  /* Seat, lane and flex lanes wrap rather than lose their end, in the narrow side column too. */
  .who {
    font: var(--t-caption);
    font-size: 11px;
    color: var(--fg2);
  }

  .tag {
    margin-left: 6px;
    padding: 0 6px;
    border-radius: 999px;
    font-size: 10.5px;
    font-weight: 600;
    vertical-align: 1px;
  }

  .tag.first {
    background: var(--green-wash);
    color: var(--green-text);
  }

  .tag.last {
    background: var(--orange-wash);
    color: var(--orange-text);
  }

  .value {
    font-size: 15px;
    font-weight: 700;
  }

  .order {
    margin: 10px 0 0;
    padding-top: 10px;
    border-top: 1px solid var(--separator);
    font-size: 12px;
    font-weight: 500;
  }

  .note {
    margin: 6px 0 0;
    font: var(--t-caption);
    color: var(--fg3);
  }
</style>
