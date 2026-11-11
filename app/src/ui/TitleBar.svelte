<script lang="ts">
  import { AREAS, type Area, type ViewModel } from '../core/view';
  import Segment from './Segment.svelte';
  import { hideToTray, minimize, onTrayHint } from '../platform/shell';
  import { changeSetting, settings } from '../live/settings.svelte';
  import { applyUpdate, update } from '../live/update.svelte';

  let {
    view,
    area,
    onArea,
    onSettings,
  }: { view: ViewModel; area: Area; onArea: (area: Area) => void; onSettings: () => void } = $props();

  let menuOpen = $state(false);
  let trayHint = $state(false);

  // The hint when the window goes to the tray for the first time (shell.rs sends it).
  onTrayHint(() => (trayHint = true));

  function togglePin() {
    void changeSetting('keepOnTop', !settings.keepOnTop);
  }

  function openSettings() {
    menuOpen = false;
    onSettings();
  }
</script>

<!-- The narrow menu closes on any click outside it, like every menu does. -->
<svelte:window onclick={(e) => { if (menuOpen && !(e.target as Element | null)?.closest('.popover, .menu')) menuOpen = false; }} />

<!-- Tauri drags by the element under the mouse, not its parent: every passive part carries the mark. -->
<header class="bar" data-tauri-drag-region>
  <div class="left" data-tauri-drag-region>
    <span class="logo" aria-hidden="true" data-tauri-drag-region><svg viewBox="0 0 20 20"><path d="M5.65 5.5h4.2a4.5 4.5 0 0 1 0 9h-4.2z" /></svg></span>
    <b data-tauri-drag-region>DirkDraft</b>
    <span class="status" data-tauri-drag-region><span class="dot {view.connection.state}"></span><span class="text" data-tauri-drag-region>{view.connection.text}</span></span>
  </div>

  <div class="center">
    <Segment value={area} onChange={onArea} options={AREAS} label="Bereich" />
  </div>

  <div class="right" data-tauri-drag-region>
    {#if update.available}
      <button
        class="update"
        onclick={() => void applyUpdate()}
        disabled={update.busy}
        title={update.error ? `Fehlgeschlagen: ${update.error}` : 'Lädt das Update, installiert es und startet DirkDraft neu.'}
      >
        <span class="long">{update.busy ? 'Wird aktualisiert …' : update.error ? 'Update erneut versuchen' : `Version ${update.available} ist da · Jetzt aktualisieren`}</span>
        <span class="short">{update.busy ? 'Aktualisiert …' : update.error ? 'Erneut versuchen' : `Update ${update.available}`}</span>
      </button>
    {/if}
    <span class="pill" title="{view.data.text} — aktualisiert sich von selbst, nie während Draft oder Spiel" data-tauri-drag-region>
      <span class="dot-small" class:stale={view.data.stale}></span>
      <span class="num long">{view.data.text}</span>
      <span class="num short">{view.data.short}</span>
    </span>
    <button class="ctl menu" aria-label="Menü" onclick={() => (menuOpen = !menuOpen)}>
      <svg viewBox="0 0 20 20"><circle cx="5" cy="10" r="1.3" /><circle cx="10" cy="10" r="1.3" /><circle cx="15" cy="10" r="1.3" /></svg>
    </button>
    <button class="ctl gear" aria-label="Einstellungen" onclick={openSettings}>
      <svg viewBox="0 0 20 20" class="stroke">
        <circle cx="10" cy="10" r="5.2" />
        <circle cx="10" cy="10" r="2" />
        <path stroke-width="2.4" stroke-linecap="butt" d="M15.2 10h2.2M13.68 13.68l1.55 1.55M10 15.2v2.2M6.32 13.68l-1.55 1.55M4.8 10H2.6M6.32 6.32 4.77 4.77M10 4.8V2.6M13.68 6.32l1.55-1.55" />
      </svg>
    </button>
    <button class="ctl" class:on={settings.keepOnTop} aria-label="Im Vordergrund halten" aria-pressed={settings.keepOnTop} onclick={togglePin}>
      <svg viewBox="0 0 20 20" class="stroke"><path d="M12.2 3.5 16.5 7.8M14.4 5.6l-3.3 3.3 1.1 3.3-1.1 1.1-6.5-6.5 1.1-1.1 3.3 1.1 3.3-3.3M8 12l-4 4" /></svg>
    </button>
    <button class="ctl" aria-label="Minimieren" onclick={() => void minimize()}>
      <svg viewBox="0 0 20 20" class="stroke"><path d="M5 10.5h10" /></svg>
    </button>
    <button class="ctl" aria-label="In den Infobereich" onclick={() => void hideToTray()}>
      <svg viewBox="0 0 20 20" class="stroke"><path d="M5.5 5.5l9 9m0-9-9 9" /></svg>
    </button>
  </div>

  {#if trayHint}
    <div class="hint" role="status">
      <b>DirkDraft läuft weiter</b>
      <span>Das Fenster liegt jetzt im Infobereich unten rechts. Beenden: Rechtsklick auf das Symbol → Beenden.</span>
    </div>
  {/if}

  {#if menuOpen}
    <div class="popover">
      <p class="num">{view.data.text}</p>
      <button onclick={openSettings}>Einstellungen</button>
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
    background: var(--fg4);
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
    /* The data line can be long ("Patch 16.22 · Platin · 6.185 Spiele · vor 5 Std."): it gives way
       to the area switch instead of running under it. */
    min-width: 0;
    flex: 0 1 auto;
    overflow: hidden;
  }

  .pill .num {
    overflow: hidden;
    text-overflow: ellipsis;
  }

  .update {
    flex: 0 1 auto;
    min-width: 0;
    height: 26px;
    padding: 0 12px;
    margin-right: 6px;
    border-radius: 999px;
    background: var(--blue);
    color: #fff;
    font: var(--t-caption);
    font-weight: 600;
    white-space: nowrap;
    overflow: hidden;
    text-overflow: ellipsis;
  }

  .update:disabled {
    opacity: 0.7;
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
    color: var(--blue-text);
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

  .hint {
    position: absolute;
    right: 10px;
    top: 48px;
    z-index: 30;
    display: flex;
    flex-direction: column;
    gap: 3px;
    max-width: 300px;
    padding: 10px 12px;
    border-radius: var(--r-m);
    background: var(--popover);
    -webkit-backdrop-filter: blur(30px);
    backdrop-filter: blur(30px);
    box-shadow: 0 12px 40px rgba(0, 0, 0, 0.35), var(--edge);
    font: var(--t-caption);
    color: var(--fg2);
  }

  .hint b {
    color: var(--fg);
    font-size: 13px;
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

  .short {
    display: none;
  }

  @container app (max-width: 1099px) {
    .status .text {
      display: none;
    }

    /* Medium: the long data line and the update label would push each other off; both shorten. */
    .long {
      display: none;
    }

    .short {
      display: inline;
    }

    .status {
      padding: 0 8px;
    }
  }

  /* Just above the narrow layout the data line lacked 12 px: a tighter pill without the status dot
     gives them (the text says the age, the tooltip the rest). */
  @container app (min-width: 720px) and (max-width: 879px) {
    .pill {
      padding: 0 8px;
      margin-right: 4px;
    }

    .pill .dot-small {
      display: none;
    }
  }

  @container app (max-width: 719px) {
    .bar {
      grid-template-columns: 1fr auto;
      padding: 0 8px 0 16px;
    }

    .center,
    .pill,
    .gear {
      display: none;
    }

    .menu {
      display: grid;
    }
  }
</style>
