# ADDENDUM — Family Converter: connector preservation + shared-param integrity

**Follows:** `2026-07-13-family-converter-prompt.md` (implemented in PR #396, branch `claude/family-converter-7fa3c2`)
**Date:** 2026-07-18 · **Status:** ready to implement on top of PR #396

Three items, in priority order. **A** is the substantial one.

---

## A. Stop dropping MEP connectors on a P2 rebuild

### Why they drop today
`ElementTransformUtils.CopyElements` does not carry `ConnectorElement`s between family documents. The current engine only *counts* them and emits a "re-add manually" note (`FamilyHostConverter.cs` step 3 in `ConvertP2`). For MEP families this makes P2 effectively unusable — a converted AHU/luminaire/socket has no connectors, so it can't join a system.

### Approach that does NOT work (don't waste time on it)
"Wrap, don't rebuild" — nesting the original family inside a new face-based shell so connectors live in an untouched nested family. **This fails for hard-hosted sources**: a ceiling/wall-based family cannot be placed inside a Generic Model face-based template because there is no host for it. It only works for unhosted sources, which already take the lossless P1 path. Rejected.

### The fix — a two-tier connector transfer

Implement `ConnectorTransfer` as a helper in `FamilyHostConverter` (or a sibling file `Core/Placement/FamilyConnectorTransfer.cs`).

#### Tier 1 — STING seed families: declarative re-mint (exact)
If the source family resolves to a `SymbolDefinition` in `Data/Seeds/STING_SEED_*.json` (match on family name, or a STING id parameter if the family carries one), do **not** do geometric matching. Call the existing `SymbolLibraryCreator.AddConnectors(tgtDoc, def)` — connectors are rebuilt from the JSON spec (`offsetX` / `facing` / `domain`) exactly as the seed builder originally made them. This covers the ~190 STING symbol families with perfect fidelity and no guessing.

#### Tier 2 — ANY family (vendor / legacy): harvest → match → recreate
This is the general fix and the reason the tool works beyond STING content.

**Step 1 — HARVEST (from the source family doc, before closing it).**
For each `ConnectorElement ce` in `new FilteredElementCollector(src).OfClass(typeof(ConnectorElement))`, capture into a `ConnectorSpec` POCO:
- `ce.Domain` and the system-type enum (`DuctSystemType` / `PipeSystemType` / `ElectricalSystemType` / conduit / cable tray)
- `ce.Origin` (XYZ) and `ce.CoordinateSystem` (Transform) — `BasisZ` is the outward normal, `BasisX` fixes rotation
- `ce.Shape` (`ConnectorProfileType`: Round / Rectangular / Oval), plus `Radius` or `Width`/`Height`
- Flow direction / utility params and every writable parameter on `ce.Parameters`

**Step 2 — MATCH (in the target doc, after `CopyElements`).**
For each spec, find the copied `PlanarFace` that hosted it:
```csharp
var opt = new Options { ComputeReferences = true, DetailLevel = ViewDetailLevel.Fine };
```
> **CRITICAL:** `ComputeReferences = true` is mandatory. Without it `face.Reference` is null and connector creation silently fails.

Iterate the geometry of the copied elements, collect `PlanarFace`s, and accept a face when **all three** hold:
1. the face plane contains the origin — `Math.Abs((spec.Origin - face.Origin).DotProduct(face.FaceNormal)) < tol` (tol ≈ 0.5 mm expressed in feet)
2. normals align — `face.FaceNormal.DotProduct(spec.BasisZ) > 0.999`
3. the projected point is actually on the face — `face.Project(spec.Origin) != null`

**Step 3 — CREATE** on that face's `Reference`, per domain:
- Duct → `ConnectorElement.CreateDuctConnector(tgt, ductSystemType, profileType, faceRef)`
- Pipe → `ConnectorElement.CreatePipeConnector(tgt, pipeSystemType, faceRef)`
- Electrical → `ConnectorElement.CreateElectricalConnector(tgt, electricalSystemType, faceRef)`
- Conduit / Cable tray → `CreateConduitConnector` / `CreateCableTrayConnector`

> **Verify every signature against the Revit 2025 API before use.** The first implementation pass already found one doc-vs-API mismatch (`FAMILY_WORK_PLANE_BASED`, not `..._PARAM`). Treat the list above as intent, not gospel; adapt and note any deviation.

Then replay dimensions (Radius / Width / Height) and the harvested parameters. All inside one `Transaction(tgt, "STING Rebuild Connectors")`.

**Step 4 — FALLBACK (never silently lose one).**
If no face matches (connector was hosted on a reference plane, or its geometry failed to copy):
- try creating on a copied `ReferencePlane` if an overload permits;
- otherwise record the spec and report it with **exact origin XYZ, normal, domain, shape and size**, so a manual re-add is a two-minute job rather than a forensic exercise.

**Step 5 — REPORT.** Replace the current blanket "connectors were NOT transferred" note with:
`Connectors: {harvested} harvested · {recreated} re-created · {manual} need manual re-add` plus the per-connector detail list for the manual ones. Only claim re-creation for connectors actually created.

### Acceptance (runtime — user verifies in Revit)
- A vendor MEP family (e.g. ceiling-hosted luminaire or diffuser) converted to face-based retains its connectors and can be connected to a system.
- A STING seed family takes the Tier-1 path and reports exact re-mint.
- A deliberately awkward connector (on a reference plane) reports as manual with usable coordinates rather than vanishing.

---

## B. Shared-parameter integrity (upgrade of the "minor note")

### The real hazard
`CopyFamilyParameters` resolves shared params via `src.Application.OpenSharedParameterFile()` — i.e. **whatever shared-parameter file the user currently has pointed**. If that is not STING's `MR_PARAMETERS.txt`, every STING shared parameter (`ASS_TAG_*`, tokens, the 53 containers) fails GUID lookup and silently degrades to a plain family parameter. The family still loads, but **tags, schedules, ExLink and COBie exports quietly stop working on converted families**. The code already counts and warns about this (`sharedFallback`), which is good — but it should prevent it, not just report it.

### Fix
1. **Pin STING's shared-parameter file for the duration of the copy, then always restore:**
```csharp
string prevSp = app.SharedParametersFilename;
try {
    string stingSp = StingToolsApp.FindDataFile("MR_PARAMETERS.txt");
    if (!string.IsNullOrEmpty(stingSp) && File.Exists(stingSp))
        app.SharedParametersFilename = stingSp;
    // … existing parameter copy …
}
finally { app.SharedParametersFilename = prevSp; }   // ALWAYS restore, even on throw
```
2. **Search both files, not just one.** Generalise `FindExternalDefinition` to take a list of `DefinitionFile`s — STING's file first, then the user's original — so vendor shared params that exist only in the user's file still resolve by GUID. A param should only fall back to a family parameter when it is in *neither*.
3. **Pre-flight in Audit Only.** Report "N shared parameter(s) would fall back to family parameters" **before** the user applies, so the loss is a decision rather than a discovery.

### Acceptance (runtime)
After a P2 convert of a STING-tagged family: `ASS_TAG_1` still reads on a placed instance, and the family still appears correctly in an existing STING schedule.

---

## C. Build baseline — resolved, no code change

Investigated: `2c73938a1` ("baseline is now 0/0") is **not** in `origin/main` — it is still stranded on `claude/gold-all-integration`. Verified:
```
git merge-base --is-ancestor 2c73938a1 origin/main   → false
```
Therefore **main's genuine baseline is 6 warnings**, and PR #396's "6 warnings (baseline), 0 from my files" was reported correctly. No regression, nothing to fix in the converter.

Two housekeeping actions (separate from this feature):
1. Rebase `claude/family-converter-7fa3c2` onto current `origin/main` — it is only **2 commits** behind.
2. Land `claude/gold-all-integration` if you want 0/0 to become the real baseline; until then, do not treat 6 as a defect in any branch.

---

## Scope note — what the converter already handles (no change needed)

Confirmed by reading the engine: the tool operates on **any loaded or imported family**, not just STING content.
- `ScanProjectFamilies` collects `OfClass(typeof(Family))` — every family in the document.
- `ImportFolder` / `Load .rfa` ingest arbitrary vendor files.
- The only gates are generic: not in-place; placement type ∈ {`OneLevelBased`, `OneLevelBasedHosted`, `WorkPlaneBased`, `TwoLevelsBased`}; and for a P2 rebuild, category ∈ `FamilyCategoryCompatibility.ModelFamilyGroup` (Generic Models, Furniture, Casework, Specialty Equipment, MEP Equipment & Fixtures, Lighting, Devices).
- Nothing tests for STING authorship.

Doors, windows, curtain panels, annotation, curve-driven and system families remain out of scope by category/placement gate. Tier 1 above is the *only* STING-specific path, and it is a fast-path optimisation — Tier 2 covers everything else.
