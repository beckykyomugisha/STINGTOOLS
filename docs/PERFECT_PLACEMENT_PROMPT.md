# Terminal-Agent Prompt — "The Path to Perfect Placement" (Planscape federation coordinates)

You are implementing **automatic, correct spatial placement** for multi-tool BIM federation in the
Planscape system (Revit + ArchiCAD + Tekla-via-IFC → Planscape web viewer). Work autonomously,
verify everything, and open small stacked PRs. This prompt is self-contained; the "Ground truth"
section is a verified audit — trust it but re-read the cited files before editing (line numbers drift).

Repo root: `C:\Dev\STINGTOOLS`. This machine **builds the .NET server AND the Revit plugin**
(Revit 2025 API present) and Docker/Postgres now work — so you can and MUST build + test, not just write.

---

## 1. Mission / definition of done

A coordinator uploads a **Revit** model and an **ArchiCAD (or Tekla) IFC** model of the **same site**
to one Planscape project, opens the viewer, and **the two models overlay correctly — right position,
right rotation (true north), right scale — with NO manual per-model transform entry.** Today that only
happens if every tool pre-exported in identical shared coordinates + units; make it automatic.

**Concretely done when:**
1. Ingesting two IFCs with different `IfcMapConversion` origins + a shared `IfcProjectedCRS` produces
   per-model transforms that are **applied automatically** (no manual "confirm" step), and their
   world-space AABBs overlap where they should — proven by an automated test.
2. Unit mismatch (feet vs mm vs m) is **reconciled automatically** (no 3.28×/1000× scatter).
3. True north is applied automatically.
4. A **manually-confirmed** transform still wins over an auto-computed one (no regression of the
   coordinator override).
5. Server builds 0 errors; new tests pass (InMemory + SQLite + Postgres-gated); the Revit plugin
   still builds.

---

## 2. Ground truth (verified audit — the chain that's broken)

The alignment **machinery exists and is mathematically sound** — `Planscape.Core/Coordinates/ModelTransformMath.cs`
(`ApplyMm`: world = T + R_z(scale·local), Z-up, mm; backed by `ModelTransformMathTests`), the entities
`ProjectModelTransform` / `ProjectCoordinateSystem` / `IfcAlignmentReport`, `IfcAlignmentValidator`
(parses `IfcMapConversion` E/N + `XAxisAbscissa/Ordinate`→rotation + `Scale` + `IfcProjectedCRS`),
`AutoAlignService`, and a **viewer that applies per-model T·R·S** (`wwwroot/viewer.html`
`applyModelTransform` ~l.1093, fetches `/models/{m}/transform` per model, multi-model loop in
`coordination-viewer.js` ~l.4018). **It is not wired end-to-end. The breaks:**

- **B1 — The confirmation gate blocks auto-alignment.** The viewer no-ops unless `isConfirmed !== false`
  (`viewer.html` ~l.1098). Both auto paths store `IsConfirmed=false`
  (`IfcIngestController.UpsertProjectModelTransformAsync` ~l.670; `AutoAlignService.ComputeAsync` ~l.210).
  So a correctly-computed transform is **never shown** until a human manually PUTs `IsConfirmed=true`.
  **This is the single highest-leverage fix.**
- **B2 — The primary Revit path computes no transform at all.** `ModelsController.Upload` (~l.80-301)
  writes a `ProjectModel` and nothing else — no `IfcAlignmentReport`, no `ProjectModelTransform`. Revit
  GLB geometry is exported about the **internal origin, project-north**, feet→metric but with **no
  survey-point offset and no true-north** (`Clash/ClashExportContext.cs:53` uses `Transform.Identity`;
  `GlbSerializer.cs:19` metres vs `RevitGltfExporter.cs:51` mm — 1000× inconsistent between the two
  writers). `RevitGltfExporter.ExportCoordinateSidecar` (~l.497-585) DOES compute lat/long + true north
  but `exportMode` is hardcoded `"ProjectInternal"`, E/N + project-base-point are `null`, and **the
  sidecar is never uploaded** (`PublishModelCommand.cs:224-235`).
- **B3 — Unit reconciliation is dead code.** `IfcIngestController` ~l.636-640: the `scaleFactor` ternary
  returns `1.0` on **both** branches. The viewer never reads `ProjectModel.Units` to rescale. Feet-vs-metric
  is not reconciled anywhere automatically.
