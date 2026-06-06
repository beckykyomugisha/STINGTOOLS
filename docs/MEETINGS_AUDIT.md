# Meetings (Track B) — audit & build log

### Recordings on the VANILLA web /app dashboard · marker `STING_DASH_BUILD recordings-web` · SERVED-verified
Mirrored the mobile recordings UI into the served web `/app` (`wwwroot/js/dashboard.js` + a nav-link in
`wwwroot/app/index.html`) — **not** the Expo app (which isn't web-exportable; see DEPLOY.md). `renderMeetings`
replaces the generic table: a **▶ REC** chip on rows that have a recording (one `GET /recordings` → Set of
meetingIds), and clicking a row opens a per-meeting **Recordings** modal (date·duration·size·status·▶Play·
⬇Download). New **🎬 Recordings** nav section (`renderRecordings`) lists ALL project recordings newest-first
(label/date/duration/size/status/Play/Download + **AD-HOC** chip). `▶ Play` opens an HTML5 `<video>`/`<audio>`
overlay on the presigned URL (gated to `COMPLETE`; FAILED/ACTIVE show status). Served proof:
`curl /js/dashboard.js` → marker + `renderRecordings`/`renderMeetings`/`openRecordingPlayer`/`case "recordings"`;
`curl /app/index.html` → `data-view="recordings"`. 2-tab PENDING-HUMAN-VERIFY.

**Web /app vs Expo parity audit (flags for what else needs porting to dashboard.js):**
- ❌ Live-meeting **JOIN / A/V** (LiveKit) — web-missing (Expo-only).
- ❌ Meeting **authoring** (create / minutes / agenda / attendees / actions) — web is a read-only list.
- ❌ Live-start notification + `?meeting=` **Join deep-link** + `MeetingScheduled` event — web has the SignalR
  notification infra (handles `MeetingCreated/Updated`) but these newer events are unwired.
- ✅ Notifications infra, meetings list, **recordings** (this change) are present on web.
See DEPLOY.md for the four-surface deploy matrix.

### Recordings — project archive view + ad-hoc coverage (P3/P4/P5) · Expo app · tsc-clean · PENDING-HUMAN-VERIFY
New **/recordings** screen lists EVERY project recording (newest first): label (meeting title, or
`Ad-hoc session · date · host` with an **AD-HOC** chip) · date/time · duration · size · status · ▶ Play ·
⬇ Download. Reachable from the dashboard via a new **🎬 Recordings** QuickAction next to Meetings. Consumes
`getProjectRecordings` (members-only + presigned, server-side). The in-app player + formatters were extracted
to a shared `src/components/RecordingPlayer.tsx` (video for mp4, **audio player for audio-only** egress) used
by both the meeting-detail section (P1) and this archive. `tsc --noEmit` clean across all files.
- Full loop: record in a meeting → appears in that meeting's Recordings section **and** the project archive →
  ▶ to play. In-browser play is PENDING-HUMAN after `expo export` redeploy; the server endpoint is REST-verified.

### Recordings — meetings-list indicator (P2) · Expo app · tsc-clean · 2-tab PENDING-HUMAN-VERIFY
Each meeting row shows a small **`▶ REC`** badge when that meeting has ≥1 recording (alongside the ● LIVE
badge). MeetingsScreen does one cheap `getProjectRecordings(projectId)` on load and builds a Set of
`meetingId`s that have a recording → passed to `MeetingRow hasRecording`. New `getProjectRecordings` +
`ProjectRecording` in endpoints.ts (shared with the archive view). `tsc --noEmit` clean; in-browser visual
PENDING-HUMAN after expo export.

### Recordings — meeting-detail section + in-app player (P1/P4/P5-audio) · Expo app · tsc-clean · 2-tab PENDING-HUMAN-VERIFY
Meeting detail (Overview) gets a dedicated **Recordings** card (the primary home), each row: 🎥/🎙 ·
date/time · duration · size · status · **▶ Play** · **⬇ Download** (Play gated to `COMPLETE` + a URL;
ACTIVE shows "recording…"). **▶ Play** opens an in-app `RecordingPlayerModal` — HTML5 `<video>` (mp4) /
`<audio>` (audio-only egress) on **web** via `React.createElement`, streaming the presigned URL; on native it
falls back to the device player via `Linking`. Pulls from `getMeetingLiveArtifacts(...).recordings`; the old
single-link render under "Live artifacts" was replaced. `tsc --noEmit` clean.
- NOTE: the web `/app` is the Expo export (not rebuilt in the API docker image), so the in-browser play/modal
  is **PENDING-HUMAN-VERIFY** after an `expo export` redeploy.

### Recordings — project archive endpoint · `GET /api/projects/{id}/recordings` · server REST-verified
Members-only (ProjectVisibility gate) project-level recordings archive (newest first), covering BOTH
scheduled-meeting recordings AND **ad-hoc** live-session recordings with no formal Meeting (labelled
`Ad-hoc session · {date} · {host}` so nothing is orphaned). Each row: id/sessionId/meetingId/kind/status/
fileSizeBytes/durationSeconds/startedAt/endedAt + `meetingTitle`/`label`/`adHoc` + a short-lived presigned
`downloadUrl` (6 h, only when a StorageKey exists). Reuses the existing `MeetingRecording` rows +
`LiveKitEgressClient.GetPresignedGetUrl` (no new transport). REST-verified on the demo project → 200, 5
recordings with labels + kind + status + presigned URLs. dotnet build 0 errors.

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

### N1 — multi-participant video + presence roster · marker `N1-presence` (livekit-av + meeting-sync) · PENDING-HUMAN-VERIFY
SERVED-proven only (container serves `N1-presence` on both bundles, HTTP 200, `sting:avState`/`avSuffix`
present). The live behaviour needs **two InPrivate tabs on the same `?meeting=<sessionId>`** against the
running LiveKit + Postgres stack — never claim it "works" from the served grep alone.

- [ ] **Remote tile populates on join:** tab A joins A/V; tab B opens the same `?meeting=` and Joins →
      A's `lkStrip` gains a SECOND tile for B (not just A's self-view) with B's name label.
