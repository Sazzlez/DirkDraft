<script lang="ts">
  import type { PostgameView } from '../../core/view';
  import Icon from '../Icon.svelte';
  import Tile from '../Tile.svelte';

  let { postgame }: { postgame: PostgameView } = $props();
</script>

<div class="head">
  <Icon kind="champion" champion={postgame.champion} size={44} />
  <div>
    <h2 class:win={postgame.result === 'Sieg'}>{postgame.result}</h2>
    <p>{postgame.champion.name}</p>
  </div>
</div>

<div class="grid">
  <Tile title="Aus den ersten drei gepickt?">
    <p class="big tone-{postgame.pickedFromTop3 ? 'good' : 'neutral'}">
      {postgame.pickedFromTop3 === null ? 'keine Empfehlung' : postgame.pickedFromTop3 ? 'Ja' : 'Nein'}
    </p>
    <p class="note">{postgame.pickNote}</p>
  </Tile>
  <Tile title="Wo wich der Build ab?">
    <p class="note">{postgame.buildNote}</p>
  </Tile>
  <Tile title="Deine letzten Spiele" sub={postgame.topThreeRate}>
    {#each postgame.history as game, i (i)}
      <div class="h">
        <Icon kind="champion" champion={game.champion} size={26} />
        <span class="nm">{game.champion.name}</span>
        <span class="r" class:win={game.result === 'S'}>{game.result === 'S' ? 'Sieg' : 'Niederlage'}</span>
        <span class="top">{game.fromTop3 === null ? '—' : game.fromTop3 ? 'Top 3' : 'eigene Wahl'}</span>
        <span class="when">{game.when}</span>
      </div>
    {/each}
    <p class="tiny">Nur lokal gespeichert, ohne Netzzugriff.</p>
  </Tile>
</div>

<style>
  .head {
    display: flex;
    align-items: center;
    gap: 12px;
    padding: 6px 16px 14px;
  }

  h2 {
    margin: 0;
    font-size: 22px;
    font-weight: 700;
    color: var(--red);
  }

  h2.win {
    color: var(--green);
  }

  .head p {
    margin: 0;
    color: var(--fg2);
  }

  .grid {
    display: grid;
    grid-template-columns: repeat(auto-fill, minmax(260px, 1fr));
    gap: 12px;
    padding: 0 16px 16px;
    align-items: start;
  }

  .big {
    margin: 0;
    font-size: 24px;
    font-weight: 700;
  }

  .note {
    margin: 4px 0 0;
    font-size: 12.5px;
    color: var(--fg2);
  }

  .h {
    display: grid;
    grid-template-columns: 26px minmax(0, 1fr) auto auto auto;
    gap: 8px;
    align-items: center;
    padding: 6px 0;
    font-size: 12px;
  }

  .h + .h {
    border-top: 1px solid var(--separator);
  }

  .nm {
    font-weight: 600;
  }

  .r {
    color: var(--red);
  }

  .r.win {
    color: var(--green);
  }

  .top,
  .when {
    color: var(--fg2);
  }

  .tiny {
    margin: 8px 0 0;
    font-size: 10.5px;
    color: var(--fg3);
  }

  @container app (max-width: 719px) {
    .grid {
      grid-template-columns: minmax(0, 1fr);
      padding: 0 12px 12px;
    }

    .when {
      display: none;
    }
  }
</style>
