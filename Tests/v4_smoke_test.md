# STING v4 MVP — manual smoke test checklist

Branch: `claude/sting-tools-v4-mvp-SiPGw`. Run after a successful Revit
build of the plug-in. Estimated time: 30–45 minutes.

## Prerequisites

- Revit 2025 (or 2026/2027) with the v4 build of `StingTools.dll` +
  `StingTools.addin` deployed to `%APPDATA%\Autodesk\Revit\Addins\<year>\`
- `data/` folder shipped alongside the DLL contains:
  - `STING_PLACEMENT_RULES.json`
  - `ROOM_TYPE_CLASSIFIER.csv`, `LUX_TARGETS_EN12464.csv`
  - `STING_FAB_RULES.json`
  - `STING_ISO_SYMBOLS_INDEX.csv`
  - `Parameters/STING_PARAMS_V4.txt`
- A sample BIM project containing rooms + at least one MEP fixture
  (pipe / duct / conduit + tray) and one cable tray run

## 20-step smoke test

1. **Open Revit** and load a sample project containing rooms + MEP
   fixtures. Confirm the STING dock panel opens without errors.
2. **Open the STING dock panel** → switch to the **TAGS** top-tab.
   Confirm `Fabrication`, `Routing`, `Fixtures` sub-tabs appear next
   to the existing `Placement / Leader & Elbow / Style & Color`.
3. **Fixture placement preview** — open the `Fixtures` sub-tab, leave
   `Dry-run preview first` checked, click `Place fixtures`. Choose
   `Preview (dry run)`. Confirm the result panel shows
   "Rooms visited", "Candidates evaluated" and per-rule counts.
   Nothing should be created in the model.
4. **Live fixture placement** — re-run `Place fixtures` and choose
   `Place now`. Confirm the result panel reports a non-zero "Placed"
   count and that the placed instances are selected in the Revit UI.
5. **Validation suite (no model changes)** — confirm the placed
   fixtures have `ASS_PLACE_ANCHOR_TXT`, `ASS_PLACE_OFFSET_X_MM`,
   `ASS_PLACE_SIDE_TXT` populated. Element Properties → STING params.
6. **Routing tab → Auto-drop** — select 1-3 placed fixtures and press
   `Auto-drop`. Confirm the result panel splits findings by
   discipline (Electrical / Plumbing / HVAC) and lists "Created" >= 0.
7. Open one of the new conduits / pipes / ducts and confirm the
   discipline-specific tags are populated:
   - Conduit: `ELC_CDT_INSTALL_METHOD_TXT`, `ELC_CDT_FAB_METHOD_TXT`
   - Pipe:    `PLM_PPE_FAB_METHOD_TXT`, `PLM_PPE_HANGER_TYPE_TXT`
   - Duct:    `HVC_DCT_FAB_METHOD_TXT`, `HVC_DCT_SEAM_TYPE_TXT`,
              `HVC_DCT_MAT_TXT`
8. **Validation_RunAll** — fire `Routing → Validate fills` (the stub
   points users to the validation pipeline). Then run the
   `Validation_RunAll` command tag (use the dock panel's command
   binding or wire a temporary button). Confirm all five validators
   appear in the result panel: Connectivity, Fill, Spec, Termination,
   Slope.
9. **Generate Fabrication Package** — select MEP elements (try a mix
   of conduit + pipe + duct) and run `Fabrication → Generate package`.
   Confirm the result panel shows non-zero "Assemblies" and "Sheets"
   counts. The first generated `SP-...` sheet should open
   automatically.
10. Inspect the generated sheet. Confirm:
    - Sheet number starts with `SP-{DISC}-{SYS}-{LVL}-{SEQ}`
    - Title block populated with `ASS_SPOOL_NR_TXT`, `ASS_FAB_LOC_TXT`,
      `ASS_FAB_STATUS_TXT`, `ASS_BOM_REV_TXT`
    - At least 3 of the 5 viewports placed (Plan / Iso / Elev / 3D),
      depending on the assembly geometry
    - BOM schedule visible on the right-hand strip
11. **CSV sidecars** — confirm CSV outputs land in
    `OutputLocationHelper.GetOutputDirectory()` (typically
    `<project_dir>/STING_BIM_MANAGER/`):
    - `STING_v4_electrical_bends.csv`
    - `STING_v4_pipe_welds.csv`
    - `STING_v4_duct_seams.csv`
12. **Re-export without regenerating** — run
    `Fabrication → Cut list`, `Fabrication → Isometrics`,
    `Fabrication → Weld map`. Confirm each emits its CSV without
    creating new sheets.
13. **ISO symbol resolution** — open
    `STING_ISO_SYMBOLS_INDEX.csv` from the data folder and confirm it
    has 180+ rows. The `IsoSymbolPlacer` will warn once per missing
    family in `StingTools.log`; this is expected when the .rfa
    library is not yet authored.
14. **No regression on existing TAG STUDIO sub-tabs** — switch
    between `Placement / Leader & Elbow / Style & Color / Tokens &
    Depth / Tools / Scale` and confirm each renders correctly. The
    new sub-tabs do not steal real-estate.
15. **Document Manager regression** — open the Document Management
    Center and confirm it still launches normally and the COBie /
    transmittal workflows are unaffected.
16. **BIM Coordination Center regression** — open the BCC and confirm
    all 13 tabs still load. v4 changes are additive, no removal.
17. **Theme switching** — cycle the theme via the dock panel header
    button. Confirm the new sub-tabs honour the theme switch (text +
    background follow the active theme brushes).
18. **Close + reopen the dock panel** — confirm v4 sub-tabs persist.
19. **Close + reopen the project** — confirm placed fixtures, drops,
    and assemblies persist; sheets remain in the project browser.
20. **Build verification** — run `dotnet build StingTools/StingTools.csproj
    --configuration Release` on a Windows machine with Revit 2025
    installed. Expect 0 errors. Treat any error as a failure of the
    runner's "without builds" caveat — open a fix runner against the
    affected section.

## Known limitations to verify

- **TODO-VERIFY-API markers**: search the diff for these comments and
  confirm each one against current Revit API:
    - `Conduit.Create(Document, ElementId, XYZ, XYZ, ElementId)`
    - `Pipe.Create(Document, ElementId, ElementId, ElementId, XYZ, XYZ)`
    - `Duct.Create(Document, ElementId, ElementId, ElementId, XYZ, XYZ)`
    - `AssemblyInstance.Create(Document, IList<ElementId>, ElementId)`
    - ISO 6412 section box transform (S5.5 `CreateSectionAt`)
- **Title block fallback**: if `STING_TB_ASSEMBLY_*.rfa` is not
  loaded into the project, ShopDrawingComposer falls back to the
  first title block found and logs a warning.
- **Lighting grid**: classification + lux lookup work, but the
  grid placement command (S2.10) only computes points; placing the
  luminaires onto those points is part of a future `LightingGridPlacer`.
- **Learn placement**: `LearnPlacementV4Command` is a documented stub
  (S2.8) that prints the planned behaviour; production deployment
  needs the real extractor.

If steps 1–20 pass and the known limitations behave as documented,
the v4 MVP is good for handover validation.
