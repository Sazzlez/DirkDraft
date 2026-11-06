<script lang="ts">
  import type { PhaseStep } from '../../core/view';

  let {
    seconds,
    turn,
    mine,
    steps,
    compact = false,
  }: { seconds: number | null; turn: string; mine: boolean; steps: PhaseStep[]; compact?: boolean } = $props();

  // A pick or ban step lasts 30 s in ranked; the ring shows what is left of it.
  const fraction = $derived(seconds === null ? 0 : Math.max(0, Math.min(1, seconds / 30)));
  const r = 26;
  const circumference = 2 * Math.PI * r;
</script>

<div class="clock" class:compact>
  <div class="ring" class:mine aria-label={seconds === null ? 'keine Zeit' : `${seconds} Sekunden`}>
    <svg viewBox="0 0 64 64">
      <circle class="track" cx="32" cy="32" r={r} />
      <circle class="left" cx="32" cy="32" r={r} stroke-dasharray={circumference} stroke-dashoffset={circumference * (1 - fraction)} />
    </svg>
    <span class="num">{seconds ?? '–'}</span>
  </div>
  <div class="text">
    <span class="turn" class:mine>{turn}</span>
    <ol class="steps">
      {#each steps as step (step.label)}
        <li class={step.state}>{step.label}</li>
      {/each}
    </ol>
  </div>
</div>

<style>
  .clock {
    display: flex;
    flex-direction: column;
    align-items: center;
    gap: 10px;
    text-align: center;
  }

  .ring {
    position: relative;
    width: 58px;
    height: 58px;
  }

  svg {
    width: 100%;
    height: 100%;
    transform: rotate(-90deg);
  }

  circle {
    fill: none;
    stroke-width: 4;
  }

  .track {
    stroke: var(--track);
  }

  .left {
    stroke: var(--fg2);
    stroke-linecap: round;
    transition: stroke-dashoffset 0.9s linear;
  }

  .mine .left {
    stroke: var(--blue);
  }

  .ring span {
    position: absolute;
    inset: 0;
    display: grid;
    place-items: center;
    font-size: 18px;
    font-weight: 650;
  }

  .text {
    display: flex;
    flex-direction: column;
    align-items: center;
    gap: 8px;
  }

  .turn {
    font-size: 13px;
    font-weight: 600;
  }

  .turn.mine {
    color: var(--blue);
  }

  .steps {
    list-style: none;
    margin: 0;
    padding: 0;
    display: flex;
    flex-direction: column;
    gap: 4px;
    align-items: flex-start;
    font-size: 11px;
    color: var(--fg3);
  }

  .steps li {
    display: flex;
    align-items: center;
    gap: 6px;
  }

  .steps li::before {
    content: '';
    width: 6px;
    height: 6px;
    border-radius: 50%;
    background: var(--track);
  }

  .steps .done {
    color: var(--fg2);
  }

  .steps .done::before {
    background: var(--green);
  }

  .steps .now {
    color: var(--teal);
    font-weight: 600;
  }

  .steps .now::before {
    background: var(--blue);
    box-shadow: 0 0 0 3px var(--blue-wash);
  }

  /* In the phase line: one row — ring, turn, steps side by side. */
  .compact {
    flex-direction: row;
    gap: 12px;
    text-align: left;
  }

  .compact .ring {
    width: 40px;
    height: 40px;
  }

  .compact .ring span {
    font-size: 14px;
  }

  .compact .text {
    align-items: flex-start;
    gap: 4px;
  }

  .compact .steps {
    flex-direction: row;
    flex-wrap: wrap;
    gap: 4px 10px;
  }

  @container app (max-width: 719px) {
    .compact .steps li:not(.now) {
      display: none;
    }
  }
</style>
