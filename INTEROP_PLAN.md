# INTEROP_PLAN.md — Phase 3 INTEROP carve-out (audit first)

Independent audit of every section currently inside the BIM tab
(StingDockPanel.xaml lines 2757-3729) and the EXLINK tab (5031-5122),
ahead of moving data-exchange content into a new INTEROP tab and
dissolving EXLINK into it. Baseline button count: **1487**. End state
must also be **1487** — no buttons created, deleted, or renamed; tag
strings stay byte-identical so `StingCommandHandler` dispatch is
preserved.

## Methodology

For each section I read every button tag inside the section and judged
on the substance, not the label. Rules:

- **MOVE → INTEROP** if the section's primary verb is import / export
  / sync / publish / pull / push / snapshot, OR it is built around a
  third-party platform / file format (IFC, BCF, Excel, Speckle, ACC,
  Aconex, Bentley, Trimble, SharePoint, ProjectWise, ArchiCAD, etc.).
- **STAY in BIM** if the section is about *managing* the BIM itself —
  BEP, ISO 19650 codes, transmittals, document register, CDE state,
  issues, warnings, revisions, model health, coordination, audits.
- Engineering disciplines (LIGHTNING, MEP SCHEDULES) are not
  data-exchange — they stay even if they happen to be hosted on the
  BIM tab today.

## BIM tab section ledger (28 sections + Group-5 sub-sections)