- [ ] **Camera-off shows a tile, not nothing:** B joins with camera OFF (or denies camera) → A still
      sees B's tile as an initials placeholder + name + 🚫 cam badge (previously a blank/invisible tile —
      the "you only see yourself" symptom).
- [ ] **Per-tile mic/cam badges live:** B mutes mic → B's tile in A shows 🔇; unmute → 🎤. B turns camera
      off → tile flips to the initials placeholder + 🚫; on → live video returns. Active speaker keeps the
      green border.
- [ ] **Clear on leave:** B clicks Leave (or closes the tab) → B's tile disappears from A's strip and
      B's roster A/V status drops to 🕓 (online, not in call), then the chip is removed on full disconnect.
- [ ] **Roster A/V status:** the "Live meeting" panel roster shows each participant with status —
      📹 (cam on) · 🎤/🔇 (mic) · 🔊 (active speaker) · ★ (host) · 🕓 (online but not in A/V); the
      header reads "N online · M in call" and updates live on join/leave/mute.
- [ ] **Identity correlation:** the roster A/V status lands on the RIGHT person (LiveKit identity =
      userId = SignalR roster userId) — not mismatched or duplicated.

### N3 — document presentation: discoverable picker + drag-drop · marker `N3-docs` (livekit-av) · PENDING-HUMAN-VERIFY
**Real bug fixed:** the shared-document surface fetched `/api/projects/{pid}/documents/{docId}/file`,
but that route does not exist (the controller serves `/download`) — so the document **never rendered**.
N3 corrects the URL to `/download`, replaces the unusable `prompt("Document id")` with a real document
**picker** (lists project documents, searchable, click to share) and a **drag-drop / upload** zone that
persists a LOCAL file to the project document store first (so every participant can fetch it by id),
then broadcasts the surface. SERVED-proven (marker `N3-docs`, `openDocPicker` present, `/file`→`/download`).

- [ ] **Discoverable entry:** as presenter, the 📄 button opens a "Present a document" picker (not a raw
      id prompt) listing the project's documents with a search box.
- [ ] **Share an existing doc:** click a document → A and B both switch to the DOCUMENT surface showing
      the SAME file (the `/download` fix — previously blank/404).
- [ ] **Drag-drop a local file:** drag a PDF/image onto the picker's drop zone (or click → file chooser)
      → it uploads to the project docs, then both tabs show it on the shared surface.
- [ ] **Markup syncs (M2):** presenter draws on the shared doc → B sees the strokes live; Save-as-Issue /
      Save-as-Snapshot still persist.
- [ ] **Presenter-gated:** a non-presenter's 📄 / drop is refused ("Only the presenter can share").

