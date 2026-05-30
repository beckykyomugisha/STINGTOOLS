# BIM Tab — Phase B Round 3 Redesign Plan

**Branch:** `feature/phase-b-bim` · **Scope:** `StingTools/UI/StingDockPanel.xaml` lines 2891–3578 (BIM `TabItem` only).

Reads against the patterns catalogue in [`docs/UI_PHASE_B_PATTERNS.md`](UI_PHASE_B_PATTERNS.md). Pattern 6 (short labels + tooltips) is universal — applied alongside every other pattern below. Baseline tag count: **188 unique** (`/tmp/bim-tags-before.txt`).

## Preservation (CRITICAL — DO NOT TOUCH STRUCTURE)

Two hero cards landed in Phase A.5 with the deliberate outline + bottom-edge-strip emphasis pattern. Both must keep their outer Border + edge strip + primary star-button intact. Surrounding sub-rows MAY get Pattern 6 label shortening + tooltips, but the hero block stays visually as-is.

| # | Hero card | Lines | Preserve |
|---|---|---|---|
| HERO-1 | BIM Coordination Center (`★ OPEN COORDINATION CENTER`) | 2904–2933 | Outer `Border BorderBrush="#1A237E" BorderThickness="0,0,0,2"`, header text, primary star button, ToolTip. The "COORDINATION CHECKS" sub-WrapPanel underneath gets Pattern 6 short labels. |
| HERO-2 | Warnings Manager (`⚠ WARNINGS DASHBOARD`) | 2935–2967 | Outer `Border BorderBrush="#E65100" BorderThickness="0,0,0,2"`, header text, primary orange button + Full Dashboard + Auto-Fix row. Secondary chip row (Export/Baseline/Select/Suppress/Compliance/Monitor) gets Pattern 6 short labels. |

## Inventory (current state, 24 sections)

| # | Section | Lines | Buttons | Notable |
|---|---|---:|---:|---|
| 1 | HEADER | 2895–2901 | 0 | Title card (kept as-is) |
| 2 | BIM COORDINATION CENTER (HERO-1) | 2903–2933 | 1 + 7 | Preserve hero; trim CHECKS sub-row |
| 3 | WARNINGS MANAGER (HERO-2) | 2935–2967 | 3 + 6 | Preserve hero; trim secondary chip row |
| 4 | DOCUMENT MANAGEMENT CENTER | 2969–2989 | 6 | Primary Doc Manager + 5 setup actions |
| 5 | PROJECT OVERVIEW | 2991–3002 | 2 | Dashboard + ISO 19650 Ref |
| 6 | BEP — BIM EXECUTION PLAN | 3004–3022 | 5 | Star BEP Dashboard + 4 ops |
| 7 | 4D/5D — SCHEDULING & COST | 3024–3085 | 19 | Largest section: 4D + 5D + Cost Mgmt P0→P8 |
| 8 | STANDARDS — COMPLIANCE | 3093–3110 | 3 | ISO 19650 / COBie / Classification |
| 9 | MEP SCHEDULES | 3112–3130 | 5 | HVAC / Electrical / Plumbing / Fire / All |
| 10 | LIGHTNING PROTECTION (LPS) | 3132–3180 | 20 | BS EN 62305 full suite |
| 11 | CDE — DOCUMENT CONTROL | 3182–3209 | 6 | Status / Register / Transmittals (3 sub-groups) |
| 12 | ISSUE / RFI TRACKER | 3211–3243 | 10 | Issue Dashboard + Raise + analytics (2 sub-groups) |
| 13 | DELIVERABLE LIFECYCLE | 3245–3265 | 8 | Issue/ReIssue/Publish/Cancel/Supersede/Replace/Bulk + Transmittal |
| 14 | COBie V2.4 — FM HANDOVER | 3267–3288 | 7 | COBie Export + Stream + Handover + Bulk + StageGate + SetOutDir + Import |
| 15 | GAP ANALYSIS — AUTOMATION | 3290–3309 | 6 | GAP-04 through GAP-10 |
| 16 | BRIEFCASE — REFERENCE DOCS | 3311–3325 | 3 | View / Read / Add |
| 17 | STICKY NOTES | 3327–3347 | 6 | Add / Export / Select / Categories / Dashboard / Search |
| 18 | MODEL HEALTH & COMPLIANCE | 3349–3365 | 4 | Health Dashboard / Export / MIDP / Full Compliance |
| 19 | 4D/5D EXTENDED | 3367–3400 | 12 | Export4D / Navis / Export5D / Cost Trace + 8 phase/quantity ops |
| 20 | REVISION MANAGEMENT | 3402–3451 | 16 | Star Dashboard + Create + 13 ops (4 sub-groups, incl. Advanced) |
| 21 | QUALITY ASSURANCE | 3453–3471 | 5 | QA Report / Health Scan / Warnings / Custom Rules / Setup Check |
| 22 | WORKSET and LINK AUDIT | 3473–3489 | 4 | Workset Audit / Create / Link Audit / Link Stats |
| 23 | SPATIAL VALIDATION | 3491–3509 | 5 | Room / Grid / Level / Family / View+Sheet audits |
| 24 | CARBON and CHANGE TRACKING | 3511–3531 | 6 | Carbon Calc / Export + Snapshot + Compare + Batch PDF + Sheet Sets |
| 25 | COORDINATION CENTER | 3533–3551 | 5 | Coord Center / Dashboard / File Monitor / Broadcast / Access |
| 26 | COMPLIANCE GATES (surfaced) | 3554–3562 | 4 | Coverage / FireWall / Healthcare / Sustainability |
| 27 | LABOUR · QR · HEALTH (surfaced) | 3564–3574 | 6 | Labour×2 / QR×2 / Health HTML / Clash→Excel |

