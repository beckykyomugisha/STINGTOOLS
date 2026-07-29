# BOQ Subsystem — Review Findings & Implementation Prompt

**Branch:** `claude/boq-review-and-hardening`
**Date:** 2026-06-26 (revised — added Part B external-alignment audit)
**Scope (Part A — internal):** `StingTools/BOQ/**`, `StingTools/Core/DesignOptions/OptionCostCarbonCalculator.cs`, `StingTools/V6/CarbonStageTracker.cs`, `StingTools/Model/StructuralDesignSuite.cs` (carbon/rebar), `StingTools/Data/{cost_rates_5d.csv,MATERIAL_LOOKUP.csv}`, `StingTools/Core/WorkflowEngine.cs`, `Planscape.Server` BOQ ingest.
**Scope (Part B — interoperability):** `StingTools/BIMManager/SpeckleLinkCommands.cs`, `StingTools/ExLink/FohlioLink.cs`, `StingTools/Commands/Twin/NiagaraCommands.cs`, `StingTools/Core/Twin/TwinReadback.cs`, `Planscape.Edge/Adapters/StubAdapters.cs`, `StingTools/BIMManager/SchedulingCommands.cs` + `Planscape.Server` `P6Controller`, `StingTools/BOQ/IfcQuantitySetWriter.cs`, `StingTools/ExLink/AutomationEngine.cs`, `StingTools/Commands/Mep/ExportPfvIfcCommand.cs`, `Planscape.Server/.../IfcBoqExtractor.cs`, `shared/ifc/**`.

This document is the agent brief.
- **Part A** is a four-axis internal-correctness audit (quantity extraction,
  material ratios/formulas, rate/cost aggregation, internal integration gaps).
- **Part B** is a cross-platform alignment audit of how StingTools' BOQ / 4D /
  5D / carbon / FF&E / digital-twin surfaces line up with the tools they must
  round-trip with: **Fohlio**, **RIB iTWO / CostX**, **Speckle**, **Niagara**,
  and the open **4D/5D/carbon/FM** exchange standards. Each Part B item cites the
  target standard and the verified current-state line.

**Every "VERIFIED" finding was confirmed against source at the cited line.**
Findings marked "CONFIRM" need a quick re-read before fixing because the exact
line may have shifted.

---

## How to use this brief

1. **Part A (P0→P3) first, then Part B (INT-*).** Part A protects the integrity of
   every issued bill — a perfectly-integrated BOQ that is internally wrong just
   exports wrong numbers faster. In particular, **P0-1 (IFC Qtos = 0) is a hard
   prerequisite for most of Part B** — every external tool (RIB, server extractor,
   Niagara/COBie) reads those quantity sets.
2. For each item: re-read the cited code, confirm the diagnosis, implement the
   fix, and add the stated verification.
3. **Do not** introduce a fifth cost/carbon engine, a second Speckle client, or a
   parallel IFC exporter. The strategic direction is *consolidation onto one
   per-element costing API* (Part A P0-7) and *deepening the existing shallow
   connectors* (Part B) rather than adding new ones.
4. This codebase is built in a Linux sandbox without the Revit API, so it is
   **not** compile-verified here. Every Revit API call must use a documented
   signature; add `// TODO-VERIFY-API` only where genuinely uncertain. Note the
   no-build caveat in the commit message and CHANGELOG, per repo convention.
5. Keep changes targeted. One logical fix per commit where practical.
6. Several Part B items are **scoping/commercial** (e.g. Fohlio has no public API,
   Speckle needs an SDK dependency decision) — these are flagged DECISION and
   should produce a recommendation + minimal scaffold, not a speculative full build.

---

## P0 — Critical: silently-wrong totals, hangs, or non-determinism

### P0-1 — IFC quantity sets are always written as ZERO (unit glyph mismatch) — VERIFIED
**`StingTools/BOQ/IfcQuantitySetWriter.cs:73-79`**
```csharp
StampQuantity(el, qtoSetName, "GrossArea",  item.Unit == "m²" ? item.Quantity : 0);
StampQuantity(el, qtoSetName, "GrossVolume", item.Unit == "m³" ? item.Quantity : 0);
StampQuantity(el, qtoSetName, "Length",      item.Unit == "m"  ? item.Quantity : 0);
```
The comparison uses Unicode glyphs `m²`/`m³`, but actual `item.Unit` values are
ASCII `m2`/`m3` (confirmed: `cost_rates_5d.csv` header/rows use `m2`/`m3`, and
`BOQCostManager.NormaliseUnit` (`BOQCostManager.cs:325-333`) canonicalises to
`"m2"`/`"m3"`). `StampQuantity` early-returns on `value <= 0`
(`IfcQuantitySetWriter.cs:~167`), so **every** GrossArea/NetArea/GrossVolume/
NetVolume/Length Qto is silently skipped. External cost tools (CostX, CostOS,
Candy) reading the exported IFC get empty quantity sets.
**Fix:** normalise both sides through `NormaliseUnit` (make it `internal`/shared)
or compare against `{ "m2", "m²" }` / `{ "m3", "m³" }` sets before stamping.
**Verify:** unit test or manual trace that an `m2` line stamps a non-zero
`GrossArea`. Also stamp `Count` for `each` and `Weight`/`NetWeight` for `kg`.

