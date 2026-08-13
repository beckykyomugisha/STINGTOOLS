# ISO Annotation Symbols — Revised Plan

**Branch**: `claude/iso-annotation-symbols-88c820`
**Date**: 2026-07-29
**Supersedes**: the phased plan in `ISO_ANNOTATION_SYMBOLS_REVIEW.md` §5
**Status**: Plan. No implementation yet.

---

## 1. What changed since the first review

The first review concluded that view markers must be hand-authored because the Revit API cannot
create Labels. That is still true, but it framed the cost too high. Two things reopened it:

1. **Labels can be inherited.** A seed `.rfa` that already contains a Label can be opened, stripped
   of geometry, redrawn, re-parameterised and saved under a new name — the Label survives because
   it is never touched. Autodesk's own guidance is that copying an existing family is the feasible
   route. This converts "hand-author 30–50 families" into "hand-author ~10 seeds, generate the rest".
2. **`SymbolDefinition.SourceFamilyPath` already exists** (`SymbolDefinition.cs:217`) and is carried
   through the creator (`SymbolLibraryCreator.cs:373`) — but is never used to load anything, and no
   catalogue sets it. The hook for seed inheritance is already in the schema, dead.

So the plan below is materially more automated than the first version.

---

## 2. Verification results — all 884 symbols

Audited every catalogue under `StingTools/Data/Symbols/`.

### Clean

| Check | Result |
|---|---|
| Total symbols | 884 (730 GenericAnnotation) |
| Duplicate IDs | 0 |
| Shared parameters | 922 / 922 = 100% |
| Empty geometry arrays | 0 |
| Coordinates rejected at build (`>|2.0|`) | 0 |

### Defects

| # | Finding | Count | Severity |
|---|---|---:|---|
| V-1 | **No `geometry` block at all** — `ISO6412_VLV_MOV`, `_AOV`, `_SOV`, `_HOV`. These build as *empty families*. | 4 | High |
| V-2 | **GenericAnnotation symbols with zero parameters** — pure pictures; cannot be tagged, scheduled, or reported to a Note Block. | 142 | High |
| V-3 | **`formulaBindings` used by nothing** across all 884. | 884 | High |
| V-4 | **`typeVariants` used by nothing** across all 884. | 884 | High |
| V-5 | **Every symbol is `status: draft`.** Nothing has passed any verification gate. | 884 | Medium |
| V-6 | **Static text baked as `TextNote`** instead of a Label — `'DB'`, `'MCC'`, `'kWh'`, `'EV'`. Correct for fixed glyph marks, wrong wherever the value should be live. | 94 | Medium |
| V-7 | **Geometry exceeds the documented `-0.5..+0.5` normalisation contract** (max 1.6×). Concentrated in SLD (35 of 49). | 49 | Low — see note |

**Note on V-7**: this is mostly *not* a bug. In `SLD_MCB`, `SLD_MCCB`, `SLD_ACB` and the LPS symbols,
the out-of-box coordinates are terminal leads and earth stems extending beyond the symbol body —
conventional for single-line diagrams. Nothing is rejected at build time (`ValidateGeometryCoord`
only rejects beyond ±2.0). The actual defect is that the *contract is unstated*: the ISO 6412 header
declares "all geometry normalised -0.5..+0.5" while 49 symbols legitimately need body-plus-lead.
Fix the contract, not the geometry — see P1.

### The compound defect: scale-awareness is inert

This is the highest-value finding and it spans three subsystems that each assume another one does
the work:

- `MepSymbolEngine.cs:16-17` documents the contract: `model_mm = target_mm × Symbol Scale`, and
  states "family formulas derive model size".
- `IsoSymbolPlacer.cs:243,475` writes `Symbol Scale` onto every placed instance.
- 524 of 884 symbols declare the `Symbol Scale` parameter.
- **No family has a formula consuming it.** `SetFormula` is reachable only via `formulaBindings`
  (`SymbolLibraryCreator.cs:1191`), and zero catalogues declare any.
- `SymbolLibraryCreator.cs:1443` says so explicitly: *"the geometry is not parametrically bound to it"*.

