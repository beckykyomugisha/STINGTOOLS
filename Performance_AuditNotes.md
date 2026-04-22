# STING v6 N-G1 FilteredElementCollector performance audit

Generated during S1.3. Catalogues `FilteredElementCollector` usages where
a slow `.Where(lambda)` filter precedes or replaces a quick `.OfCategory`
/ `.OfClass` filter, or where spatial work uses the wrong API. Fixes land
in S1.4.

## Severity ranking

| # | File | Line | Antipattern | Fix |
|---|---|---|---|---|
| 1 | BIMManager/ExcelLinkCommands.cs | 308 | `WhereElementIsNotElementType().Where(e => e.Category != null && knownCatNames.Contains(e.Category.Name))` — full-model scan, string compare per element | Build `ElementMulticategoryFilter` from `knownCatNames` once, apply before `.ToElements()` |
| 2 | BIMManager/ParameterDiffCommands.cs | 45 | `WhereElementIsNotElementType().Where(e => e.Category != null && !IsNullOrWhiteSpace(GetString(e, TAG1)))` — reads shared param for EVERY element across ENTIRE model | Pre-filter with `OST_*` category set covering tagged categories (reuse `TagConfig.GetTaggableCategories()`) |
| 3 | BIMManager/CarbonTrackingCommands.cs | 116 | `WhereElementIsNotElementType().Where(e => e.Category != null)` — full-model scan; carbon takeoff only needs categories with material qty | Pre-filter with `ElementMulticategoryFilter` of 20 carbon-relevant categories (walls, floors, ceilings, roofs, columns, beams, MEP) |
| 4 | BIMManager/SchedulingCommands.cs | 1811, 1854, 1941 | Three locations `WhereElementIsNotElementType().Where(lambda)` without category pre-filter | Same multicategory pattern — 4D/5D only needs tagged physical categories |
| 5 | Model/ModelCommands.cs | 1349 | `.Where(e => e.get_BoundingBox(null) != null)` for spatial containment | Replace with `BoundingBoxIntersectsFilter(outline)` and use collector-side filter |
| 6 | Temp/DataPipelineCommands.cs | 5191, 5214 | Same `get_BoundingBox(null) != null` Where filter | Same `BoundingBoxIntersectsFilter` fix |

## Other observations (info only, not fixing)

- `BIMManager/GapFixCommands.cs:1271,1285`: `OfCategory` IS used first; `.Where(f => f.get_Parameter(...)?.AsInteger() == 1)` is acceptable tail filter.
- `BIMManager/ExcelLinkCommands.cs:572,2268,2486`: `Cast<ViewSchedule>().Where(...)` is scoped by `OfClass(ViewSchedule)` on prior line — fine.
- `BIMManager/RevisionManagementCommands.cs:1339-1590`: `Cast<Revision>()` / `Cast<RevisionCloud>()` patterns scoped by class — fine.
- `Core/StingAutoTagger.cs:322,845`: `.Where()` on string/cat lists in memory, not FEC — fine.
- `Tags/FamilyParamCreatorCommand.cs:740`: `.Where(p => p.IsShared && ...)` on definition file parameters, not Revit elements — fine.

## Count

| Metric | Value |
|---|---|
| `new FilteredElementCollector` occurrences (Core + Tags + BIMManager + UI) | 69 |
| Confirmed antipatterns | 5 files, 8 call sites |
| Spatial-filter antipatterns | 3 call sites |
| Total fixes scheduled for S1.4 | top 5 (cross-cutting) |

## S1.4 fix order (by call-frequency impact)

1. ExcelLinkCommands.cs:308 — hot path, runs on every Excel export
2. ParameterDiffCommands.cs:45 — runs on every revision compare
3. CarbonTrackingCommands.cs:116 — runs per N-G13 workflow step
4. SchedulingCommands.cs:1811 — runs per 4D/5D trace
5. ModelCommands.cs:1349 — runs per model creation path

Expected combined speedup: 5-10× on >10k-element projects based on
Revit API profiling guidance. Remaining 3 (Scheduling 1854/1941,
DataPipeline 5191/5214) handled in a follow-up sweep.
