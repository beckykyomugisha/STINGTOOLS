import { test, type Frame, type Page } from '@playwright/test';

/**
 * Instrument for the two open viewer-meeting symptoms:
 *   1. starting a meeting makes the model reload and hang at "Loading model 0%"
 *   2. the A/V join then fails with "Couldn't join — retry"
 *
 * The earlier version of this spec tested an iframe-REMOUNT theory. That theory
 * is DISPROVEN (docs/HANDOFF_MEETING_CAMERA.md) and the spec never actually
 * drove the meeting UI, for two concrete reasons now fixed here:
 *
 *   - it looked for `button:has-text("Join")` after clicking Meet. The Meet menu
 *     entries are `<div class="menu-item" id="meetStart">`, not buttons, so
 *     nothing matched and the run stopped at "Join control not found".
 *   - "Join A/V" (`#lkJoin`) does not exist at that moment anyway. It is built by
 *     livekit-av.js, which is inert until the viewer RE-NAVIGATES itself into
 *     `?meeting=<sessionId>` (coordination-viewer.js `startMeeting`). So the flow
 *     is: #btnMeet → #meetStart → wait for the frame to navigate → #lkJoin.
 *
 * This spec asserts nothing. It reports what the live system does, because every
 * real bug in this area was found by measuring and two confident theories were
 * wrong. The single most valuable line it prints is the lobby pill AFTER the
 * join attempt: setLobby('error', detail) renders "Couldn't join — <cause>", and
 * that cause is the thing we have never yet seen.
 */

const JWT = process.env.TEST_JWT ?? '';
const PROJECT = process.env.TEST_PROJECT ?? '';
const BASE = process.env.TEST_BASE ?? 'http://localhost:3000';

const FRAME = 'iframe[title="3D model"]';

async function seedAuth(page: Page) {
  await page.goto(`${BASE}/login`, { waitUntil: 'domcontentloaded' });
  await page.evaluate((t) => localStorage.setItem('planscape_token', t), JWT);
}

/** The viewer document currently inside the iframe (it re-navigates mid-test). */
function viewerFrame(page: Page): Frame | null {
  return page.frames().find((f) => f !== page.mainFrame() && /viewer\.html/.test(f.url())) ?? null;
}

/** Text of the A/V lobby pill + its title attr, which carries the full cause. */
async function pill(page: Page): Promise<string> {
  const f = viewerFrame(page);
  if (!f) return '(no viewer frame)';
  return f
    .evaluate(() => {
      const el = document.getElementById('lkPillTxt');
      if (!el) return '(no pill — livekit-av.js inert or still booting)';
      const title = el.getAttribute('title');
      return el.textContent + (title && title !== el.textContent ? `   [title: ${title}]` : '');
    })
    .catch(() => '(frame detached)');
}

