<script lang="ts">
  // The settings sheet: a few switches, grouped as on Apple platforms. Every change is saved at once
  // in 1.5's settings.json (live/settings.svelte.ts) — no "Speichern" button to forget.
  import { fly, fade } from 'svelte/transition';
  import Segment from './Segment.svelte';
  import { changeSetting, settings, type AppSettings } from '../live/settings.svelte';
  import { canGlass, glassNow, inShell } from '../platform/shell';
  import { invoke } from '@tauri-apps/api/core';

  /** Where "Diagnose speichern" put its folder, or why it could not. */
  let saved = $state<string | null>(null);

  async function saveDiagnostics() {
    try {
      saved = `Gespeichert: ${await invoke<string>('diagnose_export')}`;
    } catch (e) {
      saved = `Nicht gespeichert: ${String(e)}`;
    }
  }

  let { onClose }: { onClose: () => void } = $props();

  type Flag = { [K in keyof AppSettings]: AppSettings[K] extends boolean ? K : never }[keyof AppSettings];

  const windowRows: { key: Flag; label: string; detail: string }[] = [
    { key: 'autoShowOnChampSelect', label: 'Bei Champ Select einblenden', detail: 'Ohne dem Client den Fokus zu nehmen.' },
    { key: 'autoShowOnGameStart', label: 'Bei Spielstart einblenden', detail: 'Auch wenn das Spiel das Fenster beim Start minimiert.' },
    { key: 'autoHideAfterChampSelect', label: 'Nach dem Draft ausblenden', detail: 'Zurück in den Infobereich, sobald das Champ Select endet.' },
    { key: 'keepOnTop', label: 'Immer im Vordergrund', detail: 'Wie die Reißzwecke oben rechts.' },
  ];

  function onKey(e: KeyboardEvent) {
    if (e.key === 'Escape') onClose();
  }
</script>

<svelte:window onkeydown={onKey} />

<div class="backdrop" transition:fade={{ duration: 150 }} onclick={onClose} role="presentation"></div>

