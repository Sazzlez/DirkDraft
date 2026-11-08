import { test } from '@playwright/test';

// One image per scene and area. Written to screenshots/ (not committed), named
// <scene>-<area>-<width>-<theme>.png so a folder listing reads like a contact sheet.
const shots: { scene: string; area?: 'game'; settings?: boolean }[] = [
  { scene: 'lobby' },
  { scene: 'planning' },
  { scene: 'banning' },
  { scene: 'picking' },
  { scene: 'teammate' },
  { scene: 'mate' },
  { scene: 'locked' },
  { scene: 'loading' },
  { scene: 'aram' },
  { scene: 'game' },
  { scene: 'nodata' },
  { scene: 'gameloading' },
  { scene: 'lobby', settings: true },
];

for (const shot of shots) {
  const label = `${shot.scene}${shot.area ? `-${shot.area}` : ''}${shot.settings ? '-settings' : ''}`;
  test(label, async ({ page }, info) => {
    const { width, theme } = info.project.metadata as { width: number; theme: string };
    await page.goto(`/?scene=${shot.scene}&theme=${theme}&shot=1`);
    await page.waitForSelector(`body[data-ready="${shot.scene}"]`);

    if (shot.area) {
      await page.getByRole('tab', { name: 'Spiel' }).first().click();
    }
    if (shot.settings) {
      // Narrow: the gear lives in the menu.
      if (width < 720) {
        await page.getByRole('button', { name: 'Menü' }).click();
        await page.getByRole('button', { name: 'Einstellungen', exact: true }).last().click();
      } else {
        await page.getByRole('button', { name: 'Einstellungen' }).click();
      }
      await page.waitForSelector('[role="dialog"]');
    }

    // Portraits and item pictures come from Data Dragon; wait until they are in.
    await page.waitForLoadState('networkidle');
    // Lazy pictures below the fold never load; nobody waits for those longer than three seconds.
    await page.evaluate(async () => {
      await Promise.all(
        [...document.images].map((img) =>
          img.complete
            ? null
            : Promise.race([
                new Promise((resolve) => (img.onload = img.onerror = resolve)),
                new Promise((resolve) => setTimeout(resolve, 3000)),
              ]),
        ),
      );
    });
    await page.waitForTimeout(350);

    const name = `${label}-${width}-${theme}`;
    await page.screenshot({ path: `screenshots/${name}.png`, fullPage: false });
  });
}
