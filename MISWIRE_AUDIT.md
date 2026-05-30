# Mis-wired Buttons Audit (re-derived, not from hints)

**Branch (audit):** `fix/miswired-buttons` (off `main`).
**Implementation branch:** `fix/miswire-cleanup` (off `main`) — see **IMPLEMENTED** at the end.
**Status:** decisions applied; build 0/0.

## Scope & definition

A **mis-wire** = a *visible button* whose **label promises feature X**, but whose
handler runs a **semantically different command Y**, where **X was never built**.
That is worse than a dead button: it misleads.

**Excluded by design:**
- Deliberate aliases / cross-surface re-exposure (DEDUPE Group 2) where the label
  honestly names the command it runs (e.g. `TagStudio_SmartPlace` → SmartPlace).
- **Dead alias *tags* with no button** — they mislead nobody. Found several the
  DEDUPE Group 3 hypothesis listed as mis-wires that turn out to have **no button**.

## Dispatch surfaces checked (all of them)

1. `UI/StingCommandHandler.cs` — the 1,839-case switch + `RunCommand<T>` (passes no tag).
2. `UI/Modules/*CommandModule.cs` — `CommandRegistry`, tried **before** the switch.
3. `UI/StingDockPanel.xaml` — `Content=` label + `Tag=` + `Click="Cmd_Click"`.
4. `UI/BIMCoordinationCenter.cs` — code-built `MakeActionButton(label, tag, …)`.
5. `Core/WarningsManager.cs` — **Phase 167 BCC action-alias dictionary** (a 2nd
   dispatch layer: action-tag → canonical-tag for the Coordination Center).
6. `UI/DocumentManagementDialog.cs` — code-built dialog buttons + `DispatchAction`.

Every confirmed mis-wire below is a **real visible button** (label quoted verbatim).

---

## Corrections to the DEDUPE Group-3 hypothesis (verify-first paid off)

| Hypothesis item | Verdict | Evidence |
|---|---|---|
| `SaveExtendedBaseline` → `SelectByDisciplineCommand` (mis-wire) | **FALSE** | `case "SaveExtendedBaseline"` is its **own correct inline handler** calling `Core.WarningsEngine.SaveExtendedBaseline(d)` (StingCommandHandler.cs:2515). Does exactly what it says. Not a mis-wire. |
| `UnicodeValidator` → `UniclassClassify` (mis-wire) | **N/A — no button** | `UnicodeValidator` is a legacy alias **tag only** (switch case, comment "Legacy alias"). No `Content=`/`MakeActionButton` anywhere → misleads nobody. Dead-tag cleanup, not a mislabel. |
| Planscape member cluster (`PlanscapeAddMember/RemoveMember/LinkProject/TestConnection/ShareReport/ExportConfig/ExportTeam`, `ViewPlatformLogs`) | **N/A — no buttons** | These exist **only** in the switch + the WarningsManager alias dict (defensive entries: the dict comment says they exist "so the resolver lookup hits a real command and never falls through to the toast"). **Zero buttons build them.** Dead alias tags, not mislabels. |
| `ISO19650Checker` → `Iso19650DeepComplianceCommand` | **NOT a mislabel** | Same standard. Label "ISO 19650" honestly → an ISO 19650 compliance check. Honest alias. |
| `UniclassValidator` / `ClassificationAudit` → `UniclassClassifyCommand` | **NOT a mislabel** | Uniclass *is* the classification system; "Classification" → Uniclass classify is honest. |
| `ExportDeliverablesRegister` → DeliverableMatrix/DocumentRegister | **NOT a mislabel** | A document register export ≈ a deliverables register. Honest enough. |
| `BatchPrintSheets` → `ExportCenterPdfCommand` | **Low (redirect)** | Batch-printing sheets ≈ exporting them to PDF; comment says "redirects to Export Centre (PDF preset)". ⚠️ A real `BatchPrintSheetsCommand` (SheetTemplateCommands.cs) **exists but is bypassed** — noted, low harm. |

---

## CONFIRMED MIS-WIRES (real buttons whose label lies)

### Cluster A — IoT / Digital-Twin family (the strongest cluster)
The IoT features were **never built**; each button borrows an adjacent FM command.
Buttons appear on **two** placements (a main pipeline area ~XAML:2980, and an FM
sub-tab ~XAML:5265). One button's **own tooltip admits it**: *"Link a Revit element
to an IoT sensor record (AssetConditionCommand)."*

