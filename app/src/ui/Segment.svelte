<script lang="ts" generics="T extends string">
  // A segmented control as on Apple platforms: one thumb that glides to the chosen segment.

  let {
    value,
    onChange,
    options,
    stretch = false,
    small = false,
    label = 'Auswahl',
  }: {
    value: T;
    onChange: (value: T) => void;
    options: { id: T; label: string }[];
    stretch?: boolean;
    small?: boolean;
    label?: string;
  } = $props();

  const index = $derived(Math.max(0, options.findIndex((o) => o.id === value)));
</script>

<div class="seg" class:stretch class:small role="tablist" aria-label={label} style:--n={options.length} style:--i={index}>
  <span class="thumb" aria-hidden="true"></span>
  {#each options as option (option.id)}
    <button role="tab" aria-selected={value === option.id} class:on={value === option.id} onclick={() => onChange(option.id)}>
      {option.label}
    </button>
  {/each}
</div>

<style>
  .seg {
    position: relative;
    display: inline-grid;
    grid-template-columns: repeat(var(--n), minmax(0, 1fr));
    padding: 2px;
    border-radius: 9px;
    background: var(--card-strong);
  }

  .seg.stretch {
    display: grid;
    width: 100%;
  }

  .thumb {
    position: absolute;
    top: 2px;
    bottom: 2px;
    left: 2px;
    width: calc((100% - 4px) / var(--n));
    transform: translateX(calc(var(--i) * 100%));
    border-radius: 7px;
    background: var(--thumb);
    box-shadow: 0 1px 3px rgba(0, 0, 0, 0.18), 0 0 0 0.5px rgba(0, 0, 0, 0.04);
    transition: transform 0.32s var(--spring);
  }

  button {
    position: relative;
    padding: 4px 16px;
    font-size: 12.5px;
    font-weight: 500;
    color: var(--fg2);
    white-space: nowrap;
    transition: color 0.2s var(--ease);
  }

  .small button {
    padding: 3px 11px;
    font-size: 11.5px;
  }

  button.on {
    color: var(--fg);
    font-weight: 600;
  }
</style>
