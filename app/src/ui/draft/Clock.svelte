<script lang="ts">
  // Whose turn it is; the countdown and the phase stay in the client.
  let { turn, mine, compact = false }: { turn: string; mine: boolean; compact?: boolean } = $props();
</script>

<div class="clock" class:compact>
  <div class="text">
    <span class="turn" class:mine>{turn}</span>
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

  /* In the phase line above the board. */
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
</style>
