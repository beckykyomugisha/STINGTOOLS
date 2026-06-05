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

### M1 remaining (client UX) — PENDING
Verify in two tabs that the existing `livekit-av.js` flow works now that tokens mint: Meet → Start
meeting reloads into `?meeting=`; connects; cam/mic prompt; own + 2nd participant tiles; screen-share
in the central pane; surface switching broadcasts. Add/confirm clear in-viewer Join/Leave + cam/mic/
screen controls + an "in a meeting" state. (Tracked below as it's implemented.)

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
