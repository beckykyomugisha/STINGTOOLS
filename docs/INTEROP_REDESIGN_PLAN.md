# INTEROP Tab — Phase B Round 1 Redesign Plan

**Branch:** `feature/phase-b-interop` · **Scope:** `StingTools/UI/StingDockPanel.xaml` lines 4757–5101 (INTEROP `TabItem` only).

Reads against the patterns catalogue in [`docs/UI_PHASE_B_PATTERNS.md`](UI_PHASE_B_PATTERNS.md). Pattern 6 (short labels + tooltips) is universal — applied alongside every other pattern below.

## Inventory (current state, 16 sections)

| # | Section | Buttons | Notable |
|---|---|---:|---|
| 1 | HEADER | 0 | Title-card Border (kept as-is) |
| 2 | DATA PIPELINE (top) | 6 | Mixed bag: PDF / Quantity / Clash / Health / Params / Dashboard |
| 3 | CROSS-SYSTEM AUTOMATION | 9 | One primary (CDE Approval) + 8 cross-link actions |
| 4 | DATA EXPORT — EXLINK STYLE | 1 | Single ★ Data Export button |
| 5 | EXCEL LINK — BIDIRECTIONAL | 7 | Export / Import / RoundTrip / Wizard / Schedules×2 / Template |
| 6 | PLATFORM INTEGRATION | 14 | ACC / CDE / BCF / Procore / Trimble / Aconex / ProjectWise / SharePoint / Sync / Dashboard / Publish / Push |
| 7 | SPECKLE (×3 duplicate) | 9 (3×3) | Diff / Send / Receive repeated 3 times byte-identical |
| 8 | DATA PIPELINE (second copy) | 8 | IFC / IFC Map / BOQ / Excel Import / Excel Link / Clash / Keynote / Validate |
| 9 | IFC INGESTION | 3 | IFC Import / Stabilize GUIDs / Push to Planscape |
| 10 | LAN COLLABORATION | 6 | Enable WS / Sync / Backup / Team / Change Log / Auto-Sync |
| 11 | PLATFORM · SCHEDULING · COST | 10 | 4D Viewer / P6×3 / BCF Sync / Cloud×2 / BOQ Snap / Cost Browser / Rev Audit |
| 12 | EXCEL LINK FILES | 7 | Browser / Export / Import / Multi / Quick / Batch / Custom |
| 13 | EXLINK — QTO & DOCUMENTS | 3 | QTO / Doc Issuance / COBie Sync |
| 14 | EXLINK — DYNAMIC EXPORT | 3 | PDF / DWG / NWC |
| 15 | EXLINK — BATCH AUTOMATION | 10 | Batch PDF/DWG/NWC/IFC / Audit / Compact / Backup / Family / Stats / Param |
| 16 | EXLINK — MODEL EXPLORER | 5 | Family / Type / Unused / CAD / In-place |
| 17 | ISB SCHEDULES | 10 | Door / Window / Room / Wall / Floor / Equip / Light / Plumb / Elec / KeyPlan |
| 18 | EXLINK — STICKY NOTES | 4 | Dashboard / Add / Export / Bulk |

Baseline tag count for INTEROP: **108 unique** (`/tmp/interop-tags-before.txt`).

## Pattern decisions

| # | Section | Pattern | Rationale |
|---|---|---|---|
| 1 | HEADER | n/a | Title card stays as-is. |
| 2 | DATA PIPELINE (top) | **5 Expander** + **6 labels** | This duplicates section 8. Promote one primary (Project Dashboard) and stash the rest under "More pipeline ops". Section 8 already covers exports — fold the strict duplicates out of view via Expander to reduce surface area. |
| 3 | CROSS-SYSTEM AUTOMATION | **5 Expander** + **6 labels** | Primary ★ CDE Approval stays proud; 8 secondaries collapse into "Advanced cross-system ops". |
| 4 | DATA EXPORT — EXLINK STYLE | **1 chips** + **6 labels** | Single emphasis card already; nothing to do beyond short label / tooltip. |
| 5 | EXCEL LINK — BIDIRECTIONAL | **4 CheckBox grid** + **5 Expander** + **6 labels** | Schedule export / import / template / wizard are toggle-flags around the core Export/Import/RoundTrip trio. Keep the trio + Wizard as primaries, fold per-target schedule choices into a "What to sync" checkbox set with a new `ExcelLink_SyncSuite` runner tag. |
| 6 | PLATFORM INTEGRATION | **3 ComboBox** + **5 Expander** + **6 labels** | 14 buttons; 6 are platform targets (ACC / Procore / Trimble / Aconex / ProjectWise / SharePoint) that share the same "publish to" verb. Combo lets the user pick a target, single "Publish" button dispatches the matching tag. BCF I/O + CDE / Health stay as chips above; Push / Publish 3D stay as emphasis cards. |
| 7 | SPECKLE (×3) | **1 chips** (after dedup) | Phase B doc explicitly authorises consolidating the byte-identical ×3 block to ×1. Tags (`SpeckleDiff`, `SpeckleSend`, `SpeckleReceive`) still reachable from the remaining instance. |
| 8 | DATA PIPELINE (second copy) | **1 chips** + **6 labels** | Real pipeline section. Shorten labels (IFC / IFC Map / BOQ / Import / Excel Link / Clash / Keynote / Validate), keep WrapPanel. |
| 9 | IFC INGESTION | **1 chips** + **6 labels** | 3 action buttons, no mode pick. Keep emphasis on IFC Import. |
| 10 | LAN COLLABORATION | **1 chips** + **6 labels** | 6 independent actions. Shrink labels. |
| 11 | PLATFORM · SCHEDULING · COST | **5 Expander** + **6 labels** | Power-user section, 10 buttons. Hide behind Expander, label "Scheduling / cost integrations". |
| 12 | EXCEL LINK FILES | **5 Expander** + **6 labels** | Primary ★ Link Browser stays; 6 secondaries (Export/Import/Multi/Quick/Batch/Custom) collapse. |
| 13 | EXLINK — QTO & DOCUMENTS | **1 chips** + **6 labels** | 3 independent buttons. Trim labels. |
| 14 | EXLINK — DYNAMIC EXPORT | **2 RadioButton mode picker** | PDF / DWG / NWC are mutually exclusive format choices → radio ring + single "Export" button. New runner tag `ExLinkDynamic_Run` (reuses existing format tags via the shared `OnRadioRouteChecked` helper). |
| 15 | EXLINK — BATCH AUTOMATION | **5 Expander** + **6 labels** | 10 buttons; nothing primary. Whole section behind Expander labelled "Batch automation". |
| 16 | EXLINK — MODEL EXPLORER | **1 chips** + **6 labels** | 5 inspection buttons. Trim labels. |
| 17 | ISB SCHEDULES | **3 ComboBox** + **6 labels** | 10 ISB schedule generators all do the same verb ("create schedule") for different categories. Combo selects category, single "Create schedule" button routes to the matching tag. New runner tag `ISB_CreateSelected`. |
| 18 | EXLINK — STICKY NOTES | **1 chips** + **6 labels** | 4 buttons with primary ★ Notes Dashboard. Trim labels. |

