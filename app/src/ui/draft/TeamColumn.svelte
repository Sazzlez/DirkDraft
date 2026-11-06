<script lang="ts">
  import type { ChampionRef, SlotView } from '../../core/view';
  import { LANE_NAMES } from '../../core/view';
  import { percent } from '../../core/format';
  import Icon from '../Icon.svelte';

  let {
    side,
    title,
    slots,
    bans,
    usesLanes,
  }: { side: 'ally' | 'enemy'; title: string; slots: SlotView[]; bans: ChampionRef[]; usesLanes: boolean } = $props();
</script>

<div class="team {side}">
  <div class="head">
    <span>{title}</span>
    {#if usesLanes}
      <span class="bans" aria-label="Banns">
        {#each Array(5) as _, i (i)}
          {@const ban = bans[i]}
          <Icon kind="champion" champion={ban ?? null} size={16} grey={!!ban} title={ban ? `gebannt: ${ban.name}` : 'kein Bann'} />
        {/each}
      </span>
    {/if}
  </div>
  <div class="slots">
    {#each slots as slot (slot.cellId)}
      <div class="slot" class:me={slot.isMe} class:target={slot.isTarget && !slot.isMe} class:open={!slot.champion}>
        <Icon kind="champion" champion={slot.champion} size={44} dim={slot.hovering} />
        <span class="name">{slot.champion?.name ?? 'offen'}</span>
        <span class="who">{slot.label}</span>
        {#if usesLanes && slot.lane}
          <span class="lane" class:warn={slot.laneUncertain}>
            {LANE_NAMES[slot.lane]}{#if slot.laneConfidence !== null}<span class="conf num" class:only-uncertain={!slot.laneUncertain}>{percent(slot.laneConfidence, 0)}</span>{/if}
          </span>
          {#if slot.laneUncertain}<button class="fix">ändern</button>{/if}
        {/if}
        {#if slot.warning}<span class="warning">{slot.warning}</span>{/if}
      </div>
    {/each}
  </div>
</div>

<style>
  .head {
    display: flex;
    align-items: center;
    justify-content: space-between;
    padding: 0 4px 8px;
    font-size: 10.5px;
    font-weight: 600;
    color: var(--fg2);
    text-transform: uppercase;
    letter-spacing: 0.06em;
  }

  .bans {
    display: flex;
    gap: 3px;
  }

  .slots {
    display: grid;
    grid-template-columns: repeat(5, minmax(0, 1fr));
    gap: 6px;
  }

  .slot {
    display: flex;
    flex-direction: column;
    align-items: center;
    gap: 1px;
    padding: 10px 4px 9px;
    border-radius: var(--r-m);
    background: var(--card);
    text-align: center;
    min-width: 0;
  }

  .slot :global(.icon) {
    margin-bottom: 4px;
  }

  .slot.me {
    background: var(--blue-wash);
    box-shadow: inset 0 0 0 1.5px var(--blue);
  }

  .slot.target {
    box-shadow: inset 0 0 0 1.5px var(--teal);
  }

  .name {
    font-size: 12.5px;
    font-weight: 600;
    max-width: 100%;
    white-space: nowrap;
    overflow: hidden;
    text-overflow: ellipsis;
  }

  .open .name {
    color: var(--fg3);
    font-weight: 500;
  }

  .me .name {
    color: var(--teal);
  }

  .who,
  .lane {
    font-size: 10.5px;
    color: var(--fg2);
    white-space: nowrap;
    overflow: hidden;
    text-overflow: ellipsis;
    max-width: 100%;
  }

  .who {
    color: var(--fg3);
  }

  .lane.warn {
    color: var(--orange);
  }

  .conf {
    margin-left: 0.35em;
  }

  .fix {
    font-size: 10px;
    font-weight: 500;
    color: var(--blue);
  }

  .warning {
    font-size: 10px;
    color: var(--red);
  }

  @container app (max-width: 1099px) {
    .slot :global(.icon) {
      --s: 36px !important;
    }

    .slot {
      padding: 8px 3px;
    }

    .conf.only-uncertain {
      display: none;
    }

    .who {
      display: none;
    }
  }
</style>
