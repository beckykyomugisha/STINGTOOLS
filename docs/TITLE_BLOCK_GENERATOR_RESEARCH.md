# Title Block Family Generator — Feasibility Research

Can we author title-block `.rfa` families programmatically the way
`Tags/TagFamilyCreatorCommand.cs` already does for tag families?
**Yes — the API supports the full pipeline** (open template, add
parameters, draw lines, place dynamic labels, save). This note
inventories what's possible, what's awkward, what's blocked, and
proposes a phased implementation.


## 1. Existing pattern — TagFamilyCreatorCommand

`StingTools/Tags/TagFamilyCreatorCommand.cs` (1,729 lines, 4 commands)
already proves the family-creation pipeline works in this codebase.
Per-family flow:

1. `app.NewFamilyDocument(templatePath)` — opens the right `.rft`
   (`Annotations/Tag.rft`, `Annotations/<Category> Tag.rft`).
2. `famDoc.IsFamilyDocument == true` — confirms we got a family doc.
3. `app.SharedParametersFilename = sharedParamFile` (with restore in
   `finally`) — points Revit at the STING shared param file so the
   parameter definitions resolve.
4. `OpenSharedParameterFile() → DefinitionFile` — reads the file.
5. Iterate `defFile.Groups[].Definitions`, find each
   `ExternalDefinition` by name.
6. `famDoc.FamilyManager.AddParameter(extDef, GroupTypeId.General,
   isInstance: true)` inside a `Transaction` — adds the shared
   parameter to the family.
7. `famDoc.SaveAs(rfaPath, saveOpts)` + `famDoc.Close(false)`.
8. Optional: `app.OpenAndActivateDocument(rfaPath)` then
   `famDoc.LoadFamily(projectDoc, loadOptions)` from the host
   project.

Today's tag creator does steps 1, 2, 3, 4, 5, 6, 7 only. It does NOT
add geometry (lines / labels / filled regions) — the operator opens
the resulting `.rfa` in Family Editor and draws the tag layout
manually. That's the gap we'd close for title blocks.

## 2. What the Revit API provides for inside-family geometry

`Document.FamilyCreate` (only valid when `doc.IsFamilyDocument`) is an
instance of `Autodesk.Revit.Creation.FamilyItemFactory`. The methods
needed for title-block authoring:

| Method | Purpose | Notes |
|---|---|---|
| `NewDetailCurve(View, Curve)` | Detail line on the family's drafting/sheet view | Used everywhere — including this codebase's `LegendBuilder` and `MatchLineEngine`. Pass `Line.CreateBound(p1, p2)`. |
| `NewLabel(View, XYZ origin, HorizontalAlign, VerticalAlign, FamilyParameter param, string prefix, string suffix, double size)` | The dynamic `${PARAM}` cells | Bound to a `FamilyParameter` returned by `FamilyManager.AddParameter`. This is what makes title-block cells DATA-DRIVEN as opposed to static text. |
| `NewTextNotes(...)` / `TextNote.Create` | Static labels (cell name like "CLIENT") | `TextNote.Create(doc, view.Id, point, "CLIENT", textTypeId)` is already used in this codebase (`LegendBuilder`, `IssueTracker`, `StructuralPhase140`). |
| `FilledRegion.Create(doc, fillTypeId, viewId, profile)` | Solid / hatched fills (the suitability chip, status band background) | Already used in `LegendBuilderCommands.cs` × 2 sites. Profile is `IList<CurveLoop>`. |
| `NewSymbol(View, XYZ, FamilySymbol)` | Place a nested family (e.g. corporate logo as a sub-family) | Possible but the logo family must be loaded first via `famDoc.LoadFamily(...)`. |

`Document.FamilyCreate` is a different beast from `Document.Create`
— that's `ItemFactoryBase`, which is what regular project commands
(BatchSectionsCommand, MatchLineEngine, etc.) use. Inside a family
doc, the geometry surface shifts to `FamilyCreate`. Same shape, fewer
methods.

## 3. What the API supports specifically for title blocks

`OST_TitleBlocks` is the family category. Title-block templates ship
in Revit's `Family Templates/English/Titleblocks/` directory (or the
metric variants in `English-Imperial`):

- `A0 metric.rft`, `A1 metric.rft`, `A2 metric.rft`, `A3 metric.rft`,
  `A4 metric.rft`, plus US Imperial sizes.
- Each template arrives with the sheet rectangle pre-drawn at the
  paper size — width × height matches the ISO-A standard.
- The family has exactly one `View` (`Drafting View` of subtype
  `TitleBlock`). All geometry goes on that view.
