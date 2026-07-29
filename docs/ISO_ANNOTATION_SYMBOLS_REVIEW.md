# ISO Annotation Symbols — Review, Research & Recommendation

**Branch**: `claude/iso-annotation-symbols-88c820`
**Date**: 2026-07-29
**Status**: Research / advisory. No code changed.

---

## 1. What StingTools already has

`StingTools/Data/Symbols/` holds **24 catalogues / ~950 symbol definitions**, all JSON-driven and
built at runtime by `Core/Symbols/SymbolLibraryCreator.cs` (2,549 lines).

| Catalogue | Count | Nature |
|---|---:|---|
| `STING_ISO6412_SYMBOLS.json` | 261 | Spool/isometric notation (valves, welds, fittings, hangers) |
| `STING_ELEC_SYMBOLS.json` | 62 | Electrical devices |
| `STING_SLD_SYMBOLS*.json` (5 files) | 242 | SLD — IEC / BS / IEEE / NFPA / CIBSE variants |
| `STING_FP_SYMBOLS.json` | 49 | Fire protection |
| `STING_MEP_SYMBOLS.json` / `PLUMBING` / `LIGHTING` / `BMS` / `TELECOM` / `GAS` / `EARTHING` / `DRAINAGE` / `SAFETY` | ~200 | Discipline devices |
| `STING_STRUCTURAL_ANNOTATIONS.json` | 17 | Structural notation |

Supporting engine: `SymbolStandardRegistry` + `SymbolStandardResolver` (region/discipline →
standard), `SymbolScaleEngine`, `SymbolViewContextResolver`, `SymbolDriftDetector`,
`SymbolCoverageAuditor`, `SymbolOrphanHealer`. This is a genuinely strong foundation — the
standards-resolution layer is better than most commercial add-ins.

**The catalogue is ~95% device symbols.** Almost nothing in it is a *drafting* annotation.

---

## 2. The three real gaps

### Gap A — No true view-marker families (the blocking one)

`STING_DRAWING_TYPES.json` has 10 drawing types referencing 6 section/elevation/callout marker
families:

```
STING_SECTION_MARK · STING_SECTION_HEAD · STING_ELEV_MARK
STING_ELEV_MARKER  · STING_CALLOUT_HEAD · STING_INTERIOR_ELEV
```

**None of these exist anywhere** — not in a symbol catalogue, not as an `.rfa`. Verified:

```bash
grep -rl 'STING_SECTION_MARK\|STING_ELEV_MARK\|STING_CALLOUT_HEAD' StingTools/
# → only STING_DRAWING_TYPES.json (the reference itself)
```

`Families/` contains 10 subfolders and **zero `.rfa` files** — only two `.params.txt` stubs under
`Families/Annotation/`. So `DrawingTypeValidator`'s section-marker pre-flight fails on every one of
those 10 types, and `sectionMarker` is a silent no-op in production.

The reason this can't be patched from the existing catalogue: Revit view markers are **their own
family categories** with dedicated templates —

| Marker | Template | Category |
|---|---|---|
| Section head/tail | `Section Tag.rft` | Section Tags (`OST_SectionHeads`) |
| Callout head | `Callout Tag.rft` | Callout Tags |
| Elevation mark | `Elevation Mark Body.rft` + `Elevation Mark Pointer.rft` | Elevation Marks |
| Grid head | `Grid Head.rft` | Grid Heads |
| Level head | `Level Head.rft` | Level Heads |
| Spot elevation | `Spot Elevation Symbol.rft` | Spot Elevation Symbols |
| View title | `View Title.rft` | View Titles |
| Revision tag | `Revision Tag.rft` | Revision Cloud Tags |

`SymbolLibraryCreator` uses **none of these** — grep for them across the repo returns nothing. It
only ever loads `Metric Generic Annotation.rft` and model-category templates. A GenericAnnotation
pictogram **cannot be assigned** as a section's marker; Revit only accepts a family of the matching
marker category. The `ISO6412_SECTION_HEAD` / `_NORTH_ARROW` / `_GRID_BUBBLE` / `_LEVEL_HEAD`
entries in the ISO 6412 catalogue are decorative pictograms for spool sheets, not usable view markers.

### Gap B — The authoring engine cannot create labels

`SymbolDefinition` supports `lines`, `arcs`, `filledRegions`, `connectionLines`, `text`. The `text`
path resolves to `TextNote.Create` (`SymbolLibraryCreator.cs:950`) — **static text**, not a
parameter-bound Label.

This matters because every useful view marker is *mostly label*: a section head is a circle plus
`{Detail Number}` and `{Sheet Number}`; a grid head is a circle plus `{Name}`; a level head is
`{Name}` + `{Elevation}`. Without labels you get empty circles.

