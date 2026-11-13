<script lang="ts">
  // Du gegen den Lane-Gegner: a vertical scale, up = stronger, down = weaker (docs/lane-vergleich.md).
  // It describes, it never tells what to do, and it moves without a sound or a flash — Riot's rules.
  import type { StrengthView } from '../../core/view';
  import Icon from '../Icon.svelte';
  import Tile from '../Tile.svelte';

  let { strength }: { strength: StrengthView } = $props();
  const STEPS = [3, 2, 1, 0, -1, -2, -3];
  // −3 … +3 onto the track, top to bottom.
  const at = $derived(((3 - Math.max(-3, Math.min(3, strength.value))) / 6) * 100);
  const tone = $derived(strength.value >= 0.5 ? 'up' : strength.value <= -0.5 ? 'down' : 'even');
  const title = $derived(`Du gegen ${strength.opponents.map((o) => o.name).join(' + ')}`);
</script>

<Tile {title}>
  {#snippet accessory()}
    <span class="opp">{#each strength.opponents as o (o.id)}<Icon kind="champion" champion={o} size={24} />{/each}</span>
  {/snippet}
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
      <b class="label tone-{tone}">{strength.label}</b>
      <div class="rows num">
        {#each strength.rows as row (row.label)}
          <span class="k">{row.label}</span>
          <span class="v">{row.mine} : {row.theirs}</span>
          <span class="d tone-{row.tone}">{row.diff}</span>
        {/each}
      </div>
      {#if strength.phases}
        <div class="phases">
          <span class="k">Dieses Duell</span>
          <div class="pills">
            {#each strength.phases as phase (phase.label)}
              <span class="pill tone-{phase.tone}" class:now={phase.now}>
                <span class="pl">{phase.label}</span>
                <span class="pw">{phase.who}</span>
              </span>
            {/each}
          </div>
        </div>
      {/if}
    </div>
  </div>
</Tile>

<style>
  .opp {
    display: flex;
    gap: 4px;
  }

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
    font-weight: 700;
    text-align: right;
  }

  .d.tone-up { color: var(--green-text); }
  .d.tone-down { color: var(--red-text); }
  .d.tone-even { color: var(--fg3); }

  .phases {
    display: flex;
    flex-direction: column;
    gap: 6px;
  }

  .pills {
    display: grid;
    grid-template-columns: repeat(3, minmax(0, 1fr));
    gap: 6px;
  }

  .pill {
    display: flex;
    flex-direction: column;
    gap: 1px;
    padding: 6px 8px;
    border-radius: var(--r-m);
    background: var(--card-strong);
    font-size: 11.5px;
    opacity: 0.6;
  }

  .pill.now {
    opacity: 1;
    box-shadow: inset 0 0 0 1px var(--fg4);
  }

  .pl {
    color: var(--fg2);
  }

  .pw {
    font-weight: 700;
  }

  .pill.tone-up .pw { color: var(--green-text); }
  .pill.tone-down .pw { color: var(--red-text); }
</style>
