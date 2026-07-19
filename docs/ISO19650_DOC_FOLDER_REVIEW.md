# Document Manager & Folder Structure Review — ISO 19650 Alignment

**Date**: 2026-07-19 · **Branch**: `claude/document-manager-iso-review-dbb595` · **Type**: review & recommendation (no code changes)

This review answers two questions:

1. Is the Document Manager a *truly* ISO 19650 management system?
2. Why does folder creation produce many disorganised folders (some repeated inside others), and what is the most flexible, sustainable, automatic, tidy way to manage the structure?

**Verdict in one line:** every ISO 19650 ingredient exists somewhere in the codebase — naming engine, S-codes, P/C revisions, CDE folders, register, transmittals, MIDP drift, hash-chained audit — but they exist **two-to-five times each, in disconnected subsystems that write to different folders**, so neither the folder tree nor the document lifecycle is a single system of record. The fix is consolidation, not new features.

---

## Part 1 — Folder creation: findings

### 1.1 Scale of the sprawl

- ~250 `Directory.CreateDirectory` call sites; ~90 hard-coded `_BIM_COORD` path builders.
- A single saved project can realistically spawn **6–10 sibling folders next to the .rvt**, out of roughly **20 distinct root trees** the plugin can create:
  - `<CODE>/` (intended unified ISO tree, `Core/ProjectFolderEngine.cs`)
  - `<rvtDir>/_BIM_COORD/` (~40 writers across BOQ, Cost, Docs, Symbols, Validation)
  - `<rvtDir>/STING_BIM_MANAGER/` (`BIMManager/BIMManagerCommands.cs:703`)
  - `<rvtDir>/_bim_manager/` (`BIMManager/GapFixCommands.cs:39`, `UI/StingCommandHandler.cs:9166`)
  - `<rvtDir>/_CDE/` (`Core/MultiBuildingExtraCommands.cs:99-109`, `BOQ/BOQBccBridge.cs:531`)
  - `<rvtDir>/STING_Exports/` (legacy, still produced by `Temp/ScheduleEnhancementCommands.cs:1478` and indexed by `UI/DocumentManagementDialog.cs:4981`)
  - `CDE_PACKAGE_<name>_<ts>/`, `SharePoint_<name>_<ts>/`, `<Model>_Briefcase_<ts>/`, `STING_QR/`, `_acc_mirror_tmp/` — new timestamped folder **every run** (`BIMManager/PlatformLinkCommands.cs:1278-1300, 2506-2517`)
  - plus ~10 machine-level roots (%APPDATA%, %LOCALAPPDATA%, %TEMP% ×7, Documents, Desktop fallbacks).

### 1.2 The "repeated inside others" mechanism

`_BIM_COORD` exists in **three project locations simultaneously**, and the same logical file can land in any of them depending on which code path runs first:

| Location | Written by |
|---|---|
| `<rvtDir>/_BIM_COORD/` | ~40 legacy writers (BOQ config, review comments, symbol families, LOD reports…) |
| `<CODE>/_data/_BIM_COORD/` | Template engine v1.1 / Workflow / Search (`Docs/Templates/*`, `Docs/Workflow/*`) |
| `<CODE>/20_MISC_<CODE>/_BIM_COORD/` | Design Options (`Core/DesignOptions/OptionFolderManager.cs:19-20`) |

`GetMetaPath` (`ProjectFolderEngine.cs:316-331`) was built to consolidate the sibling stores under `_data/` — but the live writers were never migrated, so `MigrateFromLegacy` (runs silently on every document open) moves files in while legacy code recreates the siblings right back. The tree literally fights itself.

Other multipliers:

