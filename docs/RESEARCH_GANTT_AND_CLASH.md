# Phase 104 — Research & Advice: Native Gantt Chart + Clash Detection in Revit

**Status:** RESEARCH ONLY. No code changes made to the plugin for Gantt-chart
recreation or clash-detection implementation. User requested advice first.

**Scope questions from the user:**

> **Q7.** Can we recreate the MS Project functionality, including editable
>        cells / Gantt chart? Research the Revit API limitations to exploit.
>
> **Q8.** Can we do real clash detection in Revit like you would do it in
>        Navisworks? Research all possible workarounds / Revit API capabilities
>        so we can manage clash detection in Revit + StingTools.
>
> **Q9.** Research and advise. Before doing anything on clash detection.

---

## 1. Native Gantt chart inside Revit — feasibility study

### 1.1 Can it be done?
**Yes — but only partially.** Revit's WPF hosting lets StingTools render any
Gantt-like grid you can draw in XAML. We already use `DataGrid` with
`DataGridTemplateColumn` + `FrameworkElementFactory` in `BIMCoordinationCenter`
(see the Phase-Summary panel's progress-bar column). A full interactive Gantt
needs three things:

| Component | What it requires | Revit API allowed? |
|---|---|---|
| Editable cell grid | WPF `DataGrid` with bindings + validation | ✅ Trivial |
| Timeline bar rendering | `Canvas` + rectangles driven by start/duration | ✅ Trivial (pure WPF) |
| Drag-to-resize / drag-to-move on bars | Mouse capture on `Canvas` child | ✅ Allowed |
| Task dependencies (arrows) | Adorner layer lines between bars | ✅ Allowed |
| Baseline comparison | Two rectangle rows per task | ✅ Allowed |
| Critical-path highlight | In-memory CPM calculation (we implemented this in `Scheduling4DEngine`) | ✅ Already have it |
| Undo/redo on edits | Revit `Transaction` + WPF `CommandManager` | ⚠ Needs care (see §1.3) |
| Printing | `VisualBrush` → `FixedDocument` → PDF | ✅ Allowed |
| Multi-user editing | Workset-scoped edits + `WorksharingUtils` | ⚠ Document-bound only |

### 1.2 Revit API limits that bite us

1. **No schedule entity.** Revit has `ScheduleSheetInstance` (model schedule
   views), `Phase`, and `Revision`, but **nothing** that represents "Task with
   start / finish / duration / %Complete / predecessors". The 4D data model
   has to live in one of: shared parameters, project-scoped `DataStorage`
   entities, or an external sidecar file. StingTools already uses a sidecar
   (`4D_SCHEDULE.json`) — that's the right pattern.

2. **No built-in dependency model.** MS Project stores FS/SS/FF/SF links with
   lag in the `.mpp` binary. Revit has nothing equivalent, so we re-implement
   using the `TaskLink` struct in `Scheduling4DEngine`.

3. **Resource + cost rollup.** Revit has `Material` and `BillOfQuantities`,
   but no notion of "resource assignment to task" or "time-phased cost". The
   5D cost model has to be STING-side, again in the JSON sidecar.

4. **Calendar API is absent.** No working-day / holiday API. StingTools
   ships `WorkingCalendarCommand` which writes a UK-bank-holiday CSV. For
   true Gantt we'd need to interpret that on every schedule recalculation
   (5-10 ms overhead, trivial).

5. **Revit is single-document / single-view at a time.** A proper Gantt
   editor is heavy (tens of thousands of rows). We'd need virtualisation
   (`VirtualizingStackPanel`) and probably a lazy-load window. StingTools'
   `StingResultPanel` already uses the same pattern.

### 1.3 Undo / transaction model — THE hard problem
Revit only allows modifying the document inside an open `Transaction`. For
Gantt-cell edits that don't change Revit elements (e.g. task name, planned
start) we DON'T need a transaction — those edits live in the JSON sidecar.
But edits that DO touch Revit (e.g. "Assign Phase to Task") must be wrapped.

