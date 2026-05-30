# DOCS Tab — Phase B Round 2 Redesign Plan

**Branch:** `feature/phase-b-docs` · **Scope:** `StingTools/UI/StingDockPanel.xaml` lines 1274–1803 (DOCS `TabItem` only).

Reads against the patterns catalogue in [`docs/UI_PHASE_B_PATTERNS.md`](UI_PHASE_B_PATTERNS.md). Pattern 6 (short labels + tooltips) is universal — applied alongside every other pattern below.

Baseline tag count for DOCS: **206 unique** (`/tmp/docs-tags-before.txt`). After-set must be a superset (dedup of intra-section duplicates is allowed because the tag survives elsewhere in the tab).

## Inventory (current state, 22 sections)

| # | Section | Buttons | Notable |
|---|---|---:|---|
| 1 | VIEWPORT TOOLS | 16 | 3 sub-rows: Alignment 6 · Numbering 6 · Spacing 4 |
| 2 | SHEET TOOLS | 16 | Top row 8 + Sheet Number row 8 |
| 3 | DRAWING TYPES | 15 + (Production 7 + Packages 4) × 2 dup | Production + Packages blocks duplicated verbatim at lines 1382-1401 |
| 4 | TITLE BLOCK | 14 | 5 wrap rows; Edit CSV / Populate / Validate / Build family / Audit Legacy / Migrate Legacy / etc. |
| 5 | SHEET MANAGER | 29 | 5 sub-rows: top 4 + bottom 4 + Advanced 4 + extra 4 + Templates 5 + Align 4 |
| 6 | FM / O&M HANDOVER | 5 | COBie / Maint / O&M / Asset Health / Space Handover |
| 7 | SCHEDULE TOOLS | 18 + width grid | Sync 3 + Column Width 5 + Management 10 |
| 8 | TEXT NOTE TOOLS | 10 | Case 3 + Align 4 + Leaders 3 |
| 9 | DIMENSION TOOLS | 4 | Reset Overrides / Reset Text / Find Zero / Find&Replace |
| 10 | LEGEND TOOLS | 7 | Sync/Title/Uniform/Refresh/Cleanup + Tag Dictionary primary + Color Legend |
| 11 | TITLE BLOCK TOOLS | 2 | Reset Pos / Rescue (small) |
| 12 | REVISION TOOLS | 4 | Show Clouds / Show Tags / Del Clouds View / Del Clouds Sel |
| 13 | DOCUMENTATION AUTOMATION | 11 | One-click 2 + View Creation 4 + Sheet Creation 3 + Management 3 |
| 14 | VIEWS | 9 | Organizer / Del Unused / Duplicate / Batch Rename / Magic Rename / View Colours / Copy Settings / Crop Content / Sum Areas |
| 15 | MEASUREMENT TOOLS | 3 | Lines / Areas / Perimeters |
| 16 | UTILITIES | 3 | Swap Elements / Convert Regions / Clean Spaces |
| 17 | AEC FILTERS + DRAWING TYPES (surfaced) | 5 | Filters 3 + Browser Organize + Force Resync |
| 18 | EXPORT CENTER | 6 | ★ Export Center primary + PDF Preset + ComboBox A0-A4 + All/Selected/Active |
| 19 | SHEET INDEX | 1 | Export CSV + prefix filter textbox |
| 20 | PROJECT HEALTH SCORE | 3 | Score / Report / Fix All + scope combo + progress bar (custom layout — leave) |
| 21 | VIEW CONTROLS | 10 | Temp Hide 4 + Perm Hide 3 + Graphic Overrides 3 |
| 22 | ANALYSIS HEATMAPS (AVF) | 5 | Compliance / Fill / Carbon / Acoustic / Clear |

## Pattern decisions

