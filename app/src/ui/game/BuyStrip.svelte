<script lang="ts">
  import type { GameView } from '../../core/view';
  import { count } from '../../core/format';
  import Icon from '../Icon.svelte';

  // What the gold at hand buys (core/game basketOf), drawn like the shop: the buys in order with "+",
  // the item they are for after "→", and a bar of the gold they take. With nothing affordable: the
  // next buy and how far the gold is from it. In LoL gold, like the gold chip in the head line.
  let { game }: { game: GameView } = $props();

  const support = $derived(game.nextBuy?.supportChoice ? game.nextBuy : null);
  const buys = $derived(support ? [] : game.basket);
  // One goal for all buys: the arrow points at it. Mixed goals: each buy names its own.
  const goal = $derived.by(() => {
    const first = buys[0]?.then ?? null;
    return first && buys.every((buy) => buy.then?.id === first.id) ? first : null;
  });
  const spent = $derived(buys.reduce((sum, buy) => sum + buy.cost, 0));
  const next = $derived(!support && buys.length === 0 ? game.nextBuy : null);
  const progress = $derived(next && next.cost > 0 ? Math.min(1, game.gold / next.cost) : 0);
</script>

{#if support}
  <section class="buy">
    <div class="top"><span class="kicker">Support-Quest erfüllt</span></div>
    <div class="recipe">
      <span class="part">
        <Icon kind="item" id={support.item.id} size={40} title={support.item.name} />
        <span class="pt"><b>{support.item.name}</b><span class="sub">Jetzt im Shop als Upgrade wählen</span></span>
      </span>
    </div>
  </section>
{:else if buys.length > 0}
  <section class="buy" aria-label="Jetzt kaufen">
    <div class="top">
      <span class="kicker">Jetzt kaufen</span>
      <span class="purse num">{count(game.gold)} Gold</span>
    </div>
    <div class="recipe">
      {#each buys as buy, i (i)}
        {#if i > 0}<span class="op" aria-hidden="true">+</span>{/if}
        <span class="part">
          <span class="ic" class:swapped={buy.situational}><Icon kind="item" id={buy.item.id} size={40} title={buy.item.name} /></span>
          <span class="pt">
            <b>{buy.item.name}</b>
            <span class="sub num"><span class="cost">{count(buy.cost)} Gold</span>{#if buy.situational}{' · '}<span class="sit">situativ</span>{/if}</span>
            {#if !goal && buy.then}<span class="sub">für {buy.then.name}</span>{/if}
          </span>
        </span>
      {/each}
      {#if goal}
        <span class="op arrow" aria-hidden="true">→</span>
        <span class="part goal">
          <Icon kind="item" id={goal.id} size={40} ring="target" title={goal.name} />
          <span class="pt"><span class="sub">für</span><b>{goal.name}</b></span>
        </span>
      {/if}
    </div>
    <div class="bar" role="img" aria-label="{buys.map((buy) => `${buy.item.name} ${count(buy.cost)}`).join(', ')}, Rest {count(game.goldLeft)} Gold">
      {#each buys as buy, i (i)}<i class:alt={i % 2 === 1} style:flex-grow={buy.cost}></i>{/each}
      {#if game.goldLeft > 0}<i class="rest" style:flex-grow={game.goldLeft}></i>{/if}
    </div>
    <div class="foot num">
      <span>{count(spent)} Gold für {buys.length === 1 ? 'diesen Kauf' : `${buys.length} Käufe`}</span>
      <span>Rest {count(game.goldLeft)} Gold</span>
    </div>
  </section>
{:else if next}
  <section class="buy" aria-label="Nächster Kauf">
    <div class="top">
      <span class="kicker">Nächster Kauf</span>
      <span class="purse num">{count(game.gold)} / {count(next.cost)} Gold</span>
    </div>
    <div class="recipe">
      <span class="part">
        <span class="ic" class:swapped={next.situational}><Icon kind="item" id={next.item.id} size={40} title={next.item.name} /></span>
        <span class="pt">
          <b>{next.item.name}</b>
          <span class="sub num"><span class="cost">{count(next.cost)} Gold</span>{#if next.situational}{' · '}<span class="sit">situativ</span>{/if}</span>
        </span>
      </span>
      {#if next.then}
        <span class="op arrow" aria-hidden="true">→</span>
        <span class="part goal">
          <Icon kind="item" id={next.then.id} size={40} ring="target" title={next.then.name} />
          <span class="pt"><span class="sub">für</span><b>{next.then.name}</b></span>
        </span>
      {/if}
    </div>
    <div class="bar" role="img" aria-label="{count(game.gold)} von {count(next.cost)} Gold">
      <i style:flex-grow={progress}></i>
      {#if progress < 1}<i class="rest" style:flex-grow={1 - progress}></i>{/if}
    </div>
    <div class="foot num">
      <span>{Math.round(progress * 100)} %</span>
      <span>{next.affordable ? 'kaufbar' : `noch ${count(next.cost - game.gold)} Gold`}</span>
    </div>
  </section>
{/if}

<style>
  /* An inset panel at the head of the build tile, tinted with the gold it spends. */
  .buy {
    margin-bottom: 14px;
    padding: 12px;
    border-radius: var(--r-m);
    background: color-mix(in srgb, var(--gold) 9%, transparent);
    box-shadow: inset 0 0 0 1px color-mix(in srgb, var(--gold) 32%, transparent);
  }

  .top {
    display: flex;
    align-items: baseline;
    justify-content: space-between;
    gap: 8px;
    margin-bottom: 10px;
  }

  .kicker {
    font-size: 12px;
    font-weight: 600;
    color: var(--gold-text);
  }

  .purse {
    font-size: 12px;
    font-weight: 600;
    color: var(--gold-text);
  }

  .recipe {
    display: flex;
    flex-wrap: wrap;
    align-items: center;
    gap: 8px 10px;
  }

  .part {
    display: flex;
    align-items: center;
    gap: 9px;
    min-width: 0;
  }

  .ic {
    position: relative;
    display: inline-flex;
  }

  /* A situational item in the plan's place: the orange dot of the build path. */
  .ic.swapped::before {
    content: '';
    position: absolute;
    top: -3px;
    right: -3px;
    z-index: 1;
    width: 9px;
    height: 9px;
    border-radius: 50%;
    background: var(--orange);
    box-shadow: 0 0 0 2px var(--card);
  }

  .pt {
    display: flex;
    flex-direction: column;
    min-width: 0;
    line-height: 1.3;
  }

  .pt b {
    font-size: 13.5px;
  }

  .sub {
    font-size: 11.5px;
    color: var(--fg2);
  }

  .cost {
    color: var(--gold-text);
    font-weight: 600;
  }

  .sit {
    color: var(--orange-text);
    font-weight: 600;
  }

  .op {
    font-size: 16px;
    font-weight: 600;
    color: var(--fg3);
  }

  .goal .pt b {
    color: var(--fg);
  }

  /* The gold at hand, cut into what each buy takes; what is left stays on the track. */
  .bar {
    display: flex;
    gap: 2px;
    height: 8px;
    margin-top: 12px;
    border-radius: 4px;
    overflow: hidden;
  }

  .bar i {
    display: block;
    min-width: 3px;
    background: var(--gold);
  }

  .bar i.alt {
    background: color-mix(in srgb, var(--gold) 62%, var(--fg));
  }

  .bar i.rest {
    background: var(--track);
  }

  .foot {
    display: flex;
    justify-content: space-between;
    gap: 8px;
    margin-top: 6px;
    font-size: 11.5px;
    color: var(--fg2);
  }

  /* Narrow: one buy per line, the goal last; "für" says what "+" and "→" said. */
  @container app (max-width: 719px) {
    .recipe {
      flex-direction: column;
      align-items: flex-start;
    }

    .op {
      display: none;
    }
  }
</style>
