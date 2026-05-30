# Phase 3b — main dock-panel tab reorg PLAN (audit-first)

Branch: `feature/phase3`. **This commit is the PLAN only — NO XAML moved.** Paused for your approval.

Moves are at **whole-section granularity**: each `SectionLabel` + its `GroupBorder`/buttons
moves as one block. **No button's Tag/handler/behaviour changes** — only which tab/section it
lives in. TAGS → Scale sub-tab, the Healthcare tab + `Healthcare_` dispatcher, and the
dedicated panels are all out of scope.

---

## CURRENT MAP — 11 top-level tabs (section groups as they stand today)

| Tab | Section groups (in order) |
|---|---|
| **SELECT** | AI Smart Select · Category · Spatial · Selection Scope · Selection Sets · State · View Isolate/Hide · Selection Ops · Selection Memory · Tag Selector · Project Filter · Quick Param · Bulk Parameter Write · Parameter Lookup · Conditions |
| **ORGANISE** | AI Organise Engine · Data Tagging (ISO 19650) · Visual Tag Placement · Tag Operations · TAG7 Rich Display · Tag Segment Display · Color Legends · Tag Legends · MEP&Arch Legends · VG/Filter Legends · Legend Intelligence · System Param Push · Orientation&Text Align · Nudge · Align&Distribute · Leaders · AI/Auto · Tag Appearance · Analyse · Tag Position · Pattern Learning · Batch View Processing · Room Tag Position Sync · Linked Model Elements · **Export Center** · **Sheet Index** |
| **DOCS** | Sheet Manager · Documentation Automation · Views · AEC Filters + Drawing Types *(Group-5)* |
| **TEMP** | Setup · Materials · Family Types · Symbols&Devices · SLD Generator · Schedules · MEP Schedules · Room/Space · View Templates · Template Manager · Styles · Data QA · COBie Reference Data · Workflow Automation · Quick Workflows |
| **CREATE** | Setup · Populate Tokens · Quality Assurance · Paragraph&Presentation · ISO Completeness Dashboard · Family&Display · Export · Token Inspector |
| **VIEW** | View Tag Style · Project Health Score · Parameter Anomaly Detection · AI Context-Aware Tag Placement · Colouriser · Tag Style Engine · View Controls · Analysis Heatmaps (AVF) *(Group-5)* |
| **MODEL** | Quick Build · Architectural · Structural · Structural Automation · Coverings · Excel→Structural · Full Automation · MEP · Family Quick Edit · DWG→Model |
| **BIM** | Warnings Manager · Document Management Center · Project Overview · BEP · 4D/5D Scheduling&Cost · Standards-Compliance · Data Pipeline · MEP Schedules · Lightning Protection · CDE Document Control · Issue/RFI Tracker · Deliverable Lifecycle · COBie V2.4 Handover · Gap Analysis · Briefcase · Sticky Notes · Model Health&Compliance · Cross-System Automation · 4D/5D Extended · Data Export (ExLink style) · Excel Link · Platform Integration · Speckle ×3 · Revision Management · Data Pipeline · IFC Ingestion · Quality Assurance · Workset&Link Audit · Spatial Validation · Carbon&Change Tracking · LAN Collaboration · Compliance Gates *(Group-5)* · Platform·Scheduling·Cost *(Group-5)* · Labour·QR·Health *(Group-5)* |
| **TAGS** *(Tag Studio)* | nested sub-tabs: Placement · Leader&Elbow · Style&Color · Tokens&Depth · Categories · Tools · **Scale** · Automation · Standards · MEP · Fabrication · Routing · Fixtures |
| **EXLINK** | Excel Link Files · Batch Export · Model Explorer · ISB Schedules · Sticky Notes (external-data-link suite) |
| **HEALTHCARE** *(do not touch)* | Healthcare Pack header + nested sub-tabs: Validators · MGPS · Radiation · Rooms/RDS · Specialist · Workflows |

### Problems with the current map
1. **Tagging is smeared across 4 tabs** — ORGANISE (manage), CREATE (populate/QA), VIEW
   (appearance/style), TAGS (studio). Users can't predict where a tag tool lives.
2. **VIEW is a grab-bag** — half tag-appearance, half view/analysis (health, heatmaps, controls).
3. **Export Center + Sheet Index are stranded at the bottom of ORGANISE** — they're documentation, not tagging.
4. **BIM is overloaded (~35 sections)** mixing ISO-19650 coordination with data-exchange/interop (Excel, Platform, Speckle, IFC, Data Pipeline) — two different jobs.
5. **EXLINK is a thin standalone tab** doing the same "data exchange" job already duplicated inside BIM.
6. **TEMP** is an opaque name for "project setup & standards."

---

## PROPOSED MAP — 9 tabs (11 → 9), grouped by job

