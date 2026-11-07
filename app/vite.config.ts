import { defineConfig } from 'vitest/config';
import { svelte } from '@sveltejs/vite-plugin-svelte';

// One config for three uses: the dev server (browser with replay data), the Tauri shell later
// (fixed port, no clearing of the terminal the Rust build writes to), and Vitest.
export default defineConfig({
  plugins: [svelte()],
  clearScreen: false,
  server: {
    port: 5173,
    strictPort: true,
    fs: {
      // The curated data files (traits, seat priors) live in the repository's data folder.
      allow: ['..'],
    },
  },
  build: {
    target: 'es2022',
  },
  test: {
    include: ['src/**/*.test.ts', 'tests/**/*.test.ts'],
    environment: 'node',
  },
});
