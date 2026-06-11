# KUT test fixtures

Fixtures for the Phase 192 KUT (Kampala Uganda Temple) work items.

| File | Used by | Purpose |
|---|---|---|
| `program_template_sample.xlsx` | B3 — `Program_Audit` (manual Revit smoke) | 5-row Owner program template: `Room Number`, `Room Name`, `Required Area` (m²), `Department`, `Required Count`, `Building`. Pick this in the Program Audit file dialog against a model with rooms to exercise the area/count compare + missing/extra join. |
| `bluebeam_comments_sample.csv` | C3 — `ReviewComments_Import` (manual Revit smoke) | 8-row synthetic Bluebeam Studio comment summary (`Index`, `Subject`, `Page Label`, `Author`, `Date`, `Status`, `Reply Count`). Mix of Open / Replied / Completed / Accepted / Resolved-pending / Rejected to exercise status normalisation + close-out rate. |

The pure-logic join/compare is covered by
`StingTools.Tags.Tests/ProgramAuditEngineTests.cs` (no Excel/Revit
needed); this XLSX is for end-to-end command smoke testing in Revit.
