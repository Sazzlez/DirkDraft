<script lang="ts">
  import { championIcon, itemIcon, runeIcon, spellIcon } from '../data/assets';
  import type { ChampionRef } from '../core/view';

  type Props =
    | { kind: 'champion'; champion: ChampionRef | null; size?: number; ring?: 'me' | 'target' | 'key' | null; dim?: boolean; grey?: boolean; title?: string }
    | { kind: 'item' | 'rune' | 'spell'; id: number; size?: number; ring?: 'me' | 'target' | 'key' | null; dim?: boolean; grey?: boolean; title?: string };

  let props: Props = $props();

  const size = $derived(props.size ?? 28);
  const src = $derived(
    props.kind === 'champion'
      ? props.champion && props.champion.key
        ? championIcon(props.champion.key)
        : null
      : props.kind === 'item'
        ? props.id
          ? itemIcon(props.id)
          : null
        : props.kind === 'rune'
          ? runeIcon(props.id)
          : spellIcon(props.id),
  );
  const label = $derived(props.title ?? (props.kind === 'champion' ? (props.champion?.name ?? 'offen') : ''));
  let broken = $state(false);
</script>

<span
  class="icon {props.kind}"
  class:empty={!src || broken}
  class:dim={props.dim}
  class:grey={props.grey}
  class:ring-me={props.ring === 'me'}
  class:ring-target={props.ring === 'target'}
  class:ring-key={props.ring === 'key'}
  style:--s="{size}px"
  title={label}
>
  {#if src && !broken}
    <img {src} alt={label} loading="lazy" onerror={() => (broken = true)} />
  {/if}
</span>

<style>
  .icon {
    display: inline-block;
    flex: none;
    width: var(--s);
    height: var(--s);
    border-radius: 7px;
    overflow: hidden;
    background: var(--card-strong);
    vertical-align: middle;
    position: relative;
  }

  .icon img {
    width: 100%;
    height: 100%;
    display: block;
    object-fit: cover;
  }

  .champion {
    border-radius: 50%;
  }

  .champion img {
    transform: scale(1.12);
  }

  /* Riot draws rune icons for a dark ground: a dark disc in both themes keeps them legible. */
  .rune {
    border-radius: 50%;
    background: rgba(0, 0, 0, 0.35);
  }

  :global(:root[data-theme='light']) .rune {
    background: rgba(28, 28, 34, 0.85);
  }

  .rune img {
    padding: 8%;
  }

  .empty {
    background: transparent;
    box-shadow: inset 0 0 0 1.5px var(--fg3);
  }

  .champion.empty {
    box-shadow: none;
    border: 1.5px dashed var(--fg3);
  }

  .dim {
    opacity: 0.55;
  }

  .grey img {
    filter: grayscale(1) brightness(0.75);
  }

  .ring-me {
    box-shadow: 0 0 0 2px var(--blue);
  }

  .ring-target {
    box-shadow: 0 0 0 2px var(--teal);
  }

  .ring-key {
    box-shadow: 0 0 0 1.5px var(--gold);
  }
</style>
