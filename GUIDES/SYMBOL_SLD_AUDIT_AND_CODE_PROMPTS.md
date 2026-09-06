# STING Symbol / SLD / DWG-to-MEP — Audit + Implementation Prompts

Deep-dive audit of the symbol generation → SLD render → standard-switching →
DWG-to-MEP chain, with a **ready-to-execute implementation prompt per code
work-item**. Each prompt is self-contained: problem, file:line evidence, fix
approach, acceptance criteria, effort/risk. Hand any P-item to a coding agent
(or execute in a follow-up phase) independently.

Audit method: 4 parallel code-reads + direct verification of the two candidate
bugs. Findings are grounded in file:line evidence, not inference.

---

## PART A — Audit summary (what's real vs broken)

| Subsystem | Verdict | Detail |
|---|---|---|
| SLD resolution chain | ✅ Sound design, 🔴 1 blocking bug | Concept→standard-family→FamilyInstance is real and standard-aware; but the disk auto-load points at the wrong folder (P0-1). |
| Symbol geometry engine | ✅ Complete, ⚠️ line-weight gap | lines/arcs/filledRegions all handled; scale correct; but no per-curve line weight (P1-1). |
| Standard switching | ✅ Tags, ⚠️ not model instances | 6-level resolver + `ChangeTypeId` on tags works; placed model symbols not swapped (P2-2). |
| View-type variants | ✅ Works | `viewContextOverrides` (Plan/Section/Schematic) consumed at placement. |
| `orientationStates` | ⚠️ Dead data | Parsed but never read; engine hardcodes instead (P1-2). |
| Symbol coverage | ✅ Standards-grounded | 548 geometries; 62% of 192 indexed auto-covered; 73 gap (P2-3). |
| DWG-to-MEP integration | 🔴 Decoupled | CADToModelEngine discards MEP blocks; no symbol resolution (P1-3). |
| Filled-region robustness | ⚠️ 3 edge symbols | No `FilledRegionType` fallback (P2-1). |

### Direct answers to the questions raised

- **"17 MEP symbols seems not enough."** Correct read, wrong worry. 17 is the
  `Families/MEP/README.md` *priority hand-authoring* list — and 16 of those 17
  are **already JSON-covered**. The real number: **192 indexed symbols, 119
  (62%) auto-generated from JSON, 73 (38%) still need authoring.** Total
  geometries across all catalogues incl. SLD variants: **548**. So coverage is
  broad; the gap is 73 specific items (P2-3 lists them by discipline).
- **"Was there research about the standard symbols?"** Yes — strongly. Every
  catalogue header cites a real published standard and the geometry implements
  it faithfully: IEC 60617 (electrical/SLD), BS 1553 + CIBSE Guide B + ASHRAE
  (HVAC), NFPA 13/170 + BS 5306 + BS 5839 (fire), BS EN 12056 + BS 5572
  (drainage), BS EN 60617 + BS 7671 (elec devices), ISO 6412 + BS 308 (spool),
  ISO 7010:2019 (safety), IGEM TD/4 + BS 6891 (gas), BS EN 50173 + BICSI
  (telecom), CIBSE Guide H + ISA-5.1 + ASHRAE 135/BACnet (BMS). Not ad-hoc.
- **"Switching standards?"** Wired end-to-end **for annotation tags** (IEC /
  IEEE / BS / NFPA / CIBSE) via `Symbols_SwitchProject` / `Symbols_SwitchView`
  / `Symbols_SetProfile` and a 6-level resolver. **Model-placed instances are
  NOT swapped** — only a `STING_SYMBOL_STD` int is stamped (P2-2).
- **"Elements that change symbol by view type?"** Works. `viewContextOverrides`
  in `STING_SYMBOL_CONCEPTS.json` (Plan / Section / Schematic / CeilingPlan…)
  is consumed by `SymbolConceptRegistry.ResolveFromMapping` during placement
  and tag-swap. `orientationStates` is the one dimension that's declared but
  dead (P1-2).
- **"Accuracy + scale of lines?"** Scale is **correct** — coords normalised
  (±0.5..±0.8) × per-symbol `symbolSize` mm; no double-scaling. Line **weight**
  is the gap: every curve plots at the template default (P1-1).
- **"Alignment with DWG-to-MEP / modeler?"** The weakest seam. The DWG
  converter produces architecture only and **discards extracted MEP blocks**;
  it never consults the symbol library (P1-3).

---

## PART B — Implementation prompts (execute independently)

> Convention: mm-in / feet-internal, `Transaction` named `STING …`, `StingLog`
> not silent catch, `TaskDialog` not MessageBox. Build caveat: this machine can
> build (`dotnet build StingTools/StingTools.csproj -p:RevitApiPath="C:\Program
> Files\Autodesk\Revit 2025"`) — compile before claiming done.

