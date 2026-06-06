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

## M2 — collaborative document markup on the shared DOCUMENT surface  ✅ served `M2-markup` · `a8a0d57ed`

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

---

## M3 — conferencing essentials  ✅ served `M3-confer` · `bdb7564fb`

The full conferencing layer. All co-presence (chat / reactions / hand / roster / moderation) flows
over `MeetingHub`; all media (device picker / layout / low-bandwidth) over LiveKit. No new transport.

**Server (`MeetingHub` + `HubTenantGuard`):**
- `BroadcastChat` → `ChatReceived`, `BroadcastReaction` → `ReactionReceived`, `BroadcastHand` →
  `HandChanged` (all to others-in-group).
- `MuteAll` + `RemoveParticipant` → `Moderation` — **host-gated** by new
  `HubTenantGuard.IsSessionHostAsync` (caller must be the session `HostUserId`). `RemoveParticipant`
  fans out to the GROUP carrying the target connection id; the matching client self-leaves (so the
  signal can't be aimed outside the session). The mute is a *request* the target self-applies on its
  LiveKit mic — the hub never touches media.

**Client — co-presence (`meeting-sync.js`):**
- **Chat:** 💬 toggles a chat panel (log + input, Enter to send); a closed-panel badge flags unread.
- **Reactions:** 👍 👏 ❤️ 😂 float up and broadcast.
- **Raise hand:** ✋ toggles a persistent hand state shown in the roster (✋ next to the name) for
  everyone.
- **Roster + roles:** the presence panel lists every participant with a **★ host** badge + "(you)";
  the host (resolved from session state / `RoomChanged`) sees per-participant **★ make-host** + **✖
  remove**, plus a global **🔇 mute-all** (host-only buttons hidden for non-hosts).
- **Host controls wire:** make-host → `POST …/host` (existing `SetHost`, broadcasts `RoomChanged`);
  remove → `RemoveParticipant` hub; mute-all → `MuteAll` hub (→ `sting:selfMute` → livekit-av mutes).

**Client — media (`livekit-av.js`):**
- **Device picker:** ⚙ opens a cam/mic/speaker selector (`enumerateDevices` →
  `room.switchActiveDevice(kind, id)`).
- **Speaker / gallery + pin:** ▦/▭ toggles layout; click a tile to **pin** (enlarge as the focus);
  speaker view auto-focuses the active speaker (unless pinned).
- **Low-bandwidth (audio-only):** 📶 stops publishing the local camera + unsubscribes remote **camera**
  video (audio + screen-share stay) for 3G / field; toggling back resubscribes. New camera tracks are
  dropped while low-bw is on.

**SERVED proof:** `curl /livekit-av.js` + `/meeting-sync.js` = 200; served bundles grep
`STING_MEETING_BUILD = "M3-confer"` + `STING_MEETINGSYNC_BUILD = "M3-confer"`; api `/health` 200
(MeetingHub + HubTenantGuard compiled). (Commit recorded below.)

**2-tab test — PENDING-HUMAN-VERIFY** (two InPrivate tabs Joined per M1; tab 1 = host):
- [ ] **Chat:** type in tab 1 → appears in tab 2's chat log (badge if its panel is closed) and vice
      versa.
- [ ] **Reactions:** click 👍/❤️ → the emoji floats up in BOTH tabs.
- [ ] **Raise hand:** tab 2 clicks ✋ → tab 1's roster shows "✋" next to tab 2's name + a toast; click
      again → it clears.
- [ ] **Roster/roles:** tab 1 (host) shows **★** on itself; both tabs show the other participant. Only
      the host tab shows ★/✖ per row + the 🔇 button.
- [ ] **Make host:** tab 1 clicks ★ on tab 2 → host moves to tab 2 (★ migrates; tab 2 now shows host
      controls, tab 1 loses them).
- [ ] **Mute all:** host clicks 🔇 → the other tab's mic mutes (🎤→🔇) + a toast.
- [ ] **Remove:** host clicks ✖ on a participant → that tab leaves the meeting (toast "You were
      removed") and drops from everyone's roster.
- [ ] **Device picker:** ⚙ lists real cameras/mics/speakers; choosing another camera switches the
      published video.
- [ ] **Speaker/gallery + pin:** ▦/▭ toggles; clicking a tile enlarges it (pin); speaker view enlarges
      whoever is talking.
- [ ] **Low-bandwidth:** 📶 on → remote camera tiles drop to audio-only (screen-share still shows);
      off → video returns.

**Caveats (M3):**
- **Mute-all / remove are self-applied signals**, not server-enforced LiveKit mutes (no LiveKit
  server-API/egress wired). A modified client could ignore them. Host authority over the *signal* is
  enforced (`IsSessionHostAsync`); enforcing the *media* needs the LiveKit server SDK (`RoomService.
  MutePublishedTrack` / `RemoveParticipant`) — a server-side follow-up.
- **Hand state isn't replayed to late joiners** (same as M2 markup) — a tab joining after a hand is
  raised sees it on the next toggle. Roster membership itself is live (join/leave events).
- **Speaker view enlarges within the strip** rather than a dedicated stage pane; a full stage layout
  is a visual refinement, not a transport change.

---

## M4 — AEC functions  ✅ served `M4-aec` · `a362c4582`

Turns the live meeting into a coordination tool. Reuses existing controllers (Issues, Clashes,
Meetings, Snapshots) + one new link endpoint; all UI hangs off the co-presence panel in
`meeting-sync.js` (it already owns the SignalR connection, the camera-follow + selection broadcast,
and the viewer `selectAndZoom` command channel).

**Server (`MeetingRoomController`):** new `POST …/meeting-sessions/{sid}/link-meeting {meetingId}`
sets `MeetingSession.MeetingId` (validates the meeting is in the project) and pushes `RoomChanged`
so every client learns the link. Everything else reuses existing endpoints.

**Client (`meeting-sync.js`) — an AEC tool row on the meeting panel:**
- **⚑ Raise issue** → `POST …/issues` (Type `OBS`, optional assignee email, links the **last picked
  element** as `ModelElementGuid`), then auto-captures a viewpoint snapshot named after the issue.
  (Selection is captured by extending the existing pick/pinTap bridge wrap — `state.lastPickGuid`.)
- **⧉ Clash review** → `GET …/clashes`, a ◀ ▶ stepper + "⚑→ promote". Stepping calls the viewer's
  `selectAndZoom` for the clash's `elementAGuid`; the presenter's camera moves and the **existing
  camera-follow mirrors it to every follower** (no new transport). "⚑→" = `POST …/clashes/{id}/
  promote-to-issue`.
- **📸 Viewpoint** → camera (`serializeCamera`) + highlighted element → a replayable `MeetingSnapshot`.
- **📋 Meeting** → if unlinked, create a `Meeting` + `link-meeting`; if linked, prompt to add a
  decision (`POST …/meetings/{id}/actions`) or, left blank, **generate minutes**
  (`POST …/meetings/{id}/export/minutes`). The button greens when a meeting is linked.

**SERVED proof:** `curl /meeting-sync.js` = 200; served bundle greps
`STING_MEETINGSYNC_BUILD = "M4-aec"`; api `/health` 200 (the new `link-meeting` endpoint compiled).
(Commit recorded below.)

**2-tab test — PENDING-HUMAN-VERIFY** (two tabs in a meeting; project has clashes + the user is a
project member so Issues/Meetings endpoints authorise):
- [ ] **Issue:** in the 3D model, click an element → click **⚑** → enter a title → toast "Issue
      OBS-NNNN created"; the issue exists with the element guid in its description + a viewpoint
      snapshot saved.
- [ ] **Clash review:** click **⧉** → the panel loads "Clash 1/N …"; **▶/◀** steps clashes and the
      camera flies to each; the **follower tab's camera follows** (Follow presenter on).
- [ ] **Promote:** on a clash, click **⚑→** → toast "Clash → CLASH-NNNN" (or "already an issue").
- [ ] **Viewpoint:** click **📸** → toast "Viewpoint saved"; `GET …/snapshots` lists it with the
      camera JSON.
- [ ] **Meeting link:** click **📋** (unlinked) → create a meeting → button greens, `GET …/meeting-
      sessions/{sid}` shows `meetingId` set, and the OTHER tab's button greens too (RoomChanged).
- [ ] **Action + minutes:** click **📋** (linked) → type a decision → toast "Action added"
      (`GET …/meetings/{id}` lists the action); click **📋** again, leave blank → toast "Minutes
      generated" (a MEETING_MINUTES DocumentRecord exists).

**Caveats (M4):**
- **Clash `elementAGuid` may be a clash-row identity, not a federated IfcGuid** (per the clash-job
  follow-up noted in the clash code) — `selectAndZoom` resolves only when the guid matches a loaded
  mesh's uniqueId. Camera-follow + promote-to-issue work regardless.
- **Issue links the element guid only, not ModelId** — `IssuesController` validates ModelId against
  `ProjectModels`, and a meeting session's model may be a `FederatedModel`; sending it would 400 the
  whole create, so we omit it and carry the guid (+ description) instead.
- **Minutes are a DocumentRecord** (the .docx is rendered server-side by the template engine when the
  `meeting_minutes.docx` template is present — same pattern as transmittals); the toast confirms the
  record, not a downloaded file.

---

## M5 — meetings discovery audit (cross-cutting matrix)

Docs-only slice — no served artifact (the per-feature SERVED proofs are recorded in M1–M4 above).
This is the **integration** matrix: scenarios that span slices and must hold together. Run it after
the per-slice checklists pass. Architecture invariant throughout: **LiveKit = media plane**
(camera/mic/screen), **SignalR `MeetingHub` = co-presence plane** (surface/markup/chat/roster/
moderation/state). No feature duplicates a transport; secrets are env-only.

### Setup (the canonical two-tab harness)
1. `cd Planscape.Server/docker && docker compose up -d` (api :5000, livekit :7880 dev, postgres,
   redis). Confirm `curl http://localhost:5000/health` = 200 and `…/livekit-token` mints (M1-1).
2. Sign in (so `localStorage.planscape_token` is set). Pick a project + published model; create a
   live session (BCC "Start meeting" or `POST …/meeting-sessions`). Note the session id.
3. Open **two InPrivate windows** at
   `http://localhost:5000/viewer.html?project=<pid>&model=<mid>&meeting=<sid>` — tab 1 signed in as
   the session **host**, tab 2 as another project member. (A 3rd tab tests late-join + non-member
   gating.)

### Discovery matrix — PENDING-HUMAN-VERIFY
- [ ] **Start / join:** both tabs auto-join co-presence ("Live meeting" panel, presence chips). A/V
      is gesture-gated — each tab shows **▶ Join A/V** until clicked (M1).
- [ ] **2+ participants:** after both Join A/V, each tab shows the other's tile + audio; the roster
      lists both with the ★ host badge on tab 1 (M1, M3).
- [ ] **Leave / rejoin:** tab 2 Leave → returns to the Join lobby, drops from tab 1's tiles; Join
      again → re-appears without a reload (M1).
- [ ] **Reconnect (network blip):** kill tab 2's network ~10 s then restore → SignalR auto-reconnects
      (re-`JoinSession`), the token-refresh path avoids a 401 storm (V3), and A/V re-establishes or
      falls back to the Join lobby. No ghost participant lingers in tab 1 (OnDisconnected fires
      ParticipantLeft).
- [ ] **Host handoff:** tab 1 makes tab 2 host (★) → ★ migrates, host controls move to tab 2, and the
      presenter-only surface/screen/grant affordances follow the host (M3 + RoomChanged).
- [ ] **Surface switch under load:** with both drawing markup + chatting, the host switches
      model → document → screen repeatedly → every tab stays in lock-step on the active surface; markup
      canvas shows only on `document`; no transport flooding (camera broadcast throttled, V/F3 coalesce).
- [ ] **Screen-share start/stop:** host shares a window → it renders in the central pane for both and
      the surface auto-follows to `screen`; stop → returns to `model` (WS3c/d).
- [ ] **Co-presence + A/V together:** camera-follow (move host camera → follower tracks), element
      highlight, section, clash-review camera-fly (M4) all work **while** A/V tiles + chat are live —
      the two planes don't interfere.
- [ ] **Mobile join (`Planscape/app/meetings/live.tsx`, native build):** a phone joins the same
      session → native LiveKit A/V + surface-follow + model co-presence. (Web-only for now: markup /
      chat / roster / AEC UI — mobile parity is a follow-up; see Caveats.)
- [ ] **Token expiry / refresh:** run a session past the JWT TTL → the meeting hub + livekit token
      paths refresh without a negotiate storm; the LiveKit token is minted for a 4 h TTL so a long
      meeting doesn't drop media mid-call.
- [ ] **Tenant isolation:** a user from another tenant who guesses the session GUID cannot join the
      hub group or mint a token (HubTenantGuard / controller tenant checks).

### Slice index (all on branch `claude/optimistic-bell-EfjJw`, PR #306 — do not merge)
| Slice | Marker | Commit | What |
|---|---|---|---|
| M1-1 | `M1-livekit` | (earlier) | `/livekit-token` 500→200 (raw HMACSHA256 JWT) |
| M1-2 | `M1-polish` | `39f6a59b0` | gesture-gated Join/Leave + in-meeting state + device prompts |
| M2 | `M2-markup` | `a8a0d57ed` | collaborative document markup (broadcast + Snapshot/Issue) |
| M3 | `M3-confer` | `bdb7564fb` | chat/reactions/roster+roles/host-controls/device-picker/views/low-bw |
| M4 | `M4-aec` | `a362c4582` | issue/clash-review/meeting-link/minutes/viewpoint |
| M5 | (docs) | — | this discovery matrix |

### Cross-cutting caveats / known follow-ups
- **Mobile parity:** `live.tsx` covers native A/V + surface-follow + co-presence; the M2 markup,
  M3 chat/roster/moderation, and M4 AEC UIs are **web-only** so far. Porting them to the RN app is
  the natural next phase (the hub methods + REST are already host-agnostic).
- **Server-enforced mute/remove:** M3 moderation is a host-gated *signal* the client self-applies;
  hard enforcement needs the LiveKit server SDK (`RoomService.MutePublishedTrack` / `RemoveParticipant`).
- **Late-join replay:** neither M2 markup strokes nor M3 raised-hands replay to a tab that joins
  after they happen (the hub mirrors live ops). A snapshot/full-state resync on join is the fix.
- **No 2-tab live A/V proof in this build pass:** every slice was SERVED-proven (container serves the
  exact bundle) but the live camera/mic/screen behaviour was **not** machine-verified — it requires
  two real browser tabs against the running LiveKit + Postgres stack. That is what this matrix is for.
