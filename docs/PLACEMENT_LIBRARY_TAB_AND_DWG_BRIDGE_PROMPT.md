# Implementation Prompt — Placement Centre "Library" tab + DWG-MEP → seed → swap bridge

> **For the implementing agent.** Make the STING Placement Centre the *fixture-
> lifecycle hub* by surfacing the already-existing seed/symbol/swap commands and
> the DWG-MEP importer, then build the bridge that turns DWG MEP symbols into
> placed, swappable STING instances. **Almost nothing here is new capability —
> the engines exist; the work is wiring + one bridge.** Do NOT fork or duplicate
> command logic.
>
> **Branch:** start from the LATEST `main` (PR #379 — the placement/seed/swap
> foundation — must be merged first). Create `claude/placement-library-dwg`.
> **Build:** `C:\Dev\STINGTOOLS` is a shared checkout that other agents clobber.
> If `build.bat`/`deploy-gold.bat` fails with errors in files you didn't touch,
> build in an isolated worktree at your HEAD (`git worktree add <dir> HEAD`) and
> copy `CompiledPlugin\StingTools.dll` + `data\` to `C:\Dev\STING_PLACEMENT_GOLD\`
> (Revit's addins are pinned there). Verify in Revit before merge — model-modifying.

---

## Foundation you're building on (already in `main` after PR #379)
- **Category→seed map**: `StingTools/Data/Placement/STING_CATEGORY_TO_SEED_MAP.json` + `Core/Placement/CategoryToSeedRegistry.cs` (`Resolve(doc, category) → seedId`).
- **Seed build/rebuild**: `Core/Placement/SeedEnsurer.cs` (`EnsureSeedsForRules`, `RebuildAllForRules`) → `Core/Symbols/SymbolLibraryCreator.CreateAllFromFile`. Composite geometry supported (`Solid3DDefinition.Components`).
- **Swap to real**: `Commands/Symbols/SwapToManufacturerCommand.cs` + `SwapParameterBridge.cs` (non-destructive, params preserved).
- **Placement engine**: `Core/Placement/FixturePlacementEngine.Run(...)` — host-first placement, crowding dedup, orientation.
- **Centre dispatch**: `UI/PlacementCenter/StingPlacementCenter.xaml(.cs)` already has `RunInlineAction(title, Func<UIApplication, StingResultPanel.Builder>)` for model-modifying inline actions and `Report(title, builder)` for read-only reports. **The "Rebuild Seeds" button (Tools tab → `OnRebuildSeeds_Click`) is your exact template.**
- **DWG-MEP importer (exists)**: `Model/CADToModelEngine.cs` (duct/pipe/conduit layer maps + MEP block capture) + `Model/*` commands `MepCadWizardCommand`, `MepCadToModelCommand`, `MepCadPreviewCommand`.

`grep`/read to confirm exact command class names + `[Transaction]` modes + tags before wiring — don't trust this list blindly.

---

## Phase 1 — "Library" tab in the Placement Centre (low-risk wiring)

Add a new tab **Library** to `StingPlacementCenter.xaml` (alongside Rules / Run & Routing / Tools), grouped by lifecycle stage. Each button's `Click` handler dispatches an **existing** command — never re-implement logic.

**Groups + buttons → existing command:**

| Group | Button | Command (confirm class/tag by grep) |
|---|---|---|
| Seeds | Rebuild Seeds *(already wired — keep/move here)* | `SeedEnsurer.RebuildAllForRules` |
| Seeds | Inspect Library | `InspectSymbolLibraryCommand` (`Symbols_Audit`?) |
| Seeds | Coverage Audit | `Symbols_CoverageScan` (SymbolLibraryCommands) |
| Seeds | Heal Orphans | `HealSymbolOrphansCommand` (`Symbols_HealOrphans`) |
| Seeds | Fix Drift | `FixSymbolDriftCommand` (`Symbols_DriftDetect`) |
| Families | Swap to Manufacturer | `SwapToManufacturerCommand` (`Symbols_SwapToManufacturer`) |
| Families | Augment Families | `AugmentProjectFamiliesCommand` (`Symbols_Augment`) |
| Families | Create Symbols… | `CreateLightingSymbolsCommand` / `CreateFPSymbolsCommand` / `CreateSLDSymbols*` (a sub-menu or mode picker) |
| Import | From DWG (MEP)… | `MepCadWizardCommand` (launch its wizard) |

**Dispatch pattern (two cases):**
1. **Read-only / inline-reporting commands** (Inspect, Coverage Audit): if the command exposes a `BuildReportText(doc)`-style static (like `PlacementDiagnoseCommand`), call it and `Report(title, panel)`. Otherwise run via `RunInlineAction` and summarise the result inline.
2. **Model-modifying / interactive commands** (Swap, Heal, Create Symbols, DWG wizard): these own their own `TaskDialog`/wizard UI. Run the actual `IExternalCommand` on the API thread. Prefer a thin generic helper `RunExternalCommand<T>()` that executes the command via the Centre's external event (reuse the existing `_actionEvent`/`PlacementActionHandler`; instantiate `T` and call `Execute`). Do NOT copy the command body.

**Constraints:**
- One command per operation; buttons dispatch, never duplicate.
- Keep it navigable — groups with headers, not a wall of buttons.
- Honour the existing `RunInlineAction` + `Report` patterns; results render in the shared Report panel.
- The full dock-panel catalogue stays the exhaustive surface; the Library tab is the lifecycle subset.

**Acceptance:** the Placement Centre Library tab runs Rebuild Seeds, Swap, Coverage, Heal, Fix Drift, Create Symbols, and launches the DWG-MEP wizard — each invoking the existing command, results inline where applicable. No behavioural change to the underlying commands.

---

## Phase 2 — DWG-MEP → seed → swap bridge (the flagship synthesis)

Turn DWG MEP **fixture symbols** (blocks) into real, parameterised, swappable STING instances by **reusing the foundation**, not a parallel pipeline.

Pipeline:
```
DWG import → MEP blocks (point + layer + block name)
   → map block/layer → STING category   (extend CADToModelEngine's layer map, or a new DWG_SYMBOL_MAP.json)
   → resolve category → seed             (CategoryToSeedRegistry)
   → place the SEED at the block point    (FixturePlacementEngine host-first placement, or a direct NewFamilyInstance via PlacementHostPreflight)
   → (optional) swap seed → real family   (SwapToManufacturerCommand)
```

Implementation notes:
- `CADToModelEngine` already captures MEP blocks with category mapping (lines ~455–463, ~949 layer maps). **Reuse that capture**; add a path that, for *fixture-class* blocks (not duct/pipe runs), emits `(XYZ point, category, blockName)` instead of trying to model a run.
- For each captured fixture point: `seedId = CategoryToSeedRegistry.Resolve(doc, category)`; ensure the seed is built (`SeedEnsurer`); place via `PlacementHostPreflight.Place(doc, symbol, room?, point, syntheticRule)` so it hosts to the nearest wall/ceiling and orients (host-first — same as the engine).
- Map block name → seed **variant** where possible (e.g. a `SOCKET_2G` block → the `SOCKET_2G` variant) via a `DWG_SYMBOL_MAP.json` (blockName/layer → category + variantHint). Fall back to the seed's default variant.
- Stamp provenance (`StingProvenanceSchema`) + the source DWG layer/block on each placed instance for audit.
- Surface it as **Library → From DWG → Place STING fixtures from symbols** (a mode of the wizard), reporting counts inline.
- After placement, the user can run **Swap to Manufacturer** to get real product geometry — the bridge output is swap-ready (it's STING seeds).

**Acceptance:** importing a DWG with MEP fixture symbols places hosted, parameterised STING seed instances at the symbol locations, mapped to the right category/variant, and they swap to manufacturer families via the existing bridge. No duplication of the DWG geometry engine or the placement engine.

---

## House rules
- One branch (`claude/placement-library-dwg`) off the **merged** `main`; no merge back without verification.
- **Dispatch existing commands; never fork their logic.** If a command needs a non-interactive entry point, add a thin public static method to *that command's* class and call it — keep the logic in one place.
- Revit transactions named `STING …`; model-modifying = `[Transaction(Manual)]`; user messages via the inline Report panel / `Toast`, not new modal popups where avoidable.
- Build in an isolated worktree if the shared checkout is broken by a co-agent; deploy DLL + `data\` to `C:\Dev\STING_PLACEMENT_GOLD\`; addins are pinned there.
- Log work in `docs/CHANGELOG.md`; flag the in-Revit verification caveat (model-modifying, untested in the sandbox).

## Suggested order
1. Phase 1 Library tab — wire the read-only/inspect buttons first (safest), then the model-modifying ones via the external-event helper, then the DWG-wizard launcher.
2. Phase 2 bridge — DWG fixture-block capture → category → seed → host-first place → (swap-ready). Verify on a real DWG.