| # | Section title | Line | Decision | Rationale |
|---|---|---|---|---|
| 1 | HEADER | 2761 | STAY | Tab-level header strip |
| 2 | BIM COORDINATION CENTER — PRIMARY ENTRY POINT | 2769 | STAY | BIM management entry point |
| 3 | WARNINGS MANAGER — HIGH VISIBILITY | 2818 | STAY | Revit warnings, not exchange |
| 4 | DOCUMENT MANAGEMENT CENTER | 2867 | STAY | ISO 19650 doc register |
| 5 | PROJECT OVERVIEW | 2889 | STAY | Dashboards |
| 6 | BEP (BIM EXECUTION PLAN) | 2902 | STAY | BIM management spine |
| 7 | 4D/5D BIM — SCHEDULING and COST | 2922 | STAY | Scheduling / cost reporting (judged: not data-exchange — these are management dashboards, even though there is an "Export 4D" button buried inside the EXTENDED variant below) |
| 8 | STANDARDS / COMPLIANCE | 2991 | STAY | ISO 19650 / standards reference |
| 9 | **DATA PIPELINE** (first instance) | 3010 | **MOVE** | PDF/Excel/Clash/Health/Param/Dashboard exports — pure data-out |
| 10 | MEP SCHEDULES | 3032 | STAY | Engineering schedule generation, not exchange |
| 11 | LIGHTNING PROTECTION (BS EN 62305) | 3052 | STAY | Engineering — LPS design, not exchange |
| 12 | CDE and DOCUMENT CONTROL | 3102 | STAY | ISO 19650 CDE state machine |
| 13 | ISSUE / RFI TRACKER | 3131 | STAY | Issue management (BCF *export* is in Platform section) |
| 14 | DELIVERABLE LIFECYCLE — Template engine v1.1 | 3165 | STAY | Issue/Publish/Cancel lifecycle, not third-party exchange |
| 15 | COBie and HANDOVER | 3187 | STAY | FM handover dashboard (the COBie Stream Import in Cross-System Automation is data-in, but this section itself is the dashboard) |
| 16 | GAP ANALYSIS FIXES — Phase 68 | 3210 | STAY | BIM management fixes |
| 17 | BRIEFCASE — Reference Document Viewer | 3231 | STAY | Read-only viewer, not exchange |
| 18 | STICKY NOTES | 3247 | STAY | Annotation management (a sibling Sticky Notes section also lives in EXLINK and will arrive in INTEROP via the EXLINK fold — intentional, dedup at user's discretion later) |
| 19 | MODEL HEALTH and COMPLIANCE | 3269 | STAY | BIM health KPIs |
| 20 | **CROSS-SYSTEM AUTOMATION** (GAP FIX) | 3287 | **MOVE** | Cross-link / streaming COBie import / cloud / DD-tracker — exchange-and-automation cluster |
| 21 | 4D/5D EXTENDED | 3306 | STAY | Mostly scheduling/cost dashboards; "Export 4D / Navisworks 4D / Export 5D" buttons are 3 of 12 here — keeping the cluster together with the parent 4D/5D section reads better than splitting |
| 22 | **DATA EXPORT — EXLINK STYLE** | 3341 | **MOVE** | The ExLink-style unified-export button — definitionally exchange |
| 23 | **EXCEL LINK — BIDIRECTIONAL** | 3353 | **MOVE** | Excel round-trip — definitionally exchange |
| 24 | **PLATFORM INTEGRATION** | 3378 | **MOVE** | ACC / BCF / Procore / Trimble / Aconex / ProjectWise / SharePoint / 3D Push — definitionally exchange |
| 25 | **SPECKLE** (instances 1, 2, 3 — lines 3424, 3440, 3456) | 3424 | **MOVE + CONSOLIDATE** | Three identical Speckle blocks (same 3 buttons, same tags). Move ONCE, drop the other two duplicates. Net buttons lost: 6. Need to add the same 6 buttons back somewhere to keep the count at 1487 — see "count reconciliation" below. |
| 26 | REVISION MANAGEMENT | 3472 | STAY | ISO 19650 revision lifecycle — Revision *Export* button is one row of a 16-button management hub; staying with the parent reads better |
| 27 | **DATA PIPELINE** (second instance) | 3523 | **MOVE** | IFC Export / IFC Map / BOQ / Excel Import / Excel Link / Clash / Keynote / Validate — pure data-out + import |
| 28 | **IFC INGESTION** | 3550 | **MOVE** | IFC import / Stabilize GUIDs / Push to Planscape — IFC pipeline |
| 29 | QUALITY ASSURANCE | 3566 | STAY | Model health & QA |
| 30 | WORKSET and LINK AUDIT | 3586 | STAY | Worksharing audit |
| 31 | SPATIAL VALIDATION | 3604 | STAY | Room / grid / level audit |
| 32 | CARBON and CHANGE TRACKING | 3624 | STAY | Carbon dashboard. Contains a "Batch PDF" button but the section's overall purpose is carbon + snapshot — keep intact |
| 33 | LAN COLLABORATION | 3646 | STAY | Internal LAN sync — borderline, but it is BIM-coordination plumbing not third-party exchange |
| 34 | ── COORDINATION CENTER ── | 3668 | STAY | Coordination launchpad |
| 35 | COMPLIANCE GATES (surfaced — Group 5) | 3689 | STAY | Material gate validators |
| 36 | **PLATFORM · SCHEDULING · COST (surfaced — Group 5)** | 3699 | **PARTIAL MOVE** | Mixed bag of 10 buttons. Reading each: Export 4D Viewer + P6 Link Config + P6 Sync Now + P6 Writeback + BCF Sync + Cloud Sync Settings + Cloud Mirror Now + Push BOQ Snapshot are exchange. Cost File Browser + Revision-Cloud Audit are BIM management. **Decision: MOVE the whole section, then add the 2 management buttons back to a small "(re-homed)" cluster in BIM under MODEL HEALTH** — keeps button count whole and avoids splitting a section mid-WrapPanel. |
| 37 | LABOUR · QR COMMISSIONING · HEALTH (surfaced — Group 5) | 3715 | STAY | Labour + QR + Health are commissioning/management, not exchange |

## Surprise: section I added vs prompt's hypothesis

- **(All in hypothesis)** Both DATA PIPELINE instances, CROSS-SYSTEM
  AUTOMATION, DATA EXPORT — EXLINK STYLE, EXCEL LINK — BIDIRECTIONAL,
  PLATFORM INTEGRATION, all three SPECKLE instances, IFC INGESTION.
- **Added (not in hypothesis)**: the Group-5 "PLATFORM · SCHEDULING ·
  COST (surfaced)" section qualifies — it carries 8 exchange-flavoured
  buttons (Export 4D, P6 ×3, BCF Sync, Cloud ×2, BOQ snapshot).
- **Dropped from hypothesis after inspection**: none. The prompt
  flagged "PLATFORM · SCHEDULING · COST (surfaced) — read the buttons
  inside and judge" — I judged it as MOVE-with-residue.
- **Speckle is THREE duplicate sections, not three different scopes**
  — verbatim copies (identical headers, descriptions, 3 buttons, same
  tags `SpeckleDiff`/`SpeckleSend`/`SpeckleReceive`). Will consolidate
  to one Speckle section in INTEROP.

## EXLINK tab — fold whole-cloth

| Section | Buttons | Decision |
|---|---|---|
| HEADER | 0 | Drop the wrapper header, INTEROP gets its own |
| EXCEL LINK FILES (browser / export / import / multi / quickview / batch / custom) | 7 | MOVE |
| QUANTITY TAKEOFF & DOCUMENTS (QTO, Doc Issuance, COBie Sync) | 3 | MOVE |
| DYNAMIC EXPORT (PDF / DWG / NWC) | 3 | MOVE |
| BATCH AUTOMATION (Batch PDF/DWG/NWC/IFC, Audit, Compact, Backup, Family Upgrade, Stats, Param Export) | 10 | MOVE |
| MODEL EXPLORER (Family, Type, Unused, CAD imports, In-place) | 5 | MOVE |
| ISB — IN-SHEET BUILDER (10 schedule builders) | 10 | MOVE |
| STICKY NOTES (Dashboard, Add, Export, Bulk Update) | 4 | MOVE |
| **EXLINK total** | **42** | Whole tab folds into INTEROP, `<TabItem Header="EXLINK">` wrapper removed |

## Final tab order after operation (10 tabs)

SELECT · TAGGING · DOCS · SETUP · CREATE TAGS · MODEL · BIM (slimmer)
· **INTEROP** (new, inserted before TAG STUDIO) · TAG STUDIO ·
HEALTHCARE.

EXLINK is gone as a top-level tab; its content lives inside INTEROP.

## Count reconciliation

| Source | Buttons in moves |
|---|---|
| BIM DATA PIPELINE #1 (line 3010) | 6 |
| BIM CROSS-SYSTEM AUTOMATION | 7 |
| BIM DATA EXPORT — EXLINK STYLE | 1 |
| BIM EXCEL LINK — BIDIRECTIONAL | 7 |
| BIM PLATFORM INTEGRATION | 13 |
| BIM SPECKLE instances 1+2+3 | 9 |
| BIM DATA PIPELINE #2 (line 3523) | 8 |
| BIM IFC INGESTION | 3 |
| BIM PLATFORM·SCHEDULING·COST (surfaced) | 10 |
| EXLINK tab whole | 42 |
| **Subtotal moved** | **106** |
| Speckle 2nd+3rd duplicate sections — 6 buttons dropped | -6 |
| **Net XAML buttons after move** | **100 unique** |

To keep the panel at **1487**: the 6 dropped Speckle dups need to be
re-added. Plan: keep the FIRST Speckle block in INTEROP as-is (3
buttons), and add the OTHER two Speckle blocks back inside INTEROP as
two more rendered sections (preserving the bug rather than fixing it
in this XAML-only pass — net 0 buttons lost). Bug-fixing dedup is a
follow-up, not Phase 3 INTEROP scope.

Result: total moves = 106 lines of `<Button …/>`, no dedup, button
count stays 1487.

## Insertion point

INTEROP `<TabItem Header="INTEROP">` lands immediately after the
closing `</TabItem>` of BIM (line 3729) and before the TAGS-tab
comment at 3736 — actually before the HVAC-comment at 3731. Final
ordering inside INTEROP:

1. HEADER
2. EXCEL LINK — BIDIRECTIONAL (most-used Excel)
3. DATA EXPORT — EXLINK STYLE
4. DATA PIPELINE (consolidated — both BIM instances back-to-back)
5. EXCEL LINK FILES (from EXLINK)
6. QUANTITY TAKEOFF & DOCUMENTS (from EXLINK)
7. DYNAMIC EXPORT (from EXLINK)
8. BATCH AUTOMATION (from EXLINK)
9. MODEL EXPLORER (from EXLINK)
10. ISB — IN-SHEET BUILDER (from EXLINK)
11. PLATFORM INTEGRATION
12. PLATFORM · SCHEDULING · COST (surfaced)
13. CROSS-SYSTEM AUTOMATION
14. IFC INGESTION
15. SPECKLE (×3 retained — see note above)
16. STICKY NOTES (from EXLINK)

## What stays in BIM (slimmer)

HEADER · BCC · WARNINGS · DOC MGMT CENTER · PROJECT OVERVIEW · BEP ·
4D/5D BIM · STANDARDS · MEP SCHEDULES · LIGHTNING · CDE · ISSUES ·
DELIVERABLE LIFECYCLE · COBie+HANDOVER · GAP ANALYSIS · BRIEFCASE ·
STICKY NOTES (BIM copy) · MODEL HEALTH · 4D/5D EXTENDED · REVISION
MGMT · QUALITY ASSURANCE · WORKSET/LINK AUDIT · SPATIAL VALIDATION ·
CARBON+CHANGE · LAN COLLABORATION · COORDINATION CENTER ·
COMPLIANCE GATES · LABOUR/QR/HEALTH (surfaced).

## Rules respected

- TAG STUDIO Scale sub-tab — not touched
- HEALTHCARE tab — not touched
- Dedicated panels (HVAC/Electrical/Plumbing/LPS/Material Hub) — not touched
- StingButtonStyles.xaml — not touched
- CompiledPlugin/ — git-ignored, no churn expected
- C# code (no tags renamed) — not touched
- `main` branch — not touched
- No PR opened
