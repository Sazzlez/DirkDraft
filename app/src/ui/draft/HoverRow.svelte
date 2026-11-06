<script lang="ts">
  import type { HoverView } from '../../core/view';
  import Icon from '../Icon.svelte';

  let { hover }: { hover: HoverView } = $props();
</script>

<div class="hover">
  <Icon kind="champion" champion={hover.champion} size={34} />
  <span class="text">
    <span class="kicker">Dein Hover</span>
    <span class="name">{hover.champion.name}</span>
    <span class="place">{hover.place}</span>
  </span>
  <b class="num">{hover.value ?? 'keine Daten'}</b>
</div>

<style>
  .hover {
    display: grid;
    grid-template-columns: 38px minmax(0, 1fr) auto;
    gap: 12px;
    align-items: center;
    padding: 10px 12px;
    margin-bottom: 6px;
    border-radius: var(--r-m);
    background: var(--blue-wash);
  }

  .hover :global(.icon) {
    --s: 38px !important;
  }

  .text {
    display: flex;
    flex-direction: column;
    min-width: 0;
  }

  .kicker {
    font: var(--t-caption);
    font-weight: 600;
    color: var(--blue);
  }

  .name {
    font-size: 15px;
    font-weight: 600;
    letter-spacing: -0.015em;
  }

  .place {
    font-size: 12px;
    color: var(--fg2);
  }

  b {
    font-size: 20px;
    font-weight: 700;
    letter-spacing: -0.025em;
  }
</style>
