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
    grid-template-columns: 34px minmax(0, 1fr) auto;
    gap: 10px;
    align-items: center;
    padding: 9px 10px;
    margin-bottom: 4px;
    border-radius: var(--r-m);
    background: var(--blue-wash);
    box-shadow: inset 0 0 0 1px var(--blue-line);
  }

  .text {
    display: flex;
    flex-direction: column;
    min-width: 0;
  }

  .kicker {
    font-size: 10px;
    font-weight: 600;
    color: var(--blue);
    text-transform: uppercase;
    letter-spacing: 0.06em;
  }

  .name {
    font-size: 14px;
    font-weight: 600;
  }

  .place {
    font-size: 11.5px;
    color: var(--fg2);
  }

  b {
    font-size: 17px;
    font-weight: 650;
  }
</style>
