<script lang="ts">
  let { title, text }: { title: string; text: string } = $props();
</script>

<div class="empty">
  <span class="logo" aria-hidden="true"></span>
  <h2>{title}</h2>
  <p>{text}</p>
</div>

<style>
  .empty {
    display: flex;
    flex-direction: column;
    align-items: center;
    justify-content: center;
    gap: 8px;
    min-height: 320px;
    padding: 32px 24px;
    text-align: center;
  }

  .logo {
    width: 44px;
    height: 44px;
    border-radius: 12px;
    background: linear-gradient(135deg, #0ac8b9, var(--blue));
    opacity: 0.9;
  }

  h2 {
    margin: 6px 0 0;
    font-size: 20px;
    font-weight: 700;
    letter-spacing: -0.02em;
  }

  p {
    margin: 0;
    max-width: 30em;
    color: var(--fg2);
    font-size: 13px;
  }
</style>
