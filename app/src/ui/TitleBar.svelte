<script lang="ts">
  import type { Area, ViewModel } from '../core/view';
  import Segment from './Segment.svelte';

  let { view, area, onArea }: { view: ViewModel; area: Area; onArea: (area: Area) => void } = $props();

  let pinned = $state(false);
  let menuOpen = $state(false);
</script>

<header class="bar" data-tauri-drag-region>
  <div class="left" data-tauri-drag-region>
    <span class="logo" aria-hidden="true"></span>
    <b>DirkDraft</b>
    <span class="dot {view.connection.state}" title={view.connection.text}></span>
    <span class="status">{view.connection.text}</span>
  </div>

  <div class="center">
    <Segment value={area} onChange={onArea} />
  </div>

  <div class="right">
    <button class="pill" title="Daten aktualisieren">
      {#if view.data.stale}<span class="warn-dot"></span>{/if}
      <span class="num">{view.data.text}</span>
    </button>
    <button class="menu" aria-label="Menü" onclick={() => (menuOpen = !menuOpen)}>
      <svg viewBox="0 0 16 16"><circle cx="3" cy="8" r="1.4" /><circle cx="8" cy="8" r="1.4" /><circle cx="13" cy="8" r="1.4" /></svg>
    </button>
    <button class="ctl" class:on={pinned} aria-label="Im Vordergrund halten" aria-pressed={pinned} onclick={() => (pinned = !pinned)}>
      <svg viewBox="0 0 16 16"><path d="M10.5 1.5 14.5 5.5 12 6.5 9.5 9l.5 3.5-1 1-3-3-3.5 3.5-.5-.5L5.5 10l-3-3 1-1L7 6.5 9.5 4z" /></svg>
    </button>
    <button class="ctl" aria-label="Minimieren">
      <svg viewBox="0 0 16 16"><path d="M3 8.5h10v1H3z" /></svg>
    </button>
    <button class="ctl" aria-label="In den Infobereich">
      <svg viewBox="0 0 16 16"><path d="m4 3.3 4 4 4-4 .7.7-4 4 4 4-.7.7-4-4-4 4-.7-.7 4-4-4-4z" /></svg>
    </button>
  </div>

  {#if menuOpen}
    <div class="dropdown">
      <p class="num">{view.data.text}</p>
      <button>Daten aktualisieren</button>
      <button>Einstellungen</button>
    </div>
  {/if}
</header>

<style>
  .bar {
    position: relative;
    display: grid;
    grid-template-columns: 1fr auto 1fr;
    align-items: center;
    gap: 8px;
    height: 46px;
    padding: 0 8px 0 14px;
    user-select: none;
  }

  .left,
  .right {
    display: flex;
    align-items: center;
    gap: 8px;
    min-width: 0;
  }

  .right {
    justify-content: flex-end;
    gap: 4px;
  }

  .logo {
    width: 20px;
    height: 20px;
    border-radius: 6px;
    background: linear-gradient(135deg, #0ac8b9, var(--blue));
    flex: none;
  }

  b {
    font-weight: 600;
    font-size: 13px;
  }

  .dot {
    width: 7px;
    height: 7px;
    border-radius: 50%;
    flex: none;
  }

  .dot.ok {
    background: var(--green);
    box-shadow: 0 0 0 3px var(--green-wash);
  }

  .dot.warn {
    background: var(--orange);
  }

  .dot.off {
    background: var(--fg3);
  }

  .status {
    color: var(--fg2);
    font-size: 11.5px;
    white-space: nowrap;
    overflow: hidden;
    text-overflow: ellipsis;
  }

  .pill {
    display: inline-flex;
    align-items: center;
    gap: 6px;
    height: 24px;
    padding: 0 10px;
    border-radius: 12px;
    background: var(--card-strong);
    color: var(--fg2);
    font-size: 11.5px;
    white-space: nowrap;
    margin-right: 6px;
  }

  .pill:hover {
    color: var(--fg);
  }

  .warn-dot {
    width: 6px;
    height: 6px;
    border-radius: 50%;
    background: var(--orange);
  }

  .ctl,
  .menu {
    display: grid;
    place-items: center;
    width: 30px;
    height: 30px;
    border-radius: var(--r-s);
    color: var(--fg3);
  }

  .ctl:hover,
  .menu:hover {
    background: var(--card-hover);
    color: var(--fg);
  }

  .ctl.on {
    color: var(--blue);
  }

  svg {
    width: 14px;
    height: 14px;
    fill: currentColor;
  }

  .menu {
    display: none;
  }

  .dropdown {
    position: absolute;
    right: 8px;
    top: 44px;
    z-index: 20;
    display: flex;
    flex-direction: column;
    gap: 2px;
    min-width: 200px;
    padding: 6px;
    border-radius: var(--r-m);
    background: var(--window);
    box-shadow: 0 12px 40px rgba(0, 0, 0, 0.4), 0 0 0 1px var(--separator);
  }

  .dropdown p {
    margin: 4px 8px 6px;
    color: var(--fg2);
    font-size: 11.5px;
  }

  .dropdown button {
    text-align: left;
    padding: 6px 8px;
    border-radius: 6px;
  }

  .dropdown button:hover {
    background: var(--card-hover);
  }

  /* Medium: the status text goes. */
  @container app (max-width: 1099px) {
    .status {
      display: none;
    }
  }

  /* Narrow: the segment gets its own row (Shell), the data pill moves into the menu. */
  @container app (max-width: 719px) {
    .bar {
      grid-template-columns: 1fr auto;
    }

    .center,
    .pill {
      display: none;
    }

    .menu {
      display: grid;
    }
  }
</style>