## Pattern decisions

| # | Section | Pattern | Rationale |
|---|---|---|---|
| 1 | HEADER | n/a | Title card. No change. |
| 2 | HERO-1 BIM Coordination Center | **PRESERVE** + **6 labels** on sub-row | Hero outline/edge stays. CHECKS sub-WrapPanel (Clash Detect / Manager / Run / X-Model / BCF / Live / Matrix) gets Pattern 6 short labels — labels already short (3-12 chars), tooltips kept. |
| 3 | HERO-2 Warnings Manager | **PRESERVE** + **6 labels** on secondary row | Hero star Dashboard + Full Dashboard + Auto-Fix stay proud. Secondary chip row (Export CSV / Baseline / Select / Suppress / Compliance / Monitor) gets Pattern 6 — already short. |
| 4 | DOC MGMT CENTER | **5 Expander** + **6 labels** | Primary Doc Manager stays proud, 5 secondaries (Folder Setup / Open Root / Folder Health / Migrate Legacy / Data Exchange) collapse into "Folder + setup ops". |
| 5 | PROJECT OVERVIEW | **1 chips** + **6 labels** | Already 2 buttons — chip-shrink. |
| 6 | BEP | **5 Expander** + **6 labels** | Star BEP Dashboard primary, 4 secondaries (Create/Update/Export/Validate) collapse into "BEP lifecycle". |
| 7 | 4D/5D SCHEDULING & COST | **5 Expander** ×2 + **6 labels** | 19 buttons across 3 sub-groups. Keep star Scheduling & Cost Dashboard + star BOQ Cost Manager + star Tender BOQ proud. Collapse 4D ops (Auto/Import/Timeline/Export) under "4D scheduling ops". Collapse the 8-button BOQ secondary row (BCC Refresh / Prep / Rate Heat / Export / Import / Snap / Auto Cost / Import Rates / Cost Report / Cash Flow) under "BOQ + 5D ops". |
| 8 | STANDARDS COMPLIANCE | **1 chips** + **6 labels** | 3 buttons. Shrink. |
| 9 | MEP SCHEDULES | **3 ComboBox** + **6 labels** | 5 buttons (HVAC / Electrical / Plumbing / Fire / All MEP) all run the same verb — "create MEP schedule for X". ComboBox picks discipline, single "Create schedule" button dispatches. New runner tag `Bim_MepScheduleCreate`. |
| 10 | LIGHTNING PROTECTION | **5 Expander** + **6 labels** | 20 buttons. Keep BlueBtn Dashboard + PurpleBtn LPS Class Setup proud (already-primary). Collapse the remaining 18 under "LPS engineering ops". |
| 11 | CDE — DOC CONTROL | **5 Expander** + **6 labels** | 6 buttons across 3 sub-groups. Promote Set CDE Status as primary, fold Validate Naming + Doc Register + Add Document + Create Transmittal + Review Tracker under "CDE ops". |
| 12 | ISSUE / RFI TRACKER | **5 Expander** + **6 labels** | 10 buttons. Keep star Issue Dashboard + Raise Issue proud. Collapse 3 management secondaries (Issue Dashboard / Update / Select) + 5 analytics under "Issue ops". |
| 13 | DELIVERABLE LIFECYCLE | **3 ComboBox** + **6 labels** | 7 deliverable-state buttons (Issue/ReIssue/Publish/Cancel/Supersede/Replace/Bulk) all do "change deliverable state". ComboBox picks operation; single "Run on selection" button dispatches. New runner tag `Bim_DeliverableRun`. Transmittal stays as separate chip. |
| 14 | COBie V2.4 | **5 Expander** + **6 labels** | 7 buttons. Keep PurpleBtn COBie Export + GreenBtn FM Handover proud. Collapse 5 secondaries (Stream / Bulk / Stage Gate / Set OutDir / Extended Import) under "COBie ops". |
| 15 | GAP ANALYSIS | **5 Expander** + **6 labels** | 6 power-user buttons, no obvious primary. Whole section behind Expander labelled "GAP fixes (BIM automation)". |
| 16 | BRIEFCASE | **1 chips** + **6 labels** | 3 buttons. Trim. |
| 17 | STICKY NOTES | **5 Expander** + **6 labels** | 6 buttons. Keep Add Note + Dashboard proud, collapse Export/Select/Categories/Search under "Notes ops". |
| 18 | MODEL HEALTH | **1 chips** + **6 labels** | 4 buttons. Trim labels. |
| 19 | 4D/5D EXTENDED | **5 Expander** + **6 labels** | 12 buttons across 2 sub-rows, no obvious primary. Whole section behind Expander labelled "4D/5D extended ops". |
| 20 | REVISION MANAGEMENT | **5 Expander** ×2 + **6 labels** | 16 buttons across 5 sub-groups. Keep star Revision Dashboard + RedBtn Create Revision proud. Collapse 4 main row + 5 second row + 5 third row under "Revision ops"; existing ADVANCED sub-row stays under its own Expander "Advanced revision ops". |
| 21 | QUALITY ASSURANCE | **1 chips** + **6 labels** | 5 buttons. Trim. |
| 22 | WORKSET + LINK AUDIT | **1 chips** + **6 labels** | 4 buttons. Trim. |
| 23 | SPATIAL VALIDATION | **1 chips** + **6 labels** | 5 buttons. Trim. |
| 24 | CARBON + CHANGE TRACKING | **5 Expander** + **6 labels** | 6 buttons spanning two domains. Keep Carbon Calc + Take Snapshot proud, collapse Carbon CSV / Compare / Batch PDF / Sheet Sets under "Carbon + snapshot ops". |
| 25 | COORDINATION CENTER (bottom) | **5 Expander** + **6 labels** | 5 buttons, star Coord Center primary. Collapse Dashboard / File Monitor / Broadcast / Access under "Coord ops". |
| 26 | COMPLIANCE GATES | **1 chips** + **6 labels** | 4 surfaced gate buttons. Trim. |
| 27 | LABOUR · QR · HEALTH | **5 Expander** + **6 labels** | 6 surfaced buttons across 3 unrelated domains. Whole row behind Expander labelled "Labour / commissioning / health (surfaced)". |

