# Meetings (Track B) — audit & build log

LiveKit = **media plane** (camera/mic/screen). SignalR `MeetingHub` = **co-presence plane**
(surface/markup/chat/roster/state). Never duplicate transports. Secrets via env only.

Verify with **two InPrivate tabs** in one `?meeting=<id>` session.

---

## M1 — unblock + verify A/V

### M1-1 (real bug, fixed) — `/livekit-token` returned HTTP 500, not 501  ✅ served `M1-livekit`
The dev config was already correct (`LiveKit__Url=ws://localhost:7880`, `ApiKey=devkey`,
`ApiSecret=secret`; the `livekit --dev` container uses the same `devkey`/`secret`). The real
failure was the token **factory**: `LiveKitTokenFactory.Create` signed via Microsoft.IdentityModel
(`SymmetricSecurityKey` + `SigningCredentials`), which **rejects keys < 128 bits** (`IDX10653`).
LiveKit's dev secret `secret` is 6 bytes → every token request 500'd.

**Fix:** build + sign the LiveKit JWT with **raw `HMACSHA256`** (System.Security.Cryptography) —
no key-length minimum, and exactly what LiveKit validates with. Works for the dev secret AND any
production secret. **Proven end-to-end against the running stack:** login → create session → POST
`livekit-token` → **HTTP 200** with a valid HS256 JWT (`iss=devkey`, `video.room` = session,
host gets `camera/microphone/screen_share/screen_share_audio`, `url=ws://localhost:7880`).

Creds documented in `Planscape.Server/docker/.env.template` (LiveKit section): dev falls back to
`devkey`/`secret` + the local `--dev` server on `:7880`; staging/cloud set `LIVEKIT_*` in `.env`.

### M1-2 (client UX polish, slice) — explicit Join/Leave + in-meeting state  ✅ served `M1-polish` · `39f6a59b0`
`livekit-av.js` no longer auto-connects the media plane on page-load (an unprompted camera light is
hostile, and browsers gate `getUserMedia` behind a user gesture anyway). New flow:
- **Lobby on load:** an always-visible "● In a meeting" pill (dot + status text) + a prominent
  **▶ Join A/V** button. No device access until the user clicks Join.
- **Explicit Join:** click → connect to LiveKit → request **mic + camera independently** (a denial of
  one doesn't block the other; the button paints the REAL state + a toast names the missing device).
- **Visible controls (live):** 🎤 mic · 📹 cam · 🖥 share (presenter) · 🧊/📄 surface (presenter) · ✖ Leave.
- **Explicit Leave:** returns to the lobby (Join button back) instead of destroying the bar — re-join
  the same session without reloading. Unexpected media drop falls back to the same lobby.
- **State machine** drives the pill: `Ready to join → Connecting… → ● Live → Left/Reconnecting`,
  plus `A/V unavailable` when `/livekit-token` 501s (LiveKit unconfigured).
- `?autojoin=1` opt-in auto-joins (for embeds / automated harnesses).

Co-presence (`meeting-sync.js`, model camera-follow + presence) still auto-joins — it needs no devices,
so the "Live meeting" presence panel appears independently of the A/V Join state. **No C# change**;
JS-only. LiveKit stays the media plane, MeetingHub the co-presence plane.

**SERVED proof:** `docker compose build --no-cache api && up -d --force-recreate api`;
`curl -fsS -w '%{http_code}' http://localhost:5000/livekit-av.js` = 200; the served file greps
`STING_MEETING_BUILD M1-polish`. (Recorded at commit time below.)

**2-tab test — PENDING-HUMAN-VERIFY** (two InPrivate tabs, signed in, same
`viewer.html?project=<pid>&model=<mid>&meeting=<sessionId>`; host = the session creator):
- [ ] On load, **no camera/mic prompt fires automatically**; both tabs show the "● In a meeting" pill
      reading **Ready to join** + a green **▶ Join A/V** button.
- [ ] Click **Join A/V** in tab 1 → the **browser's camera/mic permission prompt appears** (the gesture
      drove it); allow → pill flips to **● Live**, the Join button is replaced by the mic/cam/leave row,
      and your own tile appears in the strip.