### P0-2 — Snapshot checksum is non-deterministic (orders by random GUID) — VERIFIED
**`StingTools/BOQ/Sync/BoqSnapshotHasher.cs:95`** orders items by `i.Id`, but
**`StingTools/BOQ/BOQModels.cs:90`** sets `Id = Guid.NewGuid().ToString("N")`
fresh on every build. Two BOQs from an identical, unchanged model produce
different orderings → different SHA-256. The hasher's own docstring promises
idempotent dedup ("same inputs → same checksum so the server detects duplicate
pushes"); that invariant is broken. Every snapshot push looks "new" to
`BoqSyncCoordinator`/Planscape.Server.
**Fix:** order the projection by a stable key — `UniqueId`, then
`RevitElementId`, then `BOQLineRef` — and exclude the random `Id` from the
hashed projection (line 98 includes `id = i.Id`).
**Verify:** hash the same `BOQDocument` twice → identical checksum.

### P0-3 — Unmatched rate produces an invisible £0/UGX0 line folded into the grand total — VERIFIED
**`BOQCostManager.cs:201, 290-294`** — when no provider matches, the line is
created with `RateUGX = 0`, `RateSource = "None"`, `RateConfidence = 20`.
`BOQModels.cs:161-163`: `SubtotalUGX => AllItems.Sum(i => i.TotalUGX)` adds the
zero, and `GrandTotalUGX` neither excludes nor warns. A model with 30% unmatched
categories shows a confident grand total that is materially low; the only signal
is a per-row confidence buried in the data.
**Fix:**
- Add document-level rollup: count and **uncosted quantity value at risk** for
  all `RateSource == "None"` / `RateUGX <= 0` rows (excluding legitimately-zero
  categories like Rooms, `cost_rates_5d.csv:28`).
- Surface this in `BOQHealthScore`, the export summary, and **block or hard-warn**
  on tender/professional export when zero-rate priced rows exist.
- Distinguish "genuinely free" from "no rate found" (e.g. a `ZeroRateReason`
  flag) so they are not indistinguishable.
**Verify:** a model with a deliberately-unpriced category shows the at-risk
count and a blocked/warned export.

### P0-4 — `RateConfidence` is computed but never gates totals or export — VERIFIED
**`BOQModels.cs:182-183`, `BOQCostManager.cs:1636-1637`** — `AverageRateConfidence`
feeds only the advisory health score (≤20 of 100 pts). `GrandTotalUGX` is identical
whether every rate is a confidence-100 override or confidence-20 default. Low-/zero-
confidence rates flow into the issued tender with no gate.
**Fix:** thread a configurable `MinRateConfidenceForExport` (default e.g. 60) into
the export/tender path; require explicit acknowledgement below it. Reuse the P0-3
rollup surface.

### P0-5 — BCIS HTTP rate provider blocks the Revit UI thread; `RequiresNetwork` guard never honoured — VERIFIED (CONFIRM line)
**`BcisHttpRateProvider.cs:85-87`** does `_http.GetAsync(url).GetAwaiter().GetResult()`
synchronously, called from `RateProviderRegistry.Resolve` (`RateProviderRegistry.cs:~122`)
inside `BuildBOQDocument`/`CostStamp` on the Revit API thread, inside a transaction.
With an 8s per-request timeout and one round-trip per unique `category|unit|location`
cache key, a 40-category model on a cold cache + slow endpoint = up to ~5 minutes of
frozen Revit, plus a sync-over-async deadlock risk. The `IRateProvider.RequiresNetwork`
flag (`IRateProvider.cs:~112`) exists and is documented as "registry skips network
providers when offline" but `RateProviderRegistry.Resolve` never checks it.
**Fix:**
- Honour `RequiresNetwork`: skip network providers on the synchronous per-element
  path entirely.
- Move BCIS to an **off-thread pre-warm pass** that populates the cache before the
  synchronous build, with a bounded total time budget and cancellation.
- Never call `.GetAwaiter().GetResult()` on the UI thread.
**Verify:** with the BCIS endpoint unreachable, a build completes promptly (no
multi-second stall).

### P0-6 — Currency defaults to GBP/USD and silently FX-multiplies → order-of-magnitude errors — VERIFIED (CONFIRM lines)
**`RateProviderRegistry.cs:44-45, 162-196`; `RateProviders.cs:~111`;
`ProjectRateCardProvider.cs:~63`; `BcisHttpRateProvider.cs:~94`;
`MaterialLibraryRateProvider.cs:55`.**
- ES override, project rate card, and BCIS all **default a missing currency to
  `"GBP"`**, then `ConvertCurrency` multiplies by `UGX_PER_GBP` (~4700). A UGX
  rate-card row that omits the currency field becomes ~4700× too large.
- `MaterialLibraryRateProvider` tags `ALL_MODEL_COST` as `"USD"` (`:55`) with a
  comment admitting it is actually project-currency — if the project is UGX, every
  material-library rate is ×3700.
- FX rates come from `TagConfig.GetConfigDouble` defaults `3700`/`4700` baked in
  (`BOQCostManager.cs:80, 207, 273-274`) with no as-of date and no validation they
  are set.
**Fix:** make currency **mandatory** on every rate source (no silent GBP/USD
default — fail loudly or treat as project currency, decided explicitly). Validate
FX presence/age. Resolve the `ALL_MODEL_COST` currency convention against how Revit
actually stores project cost.

### P0-7 — Three (four) parallel cost/carbon engines that don't share code and disagree — VERIFIED
The same elements are priced/carboned by mutually-inconsistent engines:
- `BOQCostManager.BuildBOQDocument` — the "real" engine (rate-provider chain +
  `CarbonFactorResolver` + `TakeoffRule`).
- `Core/DesignOptions/OptionCostCarbonCalculator.cs:36` — full reimplementation:
  own CSV loader reading **USD `cells[3]`** (`:59`) vs the BOQ engine reading
  **UGX `cols[4]`** (`BOQCostManager.cs:1748`) → ~3700× divergence; own hardcoded
  per-category carbon dict (`:82-103`).
- `Temp/DataPipelineCommands.cs:~1017` legacy `BOQExportCommand` — third engine,
  own `DiscMap` + `BOQ_TEMPLATE.csv` + `BOQDescriptionEngine`.
- `V6/CarbonStageTracker.EstimateA1A3` (`CarbonStageTracker.cs:138-143`) — flat
  **350 kgCO₂e/m³** proxy for everything; writes `CBN_A1_A3_KG_CO2E` while the BOQ
  writes `CST_EMBODIED_CARBON_KG` and a third reader uses `CBN_EMBODIED_KG_CO2E`.
- `Model/StructuralDesignSuite.cs` `EmbodiedCarbonCalculator` — yet another carbon
  convention (see P1-2).
**Fix (strategic):** extract a single per-element costing/carbon API from
`BOQCostManager` (e.g. `BuildLineItemFromElement` made callable with a scoped
collector). Have `OptionCostCarbonCalculator` and `CarbonStageTracker.EstimateA1A3`
delegate to it. Converge carbon onto `CarbonFactorResolver` (the most correct,
unit-aware engine) and one stored parameter. This single change kills the USD/UGX
column bug, the 3-engine carbon drift, and the design-option fork.

### P0-8 — `WorkflowEngine` `"BOQExport"` runs the LEGACY engine; dock button runs the new one — VERIFIED
**`Core/WorkflowEngine.cs:1589`** `case "BOQExport": return new Temp.BOQExportCommand();`
(legacy), while **`StingCommandHandler.cs:3498`** maps `"BOQExport"` →
`BOQ.BOQExportCommand` (new). Every workflow preset step using `"BOQExport"`
(`WorkflowEngine.cs:2440, 2512, 2593, 2766` — ProjectKickoff, data-drops, etc.)
silently runs the **old** exporter with different rates/descriptions/DiscMap.
**Fix:** point `WorkflowEngine.ResolveCommand("BOQExport")` at
`BOQ.BOQExportCommand`. Keep legacy reachable as `"BOQExportLegacy"` (already wired
at `StingCommandHandler.cs:1434`).

---

## P1 — High: accuracy errors in quantity/material derivation

### P1-1 — No per-material split; `GetMaterialIds().First()` arbitrarily picks one material — VERIFIED
**`BOQCostManager.cs:1984-1997` (`GetPrimaryMaterialName`), `BOQByMaterialView.cs:124-128`.**
Compound/layered walls and floors contribute their **entire** quantity to one
material (first id in a non-deterministically-ordered set); other materials vanish
from the by-material BOQ, and the "primary material" can flip between sessions,
changing density and carbon factor. `Material.GetMaterialArea(matId)` /
`GetMaterialVolume(matId)` (the API for true per-material split) is **never called**
anywhere in BOQ.
**Fix:** split quantities per material via `Element.GetMaterialIds` +
`GetMaterialVolume`/`GetMaterialArea`. Cost/carbon each material slice separately.
This is the single biggest *accuracy* improvement after the P0 set.

### P1-2 — Rebar carbon/cost double-counting across engines & rates — VERIFIED
Multiple inconsistent rebar conventions:
- BOQ per-m³ carbon factors in `MATERIAL_LOOKUP.csv` (`CARBON_KG_PER_M3` rows) are
  labelled **reinforced** concrete (RC factors already include steel).
- `Model/StructuralDesignSuite.cs:590-594` applies a **plain**-concrete factor then
  **adds rebar separately** (`volume*100*CarbonFactors["rebar"]`) → ~+200 kgCO₂e/m³
  double-count relative to the RC convention.
- `cost_rates_5d.csv:7` columns rate is an **all-in** m³ rate with "160 kg/m³ rebar +
  formwork" baked in; if `AutoRebarEstimator` (160 kg/m³) or a separate rebar BOQ
  line also prices rebar, columns are double-charged. NRM2 requires concrete,
  reinforcement and formwork measured **separately** — the all-in "per NRM2" rate is
  mislabelled.
**Fix:** choose ONE convention (recommend: plain-concrete factor + explicit separate
rebar line, NRM2-compliant). Relabel/re-value the CSV factors and the all-in column
rates accordingly. Make sure exactly one place prices rebar.

### P1-3 — `1.0` placeholder quantity masquerades as a real measurement — VERIFIED
**`BOQCostManager.cs:428, 436, 444, 452, 454, 457` (legacy) and
`TakeoffRule.cs` `EvaluateQuantity` (returns `1.0` on any missing param/exception).**
When a measured parameter is absent or `!HasValue` (e.g. a zero-area wall, an
in-place family with no `HOST_AREA_COMPUTED`), the code returns `1.0` and the element
is costed as 1 m²/m³/m at the measured rate — a garbage non-zero quantity that looks
legitimate. No confidence penalty, no warning row.
**Fix:** return `0` (or sentinel) and route to the P0-3 "could not measure"
rollup with lowered confidence — never a silent `1.0`.

### P1-4 — Element collection ignores design options → double-count — VERIFIED
**`BOQCostManager.cs:1829-1860` (`CollectCandidateElements`)** runs a project-wide
`FilteredElementCollector` with **no `ElementDesignOptionFilter`**. A model with N
design-option sets bills every alternative element (primary + all alternates). The
quantity, cost, and carbon are all inflated. `BOQBccBridge.GetBoq` caches by
`PathName` only, so switching active option doesn't invalidate.
**Fix:** restrict to the primary/active option:
`el.DesignOption == null || el.DesignOption.Id == activeOptionId`. Make scope
configurable. Coordinate with P0-7 so `OptionCostCarbonCalculator` is the only
option-scoped path.

### P1-5 — BOQ ignores ISO 19650 discipline/system tags the rest of the plugin populates — VERIFIED
**`BOQCostManager.cs:1910` (`DisciplineForCategory`)** derives discipline purely from
`TagConfig.DiscMap[catName]`; it never reads `ParamRegistry.DISC`
(`ASS_DISCIPLINE_COD_TXT`). `ASS_SYSTEM_TYPE_TXT` is read **nowhere** in BOQ. A duct
the user re-disciplined to `M`, or a pipe whose SYS the user set to `HWS`, is
classified from the category default. This also breaks `TakeoffRule.Match(cat, disc,
prod)` (`BOQCostManager.cs:365`) — discipline-specific takeoff/NRM2 rules miss for
re-disciplined elements.
**Fix:** in the per-element path, prefer
`ParameterHelpers.GetString(el, ParamRegistry.DISC)` (fall back to
`DisciplineForCategory`), and feed `ASS_SYSTEM_TYPE_TXT` into NRM2-section and
takeoff-rule matching. (Already consumes `PROD` at `:281` — mirror that.)

### P1-6 — Carbon volume for m²/m elements is fabricated from guessed thicknesses/cross-sections — VERIFIED
**`BOQCostManager.cs:561-575` (area×guessed-thickness), `:568-572` (paint 0.15mm /
plaster 12.5mm / default 10mm), `:580-582` (default linear cross-section 1000 mm²),
`ReadLayerThicknessMm` `:620-633` (reads one total `Thickness`/`Width`).** Carbon for
surface/linear-priced elements multiplies net area by a single guessed thickness or a
default Ø35.7 mm cross-section, then by the *primary* material's per-m³ factor — an
essentially fabricated number for compound assemblies, cable trays, large ducts.
**Fix:** once P1-1 lands (real per-material volumes), drive carbon from actual
material volume. Where a true volume genuinely can't be had, label the row
`carbon = estimated` in output rather than presenting it as authoritative.

### P1-7 — `MaterialLibraryRateProvider` (priority 95) basis mismatch hijacks each-priced families — VERIFIED (CONFIRM)
**`MaterialLibraryRateProvider.cs:56, 74`** returns `Unit = req.Unit` (category hint)
over a value derived from `ALL_MODEL_COST` (a per-area material cost). At priority 95
it outranks the CSV category rate (90), so a curated material cost can hijack an
"each"-priced family and multiply a per-m² cost by quantity = 1 — wrong exactly where
a QS thinks curation improved accuracy.
**Fix:** only let material-library rates win for elements whose measured unit matches
the material-cost basis (m²/m³); otherwise fall through to the category/PROD rate.

---

## P2 — Medium: correctness, consistency, mapping

### P2-1 — VAT lives only in the professional exporter; `GrandTotalUGX` is ex-VAT — VERIFIED
`BOQTenderConfig.VatPct` (`:76`) is applied only in `BOQProfessionalExportCommand.cs:~1537`.
`BOQModels.cs:162-163` `GrandTotalUGX` excludes VAT and excludes any `BOQTenderConfig`
read. Everything reading `boq.GrandTotalUGX` (BccBridge agenda, budget variance
write-back `BOQCostManager.cs:872`, dashboard) reports ex-VAT while the tender PDF
shows inc-VAT — two "grand totals" that disagree by 18%.
**Fix:** single source of truth for whether the grand total includes VAT; make
`BuildBOQDocument` consult `BOQTenderConfig` (currency + VAT + markups). Ensure
`BudgetVariance` compares like-for-like.

### P2-2 — Markup model inconsistent: document-level additive vs rate-level compounded — VERIFIED
`BOQModels.cs:162-163` applies prelims+contingency+OH&P as a flat parallel sum
`Subtotal*(1+pre+con+oh)`, while `RateProviders.cs:93-96` compounds OH and Profit
multiplicatively on the rate. If both are configured, OH&P double-counts;
additive-vs-compounded also disagrees by several percent. Contingency conventionally
applies *after* prelims+OH&P.
**Fix:** define ONE markup model, document each component's base, and ensure rate-level
and document-level markups don't both fire.

### P2-3 — Snapshot list recompute hardcodes contingency/OH defaults — VERIFIED (CONFIRM)
`BOQCostManager.cs:1146-1149` recomputes a snapshot's grand total with hardcoded
`con = 10.0, oh = 8.0` (only `PrelimPct` is re-read, and as a *string* against a JSON
number → often null → stays at 12). Snapshots saved with other percentages show a
wrong total in the list/dashboard.
**Fix:** read all three percentages as numbers from the snapshot JSON.

### P2-4 — CSV Category and MAT_CODE share one flat dict, last-write-wins corruption — VERIFIED (CONFIRM)
`BOQCostManager.cs:1717-1720` inserts both `cols[0]` (Category) and `cols[1]`
(MAT_CODE) into the **same** dictionary. In `cost_rates_5d.csv`, "Air Terminals"
appears twice (ATU/M and LAT/E) → the second silently overwrites the first; any
MAT_CODE colliding with a Category name corrupts both.
**Fix:** separate Category and MAT_CODE lookup dictionaries (the `RateRequest` already
distinguishes them); detect + warn on duplicate keys at load.

### P2-5 — Hardcoded conversion factors instead of `UnitUtils` (consistency + precision) — VERIFIED
`BOQCostManager.cs:417, 425, 429, 433, 676, 697` and `TakeoffRule.cs:189-191` use magic
numbers (`0.092903`, `0.0283168`, `0.3048`) while `UnitUtils.ConvertFromInternalUnits`
is used elsewhere in the *same file* (`:628, :643, :651-652`). Inconsistent and slightly
imprecise.
**Fix:** replace all with `UnitUtils.ConvertFromInternalUnits(v, UnitTypeId.SquareMeters
/ CubicMeters / Meters)`.

### P2-6 — Per-element project-wide Material collector → O(elements × materials) — VERIFIED (CONFIRM)
`BOQCostManager.cs:471-473` (`ResolveNrm2Paragraph`) runs
`new FilteredElementCollector(doc).OfClass(typeof(Material))` **per element** to find a
material by name; same in `BOQByMaterialView.ResolveMaterialClass` (`:139-141`).
~O(5000×500) on a mid-size model per build.
**Fix:** build one `Dictionary<string, Material>` once per build and pass it down.
Also cache per-element `get_Geometry` carbon volume by element type (P1-6 path,
`BOQCostManager.cs:689-716` runs per instance with no per-type cache).

### P2-7 — MeasurementStandard strategy is decorative; not wired into the real build — VERIFIED
`BuildBOQDocument` hardcodes NRM2 inline; `BOQDocument.MeasurementStandardId` is never
consulted by the build or the two real exporters. `MeasurementStandardRegistry.
ClassifyRow/PreferredUnit/ApplyDeductions` only run in standalone preview commands. So
choosing CESMM4/ICMS3/POMI changes a preview but not the exported BOQ; CESMM4 opening
deductions (`EstimateLargeOpeningsM2` returns 0, `MeasurementStandards.cs:137`) never
run.
**Fix:** route `BuildBOQDocument` section/unit/deduction derivation through
`MeasurementStandardRegistry.Get(boq.MeasurementStandardId)`.

### P2-8 — Description edits don't round-trip (import writes wrong param) — VERIFIED (CONFIRM)
`BOQImportCommand` writes the imported Note to `ASS_DESCRIPTION_TXT`
(`BOQSupportCommands.cs:~298`) but `BuildBOQDocument` reads the description from
`ASS_NRM2_PARA_TXT` (`ResolveNrm2Paragraph`, `BOQCostManager.cs:457`). A QS who edits a
description in the workbook and re-imports loses it on next refresh. (Rate edits *do*
round-trip via `CST_UNIT_RATE_UGX`.)
**Fix:** import writes `ASS_NRM2_PARA_TXT` (or `BuildBOQDocument` prefers
`ASS_DESCRIPTION_TXT` when present).

### P2-9 — Phase filtering only excludes demolished, not "Existing-to-remain" — VERIFIED
`BOQCostManager.cs:1779-1792` (`IsPhaseDemolished`) only checks `PHASE_DEMOLISHED`.
Renovation models cost retained existing structure as new work.
**Fix:** add a configurable "costing phase" notion; optionally exclude elements created
in an "Existing" phase from new-build bills.

### P2-10 — Server runs a fourth independent IFC takeoff with no reconciliation — VERIFIED
`Planscape.Server/.../IfcBoqExtractor.cs` regex-parses IFC quantity sets and seeds
`BoqSnapshot` server-side, separate from the plugin-pushed lines, with no reconciliation
(different quantities, no NRM2 section, no carbon, no waste). Combined with P0-1 (IFC
Qtos are zero), the server seed is currently garbage.
**Fix:** reconcile or clearly separate the IFC-seeded baseline from pushed snapshots;
fixing P0-1 is a prerequisite.

---

## P3 — Low: cosmetic, hardening, dead code

- **P3-1** `AssignBoqLineRefs` hardcodes middle segment `sectionIndex = "1"`
  (`BOQCostManager.cs:1856-1868`) → refs `{NRM2}.1.{row}` collapse sub-section
  grouping; since snapshot diffing keys on `BOQLineRef` (`:1280`), two sections sharing
  an NRM2 number but different discipline can collide. Make the ref genuinely unique.
- **P3-2** `MapProviderIdToLegacySource` (`BOQCostManager.cs:332-343` and the duplicate
  in `CostStamp.cs:219-230`) doesn't map `material-library` / `project-rate-card` /
  `bcis-http` → heat-map grouping shows raw ids. De-duplicate the two copies and add the
  missing cases.
- **P3-3** `ProjectRateCardProvider` priority 87 loses to the corporate CSV (90) and
  material-library (95) — a negotiated project rate is overridden by a generic default,
  likely backwards from intent (`ProjectRateCardProvider.cs:11-16, 32`). Confirm intended
  precedence.
- **P3-4** `TotalUSD` re-rounds from a 2dp-rounded `RateUSD` (`BOQModels.cs:67-68`,
  `BOQCostManager.cs:209`) so the USD column won't reconcile against UGX. Compute USD
  totals from UGX totals at display time.
- **P3-5** `EstimateDensityKgPerM3` keeps a hardcoded density switch
  (`BOQCostManager.cs:782-792`) behind `MaterialLookupCsv.GetDensity`; timber is 480 here
  vs 500 in `StructuralDesignSuite` — consolidate to one source (`MATERIAL_LOOKUP.csv`).
- **P3-6** `TryPushSnapshotAsync` (`BOQCostManager.cs:1030-1041`) hands the live mutable
  `BOQDocument` to a background `Task.Run`; push the already-serialised JSON instead.
- **P3-7** `BoqSyncCoordinator.BuildLinePayload` ships `wastePercent = 0` while
  `netQuantity` is already gross-of-waste (`:215`) — server-side gross-up would double-
  apply. Either send the real waste % or document that `netQuantity` is final.
- **P3-8** Confirm `OST_Rebar` (and other priced structural categories) are in
  `DiscMap`/`AllCategoryEnums` so `CollectCandidateElements` doesn't silently drop
  reinforcement (`BOQCostManager.cs:94-95`).
- **P3-9** Cement/sand/aggregate/mortar ratios in `MATERIAL_LOOKUP.csv` are reference-
  only (never consumed), internally inconsistent, and **missing the ~1.54 dry-volume
  bulking factor** — if ever wired into a takeoff they would under-order sand/aggregate
  ~15–25%. Either wire them correctly (with bulking) or clearly mark them non-
  authoritative reference data.

---

# Part B — External alignment & interoperability gaps

Cross-checked against **Fohlio** (FF&E spec/procurement), **RIB iTWO / CostX**
(5D estimating), **Speckle** (open AEC data platform), **Niagara** (Tridium
BMS/digital-twin), and the open **4D/5D/carbon/FM** exchange standards. The
connectors below **already exist** in the codebase but are shallow — the gaps
are depth/correctness, not absence. Current-state lines are VERIFIED; target
standards are cited.

**The asset-identity spine.** Almost every Part B item depends on one thing:
a **stable IFC GlobalId per element that survives across revisions**, carried
through to COBie, Speckle `applicationId`, the rate round-trip join key, and the
BMS point map. Establish/verify this first (INT-0); the rest hang off it.

### INT-0 — Stable IFC GlobalId continuity is the precondition for everything else — VERIFIED gap
COBie export does **not** key Component rows off a stable IFC GlobalId (grep of
`COBieDataCommands.cs` for `GlobalId`/`UniqueId` returns nothing), and the BOQ
snapshot keys on a random per-build `Id` (Part A P0-2). The live-twin layer
*does* use GlobalId (`ExternalElementMapping`, `Host="iot"`), but the chain
`Revit UniqueId → IFC GlobalId → COBie → Speckle applicationId → Niagara point`
is broken at the COBie and BOQ hops.
- **Target:** IFC `GlobalId` (22-char IfcGloballyUniqueId) persistent across
  exports/revisions; carried as the COBie Component external identifier and the
  Speckle `applicationId`. (buildingSMART IfcRoot.GlobalId; NBIMS-US COBie.)
- **Fix:** make GlobalId the single join key across BOQ rows (fixes P0-2),
  COBie Components, Speckle objects, the rate round-trip (INT-2), and the BMS
  map (INT-4). One resolver, not the current two parallel server tables
  (`ExternalElementMapping` vs `ElementGlobalIdRegistry` — consolidate).

### INT-1 — RIB / CostX: no QTO-conformant IFC MVD, no GAEB, no priced round-trip — VERIFIED
**Current:** generic export is `IFCVersion.IFC2x3` + `ExportBaseQuantities=true`
(`ExLink/AutomationEngine.cs:186-187`); PFV path is IFC4 Reference View
(`ExportPfvIfcCommand.cs:57,64`). No `IFCVersion` sets a **Quantity Takeoff MVD**
or **COBie MVD**; no GAEB/CPIxml; the server BOQ extractor is a regex STEP parser
(`IfcBoqExtractor.cs`). There is **no path to re-import a priced BOQ** back into
the model. **Note:** Part A P0-1 means the base quantities are currently written
as zero anyway — fix that first or RIB receives an empty quantity file.
- **Targets (cited):** IFC4 **Quantity Takeoff MVD** with `IfcElementQuantity`
  base quantities (`MethodOfMeasurement="BaseQuantities"`) — the open feed into
  CostX/iTWO; **GAEB DA XML 3.2/3.3** X83 (blank tender) out / **X84** (priced
  bid) in — the EU/DACH estimator round-trip standard; **GUID-keyed XLSX** as the
  pragmatic priced re-import (no published standard exists for priced-rate
  re-import — confirmed); attach **NRM2/ICMS** as `IfcClassificationReference`.
  (gaeb.de FAQ; buildingSMART IfcElementQuantity / Quantity-Sets MVD; NBIMS-US
  Design-to-QTO; RICS NRM2.)
- **Fix:** (a) set the QTO MVD on `IFCExportOptions` for the BOQ export path and
  ensure Qtos are non-zero (depends on P0-1); (b) add a **priced-XLSX re-import**
  that joins on IFC GlobalId and writes rates into `Pset_StingCost` /
  `CST_UNIT_RATE_UGX` with `RateSource="estimator"` (extends the existing
  `BOQImportCommand` + `ParameterOverrideRateProvider`); (c) **GAEB X83/X84** is a
  larger DECISION — scope it, don't speculatively build. Validate phase semantics
  against a real `.x83`/`.x84` XSD before shipping any importer.

### INT-2 — Priced-BOQ round-trip is the highest-value 5D win — DECISION + VERIFIED
This is the single capability that turns STING's BOQ from a one-way exporter into
a 5D round-trip tool, and it reuses machinery already present (P2-8 import path,
`ParameterOverrideRateProvider` reading `CST_UNIT_RATE_UGX`, the rate-source
heat-map). Combine with INT-0 (GlobalId join) and Part A P0-3/P0-4 (so re-imported
rates are confidence-flagged and zero-rate rows are visible).
- **Fix:** GUID-keyed XLSX export → estimator prices → re-import writes rate +
  `RateSource` + CBS code per element. Make `RateSource="estimator"` outrank the
  CSV default but record provenance for the heat-map. Round-trip the **description**
  too (fixes P2-8 — import currently writes the wrong param).

### INT-3 — Speckle connector is metadata-only (flat 6-field DTO, no Base tree) — VERIFIED
**Current:** `BIMManager/SpeckleLinkCommands.cs` is a hand-rolled GraphQL+HTTP
client (`SpeckleHttpTransport`) that serialises a flat `SpeckleElementDto`
(`ElementId, Tag1-3, CategoryName, FamilyName`, line 46) with **no geometry, no
`Base` object, no `applicationId`, no detach/chunk/closure** (line 408 comment:
"No detached children"). Objects won't render in the Speckle viewer and — fatally
for a BOQ-delta workflow — **cannot be diffed**.
- **Targets (cited):** Speckle `Base` objects with content-hashed `id`, **stable
  `applicationId` = Revit UniqueId** (the key for version diff / quantity delta),
  dynamic members for quantities/cost, `displayValue` geometry detached+chunked
  (10 MB object cap); **Speckle.Sdk + Speckle.Objects** .NET SDK rather than raw
  HTTP; **project/model/version** (FE2/v3) terminology; **Automate** function on
  new version to recompute cost; **webhooks** for sync. (speckle.guide Base/
  decomposition/.NET SDK; docs.speckle.systems Automate AutomationContext.)
- **Fix:** DECISION first — adopt `Speckle.Sdk` (recommended; the raw client is
  not viable long-term) vs. extend the DTO. Minimum viable correct version: send
  real `Base` trees with `applicationId`; compute **BOQ quantity-delta** by
  id-set comparison keyed on `applicationId` across versions (this is the
  Speckle-native expression of Part A P0-2's snapshot diff). Automate function +
  webhook are phase-2.

### INT-4 — Niagara/BMS: BACnet/OPC-UA are stubs; no Haystack/Brick/oBIX tags — VERIFIED
**Current:** MQTT telemetry works end-to-end (`Planscape.Edge/Adapters/
MqttTelemetryAdapter.cs`), but **BACnet/OPC-UA/Modbus return empty**
(`Core/Twin/TwinReadback.cs:39,46` `Array.Empty<TwinSnapshot>()`;
`StubAdapters.cs`). `Commands/Twin/NiagaraCommands.cs` exports a **one-way CSV
point list** with no semantic tags. No reverse station→model sync, no
property write-back.
- **Targets (cited):** **Project Haystack** tags (`equip`/`point`/`sensor`/`space`
  + system markers) and/or **Brick Schema** RDF on assets — Niagara natively
  supports Haystack; IFC→Haystack mapping rules exist (`IfcSpace→space`,
  `IfcSensor→sensor`); read/write via **oBIX** (cleanest, Niagara is an oBIX
  server), **BACnet/SC**, or **MQTT-Sparkplug B**; an **element↔point mapping
  table** (BIM GlobalId ↔ Niagara point address). (project-haystack.org BIM WG;
  Brick schema; Tridium oBIX/JSON toolkit; Eclipse Sparkplug.)
- **Fix:** (a) **Highest leverage, lowest cost:** emit **Haystack tags** on the
  COBie/Niagara export keyed off the existing DISC/SYS/FUNC/PROD tokens (they map
  almost 1:1 to Haystack markers) — this makes assets auto-classifiable in
  Niagara with no new transport. (b) Add a Niagara **point-address column** to the
  twin/`ExternalElementMapping` (oBIX URI / BACnet object id) keyed on GlobalId.
  (c) Real BACnet/oBIX readback is a larger DECISION — oBIX client first.

### INT-5 — Fohlio connector is CSV + a NotImplementedException REST stub — VERIFIED + DECISION
**Current:** `ExLink/FohlioLink.cs` is a deliberate **reference-link layer** (CSV/
XLSX round-trip mapping `FOHLIO_REF_TXT` + manufacturer/model write-back); the
REST tier throws `NotImplementedException` x3 (lines 161/164/167). No product
specs, cut-sheets, images, POs, budget, or supplier master.
- **Research finding (cited):** **Fohlio has no public/documented REST API** — no
  developer portal, no API reference, no webhooks. The only sanctioned interchange
  is UI-driven Excel/CSV import-export. (fohlio.com product pages; no API docs
  found.)
- **Fix:** This is **DECISION/commercial, not engineering.** Do NOT build against
  a phantom API. Two honest paths: (a) harden the **CSV/Excel round-trip** into a
  schema-mapped, idempotent, GlobalId/`FOHLIO_REF`-keyed exchange with attachment
  handling out-of-band — this is the realistic ceiling; (b) if a private/partner
  API is obtained from Fohlio, implement the stub against items/specs/attachments/
  PO/vendor. Update the `FohlioRestTransport` stub comment to say "no public API
  — CSV is the supported path" so it's not mistaken for unfinished work. The
  FF&E↔BOQ overlap worth wiring: Fohlio FF&E budget lines ↔ STING BOQ `each`
  rows for furniture/equipment categories (a real reconciliation, CSV-feasible).

### INT-6 — 4D is MS-Project-XML-only; element↔activity link is heuristic — VERIFIED
**Current:** `BIMManager/SchedulingCommands.cs` imports/exports **MS Project XML
(MSPDI)** only (line 399 import, 1487 export); reads `WBS` (line 426) but links
elements to tasks by **Phase + Level + Category** heuristic (`TradeSequence`,
line 43), not by activity code/WBS on the element. No XER, no P6 PMXML, no Asta.
The server P6 link (`P6Controller` + `P6LiveLinkService`) is real but **one-way**
(reads activities → updates `%`). 5D cash-flow is S-curve-from-schedule, not
resource-loaded.
- **Targets (cited):** **IFC 4D** (`IfcTask`, `IfcWorkSchedule`, `IfcRelSequence`,
  `IfcRelAssignsToProcess`); **P6 XER + PMXML**, **MSPDI**, Asta — round-trippable
  via an MPXJ-style adapter; element↔activity by **WBS / activity code / Location
  Breakdown Structure**; ICMS for cost classification; carbon time-phased with
  the schedule. (buildingSMART IfcWorkSchedule; mpxj.org formats; ICMS Coalition.)
- **Fix:** (a) store an **activity-code/WBS shared param** on elements and link by
  it (deterministic, replaces the brittle Phase/Level/Category heuristic); (b) add
  **IFC 4D export** (`IfcTask`/`IfcWorkSchedule`/`IfcRelSequence`) so the programme
  survives outside Revit; (c) **P6 XER/PMXML** read is a larger DECISION — MSPDI
  covers MS Project + Asta-via-XML today.

### INT-7 — Carbon: three engines, four parameters, no EN 15978 module breakdown / live EPD — VERIFIED
**Current:** `BOQ/CarbonFactorResolver.cs` is the most correct (unit-aware
per-m³/per-kg, fossil/biogenic split) and writes `CST_EMBODIED_CARBON_KG`;
`V6/CarbonStageTracker.cs` writes A1-A3…C `CBN_*` params with a flat **350
kgCO₂e/m³** proxy (line 138-143); `OptionCostCarbonCalculator` has its own
category dict; `StructuralDesignSuite` a fourth convention. EPD source is a
free-text param; factors are static CSV/ICE, never queried live. (This is Part A
P0-7 / P1-2 seen from the carbon side.)
- **Targets (cited):** **BS EN 15978** module structure (A1-A5 / B / C / D);
  **RICS WLCA 2nd ed.** (mandatory for RICS members since 1 Jul 2024); **EPD
  (EN 15804)**-first, **ICE database** fallback; carry per-module carbon via custom
  Psets (native `Pset_EnvironmentalImpactValues.ClimateChange` is single-value
  only); **ICMS** for combined cost+carbon reporting. (buildingSMART
  Pset_EnvironmentalImpactValues; RICS WLCA; ICE database.)
- **Fix:** converge all carbon on **`CarbonFactorResolver`** (Part A P0-7), then
  add **EN 15978 module params** (A1-A3/A4/A5/B/C/D) and report against ICMS. Keep
  the existing biogenic split (it's correct). Live EPD/EC3 query is a phase-2
  DECISION; for now make the ICE/EPD factor table the single data-driven source.

### INT-8 — Shared IFC substrate has no cost/quantity/4D/carbon Pset or IDS — VERIFIED
**Current:** `shared/ifc/` ships 52 enums + 5 Psets + 5 IDS covering tag grammar /
spatial codes / drawing / project-org — **nothing for cost, quantity, 4D, carbon,
or FF&E**. So the host-agnostic cross-host contract (the thing that lets Bonsai/
ArchiCAD/Tekla read STING data) stops at tagging metadata.
- **Fix:** author a **`Pset_StingCost`** (already emitted ad-hoc by
  `IfcQuantitySetWriter` — formalise it), **`Pset_StingCarbon`** (EN 15978
  modules), and a **`Pset_Sting4D`** (activity code / WBS / planned dates) in the
  substrate, with matching **IDS** validation specs. This makes the 5D/carbon/4D
  data first-class in the cross-host contract, not just a Revit-side stamp.

### Part B — consolidated alignment checklist

A Revit BOQ/4D/5D/FM tool is "aligned" with these platforms when it can:
- [ ] Emit a **stable IFC GlobalId** per element, persistent across revisions, as
      the universal join key (INT-0).
- [ ] Export **IFC4 QTO MVD** with non-zero base quantities + **NRM2/ICMS
      classification refs** (INT-1; depends on P0-1).
- [ ] **Re-import a priced BOQ** (GUID-keyed XLSX; GAEB X84 as stretch) writing
      rates+provenance to `Pset_StingCost` (INT-2).
- [ ] Send **Speckle `Base` trees** with `applicationId` and compute version
      **quantity-deltas** by id-set diff (INT-3).
- [ ] Emit **Haystack/Brick tags** on assets + an element↔BMS-point map; read/write
      Niagara via **oBIX** (INT-4).
- [ ] Reconcile **FF&E budget lines ↔ BOQ each-rows** with Fohlio over the
      supported CSV path (INT-5).
- [ ] Export **IFC 4D** + link elements↔activities by **WBS/activity code**
      (INT-6).
- [ ] Carry **EN 15978 per-module carbon** from one engine, EPD-first (INT-7).
- [ ] Formalise **cost/carbon/4D Psets + IDS** in the shared substrate (INT-8).

### Part B — suggested order

1. **INT-0** (GlobalId spine) + Part A **P0-1** (IFC Qtos non-zero) + **P0-2**
   (deterministic BOQ key) — these unblock almost every other INT item.
2. **INT-2 / INT-1(a,b)** — priced-XLSX round-trip + QTO MVD: the highest-value
   5D capability, mostly reusing existing import/rate machinery.
3. **INT-7** — converge carbon (also Part A P0-7) and add EN 15978 modules.
4. **INT-4(a)** — Haystack tags off existing tokens (cheap, high FM value).
5. **INT-3** — Speckle SDK + `applicationId` + version diff (DECISION on SDK).
6. **INT-6 / INT-8 / INT-5 / INT-1(c) / INT-4(b,c)** — larger DECISIONs (P6 XER,
   GAEB, substrate Psets, Fohlio commercial, real BMS transport): scope each,
   produce a recommendation + minimal scaffold, don't speculatively full-build.

---

## What is already CORRECT (do not "fix")

The audits confirmed these are sound — leave them alone unless a change above forces it:
- **Waste-factor arithmetic** (`WasteFactor.cs:68-73`, `MeasuredAddition.cs:47-53`):
  applied once, on quantity only (never the rate), default 5%, only to measured units,
  negatives/NaN clamped, rebar-lap/concrete-over-order summed-and-applied-once. Cleanest
  part of the subsystem.
- **Per-kg vs per-m³ carbon unit routing in the BOQ path** (`BOQCostManager.cs:532-541`,
  `CarbonFactorResolver.cs:21-78`) — correctly guards the historical 1000× bug.
- **Biogenic sign convention** (`BiogenicCarbon.cs:30,34`; `CarbonFactorResolver.cs:104-105`)
  — timber biogenic −1.64 kg/kg, negatives allowed (RICS WLCA / ICE v3.0).
- **Densities, kg/m³ rebar ratio bands, brick/block counts, mortar volume factors** — all
  in correct engineering ranges (the *application* is where bugs live, not the constants).
- **Net-vs-gross for the cost quantity** — walls/floors use Revit's net
  `HOST_VOLUME_COMPUTED`/`HOST_AREA_COMPUTED`, which already subtract openings and resolve
  joins. (The code just isn't *aware* it relies on this; the carbon estimator throws the
  accuracy away — see P1-6.)

---

## Suggested execution order

1. **P0-2** (deterministic hash) and **P0-8** (workflow engine pointer) — tiny, isolated,
   high value.
2. **P0-1** (IFC zeros) and **P0-3 + P0-4** (uncosted-at-risk rollup + confidence gate) —
   these protect the integrity of every issued bill.
3. **P0-5 + P0-6** (network thread + currency safety) — prevent hangs and 4700× errors.
4. **P0-7** (engine consolidation) — the big structural change; do it deliberately, with
   the per-element API extraction, then retire `OptionCostCarbonCalculator`'s fork and the
   `CarbonStageTracker` flat proxy.
5. **P1 set** (per-material split, rebar convention, 1.0 placeholder, design options, tag
   reuse) — the accuracy core the user specifically asked about.
6. **P2 / P3** — consistency, mapping, performance, hardening.

For each change: confirm the diagnosis at the cited line, implement, add the stated
verification, and update `docs/CHANGELOG.md` with a Phase entry noting the
no-`dotnet build` caveat (Linux sandbox). Do not merge to `main` without Revit verification.
