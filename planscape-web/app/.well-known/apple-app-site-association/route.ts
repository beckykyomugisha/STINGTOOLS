import { NextResponse } from 'next/server';

/**
 * iOS Universal Links verification — `/.well-known/apple-app-site-association`.
 *
 * Same shape and the same reasoning as the Android sibling in this folder: the
 * content hinges on one value we must not guess — the Apple Developer Team ID —
 * so with `IOS_TEAM_ID` unset this 404s rather than serving a file with a made-up
 * app ID in it. iOS caches the association aggressively, so a wrong file is worse
 * than a missing one.
 *
 * `Planscape/app.config.js` already declares `associatedDomains: ['applinks:<host>']`,
 * and this file has never existed at any host — so universal links have never
 * verified. They fall back to opening Safari, which looks close enough to working
 * that it went unnoticed.
 *
 * THE PATHS BELOW MUST STAY IN STEP with the Android `intentFilters` in
 * `Planscape/app.config.js`. Two lists of the same paths is how they drift; they
 * are checked against each other by `planscape-web/lib/appLinks.test.ts`.
 *
 * TO TURN IT ON
 *   1. Team ID: Apple Developer -> Membership -> Team ID (10 chars, e.g. 3PQ4X8ZK7M).
 *   2. Set IOS_TEAM_ID on the planscape-web service, then REDEPLOY.
 *   3. Verify:
 *        curl -i https://app.planscape.build/.well-known/apple-app-site-association
 *      It must come back `application/json`, with NO `.json` extension on the path
 *      and no redirect — iOS rejects both.
 */

export const dynamic = 'force-dynamic';

const BUNDLE_ID = process.env.IOS_BUNDLE_ID || 'com.planscape.app';

/**
 * The deep-link paths the app claims. `/e/*` and `/s/*` are the STING QR codes
 * (element and sheet); the rest predate them.
 *
 * Exported so the drift test can compare this list against app.config.js rather
 * than trusting that two hand-maintained lists agree.
 */
export const APP_LINK_PATHS = [
  '/accept-invitation/*',
  '/reset-password/*',
  '/issues/*',
  '/documents/*',
  '/e/*',
  '/s/*',
];

export async function GET() {
  const teamId = (process.env.IOS_TEAM_ID || '').trim();

  // A Team ID is exactly 10 alphanumerics. Anything else would produce an appID
  // that silently never matches.
  if (!/^[A-Z0-9]{10}$/i.test(teamId)) {
    return new NextResponse(
      JSON.stringify({
        error: 'aasa_not_configured',
        detail: teamId
          ? 'IOS_TEAM_ID is set but is not a 10-character Apple Team ID.'
          : 'IOS_TEAM_ID is not set on this deployment.',
        hint: 'See the comment in planscape-web/app/.well-known/apple-app-site-association/route.ts.',
      }),
      { status: 404, headers: { 'content-type': 'application/json' } },
    );
  }

  const appId = `${teamId.toUpperCase()}.${BUNDLE_ID}`;

  return NextResponse.json(
    {
      applinks: {
        // Empty by spec since iOS 13; `details` carries the rules.
        apps: [],
        details: [{ appID: appId, paths: APP_LINK_PATHS }],
      },
    },
    {
      headers: {
        // iOS requires application/json and will not follow a redirect to get it.
        'content-type': 'application/json',
        'cache-control': 'public, max-age=3600',
      },
    },
  );
}
