import { NextResponse } from 'next/server';

/**
 * Android App Links verification — `/.well-known/assetlinks.json`.
 *
 * WHY THIS IS A ROUTE AND NOT A STATIC FILE
 * -----------------------------------------
 * The file's whole content is one secret-ish value: the SHA-256 fingerprint of
 * the signing certificate of the release APK. There is no correct placeholder for
 * it, and a WRONG one is worse than a missing file: Android caches a failed
 * verification, so shipping a guessed fingerprint can leave app links broken on
 * devices long after the real value is set.
 *
 * So this fails CLOSED. With `ANDROID_CERT_SHA256` unset it 404s — exactly what
 * the host does today — and nothing regresses. Set the env var and the file
 * appears, with no code change and nothing fabricated in between.
 *
 * WHAT WAS ALREADY BROKEN
 * -----------------------
 * `Planscape/app.config.js` has declared `autoVerify: true` intent filters for
 * `/accept-invitation`, `/reset-password`, `/issues` and `/documents` since M2,
 * and this file has never existed at any host. So NONE of those links have ever
 * verified — they have been opening in a browser, silently, which looks close
 * enough to working that nobody caught it. The QR work needs `/e` and `/s` to
 * verify, so the missing half is built here for all of them.
 *
 * TO TURN IT ON
 *   1. Get the fingerprint of the signing cert:
 *        keytool -list -v -keystore <release.keystore> -alias <alias>
 *      or, for Play App Signing, copy it from
 *        Play Console -> Release -> Setup -> App signing -> SHA-256
 *   2. Set ANDROID_CERT_SHA256 on the planscape-web service (colon-separated hex,
 *      e.g. "14:6D:E9:83:C5:73:06:50:D8:EE:B9:95:2F:34:FC:64:16:A0:83:42:E6:1D:BE:A8:8A:04:96:B2:3F:CF:44:E5").
 *      Multiple certs (upload + Play-signed) are allowed: separate with commas.
 *   3. Redeploy. Bindings are captured at DEPLOY time on some hosts — setting the
 *      var without redeploying can leave the old behaviour in place.
 *   4. Verify with:
 *        curl https://app.planscape.build/.well-known/assetlinks.json
 *      and Google's tester:
 *        https://developers.google.com/digital-asset-links/tools/generator
 */

// Must not be statically pre-rendered: the env var is read at request time.
export const dynamic = 'force-dynamic';

const PACKAGE_NAME = process.env.ANDROID_PACKAGE_NAME || 'com.planscape.app';

/** Accepts "AA:BB:..." or several, comma-separated. Rejects anything that is not
 *  32 colon-separated hex octets — a malformed fingerprint verifies as badly as a
 *  wrong one, and it should be caught here rather than by a silent failure on a
 *  user's phone. */
function parseFingerprints(raw: string | undefined): string[] {
  if (!raw) return [];
  return raw
    .split(',')
    .map(s => s.trim().toUpperCase())
    .filter(s => /^([0-9A-F]{2}:){31}[0-9A-F]{2}$/.test(s));
}

export async function GET() {
  const raw = process.env.ANDROID_CERT_SHA256;
  const fingerprints = parseFingerprints(raw);

  if (fingerprints.length === 0) {
    // 404, not an empty-but-valid file. An empty statements array is a VALID
    // assetlinks.json that positively asserts "no app is associated with this
    // domain" — which is a worse answer than no file at all.
    return new NextResponse(
      JSON.stringify({
        error: 'assetlinks_not_configured',
        detail: raw
          ? 'ANDROID_CERT_SHA256 is set but no value in it is 32 colon-separated hex octets.'
          : 'ANDROID_CERT_SHA256 is not set on this deployment.',
        hint: 'See the comment in planscape-web/app/.well-known/assetlinks.json/route.ts.',
      }),
      { status: 404, headers: { 'content-type': 'application/json' } },
    );
  }

  return NextResponse.json(
    [
      {
        relation: ['delegate_permission/common.handle_all_urls'],
        target: {
          namespace: 'android_app',
          package_name: PACKAGE_NAME,
          sha256_cert_fingerprints: fingerprints,
        },
      },
    ],
    {
      headers: {
        'content-type': 'application/json',
        // Android re-fetches on install and periodically. An hour is short enough
        // that a fingerprint correction propagates the same day.
        'cache-control': 'public, max-age=3600',
      },
    },
  );
}