---

### 🔴 P0-1 — Fix SLD symbol auto-load path (blocking, small)

**Problem.** After `Symbols_CreateAll` writes SLD families to disk, SLD
generation cannot find them and logs *"family not found — run Seeds_Build"*.
Every SLD symbol silently fails to place unless the family is already loaded in
the document. This reads as "SLD is broken" when the engine is fine.

**Root cause — path divergence.** The generator and the rest of the system
agree on `<project>/_BIM_COORD/Families/Symbols/<group>/` and search it
recursively; SLDGenerator alone uses a bespoke flat folder.

- Generator writes to `_BIM_COORD/Families/Symbols/` — `StingTools/Commands/Symbols/SymbolLibraryCommands.cs:86`
- Canonical recursive search (correct) — `StingTools/Core/Content/ContentResolver.cs:182` (`SearchOption.AllDirectories`) + `ContentRoots.cs:35`; also `Core/Placement/FixturePlacementEngine.cs:1748`
- **Broken loader** — `StingTools/Core/SLD/SLDGenerator.cs:391-393`:
  ```csharp
  string symbolsDir = System.IO.Path.Combine(
      System.IO.Path.GetDirectoryName(doc.PathName),
      "_BIM_COORD", "symbols");          // ← wrong: flat, non-recursive, wrong name
  ```
  Full method: `SLDGenerator.cs:377-424` (`FindOrLoadFamilySymbol`).

**Fix approach.** Replace the bespoke loader with the shared resolution path so
SLD uses the same search as everything else:
1. Keep the fast path (already-loaded `FamilySymbol` by name).
2. On miss, resolve via `ContentRoots.Resolve(doc)` → for each root,
   `Directory.EnumerateFiles(root, symbolName + ".rfa", SearchOption.AllDirectories)`,
   then `doc.LoadFamily`. Prefer reusing `ContentResolver` if a name-based
   overload fits; otherwise mirror `MepSymbolEngine.ResolveFamilySymbol`
   (`Core/Symbols/MepSymbolEngine.cs:681`) which already does the correct
   multi-root + shared-root (`STING_SYMBOL_LIB`) scan.
3. Honour the shared library root (`STING_SYMBOL_LIB` / `sting_symbols.json`)
   so firm-wide builds resolve.
4. Keep the existing `StingLog.Warn` but update the hint to name the real
   folder and the `Symbols_CreateAll` command.

**Acceptance criteria.**
- With families built to `_BIM_COORD/Families/Symbols/SLD/IEC/` and NOT
  pre-loaded, `SLD_Generate` places every resolved concept (SymbolsPlaced ==
  node count with a valid concept).
- Switching standard to IEEE resolves `IEEE_SLD_*` from `SLD/IEEE/`.
- Shared-root build (`STING_SYMBOL_LIB` set) resolves with no per-project copy.
- No regression to the fast path when families are already loaded.

**Effort:** ~1–2 h. **Risk:** low (isolated method). **Test:** small board →
circuits → `Symbols_CreateAll` → `SLD_Generate`; confirm symbols render + log
shows `auto-loaded …/Families/Symbols/SLD/IEC/SLD_MCB.rfa`.

---

### 🟠 P1-1 — Bind curves to graphic subcategories (line-weight accuracy)

**Problem.** All symbol lines plot at the family template's default line weight.
IEC/BS drafting expects weight differentiation (e.g. main conductor vs
construction line). The JSON already declares intent but it's unused.

**Evidence.**
- JSON declares `subcategory` **947×** across catalogues, but no `lineWeight`.
- `DrawLine` creates `NewSymbolicCurve`/`NewModelCurve`/`NewDetailCurve` and
  **never assigns a `GraphicsStyle`** — `Core/Symbols/SymbolLibraryCreator.cs:708-745`.
- No `NewSubcategory` / `GraphicsStyle` / `SetLineWeight` anywhere in the
  creator (grep clean). `SymbolDefinition.Subcategory` is copied between
  variants (`:283`, `:361`) but never materialised in Revit.

**Fix approach.**
1. Extend the symbol/geometry schema with optional `lineWeight` (1–16) and
   reuse existing `subcategory`. Add a small standard default table
   (e.g. IEC: outline=1, main=4, auxiliary=2) keyed by subcategory name.
2. In the creator, before drawing: ensure a family subcategory (GraphicsStyle)
   exists per distinct `subcategory` via `doc.OwnerFamily.Categories` /
   `Document.Settings.Categories.NewSubcategory`, set its projection line weight.
