<script lang="ts">
  import type { ChampionRef, Lane, SlotView } from '../../core/view';
  import { LANES, LANE_NAMES } from '../../core/view';
  import { percent } from '../../core/format';
  import Icon from '../Icon.svelte';

  let {
    side,
    title,
    slots,
    bans,
    usesLanes,
    onSeat,
    onLane,
  }: {
    side: 'ally' | 'enemy';
    title: string;
    slots: SlotView[];
    bans: ChampionRef[];
    usesLanes: boolean;
    /** A click on an ally seat: its list until the clock moves on (1.5's slot click). */
    onSeat?: (cellId: number) => void;
    /** The lane picker: a lane by hand, or null for "automatisch". */
    onLane?: (cellId: number, lane: Lane | null) => void;
  } = $props();

  /** The seat whose lane menu is open. */
  let menuFor = $state<number | null>(null);

  function setLane(cellId: number, lane: Lane | null) {
    menuFor = null;
    onLane?.(cellId, lane);
  }
</script>

<svelte:window onclick={(e) => { if (menuFor !== null && !(e.target as Element | null)?.closest('.lanepick')) menuFor = null; }} />

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
        {#snippet face()}
          <span class="avatar">
            <Icon kind="champion" champion={slot.champion} size={48} dim={slot.hovering} />
            {#if slot.hovering}<span class="badge">Hover</span>{/if}
          </span>
          <span class="name">{slot.champion?.name ?? 'offen'}</span>
          <span class="who">{slot.label}</span>
        {/snippet}
        {#if slot.canTarget && onSeat}
          <button
            class="face"
            onclick={() => onSeat(slot.cellId)}
            aria-pressed={slot.isTarget}
            title={slot.isTarget ? `Die Vorschläge gelten ${slot.isMe ? 'dir' : slot.label}` : `Vorschläge für ${slot.isMe ? 'dich' : slot.label} zeigen`}
          >
            {@render face()}
          </button>
        {:else}
          <span class="face">{@render face()}</span>
        {/if}
        {#if usesLanes && (slot.lane || slot.laneEditable)}
          <span class="lanepick">
            {#if slot.laneEditable && onLane}
              <button
                class="lane edit"
                class:warn={slot.laneUncertain}
                class:manual={slot.laneManual}
                onclick={() => (menuFor = menuFor === slot.cellId ? null : slot.cellId)}
                aria-haspopup="menu"
                aria-expanded={menuFor === slot.cellId}
                title={slot.laneManual ? 'Von dir festgelegt' : 'Lane ändern'}
              >
                {slot.lane ? LANE_NAMES[slot.lane] : 'Lane?'}{#if slot.laneConfidence !== null && !slot.laneManual}<span class="conf num" class:only-uncertain={!slot.laneUncertain}>{percent(slot.laneConfidence, 0)}</span>{/if}<svg viewBox="0 0 10 10" aria-hidden="true"><path d="M2.5 4 5 6.5 7.5 4" /></svg>
              </button>
              {#if menuFor === slot.cellId}
                <span class="menu" role="menu">
                  {#each LANES as lane (lane)}
                    <button role="menuitemradio" aria-checked={slot.laneManual && slot.lane === lane} class:on={slot.laneManual && slot.lane === lane} onclick={() => setLane(slot.cellId, lane)}>{LANE_NAMES[lane]}</button>
                  {/each}
                  <button role="menuitemradio" aria-checked={!slot.laneManual} class:on={!slot.laneManual} onclick={() => setLane(slot.cellId, null)}>automatisch</button>
                </span>
              {/if}
            {:else if slot.lane}
              <span class="lane" class:warn={slot.laneUncertain}>
                {LANE_NAMES[slot.lane]}{#if slot.laneConfidence !== null}<span class="conf num" class:only-uncertain={!slot.laneUncertain}>{percent(slot.laneConfidence, 0)}</span>{/if}
              </span>
            {/if}
          </span>
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
    position: relative;
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

  .face {
    display: flex;
    flex-direction: column;
    align-items: center;
    gap: 1px;
    width: 100%;
    min-width: 0;
  }

  button.face {
    border-radius: var(--r-m);
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

  .lanepick {
    position: relative;
    max-width: 100%;
  }

  .lane.edit {
    display: inline-flex;
    align-items: center;
    gap: 1px;
    padding: 0 3px;
    border-radius: 5px;
  }

  .lane.edit:hover {
    background: var(--card-strong);
    color: var(--fg);
  }

  .lane.edit svg {
    width: 9px;
    height: 9px;
    flex: none;
    fill: none;
    stroke: currentColor;
    stroke-width: 1.4;
    opacity: 0.7;
  }

  .lane.warn {
    color: var(--orange);
  }

  .lane.manual {
    color: var(--blue);
  }

  .conf {
    margin-left: 0.35em;
  }

  .menu {
    position: absolute;
    top: calc(100% + 4px);
    left: 50%;
    transform: translateX(-50%);
    z-index: 30;
    display: flex;
    flex-direction: column;
    gap: 1px;
    min-width: 116px;
    padding: 5px;
    border-radius: var(--r-m);
    background: var(--popover);
    -webkit-backdrop-filter: blur(30px);
    backdrop-filter: blur(30px);
    box-shadow: 0 12px 40px rgba(0, 0, 0, 0.35), var(--edge);
    text-align: left;
  }

  .menu button {
    padding: 5px 9px;
    border-radius: 6px;
    font-size: 12px;
  }

  .menu button.on {
    color: var(--blue);
    font-weight: 600;
  }

  .menu button:hover {
    background: var(--blue);
    color: #fff;
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
