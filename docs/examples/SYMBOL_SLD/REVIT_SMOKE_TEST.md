# Symbol library and SLD — Revit smoke-test checklist

> **Generated file — do not edit.** Source:
> [`smoke_test.json`](smoke_test.json) · regenerate with
> `python tools/build_smoke_test.py` · gated by `tools/check_smoke_test.py`.

Ordered manual checklist for the symbol library and single line diagram work ported from PR #951 on branch `claude/port-symbol-sld`: SLD symbols resolved through the shared content roots, solid filled regions, line weights by subcategory, the 73 indexed symbols, orientation variants and their audit, the standard switch that now swaps placed instances, the template preflight and the Symbols and SLD workflow. Everything below builds clean and its wiring is machine-checked (`python tools/check_smoke_test.py`), but **none of it has been exercised in Revit**. Walk this list on a test model before relying on it.

Steps marked **pre-cleared offline** have had their contract asserted by the CI gate: the command tag resolves, the button is on the declared panel, tab and section with that label, and the workflow preset contains the step. What the gate cannot prove is anything about geometry: whether a symbol draws, whether a line is heavier, whether the right family is swapped in. That is what this session is for.

## Setup

1. **Deploy this checkout and confirm Revit loads it** — _Revit-native action — no STING command_
   - Expected: The STING dock panel loads with no startup error, and the live manifest points at this checkout's CompiledPlugin folder
   - Note: Close Revit and the Planscape Companion tray app first (both hold StingTools.dll). Run deploy.bat from this checkout; it builds and rewrites the manifest. Then confirm the live path: grep -h "<Assembly>" "$APPDATA/Autodesk/Revit/Addins"/*/StingTools.addin | sort -u. Copying into a folder the manifest does not name fails silently. The log is StingTools_yyyyMMdd.log beside that DLL, dated, not StingTools.log.

2. **Open a saved test model with electrical panels and circuits, some MEP elements, and a DWG import on a level** — _Revit-native action — no STING command_
   - Expected: Model opens; Revit Options > File Locations > Family Template Files points at a folder holding Metric Generic Annotation.rft
   - Depends on: step(s) 1
   - Note: The model must be saved: SLD symbols resolve from the project's _BIM_COORD/Families/Symbols folder, which an unsaved model does not have.

3. **Load Params** — **Load Params** (STING panel · CREATE TAGS · ⚙ SETUP) _(pre-cleared offline)_
   - Expected: Binds without error; the summary dialog reports the binding passes
   - Command tag: `LoadSharedParams`
   - Depends on: step(s) 2

## Symbol library

4. **Preflight with the template folder set** — **Preflight** (STING panel · SETUP · SYMBOLS & DEVICES) _(pre-cleared offline)_
   - Expected: Dialog title reads "Preflight OK: ready to build"; template folder YES with its path; Generic Annotation .rft OK
   - Command tag: `Symbols_Preflight`
   - Depends on: step(s) 2

5. **Preflight and Create All with the template folder cleared** — **★ Create All** (STING panel · SETUP · SYMBOLS & DEVICES) _(pre-cleared offline)_
   - Expected: Temporarily point Family Template Files at an empty folder. Preflight reports "Preflight failed" with the fix; Create All cancels with "Preflight failed: building now would produce 0 families" and builds nothing. Restore the folder afterwards
   - Command tag: `Symbols_CreateAll`
   - Depends on: step(s) 4
   - Note: If ResolveTemplateFolder finds a fallback folder on this machine the failure cannot be provoked this way; say so in the log rather than marking the step passed.

6. **Create All with the template folder restored** — **★ Create All** (STING panel · SETUP · SYMBOLS & DEVICES) _(pre-cleared offline)_
   - Expected: Report shows Created > 0 and Failed 0 or close to it. The 73 families added from the MEP symbols index (sockets, containment, fire alarm devices, switches, HVAC plant, pipe and plumbing accessories, SLD boards and final circuits) exist under _BIM_COORD/Families/Symbols. A "Degraded fills" section appears only if a template had no FilledRegionType; any symbol it lists draws as an outline
   - Command tag: `Symbols_CreateAll`
   - Artefact: `<project root>/_data/_BIM_COORD/Families/Symbols/**/*.rfa`
   - Depends on: step(s) 4
   - Note: Spot-check ELEC_PANEL_BOARD, FP_CALL_POINT, PLM_TRAP_P and SLD_MDB. Expect warnings naming 25 new model symbols with no realSizeMm (for example ELEC_ATS, ELEC_CONTAINMENT_TRAY, PLM_TRAP_P). Those were left unset deliberately rather than given a guessed size, so they build at paper size; the warning is the correct outcome.

7. **Open SLD_MCB.rfa and a fill-only symbol, and check line weights and fills** — _Revit-native action — no STING command_
   - Expected: In the MCB family, Manage > Object Styles shows subcategories Outline (weight 1), Main (4) and Conductor (4); the breaker box is visibly lighter than the diagonal and the stubs. A fill-only symbol renders solid black, not blank
   - Depends on: step(s) 6
   - Note: LTG_SURFACE_SQ is a fill-only symbol. Weights come from Data/Symbols/STING_LINE_WEIGHTS.json; a project override at _BIM_COORD/line_weights.json is merged over it on the next Create All.

8. **Validate the catalogues** — **Validate** (STING panel · SETUP · Create & standards) _(pre-cleared offline)_
   - Expected: Catalogues BAD 0; extent violations and empty geometry do not name any of the 73 new symbols
   - Command tag: `Symbols_Validate`
   - Depends on: step(s) 3
   - Note: The button is in the "Create & standards" expander of SETUP > SYMBOLS & DEVICES.

## Orientation and standards

9. **Orientation audit** — **Orient Audit** (STING panel · SETUP · Create & standards) _(pre-cleared offline)_
   - Expected: Dialog lists the concepts declaring orientation states and the variant families they reference but that are not loaded or in the content library. A non-zero missing count is expected: no vertical or end-on variant family has been authored yet
   - Command tag: `Symbols_OrientationAudit`
   - Depends on: step(s) 6
   - Note: The button is in the "Create & standards" expander of SETUP > SYMBOLS & DEVICES, beside Std Audit.

10. **Place symbol overlays in a section through a vertical pipe or duct** — **Place View** (STING panel · SETUP · Create & standards) _(pre-cleared offline)_
   - Expected: Overlays are placed with the base family; the day's log has one "orientation variant unavailable" line per such host naming the base family used
   - Command tag: `Symbols_PlaceView`
   - Depends on: step(s) 9
   - Note: The button is in the "Create & standards" expander of SETUP > SYMBOLS & DEVICES.

11. **Switch the project standard to one with no built families** — **Std Proj** (STING panel · SETUP · Create & standards) _(pre-cleared offline)_
   - Expected: Guard dialog "No '<standard>' symbol families are built" with "Build the symbol library now" and Cancel. Cancel leaves the project standard unchanged (re-run and check the picker's current value, or Std Audit)
   - Command tag: `Symbols_SwitchProject`
   - Depends on: step(s) 6
   - Note: Which standards are unbuilt depends on which catalogues step 6 produced; pick one Std Audit shows no families for. The button is in the "Create & standards" expander of SETUP > SYMBOLS & DEVICES.

12. **Switch the project standard to a built one with placed symbols in the model** — **Std Proj** (STING panel · SETUP · Create & standards) _(pre-cleared offline)_
   - Expected: Report reads "Switched to <standard>. N tag(s) updated." plus a model-instance line: swapped and skipped counts, or a plain statement that every instance was skipped because its target families are not loaded. Placed symbol instances change family where a target exists; none changes category
   - Command tag: `Symbols_SwitchProject`
   - Depends on: step(s) 10
   - Note: The button is in the "Create & standards" expander of SETUP > SYMBOLS & DEVICES.

## Single line diagram

13. **Generate the SLD from the Electrical panel** — **⚡ Generate** (ELECTRICAL panel · SLD · ) _(pre-cleared offline)_
   - Expected: A drafting view with breaker, board and load symbols drawn, not blank boxes. The log shows "PlaceSymbols: auto-loaded" lines with paths under _BIM_COORD/Families/Symbols and no warning telling you to run Seeds_Build
   - Command tag: `SLD_Generate`
   - Depends on: step(s) 6
   - Note: The Generate button sits at the top of the SLD tab under the SINGLE LINE DIAGRAM / DISTRIBUTION HIERARCHY header, which the checker reads as no section, hence the empty section. Before the port the SLD looked only in <model folder>/_BIM_COORD/symbols, which nothing writes to.

14. **Run the Symbols and SLD workflow** — run **WORKFLOW_SymbolsAndSLD.json** (workflow only — no standalone button) _(pre-cleared offline)_
   - Expected: Four steps run in order: preflight, load parameters, build all catalogues, generate the SLD. With the template folder cleared (as in step 5) the run stops at the first step and rolls back
   - Command tag: `WorkflowPreset`
   - Depends on: step(s) 5
   - Note: Launch from the preset runner ("Run preset") and pick "Symbols and SLD".

## DWG to model

15. **Convert DWG MEP content through the MEP CAD path** — **Convert** (STING panel · MODEL · DWG → MEP (fixtures)) _(pre-cleared offline)_
   - Expected: MEP fixtures and straight runs are placed from the DWG and auto-tagged. This is the only DWG-to-MEP path; DWG → Model does not place MEP
   - Command tag: `Mep_CadToModel`
   - Depends on: step(s) 2
   - Note: PR #951 also added MEP placement inside DWG → Model (P1-3). That was deliberately not ported because this command already exists.

16. **Convert DWG walls and check the wall types chosen** — **DWG → Model** (STING panel · MODEL · DWG TO MODEL) _(pre-cleared offline)_
   - Expected: Walls on a layer whose name matches a wall type (for example a layer named like an existing type) take that type; walls on other layers take the type closest in thickness, as before
   - Command tag: `ModelDWGToModel`
   - Depends on: step(s) 2
   - Note: Matching: exact type name, then a type whose name contains every token of the layer name split on space, hyphen and underscore.

Log any failure with the command, the excerpt from the day's `StingTools_yyyyMMdd.log` (next to the DLL the live `.addin` points at), and the model context.

**This file is generated.** Edit `docs/examples/SYMBOL_SLD/smoke_test.json` and run `python tools/build_smoke_test.py`; `tools/check_smoke_test.py` fails CI if the two disagree. See [`docs/examples/_smoke_test_schema.md`](../_smoke_test_schema.md).