The Revit API has **no method to create a Label element** in an annotation family. `FamilyManager`
creates and manages *parameters*, but the Label (the graphic that displays a parameter) has no
creation API. This is long-standing and confirmed by Autodesk's own guidance that copying an
existing tag family is the feasible approach. It matches what this repo already learned on the tag
family label-tier work — the same wall, hit from the other side.

**Consequence: the runtime-generation strategy that works for device symbols does not work for view
markers.** They must be hand-authored `.rfa` seeds, committed, and *loaded* rather than *generated*.

### Gap C — No general drafting annotation set

Beyond view markers, the following are absent from all 24 catalogues:

| Domain | Missing | Standard |
|---|---|---|
| Drawing conventions | North arrow (true annotation), scale bar, projection symbol (1st/3rd angle), break line, match line head, key plan | ISO 128-30/-34, ISO 5455, ISO 5456 |
| Levels & slopes | Spot elevation, datum/level marker, fall/slope arrow, rise & drop | ISO 129-1 |
| Welding | Full ISO 2553 arrow-and-reference-line system (elementary symbols, supplementary, dimensions) | ISO 2553 |
| Machining | Surface texture, GD&T feature control frames | ISO 1302, ISO 1101 |
| Revisions | Revision cloud tag, revision triangle/delta | ISO 7200 / BS 8888 |
| Materials | Hatch/material indication key | ISO 128-50 |

The 31 "Welds" in the ISO 6412 set are *joint-location pictograms* on a spool line, not the ISO 2553
arrow/reference-line symbol system used on fabrication details. Different thing, both needed.

---

## 3. Drawing-production automation — current state

`Core/Drawing/AnnotationRunner.cs` (1,109 lines) is well built: rule-driven tagging
(`TagByRules` → `IndependentTag.Create`, density-gated, idempotent via a tagged-element index) and
dimensioning (`DimByRules` → `GridDimensioner` / `MEPDimensioner` / `DrainageInvertDimensioner`).
36 tag families bind per-category via `annotation.tagFamilies` across the 93 drawing types, and
those resolve against `LABEL_DEFINITIONS.json`.

Open items already recorded in `docs/ROADMAP.md`:

- **B1** — `AnnotationConditionEvaluator` has **no call site**; `denseUntilScale` and
  `dimensionStrategy` are silent no-ops on 48 shipped drawing types.
- Two competing grid-dimensioning implementations (`DimGrids` vs `GridDimensioner`) need converging.
- Size-variant tag selection is not wired into `DrawingProducer` / `AnnotationRunner`.

**What is entirely missing from the runner: symbol placement.** It places tags and dimensions. It
never places a north arrow, scale bar, section marker, level datum, match-line head, or revision
tag. There is no `SymbolRule` kind — only `_tagRuleKinds` (`AutoTag`, `RoomTag`, `SpaceTag`,
`AreaTag`, `MaterialTag`, `KeynoteTag`, `MultiCategoryTag`).

---

## 4. How GRAITEC configures annotation automation

From the PowerPack for Revit documentation, its **Annotations Configuration** dialog has five tabs:

| Tab | Purpose |
|---|---|
| Dimensioning | Dimension line types + spacing between chains for Auto-Dimensioning |
| Quick Dimension | Separate plan-view and side-view configs; select which categories get dimensioned in each |
| Join Dimension | Dimension line types for joining/merging chains |
| Tags and Symbols | Symbol behaviour options (e.g. show opening height) |
| Level Dimension | Spot dimension types |

Two things worth copying, and one worth not:

**Worth copying — the view-context split.** GRAITEC splits Quick Dimension config by *plan vs side
view* with a different category set in each. StingTools' `AnnotationRulePack` is flat: one rule set
per drawing type regardless of the view's orientation. Since a drawing type already knows its
`purpose` (Plan / Section / Elevation / Detail), this split is nearly free and materially improves
output — you dimension grids and openings in plan, levels and heights in section.

**Worth copying — content ships with the tool.** GRAITEC bundles **1,100+ ready families**
(rebar annotations, reinforcement and formwork symbols, dimensions, title blocks), multi-language.
Their automation is thin over a fat, hand-authored library. StingTools has inverted this: a fat
generator over an empty `Families/` folder. For device symbols that inversion is a genuine
advantage. For **annotation** it is the wrong trade, because of Gap B.

**Not worth copying — dialog-scoped config.** GRAITEC's settings live in a modal dialog per
machine/user. StingTools' JSON + corporate-baseline + `_BIM_COORD` project-override model is
strictly better: version-controllable, shareable, checksum-lockable. Keep it.

---

## 5. Recommendation

### Strategy: split the pipeline by what the API can actually do