| Label (verbatim) | Tag | Claims | Actually runs | Promised cmd exists? | Conf |
|---|---|---|---|---|---|
| "Sensor Link" | `IoTSensorLink` | Link element ↔ live IoT sensor | `Temp.AssetConditionCommand` (static ISO 15686 condition rating) | No | **High** |
| "Alert Config" | `IoTAlertConfig` | Configure IoT alert thresholds | `Temp.EnergyAnalysisCommand` (energy analysis — unrelated) | No | **High** |
| "IoT Dashboard" / "Dashboard" | `IoTDashboard` | Live IoT telemetry dashboard | `Temp.MaintenanceScheduleCommand` (PPM schedule — unrelated) | No | **High** |
| "History Export" / "Export CSV" | `IoTHistoryExport` | Export IoT sensor time-series | `Temp.DigitalTwinExportCommand` (twin model export — adjacent, not sensor history) | No | **Med-High** |

### Cluster B — Standards "Checker" mismatch
| Label | Tag | Claims | Actually runs | Promised cmd exists? | Conf |
|---|---|---|---|---|---|
| "BS 1192" | `BS1192Checker` | BS 1192 info-management check | `Temp.Bs7671ComplianceCommand` (**BS 7671 — electrical wiring**, a different standard) | No | **High** |

### Cluster C — Coordination / Issues / Approval
| Label | Tag | Claims | Actually runs | Promised cmd exists? | Conf |
|---|---|---|---|---|---|
| "Webhook" | `WebhookPayload` | Configure/preview a webhook payload | `BIMManager.BCFExportCommand` (BCF 2.1 export — unrelated) | No | **High** |
| "Meeting Templates" | `MeetingTemplates` | Insert/manage meeting templates | `BIMManager.ExportCoordLogCommand` (export coordination log — unrelated) | No | **High** |
| "Assign Issues" / "Reassign" | `AssignIssues` | Multi-assign issues to team by role/discipline | `BIMManager.RaiseIssueCommand` (raise a *new* single issue) | No (but `UpdateIssueCommand` exists — the natural target) | **High** |
| "From Warnings" | `CreateIssuesFromWarnings` | Auto-create NCR/SI from Revit warnings | `BIMManager.RaiseIssueCommand` (manual single raise) | No | **High** |
| "Approval Workflow" | `ApprovalWorkflow` | Submit docs for ISO 19650 approval | `BIMManager.DocumentRegisterCommand` (switch) / `CDEStatus` (BCC dict) | **YES — `CDEApprovalWorkflowCommand` exists, unused** | **High** |

> Note: `ApprovalWorkflow` resolves to **two different things** depending on surface
> (switch → DocumentRegister; BCC alias dict → CDEStatus). Neither is the real
> `CDEApprovalWorkflowCommand`.

### Cluster D — Colour / Viewport / View (Medium)
| Label | Tag | Claims | Actually runs | Promised cmd exists? | Conf |
|---|---|---|---|---|---|
| "Apply Gradient" | `GradientApply` | Continuous gradient colouring | `Select.ColorByParameterCommand` (categorical / palette by value) | No gradient command | **Med** |
| "→Right" | `VPAlignRight` | One-click align viewports to right edge | `Docs.AlignViewportsCommand` (general align dialog — you re-pick the edge) | No right-specific cmd | **Med** |
| "Find&Replace" | `SheetFindReplace` | Find/replace on **sheet** names | `Docs.BatchRenameViewsCommand` (**views** rename dialog) | No sheet find/replace cmd | **Med** |

### Cluster E — QR family (Medium — single generator behind 3 distinct labels)
All three run the one `Tags.QRCodeCommand`; no sheet-vs-tag-vs-link differentiation.
| Label | Tag | Claims | Actually runs | Conf |
|---|---|---|---|---|
| "QR Sheet Register" | `GenerateQRSheet` | A sheet/register of QR codes for all tagged elements | `Tags.QRCodeCommand` | **Med** |
| "Print QR Tags" | `PrintQRTags` | Printable QR label sheet for FM | `Tags.QRCodeCommand` | **Med** |
| "Generate QR Link" | `PlanscapeQR` | QR linking to the exported HTML dashboard | `Tags.QRCodeCommand` | **Med** |

### Cluster F — COBie (Low-Med)
| Label | Tag | Claims | Actually runs | Conf |
|---|---|---|---|---|
| "COBie Check" | `COBieValidator` | Validate COBie data | `Temp.COBieDataSummaryCommand` (summary, not a pass/fail validate) | **Low-Med** |

### Cluster G — Deliverables (Low — BCC button is partly real)
| Label | Tag | Claims | Actually runs | Conf |
|---|---|---|---|---|
| "Bulk Status" | `BulkDeliverableStatus` | Bulk-set deliverable status | BCC button **does** apply status inline (DocumentManagementDialog.cs:8048) then dispatches `DocumentRegister`; switch → `DeliverableMatrixCommand` | **Low** |

