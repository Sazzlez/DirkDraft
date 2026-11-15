<script lang="ts">
  // Team gegen Team, in the lane scale's place once the lane phase is over: both teams' levels and
  // item worth as the scoreboard shows them. Numbers and a split bar, no verdict — nothing says yet
  // what a level or 1.000 gold are worth to a team (core/strength teamStandOf).
  import type { TeamStandView } from '../../core/view';
  import Tile from '../Tile.svelte';

  let { team }: { team: TeamStandView } = $props();
</script>

<Tile title="Team gegen Team">
  <div class="sides"><span>Ihr</span><span>Gegner</span></div>
  {#each team.rows as row (row.label)}
    <div class="row">
      <div class="line num">
        <b>{row.mine}</b>
        <span class="k">{row.label}<span class="d tone-{row.tone}">{row.diff}</span></span>
        <b>{row.theirs}</b>
      </div>
      <div class="bar" role="img" aria-label="{row.label}: ihr {row.mine}, Gegner {row.theirs}">
        <i class="mine" style:flex-grow={row.share}></i>
        <i class="theirs" style:flex-grow={1 - row.share}></i>
      </div>
    </div>
  {/each}
  <p class="tiny">Level und Itemwert aller zehn Champions, wie im Scoreboard.</p>
</Tile>

<style>
  .sides {
    display: flex;
    justify-content: space-between;
    font-size: 11.5px;
    color: var(--fg2);
  }

  .row {
    padding: 8px 0 2px;
  }

  .line {
    display: grid;
    grid-template-columns: minmax(0, 1fr) auto minmax(0, 1fr);
    align-items: baseline;
    gap: 8px;
  }

  .line b {
    font-size: 15px;
  }

  .line b:last-child {
    text-align: right;
  }

  .k {
    display: inline-flex;
    align-items: baseline;
    gap: 6px;
    font-size: 11.5px;
    color: var(--fg2);
  }

  .d {
    font-weight: 600;
  }

  .d.tone-up { color: var(--green-text); }
  .d.tone-down { color: var(--red-text); }
  .d.tone-even { color: var(--fg3); }

  /* Both sides of the same sum: the own part from the left, the enemies' from the right. */
  .bar {
    display: flex;
    gap: 2px;
    height: 6px;
    margin-top: 6px;
    border-radius: 3px;
    overflow: hidden;
  }

  .bar i {
    display: block;
    min-width: 3px;
  }

  .bar .mine {
    background: var(--blue);
  }

  .bar .theirs {
    background: var(--red);
  }

  .tiny {
    margin: 10px 0 0;
    font-size: 11.5px;
    color: var(--fg3);
  }
</style>
