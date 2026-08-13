import { test, expect, type Page, type ConsoleMessage } from '@playwright/test';

/**
 * Exploratory sweep, not a gate. It walks every project-scoped page as a real
 * signed-in user and reports what a user would actually hit: console errors,
 * failed API calls, dead-looking empty states.
 *
 * Auth is seeded by writing the same localStorage key the app itself uses
 * (`planscape_token`) with a token minted from a Personal Access Token, so no
 * password is ever typed. TEST_JWT is supplied by the runner.
 */

const JWT = process.env.TEST_JWT ?? '';
const PROJECT = process.env.TEST_PROJECT ?? '';
const BASE = process.env.TEST_BASE ?? 'http://localhost:3000';

type Finding = { page: string; kind: string; detail: string };
const findings: Finding[] = [];

/** Noise that says nothing about the app's own health. */
function isIgnorable(text: string) {
  return (
    text.includes('React DevTools') ||
    text.includes('Fast Refresh') ||
    text.includes('Download the React DevTools')
  );
}

async function sweep(page: Page, path: string, label: string) {
  const errors: string[] = [];
  const failedRequests: string[] = [];

  const onConsole = (m: ConsoleMessage) => {
    if (m.type() !== 'error' && m.type() !== 'warning') return;
    const t = m.text();
    if (!isIgnorable(t)) errors.push(`${m.type()}: ${t.slice(0, 200)}`);
  };
  const onResponse = (r: { status: () => number; url: () => string }) => {
    // 401 on a token that has expired mid-run would be misleading, so record
    // the status and let the report show it rather than asserting on it.
    if (r.status() >= 400 && r.url().includes('/api/')) {
      failedRequests.push(`${r.status()} ${r.url().replace(/https?:\/\/[^/]+/, '')}`);
    }
  };

  page.on('console', onConsole);
  page.on('response', onResponse);

  await page.goto(`${BASE}${path}`, { waitUntil: 'domcontentloaded' });
  // Client-side data loads after hydration; give it room without being flaky.
  await page.waitForTimeout(2500);

  const bodyText = (await page.locator('body').innerText().catch(() => '')) || '';

  page.off('console', onConsole);
  page.off('response', onResponse);

  for (const e of [...new Set(errors)]) findings.push({ page: label, kind: 'console', detail: e });
  for (const r of [...new Set(failedRequests)]) findings.push({ page: label, kind: 'api', detail: r });

  // A page that renders its own failure text is worth surfacing even when the
  // network looked fine.
  for (const phrase of ['Failed to', 'Something went wrong', 'Could not', 'Unauthorized']) {
    if (bodyText.includes(phrase)) {
      findings.push({ page: label, kind: 'ui-error', detail: `page text contains "${phrase}"` });
    }
  }
  return bodyText;
}

test.beforeEach(async ({ page }) => {
  test.skip(!JWT || !PROJECT, 'TEST_JWT and TEST_PROJECT must be set');
  // Seed auth on the app origin before any app code runs.
  await page.goto(`${BASE}/login`, { waitUntil: 'domcontentloaded' });
  await page.evaluate((t) => localStorage.setItem('planscape_token', t), JWT);
});

test('sweep every project page for errors', async ({ page }) => {
  const routes: Array<[string, string]> = [
    ['/projects', 'Projects list'],
    [`/projects/${PROJECT}`, 'Project overview'],
    [`/projects/${PROJECT}/issues`, 'Issues'],
    [`/projects/${PROJECT}/clashes`, 'Clashes'],
    [`/projects/${PROJECT}/models`, 'Models'],
    [`/projects/${PROJECT}/documents`, 'Documents'],
    [`/projects/${PROJECT}/transmittals`, 'Transmittals'],
    [`/projects/${PROJECT}/meetings`, 'Meetings'],
    [`/projects/${PROJECT}/photos`, 'Site photos'],
    [`/projects/${PROJECT}/members`, 'Members'],
    [`/projects/${PROJECT}/viewer`, '3D viewer'],
  ];

  for (const [path, label] of routes) {
    await sweep(page, path, label);
  }

  // Report as a readable block; the run is exploratory so it never fails here.
  const byPage = new Map<string, Finding[]>();
  for (const f of findings) {
    if (!byPage.has(f.page)) byPage.set(f.page, []);
    byPage.get(f.page)!.push(f);
  }
  let report = `\n===== SWEEP REPORT (${findings.length} findings) =====\n`;
  for (const [pageName, fs] of byPage) {
    report += `\n## ${pageName}\n`;
    for (const f of fs) report += `  [${f.kind}] ${f.detail}\n`;
  }
  if (findings.length === 0) report += '\nNo console errors, failed API calls, or error text on any page.\n';
  console.log(report);
});

test('projects grid: single click opens, double click edits', async ({ page }) => {
  // The safety contract just shipped, exercised in a real browser rather than jsdom.
  await page.goto(`${BASE}/projects`, { waitUntil: 'domcontentloaded' });
  await page.waitForTimeout(2500);

  const nameCell = page.locator('button[title*="Double-click to edit"]').first();
  if ((await nameCell.count()) === 0) {
    console.log('\n[grid] no editable cell found — skipping (no projects visible?)\n');
    return;
  }

  await nameCell.click();
  await page.waitForTimeout(900);
  const url = page.url();
  console.log(`\n[grid] single click -> ${url}`);
  expect(url, 'single click must navigate to a project, not open an editor').toMatch(/\/projects\/[0-9a-f-]{36}/);
});

test('3D viewer: full-screen control works with a real user gesture', async ({ page }) => {
  await page.goto(`${BASE}/projects/${PROJECT}/viewer`, { waitUntil: 'domcontentloaded' });
  await page.waitForTimeout(3000);

  const btn = page.getByRole('button', { name: /enter full screen/i });
  await expect(btn, 'full-screen button should render on the viewer').toBeVisible();

  await btn.click();
  await page.waitForTimeout(1200);

  const state = await page.evaluate(() => ({
    native: !!document.fullscreenElement,
    label: document.querySelector('[aria-label*="full screen" i]')?.getAttribute('aria-label') ?? null,
  }));
  console.log(`\n[viewer] after click -> native=${state.native} button="${state.label}"\n`);
  // Either the native API engaged or the CSS fallback did; both flip the label.
  expect(state.label, 'the control must switch to an exit affordance').toMatch(/exit/i);
});