test('starting a meeting: drive Meet → Start → Join A/V and report what fails', async ({ page }) => {
  test.skip(!JWT || !PROJECT, 'TEST_JWT and TEST_PROJECT must be set');

  const navs: string[] = [];
  const logs: string[] = [];
  const api: string[] = [];
  const stamp = () => new Date().toISOString().slice(11, 23);

  page.on('framenavigated', (f) => {
    if (f === page.mainFrame()) return;
    navs.push(`${stamp()} ${f.url().slice(0, 140)}`);
  });
  // WARN matters as much as ERROR here: livekit-av.js reports the real causes
  // through console.warn ("[livekit] token 403", "connect failed", …), so the
  // previous error-only filter discarded exactly the diagnostics we need.
  page.on('console', (m) => {
    if (['error', 'warning'].includes(m.type())) logs.push(`${stamp()} [${m.type()}] ${m.text().slice(0, 240)}`);
  });
  page.on('pageerror', (e) => logs.push(`${stamp()} [pageerror] ${String(e.message).slice(0, 240)}`));
  page.on('requestfailed', (r) => {
    if (/livekit|meeting|hubs|models/i.test(r.url())) {
      api.push(`${stamp()} FAILED ${r.failure()?.errorText ?? '?'}  ${r.url().slice(0, 120)}`);
    }
  });
  page.on('response', (r) => {
    if (/meeting-sessions|livekit-token|\/models\/|hubs\/meeting/i.test(r.url())) {
      api.push(`${stamp()} ${r.status()} ${r.request().method()} ${r.url().slice(0, 120)}`);
    }
  });

  await seedAuth(page);
  await page.goto(`${BASE}/projects/${PROJECT}/viewer`, { waitUntil: 'domcontentloaded' });
  await page.waitForTimeout(8000);
  console.log(`\n[before] viewer frame: ${viewerFrame(page)?.url().slice(0, 140) ?? '(none)'}`);

  // 1 ── open the Meet menu
  const frame = page.frameLocator(FRAME);
  const meet = frame.locator('#btnMeet');
  if ((await meet.count()) === 0) {
    console.log('[meet] #btnMeet not present — viewer toolbar never rendered; stopping.');
    return;
  }
  await meet.click({ timeout: 10_000 });
  await page.waitForTimeout(600);

  // 2 ── "Start a live meeting" (a div.menu-item, NOT a button)
  const start = frame.locator('#meetStart');
  if ((await start.count()) === 0) {
    console.log('[meet] #meetStart not found after opening the menu; stopping.');
    return;
  }
  await start.click({ timeout: 10_000 });
  console.log('[meet] clicked "Start a live meeting" — the viewer now re-navigates itself');

  // 3 ── the viewer rewrites its own location to ?meeting=<id> (700ms delay in
  //      startMeeting, plus a full document load). Wait for the new document.
  await page
    .waitForFunction(
      () => {
        const f = document.querySelector('iframe[title="3D model"]') as HTMLIFrameElement | null;
        return !!f && /[?&]meeting=/.test(f.contentWindow?.location?.href ?? '');
      },
      undefined,
      { timeout: 20_000 },
    )
    .catch(() => console.log('[meet] the frame never re-navigated into ?meeting= within 20s'));
  await page.waitForTimeout(6000); // livekit-av.js boot + fetchToken

  const inMeeting = viewerFrame(page)?.url() ?? '';
  console.log(`[after start] viewer frame: ${inMeeting.slice(0, 160)}`);
  console.log(`[after start] lobby pill: ${await pill(page)}`);

  // 4 ── Join A/V (#lkJoin, built by livekit-av.js once a token was fetched)
  const join = frame.locator('#lkJoin');
  if ((await join.count()) === 0) {
    console.log('[join] #lkJoin absent — livekit-av.js never built the bar (no ?meeting=, or the lib failed to load)');
  } else {
    const disabled = await join.isDisabled().catch(() => false);
    console.log(`[join] #lkJoin present, disabled=${disabled}  (disabled means lobby state "unavailable" — the token fetch returned nothing)`);
    if (!disabled) {
      await join.click({ timeout: 10_000 }).catch((e) => console.log(`[join] click failed: ${e.message}`));
      await page.waitForTimeout(12_000); // connect + device publish
      console.log(`\n>>> [join] lobby pill AFTER the attempt: ${await pill(page)}`);
    }
  }

  // 5 ── symptom 1: is the model overlay still spinning?
  const body = await page.frameLocator(FRAME).locator('body').innerText().catch(() => '');
  const stuck = /Loading model/i.test(body);
  const pct = /Loading model[^\n]*/i.exec(body)?.[0] ?? '(overlay gone)';
  console.log(`\n[model] still showing "Loading model": ${stuck}   → ${pct.slice(0, 80)}`);

  console.log(`\n[frame navigations] ${navs.length}`);
  navs.forEach((n) => console.log(`   ${n}`));
  console.log(`\n[meeting/model API calls] ${api.length}`);
  api.forEach((a) => console.log(`   ${a}`));
  console.log(`\n[console warn/error] ${logs.length}`);
  [...new Set(logs)].slice(0, 25).forEach((l) => console.log(`   ${l}`));
  console.log('');
});
