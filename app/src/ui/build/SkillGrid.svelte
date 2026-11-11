<script lang="ts">
  // In the game: `taken` levels are spent, `next` is the plan's next point (core/game).
  let {
    skills,
    taken = 0,
    next = null,
  }: { skills: { level: number; key: 'Q' | 'W' | 'E' | 'R'; derived: boolean }[]; taken?: number; next?: number | null } = $props();
</script>

{#if skills.length === 0}
  <p class="none">Keine Skill-Reihenfolge in den Daten.</p>
{:else}
  <div class="sk" role="list" aria-label="Skill-Reihenfolge">
    {#each skills as s (s.level)}
      <div
        role="listitem"
        class:done={s.level <= taken}
        class:next={next !== null && s.level === next}
        class:r={s.key === 'R'}
        class:derived={s.derived}
        title={s.derived ? 'abgeleitet, nicht in den Daten' : ''}
      >
        <b>{s.key}</b>
        <span class="num">{s.level}</span>
      </div>
    {/each}
  </div>
{/if}

<style>
  .sk {
    display: grid;
    grid-template-columns: repeat(18, minmax(0, 1fr));
    gap: 4px;
  }

  .sk > div {
    display: flex;
    flex-direction: column;
    align-items: center;
    gap: 3px;
    font-size: 9.5px;
    color: var(--fg3);
  }

  b {
    width: 100%;
    height: 24px;
    border-radius: 6px;
    display: grid;
    place-items: center;
    font-size: 11.5px;
    font-weight: 650;
    background: var(--card-strong);
    color: var(--fg);
  }

  .r b {
    color: var(--teal-text);
  }

  .derived b {
    opacity: 0.45;
  }

  .done b {
    background: var(--track);
    color: var(--fg3);
  }

  .next b {
    background: var(--blue);
    color: #fff;
    box-shadow: 0 0 12px rgba(10, 132, 255, 0.5);
  }

  .next {
    color: var(--teal-text);
    font-weight: 600;
  }

  .none {
    margin: 0;
    font-size: 12px;
    color: var(--fg3);
  }

  @container app (max-width: 719px) {
    .sk {
      grid-template-columns: repeat(9, minmax(0, 1fr));
    }
  }
</style>
