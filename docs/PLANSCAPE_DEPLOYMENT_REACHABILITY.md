# Planscape — opening on phones and other computers

Short answer to "why won't it open on other devices?": every component
defaults to `localhost`, which on a phone or another computer means
*that* device's loopback, not the dev machine's. To make Planscape
reachable from outside the dev box, three things need real hostnames:

1. **API server** must be reachable from the LAN / internet.
2. **Mobile app** must be built (or configured) to point at that host.
3. **Web app** (planscape-site) must list that host as an allowed CORS
   origin and be deployed to a real URL itself.

This branch wires up the configuration knobs for all three. None of
them require code changes after the initial fix-up — they're all
environment variables.

---

## 1. API server (Planscape.Server)

### Bind address

`Planscape.Server/docker/Dockerfile` already binds Kestrel to all
interfaces:

```dockerfile
ENV ASPNETCORE_URLS=http://+:8080
```

`Planscape.Server/docker/docker-compose.yml` exposes it:

```yaml
ports:
  - "5000:8080"        # 0.0.0.0:5000 → container 8080 (LAN-reachable)
```

> Docker maps to `0.0.0.0` by default unless you write
> `127.0.0.1:5000:8080`. From the dev machine's LAN, the API is at
> `http://<dev-machine-ip>:5000`.

### CORS origins

Web origins must be explicitly whitelisted before the dashboard's
cookie-authenticated calls work cross-origin. See
`Planscape.Server/src/Planscape.API/appsettings.json:Cors:Origins`.

This branch adds the production hostnames:

```json
"Cors": {
  "Origins": [
    "http://localhost:3000",
    "http://localhost:5173",
    "https://planscape.app",
    "https://www.planscape.app",
    "https://app.planscape.app",
    "https://viewer.planscape.app",
    "https://staging.planscape.app"
  ]
}
```

Override per environment with `Cors__Origins__0=https://your-domain` env
vars (the `__0`/`__1`/... index syntax replaces the whole array).

The Mobile policy in `Program.cs:733` is permissive on origin (Expo Go
uses dynamic LAN IPs) but *does not* allow credentials — mobile uses
Bearer tokens, not cookies — so it never needs an explicit list.

---

## 2. Mobile app (Planscape/)

The app reads its API base from (in order):

1. **SecureStore** — what the user typed in Settings ("API server").
2. **EXPO_PUBLIC_API_BASE** — baked into the build at compile time
   from `.env` or `eas.json`. **This is the lever that makes a release
   build work on a phone out of the box.**
3. `http://localhost:5000` — fallback for fresh dev runs.

### Local LAN dev

```bash
# Find your dev machine's LAN IP
hostname -I  # or `ipconfig getifaddr en0` on macOS

# In Planscape/.env
EXPO_PUBLIC_API_BASE=http://192.168.1.42:5000
```

Reload the Expo client and the phone now talks to the dev machine.

### Release builds

`Planscape/eas.json`'s `production` profile already requires
`PLANSCAPE_HOST` to be set. This branch adds the matching mobile
`.env.example` so the variable is documented alongside the rest.

### Settings escape hatch

Even if the build was tagged with the wrong host, the user can
override it at runtime: **Settings → API server → enter URL → Save**.
The override is stored in SecureStore and survives reboots.

---

## 3. Web app (planscape-site/)

Static Next.js export, no built-in API integration. Two reachability
issues to fix when deploying:

1. **Pick a host.** `next.config.js` has `output: 'export'` so the
   build emits a static site that can drop into Vercel, Netlify,
   Cloudflare Pages, AWS S3 + CloudFront, or the API host itself
   (under `/app`).
2. **Tell the API its origin.** Add the hostname you picked to
   `Cors:Origins` (see §1).

### Africa map

Set `NEXT_PUBLIC_MAPBOX_TOKEN` in `.env.local` (dev) or
`.env.production` (deployed). See `planscape-site/.env.example`.
Without a token the section renders a fallback message rather than
crashing the page.

---

## 4. Coordination viewer (`Planscape/assets/viewer/viewer.html`)

The viewer can be embedded inside the mobile app's WebView (no network
calls — RN host bridges everything) **or** served standalone from any
host (e.g., `https://viewer.planscape.app/viewer.html?project=…`).

When served standalone:

- The viewer's API base is resolved at runtime from
  `localStorage.planscape_api_base` (set via the Settings popover the
  brand bar, ⚙ icon), then `?api=` URL param, then a localhost
  fallback. Users can roam between LAN / staging / production by
  changing the Settings value — no rebuild required.
- The viewer also accepts a `?embed=1` param so a parent dashboard can
  iframe it without triggering the auto-redirect on 401.

---

## Quick checklist for a new deployment

| Step | What | Where |
|---|---|---|
| 1 | Pick API hostname | DNS A-record / load balancer |
| 2 | Set `Cors__Origins__0=https://<host>` | App service env vars |
| 3 | Set `EXPO_PUBLIC_API_BASE` | `Planscape/eas.json` env block |
| 4 | Set `NEXT_PUBLIC_MAPBOX_TOKEN` | `planscape-site/.env.production` |
| 5 | Deploy the static export | Vercel / Netlify / Cloudflare Pages |
| 6 | (Optional) Reverse-proxy `/app` and `/viewer.html` from the API host | nginx / caddy |
| 7 | Run `dotnet ef database update` for the new `WatcherUserIds` column | DB host |

Once those seven values are filled in, the API, mobile app, web app,
and viewer all open from any device on the same network or the public
internet.
