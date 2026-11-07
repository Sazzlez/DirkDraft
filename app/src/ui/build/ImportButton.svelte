<script lang="ts">
  // "Runen und Zauber übernehmen": the one write to the client, on this click only (live/bridge).
  // When the account's page limit would cost a foreign rune page, the question is asked right here,
  // by the page's name, and nothing is written before the answer.
  import type { BuildView } from '../../core/view';
  import { importLabel } from '../../core/loadout';
  import { runImport } from '../../live/bridge';

  let { build }: { build: BuildView } = $props();

  const label = $derived(importLabel(!!build.runes, build.spells.length >= 2));

  let busy = $state(false);
  let result = $state<string | null>(null);
  /** The foreign page that would have to go, while the question is open. */
  let asking = $state<{ name: string; answer: (yes: boolean) => void } | null>(null);

  function confirm(name: string): Promise<boolean> {
    return new Promise((resolve) => {
      asking = {
        name,
        answer: (yes) => {
          asking = null;
          resolve(yes);
        },
      };
    });
  }

  async function take() {
    busy = true;
    result = null;
    try {
      result = await runImport(confirm);
    } catch (e) {
      result = `Fehlgeschlagen: ${String(e)}`;
    } finally {
      busy = false;
      asking = null;
    }
  }
</script>

{#if asking}
  {@const question = asking}
  <div class="ask" role="alertdialog" aria-labelledby="ask-title">
    <b id="ask-title">Alle Runenseiten-Plätze sind belegt</b>
    <span>Für die neue Seite müsste deine Seite „{question.name}“ gelöscht werden.</span>
    <div class="row">
      <button class="ghost" onclick={() => question.answer(false)}>Abbrechen</button>
      <button class="danger" onclick={() => question.answer(true)}>Löschen und übernehmen</button>
    </div>
  </div>
{:else}
  <button class="primary" onclick={take} disabled={busy}>
    <svg viewBox="0 0 16 16"><path d="M8 2v8m0 0 3-3m-3 3L5 7M3 13h10" /></svg>
    {busy ? 'Wird übernommen …' : label}
  </button>
{/if}
{#if result}<p class="result" role="status">{result}</p>{/if}

<style>
  .primary {
    display: flex;
    align-items: center;
    justify-content: center;
    gap: 6px;
    width: 100%;
    height: 32px;
    margin-top: 12px;
    border-radius: 9px;
    background: var(--blue);
    color: #fff;
    font-weight: 600;
  }

  .primary:disabled {
    opacity: 0.6;
    cursor: default;
  }

  .primary svg {
    width: 13px;
    height: 13px;
    fill: none;
    stroke: currentColor;
    stroke-width: 1.6;
  }

  .ask {
    display: flex;
    flex-direction: column;
    gap: 4px;
    margin-top: 12px;
    padding: 10px 12px;
    border-radius: 10px;
    background: var(--orange-wash);
    font-size: 12px;
    color: var(--fg2);
  }

  .ask b {
    color: var(--fg);
    font-size: 13px;
  }

  .row {
    display: flex;
    justify-content: flex-end;
    gap: 8px;
    margin-top: 6px;
  }

  .row button {
    height: 28px;
    padding: 0 12px;
    border-radius: 8px;
    font-weight: 600;
  }

  .ghost {
    background: var(--card-strong);
  }

  .danger {
    background: var(--red);
    color: #fff;
  }

  .result {
    margin: 8px 2px 0;
    font-size: 11.5px;
    color: var(--fg2);
  }
</style>
