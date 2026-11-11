<script lang="ts">
  import type { PhaseStep } from '../../core/view';

  // Whose turn it is and the draft's steps; the countdown stays in the client.
  let {
    turn,
    mine,
    steps,
    compact = false,
  }: { turn: string; mine: boolean; steps: PhaseStep[]; compact?: boolean } = $props();
</script>

<div class="clock" class:compact>
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

  .text {
    display: flex;
    flex-direction: column;
    align-items: center;
    gap: 10px;
  }

  .turn {
    font: var(--t-title);
    letter-spacing: -0.02em;
  }

  .turn.mine {
    color: var(--blue-text);
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
    color: var(--teal-text);
    font-weight: 600;
  }

  .steps .now::before {
    background: var(--blue);
    box-shadow: 0 0 0 3px var(--blue-wash);
  }

  /* In the phase line: one row — turn and steps side by side. */
  .compact {
    flex-direction: row;
    gap: 12px;
    text-align: left;
  }

  .compact .turn {
    font: var(--t-large);
    font-size: 24px;
    letter-spacing: -0.03em;
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
