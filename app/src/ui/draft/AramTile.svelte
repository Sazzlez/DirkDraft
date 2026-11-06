<script lang="ts">
  import type { AramView } from '../../core/view';
  import Icon from '../Icon.svelte';
  import Tile from '../Tile.svelte';

  let { aram }: { aram: AramView } = $props();
</script>

<Tile title="Behalten oder tauschen?" sub="{aram.rerolls} {aram.rerolls === 1 ? 'Reroll' : 'Rerolls'}" main>
  {#if aram.tied >= 2}<div class="group">Vorne gleichauf · {aram.tied}</div>{/if}
  {#each aram.rows as row, i (row.champion.id)}
    <div class="row" class:own={row.own} class:grouped={i < aram.tied}>
      <Icon kind="champion" champion={row.champion} size={34} ring={row.own ? 'me' : null} />
      <span class="text">
        <span class="name">{row.champion.name}{#if row.own}<em>{' · dein Champ'}</em>{/if}</span>
        <span class="reason">{row.reasons.find((r) => !r.text.startsWith('dein'))?.text ?? row.reason}</span>
      </span>
      <span class="value">
        <b class="num">{row.value}</b>
        <small class="tone-{row.tone}">{row.verdict}</small>
      </span>
    </div>
  {/each}
  <p class="foot">{aram.note} Getauscht wird nichts automatisch.</p>
</Tile>

<style>
  .group {
    margin-bottom: 2px;
    font-size: 10.5px;
    font-weight: 600;
    color: var(--green);
    text-transform: uppercase;
    letter-spacing: 0.06em;
  }

  .row {
    display: grid;
    grid-template-columns: 34px minmax(0, 1fr) auto;
    gap: 10px;
    align-items: center;
    padding: 9px 4px;
    border-top: 1px solid var(--separator);
  }

  .row.grouped {
    background: linear-gradient(90deg, var(--green-wash), transparent 55%);
    border-radius: var(--r-s);
    border-top-color: transparent;
  }

  .row.own {
    background: var(--blue-wash);
    border-radius: var(--r-s);
  }

  .text {
    display: flex;
    flex-direction: column;
    min-width: 0;
  }

  .name {
    font-size: 14px;
    font-weight: 600;
  }

  em {
    font-style: normal;
    color: var(--blue);
    font-weight: 500;
    font-size: 12px;
  }

  .reason {
    font-size: 11.5px;
    color: var(--fg2);
  }

  .value {
    display: flex;
    flex-direction: column;
    align-items: flex-end;
  }

  .value b {
    font-size: 17px;
    font-weight: 650;
  }

  .value small {
    font-size: 10.5px;
  }

  .foot {
    margin: 10px 0 0;
    font-size: 11px;
    color: var(--fg3);
  }
</style>