### N4 — flexible meeting ⇄ model layout · marker `N4-layout` (meeting-sync + livekit-av) · PENDING-HUMAN-VERIFY
The "Live meeting" panel is now **movable** (drag the header), **minimisable** (– collapses the body to
the header pill), and **closeable** (✕ → leaves LiveKit media via `STING_LIVEKIT.leave()` + leaves the
SignalR room via `LeaveSession` + `conn.stop()` + hides every meeting overlay). A layout button (▦)
cycles **PiP → sidebar → theater**; the A/V bar repositions to match (sidebar docks bottom-right). The
mode + minimised flag + dragged position **persist** (`localStorage.planscape_meet_layout`) and each
change calls `STING_VIEWER.sizeRenderer()` (ortho-aware) so the 3D re-fits. **Deferred (regression
safety):** a true grid-reflowing dock that shrinks the canvas *column* (vs the overlay reposition done
here) — it would touch the app-shell grid / FITFIX / camera, so it's a separate follow-up.

- [ ] **Move:** drag the panel header → it repositions and stays put across a reload (persisted).
- [ ] **Minimise / restore:** – collapses to the header pill; the icon flips; restores on click; survives reload.
- [ ] **Close = full teardown:** ✕ → own A/V disconnects (tile gone for the other tab), you drop from the
      other tab's roster (SignalR LeaveSession), and ALL meeting overlays hide (panel + A/V bar + screen/doc).
- [ ] **Layout cycle:** ▦ cycles PiP → sidebar → theater; the A/V bar moves to bottom-right in sidebar and
      back to centre otherwise; the 3D doesn't end up mis-framed (sizeRenderer ran).
- [ ] **Persistence:** chosen layout/min/position restore on reopening the meeting.

### N5 — BCC meetings ⇄ live meetings (one flow) · server + mobile · FUNCTIONALLY VERIFIED (server) + tsc-clean (mobile)
Makes a scheduled BCC `Meeting` and a live `MeetingSession` one flow. **No new entities → no migration.**

**Server** (`MeetingsController` + `MeetingRoomController`):
- `POST /meetings/{id}/live-session` — create-or-get the ACTIVE session bound to the meeting
  (idempotent), make the caller host, flip the meeting to IN_PROGRESS on first start.
- `liveSessionId` added to the meeting **list + detail** DTOs (drives the in-progress badge + Join).
- `GET /meetings/{id}/live-artifacts` — viewpoint/markup snapshots + attendance (from the live roster)
  + linked sessions; recordings empty until N2 (Egress) lands.
- `POST /meeting-sessions/{id}/end` enhanced: when bound to a meeting, flow the live roster back as
  **ATTENDED** attendees and complete the meeting. (Actions already flow live via the M4 link; minutes
  via the existing `POST /meetings/{id}/export/minutes` after end.)

**Server functional proof** (logged-in REST run against the rebuilt container, demo tenant):
create meeting → `liveSessionId:null`/SCHEDULED → `live-session` ⇒ ACTIVE `isNew:true` + meeting
IN_PROGRESS → 2nd call ⇒ **same session, `isNew:false`** → detail `liveSessionId` matches →
live-artifacts `sessions:1, attendance:1 (host), snapshots:0, recordings:0` → `end` 204 ⇒ meeting
**COMPLETED**, **1 attendee (ATTENDED)**, `liveSessionId:null`, session **ENDED**. ✅ all pass.

**Mobile** (`/app/meetings/index.tsx` + `src/api/endpoints.ts` + `src/types/api.ts`): `● LIVE` badge on
rows with an active session; "Join live" now uses the idempotent `startLiveSession` (Resume when
in-progress); a **Live artifacts** card shows session/snapshot/attendance counts + attendees + recent
snapshots. `tsc --noEmit` → 0 errors.

PENDING-HUMAN-VERIFY (full loop on devices):
- [ ] Schedule a meeting in the /app Meetings page → tap **Join live** → the live viewer opens bound to
      this meeting; the row shows ● LIVE and the card shows ● IN PROGRESS.
- [ ] Do stuff live (capture a viewpoint, raise an action via the meeting link) → they appear under the
      meeting's **Live artifacts** / Actions.
- [ ] End the session → the BCC meeting flips to COMPLETED, attendance lists who joined, and
      `Export Minutes` produces the minutes doc. (Recording row appears once N2 is deployed.)

### N2 — meeting recording via LiveKit Egress · markers `N2-recording` (livekit-av + meeting-sync) · FUNCTIONAL + PROVEN LOCALLY (a real .mp4 was recorded)
Server-side recording of a live session to MinIO via LiveKit Egress, linked to the session + meeting
(flows into N5 live-artifacts). **Host-gated start/stop; a consent `● REC` indicator shows for everyone.**
**Now wired against the in-stack MinIO and proven with a real recording** (was 501-only before).

