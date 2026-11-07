<script lang="ts">
  import type { RunePageView } from '../../core/view';
  import { runeName } from '../../data/assets';
  import Icon from '../Icon.svelte';

  let { runes, large = false }: { runes: RunePageView; large?: boolean } = $props();
  const size = $derived(large ? 32 : 26);
</script>

<div class="runes">
  <Icon kind="rune" id={runes.keystone} size={large ? 46 : 36} ring="key" title={runeName(runes.keystone)} />
  {#each runes.primary.slice(1) as id (id)}
    <Icon kind="rune" {id} {size} title={runeName(id)} />
  {/each}
  <span class="sep"></span>
  {#each runes.secondary as id (id)}
    <Icon kind="rune" {id} {size} title={runeName(id)} />
  {/each}
</div>
<p class="meta">
  <b>{runeName(runes.keystone)}</b> · {runeName(runes.primaryStyle)} + {runeName(runes.secondaryStyle)} · {runes.shards.join(' / ')}
</p>
<p class="stats num">{runes.stats}</p>

<style>
  .runes {
    display: flex;
    align-items: center;
    gap: 6px;
    flex-wrap: wrap;
  }

  .sep {
    width: 1px;
    height: 22px;
    background: var(--separator);
    margin: 0 3px;
  }

  .meta {
    margin: 8px 0 0;
    font-size: 12px;
    color: var(--fg2);
  }

  .meta b {
    color: var(--fg);
    font-weight: 600;
  }

  .stats {
    margin: 2px 0 0;
    font-size: 11px;
    color: var(--fg3);
  }
</style>