3. In `DrawLine`/`DrawArc`/`DrawFilledRegion`, assign the created curve's
   `.Subcategory`/`GraphicsStyle` (via `LineStyle`/`Subcategory` on the
   symbolic/detail curve) so weight/colour/pattern apply.
4. Keep a safe default when `subcategory` absent (current behaviour).

**Acceptance criteria.** A regenerated SLD_MCB shows heavier main lines vs
lighter outline per the table; subcategories visible/toggleable in VG;
no exceptions on templates lacking the subcategory (auto-created).

**Effort:** ~1 day. **Risk:** medium (per-template graphics-style API quirks;
symbolic-curve subcategory assignment differs from detail curves — test both
GenericAnnotation and DetailItem family kinds).

---

### 🟠 P1-2 — Wire or retire `orientationStates` (dead data-model surface)

**Problem.** `orientationStates` is declared across pipe/HVAC/plumbing concepts
and parsed into the model, but no code reads it; orientation is hardcoded.

**Evidence.**
- Parsed: `SymbolDefinition.cs` `OrientationStates` (~`:541`).
- Never consumed: grep returns only the property + JSON.
- Hardcoded instead: `Core/Symbols/SymbolOrientationEngine.cs:68-79`
  (`GetOrientationStateKey` switch).

**Fix approach (pick one, decide first).**
- **(A) Wire it:** have `SymbolOrientationEngine` / placement resolve the
  per-orientation family variant from `concept.OrientationStates[key]` (falling
  back to the hardcoded key), so vertical-riser vs horizontal-run vs end-on
  symbols become data-driven. Requires the variant families to exist.
- **(B) Retire it:** remove the field + JSON entries to eliminate misleading
  dead data, and document that orientation is engine-fixed.

**Recommendation:** (A) if riser/end-on symbol differentiation matters for the
tester's drawings (it usually does for pipework); else (B).

**Acceptance criteria (A).** A vertical pipe accessory in a plan view resolves
its `*_VERTICAL_VIEW_PLAN` variant where defined; falls back cleanly otherwise.

**Effort:** (A) ~0.5–1 day, (B) ~1 h. **Risk:** low.

---

### 🟠 P1-3 — Integrate DWG-to-MEP with the symbol/placement library (biggest value)

**Problem.** Running DWG-to-BIM on an MEP drawing yields **walls/floors/rooms
only**. Extracted MEP blocks (ducts/pipes/panels) are read then discarded; the
converter never consults the symbol library or placement engine.

