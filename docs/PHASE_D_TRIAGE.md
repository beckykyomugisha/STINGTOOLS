# Phase D — Triage Sweep

**Branch:** `audit/phase-d-triage` (off `origin/main` @ `f011ac092`).
**Status:** READ-ONLY audit. NO code touched. One doc commit only.

Status legend: **CLOSED** (already fixed) · **OBSOLETE** (the feature no
longer exists) · **OPEN-SMALL** (< 0.5d) · **OPEN-MEDIUM** (0.5–2d) ·
**OPEN-LARGE** (> 2d, may need own phase) · **BLOCKED** (needs external
decision).

Cross-references: `PHASE3_NOTES.md`, `HEALTHCARE_WIRING.md`,
`MISWIRE_AUDIT.md`, `ORPHANS_AUDIT.md`, `DEDUPE_AUDIT.md`,
`docs/UI_PHASE_B_PATTERNS.md`.

---

## Bucket 1 — Group 3 build TODOs

Source: `MISWIRE_AUDIT.md` § "Flagged as BUILD TODOs" + § "NEW cluster
found during implementation" + `PHASE3_NOTES.md` Group-3 list.

| Item | Status | Evidence | Recommendation |
|---|---|---|---|
| `AssignIssues` ("Assign Issues" / "Reassign") → should route to `UpdateIssueCommand`; currently runs `RaiseIssueCommand` | **OPEN-SMALL** | `StingTools/UI/StingCommandHandler.cs:3769` (`case "AssignIssues": RunCommand<BIMManager.RaiseIssueCommand>(app)`) + `StingTools/UI/IssueTrackerDashboard.cs:475` + `StingTools/UI/BIMCoordinationCenter.cs:3153` (still dispatch `AssignIssues`). `UpdateIssueCommand` exists and is reachable via tag `UpdateIssue` (`StingCommandHandler.cs:1971`). Phase B BIM redesign did NOT touch this wiring. | Rewire 2 switch cases + 1 BimCommandModule registration → `UpdateIssueCommand`. ~30 min. |
| `CreateIssuesFromWarnings` ("From Warnings") → should auto-create NCR/SI from Revit warnings; currently runs `RaiseIssueCommand` | **OPEN-MEDIUM** | `StingTools/UI/StingCommandHandler.cs:3770` + `StingTools/UI/BIMCoordinationCenter.cs:2683` (label "From Warnings"). No `WarningsToIssue` command exists in tree — needs new command iterating `WarningsManager` records → `BimIssue` creation. | Build a new `BIMManager.CreateIssuesFromWarningsCommand` that reads `WarningsManager.ActiveWarnings`, classifies by severity, and creates `BimIssue` entries with cross-refs. ~1–1.5d. |
| `QRCodeCommand` parameterisation (sheet vs per-tag vs dashboard-link variants) | **OPEN-MEDIUM** | `StingTools/Tags/QRCodeCommand.cs:23` — single-variant command, no `Tag`/`Mode` arg in `Execute`. Still dispatched verbatim by `GenerateQRCode`, `GenerateQRSheet`, `PlanscapeQR` (`StingCommandHandler.cs:3747-3832`). `BIMCoordinationCenter.cs:4264` button labelled "🔗 QR Code" tooltip still cites MISWIRE_AUDIT cluster E TODO. | Add `QRMode` enum (Element / SheetRegister / DashboardLink) + `SetExtraParam("QR.Mode", …)` reads. Three variants then become 3 distinct entry points honouring their labels. ~1d. |
| `DrawingTypeEditorDialog` 6-button viewport-align row (`VPAlignTop/MidY/Bot/Left/MidX/Right` all fall through to one `AlignViewportsCommand`) | **OPEN-SMALL** | `StingTools/UI/DrawingTypeEditorDialog.cs:1489-1494` (6 distinctly-labelled buttons) + `StingCommandHandler.cs:1644-1649` (all 6 fall through to `RunCommand<Docs.AlignViewportsCommand>`). Was flagged a "self-consistent misleading set" in MISWIRE_AUDIT. | Two options: (a) collapse the 6 into one "Align viewports…" button (0.5h) OR (b) parameterise `AlignViewportsCommand` with `Edge` enum + `SetExtraParam("VPAlign.Edge", "Top|MidY|Bot|Left|MidX|Right")` (~3h). User preference needed. |

