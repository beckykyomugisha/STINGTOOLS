# MODEL tab — Phase B Round 2 redesign plan

Per `docs/UI_PHASE_B_PATTERNS.md`. Scope: MODEL `TabItem`
(StingDockPanel.xaml lines 2324–2760, ~21 sections). MODEL is the
heaviest tab in the panel — STRUCTURAL AUTOMATION alone has ~60
buttons across 9 sub-bands. Goal: keep every dispatch tag reachable,
collapse vertical real estate, promote the star primaries.

Verification gate: AFTER tag-set is a superset of BEFORE
(`grep -oE 'Tag="[A-Za-z_0-9]+"' ... | sort -u`). New runner tags
allowed (`Model_StrAutoRunner` etc.) — never lose an existing tag.

## Decision tree applied per section

| # | Section | Buttons | Pattern | Rationale |
|---|---|---|---|---|
| 1 | HEADER | 0 | leave | Just a label band |
| 2 | QUICK BUILD | 16 | **Pattern 5** | 3 star primaries stay proud; collapse "MULTI-BUILDING SITE (§B1–B6)" 5-button sub-band into Expander; collapse secondary creation row (Ramp / Canopy / Route Analysis) into Expander. Promote Placement Centre / Preset Combinations / DWG Wizard / Building Shell / Room as primaries. |
| 3 | MODEL WIZARD | 1 | Pattern 6 | One star button, already short — keep, just verify tooltip. |
| 4 | ARCHITECTURAL | 9 | **Pattern 1 + 6** | All immediate-action element creators (Wall / Floor / Ceiling / Roof / Door / Window / Opening / Curtain Wall / Stair Design). Shrink to chips with short labels + tooltips, two rows. |
| 5 | STRUCTURAL | 3 | **Pattern 1 + 6** | Column / Column Grid / Beam — chips. |
| 6 | STRUCTURAL AUTOMATION | ~60 | **Pattern 5 ×8** | The big one. 9 sub-bands. Keep the 4 star primaries proud (★★ Build Complete, ★★★ Diagnostics, ★ Grid Frame, ★ Full Report) in a "PRIMARY" row at the top. Every other sub-band ("Foundations", "Framing", "Elements", "Analysis & Auto-Sizing", "DWG → STRUCTURAL BIM", "Structural Analysis", "Advanced Analysis", "Intelligent Automation", "Design & Sustainability", "Precision Intelligence", "Detail Design (EC2/EC3)", "Smart Factory", "EC Code Checks") → individual `<Expander>` collapsed by default, with chip-style buttons inside. Massive visual reduction without losing any function. |
| 7 | COVERINGS | 11 | **Pattern 5** | 3 star primaries stay proud (★★ Smart Covering, ★ Batch All, Room Finishes). 8 secondary actions (Materials / Substrate / Paint System / Coverage / QA / Export / Fire Rating / Moisture Risk) → Expander chips. |
| 8 | EXCEL → STRUCTURAL | 16 | **Pattern 5** | Main row keeps 7 import buttons (chips with Pattern 6). "Enhanced Structural Algorithms" 9-button sub-band → Expander. |
| 9 | FULL AUTOMATION | 1 | leave | One ★★★ star — keep. |
| 10 | MEP | 3 | **Pattern 1 + 6** | Duct / Pipe / Fixture — chips. |
| 11 | FAMILY QUICK EDIT | 6 | **Pattern 5** | ★ Family Quick Edit + Tag Family Params (the two most-used) stay proud; collapse Change Host / Swap Category / Automation Pack / Conformance Check → Expander. |
| 12 | DWG TO MODEL | 2 | **Pattern 1** | Two buttons — chip them, keep DWG → Model proud. |
| 13 | MODEL INTELLIGENCE | 4 | **Pattern 1 + 6** | Embodied Carbon / Floor Efficiency / Room Area Audit / Model Complexity — chips. |
| 14 | BIM COORDINATOR | 5 | **Pattern 1 + 6** | Deliverable Readiness / Action Plan / COBie Readiness / Drawing Issue / Spatial QA — chips. |
| 15 | BIM COORDINATOR PLANNER | 5 | **Pattern 1 + 6** | Daily Planner / Deliverable Matrix / Warning Prediction / Compliance Check / Export Audit Log — chips. |
| 16 | WORKFLOW & COORDINATION | 11 | **Pattern 5** | 11 buttons is too many for one band. Keep Scheduler + Mid-Day Check + Review Prep proud; collapse Root Cause / Suppression Audit / Team Activity / Compliance Trends / SLA Report / Federated Pre-Flight / Transmittal Gate / Container Check → Expander. |
| 17 | ACOUSTIC & SUSTAINABILITY | 3 | **Pattern 1 + 6** | Three buttons — chips. |
| 18 | MEP INTELLIGENCE | 1 | leave | One button. |
| 19 | STRUCTURAL DEEP | 1 | leave | One button. |
| 20 | DOC & SCHEDULE AUTOMATION | 4 | **Pattern 1 + 6** | Four buttons — chips. |
| 21 | WORKFLOW AUTOMATION | 3 | **Pattern 1 + 6** | Three buttons — chips. |

## Code-behind additions

- **REUSE** `OnRadioRouteChecked` (idempotent stash, already on main).
- No new RadioButton routing needed in MODEL — no mutually exclusive
  scope/mode pickers fit naturally. Element creation is per-button.
- No new suite runners needed — Expanders just hide buttons, they
  don't fan out to multiple commands. So **no additions to
  `RunInteropRunner`** required, and no `RunModelRunner` needed in
  this round. Pure XAML diff.

## Tag preservation

Every existing `Tag="..."` value is preserved verbatim; only the
`Content="..."` / structural container (`Expander` wrapping
`WrapPanel`) changes. Pattern 6 short labels move long text to
`ToolTip`. Net new tags: **zero** (no suite runners). Verification
script will assert AFTER ⊇ BEFORE.

## Risk

LOW. All 21 sections are pure regrouping. No control flow change.
Worst case: a user's muscle-memory click target moves into an
Expander; mitigation is the tooltip text + the Pattern 5 rule of
keeping primaries (★ / ★★ / ★★★) proud at the top of each band.
