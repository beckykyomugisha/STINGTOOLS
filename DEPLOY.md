# DEPLOY — the four front-end surfaces (don't conflate them)

Planscape has **four** distinct front-end surfaces. A change to one does **not** reach the
others, and each has a different "make it SERVED" step. Always verify the **served** artifact
(curl + grep a marker), not just the source file.

| # | Surface | Source | How it's served | To deploy a change | SERVED gate |
|---|---|---|---|---|---|
| 1 | **Viewer (esbuild bundle)** | `Planscape/assets/viewer/coordination-viewer.js`, `viewer-extras.js`, `signalr-shim.js` | esbuild-**minified** by `build.mjs` in the Dockerfile → `dist/`, overlaid into the image | edit BOTH `assets/viewer/*` **and** `wwwroot/*` (byte-equal), then `docker compose build --no-cache api && up -d --force-recreate api` | `curl localhost:5000/coordination-viewer.js \| grep <marker>` (marker must be a string literal to survive minify) |
| 2 | **wwwroot verbatim** | `Planscape/assets/viewer/viewer.html`, `livekit-av.js`, `meeting-sync.js`, `vendor/*` (+ `wwwroot/app/index.html`) | copied verbatim, **baked into the image** at build | edit BOTH copies, then `docker compose build --no-cache api && up -d --force-recreate api` | `curl localhost:5000/viewer.html \| grep <marker>` |
| 3 | **Vanilla web `/app` dashboard** | `wwwroot/app/index.html` (nav) + `wwwroot/js/dashboard.js` + `wwwroot/css/*` | static under `/app`; **`wwwroot/js` + `wwwroot/css` are VOLUME-MOUNTED** in `docker/docker-compose.yml`, `wwwroot/app/index.html` is **baked** | `dashboard.js`/css: edit → `docker compose restart api` (no rebuild). `index.html`: edit → `docker compose build --no-cache api && up -d --force-recreate api` | `curl localhost:5000/js/dashboard.js \| grep STING_DASH_BUILD` |
| 4 | **Expo mobile app** | `Planscape/app/**`, `Planscape/src/**` | Expo dev server / EAS build (mobile). **NOT web-exportable today** (no `react-native-web`, native-only `@livekit/react-native-webrtc`); **NOT served at `/app`** | `tsc --noEmit` to gate; ship via Expo/EAS. Web export would need react-native-web + web LiveKit shims + `baseUrl:/app` first | n/a here — verify in the mobile app / Expo |

## Critical: `/app` is surface #3, NOT surface #4
The served web `/app` is the **vanilla JS office dashboard** (#3), not the Expo app (#4).
`npx expo export -p web` currently **fails** (missing `react-native-web`; the app uses
native-only LiveKit/WebRTC). So a feature the user should see on the **web** dashboard must be
built in `dashboard.js` (#3); the Expo app (#4) is mobile-only until web support is added.

## Markers (one per surface)
- #1 viewer: `STING_VIZ_BUILD <name>` in `coordination-viewer.js` (survives minify as a literal).
- #2 verbatim: grep a known string in the served `.html`/`.js`.
- #3 web `/app`: `STING_DASH_BUILD <name>` in `dashboard.js`.
- #4 Expo: no served marker; `tsc --noEmit` + in-app/Expo verification.

## Volume mounts (compose) — what reflects WITHOUT a rebuild
`docker/docker-compose.yml` mounts `../src/Planscape.API/wwwroot/js` and `.../wwwroot/css`
into the api container. So **only** `wwwroot/js/*.js` + `wwwroot/css/*.css` reflect on a plain
`docker compose restart api` (or even a browser refresh). Everything else in `wwwroot`
(`viewer.html`, `coordination-viewer.js` dist overlay, `wwwroot/app/index.html`, `vendor/*`)
is baked at image build → needs `docker compose build --no-cache api`.

## Meeting features — web `/app` (#3) vs Expo (#4) parity (2026-06, audit)
| Feature | Web `/app` (dashboard.js) | Expo mobile |
|---|---|---|
| Meetings list | ✅ (read-only table) | ✅ |
| **Recordings** (detail block + list badge + project archive + player) | ✅ (this change) | ✅ |
| Notifications infra (SignalR `NotificationHub`) | ✅ (handles `MeetingCreated`/`MeetingUpdated`) | ✅ |
| Live-meeting **JOIN / A/V** (LiveKit) | ❌ web-missing | ✅ |
| Meeting **authoring** (create / minutes / agenda / attendees / actions) | ❌ web-missing (list only) | ✅ |
| Live-start notification + `?meeting=` **Join deep-link** + `MeetingScheduled` event | ❌ unwired on web | ✅ (push + in-app) |

**Web-missing → candidates to port to `dashboard.js`** if web parity is wanted: live-meeting
JOIN/A-V, meeting authoring, and wiring the live-start notification + Join deep-link.
