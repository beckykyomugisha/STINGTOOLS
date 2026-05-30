# Phase 3b — INTEROP split PLAN (audit-first)

Branch: `feature/phase3`. **This commit is the PLAN only — no XAML moved.**

The INTEROP split carves data-exchange sections out of the BIM tab into a new
INTEROP tab, then folds the EXLINK tab into INTEROP. End state: BIM is slimmer
and focused on BIM management (BEP/COBie/issues/revisions/coordination); INTEROP
owns everything that pushes data out of (or pulls into) Revit.

Net tab count is unchanged (-EXLINK +INTEROP). Net button count must stay at
**1487**. XAML-only; no command tags renamed, no handler changes.

---

## Baseline (verified `2026-05-30` on `feature/phase3` HEAD `f5f1284fb`)

- StingDockPanel.xaml: 5833 lines · 1487 buttons
- BIM tab: lines 2757–3737 (980 lines, 36 SectionLabel headers in tab)
- EXLINK tab: lines 5031–5125 (94 lines, no SectionLabel — uses inline
  bold TextBlocks for sub-headers)
- Top-level tab count: 10 (SELECT · TAGGING · DOCS · SETUP · CREATE TAGS ·
  MODEL · BIM · TAG STUDIO · EXLINK · HEALTHCARE)

---

## BIM-tab section inventory + decision