- Templates also pre-register a `FamilyType` named `Default` so
  `FamilyManager.NewType` is unnecessary.

`FamilyManager.AddParameter` has an overload that takes
`ParameterType.Image` — the Revit image-parameter type. This lets
the generator add `PRJ_ORG_ORIGINATOR_LOGO_IMG` and
`PRJ_ORG_CLIENT_LOGO_IMG` parameters, which Revit later renders as
embedded images on the placed sheet (the user attaches the image at
project setup time via the sheet's instance properties).

`FamilyManager.MakeType()` then `SetFormula` lets us define a
calculated parameter. So `STING_SHEET_FULL_REF_TXT` can be a
calculated string concatenating the seven segments — so the user
edits VOLUME / LEVEL / TYPE / ROLE / SEQ separately and the full
ref recomputes automatically.

## 4. End-to-end pipeline a generator command would run

Per family (e.g. `STING_TB_A1_v2.0`):

1. Open the right template: `app.NewFamilyDocument(rftPath)`.
2. Activate shared-param file: `app.SharedParametersFilename = file`
   (with finally-restore).
3. **In one Transaction** (or `TransactionGroup` per family):
   a. `FamilyManager.AddParameter(extDef, GroupTypeId.IdentityData,
      isInstance: true)` for each of the ~35 cells in §2.1 of
      `TITLE_BLOCK_FAMILY_DESIGN.md`.
   b. For calculated cells (e.g. `STING_SHEET_FULL_REF_TXT`):
      `FamilyManager.SetFormula(fp, "$SHEET_PROJECT & \"-\" & $SHEET_ORIGINATOR
      & \"-\" & $SHEET_VOLUME & ...")`.
   c. Get the family's drafting view via
      `new FilteredElementCollector(famDoc).OfClass(typeof(View)).
      Cast<View>().First(v => v.ViewType == ViewType.DraftingView)`.
   d. **Lines** — for every cell border in the spec:
      `famDoc.FamilyCreate.NewDetailCurve(view, Line.CreateBound(p1, p2))`.
      Set the `LineStyle` to "Wide Lines" / "Medium Lines" / "Thin
      Lines" by category lookup, mirroring `MatchLineEngine`.
   e. **Static labels** — for every cell name ("CLIENT", "PROJECT"):
      `TextNote.Create(famDoc, view.Id, anchorPoint, "CLIENT",
      textTypeId)`.
   f. **Dynamic labels** — for every `${PARAM}` cell:
      `famDoc.FamilyCreate.NewLabel(view, anchorPoint,
      HorizontalAlign.Left, VerticalAlign.Middle, familyParameter,
      "", "", labelSize)` — where `familyParameter` is the
      `FamilyParameter` that `AddParameter` returned in step (a).
   g. **Filled regions** — for the suitability chip + status band:
      `FilledRegion.Create(famDoc, regionTypeId, view.Id,
      new List<CurveLoop> { boundaryLoop })`. Region type loaded
      from `famDoc` (Revit ships `Solid Black` / `Solid Light Grey`
      defaults; STING-branded fills can be loaded first via
      `FillPatternElement.Create`).
4. Commit the transaction.
5. `famDoc.SaveAs(<projectFolder>/Families/TitleBlocks/<name>.rfa,
   saveOpts)`.
6. `famDoc.Close(false)`.
7. Optional one-shot load into the host project: re-open the .rfa,
   call `loadDoc.LoadFamily(hostDoc, new TitleBlockLoadOptions())` —
   pattern proven in `Tags/SmartTagPlacementCommand.cs:2737`.

The 35 cells × line geometry × label placement adds up to roughly
150 API calls per family. That's ~3-5 seconds of execution time per
family, around 2 minutes total for the 18-family library. Today,
hand-authoring one A1 title block in Family Editor takes a designer
~3-4 hours and varies between projects.

## 5. JSON spec format

Mirror the existing `STING_TAG_RULES.json` shape so editors can
produce it without code:

```jsonc
{
  "schemaVersion": 1,
  "families": [
    {
      "id": "STING_TB_A1_v2.0",
      "templateRft": "Annotations/Titleblocks/A1 metric.rft",
      "saveAs": "Families/TitleBlocks/STING_TB_A1_v2.0.rfa",
      "category": "OST_TitleBlocks",

      "parameters": [
        { "name": "PRJ_ORG_PROJECT_NAME_TXT",  "instance": true,
          "group": "IdentityData", "type": "Text" },
        { "name": "STING_SHEET_FULL_REF_TXT",  "instance": true,
          "group": "IdentityData", "type": "Text",
          "formula": "$SHEET_PROJECT & \"-\" & $SHEET_ORIGINATOR & ..." },
        { "name": "PRJ_ORG_ORIGINATOR_LOGO_IMG", "instance": true,
          "group": "IdentityData", "type": "Image" }
      ],

      "lines": [
        { "from": [594, 0], "to": [594, 30],   "style": "Wide Lines"  },
        { "from": [0, 30],  "to": [841, 30],   "style": "Wide Lines"  },
        { "from": [641, 0], "to": [641, 30],   "style": "Medium Lines" }
      ],

      "labels": [
        { "param": "PRJ_ORG_PROJECT_NAME_TXT",
          "anchor": [620, 24], "h": "Left", "v": "Middle", "size": 3.0 },
        { "param": "STING_SHEET_FULL_REF_TXT",
          "anchor": [720, 6],  "h": "Center", "v": "Middle", "size": 5.0 }
      ],

      "staticText": [
        { "text": "PROJECT", "anchor": [602, 28], "size": 1.8 },
        { "text": "DRAWING TITLE", "anchor": [602, 18], "size": 1.8 }
      ],

      "filledRegions": [
        { "kind": "rect", "topLeft": [600, 30], "bottomRight": [841, 38],
          "fillTypeName": "Solid Light Grey" },
        { "kind": "rect", "topLeft": [641, 0],  "bottomRight": [681, 8],
          "fillTypeName": "Solid Amber" }
      ]
    }
  ]
}
```

All coordinates in mm relative to the sheet's bottom-left
(consistent with Revit's family-doc coord system). Per-family JSON
re-runnable: edit, regenerate, reload in the host project.

## 6. What's harder — the four real friction points

### 6.1 Image (logo) parameters

`FamilyManager.AddParameter(name, group, ParameterType.Image,
isInstance)` adds the parameter, but **populating** it requires the
image file to exist at family-create time AND
`famParam.Set(imageElementId)` (where `imageElementId` is an
`ImageType` loaded into the family doc via
`ImageType.Create(famDoc, imagePath)` — added in Revit 2021).

Generator command can load corporate logos from a known path
(`Families/Logos/<originator>.png`); per-project client logos stay
manual (project setup wizard prompts the user for the file).

### 6.2 Revision schedule

Title blocks need a revision schedule that grows when revisions are
added to the project. The Revit API supports
`ViewSchedule.CreateRevisionSchedule(famDoc, ...)` inside a family
doc — adds the schedule element, populates the columns from
`SchedulableField` definitions. Doable but verbose (8 columns,
column widths, per-column header formatting). One-off per family,
shareable across paper sizes.

### 6.3 QR code rendering

No native Revit API for QR. Three workable options:

1. **Bake at issue time** — generator skips the QR; at PDF-export
   time, a post-processor renders QR onto the PDF.
2. **Render to image** — pre-render `planscape://project/{id}/sheet/{guid}`
   to a PNG via `ZXing.Net` (already a project dep — see
   `StingTools.csproj`). Save as image-parameter or as a static
   image inside the family.
3. **Filled-region stippling** — synthesize the QR matrix as a
   grid of `FilledRegion` cells (option 2 cells × 2 cells minimum
   per "module"). Slow at family-create time, looks correct.

Recommendation: option 1 (bake at PDF export). The QR encodes the
final sheet GUID anyway, which only exists after the sheet is
created in the project — not at family-author time.

### 6.4 Line styles must exist in the family doc

`Wide Lines` / `Medium Lines` / `Thin Lines` exist in every Revit
family template by default, so basic borders work out of the box.
But `STING - 0.50mm` (or whatever line style the corporate set
prescribes) must be created first via `Category.GetGraphicsStyle`
+ family-doc category creation. Same pattern STING already uses for
project-side line-style creation
(`Temp/TemplateExtCommands.LineStylesCommand`); it just runs
`famDoc.Settings.Categories[OST_Lines].SubCategories.Create(...)`
inside the family doc instead of the project doc.

## 7. What's blocked / not feasible programmatically

- **Pixel-perfect typography tweaks** — Revit Family Editor's manual
  click-and-drag is 10× faster than computing exact label-anchor
  XYZ for every cell. Generator gets 95 % correct; final 5 % is
  human polish.
- **North arrow + key-plan placeholders** — these are detail-item
  families nested into the title block. Possible to reference them
  as nested families, but the FAMILY family must be authored first.
- **Custom hatch patterns** — `FillPatternElement.Create` works
  inside family docs, but the pattern grammar is tedious; ship the
  patterns project-side and have the generator look them up rather
  than re-author.
- **Embedded view of project north** — the title block's project-
  north arrow rotates with the sheet's host view; that's a Revit
  built-in, not a generator concern.

## 8. Proposed architecture

Mirror the tag-family creator pattern with one engine + four
commands.

### 8.1 New files

| Path | Lines | Role |
|---|---|---|
| `StingTools/Core/Drawing/TitleBlockFactory.cs` | ~600 | The engine. Per-family pipeline (open template → add params → draw lines → place labels → filled regions → SaveAs → close). Static helpers `BuildLineStyle`, `BuildLabel`, `BuildFilledRegion`, `BuildStaticText`, `Compile7SegmentFormula`. |
| `StingTools/Core/Drawing/TitleBlockSpec.cs` | ~150 | POCOs matching the JSON schema in §5. `TitleBlockSpec`, `ParamSpec`, `LineSpec`, `LabelSpec`, `StaticTextSpec`, `FilledRegionSpec`. |
| `StingTools/Commands/Drawing/TitleBlockFactoryCommands.cs` | ~250 | Four commands: Create / CreateAll / LoadIntoProject / Audit (mirrors the four tag-family commands). |
| `StingTools/Data/STING_TITLE_BLOCKS.json` | ~700 | Specs for the 18 families designed in `TITLE_BLOCK_FAMILY_DESIGN.md`. Editable JSON, not code. |

### 8.2 Commands

| Tag | Class | Purpose |
|---|---|---|
| `TitleBlock_Create`         | `TitleBlockCreateCommand`         | Create one title-block family from the spec, prompting the user to pick which one |
| `TitleBlock_CreateAll`      | `TitleBlockCreateAllCommand`      | Mint every family declared in `STING_TITLE_BLOCKS.json` (idempotent — skip when target `.rfa` exists and checksum matches) |
| `TitleBlock_LoadIntoProject`| `TitleBlockLoadIntoProjectCommand`| Load the matching `.rfa` files into the active project from `Families/TitleBlocks/` |
| `TitleBlock_Audit`          | `TitleBlockAuditCommand`          | Read-only — for each spec, report whether the `.rfa` exists, how many of the declared parameters are present, what cells are missing labels |

### 8.3 Wiring

- Dispatcher entries in `UI/StingCommandHandler.cs` (one-liners
  matching the `MatchLine_*` pattern).
- DOCS panel sub-tab "Title Blocks" with 4 buttons.
- Project Setup Wizard step 4 ("Title blocks") gets a "Create
  STING title blocks" checkbox that, when ticked, runs
  `TitleBlock_CreateAll` after the project setup pipeline.

### 8.4 Phase plan

| Phase | What lands | Risk |
|---|---|---|
| **170** | `TitleBlockSpec` + `TitleBlockFactory` + `TitleBlock_Create` for ONE family (`STING_TB_A1_v2.0`) — proves the API end-to-end | Low |
| **171** | `STING_TITLE_BLOCKS.json` populated with the 6 working-sheet families (A0/A1/A2/A3/A3-port/A4-port) + `TitleBlock_CreateAll` | Low |
| **172** | Add the remaining 12 specialty families (fab, authority, presentation, cover, divider, register, transmittal, clarification) | Med — each needs visual review |
| **173** | `TitleBlock_LoadIntoProject` + Project Setup Wizard integration | Low |
| **174** | Image-parameter logo handling + revision-schedule generation | Med — image API quirks |
| **175** | QR code post-processor at PDF export | Med — touches the export pipeline |

Each phase is independently committable + Revit-testable. Phase 170
is the proof point — if the API behaves as documented the rest is
near-mechanical.

## 9. Tradeoffs vs. manual Family Editor authoring

| Aspect | Manual Family Editor | Programmatic generator |
|---|---|---|
| First-time authoring | 3-4 h per family × 18 = ~60 h | 5 min/family + 1 day spec authoring |
| Re-authoring after parameter changes | re-touch every cell | edit JSON, regenerate |
| Cross-project consistency | depends on file-share discipline | guaranteed |
| Visual fidelity (typography, micro-tweaks) | excellent | 95 % — final polish needs Family Editor |
| Onboarding new sheet sizes | clone+modify ~2 h | clone JSON + regenerate ~5 min |
| Version control friendliness | binary `.rfa` (opaque diffs) | JSON + per-family `.rfa` regenerated |
| Live preview during authoring | excellent | none (must save + load to see) |
| Revit version compat | manual port | regenerate against new template |

Pragmatic recommendation: **use the generator as a "starter"** —
produces 95-%-correct families, then a designer runs through each
in Family Editor for final visual polish (10-15 min per family
vs. 3-4 h from scratch). The polished `.rfa` then lives alongside
the JSON spec in the repo, and the JSON serves as the source-of-truth
for parameters + structure, while the `.rfa` carries the visual
polish.

## 10. Risks / open questions

- **`NewLabel` overload signatures vary by Revit version**. The
  Revit 2025+ signature is documented but lacks an authoritative
  reference for behaviour with image parameters. Phase 170 confirms.
- **`FamilyManager.SetFormula` for calculated 7-segment ID** — the
  formula syntax for string concatenation in family parameters is
  `&` (e.g. `$A & "-" & $B`). Reasonably well documented but
  parameter-name escaping (spaces, hyphens) needs validation.
- **Family templates ship at install time** — the path
  (`Annotations/Titleblocks/A1 metric.rft`) is platform-dependent
  + locale-dependent (English UK / English US / Metric Imperial).
  Generator must scan multiple known paths or accept a config
  override.
- **`SaveAs` to a path that exists** — must use
  `SaveAsOptions.OverwriteExistingFile = true`; otherwise
  `Autodesk.Revit.Exceptions.OperationCanceledException`. Pattern
  already used in `TemplateManagerCommands.cs:3261`.
- **Closing the family doc detaches it from the running project**
  — `LoadFamily` takes a load-options instance for handling
  duplicate symbol/parameter conflicts. STING already implements a
  custom `IFamilyLoadOptions` (`TagFamilyLoadOptions` in
  `SmartTagPlacementCommand.cs`). Reuse the pattern.

## 11. Recommendation

**Yes — build this.** Phase 170 is small and de-risks the rest.
Start with one family, prove the pipeline, then crank through the
JSON for the remaining 17. Net effect on the title-block library:
authoring drops from 60 h to ~6 h, sharing across teams becomes
reproducible, and JSON-driven means the same spec authors a future
A1 v3.0 with a single edit + regenerate.

Trigger conditions for the next sprint:
1. Reviewer confirms `app.NewFamilyDocument` works against the
   target Revit version's title-block templates on Windows.
2. The `.rft` template paths get pinned in `STING_TITLE_BLOCKS.json`
   (templatePathOverride per family if needed).
3. Phase 170 (proof-of-concept single family) lands and a designer
   visually validates the output.

Once those land, Phases 171-175 can run in parallel — different
families per discipline / paper size.

## 12. Reference — Revit API entries used

Direct documentation links (Revit 2025):

- `Autodesk.Revit.ApplicationServices.Application.NewFamilyDocument(string)` — open template
- `Autodesk.Revit.DB.FamilyManager.AddParameter(ExternalDefinition, ForgeTypeId, bool)` — add shared param
- `Autodesk.Revit.DB.FamilyManager.AddParameter(string, ForgeTypeId, ForgeTypeId, bool)` — add family-internal param
- `Autodesk.Revit.DB.FamilyManager.SetFormula(FamilyParameter, string)` — calculated param
- `Autodesk.Revit.Creation.FamilyItemFactory.NewDetailCurve(View, Curve)` — line geometry
- `Autodesk.Revit.Creation.FamilyItemFactory.NewLabel(View, XYZ, HorizontalAlign, VerticalAlign, FamilyParameter, string, string, double)` — bound label
- `Autodesk.Revit.DB.TextNote.Create(Document, ElementId, XYZ, string, ElementId)` — static text (works in family docs)
- `Autodesk.Revit.DB.FilledRegion.Create(Document, ElementId, ElementId, IList<CurveLoop>)` — filled region
- `Autodesk.Revit.DB.ImageType.Create(Document, string)` — load logo image
- `Autodesk.Revit.DB.Document.SaveAs(string, SaveAsOptions)` + `Close(bool)` — persist
- `Autodesk.Revit.DB.Document.LoadFamily(Document, IFamilyLoadOptions, out Family)` — reload into project

Existing in-codebase patterns to reference:
- `Tags/TagFamilyCreatorCommand.cs` — overall family-creation pattern, shared-param-file restore, `LoadFamily` with `TagFamilyLoadOptions`
- `Tags/LegendBuilderCommands.cs` — `FilledRegion.Create`, `TextNote.Create`, and `NewDetailCurve` usage
- `MatchLineEngine.cs` — Phase 168 line-style resolution + `OverrideGraphicSettings` pattern
- `Temp/TemplateManagerCommands.cs` — `SaveAs(path, fallbackOpts)` with override handling
- `Commands/TagStudio/MigrateTagFamiliesCommand.cs` — family-doc transaction discipline + `Close(false)` pattern