1. **Four independent WIP/SHARED/PUBLISHED/ARCHIVE generators** — the numbered tree, `_CDE/`, and the two per-run timestamped package trees. A project can hold four different CDE hierarchies.
2. **Two builders of the "same" numbered tree that disagree.** `CreateFolderStructure` (`ProjectFolderEngine.cs:941-1000`) creates 7 discipline subfolders and 14 issue subfolders; the auto-bootstrap path (`ProjectSetup.CreateBIM`, `ProjectSetup.cs:69-91`) creates 5 disciplines and 4 issue subfolders, and adds discipline subs under `06_DRAWINGS` which the first builder doesn't. Same project → different tree depending on entry point.
3. **Project-code-derived root + code-suffixed folder names.** Root = `<rvtDir>/<CODE>` where CODE comes from the Revit Project Number (sanitised, 8 chars, else `"PRJ"`). Every folder is also suffixed (`01_WIP_FIRESTON`). Edit the project number → a **whole new root tree** is auto-bootstrapped on the next export, and new suffixed siblings appear inside the old one. Multi-model linking relies on a fragile first-8-chars filename match (`ProjectFolderEngine.cs:237-268`).
4. **`20_MISC` is a second sprawl.** `OutputLocationHelper.GetOutputDirectory` funnels ~150 export commands into `20_MISC_<CODE>/`, where each command mints its own subfolder (`electrical/`, `SLD_Export/`, `ElecPDF/`, `PlacementDiagnose/`, `_bim_manager/`, `_BIM_COORD/options/`…). The "single container" fans out internally with no governance.
5. **Cross-document contamination risk.** `ProjectFolderEngine._rootPath` is a static cached across documents; `LoadOrDetectSetup` also adopts *any* sibling folder containing `_data/project_setup.json` — two projects in one directory can adopt each other's roots.
6. **Junk folders inside the CDE tree**: `_RECYCLE/` created wherever a delete happens (`ProjectFolderEngine.cs:1199-1203`), `_acc_mirror_tmp/` created beside published files and never cleaned (`:2018-2027`), `_DATA/sharepoint_queue/` (case-inconsistent with `_data`).
7. **Dead config**: `AUTO_CREATE_CDE_FOLDERS` is parsed (`TagConfig.cs:903-908`) but no document-open path calls `CreateFolderStructure` any more — the flag governs nothing.

---

## Part 2 — Document Manager vs ISO 19650: findings

Capability scorecard (evidence in file:line):

| # | ISO 19650 capability | Status | Key evidence / gap |
|---|---|---|---|
| 1 | Container naming convention | **PARTIAL** | Real token engine + atomic sequences (`DocumentIdentityGenerator.cs:27-165`); full vocabulary (`Iso19650Vocabulary.cs:22-135`). But default format uses `fb`/`sb` not Volume+Level; **no validator parses an assembled name**; auto-registration mints non-ISO `STING-{type}-{timestamp}` IDs (`BIMManagerCommands.cs:4967`). |
| 2 | Status/suitability codes | **PARTIAL** | S0–S7 enforced with history (`BIMManagerCommands.cs:57-69, 2157-2199`). But **five divergent code tables**; A1–A6/B1–B6 authorization codes exist only in the sheet vocabulary; no legality check of suitability-per-state. |
| 3 | Revision convention (P/C) | **PARTIAL** | P01–P99/C01–C99 validation (`RevisionManagementCommands.cs:359-376`) + `BumpRevision` (`DeliverableLifecycle.cs:135-146`). No automated P→C promotion at authorization. |
| 4 | CDE states WIP→Shared→Published→Archive | **PARTIAL** | Files are physically moved between `01_WIP…04_ARCHIVE` (`ProjectFolderEngine.cs:1271-1295`); transition maps + compliance gates exist (`BIMManagerCommands.cs:88-153`). But the actual per-instance workflow state machine is **never instantiated for deliverables** — CDE "state" is a string + a folder move. Three competing state-machine definitions. |
| 5 | Check/Review/Approve → Accept/Authorize gates | **MOSTLY MISSING** | Fields exist (`reviewed_by`, `ApproveRoles`), but `Issue`/`Publish` require no prior review; `WorkflowEngine.Transition` **ignores `allowed_roles`** (`WorkflowEngine.cs:66-98`); no Authorize step. |
| 6 | Document register | **IMPLEMENTED**, split-brained | 19-column CSV export, 15+ ISO fields (`BIMManagerCommands.cs:2121-2146`). But it is one of **two registers** (see below). |
| 7 | Transmittals | **PARTIAL** | Orchestrated path is good: `TX-NNNN`, render, workflow, hash-chain audit (`TransmittalOrchestrator.cs:45-123`). But a second auto-log store writes stub `TR-…` records with `recipient:"(auto-logged)"` (`ProjectFolderEngine.cs:1735-1782`); no acknowledgement capture UI. |
| 8 | MIDP/TIDP | **PARTIAL, broken join** | Real drift engine (`MidpEngine.cs:26-150`). But CSV-in only; and the lifecycle join reads the **legacy sibling** `_BIM_COORD/deliverables.json` with non-matching key fields (`DeliveryCommands.cs:331-341`) — the join typically resolves empty. |
| 9 | Audit trail | **IMPLEMENTED for one subsystem** | SHA-256 hash-chained JSONL + `VerifyChain` (`AuditLog.cs:41-166`) — but only the template-engine side writes to it; register-side edits go to a plain-text activity log. |
| 10 | Folder tree ↔ CDE state alignment | **PARTIAL** | The four CDE folders are real and tagged. But **rendered deliverables land in `_data/_BIM_COORD/generated/`** — outside the CDE tree, never registered, never moved through states (`TemplateEngine.cs:29, 60-66`). The issued documents don't live in the CDE. |