**What landed**
- Entity `MeetingRecording` (ITenantScoped) + table via `PatchDevSchemaAsync` CREATE TABLE + manual
  `dev-schema-patch.sql`. Hand-authored migration `20260607000000_MeetingRecordings` (repo convention)
  + the model snapshot block in the same commit (P3-2 forward-practice → no NEW drift).
- `LiveKitEgressClient` — Twirp `RoomService.CreateRoom` (pre-create so egress can attach) →
  `Egress.StartRoomCompositeEgress`/`StopEgress` with a raw-HMAC admin JWT
  (`roomRecord+roomCreate+roomList+roomAdmin`); S3 output per-request. Audio-only toggle
  (`{audioOnly:true}` → OGG track egress) for a lightweight audio archive. `GetPresignedGetUrl` returns
  a **browser-reachable** presigned GET (PublicEndpoint; scheme forced to match http MinIO). `IsConfigured`
  false ⇒ 501 (ships dark) — but configured-by-default locally now.
- `MeetingRoomController`: `POST /{sid}/recording/start` (host-gated, idempotent), `/stop`, `GET
  /{sid}/recording` (returns `downloadUrl`); broadcasts `RecordingChanged`. N5 `live-artifacts.recordings`
  returns rows **with presigned `downloadUrl`**.
- `LiveKitWebhookController` `POST /api/livekit/webhook` — HMAC-verified (HS256 + sha256 body claim);
  on `egress_ended` finalises the row → COMPLETE (only `EGRESS_COMPLETE`; aborted/failed → FAILED) +
  StorageKey / FileSizeBytes / DurationSeconds (ns→s). Matched by EgressId (IgnoreQueryFilters).
- Frontend: host-only ⏺ toggle + `● REC` consent indicator for everyone; mobile N5 artifacts card shows
  recordings with a tappable Play/download link.
- **Infra (docker-compose, default-on):** `livekit.yaml` replaces `--dev` (keys devkey:secret + redis +
  egress webhook); `egress` service (shares redis); `minio` (S3 API :9000 + console :9001) + a
  `createbuckets` one-shot (`recordings` bucket); api env `LiveKit__ServerUrl=http://livekit:7880` +
  `LiveKit__Egress__S3__*` → MinIO (Endpoint internal `http://minio:9000`, **PublicEndpoint**
  `http://localhost:9000` for presign). `egress` needs `cap_add: SYS_ADMIN` (headless chrome).

**PROVEN end-to-end here** (rebuilt stack; demo media published into the room via `livekit-cli
room join --publish-demo`, since the sandbox has no browser/camera):
- `recording/start` → **200 ACTIVE** (configured, no longer 501); egress pipeline **PLAYING →
  egress_active**.
- `stop` → webhook → **status COMPLETE**, key `<session>/<ts>.mp4`, **FileSizeBytes 5,738,917 (5.5 MB)**,
  **DurationSeconds 14.48**.
- MinIO `recordings` bucket lists the **5.5 MiB .mp4**; the presigned `downloadUrl`
  (`http://localhost:9000/recordings/<session>/<ts>.mp4?…`) downloads **http 200, 5,738,917 bytes,
  content-type video/mp4**, `file` → **ISO Media MP4 v2 (playable)**.
- BCC meeting `live-artifacts.recordings` → 1 row, COMPLETE, 5.5 MB, `downloadUrl` set (linked + playable
  in the meeting record).
- Object key proven: `recordings/<sessionId>/<yyyyMMddHHmmss>.mp4` (e.g. `…/20260606133310.mp4`).

**PROD swap:** replace MinIO with real S3 (set `EGRESS_S3_*` → bucket/keys/region + `PublicEndpoint` to a
public host/CDN) and run a public LiveKit (NOT `--dev`; rotate `LIVEKIT_*`). All secrets via `.env` only —
never commit. Empty `EGRESS_S3_*` ⇒ endpoints 501 (ships dark).

- [ ] PENDING-HUMAN-VERIFY (real browser path): 2 InPrivate tabs on one `?meeting=` with cameras/mics →
      host ⏺ → both tabs show `● REC` → stop → the recording captures the real participants' A/V (this
      proof used a synthetic demo publisher, not live webcams).