| # | Section | Pattern | Rationale |
|---|---|---|---|
| 1 | VIEWPORT TOOLS | **6 labels** | Already chip-sized buttons with short labels; just tighten tooltips. Three semantic sub-rows are correct. |
| 2 | SHEET TOOLS | **5 Expander** + **6 labels** | Primary row of 8 (Organizer/Index/Transmittal/etc.) stays proud; SHEET NUMBER row (8 fine-grained renumber chips) collapses into "Advanced renumber / rename" Expander. |
| 3 | DRAWING TYPES | **dedup** + **5 Expander** + **6 labels** | The Production + Packages blocks are LITERALLY duplicated (lines 1361-1381 == 1382-1401). Dedup per SPECKLE precedent. Then split: top row (Edit Types / Inspect / Reload / Pres Setup / Group Browser / Sync Styles / From Scope Boxes / Export Excel / Import Excel) stays as chips; the second wave (Heal TBs / Renumber / Doctor / Migrate CSV / Migrate Params / Audit Params / Sync Rev / Re-Stamp) folds into "Advanced drawing-type ops" Expander; Production sub-block stays visible (real workflow); Packages stays visible with the ★ Produce & Export primary already shipping. |
| 4 | TITLE BLOCK | **5 Expander** + **6 labels** | Top WrapPanel (Edit CSV / Populate / Validate / Set Variant / Legend Bind) stays as chips — daily ops. Bottom three WrapPanels (Count Sheets / Revision Sync / Stamp TX / Pre-Export · Build Family / Build All · Auto-place / Toggle BIM · Audit Legacy / Migrate Legacy) fold into "Advanced title-block ops" Expander. |
| 5 | SHEET MANAGER | **5 Expander** + **6 labels** | Top row (Sheet Manager / Auto-Layout / Clone / Place Unplaced) stays as chips — daily. Optimal Scale + Sheet Audit + Batch Arrange + Move VP also stay (high-frequency). ADVANCED sub-rows (MaxRects / Save Layout / Apply Layout / Overflow / Batch Clone / Renumber / VP Types / Export CSV) + TEMPLATES & COMPLIANCE (5 + 4) all fold into "Advanced layout & batch ops" Expander. |
| 6 | FM / O&M HANDOVER | **1 chips** + **6 labels** | 5 independent action chips. Already chip-sized. Just tighten labels (COBie / Maint / O&M / Health / Spaces). |
| 7 | SCHEDULE TOOLS | **5 Expander** + **6 labels** | Sync row (Sync Pos / Sync Rot / Show Hidden) stays. Column Width grid stays (data input UI). Schedule Management row of 10 → fold most into "Advanced schedule ops" Expander; primary chip Audit + Refresh stay. |
| 8 | TEXT NOTE TOOLS | **6 labels** | Three semantic sub-rows are already correct (Case 3 / Align 4 / Leaders 3). Just chip-sized labels + tooltips. |
| 9 | DIMENSION TOOLS | **1 chips** + **6 labels** | 4 independent action chips. Trim labels (Reset OG / Reset Txt / Find 0 / F&R). |
| 10 | LEGEND TOOLS | **1 chips** + **6 labels** | Already split: 5 ops chips + primary Tag Dictionary + Color Legend. Just tighten labels. |
| 11 | TITLE BLOCK TOOLS | **1 chips** + **6 labels** | Only 2 buttons. Already correct. |
| 12 | REVISION TOOLS | **2 RadioButton mode picker** | Show Clouds / Show Tags / Del Clouds (View) / Del Clouds (Sel). The 2 "Del Clouds" buttons share the verb "delete" but differ in scope (View vs Selection) — classic scope-mode-picker pattern. Show Clouds + Show Tags stay as toggles. Scope radio (View / Selection) gates a single "⚡ Delete Clouds" runner. Two existing tags (`RevDelCloudsView`, `RevDelCloudsSel`) still wired via `OnRadioRouteChecked`. |
| 13 | DOCUMENTATION AUTOMATION | **5 Expander** + **6 labels** | Already has primary ★ Doc Wizard + Doc Package — proud. The 9 secondaries (View Creation 4 + Sheet Creation 3 + Management 3) fold into "Manual view/sheet creation" Expander. |
| 14 | VIEWS | **5 Expander** + **6 labels** | 9 buttons; Organizer + Del Unused + Duplicate + Batch Rename are daily; Magic Rename + View Colours + Copy Settings + Crop + Sum Areas fold into "Advanced view ops" Expander. |
| 15 | MEASUREMENT TOOLS | **1 chips** + **6 labels** | 3 chips. Already correct. |
| 16 | UTILITIES | **1 chips** + **6 labels** | 3 chips. Already correct. |
| 17 | AEC FILTERS + DRAWING TYPES (surfaced) | **1 chips** + **6 labels** | 5 power-user chips already short. Tighten labels (Mint / Inspect / Reload / Browser / Resync). |
| 18 | EXPORT CENTER | **3 ComboBox** + **6 labels** | Already has a `cmbPaperSize` ComboBox + ★ Export Center primary. Add a second runner: paper-size combo + "Print sheets" combo (All / Selected / Active) replacing 3 separate Print buttons — but the 3 PDF buttons (All Sheets / Selected Sheets / Active View) are mutually exclusive scopes — **switch to Pattern 2 RadioButton ring** for the print-scope row. Existing tags `PrintSheets` / `PdfSelectedSheets` / `PdfActiveView` stay reachable via `OnRadioRouteChecked` on the radios + single "⚡ Print PDF" button. PDF Preset / Export Center stay as emphasis chips. |
| 19 | SHEET INDEX | **6 labels** | Single button + filter textbox. Already minimal. |
| 20 | PROJECT HEALTH SCORE | **6 labels** | Custom layout with progress bar — leave geometry; just tighten tooltips. |
| 21 | VIEW CONTROLS | **6 labels** | 3 semantic sub-rows (Temp Hide / Perm Hide / Graphic Overrides) — each row's items are independent verbs. Chip-size + tooltips. |
| 22 | ANALYSIS HEATMAPS (AVF) | **2 RadioButton mode picker** | 4 mutually exclusive heatmap types (Compliance / Fill / Carbon / Acoustic) + 1 Clear button. Heatmap type is a mode pick — radio ring + "⚡ Paint heatmap" runner + Clear chip. Existing tags `Heatmap_Compliance` / `Heatmap_Fill` / `Heatmap_Carbon` / `Heatmap_Acoustic` stay reachable via `OnRadioRouteChecked`. `Heatmap_Clear` stays as a danger-style chip. |

