# Handoff — two open bugs in the web 3D viewer's live meeting

Written 2026-08-01. Paste this into a fresh session to continue.

Read "Already ruled out" before forming a theory — three plausible explanations
have been tested and DISPROVEN, and repeating them wastes a lot of time.

## The symptoms (reproducible for the product owner)

1. **Starting a live meeting makes the 3D model reload and never finish** — it
   sits at "Loading model 0%" indefinitely. The model had rendered fine before.
2. **"Join A/V" fails** with `Couldn't join — retry`; no camera/video ever
   appears. The meeting panel shows `1 online · 0 in call`.

Their screenshot also shows an `Offline` pill in the viewer toolbar during this,
which may or may not be related.

## Environment

- Repo `beckykyomugisha/STINGTOOLS`; work in git worktrees under
  `C:\Dev\STINGTOOLS\.claude\worktrees\`. Branch from `main`.
- Live API: `https://planscape-api-free.onrender.com` — Render FREE tier, spins
  down when idle, so the first request after a pause can take ~50s. Not a hang.
- Live web: `https://planscape-web-free.onrender.com`
- Render auto-deploys `main`; both services redeploy on merge.
- For live API calls, ask the user for a Personal Access Token, then
  `POST /api/auth/token/exchange` with `{"token":"psat_..."}` → `accessToken`.
  JWTs last 30 minutes.