### Discoverability — labelled Record + Present buttons · marker `meet-discover` (livekit-av) · served-verified
The Record (host-only) + Present-document actions existed but were **bare icons** users couldn't find. Both
are now **labelled pills** in the in-meeting live bar (new `labelBtn` helper): `📄 Present` (open a project
doc / drag-drop / upload — everyone sees it) and `⏺ Record` (host-only; toggles to `⏹ Stop` while recording).
They appear on Join (in `lkLive`, shown by `showLiveControls`). Served proof: marker `meet-discover`,
`labelBtn` ×3, Record/Present labels present. **View ▾ menu confirmed on this fresh `--no-cache` build:**
`vRealistic` + `vClashMarkers` + `vIssueMarkers` + `vGlass` all present (1 each). PENDING-HUMAN-VERIFY (2 tabs):
host sees `⏺ Record`; presenter sees `📄 Present`; both legible.

### Live-meeting notifications · server (MeetingRoomController + MeetingsController) · FUNCTIONAL (server) · 2-account PENDING-HUMAN-VERIFY
When a live MeetingSession STARTS, project members are notified **in-app (SignalR) + push (FCM/APNs)** via
`INotificationService.NotifyUserAsync` (per-user prefs honoured, membership-filtered, **starter excluded**):
"{user} started a meeting — Join the live meeting in {project}" with data `{ type:"meeting_live",
meetingSessionId, projectId, deepLink:"?meeting=<id>" }`. Wired at BOTH session-create paths:
`MeetingRoomController.Create` (web auto-create) **and** `MeetingsController.StartOrJoinLiveSession` (BCC
"Join live", on `isNew`). Scheduled-meeting create also emits a lightweight
`NotifyProjectEventAsync("MeetingScheduled")` to the project group so the **dashboard surfaces it** (no
double-push — invitees already get the invite push). Best-effort (try/catch) — never breaks session creation.
Local `dotnet build` → 0 errors.
- [ ] PENDING-HUMAN-VERIFY (2 accounts): account A starts a live session in a shared project → account B (a
      member) gets the in-app notification + push with a Join link to `?meeting=<id>`; the starter (A) does NOT.

### Slice index (all on branch `claude/optimistic-bell-EfjJw`, PR #306 — do not merge)
| Slice | Marker | Commit | What |
|---|---|---|---|
| M1-1 | `M1-livekit` | (earlier) | `/livekit-token` 500→200 (raw HMACSHA256 JWT) |
| M1-2 | `M1-polish` | `39f6a59b0` | gesture-gated Join/Leave + in-meeting state + device prompts |
| M2 | `M2-markup` | `a8a0d57ed` | collaborative document markup (broadcast + Snapshot/Issue) |
| M3 | `M3-confer` | `bdb7564fb` | chat/reactions/roster+roles/host-controls/device-picker/views/low-bw |
| M4 | `M4-aec` | `a362c4582` | issue/clash-review/meeting-link/minutes/viewpoint |
| M5 | (docs) | — | this discovery matrix |
| N1 | `N1-presence` | (this commit) | remote video tiles populate on join / clear on leave · per-tile mic/cam badge + camera-off initials placeholder · live roster A/V status (online/in-call/cam/mic/presenter/away) correlated by userId |
| N3 | `N3-docs` | (this commit) | document presentation: fix `/file`→`/download` (surface never rendered) · discoverable doc picker (searchable list) · drag-drop / upload a local file → persisted then shared |
| N4 | `N4-layout` | (this commit) | meeting panel movable / minimisable / closeable (full LiveKit+SignalR teardown) · PiP/sidebar/theater layout modes + persistence + sizeRenderer reframe (grid-reflow dock deferred) |
| N5 | (server+mobile) | (this commit) | BCC⇄live one flow: `POST /meetings/{id}/live-session` (idempotent), `liveSessionId` on meeting DTOs, `GET /meetings/{id}/live-artifacts`, end→roster→attendees + COMPLETED · mobile LIVE badge + Resume + artifacts card. Server functionally verified; mobile tsc-clean. |
| N2 | `N2-recording` | (this commit) | LiveKit Egress recording — **FUNCTIONAL + proven locally** (real 5.5 MB .mp4 → MinIO `recordings`, COMPLETE via HMAC webhook, presigned http playback, linked in BCC live-artifacts). Configured livekit.yaml + egress + minio + createbuckets (default-on); room pre-create; audio-only toggle. Real-webcam 2-tab path PENDING-HUMAN-VERIFY. |

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