**Net effect: every symbol is fixed-size baked geometry.** `Symbol Scale` is written and ignored.
A symbol authored for 1:50 is wrong at 1:100. The parameter is decorative on 524 symbols.

---

## 3. What is and isn't automatable — verified

| Capability | Status | Evidence |
|---|---|---|
| Load any `.rft` template | ✅ works | `CreateFamilyDocument` is template-agnostic |
| Draw lines/arcs in annotation families | ✅ works | `NewSymbolicCurve`, `SymbolLibraryCreator.cs:736` |
| Sketch plane in annotation family | ✅ solved | Cannot be *created*; harvested from an existing `ReferencePlane` (~line 700) |
| Add parameters (incl. `YesNo`) | ✅ works | `AddParameter`, `SpecTypeId.Boolean.YesNo:1381` |
| Shared parameters | ✅ works | `ExternalDefinition` path, line 1303 |
| Formulas | ✅ works | `SetFormula:1191` — used by nothing |
| Type variants | ✅ works | `AddTypeVariants:1063` — used by nothing |
| Nest families | ✅ works | `LoadFamily` + `NewFamilyInstance`, line 1926 |
| **Create a Label** | ❌ **impossible** | `TextElement` has no public constructor; only `TextNote` is instantiable |
| **Rebind an existing Label** | ❌ **impossible** | No Label class in the API at all |
| Inherit a Label from a seed | ✅ viable | Untouched elements survive a save-as |

**Consequence**: seeds are keyed by **label layout**, not by symbol. A seed whose label shows
`Detail Number` can never be retargeted; you need a separate seed per distinct label set.

Estimated seed count — roughly 10:

| Seed | Label content |
|---|---|
| Section head | Detail Number + Sheet Number |
| Callout head | Detail Number + Sheet Number |
| Elevation mark body | Detail Number + Sheet Number |
| Grid head | Name |
| Level head | Name + Elevation |
| View title | View Name + Scale + Detail Number |
| Revision tag | Revision Number |
| Spot elevation | (system-driven) |
| Generic annotation — single label | 1 generic text param |
| Generic annotation — tier stack | N stacked rows for the presentation-mode matrix |

**Hard limit that survives**: Section Tags are restricted to Detail Number / Sheet Number. Custom
data cannot be injected into a section head by any route.

**The route that *does* work for other markers**: shared parameter in the marker's label + the same
shared parameter bound as a **project parameter to the host category**. Verified for Grid Heads
(Grids) and Level Heads (Levels). `CATEGORY_BINDINGS.csv` currently binds **nothing** to Grids,
Levels, Views, Sheets or Revisions — that is the enabling work, and it is cheap.

---

## 4. Target architecture

Three generation modes instead of today's one:

| Mode | Trigger | Used for | Labels? |
|---|---|---|---|
| **Template** (today) | no `sourceFamilyPath` | Device symbols, SLD glyphs | No |
| **Seed-inherited** (new) | `sourceFamilyPath` set | View markers, tags, anything with a label | Yes — inherited |
| **Augment** (exists) | `FamilyAugmentationEngine` | Adding params to vendor families | N/A |

Seed-inherited build sequence:

```
open seed .rfa  →  purge symbolic curves  →  draw ISO geometry from JSON
                →  add params + shared params  →  set formulas
                →  mint type variants  →  emit type catalog .txt  →  save-as
```

The Label is never in the mutation path, so it survives.

---

## 5. Phased plan

### P0 — Repair (½ day, no dependencies)

- Author geometry for the 4 empty valve symbols (V-1), or remove them if they are duplicates of
  the operator-suffixed variants.
- Formalise the normalisation contract (V-7): add an explicit `bodyExtent` (0.5) vs `overallExtent`
  field to `SymbolDefinition`, document lead-overflow as legal for SLD, and tighten
  `ValidateGeometryCoord` from ±2.0 to the declared `overallExtent` so genuine typos surface.
- Extend `Symbols_Validate` with the checks used in this audit: empty-geometry, zero-parameter GA,
  normalisation overflow, `status` distribution.

**Exit**: `Symbols_Validate` reports 0 empty-geometry and 0 unexplained overflow.

