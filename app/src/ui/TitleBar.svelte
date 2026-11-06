<script lang="ts">
  import { AREAS, type Area, type ViewModel } from '../core/view';
  import Segment from './Segment.svelte';

  let { view, area, onArea }: { view: ViewModel; area: Area; onArea: (area: Area) => void } = $props();

  let pinned = $state(false);
  let menuOpen = $state(false);
</script>

<header class="bar" data-tauri-drag-region>
  <div class="left" data-tauri-drag-region>
    <span class="logo" aria-hidden="true"><svg viewBox="0 0 20 20"><path d="M5.65 5.5h4.2a4.5 4.5 0 0 1 0 9h-4.2z" /></svg></span>
    <b>DirkDraft</b>
    <span class="status"><span class="dot {view.connection.state}"></span><span class="text">{view.connection.text}</span></span>
  </div>

  <div class="center">
    <Segment value={area} onChange={onArea} options={AREAS} label="Bereich" />
  </div>

  <div class="right">
    <span class="pill" title="Aktualisiert sich von selbst — nie während Draft oder Spiel">
      <span class="dot-small" class:stale={view.data.stale}></span>
      <span class="num">{view.data.text}</span>
    </span>
    <button class="ctl menu" aria-label="Menü" onclick={() => (menuOpen = !menuOpen)}>
      <svg viewBox="0 0 20 20"><circle cx="5" cy="10" r="1.3" /><circle cx="10" cy="10" r="1.3" /><circle cx="15" cy="10" r="1.3" /></svg>
    </button>
    <button class="ctl" class:on={pinned} aria-label="Im Vordergrund halten" aria-pressed={pinned} onclick={() => (pinned = !pinned)}>
      <svg viewBox="0 0 20 20" class="stroke"><path d="M12.2 3.5 16.5 7.8M14.4 5.6l-3.3 3.3 1.1 3.3-1.1 1.1-6.5-6.5 1.1-1.1 3.3 1.1 3.3-3.3M8 12l-4 4" /></svg>
    </button>
    <button class="ctl" aria-label="Minimieren">
      <svg viewBox="0 0 20 20" class="stroke"><path d="M5 10.5h10" /></svg>
    </button>
    <button class="ctl" aria-label="In den Infobereich">
      <svg viewBox="0 0 20 20" class="stroke"><path d="M5.5 5.5l9 9m0-9-9 9" /></svg>
    </button>
  </div>

  {#if menuOpen}
    <div class="popover">
      <p class="num">{view.data.text}</p>
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
    height: 52px;
    padding: 0 10px 0 16px;
    user-select: none;
  }

  .left,
  .right {
    display: flex;
    align-items: center;
    gap: 9px;
    min-width: 0;
  }

  .right {
    justify-content: flex-end;
    gap: 2px;
  }

  .logo {
    display: grid;
    place-items: center;
    width: 22px;
    height: 22px;
    border-radius: 6px;
    background: linear-gradient(145deg, #2fd6c5, var(--blue) 70%);
    box-shadow: inset 0 0.5px 0 rgba(255, 255, 255, 0.4), 0 1px 3px rgba(0, 0, 0, 0.3);
    flex: none;
  }

  .logo svg {
    width: 14px;
    height: 14px;
    fill: none;
    stroke: #fff;
    stroke-width: 2.2;
  }

  b {
    font: var(--t-head);
    font-size: 14px;
    letter-spacing: -0.015em;
  }

  .status {
    display: inline-flex;
    align-items: center;
    gap: 6px;
    height: 22px;
    padding: 0 9px 0 8px;
    border-radius: 999px;
    background: var(--card);
    color: var(--fg2);
    font: var(--t-caption);
    white-space: nowrap;
    min-width: 0;
  }

  .dot {
    width: 6px;
    height: 6px;
    border-radius: 50%;
    flex: none;
    background: var(--fg3);
  }

  .dot.ok {
    background: var(--green);
    box-shadow: 0 0 6px var(--green);
  }

  .dot.warn {
    background: var(--orange);
  }

  .pill {
    display: inline-flex;
    align-items: center;
    gap: 7px;
    height: 26px;
    padding: 0 12px;
    margin-right: 8px;
    border-radius: 999px;
    background: var(--card);
    box-shadow: var(--edge);
    color: var(--fg2);
    font: var(--t-caption);
    white-space: nowrap;
    cursor: default;
  }

  .dot-small {
    width: 6px;
    height: 6px;
    border-radius: 50%;
    background: var(--green);
  }

  .dot-small.stale {
    background: var(--orange);
  }

  .ctl {
    display: grid;
    place-items: center;
    width: 30px;
    height: 30px;
    border-radius: 8px;
    color: var(--fg2);
    transition: background 0.15s var(--ease), color 0.15s var(--ease);
  }

  .ctl:hover {
    background: var(--card-hover);
    color: var(--fg);
  }

  .ctl.on {
    color: var(--blue);
  }

  svg {
    width: 16px;
    height: 16px;
    fill: currentColor;
  }

  svg.stroke {
    fill: none;
    stroke: currentColor;
    stroke-width: 1.6;
    stroke-linecap: round;
    stroke-linejoin: round;
  }

  .menu {
    display: none;
  }

  .popover {
    position: absolute;
    right: 10px;
    top: 48px;
    z-index: 20;
    display: flex;
    flex-direction: column;
    gap: 1px;
    min-width: 220px;
    padding: 6px;
    border-radius: var(--r-m);
    background: var(--popover);
    -webkit-backdrop-filter: blur(30px);
    backdrop-filter: blur(30px);
    box-shadow: 0 12px 40px rgba(0, 0, 0, 0.35), var(--edge);
  }

  .popover p {
    margin: 4px 10px 6px;
    color: var(--fg2);
    font: var(--t-caption);
  }

  .popover button {
    text-align: left;
    padding: 7px 10px;
    border-radius: 6px;
  }

  .popover button:hover {
    background: var(--blue);
    color: #fff;
  }

  @container app (max-width: 1099px) {
    .status .text {
      display: none;
    }

    .status {
      padding: 0 8px;
    }
  }

  @container app (max-width: 719px) {
    .bar {
      grid-template-columns: 1fr auto;
      padding: 0 8px 0 16px;
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