| # | Line | Section | Decision | Rationale |
|---|---|---|---|---|
| 1 | 2819 | ⚠ WARNINGS MANAGER | **STAY** | Model warning triage — BIM coordination |
| 2 | 2868 | DOCUMENT MANAGEMENT CENTER | **STAY** | ISO 19650 register / CDE workflow |
| 3 | 2890 | PROJECT OVERVIEW | **STAY** | BIM dashboard launcher |
| 4 | 2903 | BEP — BIM EXECUTION PLAN | **STAY** | BEP authoring/export — core BIM |
| 5 | 2923 | 4D/5D BIM — SCHEDULING & COST | **STAY** | Scheduling/cost = BIM management |
| 6 | 2992 | STANDARDS — COMPLIANCE | **STAY** | ISO/CIBSE/Uniclass standards reference |
| 7 | 3011 | DATA PIPELINE *(first)* | **MOVE → INTEROP** | PDF/QTO/Excel exports + batch params = data exchange |
| 8 | 3033 | MEP SCHEDULES | **STAY** | Schedule generation — BIM authoring |
| 9 | 3053 | ⚡ LIGHTNING PROTECTION | **STAY** | LPS commands — engineering, dedicated panel surface |
| 10 | 3103 | CDE — DOCUMENT CONTROL | **STAY** | ISO 19650 CDE workflow |
| 11 | 3132 | ISSUE / RFI TRACKER | **STAY** | BIM issue management |
| 12 | 3166 | DELIVERABLE LIFECYCLE — Template engine v1.1 | **STAY** | ISO 19650 deliverable state machine |
| 13 | 3188 | COBie V2.4 — FM HANDOVER | **STAY** | COBie generator (not an external sync) |
| 14 | 3211 | GAP ANALYSIS — AUTOMATION | **STAY** | BIM coordination automation (BEP stage / issue-rev links / minutes) |
| 15 | 3232 | BRIEFCASE — REFERENCE DOCS | **STAY** | In-Revit doc viewer — BIM workflow |
| 16 | 3248 | STICKY NOTES | **STAY** | Per-element BIM notes (separate from EXLINK's notes section) |
| 17 | 3270 | MODEL HEALTH & COMPLIANCE | **STAY** | Health dashboard / MIDP — BIM management |
| 18 | 3288 | CROSS-SYSTEM AUTOMATION | **MOVE → INTEROP** | Mixed but dominantly streaming-COBie / cross-link / cloud-sync flavour; per user prompt |
| 19 | 3307 | 4D/5D EXTENDED | **STAY** | Phase / milestones / calendar = BIM scheduling. Has some exports (Export 4D, Navisworks, Export 5D) but the section's identity is 4D/5D project workflow |
| 20 | 3342 | DATA EXPORT — EXLINK STYLE | **MOVE → INTEROP** | Explicit ExLink-style data export |
| 21 | 3353 | EXCEL LINK — BIDIRECTIONAL | **MOVE → INTEROP** | Excel round-trip data exchange |
| 22 | 3379 | PLATFORM INTEGRATION | **MOVE → INTEROP** | ACC / BCF / SharePoint / CDE platform sync |
| 23 | 3425 | SPECKLE *(instance 1)* | **MOVE → INTEROP** | Speckle is a data-exchange platform |
| 24 | 3441 | SPECKLE *(instance 2 — byte-identical duplicate of #23)* | **MOVE → INTEROP (keep duplicate)** | Three byte-identical 3-button Speckle blocks (Diff/Send/Receive) exist today. They are accidental duplicates from a prior copy-paste, not three distinct scopes. Dropping them would violate the 1487 button-count guard, so all three move intact. A separate dedup commit can drop two later, deliberately. |
| 25 | 3457 | SPECKLE *(instance 3 — byte-identical duplicate of #23)* | **MOVE → INTEROP (keep duplicate)** | See #24 |
| 26 | 3473 | REVISION MANAGEMENT | **STAY** | Revision workflow — BIM management (some export buttons inside, but section identity is revision tracking) |
| 27 | 3524 | DATA PIPELINE *(second)* | **MOVE → INTEROP** | IFC / BOQ / Excel / Keynote = data exchange |
| 28 | 3551 | IFC INGESTION | **MOVE → INTEROP** | IFC import + push to Planscape — pure interop |
| 29 | 3567 | QUALITY ASSURANCE | **STAY** | Model QA / health scan — BIM |
| 30 | 3587 | WORKSET and LINK AUDIT | **STAY** | Worksharing audit — BIM management |
| 31 | 3605 | SPATIAL VALIDATION | **STAY** | Room connectivity audit — BIM |
| 32 | 3625 | CARBON and CHANGE TRACKING | **STAY** | Carbon = BIM (some export inside, but core function is tracking) |
| 33 | 3647 | LAN COLLABORATION | **MOVE → INTEROP** | LAN-based model collaboration / sync = data exchange |
| 34 | 3689 | COMPLIANCE GATES (surfaced) | **STAY** | Stage compliance gate — BIM workflow |
| 35 | 3699 | PLATFORM · SCHEDULING · COST (surfaced) | **MOVE → INTEROP** | P6 sync / BCF sync / Cloud sync / BOQ push = data exchange (one BIM stray — Revision-Cloud Audit — comes along for the ride; acceptable per "whole-section moves") |
| 36 | 3715 | LABOUR · QR COMMISSIONING · HEALTH (surfaced) | **STAY** | Labour hours / QR commissioning / Health dashboard — BIM workflow |

### Sections that the user's hypothesis list did NOT name but I'm moving

- **Section #33 LAN COLLABORATION** — not in the original list but is data-sync between Revit instances over LAN. Pure interop.

### Sections that user named but I am NOT moving

- *(none)* — every hypothesis from the prompt is moving.

### Sections moved that user named that need a footnote

- **CROSS-SYSTEM AUTOMATION** (#18): mixed. Half is CDE-approval workflow
  (arguably BIM), half is COBie streaming import + cross-system entity
  linking + cloud-mirror (arguably interop). User asked it be moved, and
  the "stream import / cross-link / sync" weight tips it interop.
- **PLATFORM · SCHEDULING · COST (surfaced)** (#35): contains one BIM
  outlier (`Revision_CloudAudit`). Moving the section whole per
  "whole-section moves" rule — projects that disagree can rebalance later.

---

## EXLINK tab — fold whole into INTEROP

EXLINK (lines 5031–5125, 94 lines, **0** `SectionLabel` headers, **5** inline
bold TextBlock sub-headers) becomes 5 sections inside INTEROP, all using the
`SectionLabel` style for consistency with the rest of the tab:

| Original sub-header | New INTEROP section name |
|---|---|
| EXCEL LINK FILES | EXCEL LINK FILES |
| QUANTITY TAKEOFF & DOCUMENTS | EXLINK — QTO & DOCUMENTS |
| DYNAMIC EXPORT | EXLINK — DYNAMIC EXPORT |
| BATCH AUTOMATION | EXLINK — BATCH AUTOMATION |
| MODEL EXPLORER | EXLINK — MODEL EXPLORER |
| ISB — IN-SHEET BUILDER | ISB SCHEDULES |
| STICKY NOTES | EXLINK — STICKY NOTES *(retained as duplicate of BIM tab's STICKY NOTES — different tags `StickyNoteCreate` vs `StickyNote`, separate handler chain)* |

Inline `<TextBlock Text="..." FontWeight="Bold" ...>` headers become
`<TextBlock Style="{StaticResource SectionLabel}" Text="..."/>` to match the
rest of the dock panel. Per-button XAML is copied verbatim — tags and handlers
untouched.

The EXLINK header banner (`Border` around "EXLINK — DATA EXCHANGE & AUTOMATION")
gets retained as the INTEROP tab header (relabelled to "INTEROP — DATA EXCHANGE
& EXTERNAL SYNC").

---

## Button arithmetic

| Action | Δ buttons |
|---|---|
| Move 9 BIM sections to INTEROP (DATA PIPELINE ×2 + Cross-System + 4D/5D EXLink Export + Excel Link + Platform Integration + 3× Speckle + IFC Ingestion + LAN Collaboration + Platform·Scheduling·Cost) | 0 (relocation) |
| Fold EXLINK tab into INTEROP | 0 (relocation) |
| **Net** | **0 — count stays 1487** |

The 3-way SPECKLE duplication is preserved deliberately to honour the 1487
count guard. Three identical 3-button strips render in INTEROP. A future
dedup commit can drop two of them (target count 1481) once user explicitly
approves.

---

## End-state tab order (10 tabs, unchanged count)

1. SELECT
2. TAGGING
3. DOCS
4. SETUP
5. CREATE TAGS
6. MODEL
7. BIM *(slimmer — 27 sections)*
8. **INTEROP** *(new — 14 sections: 9 moved-from-BIM + 5 from-EXLINK + Speckle once)*
9. TAG STUDIO
10. HEALTHCARE

EXLINK is removed. INTEROP slots into its line-position to keep TAG STUDIO and
HEALTHCARE in their current order.