### P1 — Real-world sizing for model-category symbols ✅ part 1 done

**This phase was re-scoped during implementation. The original plan was wrong** and is recorded
here because the reasoning matters.

The original P1 proposed adding a `Symbol Scale` formula to *every* generated family. That would
have broken 726 of them. Annotation and detail templates draw in **paper space** — Revit already
holds them at a constant plotted size at any view scale — so a scale formula there double-scales.

The parameter distribution showed the mismatch exactly:

| Family type | Count | Declares `Symbol Scale` | Actually needs it |
|---|---:|---:|---|
| GenericAnnotation | 726 | 520 | no — paper space |
| MEPEquipment / MEPAccessory | 154 | **0** | yes — model space |

It is on precisely the wrong set.

The real defect underneath was worse. All 154 model-category symbols had geometry expanded by
`Scale(coord, symbolSize)` — a *paper* dimension — while living in **model** space. So
`ELEC_SOCKET_SINGLE` was a 4 mm object (real 13A plates are 86 mm), `ELEC_SWITCH_1G` 3.5 mm,
`ELEC_EV_CHARGER` 6 mm. At 1:50 a 4 mm object plots at 0.08 mm. **Those 154 families were
effectively invisible in any model view**, and since none declared `Symbol Scale`, the placer's
write-back could not rescue them.

**Decision taken**: hybrid — real-world model geometry plus a nested annotation glyph for plan
legibility. Model stays dimensionally correct for clash, quantities and COBie; plans stay schematic.

**Done in this pass**:
- `SymbolDefinition.RealSizeMm` + `PlanSymbol` added to the schema.
- `ResolveGeometrySizeMm(def, kind, result)` — the single size-resolution point. Paper templates
  keep `symbolSize`; model templates use `realSizeMm` and **warn loudly** on fallback rather than
  silently building a millimetre-scale device.
- The connector path resolves size through the same helper. It previously had its own copy of the
  expression, which would have left connectors floating off the geometry once sizes diverged.
- `realSizeMm` populated on all 154, from BS 1363 / BS 5839 / BS 6465 / BS EN 12845 plate and
  fixture dimensions and typical plant envelopes. Range 50 mm (sprinkler head) to 2000 mm (AHU).
- `Symbol Scale` left on the 520 annotation symbols and documented as inert by design, so nobody
  later "fixes" it into breaking them.

**Verified**: 154/154 model symbols carry `realSizeMm`; 726 annotation symbols carry none; no
symbol has a real size smaller than its paper size. Build 0 warnings / 0 errors.

**Honest scope note**: the model geometry is the existing *schematic* linework expanded to
real-world size, not manufacturer-accurate 3D. For plate-type devices (sockets, switches, call
points) an 86 mm outline is genuinely close to the product. For plant (AHU, FCU) it is a correct
footprint but not a detailed model. Swapping in manufacturer families remains
`Symbols_SwapToManufacturer`'s job.

**Still open — P1 part 2**: `PlanSymbol` is in the schema but unpopulated, and the generator does
not yet nest. Most of the 154 have no GenericAnnotation twin to nest, so this needs ~154 glyph
annotations authored first. That is P4-scale content work, not a code gap.

**Exit for part 2**: a socket placed in a 1:50 plan shows its schematic glyph, not an 86 mm square.

### P2 — Seed inheritance (3–4 days code + authoring)

- Implement the `sourceFamilyPath` load path in `SymbolLibraryCreator` (schema field already exists).
- Add a purge step that removes seed geometry while preserving Labels, reference planes and params.
- Add view-marker templates to the template map: `Section Tag.rft`, `Callout Tag.rft`,
  `Elevation Mark Body.rft`, `Elevation Mark Pointer.rft`, `Grid Head.rft`, `Level Head.rft`,
  `Spot Elevation Symbol.rft`, `View Title.rft`, `Revision Tag.rft`.
- **Manual**: author the ~10 label seeds into `Families/Annotation/`. Requires Revit; not scriptable.

**Exit**: a generated grid head displays its Name label in a project.

### P3 — Parameter provisioning (1–2 days) — *do before P4 authoring*