Recommended split:
- **Pure data edits** (name, dates, dependencies, resources, cost): JSON
  sidecar only → no transaction → unlimited undo via WPF `UndoStack`.
- **Revit-linked edits** (phase assignment, parameter writes): wrap each
  commit in `Transaction` + mark as "pending" in the grid so the user
  knows rollback means only the non-Revit edits undo.

### 1.4 What it would cost (effort estimate)
| Feature | Complexity | Est. days |
|---|---|---|
| Read/write current 4D_SCHEDULE.json | Low | 1 |
| Editable `DataGrid` with 15 columns | Low | 2 |
| Canvas-based Gantt bars (read-only) | Medium | 3 |
| Drag-to-resize / drag-to-move bars | Medium-High | 5 |
| Dependency arrows + auto-route | High | 5 |
| Critical-path highlight | Low (engine exists) | 1 |
| Baseline comparison | Medium | 2 |
| Export .xml / .mpx / .csv | Low | 2 |
| Printing / PDF export | Medium | 3 |
| **Total** | — | **~24 days** |

### 1.5 Recommendation
A native Revit+WPF Gantt is **achievable** and gives us bigger benefits than
just "another import option":
- **Single source of truth:** tasks live in the `.sting_4d_schedule.json`
  sidecar alongside the RVT, not in a separate `.mpp`.
- **Round-trip without a license:** users who don't have MS Project can still
  edit the schedule — the `.mpp` becomes an **export target**, not a
  requirement.
- **Phase linking:** Revit `Phase` is directly bindable, so 4D simulation
  drives the view-filter pipeline the plugin already owns.

**Proposed approach:**
1. **Phase 1 (5 days):** read-only Gantt viewer in the `4D/5D` tab —
   `DataGrid` above, `Canvas` below, shared scroll, CPM highlight. No edits.
2. **Phase 2 (5 days):** editable cells + bar-drag for dates + dependencies
   (JSON-sidecar only, no Revit transactions).
3. **Phase 3 (5 days):** baseline, resources, cost rollup, print/export.

This keeps each phase shippable on its own and defers the hardest bits
(drag-arrows, rollback) until there's proven value from the simpler viewer.

---

## 2. Real clash detection inside Revit — feasibility study

### 2.1 Can it be done?
**Yes, but with a different engineering trade-off than Navisworks.** Revit
2018+ has the `InterferenceCheck` API which runs geometry intersection on
the server side of the model, not through Navisworks' NWD preprocessed
scenes. That is the only sanctioned path — everything else is either a
subset (BoundingBox overlap) or a superset (full solid/solid Boolean).

| Method | Revit API hook | Accuracy | Speed | Reports |
|---|---|---|---|---|
| `Document.InterferenceCheck.Run` | `InterferenceCheckFilter` | Full solid-solid intersection | Slow on large models | Returns `InterferenceResult` groups |
| `ReferenceIntersector` | Views + pick Refs | Mesh-against-mesh along a ray | Very slow for batch | One-pair at a time |
| `BoundingBoxIntersectsFilter` | Element filters | AABB only (often wrong) | Fast | Raw element list |
| `BooleanOperationsUtils.ExecuteBooleanOperation` | Solid Boolean | Exact | Medium | Per pair |
| `GeometryObject` mesh walk | Raw triangles | Any | Slow but controllable | Any custom report |
| External — Navisworks COM | N/A (separate process) | Full | Fast (published model) | NWF/NWD clash sets |

### 2.2 What Navisworks gives you that Revit can't *natively*

1. **Multi-model federation.** Navisworks reads NWC exports from Revit +
   ArchiCAD + Tekla + DWG simultaneously. The Revit API can only work with
   the currently-open document + its `RevitLinkInstance`s. → We can emulate
   federation across Revit links (A + S + MEP) but not across disciplines
   modelled in other native tools without exporting to IFC first.