---

## Proposed fix per cluster (your call — I will NOT build anything without per-item go-ahead)

| Cluster | Cheapest honest fix (a relabel) | (b) remove | (c) build/rewire — needs your greenlight |
|---|---|---|---|
| **A — IoT (4 tags, ~8 buttons)** | Relabel to the FM command each runs — **but** those labels would collide with the existing Asset-Condition / Maintenance-Schedule / Energy / Twin buttons (dups). So relabel is awkward. | **Recommended default: remove the 4 IoT buttons** (features unbuilt; FM equivalents already have honest buttons). | Build a real IoT/twin integration (large). |
| **B — BS 1192** | Relabel "BS 1192" → "BS 7671" — **but** a BS 7671 button likely already exists (would dup). | **Recommended: remove** the BS 1192 button. | Build a real BS 1192 / ISO 19650 info-mgmt checker. |
| **C — Webhook** | — (no honest near-label) | **Recommended: remove.** | Build webhook config. |
| **C — Meeting Templates** | Relabel "Export Coord Log" (already exists → dup) | **Recommended: remove.** | Build meeting-template manager. |
| **C — Assign Issues / From Warnings** | Relabel both → "Raise Issue" (honest but loses intent) | remove | **(c) rewire**: `AssignIssues` → `UpdateIssueCommand` (exists); build a real warnings→issue command for `CreateIssuesFromWarnings`. |
| **C — Approval Workflow** | Relabel → "Update CDE Status" (matches BCC behaviour) | remove | **(c) rewire to the existing `CDEApprovalWorkflowCommand`** — the real feature is already built, just unwired. **My recommendation: greenlight this rewire** (cheap, no new code, makes the label true). |
| **D — Apply Gradient** | Relabel "Color by Parameter" | remove | build a gradient mode in ColorByParameter. |
| **D — →Right** | Relabel "Align…" | keep+relabel | build a one-click right-align. |
| **D — Find&Replace** | Relabel "Rename Views…" (it renames views) | — | build sheet find/replace. |
| **E — QR (3 tags)** | Relabel all three honestly to "Generate QR" (or keep one, remove 2 dups) | remove 2 | build distinct sheet/tag/link QR variants. |
| **F — COBie Check** | Relabel "COBie Summary" | — | build a real COBie validator. |
| **G — Bulk Status** | Leave (BCC behaviour is mostly honest) — lowest priority | — | — |

---

## What I'll do after your decision (per your 3-step rule)
- Apply **relabels** and **removals** you approve, per cluster.
- Leave every **(c) build** item as a tracked TODO **unless you greenlight it**.
- The one (c) I actively recommend: **rewire `ApprovalWorkflow` → existing
  `CDEApprovalWorkflowCommand`** (the feature exists; this just stops the label lying
  with zero new code).
- Untouched: TAGS → Scale sub-tab. No label left lying.

**Decisions I need (per cluster A–G):** relabel / remove / build (or rewire for C-Approval).
Default recommendations are in the table; tell me where to deviate.

---

## IMPLEMENTED (Group 3 cleanup, branch `fix/miswire-cleanup`)

Applied the user's per-cluster decisions. Build 0 warnings / 0 errors, no CompiledPlugin churn.

### Two corrections found *during* implementation (verifying the live surface, not just the switch)

1. **Cluster C "Meeting Templates" is NOT a mis-wire — left in place.** The switch case
   `MeetingTemplates → ExportCoordLogCommand` is a **dead surface no button reaches**. The
   live BCC "Meeting Templates" button dispatches through `WarningsManager.ProcessAction`,
   whose inline `case "MeetingTemplates"` **short-circuits to
   `DocumentManagementDialog.ShowMeetingTemplates(doc)`** (a real method) and `return`s
   before the dict/`GetCommandInstance` fallback. So the label is honest. **Not removed.**
   (The dead switch case was left as harmless defensive code.)

2. **Cluster E QR had a *fourth*, honest button.** The BCC qrWrap actually held 3 buttons:
   "Generate QR for Selected" (`GenerateQRCode`, **honest** — matches `QRCodeCommand`) plus
   the two lying duplicates. Collapsed by **removing the 2 duplicates and keeping the honest
   one**, rather than relabelling identically.

### Actions taken

