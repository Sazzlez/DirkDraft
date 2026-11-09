<script lang="ts">
  import type { GapLine } from '../../core/view';

  // "Eurem Team fehlt noch: Frontline, AP-Schaden und Engage" — each name stays on one line, so
  // "AD-Schaden" never breaks at its hyphen.
  let { line }: { line: GapLine } = $props();
</script>

{line.lead}{#if line.items.length > 0}:{' '}{#each line.items as item, i (item)}{#if i > 0}{i === line.items.length - 1 ? ' und ' : ', '}{/if}<span class="nw">{item}</span>{/each}{/if}

<style>
  .nw {
    white-space: nowrap;
  }
</style>
