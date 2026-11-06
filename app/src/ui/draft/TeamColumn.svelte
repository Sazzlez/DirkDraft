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
          <Icon kind="champion" champion={ban ?? null} size={18} grey={!!ban} title={ban ? `gebannt: ${ban.name}` : 'kein Bann'} />
        {/each}
      </span>
    {/if}
  </div>
  <div class="slots">
    {#each slots as slot (slot.cellId)}
      <div class="slot" class:me={slot.isMe} class:target={slot.isTarget && !slot.isMe} class:open={!slot.champion}>
        <span class="avatar">
          <Icon kind="champion" champion={slot.champion} size={48} dim={slot.hovering} />
          {#if slot.hovering}<span class="badge">Hover</span>{/if}
        </span>
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
    padding: 0 6px 10px;
    font: var(--t-head);
    font-size: 13px;
    color: var(--fg2);
  }

  .bans {
    display: flex;
    gap: 4px;
  }

  .slots {
    display: grid;
    grid-template-columns: repeat(5, minmax(0, 1fr));
    gap: 4px;
  }

  .slot {
    display: flex;
    flex-direction: column;
    align-items: center;
    gap: 1px;
    padding: 10px 4px;
    border-radius: var(--r-l);
    text-align: center;
    min-width: 0;
    transition: background 0.2s var(--ease);
  }

  .slot:hover {
    background: var(--card-hover);
  }

  .avatar {
    position: relative;
    margin-bottom: 7px;
    border-radius: 50%;
    box-shadow: 0 4px 14px rgba(0, 0, 0, 0.35);
  }

  .open .avatar {
    box-shadow: none;
  }

  .badge {
    position: absolute;
    left: 50%;
    bottom: -6px;
    transform: translateX(-50%);
    padding: 1px 6px;
    border-radius: 999px;
    background: var(--blue);
    color: #fff;
    font-size: 9.5px;
    font-weight: 600;
  }

  .slot.me {
    background: var(--blue-wash);
    box-shadow: inset 0 0 0 1px var(--blue-line);
  }

  .slot.target {
    background: var(--card);
    box-shadow: inset 0 0 0 1px var(--separator);
  }

  .name {
    font-size: 13px;
    font-weight: 600;
    letter-spacing: -0.01em;
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
    color: var(--blue);
  }

  .who,
  .lane {
    font: var(--t-caption);
    font-size: 11px;
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
    margin-top: 2px;
    font-size: 11px;
    font-weight: 600;
    color: var(--blue);
  }

  .warning {
    font-size: 10.5px;
    color: var(--red);
  }

  @container app (max-width: 1099px) {
    .slot :global(.icon) {
      --s: 38px !important;
    }

    .slot {
      padding: 8px 2px;
    }

    .conf.only-uncertain,
    .who {
      display: none;
    }
  }
</style>
