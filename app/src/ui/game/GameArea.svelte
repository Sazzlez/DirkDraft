<script lang="ts">
  import type { BuildView, GameView } from '../../core/view';
  import { count } from '../../core/format';
  import Icon from '../Icon.svelte';
  import Tile from '../Tile.svelte';
  import SkillGrid from '../build/SkillGrid.svelte';

  let { game, build }: { game: GameView; build: BuildView | null } = $props();
  const owned = $derived(game.path.flatMap((p) => p.items).filter((i) => i.owned).length);
  const total = $derived(game.path.flatMap((p) => p.items).length);
</script>

<div class="head">
  <Icon kind="champion" champion={game.me} size={38} ring="me" />
  <div class="title">
    <b>{game.title}</b>
    {#if game.draftLine}<span>{game.draftLine}</span>{/if}
  </div>
  <div class="stats num">
    <span class="chip">{game.clock}</span>
    <span class="chip">Stufe {game.level}</span>
    <span class="chip gold">{count(game.gold)} Gold</span>
  </div>
</div>

<div class="layout">
  <div class="col left">
  <div class="buy">
    {#if game.nextBuy}
      <section class="next">
        <Icon kind="item" id={game.nextBuy.item.id} size={44} />
        <span class="t">
          <span class="kicker">{game.nextBuy.supportChoice ? 'Support-Quest erfüllt' : 'Nächster Kauf'}</span>
          <b>{game.nextBuy.item.name}</b>
          {#if game.nextBuy.supportChoice}
            <span>Jetzt im Shop als Upgrade wählen</span>
          {:else}
            <span class="num">{count(game.nextBuy.cost)} Gold · {game.nextBuy.affordable ? 'kaufbar' : `noch ${count(game.nextBuy.cost - game.gold)} Gold`}</span>
          {/if}
        </span>
        {#if game.nextBuy.then}
          <span class="then">danach <Icon kind="item" id={game.nextBuy.then.id} size={26} title={game.nextBuy.then.name} /></span>
        {/if}
      </section>
    {/if}
  </div>

  <div class="skill">
    {#if game.nextSkill}
      <section class="next-skill">
        <span class="key">{game.nextSkill.key}</span>
        <span>{game.nextSkill.now ? 'Jetzt skillen' : `Nächster Skillpunkt, auf Stufe ${game.nextSkill.level}`}</span>
      </section>
    {/if}
  </div>

  <div class="path">
    <Tile title="Dein Build" sub="{owned} von {total} Items">
      <div class="track">
        {#each game.path as group (group.slot)}
          <div class="g">
            <span class="k">{group.slot}</span>
            <span class="icons">
              {#each group.items as item, i (i)}
                <span class="has" class:owned={item.owned}>
                  <Icon kind="item" id={item.id} size={34} dim={item.owned} ring={item.next ? 'target' : null} title={item.name} />
                  {#if item.owned}<svg class="tick" viewBox="0 0 12 12"><path d="m2.5 6.2 2.2 2.2 4.8-4.8" /></svg>{/if}
                </span>
              {/each}
            </span>
          </div>
        {/each}
      </div>
      {#if build}
        <div class="skills">
          <span class="k">Skills · Stufe {game.level}</span>
          <SkillGrid skills={build.skills} taken={game.skillPoints} next={game.nextSkill?.level ?? null} />
        </div>
      {/if}
    </Tile>
  </div>

  {#if game.situational}
    <div class="situational">
      <Tile title="Für dieses Spiel" sub="Vorschlag zur aktuellen Lage">
        {#snippet accessory()}<span class="badge">Alternative</span>{/snippet}
        {#each game.situational as s (s.item.id)}
          <div class="alt">
            <Icon kind="item" id={s.item.id} size={34} title={s.item.name} />
            <span class="t">
              <b>{s.item.name}</b>
              <span>{s.reason}{#if s.instead}{' · '}statt {s.instead.name}{/if}</span>
              <span class="num stats">{s.stats}</span>
            </span>
          </div>
        {:else}
          <p class="tiny">Gegen diese Gegner ist nichts klar besser als dein Build. Bleib dabei.</p>
        {/each}
        <p class="tiny">Was Platin-Spieler deines Champions in derselben Lage häufiger kaufen, sofern es nicht schlechter abschneidet. Der Build oben bleibt dein Plan; das hier ist eine Alternative für dieses Spiel.</p>
      </Tile>
    </div>
  {/if}

  </div>
  <div class="col right">
  <!-- Only with something to say: an empty tile with just its footnote read like a fault (live test 07.11.). -->
  {#if game.hints.length > 0}
  <div class="hints">
    <Tile title="Hinweise">
      {#each game.hints as hint (hint.title)}
        <div class="hint">
          <i class="tone-{hint.tone}"></i>
          <span><b>{hint.title}</b>{hint.detail}</span>
        </div>
      {/each}
      <p class="tiny">Hinweise stehen neben dem Build. Sie ändern seine Reihenfolge nie.</p>
    </Tile>
  </div>
  {/if}

  <div class="enemies">
    <Tile title="Gegner im Spiel" sub="laut Scoreboard">
      {#each game.enemies as enemy (enemy.champion.id)}
        <div class="er">
          <Icon kind="champion" champion={enemy.champion} size={28} />
          <span class="nm">{enemy.champion.name}</span>
          <span class="its">{#each enemy.items as item, i (i)}<Icon kind="item" id={item.id} size={22} title={item.name} />{/each}</span>
          <span class="dt" class:magic={enemy.damage === 'magisch'}>{enemy.damage}</span>
        </div>
      {/each}
    </Tile>
  </div>
  </div>
</div>

<style>
  .head {
    display: flex;
    align-items: center;
    gap: 10px;
    padding: 4px 16px 12px;
  }

  .title {
    display: flex;
    flex-direction: column;
  }

  .title b {
    font-size: 15px;
  }

  .title span {
    font-size: 11.5px;
    color: var(--fg2);
  }

  .stats {
    margin-left: auto;
    display: flex;
    gap: 6px;
  }

  .chip {
    display: inline-flex;
    align-items: center;
    height: 24px;
    padding: 0 10px;
    border-radius: 12px;
    background: var(--card-strong);
    font-size: 12px;
    font-weight: 600;
  }

  .chip.gold {
    color: var(--gold);
  }

  .layout {
    display: grid;
    grid-template-columns: minmax(0, 1.55fr) minmax(0, 1fr);
    gap: 12px;
    padding: 0 16px 16px;
    align-items: start;
  }

  .col {
    display: flex;
    flex-direction: column;
    gap: 12px;
    min-width: 0;
  }

  .enemies { order: 1; }
  .hints { order: 2; }

  .next {
    display: flex;
    align-items: center;
    gap: 12px;
    padding: 12px;
    border-radius: var(--r-l);
    background: var(--blue-wash);
    box-shadow: inset 0 0 0 1px var(--blue-line);
  }

  .t {
    display: flex;
    flex-direction: column;
  }

  .kicker {
    font-size: 11.5px;
    font-weight: 600;
    color: var(--blue);
  }

  .t b {
    font-size: 15px;
  }

  .t .num {
    font-size: 11.5px;
    color: var(--fg2);
  }

  .then {
    margin-left: auto;
    display: flex;
    align-items: center;
    gap: 8px;
    font-size: 11.5px;
    color: var(--fg2);
  }

  .next-skill {
    display: flex;
    align-items: center;
    gap: 10px;
    padding: 10px 12px;
    border-radius: var(--r-l);
    background: var(--card);
    font-size: 12.5px;
  }

  .key {
    width: 26px;
    height: 26px;
    border-radius: 7px;
    background: var(--blue);
    color: #fff;
    font-weight: 700;
    display: grid;
    place-items: center;
  }

  .track {
    display: flex;
    flex-wrap: wrap;
    gap: 16px;
  }

  .g {
    display: flex;
    flex-direction: column;
    gap: 6px;
  }

  .k {
    font-size: 11.5px;
    font-weight: 600;
    color: var(--fg2);
  }

  .icons {
    display: flex;
    gap: 5px;
  }

  .has {
    position: relative;
    display: inline-flex;
  }

  .tick {
    position: absolute;
    right: -4px;
    bottom: -4px;
    width: 15px;
    height: 15px;
    padding: 2px;
    border-radius: 50%;
    background: var(--green);
    fill: none;
    stroke: #06210e;
    stroke-width: 2;
  }

  .skills {
    margin-top: 14px;
    display: flex;
    flex-direction: column;
    gap: 8px;
  }

  .hint {
    display: grid;
    grid-template-columns: 10px minmax(0, 1fr);
    gap: 10px;
    padding: 7px 0;
    font-size: 12px;
    color: var(--fg2);
  }

  .hint + .hint {
    border-top: 1px solid var(--separator);
  }

  .hint i {
    width: 7px;
    height: 7px;
    margin-top: 5px;
    border-radius: 50%;
    background: currentColor;
  }

  .hint b {
    display: block;
    color: var(--fg);
    font-weight: 600;
  }

  .tiny {
    margin: 8px 0 0;
    font-size: 11.5px;
    color: var(--fg3);
  }

  /* Situational alternatives: set apart from the plan by a dashed teal rim and the badge, so they
     never read as part of the build itself. */
  .situational :global(.tile) {
    box-shadow: inset 0 0 0 1px color-mix(in srgb, var(--teal, #2fd6c5) 45%, transparent);
    background: color-mix(in srgb, var(--teal, #2fd6c5) 6%, var(--card));
  }

  .badge {
    display: inline-flex;
    align-items: center;
    height: 20px;
    padding: 0 8px;
    border-radius: 999px;
    font: var(--t-caption);
    font-weight: 600;
    color: var(--teal, #2fd6c5);
    background: color-mix(in srgb, var(--teal, #2fd6c5) 14%, transparent);
  }

  .alt {
    display: grid;
    grid-template-columns: 34px minmax(0, 1fr);
    gap: 10px;
    align-items: center;
    padding: 7px 0;
  }

  .alt + .alt {
    border-top: 1px solid var(--separator);
  }

  .alt .t {
    display: flex;
    flex-direction: column;
    gap: 1px;
    min-width: 0;
    font-size: 12px;
    color: var(--fg2);
  }

  .alt b {
    color: var(--fg);
    font: var(--t-head);
    font-size: 13px;
  }

  .alt .stats {
    color: var(--fg3);
    font-size: 11.5px;
  }

  .er {
    display: grid;
    grid-template-columns: 28px minmax(0, 1fr) auto auto;
    gap: 8px;
    align-items: center;
    padding: 6px 0;
  }

  .er + .er {
    border-top: 1px solid var(--separator);
  }

  .nm {
    font-size: 12.5px;
    font-weight: 600;
  }

  .its {
    display: flex;
    gap: 3px;
  }

  .dt {
    font-size: 11.5px;
    font-weight: 600;
    padding: 2px 7px;
    border-radius: 9px;
    background: var(--orange-wash);
    color: var(--orange);
  }

  .dt.magic {
    background: var(--purple-wash);
    color: var(--purple);
  }

  @container app (max-width: 1099px) {
    .layout {
      grid-template-columns: minmax(0, 1.3fr) minmax(0, 1fr);
    }
  }

  /* Narrow: next buy, skill point, hints, build path, enemies — one column in that order. */
  @container app (max-width: 719px) {
    .head {
      padding: 0 12px 10px;
      flex-wrap: wrap;
    }

    .stats {
      margin-left: 0;
      width: 100%;
    }

    .layout {
      display: flex;
      flex-direction: column;
      padding: 0 12px 12px;
      gap: 10px;
    }

    .col {
      display: contents;
    }

    .buy { order: 1; }
    .skill { order: 2; }
    .hints { order: 3; }
    .path { order: 4; }
    .situational { order: 5; }
    .enemies { order: 6; }

    .then {
      display: none;
    }
  }
</style>
