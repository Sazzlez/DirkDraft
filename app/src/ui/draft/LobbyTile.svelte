<script lang="ts">
  import type { Tone } from '../../core/view';

  let { title, lines, text }: { title: string; lines: { label: string; value: string; tone: Tone }[]; text: string | null } = $props();
</script>

<div class="lobby">
  <span class="logo" aria-hidden="true"></span>
  <h2>{title}</h2>
  {#if text}<p>{text}</p>{/if}
  <ul>
    {#each lines as line (line.label)}
      <li>
        <i class="dot {line.tone}"></i>
        <span class="label">{line.label}</span>
        <span class="value">{line.value}</span>
      </li>
    {/each}
  </ul>
</div>

<style>
  .lobby {
    display: flex;
    flex-direction: column;
    align-items: center;
    gap: 8px;
    padding: 48px 16px 24px;
    text-align: center;
  }

  .logo {
    width: 52px;
    height: 52px;
    border-radius: 14px;
    background: linear-gradient(135deg, #0ac8b9, var(--blue));
  }

  h2 {
    margin: 8px 0 0;
    font-size: 22px;
    font-weight: 700;
    letter-spacing: -0.025em;
  }

  p {
    margin: 0;
    color: var(--fg2);
  }

  ul {
    list-style: none;
    margin: 18px 0 0;
    padding: 6px 14px;
    width: min(420px, 100%);
    border-radius: var(--r-l);
    background: var(--card);
    text-align: left;
  }

  li {
    display: grid;
    grid-template-columns: 10px auto minmax(0, 1fr);
    gap: 10px;
    align-items: center;
    padding: 9px 0;
    font-size: 12.5px;
  }

  li + li {
    border-top: 1px solid var(--separator);
  }

  .dot {
    width: 7px;
    height: 7px;
    border-radius: 50%;
    background: var(--fg3);
  }

  .dot.good {
    background: var(--green);
  }

  .dot.warn {
    background: var(--orange);
  }

  .label {
    color: var(--fg2);
  }

  .value {
    text-align: right;
  }
</style>