- Test project: `fd1b8ac7-0351-493b-820b-e0ef26b1c7ef`. NOTE the user's own
  affected project is in a DIFFERENT tenant and is not reachable with that
  token (correct tenant isolation — don't mistake the 404 for a bug).

## Proven working — do not re-investigate

- **LiveKit is fully configured server-side.** `POST /api/projects/{id}/meeting-sessions`
  then `POST .../{sessionId}/livekit-token` → HTTP 200 with a COMPLETE credential
  set: `token`, `url` (`wss://planscape-u3ne7qsq.livekit.cloud`), `identity`,
  `room`, `isPresenter`. The server is not the problem.
- R2 storage works (document upload + model publish both 201).
- Invite email works (`emailSent: true`, via Resend).

## Already ruled out — do NOT repeat

1. **The viewer iframe does NOT remount when a meeting starts.** The theory was
   that the iframe `src` carries the auth token, so a refresh would swap the src
   and restart the model load. Playwright measured it: the src had exactly ONE
   distinct value and there were ZERO frame navigations after clicking Meet.
2. **`RangeError: Offset is outside the bounds of the DataView` in
   `GLTFBinaryExtension` is a red herring** — caused by a 16-byte fake `.glb`
   probe file uploaded during earlier storage testing, since deleted.
3. **CORS is not broken.** A separate bug here presented as a CORS error purely
   because an ASP.NET 500 loses its CORS headers. If you see "blocked ... from
   origin", confirm the real status with curl + an explicit `Origin` header
   before believing the browser.

## Session 2 (2026-08-05) — four more theories killed, one new lead

No live reproduction yet (no token this session). All of the below is either a
measurement against the live service or a fact read off the code that is now
PROVEN to be the code the live service runs.

**The bundle you read IS the bundle that runs.** `curl https://planscape-api-free
.onrender.com/livekit-av.js` is byte-identical to `Planscape/assets/viewer/
livekit-av.js` apart from CRLF↔LF, and the served marker is
`STING_MEETING_BUILD = "s2-latejoin"`. Source ↔ wwwroot are also identical. So
static reading of this file is trustworthy — that was NOT a given and is worth
re-checking with the same one-liner whenever the served behaviour surprises you.

Killed this session — do NOT re-investigate:

4. **LiveKit token staleness is NOT the cause.** Participant tokens are minted
   with `ttl: TimeSpan.FromHours(4)` (`MeetingRoomController.cs:373`). The
   10-minute `exp` in `LiveKitRoomService.cs:238` is the room-ADMIN JWT for
   server-to-server Twirp calls, not the browser's token. Don't confuse them.
5. **CSP / `frame-ancestors` is NOT blocking the embed, and
   `Permissions-Policy: camera=()` is NOT blocking the camera.**
   `SecurityHeadersMiddleware` does set both — but it runs AFTER static-file
   handling, so `/viewer.html` and the viewer JS come back with NO security
   headers at all (verified with `curl -D -`). Those headers appear only on
   `/api/*` responses, where a Permissions-Policy on a JSON response governs
   nothing. Confirmed live, not inferred.
6. **The "Couldn't join — retry" text means the detail was EMPTY.**
   `setLobby('error', detail)` renders `"Couldn't join — " + (detail || "retry")`
   and the deployed bundle already extracts the real cause from the LiveKit
   error (`joinFailReason`). So either the owner's screenshot predates that
   deploy, or the rejection carried no `.message`. A FRESH reproduction is
   therefore strictly more informative than the old screenshot — get one before
   theorising.
7. **The 403 cannot have come from the `livekit-token` endpoint.** That action
   returns 404 / 400 / 401 / 501 and has no 403 path at all. The handoff's
   original framing ("the 403 is the direct cause of Couldn't join") is wrong on
   that specific point.

**New lead — the most likely 403 source is a tenant-header mismatch.**
`TenantResolutionMiddleware` (line ~87) short-circuits with **403** whenever an
authenticated request carries an `X-Tenant` header (or subdomain) that disagrees
with the JWT's tenant. The viewer sends exactly that header:
`loadModelGlb()` sets `headers['X-Tenant'] = localStorage.getItem('planscape_tenant')`,
and that key lives in the **API origin's** localStorage, written by viewer.html's
bootstrap from the `?tenant=` param. Two things make it easy to get stale:
  - the bootstrap STRIPS `token`/`tenant`/`user` from the address bar, so when
    `startMeeting()` re-navigates via `location.href`, the new document arrives
    with NO tenant param and simply keeps whatever was already stored;
  - that store survives sign-outs and tenant switches on the app origin.
Note `fetchToken()` in livekit-av.js sends NO `X-Tenant`, so this would 403 the
model/API calls (symptom 1 territory) while leaving the A/V token fetch alone —
which fits "the model never loads" better than it fits "join fails". Verify
before believing it: compare `planscape_tenant` in the API origin's localStorage
against the `tenant` claim in the JWT during a live repro.

## The live lead

Playwright saw a **403** from inside the viewer iframe on the 3D viewer page.
That is the most likely direct cause of `Couldn't join` — and therefore of the
missing camera, since media is never published if the join never completes.

**Untested hypothesis, check this first:** the iframe gets a SNAPSHOT of the JWT
at page-load (the parent passes it as a URL param; `viewer.html`'s bootstrap
writes it into that origin's localStorage). JWTs expire after 30 minutes. The
parent app refreshes its token silently; the iframe's frozen copy never does. So
a Join clicked >30 min after page load would 403.

Cheap test: hard-reload the viewer, click Join A/V within one minute. If it
connects, that confirms it, and the fix is to refresh the iframe's token rather
than freeze it at load.

## Most useful next step

**Read the Render logs.** They cracked the previous bug in this area instantly,
after two wrong theories. Render dashboard → `planscape-api-free` → **Logs**,
filtered to the time of a Join click; find the exception behind the 403. The
user has dashboard access, the agent does not — ask them to paste the trace.

## Where the code lives

- Parent page: `planscape-web/app/projects/[id]/viewer/page.tsx` — builds the
  iframe URL (`project`, `token`, `tenant`, `user` params), drives the viewer by
  `postMessage`, owns the fullscreen toggle.
- Viewer bundle (inside the iframe), served by the API:
  - `Planscape/assets/viewer/` ← **CANONICAL SOURCE**
  - `Planscape.Server/src/Planscape.API/wwwroot/` ← synced copy
  - **A CI gate ("Source ↔ wwwroot byte-equal") fails if you edit only one.**
    Edit the canonical source; the `SyncCoordinationViewer` MSBuild target copies
    it to wwwroot on build. Editing wwwroot directly is a known trap — it has
    already caught one commit in this workstream.
  - Key files: `viewer.html`, `coordination-viewer.js`, `livekit-av.js` (the A/V
    join), `meeting-sync.js`.
- Server: `Planscape.Server/src/Planscape.API/Controllers/MeetingRoomController.cs`
  (`meeting-sessions`, `livekit-token`), `Planscape.Infrastructure/SignalR/LiveKitRoomService.cs`.

## Tooling already in place

- **Playwright** installed and committed. Specs in `planscape-web/e2e/`, run
  `npm run test:e2e` from `planscape-web`. Deliberately NOT in `npm test` (needs
  a live app + token). Env: `TEST_BASE`, `TEST_JWT`, `TEST_PROJECT`.
  - `investigate.spec.ts` — sweeps every project page for console/API errors.
  - `meeting.spec.ts` — the iframe-remount investigation. **Its Meet/Join
    selectors did NOT successfully drive the meeting UI** ("Join control not
    found after opening the menu"), so the meeting flow was never reproduced end
    to end. Fixing those selectors is a good first move.
- Playwright auth is seeded by writing the app's own `planscape_token`
  localStorage key from a PAT-exchanged JWT. **Never type a password.**

## Repo conventions

- One PR per logical change; `gh pr create` with Summary + Test plan.
- **Known-broken CI:** `Build & Test` and `CI Gate` fail on a Postgres service
  container (`role "root" does not exist`) — broken on `main` itself, unrelated
  to any change. Verify locally with `dotnet test`, then `gh pr merge --admin`.
  Do NOT bypass any OTHER failing check.
- Server test suite takes ~25-40 min (no local Redis; several tests burn ~35s
  each retrying). Don't kill it early. If interrupted, a stale `testhost.exe`
  locks the DLLs and causes a confusing `MSB3027` copy error next build —
  `taskkill //F //PID <pid>` the PID named in the error.
- Plugin deploy (only if `StingTools/` changes): build Release, copy
  `StingTools/bin/Release/.` over `C:\Dev\STINGTOOLS\CompiledPlugin\`. **Revit
  must be closed AND the Planscape Companion tray app stopped** — it holds ~17
  DLLs and the copy silently half-fails otherwise. Restart it afterwards.

## Ground rules

- Verify against the live system rather than reasoning from code alone. Every
  real bug here was found by measuring; two confident theories were wrong.
- Report honestly what was and wasn't reproduced. A clear "couldn't reproduce X"
  beats a plausible-sounding guess.