- **B4 — Two inconsistent translation conventions.** IFC ingest translates by the **negative absolute
  survey origin** (`tx = -easting*1000`, each model→world 0). `AutoAlignService` translates **relative to
  a reference model** (`tx = (refEasting-targetEasting)*1000`). If both touch one project, models land in
  two different frames.
- **B5 — StingBridge/core send no georef.** `GeorefDescriptor` (`stingtools-core/.../hosts/adapter.py`)
  is **defined but has zero consumers**; `IfcFileHostAdapter.georef_descriptor` (`hosts/ifc_file.py:123`)
  reads only `IfcMapConversion` (no CRS, no true-north, `length_unit` defaults `"mm"` while E/N are metres),
  and the watcher never even calls it. The `/models` GLB upload (`StingBridge/planscape/client.py:398`)
  carries no georef metadata; `IfcConvert` produces model-local geometry.
- **B6 — Stale AABBs.** SceneNode world AABBs are recomputed only in `ModelTransformController.Upsert`
  (~l.144-167), not after `AutoAlignService` or ingest-auto-transform — so server culling/clash bounds
  disagree with what's rendered once auto-transforms exist.

---

## 3. Guardrails (read before touching anything)

- **Respect the manual override.** A coordinator's `IsConfirmed=true` transform must always win. Do NOT
  simply delete the gate. Introduce a precedence: **manual-confirmed > auto-applied(high-confidence) > none**.
  The correct fix makes the viewer apply an *auto-applied* transform while a manual one still overrides.
