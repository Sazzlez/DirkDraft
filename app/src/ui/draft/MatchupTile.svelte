<script lang="ts">
  import type { BuildView, MatchupView } from '../../core/view';
  import { LANE_NAMES } from '../../core/view';
  import Icon from '../Icon.svelte';
  import Tile from '../Tile.svelte';
  import RuneRow from '../build/RuneRow.svelte';
  import ImportButton from '../build/ImportButton.svelte';

  let { matchup, build, canImport }: { matchup: MatchupView; build: BuildView | null; canImport: boolean } = $props();
</script>

<Tile title={matchup.title} sub={LANE_NAMES[matchup.lane]} main>
  <div class="mu">
    <Icon kind="champion" champion={matchup.me} size={48} ring={matchup.mine ? 'me' : null} />
    <span class="vs">vs</span>
    <Icon kind="champion" champion={matchup.opponent} size={48} />
    <span class="names">
      <b>{matchup.me.name} vs {matchup.opponent?.name ?? 'offen'}</b>
      <span>{matchup.sample}</span>
    </span>
    <span class="value">
      <b class="num tone-{matchup.tone === 'neutral' ? 'neutral' : matchup.tone}">{matchup.value ?? '—'}</b>
      {#if matchup.value !== null}<small>{matchup.verdict}</small>{/if}
    </span>
  </div>

  {#if build}
    <div class="card">
      {#if build.runes}<RuneRow runes={build.runes} />{/if}
      {#if canImport}<ImportButton {build} />{/if}
    </div>

    <div class="kit">
      <div>
        <span class="k">Spells</span>
        <span class="icons">{#each build.spells as id (id)}<Icon kind="spell" {id} size={26} />{/each}</span>
      </div>
      <div>
        <span class="k">Start</span>
        <span class="icons">{#each build.start?.items ?? [] as item, i (i)}<Icon kind="item" id={item.id} size={26} title={item.name} />{/each}</span>
      </div>
      <div>
        <span class="k">Skills</span>
        <span class="seq">{build.skillPriority.replaceAll('>', '›') || '—'}</span>
      </div>
      {#if build.support}
        <div>
          <span class="k">Support-Item</span>
          <span class="icons">{#each build.support.items as item, i (i)}<Icon kind="item" id={item.id} size={26} title="{item.name} · {build.support.stats}" />{/each}</span>
        </div>
      {/if}
      <div class="core">
        <span class="k">Kern</span>
        <span class="icons">
          {#each build.core?.items ?? [] as item, i (i)}
            {#if i > 0}<span class="arr">›</span>{/if}<Icon kind="item" id={item.id} size={26} title={item.name} />
          {/each}
        </span>
      </div>
    </div>
  {/if}
</Tile>

<style>
  .mu {
    display: flex;
    align-items: center;
    gap: 10px;
    margin-bottom: 12px;
  }

  .vs {
    font-size: 11px;
    color: var(--fg3);
  }

  .names {
    display: flex;
    flex-direction: column;
    min-width: 0;
    margin-left: 4px;
  }

  .names b {
    font-size: 15px;
  }

  .names span {
    font-size: 11.5px;
    color: var(--fg2);
  }

  .value {
    margin-left: auto;
    display: flex;
    flex-direction: column;
    align-items: flex-end;
  }

  .value b {
    font-size: 24px;
    font-weight: 700;
    letter-spacing: -0.02em;
  }

  .value small {
    font-size: 11px;
    color: var(--fg2);
  }

  .card {
    padding: 12px;
    border-radius: var(--r-m);
    background: var(--card);
  }

  .kit {
    display: flex;
    flex-wrap: wrap;
    gap: 14px 22px;
    margin-top: 12px;
    padding: 10px 12px;
    border-radius: var(--r-m);
    background: var(--card);
  }

  .kit > div {
    display: flex;
    flex-direction: column;
    gap: 5px;
  }

  .k {
    font-size: 11.5px;
    font-weight: 600;
    color: var(--fg2);
  }

  .icons {
    display: flex;
    gap: 4px;
    align-items: center;
  }

  .arr {
    color: var(--fg3);
  }

  .seq {
    height: 26px;
    display: flex;
    align-items: center;
    font-size: 14px;
    font-weight: 650;
    letter-spacing: 0.04em;
  }

  @container main (max-width: 469px) {
    .value b {
      font-size: 20px;
    }
  }
</style>
