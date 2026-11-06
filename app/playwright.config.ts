import { defineConfig } from '@playwright/test';

// Screenshots of every moment in the three widths of the brief, light and dark. They are how a
// layout change gets judged before anyone opens the app — the WPF version's --screenshot, in the
// browser with replay data.
const widths = [380, 880, 1240];
const themes = ['dark', 'light'] as const;

export default defineConfig({
  testDir: './tests/shots',
  outputDir: './test-results',
  fullyParallel: true,
  reporter: 'list',
  use: {
    baseURL: 'http://localhost:5173',
    deviceScaleFactor: 1,
  },
  projects: widths.flatMap((width) =>
    themes.map((theme) => ({
      name: `${width}-${theme}`,
      use: { viewport: { width, height: width < 720 ? 860 : 900 }, colorScheme: theme },
      metadata: { width, theme },
    })),
  ),
  webServer: {
    command: 'node node_modules/vite/bin/vite.js',
    port: 5173,
    reuseExistingServer: true,
  },
});
