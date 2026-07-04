# Smoke Test — Electrical Conduit Sleeve Pipeline (PR #386)

One-page, live-Revit smoke test for the sleeve → drop → connect pipeline
(Phase 196–198 on branch `claude/conduit-auto-routing`). Nothing in this PR has
been exercised in a live model; this is the gate before trusting it on a project.

**Setup** — Build Release, copy `StingTools.dll` + `data/` to `STING_PLACEMENT_GOLD`,
reopen Revit 2025. New metric model: levels L00 = 0, L01 = 3000; 2–3 **bounded rooms**
on L00; a **cable tray** run ~2700 mm above them (containment); ensure a Conduit Type,
a Cable Tray Type and one socket family type are loaded. Invoke tags via the
**Workflow picker → "Electrical Rough-In Pipeline"**, dock buttons, or the `stingtools`
MCP `run_command` tool.

| # | Action | Expect / PASS gate |
|---|--------|--------------------|
| 1 | **Seeds_Build** | `STING_SEED_ElectricalFixture` built; a type shows a **+Z conduit connector** |
| 2 | Select rooms → **Placement_PlaceFixtures** → Place now | Sockets land on walls; **fixtures stay selected** |
| 3 | **Routing_PlaceSleeveConnectorsAuto** | **No-op**; log: `placed=0 alreadyRoutable=N` (seed already routable) |
| 4 | **Routing_AutoDrop** | Conduit rises to tray; top connector **bonded** (solid dot) |
| 5 | **Validation_RunAll** | Note **CONN.OPEN** — 0 on routed fixtures |
| 6 | Swap 3 fixtures to a **connector-less** vendor family (or place generic no-connector sockets), select them → **Routing_PlaceSleeveConnectors** → Place | Preview "N would place"; each stub = **`ELC_CDT_INSTALL_METHOD_TXT = STING_SLEEVE_STUB:<id>`**, 20×150 mm, +Z |
| 7 | Re-run step 6 | Preview "**0 would place, N already sleeved**" → idempotent |
| 8 | **Routing_AutoDrop** on them | Stub extends to tray, bonded |
| 9 | **Dense**: 2 connector-less fixtures ~30 mm apart → sleeve | **2 distinct** stubs (different `:<id>`), not shared |
| 10 | **Floor box** (FLOOR_BOX, mount = 0) → sleeve | Stub **drops −Z** |
| 11 | *(opt)* off-list connector size → **Validation_RunAll** | **SIZE.MISMATCH** flagged |
| 12 | Re-run whole workflow | No duplicate stubs; **every step OK** (no FAILED) |

**Fill:** ☐1 ☐2 ☐3 ☐4 ☐5 ☐6 ☐7 ☐8 ☐9 ☐10 ☐11 ☐12

**Not-failures (expected behaviour):**
- The stub is *not* bonded to the connector-less fixture (by design — it provides the
  routable terminal; bonding is stub ↔ drop ↔ tray).
- AutoDrop needs containment within **3000 mm** or it correctly leaves the fixture open
  and warns.
- An *auto*-sleeve failure logs to `StingTools.log` but reports workflow **OK** (so it
  never blocks the pipeline) — check the log even on a green run.

**Overall PASS** = gates at step 4 (seed bonded), 7 (idempotent), 8 (stub bonded),
9 (dense fixtures), and 12 (all-OK) all green.