- Bind STING shared parameters to Grids, Levels, Views, Sheets, Revisions in `CATEGORY_BINDINGS.csv`
  (currently zero bindings to any of them).
- Add parameters to the 142 zero-parameter GA symbols (V-2), including hidden ones for Note Blocks.
- Convert the subset of the 94 static-text symbols (V-6) whose value should be live into labels via
  the P2 seed path; leave fixed glyph marks as text.

**Ordering matters**: families must be authored against parameters that already exist, or P4 gets
re-done.

### P4 — The ISO annotation catalogue (1–2 weeks)

New `STING_ISO_ANNOTATION_SYMBOLS.json` covering what no catalogue has today:

| Group | Content | Standard |
|---|---|---|
| View markers | Section head/tail, callout head, elevation body + pointer, grid head (circle + hex), level head, spot elevation, view title, revision tag | ISO 128-34, BS 1192 |
| Drawing conventions | North arrow, scale bar, projection symbol, break line, match line head, key plan | ISO 128-30, ISO 5455, ISO 5456 |
| Levels & slopes | Datum marker, fall/slope arrow, rise & drop | ISO 129-1 |
| Revisions | Revision cloud tag, revision delta | ISO 7200 |

Names must match the 6 already dangling in `STING_DRAWING_TYPES.json` — `STING_SECTION_MARK`,
`STING_SECTION_HEAD`, `STING_ELEV_MARK`, `STING_ELEV_MARKER`, `STING_CALLOUT_HEAD`,
`STING_INTERIOR_ELEV` — so 10 drawing types resolve on landing.

Backfill `typeVariants` (V-4) here: variants are the only mechanism by which a tag's graphics can
vary, because **a tag family cannot read the value it displays**. This is the delivery vehicle for
the `LABEL_DEFINITIONS.json` presentation-mode matrix.

### P5 — Placement automation (1 week)

- `AnnotationSymbolRule` as a third rule kind in `AnnotationRulePack`, alongside tags and dimensions.
- `AnnotationRunner.SymbolsByRules`, mirroring `TagByRules` including its idempotency index.
- Normalised 0..1 sheet placement, matching the existing `DrawingSlot` convention.
- Wire the dormant rule-pack fields in the same pass: `AnnotationConditionEvaluator` (no call site),
  `denseUntilScale`, `dimensionStrategy` — authored across 48 drawing types, currently silent.
  Converge `DimGrids` / `GridDimensioner` here rather than adding a third path (ROADMAP B1).

### P6 — Sections & callouts (1 week)

- **Auto-section generator**: per element / per grid line / at N-metre intervals. Build the
  `BoundingBoxXYZ` with `BasisX` along the element, `BasisY` vertical, `BasisZ` = cross product;
  `Min`/`Max` set crop and far-clip. Hand off to the existing DrawingType pipeline, which already
  supplies scale — so "sections at set ratios" needs only the generator, not new scale plumbing.
- **Auto-callout**: `ViewSection.CreateCallout`. Absent from the codebase entirely.
- **Reference views**: `CreateReferenceSection` / `CreateReferenceCallout` + `ReferenceableViewUtils`
  for "see 3/A-201" markers across a sheet set. Also entirely absent. Caveat: only cropped views
  can be referenced, except drafting views.

### P7 — Note Block integration (2–3 days)

Only generic annotation families report to Note Blocks, and parameters not on a label still
schedule. With P3 done, this auto-generates symbol legends and general-notes schedules from data
already carried on every symbol. Cheap, and currently unused.

### Deferred

- **ISO 2553 welding** arrow/reference-line system — serves the fabrication package, not general
  drawing production. After P2 proves the seed SOP.
- **GD&T (ISO 1101) / surface texture (ISO 1302)** — mechanical-drafting standards, little AEC/FM use.
- **Regenerating existing device symbols** beyond the P1 scale retrofit. They work.

---

## 6. Dependency order

```
P0 ──┐
P1 ──┼──> P3 ──> P4 ──> P5 ──> P7
P2 ──┘                   └──> P6
```

