<script lang="ts">
  // Euer Team im Teamfight (docs/teamfight-skala.md): the lane scale's vertical track for team against
  // team — up = the own team would win a fight of five against five more often. Below it the measured
  // share of the step and the parts, the largest first. It describes, it never tells what to do, and it
  // moves without a sound or a flash — Riot's rules.
  import type { TeamStrengthView } from '../../core/view';
  import Tile from '../Tile.svelte';

  let { team }: { team: TeamStrengthView } = $props();
  const STEPS = [3, 2, 1, 0, -1, -2, -3];
  const at = $derived(((3 - Math.max(-3, Math.min(3, team.value))) / 6) * 100);
  const tone = $derived(team.value >= 0.5 ? 'up' : team.value <= -0.5 ? 'down' : 'even');
</script>

<Tile title="Euer Team im Teamfight">
  <div class="body">
    <div class="scale" aria-label="Skala von schwächer bis stärker">
      <span class="end">stärker</span>
      <div class="track">
        <span class="half top"></span>
        <span class="half bottom"></span>
        {#each STEPS as step (step)}
          <span class="tick" class:zero={step === 0} style="top: {((3 - step) / 6) * 100}%"></span>
        {/each}
        <span class="marker tone-{tone}" style="top: {at}%"></span>
      </div>
      <span class="end">schwächer</span>
    </div>

    <div class="info">
      <div>
        <b class="label tone-{tone}">{team.label}</b>
        <p class="odds">{team.odds}</p>
      </div>
      <div class="rows num">
        {#each team.rows as row (row.label)}
          <span class="k">{row.label}</span>
          <span class="v">{row.text}{#if row.diff}<span class="d">{row.diff}</span>{/if}</span>
          <span class="a tone-{row.tone}" aria-label={row.tone === 'up' ? 'für euch' : row.tone === 'down' ? 'für die Gegner' : 'zählt kaum'}>{row.arrows}</span>
        {/each}
      </div>
    </div>
  </div>
</Tile>

<style>
  .body {
    display: grid;
    grid-template-columns: 64px minmax(0, 1fr);
    gap: 14px;
    align-items: stretch;
  }

  .scale {
    display: flex;
    flex-direction: column;
    align-items: center;
    gap: 6px;
  }

  .end {
    font-size: 10.5px;
    font-weight: 600;
    color: var(--fg3);
  }

  .track {
    position: relative;
    flex: 1;
    min-height: 150px;
    width: 14px;
    border-radius: 7px;
    overflow: visible;
  }

  .half {
    position: absolute;
    left: 0;
    right: 0;
    height: 50%;
  }

  .half.top {
    top: 0;
    border-radius: 7px 7px 0 0;
    background: linear-gradient(to bottom, var(--green), color-mix(in srgb, var(--green) 18%, transparent));
  }

  .half.bottom {
    bottom: 0;
    border-radius: 0 0 7px 7px;
    background: linear-gradient(to top, var(--red), color-mix(in srgb, var(--red) 18%, transparent));
  }

  .tick {
    position: absolute;
    left: -5px;
    width: 4px;
    height: 1px;
    background: var(--fg4);
    transform: translateY(-0.5px);
  }

  .tick.zero {
    left: -8px;
    width: 30px;
    background: var(--fg2);
  }

  /* The marker glides; no flash, no sound when it crosses the middle. */
  .marker {
    position: absolute;
    left: 50%;
    width: 22px;
    height: 22px;
    border-radius: 50%;
    transform: translate(-50%, -50%);
    background: var(--window);
    box-shadow: 0 0 0 3px currentColor, var(--shadow);
    transition: top 600ms var(--ease);
  }

  .marker.tone-up { color: var(--green-text); }
  .marker.tone-down { color: var(--red-text); }
  .marker.tone-even { color: var(--fg2); }

  .info {
    display: flex;
    flex-direction: column;
    gap: 10px;
    min-width: 0;
  }

  .label {
    font: var(--t-title);
    font-size: 20px;
  }

  .label.tone-up { color: var(--green-text); }
  .label.tone-down { color: var(--red-text); }
  .label.tone-even { color: var(--fg); }

  .odds {
    margin: 2px 0 0;
    font-size: 11.5px;
    color: var(--fg2);
  }

  .rows {
    display: grid;
    grid-template-columns: auto minmax(0, 1fr) auto;
    gap: 6px 12px;
    font-size: 12.5px;
  }

  .k {
    font-size: 11.5px;
    font-weight: 600;
    color: var(--fg2);
  }

  .v {
    font-weight: 600;
  }

  .d {
    margin-left: 8px;
    font-weight: 400;
    color: var(--fg3);
  }

  .a {
    font-weight: 700;
    text-align: right;
    letter-spacing: -1px;
  }

  .a.tone-up { color: var(--green-text); }
  .a.tone-down { color: var(--red-text); }
  .a.tone-even { color: var(--fg3); }
</style>
