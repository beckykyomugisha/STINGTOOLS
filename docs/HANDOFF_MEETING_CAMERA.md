# Handoff — the web 3D viewer's live meeting (CLOSED 2026-08-06)

> **STATUS: both bugs RESOLVED — confirmed by the product owner on the live
> deploy, 2026-08-06.** Nothing here is open. This file is kept for the seven
> disproven theories below, which cost real time to eliminate and would
> otherwise be re-tried by the next person. Read it before forming a theory
> about meetings, camera, or the viewer's model load; do not read it as a
> to-do list.
>
> If a NEW meeting/camera fault appears, start from a fresh reproduction — do
> not assume it is one of these two returning.

## Resolution

Owner-run reproduction against `planscape-web-free` / `planscape-api-free`
(project `2e4a5fb9-a65d-4062-bc8e-a5e53e8cb462`), browser console captured:

- **Symptom 1 — model hangs at "Loading model 0%": not reproducing.** The model
  renders: 624 elements, `elementCount: 562` with real bounds, and
  `mesh→meta resolver: 562/562 meshes resolved (100%)`.
- **Symptom 2 — "Couldn't join — retry", no camera: not reproducing.** The panel
  reads **`1 online · 1 in call`** (was `0 in call`), LiveKit logs
  `publishing track`, and the local camera tile renders.

No single commit is identifiable as "the fix" — the symptoms were gone by the
time the flow could be driven end to end. The changes in that area since the
report are the `s2-latejoin` A/V bundle, the `ready`-COUNTER fix in the host
page (a boolean bailed out of React's re-render, so the reloaded viewer was
never re-sent its model), and carrying `modelId` across the meeting
re-navigation in `meetingJoinUrl`. Between them they cover symptom 1's
mechanism directly.

**One real bug WAS found in that console and fixed** — unrelated to either
symptom: the "join link copied" toast was lying. The iframe's `allow` attribute
omitted `clipboard-write` (that attribute REPLACES the frame's default
permissions policy), and `copyToClipboard` could not detect the refusal because
`navigator.clipboard.writeText` rejects asynchronously — so the synchronous
`try/catch` never saw it and the `execCommand` fallback never ran.

Console noise that is NOT a fault, so nobody chases it again:
- **A 403 every 15s on `/health`.** Deliberate: `/health` is restricted to
  private-range client IPs + `X-Health-Token`, so a browser ALWAYS gets 403. An
  anonymous `curl` with no JWT and no tenant header gets 403 too. The viewer
  already treats any HTTP response as "reachable", which is why the pill
  correctly reads `Live`. (The `Offline` pill in the original report was a
  separate, earlier state and did not recur.)
- **404 on `/scene`** — no federation chunks published; the single-model
  fallback path is correct.
- **`silence detected on local audio track`** — environmental (OS input
  device), not code.

## The original symptoms (for reference — both now resolved)

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

*(1–3 were eliminated in session 1; 4–7 in session 2, listed further down.)*

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

**RETRACTED (2026-08-06) — the tenant-mismatch lead below is WRONG.** The 403s
in the owner's console are all on `/health`, which returns 403 to an anonymous
`curl` carrying no JWT and no `X-Tenant` at all. It is IP-restricted by design.
`TenantResolutionMiddleware` was not involved. The paragraph is kept only so the
theory is not re-derived from the same code reading — it is a plausible-looking
inference that a one-line `curl` disproves.

~~New lead — the most likely 403 source is a tenant-header mismatch.~~
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

## The live lead — CLOSED, and it was a false trail

Playwright saw a **403** from inside the viewer iframe and this section treated
it as the direct cause of `Couldn't join`. Both halves turned out to be wrong,
and the way they were wrong is the most transferable lesson in this file:

- The 403 was the 15-second `/health` poll — expected, by design, and already
  handled by the code that issues it. It had nothing to do with the join.
- It could not have been the join in any case: the `livekit-token` action has
  **no 403 path at all** (404 / 400 / 401 / 501 only).

The JWT-snapshot hypothesis below was never confirmed and is now moot. It was
also aimed at the wrong token: LiveKit participant tokens are minted with a
**4-hour** TTL, so staleness on that side was never plausible.

~~Untested hypothesis:~~ the iframe gets a SNAPSHOT of the JWT at page-load (the
parent passes it as a URL param; `viewer.html`'s bootstrap writes it into that
origin's localStorage). JWTs expire after 30 minutes; the iframe's frozen copy
never refreshes. *(Still structurally true of the code — the token IS captured
once at module load and the frame is cross-origin so the parent cannot refresh
it. It just was not causing either reported symptom. Worth remembering if a
long-lived session ever does start 401ing on the viewer's own API calls.)*

## If you are here about a NEW meeting fault

The single highest-value first step, ahead of any code reading:

1. **Get the browser console from a live reproduction.** The deployed bundle
   already surfaces real causes — `setLobby('error', detail)` renders
   `Couldn't join — <cause>`, and livekit-av.js reports through `console.warn`.
   One screenshot of that console settled what three sessions of inference could
   not.
2. `planscape-web/e2e/meeting.spec.ts` now drives the flow properly
   (`#btnMeet → #meetStart → wait for the frame to re-navigate → #lkJoin`) and
   prints the pill text, every meeting/model request status, and console
   warnings. Needs `TEST_JWT`, `TEST_PROJECT`, `TEST_BASE`.
3. Render logs (dashboard → `planscape-api-free` → Logs) remain the best tool
   for anything that looks server-side. The agent has no dashboard access — ask
   for the trace.

**Confirm the served bundle is the code you are reading**, which is not a given:

```bash
curl -s https://planscape-api-free.onrender.com/livekit-av.js | grep -o 'STING_MEETING_BUILD = "[^"]*"'
```

then `diff` it against `Planscape/assets/viewer/livekit-av.js` (normalise CRLF).
On 2026-08-06 they were byte-identical at marker `s2-latejoin`.

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
