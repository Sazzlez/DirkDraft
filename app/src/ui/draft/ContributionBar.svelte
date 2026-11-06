<script lang="ts">
  import type { TermView } from '../../core/view';

  let { terms }: { terms: TermView[] } = $props();

  // The bar shows where the estimate's distance from 50 % comes from: each criterion's points as a
  // coloured run, deductions in red at the end. Scaled to six points — the spread real candidates
  // live in — so bars of different rows compare.
  const SCALE = 6;
  const parts = $derived(
    terms
      .filter((t) => t.points !== null && t.gate === null && Math.abs(t.points) >= 0.05)
      .map((t) => ({ kind: t.kind, value: t.points as number })),
  );
  const positive = $derived(parts.filter((p) => p.value > 0));
  const negative = $derived(parts.filter((p) => p.value < 0).reduce((sum, p) => sum + Math.abs(p.value), 0));
</script>

<span class="bar" aria-hidden="true">
  {#each positive as part, i (i)}
    <i class={part.kind} style:width="{Math.min(100, (part.value / SCALE) * 100)}%"></i>
  {/each}
  {#if negative > 0}
    <i class="neg" style:width="{Math.min(100, (negative / SCALE) * 100)}%"></i>
  {/if}
</span>

<style>
  .bar {
    display: flex;
    height: 8px;
    border-radius: 4px;
    overflow: hidden;
    background: var(--track);
  }

  i {
    display: block;
    height: 100%;
    background: var(--blue);
  }

  .matchup,
  .enemies { background: var(--teal); }
  .synergy { background: var(--purple); }
  .comp { background: var(--orange); }
  .ban { background: var(--fg2); }
  .neg { background: var(--red); margin-left: auto; }
</style>