## New runner / dispatch tags

To preserve the tag-superset invariant, two new runner tags are added:

| New tag | Pattern | Reads | Dispatches |
|---|---|---|---|
| `Bim_MepScheduleCreate` | 3 (combo) | `cmbBimMepSchedule.SelectedItem.Tag` (one of `MEPScheduleHVAC` / `MEPScheduleElec` / `MEPSchedulePlumb` / `MEPScheduleFire` / `MEPScheduleAll`) | Dispatches the resolved tag |
| `Bim_DeliverableRun` | 3 (combo) | `cmbBimDeliverableOp.SelectedItem.Tag` (one of `IssueDeliverable` / `ReIssueDeliverable` / `PublishDeliverable` / `CancelDeliverable` / `SupersedeDeliverable` / `ReplaceDeliverable` / `BulkIssueDeliverables`) | Dispatches the resolved tag |

Both runners route via a new `RunBimRunner(string)` helper added once to `StingDockPanel.xaml.cs` alongside `RunInteropRunner` / `RunDocsRunner` / `RunSetupRunner` (same shape, BIM-scoped). `Cmd_Click` gains a 4th early-return branch for the two new tags.

## Code-behind helpers added (once, idempotent)

```csharp
// BIM suite-runner helper. Parallel to RunInteropRunner / RunDocsRunner / RunSetupRunner.
private bool RunBimRunner(string runnerTag)
```

`OnRadioRouteChecked` already exists (Round 1) — reused if any radios are added. Round 3 uses combos only, so no new radio routing.

## SPECKLE / dedup considerations

No SPECKLE in BIM tab (lives in INTEROP). No structural dedups needed.

## Hard-rule compliance

- **Tag superset.** All 188 existing BIM tags retained; 2 net-new runner tags added (`Bim_MepScheduleCreate`, `Bim_DeliverableRun`). Verification: `comm -23 /tmp/bim-tags-before.txt /tmp/bim-tags-after.txt` must be empty.
- **XAML parses well-formed.** Verified with `xml.etree.ElementTree.parse`.
- **Zero CompiledPlugin/Data churn.** XAML + code-behind only.
- **One tab scope.** Touches BIM `TabItem` + idempotent code-behind helper; nothing else.
- **Phase A locks intact.** No edits to outline styles, theme wiring, emphasis cards (HERO-1 / HERO-2 preserved structurally), swatches, gradient borders, or `StingButtonStyles.xaml`.
- **Round 1/2 helpers intact.** `OnRadioRouteChecked`, `RunInteropRunner`, `RunDocsRunner`, `RunSetupRunner` unchanged; `RunBimRunner` added alongside.
