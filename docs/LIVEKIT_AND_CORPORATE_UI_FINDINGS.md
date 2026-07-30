# LiveKit hosting · corporate ACC-style UI · two-firm multi-tenancy — findings

**Researched:** 2026-07-31 · **Base:** `origin/main` @ `2abc332ad` (PR #511) · worktree
`claude/livekit-corporate-ui-research-3233e0`.

Everything under "verified in-repo" was read from the tree at that commit. Everything under
"external" was fetched live on 2026-07-31 and **will rot** — vendor pricing pages change; re-check
before committing money.

---

## 0. Verdict up front

| Question | Answer |
|---|---|
| Can we get cameras working on the deployed free tier without new paid infra? | **Yes.** LiveKit Cloud's free *Build* tier + 3 env vars on `planscape-api-free`. £0, no card. |
| Can we self-host LiveKit on Render? | **No.** Render has no UDP. This is a hard platform limit, not a config problem. |
| Is the meetings feature set actually built? | **Yes, and more than expected** — but it lives in the API's `wwwroot/` bundles, not in the Next.js app. That distinction changes the ACC-UI scope materially. |
| Is two-firm isolation ready? | **Structurally yes, with three concrete holes.** The DB/API layer is genuinely good; recordings storage, LiveKit room namespacing, and one-tenant-per-user in handoff are the gaps. |
| Is any of this blocked on the Render card? | **The camera demo is not.** The production path is. |

---

## 1. LiveKit hosting — options and real cost

### 1a. What the repo actually needs from LiveKit

Verified in-repo:

| Config key | Read at | Purpose |
|---|---|---|
| `LiveKit:Url` | `MeetingRoomController.cs:348` | `wss://` URL handed to the **browser** |
| `LiveKit:ApiKey` / `LiveKit:ApiSecret` | `MeetingRoomController.cs:349-350` | HS256 token signing |
| `LiveKit:ServerUrl` | `LiveKitEgressClient.cs:47` | server→LiveKit **https** for Twirp admin/egress calls |
| `LiveKit:Egress:S3:*` | `LiveKitEgressClient.cs:58-67` | recording destination |

Two independent gates, both returning **501 when unset** (ships dark):

- `/livekit-token` → 501 unless `Url` + `ApiKey` + `ApiSecret` are all present (`MeetingRoomController.cs:351-352`).
- `/recording/start` → 501 unless `IsConfigured` — which additionally requires `ServerUrl` **and** S3
  bucket/key/secret/endpoint (`LiveKitEgressClient.cs:72-77`).

So **A/V needs 3 env vars. Recording needs 8.** They are separable — you can have cameras with no
recording, which is exactly the right first step.

`LiveKitTokenFactory` signs with raw `HMACSHA256` (deliberately, per its own docstring — Microsoft's
JWT stack rejects the 6-byte dev secret with `IDX10653`). This works with any secret length, so a
real LiveKit Cloud secret needs no code change.

Neither Render blueprint has A/V configured today: `render.yaml:294-301` declares the four LiveKit
keys as `sync: false` (never set); `render.free.yaml` has **no LiveKit keys at all**.

### 1b. Option A — LiveKit Cloud, free "Build" tier ✅ recommended for the demo

External, fetched 2026-07-31 from [livekit.com/pricing](https://livekit.com/pricing) and
[docs.livekit.io quotas](https://docs.livekit.io/home/cloud/quotas-and-limits/):

| Build (free) | Included |
|---|---|
| Cost | **$0/mo, no credit card** |
| WebRTC participant-minutes | **5,000/mo** |
| Concurrent connections | 100 |
| Downstream data transfer | 50 GB |
| Recording / transcode | **60 minutes** (shared) |
| Concurrent egress sessions | 2 |
| Overage behaviour | **Hard cap** — requests fail, you are never billed |

**What 5,000 participant-minutes buys.** A participant-minute = one user connected for one minute,
so a meeting costs `participants × duration`:

| Scenario | Cost/meeting | Meetings/month within free tier |
|---|---|---|
| 2-person, 1 h (the PENDING-HUMAN-VERIFY test) | 120 p-min | ~41 |
| 6-person coordination call, 1 h | 360 p-min | ~13 |
| **Two firms, each one weekly 6-person hour** | 2,880 p-min/mo | fits, with ~40% headroom |

That is a genuinely usable pilot allowance for two firms — not just a toy. The binding constraint is
**recording: 60 min/month total**, which one recorded meeting exhausts. Recording stays a
demo-only capability on the free tier.

The hard cap matters: there is no surprise bill. Exceeding the quota degrades to "meeting won't
start", which is the failure mode you want while proving the thing.

### 1c. Option B — self-host LiveKit on Render ❌ not possible

LiveKit's own deployment docs require **UDP 50000-60000** for media plus **UDP 3478** for TURN, a
public IP, and recommend host networking; TCP 7881 exists only as a degraded fallback.

Render exposes external traffic over **HTTPS/TCP only**. UDP support is an open, unshipped
[feature request](https://feedback.render.com/features/p/support-udp); multiple community threads
([1](https://community.render.com/t/how-to-set-web-rtc-udp-ports-3478-and-1027-to-65535/22810),
[2](https://render.discourse.group/t/webrtc-udp-server-possible-the-render-com-server-will-be-the-central-peer/4109))
land on the same answer. A Render web service also exposes only one public port.

Forcing every participant through TCP/TURN would technically connect but gives materially worse
latency and jitter — and you still cannot open 3478/UDP for the clients that need it. **This option
is closed regardless of what plan is paid for.** It is worth writing down explicitly because
`render.yaml:23` currently implies self-hosting is an option ("LiveKit Cloud (or self-host)").

### 1d. Option C — self-host LiveKit on a VPS ⚠️ possible, but not cheap in the way it looks

A small Hetzner CX22-class box (2 vCPU / 4 GB) is roughly **€4–5/month**
([pricing](https://www.hetzner.com/pressroom/new-cx-plans/) — note Hetzner raised cloud prices in
June 2026, so verify). That number is misleading:

- LiveKit's docs recommend **10 Gbps** networking for production; a €4 VPS is demo-grade.
- The **egress** container runs headless Chrome and needs `cap_add: SYS_ADMIN` (already encoded in
  `Planscape.Server/docker/docker-compose.yml`) — real CPU and RAM, not a sidecar.
- You inherit TLS certs, TURN, a UDP firewall, `use_external_ip`/`LIVEKIT_NODE_IP` ICE config, key
  rotation, and upgrades.

The compose stack (`livekit.yaml` + `egress` + `minio` + `createbuckets`) means the *software* is
already assembled — this is an ops-cost decision, not a build-cost one. Revisit only if LiveKit
Cloud's Ship tier ever becomes the binding cost, which at two firms it will not.

### 1e. Cost summary — fast demo vs production

| Path | LiveKit | Hosting | Monthly | Blocked on the Render card? |
|---|---|---|---|---|
| **Fast demo** | Cloud Build (free) | `render.free.yaml` (deployed) | **$0** | **No** |
| **Production** | Cloud Ship (from **$50/mo**: 150k WebRTC min, 1,000 concurrent, 250 GB, 600 rec min, then $0.02/min video) | `render.yaml` paid stack (~$54/mo) | **~$104/mo** | Yes |
| Self-host SFU | VPS ~€5–15 + ops | either | lower cash, higher ops | Partially |

Free-tier caveats that apply to the demo *regardless* of LiveKit
(`render.free.yaml:12-23`): the API **sleeps after ~15 min idle** with a 30–60 s cold start, 512 MB
RAM is tight for .NET, and the free Postgres is time-limited.

One nuance in our favour: **LiveKit media is independent of our API once the token is minted.** If
the API sleeps mid-call, the video keeps flowing; what drops is SignalR co-presence (roster, markup,
chat, camera-follow). So a sleeping free-tier API degrades the meeting rather than killing it —
worth knowing before anyone reports it as a LiveKit bug.

---

## 2. `docs/MEETINGS_AUDIT.md` triage — wire-and-verify vs needs-code

Read end to end. Every PENDING-HUMAN-VERIFY item, split by what it actually blocks on.

### 2a. Needs LiveKit connected, then just verify (no new code)

These are SERVED-proven — the container demonstrably serves the right bundle — and only lack the
two-real-webcams test:

| Ref | Item | Marker |
|---|---|---|
| M1-2 | Join/Leave lobby, gesture-gated device prompts, per-device denial handling, state pill | `M1-polish` |
| M2 | Document markup: pen/arrow/text/rect/highlight broadcast, colours, Clear, Grant, Snapshot, ⚑Issue | `M2-markup` |
| M3 | Chat, reactions, raise-hand, roster+roles, make-host, mute-all, remove, device picker, speaker/gallery/pin, low-bandwidth | `M3-confer` |
| M4 | Raise-issue-from-meeting, clash-review stepper + camera-follow, viewpoint snapshot, meeting link, actions, minutes | `M4-aec` |
| M5 | The cross-cutting discovery matrix (start/join, 2+ participants, leave/rejoin, reconnect, host handoff, surface switch under load, screen share, token expiry, tenant isolation) | docs |
| N1 | Remote tiles populate/clear, camera-off initials placeholder, per-tile mic/cam badges, roster A/V status, identity correlation | `N1-presence` |
| N3 | Document picker + drag-drop upload + the `/file`→`/download` fix | `N3-docs` |
| N4 | Panel move/minimise/close, PiP→sidebar→theater cycle, persistence | `N4-layout` |
| N2 | Real-webcam recording (the existing proof used a synthetic `livekit-cli --publish-demo` publisher) | `N2-recording` |
| — | Labelled `⏺ Record` / `📄 Present` pills are discoverable | `meet-discover` |
| — | Live-meeting notification reaches a second account, excludes the starter | (server) |

**Current markers on disk** (`wwwroot/livekit-av.js:30`, `wwwroot/meeting-sync.js:27`):
`meet-discover` and `ws1d-syncview`. These are the strings to bump when proving a change is SERVED.

Note N2 additionally needs the **8** egress env vars, not just the 3 A/V ones — and the free
LiveKit tier only allows 60 recorded minutes/month.

### 2b. Genuinely needs new code

| Gap | Where | Why it's real | Rough size |
|---|---|---|---|
| **No late-join replay** of markup strokes or raised hands | `MeetingHub` mirrors live ops only | A tab joining mid-session sees only *subsequent* strokes. Needs a server-side markup buffer or a "request current state" hub round-trip. | S–M |
| **Mute/remove are client-honoured signals**, not server-enforced | M3 caveat; host authority over the *signal* is enforced by `HubTenantGuard.IsSessionHostAsync` | A modified client can ignore them. Real enforcement needs the LiveKit server SDK (`RoomService.MutePublishedTrack` / `RemoveParticipant`). We already have Twirp plumbing in `LiveKitEgressClient` calling `livekit.RoomService` — so this is an extension of an existing pattern, not new transport. | S |
| **Mobile parity** | `Planscape/app/meetings/live.tsx` (175 lines) — grepped: **zero** hits for chat/roster/markup/reaction/moderation/record | Native has A/V + surface-follow + co-presence only. M2/M3/M4 UIs are web-only. | **L** |
| **Issue raster is markup-on-white**, not composited over the document | sandboxed cross-origin iframe pixels can't be read into a canvas | Architectural; needs server-side composition or a same-origin document proxy. | M |
| **Clash `elementAGuid` may not be a federated IfcGuid** | M4 caveat | `selectAndZoom` silently no-ops when the guid doesn't match a loaded mesh. | M |
| **BCC (WPF) meetings tab** — recordings list + attendee dropdowns | tracked follow-up | Needs Revit to build. | M |

### 2c. An architectural wrinkle worth knowing before the demo

There are **three** web surfaces, not one, and the meetings work is split across two of them:

| Surface | Where | Meeting capability |
|---|---|---|
| `planscape-web` (Next.js, the Render service) | `planscape-web/` — 23 routes, ~4,800 lines | `meetings/[id]/live/page.tsx` (193 lines) has its **own minimal** LiveKit client: tiles + mic + cam + leave. Embeds `viewer.html?meeting=` in an iframe for everything else. |
| API-served `/app` dashboard | `wwwroot/js/dashboard.js` (2,579 lines) | Meetings list + recordings archive + player |
| API-served viewer | `wwwroot/viewer.html` + `coordination-viewer.js` (7,526) + `livekit-av.js` (1,046) + `meeting-sync.js` (840) | **All of M1–M5 / N1–N5 lives here** |

Consequence: the Next.js live page reaches markup/chat/roster **only through the iframe**. Both the
Next.js page and `livekit-av.js` inside the iframe are LiveKit clients using the **same identity**
(`identity = userId`, `MeetingRoomController.cs:355`). LiveKit disconnects the older connection on a
duplicate identity. The iframe URL passes `?meeting=` but **not** `?autojoin=1`, so `livekit-av.js`
should stay in its lobby and never connect — but that is the *only* thing preventing a collision,
and it is exactly the kind of thing a two-tab test will surface. **Flag it in the test script**: if
tab 2's video drops the instant someone clicks "Join A/V" inside the embedded viewer, this is why.

---

## 3. Corporate ACC-style UI shell — scoped, not built

### 3a. Prior art (searched, don't assume a blank slate)

`git log -i --grep` across all branches found ACC-style work, but **none of it is a nav shell**:

| Commit | What it actually was |
|---|---|
| `c1ace500b`, `b9b2ff74a`, `c96b640f2` | ACC-style **viewer navigation** — orbit pivot, dblclick focus, zoom-to-fit |
| `92dce45e5` (Phase 169) | ACC-style **project cards** + Mapbox location map — in `wwwroot/js/dashboard.js`, not Next.js |
| `bb99d1247` (Phase 48) | "Interactive corporate UI" for the **WPF** BIM Coordination Center |

No design doc, Figma reference, or component library exists. `grep -i "ACC"` across `docs/` returns
only unrelated hits (guides, CSV parameter data). So: **the interaction ideas have precedent, the
shell does not.**

### 3b. What exists to build on

`planscape-web` is small and unopinionated — which is good news for a reskin:

- **23 page routes**, ~4,800 lines total.
- **3 components**: `AppShell.tsx` (63 lines), `NotificationBell.tsx` (79), `RagBadge.tsx`.
- `AppShell` is a **top bar only** — logo, search, bell, tokens link, email, sign-out; content is
  `max-w-5xl` centred. There is **no left nav, no project switcher, no tenant switcher**.
- Stack: Next.js 14 App Router + Tailwind 3.4 + TypeScript. **No component library, no design
  tokens, no dark mode.**

### 3c. Proposed scope (recommendation — not started)

The honest framing: this is a **`planscape-web`-only** workstream. It should **not** touch the WPF
BIM Coordination Center (different platform, local-first, needs Revit to build) or the Expo app
(different idiom entirely — a phone doesn't get a left rail). Say that out loud early, because
"corporate UI everywhere" is how this becomes a three-month project.

| Slice | Contents | Est. |
|---|---|---|
| **U1 — design tokens** | Tailwind theme extension: colour ramp, spacing, radii, elevation, type scale. Light + dark. One `tokens.css`. | 0.5 d |
| **U2 — shell chrome** | Rewrite `AppShell`: fixed left rail (collapsible, icon+label), top bar with **project switcher** + **tenant switcher** (`/api/auth/tenants` + `/api/auth/switch-tenant` already exist — §4b), breadcrumb, search, bell, avatar menu. Content area becomes full-bleed. | 2 d |
| **U3 — primitives** | ~10 components the grids need: `DataGrid` (sort/filter/inline-edit/select), `Toolbar`, `Modal`, `Drawer`, `Tabs`, `Button`, `Input`, `Select`, `Badge`, `EmptyState`, `Skeleton`. This is the bulk of the work. | 4–5 d |
| **U4 — route migration** | Move all 23 routes onto the shell + primitives. Mostly mechanical; issues/clashes/documents/members/transmittals become real editable grids. | 4–5 d |
| **U5 — polish** | Responsive rail collapse, keyboard nav, focus rings, loading/error states, a11y pass. | 1–2 d |

**Total ≈ 12–15 focused days.** The risk is not difficulty — it is that "editable grids" is
open-ended. Pin the grid contract (which columns are editable, what saves optimistically, what
conflicts look like) *before* U3, or U3 and U4 both expand.

**Suggested decision to take before starting:** hand-roll the primitives on Tailwind, or adopt a
headless library (Radix / shadcn-style)? Adopting one cuts U3 roughly in half and improves a11y for
free; hand-rolling keeps the dependency surface at zero, which matches this repo's current posture
(the whole app has 5 runtime deps). **Recommendation: adopt headless primitives** — U3 is where the
schedule risk lives, and accessibility is not something to re-derive.

---

## 4. Two-firm multi-tenancy readiness

### 4a. What's genuinely good (verified in-repo)

This is stronger than the brief assumed.

- **104 of 112 entity classes implement `ITenantScoped`.** The DbContext applies a global tenant
  query filter + auto-stamp + auto-index to every one.
- **`TenantScopedEntityConventionTests`** fails the build if any entity carries a `TenantId`
  property but forgets the interface — a real regression guard, not a comment. (This is the test the
  brief remembered as `TenantIsolation*`; there is no file by that name.)
- **`HubTenantGuard`** (`Planscape.Infrastructure/SignalR/HubTenantGuard.cs`) closes the SignalR
  hole properly. Its docstring is explicit: a hub connection has no `HttpContext`, so the query
  filter resolves to an empty TenantId and **cannot be relied on** — it therefore reads `tenant_id`
  from the connection's own claims and queries with `IgnoreQueryFilters`. Three gates:
  `OwnsProjectAsync`, `OwnsSessionAsync`, `IsSessionHostAsync`.
- **Storage is tenant-prefixed**: `{StoragePath}/t_{tenantId}/{projectId}/{fileName}`, with
  `SaveScopedAsync` rejecting `Guid.Empty` (`LocalFileStorageService.cs:8-30`).
- **Multi-tenant switching already exists**: `GET /api/auth/tenants` (`AuthController.cs:708`) and
  `POST /api/auth/switch-tenant` (`AuthController.cs:747`).
- **LiveKit token minting is tenant-gated**: `LiveKitToken` calls `ProjectInTenant(projectId)` before
  it will sign anything (`MeetingRoomController.cs:340`).

### 4b. The three concrete gaps

**G1 — recordings are not tenant-namespaced in object storage.** `LiveKitEgressClient.StartAsync`
builds the key as:

```csharp
var key = $"{room}/{DateTime.UtcNow:yyyyMMddHHmmss}.{ext}";   // room == sessionId
```

Every other file in the system lands under `t_{tenantId}/…`; recordings land at the bucket root
keyed only by session GUID. Two firms' meeting recordings therefore interleave in one flat
namespace. Reads are still authorised through `MeetingRecording` (which *is* `ITenantScoped`), so
this is not an active leak — but it defeats bucket-level policy, per-tenant lifecycle rules,
per-tenant retention, and "delete this firm's data" as a single operation.
**Fix: prefix the key with `t_{tenantId}/`. Small change, do it before any real recording exists.**

**G2 — LiveKit rooms share one flat namespace across tenants.** `room = sessionId.ToString()`. With
one LiveKit Cloud project serving both firms, both firms' rooms sit in the same namespace and the
same dashboard/analytics view. A GUID is unguessable and the token gate is tenant-checked, so this
is defence-in-depth rather than a hole — but `t{tenantId}-{sessionId}` costs nothing and makes
per-tenant observability and any future per-tenant LiveKit project a rename instead of a migration.

**G3 — one tenant per user through the handoff.** `docs/PLANSCAPE_IDENTITY_HANDOFF.md` lists this
under "Deliberately out of scope": the .NET side supports switching, but D1 doesn't model
multi-tenancy, so the ticket carries exactly one tenant. **Correction to that doc's header** — it
says "design agreed 2026-07-18, **not yet implemented**", but `POST /api/auth/handoff/exchange`
exists at `AuthController.cs:994` and is covered by `HandoffProvisioningTests` +
`HandoffProvisioningSqliteTests`. It shipped; the doc header is stale.

For two *separate* firms this is fine — each firm is one D1 tenant, each user belongs to one firm.
It only bites when one human legitimately belongs to both firms (a consultant working for two
clients). Worth confirming with the user whether that case is real before building for it.

### 4c. What "two firms safely" concretely requires

| # | Item | Size |
|---|---|---|
| 1 | Prefix egress recording keys with `t_{tenantId}/` (G1) | XS |
| 2 | Namespace LiveKit rooms `t{tenantId}-{sessionId}` (G2) | XS |
| 3 | Add a `TenantIsolation` integration test class: firm-A user hits firm-B project/session/document/recording IDs directly → 404/403 on every route family. Today the guard is a *convention* test; this makes it a *behaviour* test. | M |
| 4 | Seed a second real tenant and run the matrix end-to-end (local Postgres already has 2 tenants / 10 projects) | S |
| 5 | Decide + document the dual-firm-membership question (G3) | Decision |
| 6 | Per-tenant quota/rate-limit review — one firm shouldn't exhaust the shared LiveKit participant-minute pool and lock the other out. **This is a live concern on the free tier's hard cap.** | S |

Billing separation is out of scope here — it lives Cloudflare-D1-side.

---

## 5. Sequencing recommendation

```
NOW, unblocked, ~1 hour of work:
  [A] LiveKit Cloud free tier → 3 env vars on planscape-api-free
      → the two-browser-tab camera test finally becomes runnable
      Cost £0. No card. Not blocked on anything.

IMMEDIATELY AFTER A (cheap, do while the context is hot):
  [B] G1 + G2 — tenant-prefix recording keys and LiveKit room names (XS + XS)

PARALLEL, independent of A:
  [C] ACC UI shell — U1→U5, ~12-15 days, planscape-web only
  [D] TenantIsolation behaviour test suite + second-tenant seed (items 3-4)

AFTER the two-tab test reports back:
  [E] Fix whatever the human test actually finds (unknowable until run)
  [F] Server-enforced mute/remove — small, extends existing Twirp plumbing
  [G] Late-join state replay

BLOCKED on the Render card:
  [H] render.yaml paid stack (~$54/mo) + LiveKit Ship ($50/mo) = production path
  [I] Anything needing the worker/converter (IFC→GLB, redaction, backups)
  [J] Persistent data — the free Postgres is time-limited; nothing real lives there yet

DEFERRED (large, own session):
  [K] Mobile parity for markup/chat/roster — the single largest remaining item
```

**Why A first:** it is the only item that converts a large body of SERVED-but-unproven work into
verified work, and it costs nothing. Everything in §2a has been sitting at "we think it works" for
months purely because no deployment had LiveKit keys.

**Why C can run in parallel:** the ACC shell touches `planscape-web` only; the meetings work lives
in `wwwroot/`. They do not collide. The one seam is `meetings/[id]/live/page.tsx` — settle §2c's
duplicate-identity question before U4 reworks that route.

**The card block is narrower than it looks.** It gates production capacity and background jobs. It
does **not** gate the camera demo, the UI shell, or the isolation hardening.

---

## 6. Open questions for the user

1. **The deployed free-tier URL is not recorded anywhere in the repo.** `render.free.yaml` says the
   `*.onrender.com` hostnames aren't known until first deploy, and no doc captured them afterwards.
   Needed to curl a SERVED proof against the deployment. (Worth committing to `docs/` once known.)
2. **LiveKit Cloud signup** creates an account and issues API keys — per the brief's own rule, that
   is an account-level action to confirm before doing. Free tier needs no card.
3. **Dual-firm membership** (§4b/G3): does one human ever need to belong to both firms?
4. **ACC grid contract**: which columns are editable, and is a headless primitive library acceptable
   (§3c)?

---

## Sources

- [LiveKit Cloud pricing](https://livekit.com/pricing)
- [LiveKit Cloud quotas and limits](https://docs.livekit.io/home/cloud/quotas-and-limits/)
- [LiveKit self-hosting deployment requirements](https://docs.livekit.io/transport/self-hosting/deployment/)
- [Render — Support UDP (open feature request)](https://feedback.render.com/features/p/support-udp)
- [Render community — WebRTC UDP ports](https://community.render.com/t/how-to-set-web-rtc-udp-ports-3478-and-1027-to-65535/22810)
- [Render community — WebRTC/UDP server possible?](https://render.discourse.group/t/webrtc-udp-server-possible-the-render-com-server-will-be-the-central-peer/4109)
- [Hetzner new CX plans](https://www.hetzner.com/pressroom/new-cx-plans/)