- **Only auto-apply HIGH-CONFIDENCE transforms.** A transform derived from a real `IfcMapConversion` +
  `IfcProjectedCRS` (or matching CRS to the project's `ProjectCoordinateSystem`) with a `PASS`/`WARN`
  `IfcAlignmentReport` verdict is high-confidence. A guessed/absent-georef model should NOT be moved
  (leave it at origin rather than scatter it wrongly). Encode this as an explicit confidence/appliedness
  concept, not a blanket flip of `IsConfirmed`.
- **Build + test every change.** Server: `dotnet build src/Planscape.API/Planscape.API.csproj` (0 errors).
  Plugin (if you touch Revit): `dotnet build StingTools/StingTools.csproj -p:RevitApiPath="C:/Program Files/Autodesk/Revit 2025"`.
- **Test harness patterns already in the repo** (use them):
  - EF **InMemory** for service/logic (construct `PlanscapeDbContext` directly — no host boot; `IgnoreQueryFilters()`).
  - In-memory **SQLite** for anything needing real relational semantics / constraints (`HandoffProvisioningSqliteTests` pattern).
  - **Postgres** integration via `[SkippableFact]` + `Skip.If(SkipReason)` gated on env `PLANSCAPE_TEST_PG`
    (`PostgresSequenceCounterTests`, `Postgres2bIntegrationTests`), transaction-rolled-back. To run locally:
    `docker run -d --name pgtest -e POSTGRES_PASSWORD=testpass -e POSTGRES_USER=planscape -e POSTGRES_DB=planscape -p 55432:5432 postgres:16`
    then `PLANSCAPE_TEST_PG="Host=localhost;Port=55432;Database=planscape;Username=planscape;Password=testpass"`.
  - For viewer JS, at minimum a headless assertion of `applyModelTransform` precedence (there may be a
    JS test harness; if not, keep the change small + reason it through + note manual-verify).
- **Branching:** one PR per phase, stacked. Base off current `main` (the R1–R8 identity work + 2b unique
  index are already merged; don't re-touch identity). End commits with
  `Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>`. Never commit to `main` directly.
- **Out of scope for THIS prompt** (do not get pulled in — they are separate, higher-urgency tracks):
  the cross-project **authorization** gaps on `ModelTransformController`/`CoordinateSystemController`/
  `AlignmentController`/`SceneNodesController`/`ModelDiffController`/`FederatedModelHub`; delete/tombstone
  propagation; model versioning/supersede + orphan purge; the Revit geometry-delta silent-loss (H1) and
  9-category limit (H2). If a change here trivially enables one of those, leave a clear `// TODO(track:...)`
  and a ROADMAP note — don't implement them in the placement PRs.

---

## 4. Work plan (ordered phases — each is one stacked PR)

### P1 — Auto-apply high-confidence transforms (the unlock; fixes B1)
- Add an explicit "applied automatically" concept distinct from the manual `IsConfirmed`. Options (pick the
  cleanest that preserves override precedence): a new `AppliedAutomatically` bool + `Confidence` on
  `ProjectModelTransform`, OR keep `IsConfirmed` for manual and add `AutoApplied`. The API returned to the
  viewer (`ModelTransformController` GET, and the `/federation/manifest`) must expose enough for the viewer
  to decide "apply this (auto) unless a manual one exists."
- `AutoAlignService.ComputeAsync` (~l.199-213) and `IfcIngestController.UpsertProjectModelTransformAsync`
  (~l.618-699): when the source georef is high-confidence (see guardrails), mark the transform auto-applied.
  **Guard against clobbering an existing manual `IsConfirmed=true`** (AutoAlign currently does not — fix that too).
- Viewer `applyModelTransform` (`viewer.html` ~l.1093): apply when `isConfirmed===true` **OR**
  `appliedAutomatically===true`; manual still wins if both somehow set. Keep the mm→m + Z-rotation math.
- **Acceptance:** a `ProjectModelTransform` with georef-derived values + auto-applied renders moved in the
  viewer's transform-selection logic; a manual-confirmed transform on the same model still takes precedence.
  Add server tests for the precedence + the no-clobber guard.

### P2 — Give the Revit-GLB path an alignment source (fixes B2)
- Make Revit emit its georeference and the server turn it into a transform, mirroring the IFC path (do NOT
  bake survey coordinates into the mesh — send metadata + let the server compute, consistent with IFC).
- Revit side (`StingTools`): populate `RevitGltfExporter.ExportCoordinateSidecar` E/N + survey offset + true
  north from `ProjectLocation`/`GetProjectPosition` where the API allows, set `exportMode` truthfully, and
  **upload the sidecar** alongside the GLB in `PublishModelCommand`/`ModelsController` upload
  (extend `UploadModelRequest` with an optional georef block: E/N/elevation, trueNorthDeg, crsEpsg, lengthUnit).
- Server side: on model upload with a georef block, create an `IfcAlignmentReport`-equivalent +
  `ProjectModelTransform` (auto-applied per P1). Reuse `IfcAlignmentValidator`/`AutoAlignService` math.
- **Acceptance:** a Revit GLB uploaded with a georef block gets an auto-applied transform; without one it
  stays at origin (not scattered). Test with a fixture upload payload.

### P3 — Fix unit reconciliation (fixes B3)
- Implement the dead `scaleFactor` (`IfcIngestController` ~l.636-640): compute from the model's length unit
  vs the project `ProjectCoordinateSystem.LengthUnit` (or a metre canonical). Use the IFC unit scale
  (`XbimIfcIngester.ExtractUnitScaleToMm`) on the IFC path; for the Revit georef block, honor its `lengthUnit`.
- Ensure `AutoAlignService`'s scale path and the ingest path agree (they currently diverge).
- Decide the mesh-unit convention once (glTF is metres) and make the two Revit GLB writers consistent
  (`GlbSerializer` metres vs `RevitGltfExporter` mm) — or record the unit on the model and rescale in the
  viewer via `ProjectModel.Units`. Pick ONE and document it.
- **Acceptance:** a model whose unit ≠ project canonical gets a non-1.0 `ScaleFactor` and renders at correct
  size; unit test the scale computation for ft/mm/m → metre canonical.

### P4 — StingBridge/core producer georef (fixes B5)
- `stingtools_core`: populate `GeorefDescriptor` fully — read `IfcMapConversion` (E/N/height/scale +
  `XAxisAbscissa/Ordinate`→`true_north_deg`), `IfcProjectedCRS`→`crs_epsg`, and set `length_unit` from the
  file's actual unit (`ifcopenshell.util.unit.calculate_unit_scale`); stop labelling metre magnitudes as mm.
- StingBridge: attach the georef block to the `/models` upload (the P2 georef upload field), so ArchiCAD/IFC
  models get the same server-side auto-transform as Revit. (Live ArchiCAD JSON path has no georef source —
  leave it; note it.)
- **Acceptance:** core unit tests for the populated descriptor (CRS, true-north, correct unit); a StingBridge
  test asserting the upload carries the georef block.

### P5 — Unify the two translation conventions + AABB freshness (fixes B4, B6)
- Pick ONE convention (recommend: transform each model from its own CRS origin into the **project**
  `ProjectCoordinateSystem` origin frame, so relative offsets are preserved and it composes with a
  project-level basepoint). Use it in both `IfcIngestController.UpsertProjectModelTransformAsync` and
  `AutoAlignService`.
- Recompute SceneNode world AABBs whenever a transform changes (extract the recompute in
  `ModelTransformController.Upsert` into a shared service; call it from AutoAlign + ingest-auto-transform).
- **Acceptance:** two models with different base points but the same CRS keep their true relative offset
  (test); manifest AABBs match rendered positions after an auto-transform.

### P6 — Tekla-via-IFC (document + verify, no native connector)
- Tekla has no producer; its only real route is **Tekla-authored IFC file upload** through the generic
  IFC ingest (`XbimIfcIngester` already recognizes Tekla psets + `IfcMapConversion`). Verify a Tekla IFC
  with `IfcMapConversion` flows through P1–P5 and lands aligned; document this as the supported Tekla path
  in the placement doc. A native Tekla plugin is explicitly out of scope.

---

## 5. Test / verification strategy (do all that apply per phase)
- Server unit/logic: EF InMemory (construct `PlanscapeDbContext` directly).
- Constraints/relational: in-memory SQLite.
- Real Postgres end-to-end (the payoff test): `[SkippableFact]`/`PLANSCAPE_TEST_PG` — **ingest two IFC
  fixtures with different `IfcMapConversion` origins + shared CRS → assert both transforms auto-applied and
  their transformed AABBs overlap** where expected (rolled-back transaction; throwaway pg on :55432).
- Plugin: `dotnet build` the Revit project if touched (command in §3).
- Viewer: assert `applyModelTransform` precedence (manual > auto > none). If no JS harness exists, keep the
  diff minimal, reason it explicitly in the PR, and flag manual-verify with two real models.
- Every PR: 0 build errors; state exactly what ran and its result; note anything you couldn't run and why.

## 6. Definition of done (recap)
Two same-site models from different tools overlay automatically in the viewer — correct position, rotation
(true north), and scale — with no manual transform entry, manual override still respected, dead unit code
removed, conventions unified, AABBs fresh, and the whole thing covered by InMemory + SQLite + a Postgres
end-to-end test. Update `docs/COORDINATION_AUDIT_FINDINGS.md` + `docs/ROADMAP.md` (the R7 'Units' item and
the coordinate-federation backlog) as items close.

## 7. Key files (grounding)
- Server: `src/Planscape.Core/Coordinates/ModelTransformMath.cs`; `src/Planscape.Core/Entities/ProjectModelTransform.cs`,
  `ProjectCoordinateSystem.cs`, `IfcAlignmentReport.cs`; `src/Planscape.API/Controllers/ModelsController.cs`,
  `ModelTransformController.cs`, `IfcIngestController.cs`, `AlignmentController.cs`, `CoordinateSystemController.cs`;
  `src/Planscape.Infrastructure/Services/AutoAlignService.cs`, `IfcAlignmentValidator.cs`, `XbimIfcIngester.cs`;
  `src/Planscape.API/wwwroot/viewer.html` (`applyModelTransform`), `coordination-viewer.js`.
- Revit: `StingTools/BIMManager/RevitGltfExporter.cs`, `PublishModelCommand.cs`, `PlanscapeServerClient.cs`;
  `StingTools/Commands/IFC/GlbSerializer.cs`, `IFC_PushModelCommand.cs`; `StingTools/Clash/ClashExportContext.cs`.
- Python: `stingtools-core/python/stingtools_core/hosts/adapter.py`, `hosts/ifc_file.py`;
  `StingBridge/watch/ifc_watcher.py`, `StingBridge/planscape/client.py`.
- Prior art / do-not-re-report: `docs/COORDINATION_AUDIT_FINDINGS.md` (BLK-2), `docs/ROADMAP.md` (R7),
  `docs/MULTI_HOST_INTEGRATION_PROMPT.md`.