## SPECKLE consolidation

Three identical `<!-- SPECKLE -->` blocks at lines 4894–4923, 4925–4939, and a second copy (already in the inventory at #7). Only the first instance retained; the two duplicates removed. Dispatch tags `SpeckleDiff` / `SpeckleSend` / `SpeckleReceive` remain reachable from the surviving block. Documented per the catalogue's "do-not-touch" exception list.

## New runner / dispatch tags

To preserve the **tag-superset** invariant (no tag may disappear), three new runner tags are added — their handlers route to existing tags based on UI state:

| New tag | Pattern | Reads | Dispatches |
|---|---|---|---|
| `ExcelLink_SyncSuite` | 4 (checkbox grid) | `chkSyncElements` / `chkSyncSchedules` / `chkSyncTemplate` | One or more of `ExportToExcel`, `ImportFromExcel`, `ExcelRoundTrip`, `ExportSchedulesToExcel`, `ImportSchedulesFromExcel`, `ExportExcelTemplate` in sequence |
| `Platform_PublishTarget` | 3 (combo) | `cmbPlatformTarget.SelectedItem.Tag` (one of `ACCPublish` / `ProcorePackage` / `TrimbleExport` / `AconexPackage` / `ProjectWiseExport` / `SharePointExport`) | Dispatches the resolved tag |
| `ExLinkDynamic_Run` | 2 (radio) | Radios `rbExLinkPDF` / `rbExLinkDWG` / `rbExLinkNWC` via shared `OnRadioRouteChecked` | Dispatches `ExLinkDynamicPDF` / `ExLinkDynamicDWG` / `ExLinkDynamicNWC` |
| `ISB_CreateSelected` | 3 (combo) | `cmbISBSchedule.SelectedItem.Tag` (one of `ISBDoorSchedule`…`ISBKeyPlan`) | Dispatches the resolved tag |

All four runner-tag handlers are added once to `StingDockPanel.xaml.cs` alongside the existing `Cmd_Click` (idempotent — first INTEROP agent adds them, subsequent Phase B agents reuse the pattern). They DispatchCommand-route, so the underlying tags are exercised exactly as before.

## Code-behind helpers added (once, idempotent)

```csharp
// Shared by every Pattern 2 ring used in INTEROP and future tabs.
private void OnRadioRouteChecked(object sender, RoutedEventArgs e)

// Internal helper used by suite-runner Cmd_Click branches.
private void RunInteropRunner(string runnerTag) // resolves the runner -> 1..N concrete tags -> DispatchCommand each
```

`OnRadioRouteChecked` matches the catalogue signature exactly so the next Phase B agent does NOT add a second copy. `RunInteropRunner` is INTEROP-scoped; if HEALTHCARE or another tab needs a generic suite-runner, it should generalise this rather than copy-paste.

## Hard-rule compliance

- **Tag superset.** All 108 existing INTEROP tags retained; 4 net-new runner tags added.
- **XAML parses well-formed.** Verified with `xml.etree.ElementTree.parse`.
- **Zero CompiledPlugin churn.** XAML + code-behind edits only.
- **One tab scope.** Touches `StingDockPanel.xaml` INTEROP `TabItem` + idempotent code-behind helpers; nothing else.
- **Phase A locks intact.** No edits to outline styles, theme wiring, emphasis cards, swatches, gradient borders, or `StingButtonStyles.xaml`.
