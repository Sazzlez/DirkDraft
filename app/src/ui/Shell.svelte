<script lang="ts">
  import { fade, fly } from 'svelte/transition';
  import { AREA_OF, AREAS, type Area, type Moment, type ViewModel } from '../core/view';
  import { nextArea } from '../core/moment';
  import TitleBar from './TitleBar.svelte';
  import Segment from './Segment.svelte';
  import DraftArea from './draft/DraftArea.svelte';
  import BuildArea from './build/BuildArea.svelte';
  import GameArea from './game/GameArea.svelte';
  import PostgameArea from './game/PostgameArea.svelte';
  import EmptyState from './EmptyState.svelte';

  let { view }: { view: ViewModel } = $props();

  let lastMoment = $state<Moment | null>(null);
  let manual = $state<Area | null>(null);
  let area = $state<Area>('draft');

  // The moment switches the area; a manual switch holds until the next moment begins.
  $effect.pre(() => {
    const next = nextArea(lastMoment, view.moment, manual, AREA_OF[view.moment]);
    area = next.area;
    manual = next.manual;
    lastMoment = view.moment;
  });

  function choose(next: Area) {
    manual = next;
    area = next;
  }

  const reduced = window.matchMedia('(prefers-reduced-motion: reduce)').matches;
  const enter = (node: Element) => (reduced ? fade(node, { duration: 120 }) : fly(node, { y: 8, duration: 260, opacity: 0 }));
</script>

<div class="shell">
  <TitleBar {view} {area} onArea={choose} />
  <div class="segrow">
    <Segment value={area} onChange={choose} options={AREAS} label="Bereich" stretch />
  </div>

  {#if view.sampleNote}
    <p class="sample">{view.sampleNote}</p>
  {/if}

  <main class="content">
    {#key area + view.moment}
      <div class="pane" in:enter>
        {#if area === 'draft'}
          {#if view.board || view.main.kind !== 'none'}
            <DraftArea {view} />
          {:else}
            <EmptyState title="Kein Draft" text={view.emptyText ?? 'Sobald ein Champ Select beginnt, steht hier das Board.'} />
          {/if}
        {:else if area === 'build'}
          {#if view.build}
            <BuildArea build={view.build} />
          {:else}
            <EmptyState title="Noch kein Build" text="Sobald du einen Champion hoverst oder lockst, steht hier sein Build." />
          {/if}
        {:else if view.game}
          <GameArea game={view.game} build={view.build} />
        {:else if view.postgame}
          <PostgameArea postgame={view.postgame} />
        {:else if view.moment === 'game' || view.moment === 'loading'}
          <EmptyState title="Das Spiel lädt …" text={view.emptyText ?? 'Sobald das Spiel läuft: nächster Kauf gegen dein Gold, der nächste Skillpunkt und was die Gegner bauen.'} />
        {:else if view.moment === 'postgame'}
          <EmptyState title="Spiel vorbei" text={view.emptyText ?? 'Das Nachspiel kommt hierher.'} />
        {:else}
          <EmptyState title="Noch kein Spiel" text="Sobald das Spiel startet: nächster Kauf gegen dein Gold, der nächste Skillpunkt und was die Gegner bauen." />
        {/if}
      </div>
    {/key}
  </main>
</div>

<style>
  .shell {
    container: app / inline-size;
    height: 100%;
    display: flex;
    flex-direction: column;
    min-width: 340px;
    background: var(--window);
    -webkit-backdrop-filter: blur(50px) saturate(180%);
    backdrop-filter: blur(50px) saturate(180%);
    box-shadow: inset 0 0.5px 0 rgba(255, 255, 255, 0.1);
  }

  .segrow {
    display: none;
    padding: 0 16px 10px;
  }

  .sample {
    align-self: flex-start;
    margin: 0 20px 10px;
    padding: 4px 10px;
    border-radius: 999px;
    background: var(--orange-wash);
    color: var(--orange);
    font: var(--t-caption);
  }

  .content {
    flex: 1;
    min-height: 0;
    overflow-y: auto;
    overflow-x: hidden;
  }

  .pane {
    min-height: 100%;
  }

  @container app (max-width: 719px) {
    .segrow {
      display: block;
    }

    .sample {
      margin: 0 16px 10px;
    }
  }
</style>