- [ ] Click **Join A/V** in tab 2 + allow → within ~1–2 s **each tab shows the other participant's
      tile** (2 tiles total) and hears audio.
- [ ] **Deny** the prompt in a 3rd tab (or block the camera) → it still joins audio if mic allowed; the
      denied control shows struck-through "off" + a "Camera/Microphone unavailable" toast (no crash).
- [ ] Toggle 🎤 / 📹 → the control paints on/off and the peer sees the track stop/start.
- [ ] Click **✖ Leave** in tab 2 → tab 2 returns to the **▶ Join A/V** lobby (pill: **Left — Join to
      rejoin**); tab 1 drops tab 2's tile. Click **Join A/V** again → re-joins without reload.
- [ ] Host only: 🖥 / 🧊 / 📄 controls are present (presenter); a non-host tab does **not** show 🖥.
- [ ] If LiveKit is down/unconfigured (token 501): pill reads **A/V unavailable**, Join is disabled;
      the model co-presence panel still works.

---

## Track B scaffolding assessment (what exists vs net-new) — as of M1

Surveyed `MeetingHub.cs`, `meeting-sync.js`, `livekit-av.js`, `MeetingsController.cs` to scope
M2–M5 accurately (not claim un-built work as done).

**Present today (build on these):**
- **Media plane (M1):** `livekit-av.js` — connect, cam/mic/screen toggles, leave, participant
  tiles, screen-share → central pane, auto-connect on `?meeting=`. **Now unblocked** by the token
  fix. Control bar (`#lkBar`) has cam/mic/screen/leave.
- **Co-presence plane:** `MeetingHub` exposes `JoinSession`/`LeaveSession`/`BroadcastCamera`/
  `BroadcastHighlight`/`BroadcastOverlay`/`BroadcastSection`/`BroadcastSurface`; `meeting-sync.js`
  receives `ParticipantJoined/Left`, `CameraMoved`, `OverlayChanged`, `SectionChanged`,
  `HighlightChanged`, `RoomChanged`, **`SurfaceChanged`**. So **surface switching (model/document/
  screen) already broadcasts** (M1/M2a transport).
- Meeting record + session: `MeetingsController` (formal Meeting) + `MeetingRoomController`
  (live MeetingSession, participants, join/leave/host, set-surface, bind-model).

**Net-new for M2–M5 (NOT yet built — honest status):**
- **M2** document surface + **collaborative markup**: a `document` surface render (PDF/image/sheet
  + drag-drop upload), a markup canvas overlay, a new `MeetingHub.BroadcastDocMarkup` + client
  handler, and `MeetingSnapshot` persistence + "save markup as Issue/Snapshot". (Surface *switching*
  exists; the document *renderer* + *markup* do not.)
- **M3** chat, raise-hand + reactions, roster with roles + host controls (make-presenter/mute-all/
  remove), device picker, speaker/gallery + pin, low-bandwidth (audio-only) mode. (Roster exists as
  participants; the rest is net-new — each needs a hub method + client UI.)
- **M4** in-meeting issue (assign/due/link), clash-review mode (step + camera-follow), link live
  session ↔ formal Meeting (agenda/actions/minutes), viewpoint/snapshot → issue/minutes.
- **M5** the two-tab discovery matrix.

**Recommendation:** M1 (the token unblock) is the foundational fix and is done + verified. M2–M5
are each a focused feature pass (hub method + client UI + two-tab human verification) — best built
and verified one at a time rather than batched. Constraints to hold throughout: LiveKit = media,
MeetingHub = co-presence/markup/chat/state (no duplicate transports); secrets via env only.

---

## M2 — collaborative document markup on the shared DOCUMENT surface  ✅ served `M2-markup`

The headline net-new feature. Surface *switching* (model | document | screen) already existed; the
document *renderer* existed (an auth-fetched blob in a sandboxed iframe). M2 adds the **markup** —
pen / arrow / text / rectangle / highlighter strokes drawn on a canvas that overlays the document,
broadcast live to every participant, plus durable capture.

