<script lang="ts">
  import type { Area } from '../core/view';

  let { value, onChange, stretch = false }: { value: Area; onChange: (area: Area) => void; stretch?: boolean } = $props();

  const options: { id: Area; label: string }[] = [
    { id: 'draft', label: 'Draft' },
    { id: 'build', label: 'Build' },
    { id: 'game', label: 'Spiel' },
  ];
</script>

<div class="seg" class:stretch role="tablist" aria-label="Bereich">
  {#each options as option (option.id)}
    <button role="tab" aria-selected={value === option.id} class:on={value === option.id} onclick={() => onChange(option.id)}>
      {option.label}
    </button>
  {/each}
</div>

<style>
  .seg {
    display: inline-flex;
    gap: 2px;
    padding: 2px;
    border-radius: var(--r-s);
    background: var(--card-strong);
  }

  .seg.stretch {
    display: flex;
    width: 100%;
  }

  button {
    padding: 4px 14px;
    border-radius: 6px;
    font-size: 12px;
    font-weight: 500;
    color: var(--fg2);
    white-space: nowrap;
    transition: background 0.2s var(--spring), color 0.2s;
  }

  .stretch button {
    flex: 1;
  }

  button.on {
    background: var(--card-strong);
    color: var(--fg);
    box-shadow: var(--shadow);
  }

  :global([data-theme='light']) button.on {
    background: #fff;
  }
</style>