### Duplicate subsystems (the core problem)

| Concern | Copies | Where |
|---|---|---|
| Document register | **2** | `_BIM_COORD/deliverables.json` (lifecycle/BCC) vs `STING_BIM_MANAGER/document_register.json` (dialog) — different schemas, no sync |
| Transmittal store | **2** | `TX-NNNN` orchestrated vs `TR-<ts>` auto-log stub |
| Numbering scheme | **3** | `DocumentIdentityGenerator` vs `STING-{type}-{ts}` vs Revit revision seq |
| CDE state machine | **3** | `BIMManagerEngine.CDEStateTransitions` (7 states) vs dialog map (4 states) vs workflow JSON (never instantiated) |
| Suitability table | **5** | `BIMManagerEngine`, `DocStatusCodes`, `Iso19650Vocabulary`, `TemplateManifest`, `MidpEngine` — mutually inconsistent |
| MIDP engine | **2** | `Core.Delivery.MidpEngine` vs sheet-derived `BIMManagerEngine.MidpEngine` |
| Coordination UI | **2** | `BIMCoordinationCenter` vs `DocumentManagementDialog`, joined by reflection |

---

## Part 3 — Recommendation

### 3.1 Target: ONE root, TWO zones (answers "two structures or one")

Exactly **one visible tree per project**, containing a human zone and a machine zone. The existing BIM/Mini presets remain as the only two *layouts* of the same engine.

```
<rvtDir>/
  MYPROJECT.rvt
  MYPROJECT/                        ← the ONLY folder StingTools ever creates here
    00_WIP/                         ┐
    01_SHARED/                      │  the CDE — ISO 19650 states, nothing else
    02_PUBLISHED/                   │  at top level
    03_ARCHIVE/                     ┘
    _data/                          ← ALL machine state (one metadata root)
      register.json                 ← ONE register (merged)
      transmittals.json             ← ONE store
      doc_sequences.json
      audit/                        ← hash-chained JSONL (both subsystems)
      templates/  workflows/  search_index/
      staging/    (acc, sharepoint queues — replaces _acc_mirror_tmp etc.)
      recycle/    (single recycle bin — replaces per-folder _RECYCLE)
```

Inside each CDE state folder, sub-structure by **discipline → content type**, created lazily on first write:

```
00_WIP/E_Electrical/Drawings/   00_WIP/E_Electrical/Schedules/   00_WIP/Z_General/Reports/ …
```

**Why this is the ISO-correct shape:** ISO 19650 organises information by *state*, not by file type. The current `05_MODELS … 20_MISC` folders sit outside the CDE states, so most exports never exist in any CDE state at all. Folding content types *inside* the states means every file the plugin produces is born in WIP, and Share/Publish/Archive are real moves with register + audit entries. The 20-folder numbered tree becomes unnecessary: MODELS/DRAWINGS/SCHEDULES/… are subfolders per state; ISSUES/CLASHES/REGISTERS/COMPLIANCE outputs are just registered documents like everything else.

### 3.2 Five rules that make it sustainable and automatic

1. **One PathService, hard boundary.** A single static API is the only legal source of paths:
   - `StingPaths.Cde(doc, state, discipline, contentType)` — all exports/imports
   - `StingPaths.Meta(doc, bucket)` — all JSON/state (absorbs `GetMetaPath`)
   - `StingPaths.Staging(doc, channel)` / `StingPaths.Recycle(doc)`
   Enforce with a CI grep gate: no `Directory.CreateDirectory` or `Path.Combine(<rvtDir>, …)` outside `Core/`. That is what prevents regression — today's sprawl is precisely 250 call sites each choosing their own root.
