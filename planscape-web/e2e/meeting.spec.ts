import { test, type Page } from '@playwright/test';

/**
 * Reproduces two reported symptoms that look like one cause:
 *   1. starting a meeting makes the model reload and hang at "Loading model 0%"
 *   2. the A/V join then fails with "Couldn't join — retry"
 *
 * The suspicion under test is that the viewer iframe is being REMOUNTED. Its
 * src carries the auth token, so anything that changes the token (or merely the
 * identity of the auth object the effect depends on) produces a new src, which
 * reloads the frame — restarting the model download from 0% and tearing down an
 * in-flight LiveKit connection. If that is what happens, both symptoms have a
 * single fix and neither is a meeting bug.
 */

const JWT = process.env.TEST_JWT ?? '';
const PROJECT = process.env.TEST_PROJECT ?? '';
const BASE = process.env.TEST_BASE ?? 'http://localhost:3000';

async function seedAuth(page: Page) {
  await page.goto(`${BASE}/login`, { waitUntil: 'domcontentloaded' });
  await page.evaluate((t) => localStorage.setItem('planscape_token', t), JWT);
}

test('starting a meeting: does the viewer iframe remount?', async ({ page }) => {
  test.skip(!JWT || !PROJECT, 'TEST_JWT and TEST_PROJECT must be set');

  const frameLoads: string[] = [];
  const errors: string[] = [];

  // A frame navigation IS the remount we are looking for.
  page.on('framenavigated', (f) => {
    if (f === page.mainFrame()) return;
    frameLoads.push(`${new Date().toISOString().slice(11, 23)} ${f.url().slice(0, 120)}`);
  });
  page.on('console', (m) => {
    if (m.type() === 'error') errors.push(m.text().slice(0, 220));
  });

  await seedAuth(page);
  await page.goto(`${BASE}/projects/${PROJECT}/viewer`, { waitUntil: 'domcontentloaded' });
  await page.waitForTimeout(6000);

  const loadsBefore = frameLoads.length;
  console.log(`\n[baseline] iframe navigations while just sitting on the page: ${loadsBefore}`);
  frameLoads.forEach((l) => console.log(`   ${l}`));

  // Track src changes directly too — React swapping the attribute is the
  // mechanism, whether or not a navigation event is emitted for it.
  const srcSamples: string[] = [];
  const sample = async () => {
    const src = await page.locator('iframe[title="3D model"]').getAttribute('src').catch(() => null);
    if (src && srcSamples[srcSamples.length - 1] !== src) srcSamples.push(src);
  };
  await sample();

  // Open the Meet menu inside the viewer iframe and start/join.
  const frame = page.frameLocator('iframe[title="3D model"]');
  const meet = frame.locator('#btnMeet, [id*="Meet" i], button:has-text("Meet")').first();
  if ((await meet.count()) === 0) {
    console.log('\n[meet] no Meet control found in the iframe — cannot drive the meeting from here\n');
  } else {
    await meet.click({ timeout: 10_000 }).catch((e) => console.log(`[meet] click failed: ${e.message}`));
    await page.waitForTimeout(1500);
    await sample();

    const join = frame.locator('button:has-text("Join A/V"), button:has-text("Join")').first();
    if ((await join.count()) > 0) {
      await join.click({ timeout: 10_000 }).catch((e) => console.log(`[join] click failed: ${e.message}`));
      console.log('[meet] clicked Join A/V');
    } else {
      console.log('[meet] Join control not found after opening the menu');
    }
  }

  await page.waitForTimeout(8000);
  await sample();

  console.log(`\n[after meet] total iframe navigations: ${frameLoads.length} (was ${loadsBefore} before clicking)`);
  frameLoads.slice(loadsBefore).forEach((l) => console.log(`   ${l}`));

  console.log(`\n[iframe src] distinct values seen: ${srcSamples.length}`);
  srcSamples.forEach((s, i) => {
    // The token is the part most likely to differ between samples.
    const tok = /[?&]token=([^&]+)/.exec(s)?.[1] ?? '(none)';
    console.log(`   #${i + 1} token=${tok.slice(0, 24)}… len=${s.length}`);
  });
  if (srcSamples.length > 1) {
    console.log('\n   >>> iframe src CHANGED — that remounts the viewer and restarts the model load.');
  }

  const modelText = await page
    .frameLocator('iframe[title="3D model"]')
    .locator('body')
    .innerText()
    .catch(() => '');
  const stuck = /Loading model/i.test(modelText);
  console.log(`\n[model] still showing "Loading model": ${stuck}`);

  console.log(`\n[console errors] ${errors.length}`);
  [...new Set(errors)].slice(0, 12).forEach((e) => console.log(`   ${e}`));
  console.log('');
});
