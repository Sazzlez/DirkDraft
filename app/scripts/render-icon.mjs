// Renders assets/icon.svg to a 1024 px PNG with Playwright's Chromium, the master that
// `tauri icon` turns into every size Windows, macOS and the installer need.
//   node scripts/render-icon.mjs   →   assets/icon-1024.png
import { chromium } from '@playwright/test';
import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';

const svg = readFileSync(fileURLToPath(new URL('../assets/icon.svg', import.meta.url)), 'utf8');
const out = fileURLToPath(new URL('../assets/icon-1024.png', import.meta.url));

const browser = await chromium.launch();
const page = await browser.newPage({ viewport: { width: 1024, height: 1024 }, deviceScaleFactor: 1 });
await page.setContent(`<html><body style="margin:0;background:transparent">${svg.replace('<svg ', '<svg width="1024" height="1024" ')}</body></html>`);
await page.screenshot({ path: out, omitBackground: true, clip: { x: 0, y: 0, width: 1024, height: 1024 } });
await browser.close();
console.log(out);