**Counts:** CLOSED 0 · OBSOLETE 0 · OPEN-SMALL 2 · OPEN-MEDIUM 2 ·
OPEN-LARGE 0 · BLOCKED 0.

---

## Bucket 2 — Healthcare TODOs

Source: `HEALTHCARE_WIRING.md` § "Outstanding TODOs" + Phase 185/186
caveats in `CLAUDE.md`.

| Item | Status | Evidence | Recommendation |
|---|---|---|---|
| `MgasVerifyCommand` to honour `HcOptions.MgasStep` (today runs full NFPA 99 §5.1.12 verify, ignoring per-step selection) | **OPEN-SMALL** | `StingTools/Commands/MedGas/MgasVerifyCommand.cs` — grep for `MgasStep` / `Hc.Mgas.Step` / `Step` returns NOTHING. `HcOptions` exposes the field (`HEALTHCARE_WIRING.md` line 14). Button is already labelled "MGAS Verify (Full)" (relabel landed; param honour did not). | Add `var step = HcOptions.MgasStep` near the command top, gate the 12-step loop to run only the named step when set, surface a tagged-result panel saying "Step X/12 verified". ~2–3h. |
| Chunked / async cancel pipeline for long-running Healthcare validators | **OPEN-LARGE** | `StingTools/Core/Validation/Healthcare/RunAllHealthcareValidators.cs:51` + `RunSelectedHealthcareValidators.cs:52` poll `HcOptions.CancelRequested` at step boundaries (Phase HEALTHCARE_WIRING.md item 2 — landed). No async/chunked refactor exists. Search for `async Task` / `ChunkedRun` under `Commands/Healthcare/` returns NOTHING. As HEALTHCARE_WIRING.md line 130 explains: dock-panel dispatch is synchronous on Revit API thread → Cancel click only processed AFTER current command returns. | True mid-run cancel needs the whole healthcare dispatch path refactored to chunked/async (small sub-transactions per validator, with `Task.Delay(0)` or `Dispatcher.DoEvents`-style pumps between chunks). This is invasive — touches `StingCommandHandler` dispatch model, every healthcare validator's `Execute`, and the result-strip update flow. Recommend dedicated Phase E. ~3–5d. |
| Healthcare Pset bundle authoring (5 Psets referenced in bSDD plan but not authored — Phase 186 caveat 6) | **OPEN-MEDIUM** | `CLAUDE.md` Phase 186 caveat 6 explicitly lists `Pset_StingHealthcareClinical/MGS/Radiation/ClinicalEquipment/Ligature` as missing. `shared/ifc/` directory check would confirm; not a UI-cleanup-campaign item but appears in the broader Healthcare TODO list. | Out of scope for UI cleanup. Track separately under Substrate phase. |
| Dock-panel Healthcare TAB itself (Phase 185 caveat 6: "No dedicated Healthcare tab in the dock panel") | **CLOSED** | Healthcare tab now lives in main `StingDockPanel.xaml` (referenced in `HEALTHCARE_WIRING.md` line 11 — `StingDockPanel.HealthcareTab.cs:SetHealthcareOptions`). Cancel + RDS + RadCalc + MGAS verify buttons all exist and dispatch correctly per HEALTHCARE_WIRING.md "IMPLEMENTED" section. | No action. |
| `RAD_QE_NAME_TXT` mandatory sign-off before radiation calcs treated as authoritative (Phase 185 caveat 4) | **OPEN-SMALL** | `CLAUDE.md` caveat verbatim; not contradicted by any later phase note. Search for `RAD_QE_NAME_TXT` validation gate in radiation commands would confirm coverage. | Add a guard at top of `RadCalcChestRoomCommand` / `RadCalcCtRoomCommand` / `RadCalcLinacVaultCommand` that reads `RAD_QE_NAME_TXT` on `ProjectInformation` and warns/blocks when empty. ~2h. |

**Counts:** CLOSED 1 · OBSOLETE 0 · OPEN-SMALL 2 · OPEN-MEDIUM 1 ·
OPEN-LARGE 1 · BLOCKED 0.

