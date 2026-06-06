# PROGRESS — viewer visualization

## MARATHON — close all web meeting gaps (W0→W6) · branch claude/optimistic-bell-EfjJw · no PR/merge
Resume rule: read this table + `git log`, continue from the first non-DONE item; never redo DONE.
Surfaces + SERVED gates per DEPLOY.md. `meetings-core.js` + `dashboard.js` are volume-mounted (restart, no build);
`index.html` is baked (needs `docker compose build`).

| Item | Status | Commit | SERVED proof / exact human test |
|---|---|---|---|
| W0 shared meetings-core.js | DONE-SERVED | 71cadfc54 | `curl /js/meetings-core.js \| grep "STING_MEETINGS_CORE_BUILD w0-core"`; index.html loads it |
| W1 web meeting authoring | DONE-SERVED | 48557abb3 | `curl /js/dashboard.js \| grep w1-authoring`. HUMAN(2-tab/role): as Host create meeting + add agenda/action/attendee + edit+save minutes + Generate doc; plain attendee sees only THEIR actions + read-only minutes; client read-only. Role-gating best-effort (server enforces). |
| W2 web join-live + live notifications | DONE-SERVED | marker w2-livejoin | `curl /js/dashboard.js \| grep w2-livejoin`. HUMAN(2-tab): acct A starts a live meeting → acct B (member) sees top 'Join' banner → click opens viewer ?meeting= (A/V). 🎥 Join on list rows + detail open the viewer. Starter excluded/membership-filtered server-side. |
| W3 web recordings via core | DONE-SERVED | marker w3-rec-core | `curl /js/dashboard.js \| grep w3-rec-core`. Recordings (detail tab + project archive + REC badge + Play/Download, Play gated to COMPLETE) all route through MeetingsCore.listRecordings/recordingsByMeeting. REST /recordings 200. HUMAN: Play opens HTML5 modal; FAILED rows show status. |
| W4 mobile re-point to core | TODO | — | tsc --noEmit clean |
| W5 role matrix (shared) | TODO | — | one role→cap map both surfaces (in meetings-core) |
| W6 DEPLOY.md + gates current | TODO | — | DEPLOY.md 4 surfaces |