| Track | Content | Method |
|---|---|---|
| **Generated** (today's path) | Device symbols, SLD glyphs, spool notation — geometry only, no labels | `SymbolLibraryCreator` from JSON. Unchanged. |
| **Authored** (new) | View markers + drafting symbols — anything needing a Label | Hand-author `.rfa` in the Family Editor once, commit to `Families/Annotation/`, load + place from JSON rules |

Trying to close Gap A with the generator will fail on the Label API. Accept that and build the
loading/placement half properly.

### Phase 1 — Author the ISO view-marker set (~30 families, unblocks 10 drawing types)

Author against the correct templates, one per marker category. Minimum viable set:

- Section head + tail (ISO 128-34 / BS 1192 convention), 2 variants
- Callout head + tail
- Elevation mark body + 4-way pointer (interior + exterior variants)
- Grid head (circle + hexagon)
- Level head (plan datum + section datum)
- Spot elevation symbol (3 forms)
- View title
- Revision tag + revision delta
- North arrow, scale bar (true annotation, scale-aware)
- Break line, match line head, key plan marker

Naming must match the strings already in `STING_DRAWING_TYPES.json` so the 10 dangling references
resolve on day one.

Note: `Families/` currently holds **zero `.rfa`**, so this also establishes the binary-content
intake SOP that `Families/README.md` describes but nothing has yet exercised.

### Phase 2 — `AnnotationSymbolRule` in the rule pack

Add a third rule kind alongside tags and dimensions:

```jsonc
"annotation": {
  "symbols": [
    { "kind": "NorthArrow",  "family": "STING_NORTH_ARROW",
      "placement": "SheetRelative", "x": 0.92, "y": 0.90,
      "views": ["Plan", "RCP"] },
    { "kind": "ScaleBar",    "family": "STING_SCALE_BAR",
      "placement": "SheetRelative", "x": 0.08, "y": 0.04,
      "scaleAware": true },
    { "kind": "LevelDatum",  "family": "STING_LEVEL_HEAD",
      "placement": "PerLevel", "views": ["Section", "Elevation"] }
  ]
}
```

Then `AnnotationRunner.SymbolsByRules` — mirroring `TagByRules`, including its idempotency index so
re-running a drawing type doesn't duplicate. Normalised 0..1 sheet placement matches the existing
`DrawingSlot` convention, so it stays paper-size independent.

### Phase 3 — Wire the dormant rule-pack fields

Do this in the same pass, not separately: give `AnnotationConditionEvaluator` its call site, and
honour `denseUntilScale` + `dimensionStrategy`. They're already authored across 48 drawing types and
currently do nothing. Converge `DimGrids` / `GridDimensioner` here too (ROADMAP B1) rather than
adding a third path.

### Phase 4 — Purpose-scoped rules (the GRAITEC lesson)

Let `AnnotationRulePack` resolve per `DrawingType.purpose`, so Plan / Section / Elevation / Detail
each carry their own tag + dimension + symbol set instead of sharing one flat pack.

### Phase 5 — ISO 2553 welding + fabrication annotation

Arrow-and-reference-line system for fabrication details. Depends on Phase 1's authoring SOP being
proven. Lower priority than 1–4 — it serves the fabrication package, not general drawing production.

### Deliberately deferred

- **GD&T (ISO 1101) / surface texture (ISO 1302).** Mechanical-drafting standards. Little use in an
  AEC/FM plugin unless plant fabrication becomes a target.
- **Regenerating existing device symbols.** They work. Leave them.

---

## 6. Sequencing note

Phase 1 is hand-authoring in the Revit Family Editor — it cannot be scripted, and it is the
dependency for everything after it. Phases 2–4 are ordinary C# and can be built against a small
stub set (grid head + level head + north arrow) before the full 30 families are drawn, so the code
and the content can proceed in parallel.

---

## Sources

- [GRAITEC PowerPack — Annotations Configuration](https://www.graitec.com/Help/PowerPack_for_Revit/En/Annotations_Configuration.htm)
- [GRAITEC PowerPack — Dimension tools](https://www.graitec.com/Help/PowerPack-for-Revit/desktop/Tools-Dimension.htm)
- [GRAITEC PowerPack for Revit — Autodesk App Store](https://apps.autodesk.com/RVT/en/Detail/Index?appLang=en&id=4890983144102907008&os=Win64)
- [The Revit Family API — Jeremy Tammik](https://jeremytammik.github.io/tbc/a/0199_family_api.htm)
- [Creating a custom tag family via the Revit API — Autodesk forum](https://forums.autodesk.com/t5/revit-api-forum/creating-custom-tag-family-with-dynamic-parameters-via-revit-api/td-p/13253350)
- [Create a Custom Elevation Tag — Autodesk Help](https://help.autodesk.com/view/RVT/2024/ENU/?guid=GUID-5760B8B6-1E44-4F2D-9ECB-42F52F36741A)
- [Annotation Families — Modelical](https://www.modelical.com/en/gdocs/annotation-families/)