| # | Tab | Source | What lives here |
|---|---|---|---|
| 1 | **SELECT** | keep | selection + filters + bulk-param + lookup (unchanged) |
| 2 | **TAGGING** | ORGANISE (renamed) **+ CREATE + VIEW's tag sections** | the single home for everyday tagging: data-tag, visual placement, ops, legends, leaders, align, appearance, **token populate, QA, completeness dashboard, tag-style engine, colouriser, anomaly** |
| 3 | **TAGS** *(→ rename "TAG STUDIO")* | keep, untouched | advanced curated workspace (compass / Scale / etc.) |
| 4 | **MODEL** | keep | element creation + DWG→BIM (unchanged) |
| 5 | **DOCS** | keep **+ Export Center, Sheet Index (from ORGANISE) + View Controls, Project Health Score, Analysis Heatmaps (from VIEW)** | sheets, views, export, drawing types, view analysis |
| 6 | **SETUP** | TEMP (renamed) | project setup, materials, families, schedules, templates, styles, COBie ref, workflows (unchanged contents) |
| 7 | **BIM** | keep, **minus the interop sections** | ISO-19650 coordination: warnings, doc-mgmt, BEP, project overview, CDE, issues/RFI, deliverable lifecycle, COBie handover, briefcase, sticky notes, model health, QA, workset/link audit, spatial validation, gap analysis, revisions, standards-compliance, 4D/5D, carbon, compliance-gates *(Group-5)*, labour/QR/health *(Group-5)* |
| 8 | **INTEROP** | NEW — split from BIM **+ EXLINK merged in** | data exchange: Data Pipeline, IFC Ingestion, Excel Link, Platform Integration, Speckle, Data Export (ExLink-style), platform·scheduling·cost *(Group-5)*, + all EXLINK suite |
| 9 | **HEALTHCARE** | keep, untouched | (dispatcher + selection model untouched) |

Net: **VIEW dissolved**, **EXLINK merged**, **CREATE merged into TAGGING**, **BIM split → BIM + INTEROP**. Result: every job has one obvious home.

---

## PER-SECTION MOVE TABLE (justification, one line each)

### CORE moves (recommended — low risk, high clarity)
| Section | From → To | Why |
|---|---|---|
| Populate Tokens · Quality Assurance · Paragraph&Presentation · ISO Completeness Dashboard · Family&Display · Export · Token Inspector · Setup *(CREATE)* | CREATE → **TAGGING** | CREATE is entirely tag-lifecycle; folding it ends the ORGANISE/CREATE split (CREATE tab removed) |
| View Tag Style · Tag Style Engine · Colouriser · AI Context-Aware Tag Placement · Parameter Anomaly Detection | VIEW → **TAGGING** | these are tag appearance/placement — they belong with tagging, not a "view" tab |
| View Controls · Project Health Score · Analysis Heatmaps (AVF) | VIEW → **DOCS** | view-level analysis/visualisation sits naturally beside views/sheets (VIEW tab then removed) |
| Export Center · Sheet Index | ORGANISE → **DOCS** | documentation output stranded in the tagging tab → move to the docs home |
| *(rename)* ORGANISE → **TAGGING** | — | after the merges the tab is the one tagging home; name it for what it is |
| *(rename)* TEMP → **SETUP** | — | "TEMP" is opaque; contents are project setup & standards |
| *(rename)* TAGS → **TAG STUDIO** | — | disambiguates the advanced studio from the everyday TAGGING tab |

### INTEROP split (recommended, but larger move — your call to include now or defer)
| Section | From → To | Why |
|---|---|---|
| Data Pipeline · IFC Ingestion · Excel Link · Platform Integration · Speckle ×3 · Data Export (ExLink style) · Cross-System Automation · platform·scheduling·cost *(Group-5)* | BIM → **INTEROP** | data-exchange is a distinct job from ISO-19650 coordination; unloads the overloaded BIM tab |
| entire EXLINK tab (Excel Link Files · Batch Export · Model Explorer · ISB Schedules · external Sticky Notes) | EXLINK → **INTEROP** | same job as the above; removes a thin standalone tab (EXLINK tab removed) |

### Intentionally NOT moved (flagged)
| Section | Decision |
|---|---|
| BIM → "Lightning Protection" | Overlaps the dedicated **LPS panel**. Leave for now (removing ≠ reorg). Flag for a later "dedicated-panel dedupe" round, not this one. |
| BIM → "MEP Schedules", TEMP → "MEP Schedules", TEMP → "SLD Generator", "Symbols&Devices" | Quick-access duplicates of dedicated-panel work. Leave (moving/removing would be a dedupe decision, out of 3b scope). Flagged in PHASE3_NOTES. |
| TAGS → Scale sub-tab | **Untouched** (hard constraint). |
| Healthcare tab (all) | **Untouched** (dispatcher + selection model). |

---

## RISK / EXECUTION NOTES
- Moves are **cut-whole-section / paste-into-target-tab**; tags + `Click="Cmd_Click"` ride along, so dispatch is unchanged. Build must stay 0/0.
- TAGGING will be a tall scrollable tab (~37 sections) — acceptable (BIM is similar today). If you'd rather, I can keep ORGANISE and CREATE as two tabs (skip that merge) — say so.
- The INTEROP split is the biggest single move; if you want a smaller first step, approve the **CORE moves only** and I'll leave BIM/EXLINK as-is for a later pass.
- No new section headers are inserted in 3b unless a merged tab needs a divider; I'll use `StingDivider`/`StingSectionHeader` from 3a where a seam needs marking.

## What I need from you
1. Approve the **map** (9 tabs) — or tell me which renames/merges to drop.
2. Approve **CORE moves** and separately the **INTEROP split** (include now / defer).
3. Confirm "TAGGING merges CREATE in" vs "keep CREATE separate."

On approval I apply the moves, build 0/0, push the final commit on `feature/phase3`, and pause for your Revit verification.