P0/P1/P2 are independent and can run in parallel. P3 must precede P4 authoring. P2's seed authoring
is the only irreducibly manual step and is on the critical path for P4 — start it first.

---

## 7. Open questions

1. **Spot Elevation Symbols and View Titles** — unverified whether they accept the host-category
   binding trick that Grid/Level Heads do. Views can take project parameters, but this is from
   forum sources, not tested. Verify in Revit before authoring those two seeds.
2. **Parametric symbolic curves** — P1 assumes generated geometry can be dimension-driven in an
   annotation family. Prototype before committing to 884 symbols.
3. **Type catalogs for annotation families** — documented for loadable families generally; not
   confirmed specifically for annotation categories. Verify before building the emitter in P4.
4. **The 4 empty valve symbols** — repair or delete? Depends on whether `ISO6412_VLV_MOV` etc. are
   intended as distinct symbols or duplicates of operator-suffixed variants. Needs a decision.

---

## 8. Sources

- [The Revit Family API — Jeremy Tammik](https://jeremytammik.github.io/tbc/a/0199_family_api.htm)
- [Revit API — access to Text & Label types (TextElement has no public constructor)](https://forums.autodesk.com/t5/revit-api-forum/revit-api-access-to-text-amp-label-types-within-family-and/td-p/3588666)
- [Cannot create labeling family in Revit — Autodesk](https://www.autodesk.com/support/technical/article/caas/sfdcarticles/sfdcarticles/Cannot-create-labeling-family-in-Revit.html)
- [Section head annotation label contains limited list of parameters — Autodesk](https://www.autodesk.com/support/technical/article/caas/sfdcarticles/sfdcarticles/Section-head-annotation-label-contains-limited-list-of-parameters-in-Revit.html)
- [Create a Custom Elevation Tag — Autodesk Help](https://help.autodesk.com/view/RVT/2024/ENU/?guid=GUID-5760B8B6-1E44-4F2D-9ECB-42F52F36741A)
- [Create an Annotation Schedule (Note Block) — Autodesk Help](https://help.autodesk.com/cloudhelp/2025/ENU/Revit-DocumentPresent/files/GUID-A2394758-978A-4E48-A2B4-3A8A690D01F5.htm)
- [Label Parameters Options (prefix/suffix/break/spaces) — Autodesk](https://knowledge.autodesk.com/support/revit-products/learn-explore/caas/CloudHelp/cloudhelp/2021/ENU/Revit-Customize/files/GUID-4CCEF31F-FC5F-46DE-9600-339CA4163640-htm.html)
- [Breaks do not work when the preceding parameter is empty — Autodesk](https://www.autodesk.com/support/technical/article/caas/sfdcarticles/sfdcarticles/Breaks-between-parameters-does-not-work-if-the-preceding-parameter-is-empty-or-not-filled-in-a-Annotation-Tag-family-in-Revit.html)
- [ViewSection.CreateCallout — Revit API Docs](https://www.revitapidocs.com/2015/272ce735-271e-6ae3-8b36-c31207c99e56.htm)
- [ViewSection.CreateSection — Revit API Docs](https://www.revitapidocs.com/2022/d6228f68-3643-8aaf-72bb-f9a0b4125886.htm)
- [Automating sections with the Revit API — LearnRevitAPI](https://www.learnrevitapi.com/blog/how-to-automate-window-sections-in-revit-api-and-python)
- [Generic Annotations in Revit — Paul F. Aubin](https://paulaubin.com/blog/generic-annotations-in-revit/)
- [Level head with shared parameters — Autodesk Community](https://forums.autodesk.com/t5/revit-architecture-forum/level-head-with-both-base-point-elevation-and-survey-elevation/td-p/12467276)
- [Adding a label to a grid bubble — Autodesk Community](https://forums.autodesk.com/t5/revit-architecture-forum/adding-label-to-grid-bubble/td-p/9874789)
- [Revit Type Catalogs — GRAITEC](https://graitec.com/uk/blog/revit-type-catalogs/)
- [GRAITEC PowerPack — Annotations Configuration](https://www.graitec.com/Help/PowerPack_for_Revit/En/Annotations_Configuration.htm)