---

## Bucket 3 — Material surfaces consolidation

Source: `DEDUPE_AUDIT.md` Group 4 + `CLAUDE.md` "1 Material Manager
(7 tabs)" reference.

### Current state — three surfaces, three different code paths

| # | Surface | Entry point | Command class | Behaviour | XAML/UI count |
|---|---|---|---|---|---|
| 1 | Modal quick TaskDialog | tag `MaterialManager` | `StingTools/Temp/MaterialCommands.cs:1374` `StingMaterialManagerCommand` | Opens `MaterialManagerDialog.Show(uidoc)` — 4-option TaskDialog wrapper (Browse / Apply / Export / Create BLE / Create MEP) | **0 dock buttons** (orphaned — no `Tag="MaterialManager"` anywhere in `StingDockPanel.xaml`). Dispatch case still live at `StingCommandHandler.cs:1266`. |
| 2 | Full modal 7-tab WPF dialog | tag `MaterialManagerFull` | `StingTools/Temp/MaterialManagerDialog.cs:31` `MaterialManagerCommand` | Full 7-tab modal `MaterialManagerDialog` (Browse / BLE Create / MEP Create / Apply / Edit / Audit / Export) | **1 dock button** — `StingDockPanel.xaml:1917` "★ Material Manager" (BlueBtn). Dispatch at `StingCommandHandler.cs:1267`. |
| 3 | Dockable Material Hub pane | tag `ToggleMaterialHub` | `StingTools/Core/StingToolsApp.cs:2387` `ToggleMaterialHubCommand` → `MaterialHubPanel.xaml` (Phase C migrated to shared StingButtonStyles in commit `f011ac092`) | Full dockable panel — 7 tabs per `CLAUDE.md` "Material Manager (7 tabs)" heading; provider in `MaterialHubProvider.cs`; flyouts + texture browser etc. | **Ribbon button only** (`StingToolsApp.cs:2079` "STING Material Hub"). No dock-panel button for `ToggleMaterialHub`. |

### Findings

- **Surface #1 (`StingMaterialManagerCommand`) is a de-facto orphan.** The
  4-option TaskDialog dispatch lives in the switch but no dock button
  carries the `MaterialManager` tag. Originally it was the "quick"
  surface; the `MaterialManagerFull` button superseded it; Phase C
  migrated to the Hub. Phase B/Phase C did NOT remove the orphaned
  dispatch case or the underlying `MaterialManagerDialog.Show(...)`
  helper.
- **Surface #2 (`MaterialManagerCommand` — 7-tab modal) is the active
  dock-panel entry point.** Same 7-tab feature surface as the Hub but
  delivered as a one-shot modal dialog rather than a docked panel.
  Phase B/Phase C did NOT touch its button.
- **Surface #3 (Material Hub dockable pane) is the Phase-C-migrated
  modern surface** — uses shared `StingButtonStyles.xaml`, has its own
  ThemeManager wiring, flyouts, texture browser. Per CLAUDE.md it's
  the canonical "Material Manager (7 tabs)" reference.
- The 7-tab feature set is therefore duplicated between #2 (modal) and
  #3 (dockable). The 4-option TaskDialog (#1) is a strict subset of #2.

### Item table