2. **Stable root identity, not recomputed.** Stamp the project-root pointer (relative path + GUID) into the RVT via Extensible Storage (`StingProjectRootSchema`). `DetectProjectCode` is used **once** at creation to *name* the folder; identity thereafter is the stored pointer. Renaming the project number no longer forks a second tree; multi-model workspaces share the root by all stamping the same pointer, replacing the 8-char filename-prefix heuristic. Kill the static `_rootPath` cache (per-document dictionary only).
3. **Drop per-folder code suffixes.** The container is already named after the project; `01_WIP_FIRESTON` is redundant and is the fork mechanism when codes change. Keep uniqueness at the root only.
4. **One routing table.** `ProjectSetup.ExportRoutes` becomes the only router (delete the divergent `ProjectFolderEngine.ExportTypeToFolder`); routes now resolve to `(state, contentType)` pairs, default state WIP. Imports route through the same table.
5. **No timestamped sibling folders.** Per-run packages (`CDE_PACKAGE_*`, `SharePoint_*`, Briefcase) become ZIPs written to `01_SHARED/Z_General/Packages/` (and registered + transmitted), or staged under `_data/staging/`. Timestamps belong in filenames and the register, not directory names.

### 3.3 Document Manager: consolidation to a true ISO 19650 system

1. **One register.** Merge `document_register.json` into `deliverables.json`'s richer lifecycle model → `_data/register.json` (one schema, versioned). Both `DocumentManagementDialog` and `BIMCoordinationCenter` read/write it; delete the reflection bridge.
2. **One vocabulary module.** Make `Iso19650Vocabulary` the single source for suitability (S0–S7 + A1–A6 + B1–B6 + CR), doc types, roles; delete the other four tables. Add per-state legality (e.g. A-codes only in PUBLISHED).
3. **One state machine, actually running.** Instantiate the `deliverable_issue_default` workflow per deliverable on Issue; make `WorkflowEngine.Transition` enforce `allowed_roles` (it currently ignores them); wire Check→Review→Approve→(Accept/Authorize) as required transitions, with P→C revision promotion at authorization.
4. **Close the physical loop.** `TemplateEngine` renders into `00_WIP/<disc>/Documents/` (not `_data/generated/`), registers the file, and every state transition = `MoveFile` + register update + hash-chain audit entry. The register's `file_reference` always equals the current physical location.
5. **One transmittal path.** `AutoLogTransmittal` delegates to `TransmittalOrchestrator` (real recipients from distribution groups, workflow, audit). Add a minimal acknowledgement action (mark-acknowledged → workflow `acknowledge` transition).
6. **Extend the hash-chain audit** to register mutations and CDE moves (both subsystems write the same `_data/audit/` chain).
7. **Quick win:** fix the MIDP drift join — read `GetMetaPath(doc,"_BIM_COORD")/deliverables.json` and join on `DocNumber` (`DeliveryCommands.cs:331-341`). Two-line fix; today the report is silently empty.

### 3.4 Migration & rollout (suggested phases)

| Phase | Scope | Risk |
|---|---|---|
| **A — stop the bleeding** | Fix MIDP join; route `AutoLogTransmittal` → orchestrator; retire `_CDE/`, `_bim_manager/`, `STING_Exports/` writers onto `GetMetaPath`/`GetExportFolder`; single `_data/recycle/`; move `_acc_mirror_tmp`/`sharepoint_queue` under `_data/staging/`; unify the two tree builders on `ProjectSetup` defaults. | Low — mechanical rerouting |
| **B — one PathService** | Introduce `StingPaths`; migrate the ~90 sibling `_BIM_COORD` writers + ~150 `OutputLocationHelper` callers; add the CI grep gate. | Medium — wide but mechanical |
| **C — new tree + root identity** | CDE-first layout (states at top, content inside); ES-stamped root pointer; drop code suffixes; migration wizard with dry-run report that consolidates all legacy trees into the new root (extends `MigrateFromLegacy`, which already does 80% of the moves). | Medium |
| **D — doc manager unification** | Register merge, single vocabulary + state machine, role-enforced gates, render-into-WIP, audit-chain everywhere, acknowledgement capture. | Higher — schema migration, both UIs |

Phases A and B deliver most of the visible tidiness (one folder next to the RVT, nothing recreated after migration). Phases C and D deliver the "truly ISO 19650" claim.
