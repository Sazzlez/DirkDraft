<script lang="ts">
  import Shell from './ui/Shell.svelte';
  import { SCENES, sceneView } from './data/scenes';
  import type { ViewModel } from './core/view';

  const params = new URLSearchParams(location.search);
  const forcedTheme = params.get('theme');
  const screenshot = params.has('shot');

  let sceneId = $state(params.get('scene') ?? 'picking');
  let view = $state<ViewModel | null>(null);
  let error = $state<string | null>(null);

  // Dark by default; light only when Windows says so (or the URL forces it for screenshots).
  const prefersLight = window.matchMedia('(prefers-color-scheme: light)');
  let systemLight = $state(prefersLight.matches);
  prefersLight.addEventListener('change', (e) => (systemLight = e.matches));
  $effect(() => {
    const theme = forcedTheme ?? (systemLight ? 'light' : 'dark');
    document.documentElement.dataset.theme = theme;
  });

  $effect(() => {
    const id = sceneId;
    error = null;
    sceneView(id)
      .then((v) => {
        view = v;
        document.body.dataset.ready = id;
      })
      .catch((e: unknown) => (error = String(e)));
  });

  function choose(id: string) {
    sceneId = id;
    const url = new URL(location.href);
    url.searchParams.set('scene', id);
    history.replaceState(null, '', url);
  }
</script>

{#if error}
  <p class="error">{error}</p>
{:else if view}
  <Shell {view} />
{/if}

{#if !screenshot}
  <nav class="scenes" aria-label="Szenen">
    {#each SCENES as scene (scene.id)}
      <button class:on={scene.id === sceneId} onclick={() => choose(scene.id)}>{scene.title}</button>
    {/each}
  </nav>
{/if}

<style>
  .error {
    padding: 24px;
    color: var(--red);
  }

  .scenes {
    position: fixed;
    left: 8px;
    bottom: 8px;
    display: flex;
    flex-wrap: wrap;
    gap: 4px;
    max-width: calc(100vw - 16px);
    padding: 4px;
    border-radius: var(--r-m);
    background: rgba(0, 0, 0, 0.55);
    z-index: 100;
  }

  .scenes button {
    padding: 3px 8px;
    border-radius: 6px;
    font-size: 11px;
    color: #fff;
    opacity: 0.7;
  }

  .scenes button.on {
    background: var(--blue);
    opacity: 1;
  }
</style>