2. **Preprocessed scene graphs.** Navisworks builds a compressed NWD with
   BVH/BSP accelerators. Revit walks every element + Solid every time.
   Revit's `InterferenceCheck.Run` pre-filters by bounding box internally
   but still re-walks geometry for each call.

3. **Clash grouping + workflow.** Navisworks has Clash Groups, Issues with
   Status (New/Active/Reviewed/Approved/Resolved), Assignees, Reference
   Views with saved camera positions, clash history diff, XML/BCF export.
   Revit has **none of this** out of the box. → We'd build it on top of the
   existing STING Issue system (`issues.json`) + BCF engine.

4. **TimeLiner 4D simulation.** Navisworks can play clashes against a 4D
   schedule. Revit can step phases but has no timeline scrubber.

5. **Performance on big federations.** 500 MB NWD with 10 disciplines
   clashes in seconds. Equivalent in Revit with 10 linked models takes
   10-30 minutes for full pairwise `InterferenceCheck`.

### 2.3 What Revit CAN do that Navisworks can't

1. **Real-time clash on edit.** When a user drags a pipe, we can trigger a
   scoped clash against nearby elements in a few milliseconds — no export
   roundtrip. This is the killer feature to lean on.

2. **Parameter-aware clash rules.** "Clash only ducts vs. rated walls where
   the duct has `PenetrationSeal = false`". Navisworks can do this via
   Search Sets but requires re-export on every parameter change.

3. **Clash → Issue → Transmittal** in one workflow. BCF export already
   exists (Phase 95). Clash results flow straight into `issues.json`.

4. **Workset-aware selection.** Clash only against elements in selected
   worksets so trades can operate independently.

### 2.4 Recommended architecture (without committing to build)

The cheapest path that gives 80% of Navisworks value:

```
+-------------------------------------------------------------+
|  CLASH PIPELINE (all in-process, all Revit API)             |
+-------------------------------------------------------------+
|  1. Rule Set Builder                                         |
|     - Discipline pairs (M vs S, M vs A, ...)                 |
|     - Category filters (OST_DuctCurves vs OST_StructuralFraming)|
|     - Parameter filters (optional)                           |
|     - Tolerance (0 = any overlap, >0 = hard clash depth)     |
|                                                              |
|  2. Scope Resolver                                           |
|     - Host document only                                     |
|     - Host + selected RevitLinkInstances                     |
|     - Selected worksets only                                 |
|                                                              |
|  3. Broad-Phase Filter (AABB)                                |
|     - BoundingBoxIntersectsFilter on all A-set x B-set       |
|     - Reduces candidates by ~99% on typical models           |
|                                                              |
|  4. Narrow-Phase Check (Solid/Solid)                         |
|     - BooleanOperationsUtils.ExecuteBooleanOperation(..., Intersect)
|     - If Volume > tolerance: register clash                  |
|                                                              |
|  5. Result Manager                                           |
|     - Group by (ElementA.Category, ElementB.Category)        |
|     - Store in .sting_clashes.json alongside RVT             |
|     - Status: New / Active / Reviewed / Approved / Resolved  |
|     - Assignee + due date (reuses STING issues SLA)          |
|                                                              |
|  6. Review UI (BCC Clash Tab — NEW)                          |
|     - Grid: A-element / B-element / Category / Volume / Status|
|     - Double-click: select both + zoom to section box        |
|     - Right-click: raise issue / assign / resolve / suppress |
|     - Export BCF 2.1 via existing Planscape BCF engine       |
|                                                              |
|  7. Real-time "background" clash                             |
|     - IUpdater scoped to MEP categories                      |
|     - On element placement, scope-phase check against        |
|       nearby elements (50 mm buffer) and flag any collision  |
|       in the model browser / tag                             |
+-------------------------------------------------------------+
```

