# Planscape Testing Guide — From StingTools

A brief guide to wiring up the StingTools Revit plugin to a running
Planscape server so you can exercise the bidirectional sync, BCC
Platform tab, and mobile companion end-to-end.

---

## 1. Stand up the server (one-time)

```bash
cd Planscape.Server/docker
docker compose up -d
```

Verify it is alive:

| Check | URL |
|---|---|
| API health | `http://localhost:5000/health` |
| Swagger UI | `http://localhost:5000/swagger` |

Demo credentials seeded in `Planscape.API/SeedData.cs`:

| Field | Value |
|---|---|
| Email | `admin@planscape.demo` |
| Password | `admin123` |
| Tier | Premium (free seat) |

> The seed creates one tenant + one demo project + a few issues so the
> mobile dashboard isn't empty on first connect.

For a **public** test endpoint (no Docker needed), the plugin defaults
to `https://planscape-api.onrender.com` — see the BCC Platform tab
screenshot.

---

## 2. Connect the plugin

1. Open Revit 2025 / 2026 / 2027 with **StingTools** loaded.
2. Open any project (the BCC needs an active document).
3. Open the **BIM Coordination Center**:
   - Dock panel → **BIM** tab → **BIM Coordination Center**, or
   - Run `BIMCoordinationCenter` from the command bar.
4. Click the **PLATFORM** tab on the left rail.
5. Pick **Planscape ★** from the platforms list (it's the default).
6. Fill in **Server Connection**:

   | Field | Local Docker | Public test |
   |---|---|---|
   | Server URL | `http://localhost:5000` | `https://planscape-api.onrender.com` |
   | Email | `admin@planscape.demo` | (your account) |
   | Password | `admin123` | (your password) |

7. Press **Connect**.

The status block should flip to:

```
✅ Successfully connected to Planscape
User:  admin@planscape.demo
Tier:  Premium
```

…and the connection is persisted to
`<project>/_BIM_COORD/planscape_connection.json` (password is **not**
saved). The dock-panel sync chip starts ticking every 5 minutes
(`Planscape.PluginSync.SyncScheduler`).

---

## 3. First-time onboarding wizard (alternative)

If you would rather walk through the three-step flow:

- BIM tab → **Plugin Onboarding**
- Steps: 1) paste licence → 2) pick project → 3) publish first model.

The wizard delegates to `PlanscapeConnectCommand` for step 1 and
`PublishModelCommand` for step 3, so the result is identical to the
manual route above.

---

## 4. Push data and confirm the round-trip

Once connected, exercise each integration channel from the BCC:

| Channel | Plugin trigger | Server endpoint | Verify on |
|---|---|---|---|
| Tag sync | **Sync Elements to Server** (Platform tab) | `POST /api/tagsync/sync` | Mobile dashboard tag list |
| Compliance | **Compliance snapshot on revision** toggle | `POST /api/projects/{id}/compliance` | BCC Overview gauge / mobile gauge |
| Issues | Issues tab → **Raise Issue** | `POST /api/projects/{id}/issues` | Mobile Issues tab |
| Documents | Docs tab → **Add Document** | `POST /api/projects/{id}/documents` | Mobile Documents tab |
| 3D model | BIM tab → **Publish 3D Model** | `POST /api/projects/{id}/models` | Mobile Models viewer |
| Workflows | any workflow preset run | auto: `POST /api/projects/{id}/workflows` | BCC Workflows tab |
| Push tokens | mobile login | `POST /api/notifications/subscribe` | Settings → Notifications |

**Real-time updates:** SignalR hubs `/hubs/compliance`, `/hubs/tagsync`,
`/hubs/notifications` are subscribed automatically by
`PlanscapeRealtimeClient` on connect — compliance changes from the
plugin propagate to mobile within a second.

---

## 5. Validate from the mobile app (optional)

```bash
cd Planscape
npm install
npx expo start
```

- Log in with the same `admin@planscape.demo` / `admin123`.
- Pick the demo project.
- The Dashboard tab should show the compliance % you just pushed; the
  Issues tab should show whatever you raised in step 4.

For Android/iOS device testing, scan the Expo QR with the **Expo Go**
app on the same Wi-Fi as your dev box.

---

## 6. Inspect from Swagger (debugging)

Open `http://localhost:5000/swagger` and use the **Authorize** button
with the JWT obtained from `POST /api/auth/login`. The token is also
written to the Revit log (`StingTools.log`) under
`Planscape: Connected …`.

Common quick checks:

```http
GET  /api/projects                          → list of projects on tenant
GET  /api/projects/{id}/dashboard           → compliance + issue counters
GET  /api/projects/{id}/issues              → all issues for project
GET  /api/projects/{id}/compliance/latest   → latest snapshot
```

---

## 7. Going offline

The Planscape Connect dialog hits `StingOfflineConfig.RefuseIfOffline`
first, so disabling network access blocks login politely. Once
authenticated, sync attempts that fail are dropped into
`Planscape.PluginSync.OfflineQueue` (file-backed) and flushed on the
next 5-min tick when connectivity returns. The dock-panel chip turns
amber while items are queued.

---

## 8. Troubleshooting

| Symptom | Likely cause | Fix |
|---|---|---|
| `Authentication failed` | wrong URL scheme (`http` vs `https`) | match the server's actual scheme |
| `Connect` button greyed | no active Revit document | open a project first |
| Mobile shows empty dashboard | no project selected on plugin side | pass `PlanscapeProjectId` in the connect dialog or pick one in the wizard |
| Sync chip stuck red | scheduler never started | re-run `PlanscapeConnect`; it idempotently starts `SyncScheduler` |
| 401 on every endpoint | JWT expired (60-min lifetime) | client auto-refreshes; re-press **Connect** if it doesn't |

---

## 9. Reference

- Plugin entry point: `StingTools/BIMManager/PlatformLinkCommands.cs:2580` (`PlanscapeConnectCommand`)
- HTTP client: `StingTools/BIMManager/PlanscapeServerClient.cs`
- Background scheduler: `Planscape.Server/src/Planscape.PluginSync/SyncScheduler.cs`
- Server seed: `Planscape.Server/src/Planscape.API/SeedData.cs`
- Gap analysis: `Planscape.Server/docs/PLANSCAPE_GAPS.md`
