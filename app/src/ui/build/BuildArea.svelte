<script lang="ts">
  import type { BuildView, ItemOption } from '../../core/view';
  import Icon from '../Icon.svelte';
  import Tile from '../Tile.svelte';
  import RuneRow from './RuneRow.svelte';
  import SkillGrid from './SkillGrid.svelte';
  import ImportButton from './ImportButton.svelte';

  let { build, canImport }: { build: BuildView; canImport: boolean } = $props();

  const slots = $derived<{ label: string; main: ItemOption | null; also: ItemOption[] }[]>([
    { label: 'Start', main: build.start, also: build.alsoStart },
    { label: 'Schuhe', main: build.boots, also: build.alsoBoots },
    { label: 'Kern', main: build.core, also: build.alsoCore },
    ...(build.support ? [{ label: 'Support-Item', main: build.support, also: build.alsoSupport }] : []),
  ]);
</script>

<div class="head">
  <Icon kind="champion" champion={build.champion} size={40} />
  <div>
    <h2>{build.title}</h2>
    <p>{build.source}{#if build.caveat}{' · '}<span class="tone-warn">{build.caveat}</span>{/if}</p>
  </div>
</div>

<div class="grid">
  <Tile title="Runen">
    {#if build.runes}
      <RuneRow runes={build.runes} large />
    {:else}
      <p class="none">Für diesen Build liegen keine Runen vor.</p>
    {/if}
    {#if canImport}
      <ImportButton {build} />
    {/if}
  </Tile>

  <Tile title="Spells und Skills">
    <div class="spells">
      {#each build.spells as id (id)}<Icon kind="spell" {id} size={32} />{/each}
      <span class="prio">{build.skillPriority.replaceAll('>', '›')}</span>
    </div>
    <SkillGrid skills={build.skills} />
  </Tile>

  <Tile title="Items">
    {#each slots as slot (slot.label)}
      <div class="slot">
        <span class="k">{slot.label}</span>
        {#if slot.main}
          <div class="set">
            <span class="icons">
              {#each slot.main.items as item, i (i)}
                {#if i > 0 && slot.label === 'Kern'}<span class="arr">›</span>{/if}<Icon kind="item" id={item.id} size={32} title={item.name} />
              {/each}
            </span>
            <span class="stats num">{slot.main.stats}</span>
          </div>
          {#if slot.also.length > 0}
            <details>
              <summary>auch gespielt · {slot.also.length}</summary>
              {#each slot.also as alt, n (n)}
                <div class="set alt">
                  <span class="icons">{#each alt.items as item, i (i)}<Icon kind="item" id={item.id} size={24} title={item.name} />{/each}</span>
                  <span class="stats num">{alt.stats}</span>
                </div>
              {/each}
            </details>
          {/if}
        {:else}
          <span class="none">keine Daten</span>
        {/if}
      </div>
    {/each}
    {#if build.late.length > 0}
      <div class="slot">
        <span class="k">Spät</span>
        <span class="icons">{#each build.late as item, i (i)}<Icon kind="item" id={item.id} size={28} title={item.name} />{/each}</span>
      </div>
    {/if}
  </Tile>
</div>

<style>
  .head {
    display: flex;
    align-items: center;
    gap: 12px;
    padding: 8px 16px 14px;
  }

  h2 {
    margin: 0;
    font-size: 18px;
    font-weight: 700;
    letter-spacing: -0.02em;
  }

  .head p {
    margin: 2px 0 0;
    font-size: 12px;
    color: var(--fg2);
  }

  .grid {
    display: grid;
    grid-template-columns: repeat(auto-fill, minmax(320px, 1fr));
    gap: 12px;
    padding: 0 16px 16px;
    align-items: start;
  }

  .spells {
    display: flex;
    align-items: center;
    gap: 6px;
    margin-bottom: 12px;
  }

  .prio {
    margin-left: auto;
    font-size: 15px;
    font-weight: 650;
    letter-spacing: 0.04em;
  }

  .slot {
    padding: 8px 0;
  }

  .slot + .slot {
    border-top: 1px solid var(--separator);
  }

  .k {
    display: block;
    margin-bottom: 6px;
    font-size: 11.5px;
    font-weight: 600;
    color: var(--fg2);
  }

  .set {
    display: flex;
    align-items: center;
    gap: 10px;
  }

  .set.alt {
    margin-top: 6px;
  }

  .icons {
    display: flex;
    align-items: center;
    gap: 4px;
  }

  .arr {
    color: var(--fg3);
  }

  .stats {
    margin-left: auto;
    font-size: 11.5px;
    color: var(--fg2);
    white-space: nowrap;
  }

  details summary {
    margin-top: 6px;
    font-size: 11.5px;
    color: var(--blue);
    cursor: pointer;
    list-style: none;
  }

  .none {
    font-size: 12px;
    color: var(--fg3);
  }

  @container app (max-width: 719px) {
    .head {
      padding: 4px 12px 10px;
    }

    .grid {
      grid-template-columns: minmax(0, 1fr);
      padding: 0 12px 12px;
      gap: 10px;
    }
  }
</style>