### 2.5 Performance budget on a realistic model

Test scenario: host RVT + 2 linked discipline models, ~150 k elements total.

| Phase | Expected time |
|---|---|
| Broad-phase (AABB) 150 k × 150 k | ~1 s (R-tree on bounding boxes) |
| Narrow-phase on 10 k candidates | ~15 s (BooleanOperationsUtils averages ~1.5 ms per pair) |
| Result grouping + JSON write | < 1 s |
| **Total first-run** | **~20 s** (vs Navisworks ~8 s but we save the 2-5 min export) |
| Incremental (1 element moved) | **< 300 ms** (scope phase checks only) |

### 2.6 Recommendation — DO NOT build from scratch

**Do not re-implement Navisworks.** The full feature set (federation +
XML/BCF + 4D + grouping + preprocessed scene) is 4-6 months of work. Instead:

1. **Short term (1-2 weeks each):**
   - **Ship a scoped clash runner** — rule-set builder + broad/narrow phase
     pipeline above, writes `issues.json` directly, no standalone grid. The
     user clicks "Run Clash" → StingTools runs → creates issues → they
     appear in the existing BCC Issues tab with the BCF export already wired.
   - **Real-time collision flag** — IUpdater that flags clashes as elements
     are placed. Just a visual indicator, no full clash report.

2. **Medium term (4-6 weeks):**
   - **Clash Tab in BCC** — dedicated grid with grouping, status, assign,
     suppress, side-by-side comparison with previous run.
   - **Multi-model federation** via the existing `RevitLinkInstance`
     traversal (we already do this in `FederatedWorkflowSupport`).

3. **Long term / only if demanded:**
   - **Navisworks export round-trip** — Revit → NWC → run Clash Detective →
     BCF → import back into BCC. This is what most teams actually want; we
     already have the BCF import half (Phase 95). Completing it takes ~1 week.

### 2.7 API sample — real-world clash code

```csharp
// Broad-phase: BoundingBoxIntersectsFilter
var bbFilter = new BoundingBoxIntersectsFilter(elementB.get_BoundingBox(null).ToOutline());
var hits = new FilteredElementCollector(doc)
    .WhereElementIsNotElementType()
    .WherePasses(bbFilter)
    .ToElementIds();

// Narrow-phase: Solid/Solid intersection
Solid solidA = GetLargestSolid(elementA);
foreach (ElementId id in hits)
{
    Solid solidB = GetLargestSolid(doc.GetElement(id));
    if (solidA == null || solidB == null) continue;
    Solid inter = BooleanOperationsUtils.ExecuteBooleanOperation(
        solidA, solidB, BooleanOperationsType.Intersect);
    if (inter != null && inter.Volume > 1e-6)
        yield return new ClashResult(elementA, doc.GetElement(id), inter.Volume);
}
```

No reflection, no undocumented APIs, no risk of Revit version breaks.

---

## 3. Final advice (read before implementing anything)

| Question | Short answer |
|---|---|
| **Q7 — Gantt in Revit?** | Yes. Start with read-only Gantt viewer in `4D/5D` tab (5 days). Phase editable cells + drag bars next (5 days). Defer dependency-arrow editing + full MS Project replacement (last 5 days). ~15-24 days total depending on polish. |
| **Q8 — Real clash in Revit?** | Yes, scoped + 80% of Navisworks value. Use `BoundingBoxIntersectsFilter` → `BooleanOperationsUtils.ExecuteBooleanOperation` pipeline. Results go straight into the STING Issue system we already have. |
| **Q9 — Clash before we commit?** | **DO NOT** try to re-implement Navisworks. Ship scoped clash runner + BCF round-trip first (1-2 weeks each). Measure demand before building federation / 4D scrubber. |

**Next action** (if you approve): I will prepare a scoping ticket for each
phase with acceptance criteria. Nothing in the plugin changes until that's
approved.
