<script lang="ts">
  import type { Snippet } from 'svelte';

  let {
    title,
    sub = '',
    accessory,
    children,
    main = false,
  }: { title: string; sub?: string; accessory?: Snippet; children: Snippet; main?: boolean } = $props();
</script>

<section class="tile" class:main>
  <header>
    <h3>{title}{#if sub}<span class="sub">{' · '}{sub}</span>{/if}</h3>
    {#if accessory}<div class="acc">{@render accessory()}</div>{/if}
  </header>
  {@render children()}
</section>

<style>
  .tile {
    background: var(--card);
    border-radius: var(--r-l);
    padding: 14px;
    min-width: 0;
  }

  .tile.main {
    container: main / inline-size;
  }

  header {
    display: flex;
    align-items: center;
    gap: 8px;
    margin-bottom: 10px;
    min-height: 24px;
  }

  h3 {
    margin: 0;
    font-size: 13px;
    font-weight: 600;
    letter-spacing: -0.01em;
  }

  .sub {
    color: var(--fg2);
    font-weight: 500;
  }

  .acc {
    margin-left: auto;
    display: flex;
    gap: 6px;
    align-items: center;
  }
</style>
