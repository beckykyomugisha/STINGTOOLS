# stingtools-bonsai/workflows/

Empty in the Day-1 scaffold. MVP Week 6 work lands here:

| File | Purpose |
|---|---|
| `engine.py` | JSON-driven preset chain runner. Subset of the Revit-side `WorkflowEngine.cs` — handles step ordering, conditional steps (`MinCompliancePct`, `RequiresStaleElements`), audit-log emission. |
| `DailyQA.json` | 8-step daily QA preset: validate, color-by-discipline, dashboard, fix-low-priority-issues, re-validate, audit, summary |
| `ProjectKickoff.json` | Initial-setup preset: load enums, validate spatial structure, baseline compliance scan |
| `IssueDeliverable.json` | Issue-deliverable lifecycle preset: validate IDS pass-rate ≥ 95%, render docx, transmit, archive |

The 3 default presets ship as embedded resources; projects can author
their own at `<project>/_BIM_COORD/workflows/*.json` (same convention
as the Revit-side template engine).