| Item | Status | Evidence | Recommendation |
|---|---|---|---|
| Decide canonical material-management surface (Hub vs Full modal vs Quick TaskDialog) | **BLOCKED** | `DEDUPE_AUDIT.md` line 150 explicitly defers this with "Needs your call." Three live code paths confirmed above. | **USER DECISION REQUIRED** — see decision question at bottom of this doc. |
| Surface #1 (`StingMaterialManagerCommand` quick TaskDialog) — orphaned dispatch case | **OPEN-SMALL** (independent of canonical decision) | `StingCommandHandler.cs:1266` dispatches `MaterialManager` → command, but no dock button uses the tag. Class + `MaterialManagerDialog.Show(...)` helper sit unused. | Regardless of canonical choice: delete the orphan dispatch case + the `StingMaterialManagerCommand` class + the `MaterialManagerDialog.Show(...)` helper (TaskDialog flavour). ~1h. SAFE — no surviving button. |
| Phase-C style alignment of Surface #2 (full modal dialog) | **OPEN-SMALL** | `MaterialManagerDialog.cs` predates the Phase B/C shared-style migration; its 7-tab dialog is plain WPF, not using `StingButtonStyles.xaml`. | If kept (per user decision), migrate to shared styles like Hub did. ~2h. If retired (per user decision), no action. |
| Dock-panel entry point for the Hub (currently ribbon-only) | **OPEN-SMALL** | `ToggleMaterialHub` has zero dock-panel buttons; only ribbon access at `StingToolsApp.cs:2079`. Other dock panels (Electrical / Plumbing / HVAC / LPS) all have dock-panel toggle buttons. | Add a dock-panel toggle button for the Hub (mirrors Electrical/Plumbing/HVAC toggle pattern). ~30 min. Recommended regardless of canonical decision. |

**Counts:** CLOSED 0 · OBSOLETE 0 · OPEN-SMALL 3 · OPEN-MEDIUM 0 ·
OPEN-LARGE 0 · BLOCKED 1.

### The blocking question for the user

> **Material-management consolidation — which is the canonical surface
> for the 7-tab feature?**
>
> 1. **Hub-only:** retire the full modal dialog (Surface #2). Surface #3
>    (Hub) becomes sole entry. Add dock-panel toggle so users see it
>    from the main panel. (Cleanest; matches Electrical/Plumbing/HVAC
>    panel pattern.)
> 2. **Hub primary + modal kept as quick-open:** keep the
>    "★ Material Manager" dock-panel button as a fast modal route for
>    one-off material edits, but rename to "Material Manager (modal)";
>    Hub remains the panel-resident workspace.
> 3. **Modal primary, Hub retired:** retire the dockable pane (heavier
>    code retirement — `MaterialHubProvider`, the XAML pane, flyouts,
>    texture browser); keep the 7-tab modal. (Not recommended — Phase
>    C just migrated the Hub to shared styles.)
>
> Independent of the above: yes/no to deleting the orphaned
> `StingMaterialManagerCommand` (4-option TaskDialog quick variant — no
> surviving button)?

---

## Suggested next-up (top 5, highest user-visible impact first)

1. **Bucket 1 — `AssignIssues` rewire to `UpdateIssueCommand`** (OPEN-SMALL,
   ~30 min). High user-visible impact: "Reassign" / "Assign Issues"
   buttons in BCC + IssueTrackerDashboard currently silently run "raise
   new issue" instead. Single-PR fix.
2. **Bucket 1 — `DrawingTypeEditorDialog` 6-button align row collapse or
   parameterise** (OPEN-SMALL, ~30 min – 3h). Six labelled buttons that
   all do the same thing → high "label lies" complaint potential per
   MISWIRE_AUDIT philosophy.
3. **Bucket 3 — orphaned `StingMaterialManagerCommand` deletion**
   (OPEN-SMALL, ~1h, SAFE — no surviving button). Independent of the
   blocked consolidation decision; zero-risk cleanup.
4. **Bucket 2 — `MgasVerifyCommand` honour `HcOptions.MgasStep`**
   (OPEN-SMALL, ~2–3h). Button currently labelled "MGAS Verify (Full)"
   but the per-step selector in the UI is read into `HcOptions` then
   ignored — finishes the wiring the relabel started.
5. **Bucket 1 — `CreateIssuesFromWarnings` new command**
   (OPEN-MEDIUM, ~1–1.5d). Higher build cost but completes the BCC
   workflow loop (Revit warnings → NCR/SI auto-creation) and removes a
   "From Warnings" button that today silently raises a blank single
   issue.

Larger items (chunked-async Healthcare pipeline, QR parameterisation,
Healthcare Pset bundle) intentionally NOT in the top 5 — they need their
own scoping phase before commitment.

---

## Branch / commit / push checklist

- Branch: `audit/phase-d-triage` off `origin/main @ f011ac092`.
- One commit: this doc only (`docs/PHASE_D_TRIAGE.md`).
- No code under `StingTools/` touched.
- No PR opened.
- `main` not touched.
