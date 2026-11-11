<script lang="ts">
  import { slide } from 'svelte/transition';
  import type { RecommendationView } from '../../core/view';
  import { points } from '../../core/format';
  import Icon from '../Icon.svelte';
  import ContributionBar from './ContributionBar.svelte';

  let { item, ban, grouped }: { item: RecommendationView; ban: boolean; grouped: boolean } = $props();

  let open = $state(false);
  const maxTerm = $derived(Math.max(2, ...item.terms.map((t) => Math.abs(t.points ?? 0))));
</script>

<div class="row" class:grouped class:open>
  <button class="line" onclick={() => (open = !open)} aria-expanded={open}>
    <Icon kind="champion" champion={item.champion} size={34} grey={item.unavailable} />
    <span class="text">
      <span class="name">{item.champion.name}{#if item.badge}<span class="badge">{item.badge}</span>{/if}</span>
      <span class="reason">{item.reason}</span>
    </span>
    <span class="bar"><ContributionBar terms={item.terms} /></span>
    <span class="value">
      <b class="num">{item.value}</b>
      {#if item.verdict}<small class="tone-{item.tone}">{item.verdict}</small>{/if}
    </span>
  </button>

  {#if open}
    <div class="calc" transition:slide={{ duration: 220 }}>
      {#each item.terms as term (term.label)}
        <div class="term">
          <span class="label">{term.label}</span>
          <span class="verdict">{term.verdict}</span>
          <span class="axis">
            {#if term.points !== null}
              <i
                class:neg={term.points < 0}
                class={term.kind}
                style:width="{(Math.abs(term.points) / maxTerm) * 50}%"
                style:left={term.points < 0 ? `${50 - (Math.abs(term.points) / maxTerm) * 50}%` : '50%'}
              ></i>
            {/if}
          </span>
          <span class="pts num">
            {#if term.gate !== null}× {Math.round(term.gate * 100)} %{:else if term.points === null}keine Daten{:else}{points(term.points, 2)}{/if}
          </span>
        </div>
      {/each}
      {#if item.reasons.length > 1}
        <p class="reasons">
          {#each item.reasons as reason (reason.text)}
            <span class="tone-{reason.tone === 'bad' ? 'bad' : 'neutral'}">{reason.text}</span>
          {/each}
        </p>
      {/if}
      <p class="err num">Streubereich ±{item.errorPoints.toLocaleString('de-DE', { maximumFractionDigits: 1, minimumFractionDigits: 1 })} Punkte{ban ? '' : ' — darunter bedeuten Unterschiede nichts.'}</p>
    </div>
  {/if}
</div>

<style>
  /* iOS list: the separator starts where the text does, not at the edge. */
  .row {
    position: relative;
  }

  .row + :global(.row)::before {
    content: '';
    position: absolute;
    top: 0;
    left: 56px;
    right: 6px;
    height: 0.5px;
    background: var(--separator);
  }

  .row.grouped {
    background: var(--green-wash);
    border-radius: var(--r-m);
    margin-top: 3px;
  }

  .row.grouped::before,
  .row.grouped + :global(.row)::before {
    display: none;
  }

  .line {
    display: grid;
    grid-template-columns: 38px minmax(0, 1fr) 112px 72px;
    gap: 12px;
    align-items: center;
    width: 100%;
    padding: 10px 8px;
    text-align: left;
    border-radius: var(--r-m);
    transition: background 0.15s var(--ease);
  }

  .line:hover {
    background: var(--card-hover);
  }

  .line :global(.icon) {
    --s: 38px !important;
    box-shadow: 0 2px 8px rgba(0, 0, 0, 0.25);
  }

  .text {
    display: flex;
    flex-direction: column;
    gap: 1px;
    min-width: 0;
  }

  .name {
    font-size: 15px;
    font-weight: 600;
    letter-spacing: -0.015em;
  }

  .badge {
    margin-left: 7px;
    padding: 1px 7px;
    border-radius: 999px;
    background: var(--green-wash);
    color: var(--green-text);
    font-size: 10.5px;
    font-weight: 600;
    letter-spacing: 0;
    vertical-align: 2px;
    white-space: nowrap;
  }

  .reason {
    font-size: 12px;
    color: var(--fg2);
    white-space: nowrap;
    overflow: hidden;
    text-overflow: ellipsis;
  }

  .value {
    display: flex;
    flex-direction: column;
    align-items: flex-end;
    text-align: right;
  }

  .value b {
    font-size: 20px;
    font-weight: 700;
    letter-spacing: -0.025em;
    white-space: nowrap;
  }

  .value small {
    font-size: 11px;
    font-weight: 500;
    white-space: nowrap;
  }

  .calc {
    padding: 2px 8px 14px 58px;
  }

  .term {
    display: grid;
    grid-template-columns: minmax(0, 1fr) 92px 96px 78px;
    gap: 8px;
    align-items: center;
    padding: 4px 0;
    font-size: 12px;
  }

  .label {
    color: var(--fg);
  }

  .verdict {
    color: var(--fg2);
  }

  .axis {
    position: relative;
    height: 6px;
    border-radius: 3px;
    background: var(--track);
  }

  .axis::after {
    content: '';
    position: absolute;
    left: 50%;
    top: -3px;
    bottom: -3px;
    width: 1px;
    background: var(--fg4);
  }

  .axis i {
    position: absolute;
    top: 0;
    height: 100%;
    border-radius: 3px;
    background: var(--blue);
  }

  .axis i.matchup,
  .axis i.enemies { background: var(--teal); }
  .axis i.synergy { background: var(--purple); }
  .axis i.comp { background: var(--orange); }
  .axis i.ban { background: var(--fg2); }
  .axis i.neg { background: var(--red); }

  .pts {
    text-align: right;
    color: var(--fg2);
    white-space: nowrap;
  }

  .reasons {
    display: flex;
    flex-wrap: wrap;
    gap: 4px 12px;
    margin: 8px 0 0;
    font-size: 11.5px;
  }

  .err {
    margin: 6px 0 0;
    font-size: 11px;
    color: var(--fg3);
  }

  @container main (max-width: 469px) {
    .line {
      grid-template-columns: 38px minmax(0, 1fr) auto;
    }

    .bar {
      display: none;
    }

    .calc {
      padding-left: 4px;
    }

    .term {
      grid-template-columns: minmax(0, 1fr) auto 70px;
    }

    .axis {
      display: none;
    }
  }
</style>
