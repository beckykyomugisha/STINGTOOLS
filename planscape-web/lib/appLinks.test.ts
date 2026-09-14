import { readFileSync, existsSync } from 'node:fs';
import { join } from 'node:path';
import { describe, expect, it } from 'vitest';
import { APP_LINK_PATHS } from '../app/.well-known/apple-app-site-association/route';

/**
 * Deep links only work when FOUR things agree, and nothing ties them together:
 *
 *   1. the plugin stamps a URL          (StingQrFormat: /e/… and /s/…)
 *   2. this app serves a page there     (app/e/… and app/s/…)
 *   3. iOS claims the path              (apple-app-site-association: APP_LINK_PATHS)
 *   4. Android claims the path          (Planscape/app.config.js intentFilters)
 *
 * A mismatch in any one of them fails SILENTLY — the link opens a browser instead
 * of the app, which looks close enough to working that it goes unnoticed. That is
 * exactly what happened to `/issues` and `/documents`: they have declared
 * `autoVerify: true` since M2 against a `.well-known/assetlinks.json` that never
 * existed anywhere, so they have never once verified.
 *
 * These tests tie the four together.
 */
describe('app links', () => {
  const repoRoot = join(process.cwd(), '..');
  const appConfigPath = join(repoRoot, 'Planscape', 'app.config.js');

  /** Path prefixes the Android intent filters claim, read from the real config. */
  function androidPrefixes(): string[] {
    const src = readFileSync(appConfigPath, 'utf8');
    return [...src.matchAll(/pathPrefix:\s*'([^']+)'/g)].map(m => m[1]);
  }

  it('iOS and Android claim the same paths', () => {
    // APP_LINK_PATHS are glob-ish ('/issues/*'); intent filters are prefixes
    // ('/issues'). Normalise both to a bare segment before comparing.
    const norm = (p: string) => p.replace(/\/\*$/, '').replace(/\/$/, '');
    const ios = APP_LINK_PATHS.map(norm).sort();
    const android = androidPrefixes().map(norm).sort();

    expect(android).toEqual(ios);
  });

  it('claims the STING QR paths', () => {
    // The whole point of the QR work: a stamped code must open the app, not a
    // browser. If these ever drop out, every printed code silently degrades.
    const norm = (p: string) => p.replace(/\/\*$/, '').replace(/\/$/, '');
    expect(APP_LINK_PATHS.map(norm)).toContain('/e');
    expect(APP_LINK_PATHS.map(norm)).toContain('/s');
  });

  it('every claimed path has a page in this app', () => {
    // A claimed path with no route hands the user a 404 inside the app, which is
    // worse than not claiming it at all — at least a browser would have shown
    // something. Only the routes this app owns are checked; /accept-invitation
    // and /reset-password are served elsewhere.
    const owned: Record<string, string> = {
      '/e': join(process.cwd(), 'app', 'e', '[project]', '[tag]', 'page.tsx'),
      '/s': join(process.cwd(), 'app', 's', '[project]', '[sheet]', 'page.tsx'),
    };

    for (const [claimed, file] of Object.entries(owned)) {
      expect(
        APP_LINK_PATHS.some(p => p.replace(/\/\*$/, '') === claimed),
        `${claimed} has a page but is not claimed as an app link`,
      ).toBe(true);
      expect(existsSync(file), `${claimed} is claimed but ${file} does not exist`).toBe(true);
    }
  });

  it('the verification files fail closed rather than serving a guess', async () => {
    // A WRONG assetlinks.json is worse than a missing one: Android caches the
    // failed verification. So with no fingerprint configured these must 404, not
    // serve a placeholder — and an empty `statements` array is NOT an acceptable
    // stand-in, because it positively asserts "no app is associated here".
    const priorAndroid = process.env.ANDROID_CERT_SHA256;
    const priorIos = process.env.IOS_TEAM_ID;
    delete process.env.ANDROID_CERT_SHA256;
    delete process.env.IOS_TEAM_ID;
    try {
      const android = await import('../app/.well-known/assetlinks.json/route');
      const ios = await import('../app/.well-known/apple-app-site-association/route');

      const a = await android.GET();
      const i = await ios.GET();

      expect(a.status).toBe(404);
      expect(i.status).toBe(404);
    } finally {
      if (priorAndroid !== undefined) process.env.ANDROID_CERT_SHA256 = priorAndroid;
      if (priorIos !== undefined) process.env.IOS_TEAM_ID = priorIos;
    }
  });

  it('a malformed fingerprint is rejected, not passed through', async () => {
    // A fingerprint with the right shape but wrong content is a deployment
    // mistake we cannot catch. One with the WRONG SHAPE we can, and should —
    // it would otherwise produce a served file that never verifies.
    const prior = process.env.ANDROID_CERT_SHA256;
    process.env.ANDROID_CERT_SHA256 = 'not-a-fingerprint';
    try {
      const android = await import('../app/.well-known/assetlinks.json/route');
      expect((await android.GET()).status).toBe(404);
    } finally {
      if (prior === undefined) delete process.env.ANDROID_CERT_SHA256;
      else process.env.ANDROID_CERT_SHA256 = prior;
    }
  });
});
