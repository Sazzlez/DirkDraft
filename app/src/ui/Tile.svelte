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
    box-shadow: var(--edge);
    border-radius: var(--r-l);
    padding: 16px 16px 14px;
    min-width: 0;
  }

  .tile.main {
    container: main / inline-size;
  }

  header {
    display: flex;
    align-items: center;
    gap: 8px;
    margin-bottom: 12px;
    min-height: 26px;
  }

  h3 {
    margin: 0;
    font: var(--t-head);
    letter-spacing: -0.015em;
  }

  .main h3 {
    font: var(--t-title);
    letter-spacing: -0.02em;
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