**Wire (co-presence, NOT LiveKit):** new `MeetingHub.BroadcastDocMarkup(sessionId, markup)` →
`DocMarkupChanged` to the OTHER participants. One op per message: `{op:"add",stroke}` ·
`{op:"clear"}` · `{op:"grant",on}`. `meeting-sync.js` relays it as a `sting:docMarkupChanged`
event + exposes `STING_MEETING.broadcastDocMarkup`. `livekit-av.js` renders.

**Canvas (`livekit-av.js`):** a `<canvas>` overlays the doc iframe 1:1 inside a relative stage.
Strokes store **normalised 0..1 coords** so they line up across clients of any pane size. A markup
toolbar (tools · 6 colours · ✏️ draw-mode toggle · 🗑 Clear · 📸 Snapshot · ⚑ Issue · 👥 Grant) shows
only on the document surface and only when markup is allowed. **Draw-mode toggle** flips the canvas
`pointer-events` so the iframe stays scrollable when not drawing.

**Presenter-led + host grant:** by default only the presenter (host) may draw. The presenter's
👥 **Grant** broadcasts `{op:"grant",on}` so every participant's toolbar enables/disables — one
transport, no extra state. (Client-side gate; the host owns the surface — markup isn't a security
boundary.)

**Persistence (durable — separate REST, not the hub):**
- **📸 Snapshot** → `POST …/meeting-sessions/{sid}/snapshots` with `StateJson =
  {surface,documentId,strokes}` (replayable via the existing `MeetingSnapshot`).
- **⚑ Issue** → rasterise the markup (white bg + strokes; the cross-origin sandboxed iframe's
  pixels can't be read into a canvas, so the document id rides in the issue description) →
  `POST …/issues` (Type `OBS`) → `POST …/issues/{id}/attachments` (the PNG). Reuses the existing
  `IssuesController` — no server change beyond the one hub method.

**Latent bug fixed in passing:** the first switch to the document surface set `display` on a
`getElementById` that returned null (pane not yet created), leaving the doc permanently hidden.
`applySurface` now ensures the pane, then shows it.

**SERVED proof:** `curl /livekit-av.js` = 200; served bundle greps `STING_MEETING_BUILD = "M2-markup"`
and `STING_MEETINGSYNC_BUILD = "M2-markup"` (meeting-sync). (Commit recorded below.)

**2-tab test — PENDING-HUMAN-VERIFY** (two InPrivate tabs, both **Joined A/V** per M1; tab 1 = host/
presenter; the project must have a document — note its id):
- [ ] Presenter clicks **📄** (share document) → enters a document id → both tabs switch to the
      **document surface** and render the same PDF/image. (This also proves the first-switch fix.)
- [ ] Presenter's **markup toolbar** is visible; a non-presenter tab shows **no toolbar** (gated).
- [ ] Presenter clicks **✏️ Markup** (drawing on) → draws a **pen** scribble, an **arrow**, a **rect**,
      a **highlight**, and a **text** note → **each appears in the other tab within ~1 s**, aligned to
      the same spot on the document (normalised coords).
- [ ] Switch colour → new strokes use it. **🗑 Clear** → both tabs clear.
- [ ] Presenter clicks **👥 Grant** → the non-presenter tab's toolbar **appears** + a toast; that tab
      can now draw and the presenter sees it. Grant off → the tab's toolbar disappears.
- [ ] With drawing **off**, the document still **scrolls** (canvas doesn't eat pointer events).
- [ ] **📸 Snapshot** → toast "Snapshot saved"; `GET …/snapshots` lists it with the strokes JSON.
- [ ] **⚑ Issue** → enter a title → toast "Issue OBS-NNNN created"; the issue exists with a PNG
      attachment of the markup + the document id in its description.
- [ ] A 3rd tab that joins mid-session sees NEW strokes drawn after it joined (live). (Replay of
      strokes drawn *before* it joined is a known follow-up — see Caveats.)

**Caveats (M2):**
- **No stroke replay on late join** — the hub mirrors live ops; a participant who joins after strokes
  are drawn sees only subsequent ones (and any Snapshot/Issue capture). A full-state resync on join
  is the natural M2.1 (server-side markup buffer or a "request current markup" hub round-trip).
- **Issue raster is markup-on-white**, not a composite over the document pixels (sandboxed
  cross-origin iframe can't be drawn to a canvas). The document id in the description is the bridge.