## DRAWING TYPES dedup

Lines 1361-1381 (Production WrapPanel + Packages WrapPanel) are duplicated verbatim at lines 1382-1401. Same tags, same tooltips, same styles. Drop the duplicate block. SPECKLE ×3 dedup precedent applies — Phase B explicitly permits collapsing byte-identical repeats. All tags remain reachable from the surviving copy.

## New runner / dispatch tags

| Runner tag | Type | Reads | Dispatches |
|---|---|---|---|
| `Rev_DeleteClouds` | Pattern 2 | `rbRevScopeView` / `rbRevScopeSel` radios | `RevDelCloudsView` or `RevDelCloudsSel` |
| `Export_PrintScope` | Pattern 2 | `rbPrintAll` / `rbPrintSelected` / `rbPrintActive` radios | `PrintSheets` or `PdfSelectedSheets` or `PdfActiveView` |
| `Heatmap_PaintSelected` | Pattern 2 | `rbHeatmapCompliance` / `rbHeatmapFill` / `rbHeatmapCarbon` / `rbHeatmapAcoustic` radios | `Heatmap_Compliance` / `Heatmap_Fill` / `Heatmap_Carbon` / `Heatmap_Acoustic` |

All three runners use the existing `OnRadioRouteChecked` stash + a `Cmd_Click` branch that calls a new `RunDocsRunner(string)` helper. `RunDocsRunner` mirrors `RunInteropRunner`'s signature (returns bool, switch on runner tag, reads radios + dispatches concrete tag). Three new x:Name'd radio sets get added to xaml.cs `Cmd_Click` short-circuit list.

## Tag superset proof

- Before: 206 unique tags (DOCS region, baseline).
- After (planned): all 206 stay reachable. Dedup of Production+Packages duplicates removes 11 duplicate-tag occurrences but each tag survives in the canonical block. The 3 new runner tags are additive.
- Verification command (per patterns doc § verification gate):
  ```bash
  git show origin/main:StingTools/UI/StingDockPanel.xaml | grep -oE 'Tag="[A-Za-z_0-9]+"' | sort -u > /tmp/before.txt
  grep -oE 'Tag="[A-Za-z_0-9]+"' StingTools/UI/StingDockPanel.xaml | sort -u > /tmp/after.txt
  comm -23 /tmp/before.txt /tmp/after.txt   # MUST be empty
  ```

## Out of scope

- Tags moving between tabs (Phase 3b is done).
- Refactoring `StingCommandHandler` dispatcher.
- Changes outside `<TabItem Header="DOCS">`.
- StingButtonStyles.xaml (Phase A locked).
- The PROJECT HEALTH SCORE custom layout (progress bar + score readout) — leaves geometry, just tooltips.
