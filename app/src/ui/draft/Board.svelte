<script lang="ts">
  import type { BoardView } from '../../core/view';
  import { LANE_NAMES } from '../../core/view';
  import Icon from '../Icon.svelte';
  import Clock from './Clock.svelte';
  import TeamColumn from './TeamColumn.svelte';
  import { chooseLane, chooseSeat } from '../../live/bridge';

  let { board }: { board: BoardView } = $props();
</script>

<section class="board" aria-label="Draft-Board">
  <div class="phaseline">
    <Clock seconds={board.seconds} total={board.total} turn={board.turnText} mine={board.myTurn} steps={board.steps} compact />
    <span class="mode">{board.mode}{#if board.modeNote}<span class="note">{' · '}{board.modeNote}</span>{/if}</span>
  </div>

  <div class="grid">
    <TeamColumn side="ally" title="Dein Team" slots={board.allies} bans={board.allyBans} usesLanes={board.usesLanes} onSeat={chooseSeat} onLane={chooseLane} />
    <div class="clock">
      <Clock seconds={board.seconds} total={board.total} turn={board.turnText} mine={board.myTurn} steps={board.steps} />
      <span class="mode">{board.mode}{#if board.modeNote}<span class="note">{board.modeNote}</span>{/if}</span>
    </div>
    {#if board.bench}
      <div class="bench">
        <div class="bench-head">Bank · {board.bench.length}</div>
        <div class="bench-row">
          {#each board.bench as champion (champion.id)}
            <span class="bench-slot">
              <Icon kind="champion" {champion} size={44} />
              <span>{champion.name}</span>
            </span>
          {/each}
        </div>
      </div>
    {:else}
      <TeamColumn side="enemy" title="Gegner" slots={board.enemies} bans={board.enemyBans} usesLanes={board.usesLanes} onLane={chooseLane} />
    {/if}
  </div>

  <div class="strip" aria-label="Fünf gegen fünf">
    <div class="side">
      {#each board.allies as slot (slot.cellId)}
        {#if slot.canTarget}
          <button class="mini" class:me={slot.isMe} aria-pressed={slot.isTarget} onclick={() => chooseSeat(slot.cellId)} title="{slot.label}{slot.lane ? ` · ${LANE_NAMES[slot.lane]}` : ''} — Vorschläge für diesen Platz">
            <Icon kind="champion" champion={slot.champion} size={32} dim={slot.hovering} ring={slot.isMe ? 'me' : slot.isTarget ? 'target' : null} />
          </button>
        {:else}
          <span class="mini" class:me={slot.isMe} title="{slot.label}{slot.lane ? ` · ${LANE_NAMES[slot.lane]}` : ''}">
            <Icon kind="champion" champion={slot.champion} size={32} dim={slot.hovering} ring={slot.isMe ? 'me' : slot.isTarget ? 'target' : null} />
          </span>
        {/if}
      {/each}
    </div>
    {#if board.bench}
      <span class="vs">Bank</span>
      <div class="side">
        {#each board.bench as champion (champion.id)}
          <span class="mini" title={champion.name}><Icon kind="champion" {champion} size={32} /></span>
        {/each}
      </div>
    {:else}
      <span class="vs">vs</span>
      <div class="side">
        {#each board.enemies as slot (slot.cellId)}
          <span class="mini" title="{slot.label}{slot.lane ? ` · ${LANE_NAMES[slot.lane]}` : ''}">
            <Icon kind="champion" champion={slot.champion} size={32} dim={slot.hovering} />
            {#if slot.laneUncertain}<i class="flag" aria-label="Lane unsicher"></i>{/if}
          </span>
        {/each}
      </div>
    {/if}
  </div>
</section>

<style>
  .board {
    padding: 6px 20px 18px;
  }

  .grid {
    display: grid;
    grid-template-columns: minmax(0, 1fr) 160px minmax(0, 1fr);
    gap: 16px;
  }

  .clock {
    display: flex;
    flex-direction: column;
    align-items: center;
    justify-content: center;
    gap: 8px;
  }

  .mode {
    font-size: 11px;
    color: var(--fg2);
    text-align: center;
  }

  .clock .mode .note {
    display: block;
  }

  .note {
    color: var(--orange);
  }

  .bench-head {
    padding: 0 6px 10px;
    font: var(--t-head);
    font-size: 13px;
    color: var(--fg2);
  }

  .bench-row {
    display: grid;
    grid-template-columns: repeat(5, minmax(0, 1fr));
    gap: 6px;
  }

  .bench-slot {
    display: flex;
    flex-direction: column;
    align-items: center;
    gap: 7px;
    padding: 10px 4px;
    border-radius: var(--r-l);
    font-size: 12.5px;
    font-weight: 600;
    min-width: 0;
  }

  .bench-slot span {
    max-width: 100%;
    overflow: hidden;
    text-overflow: ellipsis;
    white-space: nowrap;
  }

  .phaseline,
  .strip {
    display: none;
  }

  /* Medium: clock and phase move into a line above a two-column board. */
  @container app (max-width: 1099px) {
    .phaseline {
      display: flex;
      align-items: center;
      justify-content: space-between;
      gap: 12px;
      margin-bottom: 10px;
    }

    .grid {
      grid-template-columns: minmax(0, 1fr) minmax(0, 1fr);
    }

    .clock {
      display: none;
    }
  }

  /* Narrow: the board becomes a strip of ten avatars, five against five. */
  @container app (max-width: 719px) {
    .board {
      padding: 0 12px 10px;
    }

    .grid {
      display: none;
    }

    .phaseline .mode {
      display: none;
    }

    .strip {
      display: flex;
      align-items: center;
      justify-content: space-between;
      gap: 6px;
      padding: 8px 10px;
      border-radius: var(--r-m);
      background: var(--card);
    }

    .side {
      display: flex;
      gap: 3px;
    }

    .vs {
      font-size: 11.5px;
      font-weight: 600;
      color: var(--fg3);
    }

    .mini {
      position: relative;
      display: inline-flex;
    }

    .flag {
      position: absolute;
      right: -1px;
      top: -1px;
      width: 8px;
      height: 8px;
      border-radius: 50%;
      background: var(--orange);
      box-shadow: 0 0 0 2px var(--window);
    }
  }

  @container app (max-width: 380px) {
    .strip :global(.icon) {
      --s: 28px !important;
    }
  }
</style>
