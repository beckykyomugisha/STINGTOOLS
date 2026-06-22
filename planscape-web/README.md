# Planscape Web

The browser app for Planscape — online BIM coordination. It's a thin,
real-time front end over the **Planscape Server** API: sign in, browse
projects, raise and triage issues, review clashes, manage CDE documents and
transmittals, run coordination meetings (with live video), and view the
federated 3D model.

Built with **Next.js 14 (App Router)**, **TypeScript**, and **Tailwind**.
Live updates and notifications come over **SignalR**; meetings use
**LiveKit**.

---

## Run locally

```bash
cd planscape-web
npm install
npm run dev          # http://localhost:3000
```

By default the app talks to the hosted API at `https://api.planscape.build`.
Point it elsewhere with environment variables (see below). Sign in at
`/login` with a Planscape account; the demo server seeds
`admin@planscape.demo` / `admin123`.

> **CORS:** auth is by bearer token (not cookies), so the API must allow this
> app's origin. `app.planscape.build` is allow-listed on the hosted API. For
> local dev against the hosted API, either run the API locally or add
> `http://localhost:3000` to the server's `Cors__Origins`.

### Scripts

| Command | What it does |
|---|---|
| `npm run dev` | Next dev server (hot reload) |
| `npm run build` | Production build |
| `npm start` | Serve the production build |
| `npm run typecheck` | `tsc --noEmit` |
| `npm run lint` | `next lint` |
| `npm test` | Vitest contract tests (see [Testing](#testing)) |

---

## Environment variables

All client-readable, so all are `NEXT_PUBLIC_*`. None are secret.

| Variable | Default | Purpose |
|---|---|---|
| `NEXT_PUBLIC_API_BASE` | `https://api.planscape.build` | Planscape Server base URL. Trailing slash is stripped. |
| `NEXT_PUBLIC_VIEWER_URL` | `${API_BASE}/viewer.html` | The 3D viewer HTML page. Override only if the viewer is hosted separately. |

For local dev, create `planscape-web/.env.local`:

```
NEXT_PUBLIC_API_BASE=http://localhost:5000
```

---

## Architecture

The app is deliberately thin. UI components never call `fetch` directly —
everything goes through a small data layer so auth, error handling, and URL
shaping live in one place.

```
app/        Next App Router pages (UI only)
components/ Shared UI (AppShell, NotificationBell, …)
lib/        The actual logic — see below
```

### `lib/api.ts` — the fetch wrapper

`api<T>(path, init)` is the single entry point for API calls. It:

- attaches the bearer token from `localStorage` (`Authorization: Bearer …`),
- sets `Content-Type: application/json` for bodies (skipped for multipart
  uploads, which let the browser set the boundary),
- throws a typed `ApiError(status, message)` carrying the server's message,
- normalises `204 No Content` to `undefined`,
- on `401`, clears the token and bounces to `/login`.

It also exports `API_BASE`, `getToken`, `setToken`.

### `lib/data.ts` — the typed contract

One function per endpoint (`listProjects`, `listIssues`, `updateIssue`,
`listClashes`, `uploadDocument`, `transitionDocument`, `listMeetings`,
`getLiveKitToken`, `search`, …), each returning the types from `lib/types.ts`.
Also holds URL helpers for resources that load via the browser directly
(`<img>`, `<a download>`, iframes) — `documentDownloadUrl`, `modelFileUrl`,
`photoFileUrl`, etc. These append `?access_token=` because those requests
can't set an `Authorization` header; the server accepts the token as a query
param on `/api` routes for exactly this reason.

### `lib/auth.tsx` — session

`AuthProvider` + `useAuth()`. Wraps login/logout, persists the token via
`setToken`, exposes the current user. Pages assume an authenticated session
(the wrapper redirects to `/login` on 401).

### `lib/realtime.ts` — live project updates

`useProjectRealtime(projectId, onEvent)` opens a SignalR connection to the
`NotificationHub` (token passed as `access_token`), joins the project group,
and fires `onEvent(event, payload)` for issue/comment/compliance/clash
broadcasts. Best-effort — UI degrades to manual refresh if the hub is down.

### `lib/notifications.tsx` — the bell

`NotificationsProvider` keeps a live list of notification events for the
signed-in user and drives the unread badge in `NotificationBell`.

---

## Pages

| Route | Purpose |
|---|---|
| `/login` | Sign in |
| `/dashboard` | Landing after login |
| `/projects` | Project list (+ RAG, open-issue counts, **New project**) |
| `/projects/new` | Create a project |
| `/projects/[id]` | Project home — issue list with status filters + live badge |
| `/projects/[id]/issues/new` | Raise an issue |
| `/projects/[id]/issues/[issueId]` | Issue detail — comments, assignee + due-date editing |
| `/projects/[id]/clashes` | Clash review |
| `/projects/[id]/documents` | CDE documents — upload, filter, search, state transitions, paging |
| `/projects/[id]/transmittals` | Transmittals — create, send, acknowledge, respond |
| `/projects/[id]/photos` | Site photo gallery — approve / reject |
| `/projects/[id]/members` | Project members — invite, change role, remove |
| `/projects/[id]/meetings` | Coordination meetings — agenda, minutes, actions |
| `/projects/[id]/meetings/[meetingId]/live` | Live meeting — LiveKit video + 3D viewer |
| `/projects/[id]/models` | Model uploads (IFC → GLB) |
| `/projects/[id]/viewer` | Federated 3D model viewer |
| `/search` | Tenant-wide search |

Plus `app/error.tsx` (global error boundary) and `app/not-found.tsx` (404).

---

## Testing

Contract tests (Vitest + jsdom) live next to the code they cover:

```bash
npm test            # vitest run
```

- `lib/api.test.ts` — the fetch wrapper: token round-trip, bearer header,
  JSON content-type, `204 → undefined`, `ApiError`, 401-clears-token.
- `lib/data.test.ts` — every `data.ts` function against a mocked `fetch`,
  asserting URL, method, body, query encoding, response unwrapping, and the
  `?access_token=` URL helpers.

These run without a backend (`fetch` is mocked), so they're the fast feedback
loop and the CI gate. CI (`.github/workflows/planscape-web.yml`) runs
type-check → test → build on every PR touching `planscape-web/`.

---

## Deploy

The whole platform (this app, the API, the worker, the IFC→GLB converter,
Redis, MinIO) deploys from `render.yaml` at the repo root. Step-by-step
instructions — secrets, env groups, DNS, post-deploy validation — are in
[`docs/DEPLOY_RUNBOOK.md`](../docs/DEPLOY_RUNBOOK.md).