<div class="sheet" role="dialog" aria-modal="true" aria-label="Einstellungen" transition:fly={{ y: 12, duration: 220 }}>
  <header>
    <h2>Einstellungen</h2>
    <button class="done" onclick={onClose}>Fertig</button>
  </header>

  <section>
    <h3>Fenster</h3>
    <div class="group">
      {#each windowRows as row (row.key)}
        <label class="row">
          <span class="text">
            <b>{row.label}</b>
            <small>{row.detail}</small>
          </span>
          <input type="checkbox" role="switch" checked={settings[row.key]} onchange={(e) => void changeSetting(row.key, e.currentTarget.checked)} />
        </label>
      {/each}
      {#if canGlass}
        <label class="row">
          <span class="text">
            <b>Glas-Effekt</b>
            <small>{settings.glassEffect === glassNow ? 'Windows 11 scheint durch das Fenster (Mica). Aus: deckend wie unter Windows 10.' : 'Wirkt beim nächsten Start von DirkDraft.'}</small>
          </span>
          <input type="checkbox" role="switch" checked={settings.glassEffect} onchange={(e) => void changeSetting('glassEffect', e.currentTarget.checked)} />
        </label>
      {/if}
    </div>
  </section>

  <section>
    <h3>Übernehmen</h3>
    <div class="group">
      <div class="row">
        <span class="text">
          <b>Blitz auf Taste</b>
          <small>„Runen und Zauber übernehmen“ legt Blitz immer auf diese Taste.</small>
        </span>
        <Segment
          value={settings.flashKey}
          onChange={(v) => void changeSetting('flashKey', v)}
          options={[
            { id: 'D', label: 'D' },
            { id: 'F', label: 'F' },
          ]}
          label="Blitz-Taste"
          small
        />
      </div>
    </div>
  </section>

  <section>
    <h3>Vorschläge</h3>
    <div class="group">
      <label class="row">
        <span class="text">
          <b>Auch nicht gekaufte Champions</b>
          <small>Sonst stehen nur Champions in der Liste, die du besitzt oder die gerade gratis sind.</small>
        </span>
        <input type="checkbox" role="switch" checked={settings.showUnowned} onchange={(e) => void changeSetting('showUnowned', e.currentTarget.checked)} />
      </label>
      <label class="row">
        <span class="text">
          <b>Team-Modus</b>
          <small>Für Flex mit festem Team: plant die offenen Plätze gemeinsam, schlägt eine Pick-Reihenfolge und fünf abgestimmte Banns vor.</small>
        </span>
        <input type="checkbox" role="switch" checked={settings.teamMode} onchange={(e) => void changeSetting('teamMode', e.currentTarget.checked)} />
      </label>
    </div>
  </section>

  <section>
    <h3>Diagnose</h3>
    <div class="group">
      <label class="row">
        <span class="text">
          <b>Drafts und Spiele mitschreiben</b>
          <small>Für die Fehlersuche: die letzten 10 Drafts und 3 Spiele, ohne Namen, nur auf diesem PC. Normalerweise aus.</small>
        </span>
        <input type="checkbox" role="switch" checked={settings.diagnostics} onchange={(e) => void changeSetting('diagnostics', e.currentTarget.checked)} />
      </label>
      <div class="row">
        <span class="text">
          <b>Diagnose speichern</b>
          <small>{saved ?? 'Legt das Log (und, falls eingeschaltet, die Aufzeichnungen) als Ordner auf den Desktop — zum Weitergeben, wenn etwas nicht stimmt.'}</small>
        </span>
        <button class="action" onclick={saveDiagnostics} disabled={!inShell}>Speichern</button>
      </div>
    </div>
  </section>

  <p class="foot">Die Daten aktualisieren sich von selbst alle 30 Minuten — nie während Draft oder Spiel.</p>
</div>

<style>
  .backdrop {
    position: absolute;
    inset: 0;
    z-index: 40;
    background: rgba(0, 0, 0, 0.35);
  }

  .sheet {
    position: absolute;
    z-index: 41;
    top: 56px;
    right: 12px;
    width: min(420px, calc(100% - 24px));
    max-height: calc(100% - 68px);
    overflow-y: auto;
    padding: 16px;
    border-radius: var(--r-l);
    background: var(--popover);
    -webkit-backdrop-filter: blur(40px) saturate(180%);
    backdrop-filter: blur(40px) saturate(180%);
    box-shadow: 0 20px 60px rgba(0, 0, 0, 0.4), var(--edge);
  }

  header {
    display: flex;
    align-items: center;
    justify-content: space-between;
    margin-bottom: 10px;
  }

  h2 {
    margin: 0;
    font: var(--t-title);
  }

  .done {
    padding: 5px 12px;
    border-radius: 8px;
    color: var(--blue);
    font-weight: 600;
  }

  .done:hover {
    background: var(--blue-wash);
  }

  .action {
    flex: none;
    padding: 5px 12px;
    border-radius: 8px;
    background: var(--blue-wash);
    color: var(--blue);
    font-weight: 600;
  }

  .action:disabled {
    opacity: 0.5;
    cursor: default;
  }

  section + section {
    margin-top: 14px;
  }

  h3 {
    margin: 0 4px 6px;
    font: var(--t-caption);
    color: var(--fg2);
    text-transform: uppercase;
    letter-spacing: 0.04em;
  }

  .group {
    border-radius: var(--r-m);
    background: var(--card);
    overflow: hidden;
  }

  .row {
    display: flex;
    align-items: center;
    justify-content: space-between;
    gap: 12px;
    padding: 10px 12px;
    cursor: default;
  }

  .row + .row {
    box-shadow: inset 0 0.5px 0 var(--separator);
  }

  .text {
    display: flex;
    flex-direction: column;
    gap: 2px;
    min-width: 0;
  }

  .text b {
    font-weight: 500;
  }

  .text small {
    font-size: 11.5px;
    color: var(--fg2);
  }

  /* An Apple-style switch made from the checkbox itself: keyboard and screen readers keep working. */
  input[type='checkbox'] {
    appearance: none;
    flex: none;
    position: relative;
    width: 38px;
    height: 22px;
    margin: 0;
    border-radius: 999px;
    background: var(--track);
    cursor: pointer;
    transition: background 0.2s var(--ease);
  }

  input[type='checkbox']::after {
    content: '';
    position: absolute;
    top: 2px;
    left: 2px;
    width: 18px;
    height: 18px;
    border-radius: 50%;
    background: #fff;
    box-shadow: 0 1px 3px rgba(0, 0, 0, 0.3);
    transition: transform 0.22s var(--spring);
  }

  input[type='checkbox']:checked {
    background: var(--green);
  }

  input[type='checkbox']:checked::after {
    transform: translateX(16px);
  }

  input[type='checkbox']:focus-visible {
    outline: 2px solid var(--blue);
    outline-offset: 2px;
  }

  .foot {
    margin: 14px 4px 0;
    font-size: 11.5px;
    color: var(--fg3);
  }
</style>