**Evidence.**
- Converter does walls/floors/rooms only — `Model/CADToModelEngine.cs:256-362`.
- Blocks extracted then unused — `CADToModelEngine.cs:455-486` (comment: "so
  the MepDetectionEngine can classify them" — but main path never does).
- `LayerMapper` → Revit category string only — `CADToModelEngine.cs:28-106`.
- Wall type = first-available-by-thickness, not symbol-aware —
  `Model/ModelEngine.cs:152-189` (`ResolveWallType`, null keyword → `types[0]`).
- Zero references from `Model/` to `SymbolConceptRegistry` /
  `SymbolLibraryCreator` / `STING_MEP_SYMBOLS_INDEX` (grep clean).
- A separate, non-wired MEP path exists — `Model/MepCadCommands.cs`
  (`MepDetectionEngine` + `MepFixtureBuilder`).
- Placement engine's null→skip confirmed — `Core/Placement/FixturePlacementEngine.cs:992-1013`.

**Fix approach (staged).**
1. **Seam first (quick win):** in `CADToModelEngine.ConvertImportToElements`,
   after walls/floors/rooms, add Step 5 — for each `DetectedBlock` with an MEP
   category, resolve a concept via `SymbolConceptRegistry` (by block-name → code
   using `STING_MEP_SYMBOLS_INDEX` + `STING_SYMBOL_ALIASES`) and place through
   the existing `MepFixtureBuilder` / `FixturePlacementEngine` path (reuse, do
   not fork). Carry rotation from the block transform.
2. **Unify:** converge `CADToModelEngine` and `MepCadCommands`/`MepDetectionEngine`
   so there is one DWG→MEP pipeline, not two. Prefer routing the converter's
   blocks into `MepDetectionResult` → `MepFixtureBuilder.Place`.
3. **Type intelligence:** replace null-keyword `ResolveWallType` calls with a
   symbol/registry-aware resolve where a STING code is known (still fall back to
   thickness).
4. **Tag continuity:** ensure placed MEP carries the same ISO 19650 auto-tag
   pass already applied to walls (`CADToModelEngine.cs:334-352`).

**Acceptance criteria.** Import a DWG containing duct + panel blocks on
recognised layers → run converter → ducts/panels placed as STING-resolved
families (or logged with the precise no-symbol reason), rotated correctly, and
auto-tagged. No silent block discard.

**Effort:** stage 1 ~1–2 days; full unify ~1 week. **Risk:** medium-high
(two pipelines; symbol availability). **Note:** gate on P0-1 + P2-3 so resolved
families actually exist to place.

---

### 🟡 P2-1 — FilledRegionType fallback (3 edge symbols)

**Problem.** If a family template lacks a `FilledRegionType`, filled regions are
skipped — 3 symbols that are *fill-only* render blank
(`LTG_SURFACE_SQ`, `STR_BEAM_SECT`, `STR_COLUMN_SECT`); ~148 others merely lose
a fill accent.

**Evidence.** `Core/Symbols/SymbolLibraryCreator.cs:894-901` warns *"no
FilledRegionType in template"* and returns instead of creating one.

**Fix approach.** When no `FilledRegionType` exists, duplicate the default and
create a solid-black one, then proceed. Cache per family doc.

**Acceptance criteria.** On a template with no filled-region type, the 3
fill-only symbols still render solid.

**Effort:** ~1–2 h. **Risk:** low.

---

### 🟡 P2-2 — Decide/implement model-instance standard swap

**Problem.** Switching the project standard swaps annotation **tags** but not
placed **model FamilyInstances** — those only receive a `STING_SYMBOL_STD` int.
So a plan full of placed symbols does not restyle on standard change.

**Evidence.** `Commands/Symbols/SymbolStandardCommands.cs:56-182` (`SwapAllTags`
= `IndependentTag.ChangeTypeId`); model branch `:107-120` sets param only.

**Decision needed.** Is symbol-instance restyle in scope, or are standards
expressed only through tags/SLD? If in scope:
- Extend the swap to `FamilyInstance`s carrying `STING_SYMBOL_ID`: resolve the
  new family per active standard + view context (reuse
  `SymbolConceptRegistry.GetFamilyName` with `viewCtx`/`scaleTier`, exactly as
  the tag path does) and `ChangeTypeId`.

**Acceptance criteria.** IEC→IEEE on a view swaps both tags and placed model
symbols to the IEEE family; count reported.

**Effort:** ~0.5 day. **Risk:** medium (instance ↔ family-name mapping must be
robust; skip instances with no resolvable target rather than corrupt them).

---

### 🟡 P2-3 — Close the 73-symbol coverage gap (content, not engine)

**Problem.** 73 of 192 indexed MEP symbols have no geometry (38%). Concentrated
in equipment + niche items.

**Evidence.** `Data/MEP/STING_MEP_SYMBOLS_INDEX.csv` = 192 rows; 119 JSON-backed.
Per-discipline: Mechanical 73%, Electrical 59%, Plumbing 57%, Fire 56%.

**Priority gaps by discipline (author JSON geometry, standards-cited):**
- **Mechanical:** boiler, chiller, cooling tower, heat exchanger, expansion
  vessel, base-mounted pump.
- **Electrical:** containment (tray/ladder/trunking), isolation switch,
  transformer, generator, UPS, ATS, motor starter, dimmer/occupancy sensor,
  data/telephone socket.
- **Plumbing:** CWS/HWS cistern, floor drain, P/S trap, rodding/access eye,
  mixer valve, hose union.
- **Fire:** FACP control panel, alarm valve (wet/dry), landing valve, riser
  inlet, pre-action valve, fire pump, sprinkler control set.

**Fix approach.** Author each as a JSON entry (lines/arcs/filledRegions,
normalised coords, `subcategory` + P1-1 `lineWeight`, cite the standard in the
catalogue header pattern). Regenerate via `Symbols_CreateAll`. Complex equipment
outlines can stay on the manual `.rfa` list where JSON geometry is impractical.

**Acceptance criteria.** Indexed coverage ≥ 90%; each new symbol names its
standard; `Symbols_CreateAll` reports 0 empty catalogues.

**Effort:** ~2–4 days (batchable). **Risk:** low (data only). **Note:** unblocks
P1-3 (need symbols to place from DWG).

---

## PART C — Suggested execution order

1. **P0-1** (SLD path) — unblocks all SLD testing; 1–2 h. Do first.
2. **P2-1** (filled-region) — trivial, removes blank-symbol edge case.
3. **P1-1** (line weight) — makes output standard-faithful.
4. **P2-3** (coverage) — batch content; unblocks DWG placement.
5. **P1-2** (orientation) — decide wire-vs-retire.
6. **P2-2** (instance swap) — decide scope.
7. **P1-3** (DWG↔MEP) — largest; do after symbols resolve reliably.

Each is independent except the noted gates (P1-3 after P0-1 + P2-3).