Branch `claude/optimistic-bell-EfjJw` (PR #306 tracks it — do **not** open a new PR).
Every item below is committed + STEP-0 gated (SERVED marker + `livekit-av.js` 200 on a freshly
`--no-cache`-rebuilt container). The deployed bundle is the minified `dist/`, so the marker grep
on the *served* file is the proof.

## Design decisions (this round) — PENDING-HUMAN-VERIFY

Verify in a 2nd incognito tab at `localhost:5000` (sign in → open a model in a project that has
several models, e.g. 3×MBALWA + 2×Tendo). Console should log `STING_VIZ_FEDERATION <n> model
roots co-rendered`.

### Item 1 — VIZ SCOPE = ALL LOADED MODELS  ·  `e741cf2f9` · marker `F1-federation`
- [ ] On open, **all** project models render together (federated positions), not just the active one.
- [ ] **By discipline / by category** counts + the colour legend **aggregate across every loaded
      model** (numbers exceed a single model's).
- [ ] Colour-by / Isolate / Hide / transparency / a discipline toggle affect elements in **all**
      models, not just one.
- [ ] Model list checkboxes: unchecking a model **hides that whole model**; re-checking restores it
      with the **active appearance still applied**.
- [ ] Set a scheme, reload → it restores (now keyed per project, shared across the federation).
- [ ] Orbit / minimap / coordinate readout still correct with multiple models loaded.

### Item 2 — Fit frames only VISIBLE elements  ·  `51cb480cf` · marker `F2-fitvisible`
- [ ] Isolate (or hide) some elements, then **Fit** (or Home / F) → camera frames **only what's
      shown**, not the whole model.
- [ ] Hide everything (or toggle all models off) → **Fit** is a no-op + toast "Nothing visible to
      frame" (camera doesn't jump).
- [ ] Normal Fit on a full model still frames it correctly (FITFIX — no console error).

### Item 3 — coalesced live meeting broadcast  ·  `96f6fa461` · marker `F3-coalesce`
- [ ] Two clients in one meeting. Presenter **drags the ghost-opacity / transparency slider
      continuously** → follower tracks it **near-live**, no flicker / feedback loop.
- [ ] During the drag, SignalR isn't flooded (bounded to ~10 msg/s); the **final** state always
      lands on the follower.
- [ ] Switching colour scheme / isolate / render mode on the presenter mirrors to the follower.

## Caveats carried forward
- **E4 exporter** (materials/area/volume/quantity) is C# — unbuilt in this sandbox; verify a real
  Revit publish populates Properties → Materials / Quantities.
- Still flagged (not requested): select-all-matches clone cost (no cap); measure→section explicit
  teardown.

## Done earlier (already human-confirmed or gated)
A (layered model) · B (rows/deselect/persist) · E1 (dead-control regression) · 4 console fixes
(FITFIX / CMDFILTER / SignalR handlers / vendored SignalR) · C1–C6 · D audit · E2 (elevation) ·
E3 (section flip + save/restore) · E4 (rich properties + actions + cost) · E5 (audit).
Full per-item record + markers in `docs/VISUALIZE_AUDIT.md`.

## TRACK A — stability + correctness (this round) — PENDING-HUMAN-VERIFY

Verify on the 5-model federation in a 2nd incognito tab.

### BUG 1 — discipline classification  ·  `fc8b926da` · marker `A1-disc-keys`
- [ ] Colour-by-Discipline: **Lighting Fixtures / Electrical Fixtures show as Electrical (E)**, not P.
- [ ] **Shade-only Electrical** keeps ALL electrical (incl. lighting fixtures) shaded.
- [ ] **Shade-only Plumbing** does NOT shade any electrical.
- [ ] Spot every real category (Lighting/Electrical/Mechanical/Plumbing Fixtures, Conduits, Cable
      Trays, Sprinklers, Curtain Panels) → correct discipline.

### BUG 3 — isolate/hide/select key consistency  ·  `fc8b926da` · marker `A1-disc-keys`
- [ ] Search by DISC = "E" (or isolate/hide via search) finds the SAME elements colour-by-discipline
      shows (works on as-builts via derived discipline), across all 5 models.
- [ ] Isolate / Hide / Select act on the right elements across the whole federation (not "no matches").

### BUG 2 — deterministic / idempotent controls  ·  `1a05b097c` · marker `A2-determinism`
- [ ] Active colour-by / preset button shows **pressed** (blue).
- [ ] Click the active scheme again → clears to base (toggle off). Click A → B → A → identical to first A.
- [ ] Every button responds every time; switching never leaves residual state or a dead control.

## VIEWER COMBINED FIXES (this round) — PENDING-HUMAN-VERIFY (fresh InPrivate)

### V1 — ghost/x-ray click-through  ·  `ab2e5fe97` · marker `V1-pickthrough`
- [ ] Ghost the rest, click a solid element behind a ghosted wall → the **solid** selects, not the ghost.
- [ ] X-ray/ghost render mode: clicks pass through to solids. Isolate / colour-overridden stay pickable.
- [ ] Right-click context menu targets the solid behind a ghost too. Picking works across all 5 models.

### V2 — side-panel resize + collapse  ·  `311e46130` · marker `V2-panelresize`
- [ ] Drag each side panel's rail handle → panel resizes live (clamped); 3D reframes as space changes.
- [ ] Click (no drag) the handle → collapse/expand. Reload → BOTH width + collapsed state persist.

### V3 — SignalR long-session auth  ·  `c91d2d4e6` · marker `V3-signalr`
- [ ] After a long session (past JWT TTL): no `/hubs/notifications/negotiate 401`, no negotiate storm —
      one clean reconnect with a refreshed token.
- [ ] No "No client method 'joinedproject'/'presencechanged'" warnings on the viewer.

### V4 — federation load performance  ·  `0e92aa2b6` · marker `V4-loadperf`
- [ ] Loading the 5 models does NOT lock the UI — the primary is orbitable while siblings stream in.
- [ ] Steady-state orbit on the full federation stays ~30–50 fps (low-end target).

## TRACK B — MEETINGS (this round) — PENDING-HUMAN-VERIFY

Live meetings = **LiveKit media plane** (`livekit-av.js`) ⊥ **SignalR `MeetingHub` co-presence plane**
(`meeting-sync.js`). Built marathon-style, one slice per commit, each STEP-0 SERVED-proven
(`curl http://localhost:5000/livekit-av.js` = 200 + the served file greps the slice's
`STING_MEETING_BUILD <marker>`). The 2-tab live A/V test is the human's — every slice's exact steps
are in `docs/MEETINGS_AUDIT.md` as PENDING-HUMAN-VERIFY. Resume point = that file. PR #306; do not merge.

### M1-polish — explicit Join/Leave + in-meeting state  · marker `M1-polish`
- [ ] Load `?meeting=` → NO auto camera/mic prompt; "● In a meeting" pill = **Ready to join** + **▶ Join A/V**.
- [ ] Click Join → permission prompt fires (gesture-driven); pill → **● Live**; own tile appears.
- [ ] 2nd tab Join → both tabs show 2 tiles + audio. Leave → returns to Join lobby; re-join works.
- [ ] Deny camera → joins audio-only, control struck-through + toast; no crash.
- [ ] Token 501 (LiveKit down) → pill **A/V unavailable**, Join disabled; co-presence still works.

### M2 — collaborative document markup  · markers `M2-markup` (livekit-av + meeting-sync)
- [ ] Presenter shares a doc (📄) → both tabs show the same document (first-switch fix).
- [ ] Presenter draws pen/arrow/text/rect/highlight → appears live + aligned in the other tab.
- [ ] Clear → both clear. Colour switch works. Doc still scrolls with drawing off.
- [ ] 👥 Grant → non-presenter toolbar appears + can draw; off → hidden.
- [ ] 📸 Snapshot → saved (GET …/snapshots lists strokes). ⚑ Issue → OBS-NNNN with PNG attachment.
- [ ] New tab joining mid-session sees subsequent strokes (replay of prior = known M2.1 follow-up).

### M3 — conferencing essentials  · markers `M3-confer` (livekit-av + meeting-sync)
- [ ] Chat both ways (badge when closed). Reactions 👍/❤️ float in both tabs. ✋ shows in roster.
- [ ] Roster shows ★ host + (you); host-only sees ★/✖ per row + 🔇 mute-all.
- [ ] Make-host (★) moves host; mute-all mutes others; remove (✖) ejects a participant.
- [ ] Device picker ⚙ switches cam/mic/speaker. ▦/▭ + tile-click pin = speaker/gallery.
- [ ] 📶 low-bandwidth drops remote camera video (audio + screen-share stay); off restores.

### M4 — AEC functions  · marker `M4-aec` (meeting-sync) + link-meeting endpoint
- [ ] ⚑ issue from a picked element (+ auto viewpoint snapshot). 📸 viewpoint saved.
- [ ] ⧉ clash review steps clashes; camera flies + follower follows; ⚑→ promotes to issue.
- [ ] 📋 link/create formal meeting (button greens; other tab greens via RoomChanged).
- [ ] 📋 (linked) add action item; blank → generate minutes (MEETING_MINUTES doc record).

### N2 — meeting recording via LiveKit Egress  · markers `N2-recording` + server · FUNCTIONAL + PROVEN LOCALLY
Wired against in-stack MinIO and proven with a REAL recording (was 501-only). Configured livekit.yaml
(redis + webhook, replaces --dev) + egress + minio + createbuckets (default-on); room pre-create so egress
attaches; HMAC webhook finalises COMPLETE/FAILED; presigned http playback; audio-only toggle.
PROVEN end-to-end (demo media via `livekit-cli --publish-demo`, no browser in sandbox): start→200 ACTIVE →
egress PLAYING/active → stop → webhook COMPLETE, key `<session>/<ts>.mp4`, 5,738,917 B, 14.48 s; MinIO
lists the 5.5 MiB .mp4; presigned URL downloads http 200 video/mp4 (ISO MP4, playable); BCC live-artifacts
lists it with downloadUrl. Local dotnet build + mobile tsc 0 errors.
- [ ] PENDING-HUMAN-VERIFY (real webcams): 2 InPrivate tabs, cameras on → ⏺ → ● REC both → stop → captures real A/V. (Proof used a synthetic demo publisher.)
- PROD: swap MinIO→real S3 (EGRESS_S3_* + PublicEndpoint) + public LiveKit; secrets env-only. docs/MEETINGS_AUDIT.md → N2.

### N5 — BCC meetings ⇄ live meetings (one flow)  · server + mobile (no viewer marker)
Server FUNCTIONALLY VERIFIED via a logged-in REST run against the rebuilt container (create → live-session
idempotent → IN_PROGRESS → live-artifacts → end → COMPLETED + attendance flow-back). Mobile `tsc --noEmit`
0 errors. No new entities → no migration.
- POST /meetings/{id}/live-session (create-or-get, host, IN_PROGRESS); liveSessionId on list+detail.
- GET /meetings/{id}/live-artifacts (snapshots + attendance + sessions; recordings pending N2).
- end → roster→ATTENDED attendees + meeting COMPLETED.
- /app: ● LIVE badge + idempotent Join/Resume + Live-artifacts card.
- [ ] Device loop PENDING-HUMAN-VERIFY (docs/MEETINGS_AUDIT.md → N5): schedule → Join live → do stuff → end → BCC record shows attendance/snapshots/actions + minutes.

### N4 — flexible meeting ⇄ model layout  · marker `N4-layout` (meeting-sync + livekit-av)
SERVED-proven (both files 200 + `N4-layout`; `closeMeeting`/`cycleMeetLayout`/`sting:meetLayout` present).
- [ ] Drag the panel header → repositions + persists across reload.
- [ ] – minimises to header pill / restores; ✕ tears down LiveKit + SignalR + hides all overlays.
- [ ] ▦ cycles PiP → sidebar → theater; A/V bar follows; 3D reframes (sizeRenderer); choice persists.
- Deferred: grid-reflowing dock (shrink canvas column) — would touch app-shell grid / FITFIX / camera.

### N3 — document presentation: discoverable picker + drag-drop  · marker `N3-docs` (livekit-av)
SERVED-proven (`livekit-av.js` 200 + `N3-docs`; `openDocPicker` present; `/file`→`/download` fixed).
Real bug: the shared-doc surface fetched a non-existent `/documents/{id}/file` (→404, never rendered).
- [ ] Presenter 📄 → "Present a document" picker (searchable doc list), not a raw id prompt.
- [ ] Click a doc → both tabs show the SAME file on the DOCUMENT surface (the /download fix).
- [ ] Drag-drop / upload a local file → persisted to project docs, then shared to all.
- [ ] M2 markup still syncs on the shared doc; presenter-gated.

### N6 — expanded element properties panel  · marker `N6-properties` (coordination-viewer)
SERVED-proven (`curl localhost:5000/coordination-viewer.js` 200 + `N6-properties`; "Property sets" /
"Relationships" / "Instance parameters" labels present on the served **minified** bundle).
- [ ] Select an element whose element-map has IFC psets → a "Property sets" section appears with a
      sub-head per pset and its props (previously dropped entirely).
- [ ] Classification / type-params / instance-params nested groups render as their own sections.
- [ ] Relationships section shows host / assembly / room-space / IFC type+GUID / Revit id when present.
- [ ] `Filter properties…` narrows the new rows too; absent groups simply don't show; no double rows
      for quantities/cost/materials.

### N1 — multi-participant video + presence roster  · markers `N1-presence` (livekit-av + meeting-sync)
SERVED-proven (`curl localhost:5000/livekit-av.js` 200 + `N1-presence`; `meeting-sync.js` 200 +
`N1-presence`; `sting:avState`/`avSuffix` present on the served bundles). 2-tab live A/V is the human's
— full checklist in `docs/MEETINGS_AUDIT.md` → "N1 — multi-participant video + presence roster".
- [ ] Tab B joins the same `?meeting=` → B's video tile appears in A's strip (not just A's self-view).
- [ ] Camera-off remote shows an initials placeholder + name + 🚫, not a blank tile.
- [ ] Per-tile mic/cam badge tracks mute/unmute + camera on/off live; active-speaker keeps the border.
- [ ] B leaves → B's tile clears from A; roster A/V drops to 🕓 then the chip is removed on disconnect.
- [ ] Roster shows 📹/🎤/🔇/🔊/★/🕓 per person + "N online · M in call"; correlated by userId.

### M5 — meetings discovery audit  · docs-only (no served artifact)
- [ ] Cross-cutting matrix in docs/MEETINGS_AUDIT.md: start/join/leave/reconnect, 2+ participants,
      host handoff, surface switch under load, screen-share start/stop, mobile join (live.tsx),
      co-presence + A/V together, token expiry/refresh, tenant isolation.
- Slice index (all PR #306, do not merge): M1 `39f6a59b0` · M2 `a8a0d57ed` · M3 `bdb7564fb` ·
  M4 `a362c4582` · M5 docs.
- Known follow-ups: mobile parity for markup/chat/AEC; server-enforced mute/remove (LiveKit SDK);
  late-join replay of markup/hand state.