| Cluster | Action | Surfaces edited |
|---|---|---|
| **A — IoT/Twin (4 tags)** | **REMOVED** all 8 buttons (whole "IoT — SENSOR DATA" section in the main panel + 4 buttons from the Healthcare IoT sub-tab, keeping the real `Healthcare_IoTRegistry`/`Healthcare_IoTStaleness`) + the 4 switch cases | `StingDockPanel.xaml`, `StingCommandHandler.cs` |
| **B — BS 1192** | **REMOVED** the "BS 1192" button + switch case (ran BS 7671) | `StingDockPanel.xaml`, `StingCommandHandler.cs` |
| **C — Webhook** | **REMOVED** the "Webhook" button + switch case (ran BCF export) | `StingDockPanel.xaml`, `StingCommandHandler.cs` |
| **C — Meeting Templates** | **KEPT** — verified honest (see correction 1) | — |
| **C — Approval Workflow** | **REWIRED** to the existing `CDEApprovalWorkflowCommand` on **both** surfaces: switch case + the BCC alias dict (`ApprovalWorkflow → CDEApprovalWorkflow`); added a `CDEApprovalWorkflow` case to `WorkflowEngine.ResolveCommand` so the BCC path resolves it. Label now honest. | `StingCommandHandler.cs`, `WarningsManager.cs`, `WorkflowEngine.cs` |
| **D — Apply Gradient** | **RELABELLED** "Apply Gradient" → "Color by Value" (+tooltip) | `StingDockPanel.xaml` |
| **D — →Right** | **RELABELLED** dock "→Right" → "Align…" (+tooltip). Editor 6-align row: see new TODO below | `StingDockPanel.xaml` |
| **D — Find&Replace** | **RELABELLED** dock "Find&Replace" → "Rename…" and editor "Find&Replace" → "Rename Views…" (both → `BatchRenameViewsCommand`) | `StingDockPanel.xaml`, `DrawingTypeEditorDialog.cs` |
| **E — QR** | **COLLAPSED**: removed "QR Sheet Register" + "Print QR Tags" duplicates (kept honest "Generate QR for Selected"); relabelled Planscape-tab "Generate QR Link" → "QR Code"; retired `PrintQRTags` switch case + module reg + WorkflowEngine case | `BIMCoordinationCenter.cs`, `StingCommandHandler.cs`, `WorkflowEngine.cs`, `TagsCommandModule.cs` |
| **F — COBie Check** | **RELABELLED** "COBie Check" → "COBie Summary" (+tooltip; runs `COBieDataSummaryCommand`) | `StingDockPanel.xaml` |
| **G — Bulk Status** | left as-is (per decision) | — |

### Flagged as BUILD TODOs (no relabel/remove — per your decision)

- **C — `AssignIssues` ("Assign Issues" / "Reassign")** → currently runs `RaiseIssueCommand`.
  Promised multi-assign/reassign feature not built. **TODO:** build a real assign path
  (`UpdateIssueCommand` exists and is the natural target). Buttons left unchanged.
- **C — `CreateIssuesFromWarnings` ("From Warnings")** → currently runs `RaiseIssueCommand`.
  Promised auto-create-NCR-from-warnings feature not built. **TODO:** build a
  warnings→issue command. Button left unchanged.
- **E — QR parameterisation** → `QRCodeCommand` takes no variant arg, so distinct
  sheet / print / dashboard-link QR outputs can't be offered honestly. **TODO:** parameterise
  `QRCodeCommand` (sheet vs per-tag vs dashboard-link), then re-add distinct buttons.

### NEW cluster found during implementation (not in the original audit) — flagged, not changed

- **D2 — DrawingTypeEditorDialog 6-button viewport-align row.** `VPAlignTop / VPAlignMidY /
  VPAlignBot / VPAlignLeft / VPAlignMidX / VPAlignRight` **all** fall through to the single
  `AlignViewportsCommand` (a dialog where you re-pick the edge) — so each "Align to <edge>"
  label is misleading. Left as a self-consistent set (relabelling 1-of-6 would be worse).
  **TODO (your call):** either collapse the 6 to one "Align viewports…" button, or
  parameterise `AlignViewportsCommand` to honour a per-edge arg. The standalone **dock**
  "→Right" button was relabelled to "Align…" (it had no siblings).

### Net
- Removed: 10 mislabelled buttons (8 IoT + BS 1192 + Webhook) + 2 QR duplicates = 12 buttons.
- Rewired: 1 (Approval Workflow → real command, 3 surfaces).
- Relabelled: 5 (Gradient, dock →Right, dock + editor Find&Replace, COBie).
- Kept (correction): Meeting Templates.
- Build TODOs logged: 3 (Assign, FromWarnings, QR-parameterise) + 1 new (editor align row).
- TAGS → Scale sub-tab: untouched.
