# Tag Tier Automation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Automatically populate tier rows 2–10 (containers `ASS_TAG_2_TXT … ASS_TAG_10_TXT`) as visible, depth-gated label rows across all 206 tag families, with **zero per-family manual Family-Editor work**, reusing the existing `FamilyLabelAuthor` tier pipeline.

**Architecture:** The heavy lifting already exists — `FamilyLabelAuthor.AuthorLabelsMulti` binds every content param across **all tag modes**, OR-merges the `and(TAG_PARA_STATE_N_BOOL, HANDOVER_MODE_*_BOOL)` visibility formulas per parameter, and best-effort rebinds the primary label; `TagConfigPlanResolver.LoadAllPerMode(doc)` parses per-family `TierPlan`s per mode from the paired `STING_TAG_CONFIG_v5_0_*` / `*_DesignConstruction` CSVs; `SetParagraphDepthCommand` drives depth 1–10 via the `TAG_PARA_STATE_N_BOOL` gates; `SetPatternMode_*` flips the active mode at runtime. The **only** missing capability is creating the *visible label rows* inside a family that ships with one. STING implements tag labels as **`Dimension` elements with `FamilyLabel`** (`FamilyLabelAuthor.cs:416-427`), and the Revit API **can** copy dimensions. So the primary strategy is: **materialize the physical dimension-rows a family needs (one per merged `TierRow` across all modes), then let `AuthorLabelsMulti` bind params + wire the mode×depth formulas** — all via the API, retrofitting the existing 206 families in place. The cloner creates rows only; it implements **no** mode logic. If the clone proves unstable (dimensions carry geometric references that may not survive copy), fall back to a bounded set of master template families (~the distinct entries in `CategoryTemplateMap`) authored once with the full row set, then regenerated automatically.

**Modes / construction-handover (critical):** A tag is a 2-D grid — **depth (T1–T10) × mode (DC / Handover / Custom)**. Each mode ships its own CSV with *different* T4–T10 content, and a *tier* is a **group of rows** (e.g. Handover T4 = `COMM_STATE_TXT` + `COMM_DATE_TXT` + `COMM_OPERATIVE_TXT`). The number of physical label rows a family needs is therefore the **union of all `TierRow`s across all mode plans**, not 9. Mode selection at runtime is a project/type BOOL flip (`SetPatternMode_*`) that costs no re-author. See `## How modes (construction/handover) are handled` below.

**Tech Stack:** C# / .NET 8 (`net8.0-windows`), Revit API 2025/2026 (`Autodesk.Revit.DB.FamilyManager`, `Dimension.FamilyLabel`, `ElementTransformUtils.CopyElements`), xUnit 2.9.2 for the pure-logic test project (`StingTools.Tags.Tests`, Revit-API-free).

**Build note:** This machine CAN build the plugin (Revit 2025/2026 + .NET SDK present — see memory `reference-build-environment`). Run `dotnet build` for compile verification. Family-editor / runtime behaviour still requires an in-Revit smoke test — those steps are marked **[VERIFY-IN-REVIT]** and cannot be unit-tested.

**Deploy target:** Revit addin loads from `C:\Dev\STING_PLACEMENT_GOLD` (memory `project-stingtools-deploy-target`). Close Revit, build Release, copy DLLs to GOLD, reopen to smoke-test.

---

## Why this plan is small

A survey of the codebase (2026-07-03) found the tier machinery is ~80% built:

| Capability | Status | Where |
|---|---|---|
| Parse per-family tier rows (T4–T10) from CSV | ✅ Built | `TagConfigCsvReader.LoadFile/Parse` → `TierPlan`/`TierRow` (`Core/TagConfigCsvReader.cs`, `Core/PerFamilyTierMap.cs:22-70`) |
| Bind tier container shared params into a family | ✅ Built | `FamilyLabelAuthor.BindSharedParameters` (`Tags/FamilyLabelAuthor.cs:240`) |
| Apply depth-gate visibility formulas | ✅ Built | `FamilyLabelAuthor.ApplyVisibilityFormulas` (`:321`) |
| Depth 1–10 driver via `TAG_PARA_STATE_N_BOOL` | ✅ Built | `SetParagraphDepthCommand` (`Tags/ParagraphDepthCommand.cs:37`) |
| Rebind the **primary** label (tier 1) | ✅ Best-effort | `FamilyLabelAuthor.TryRebindPrimaryLabel` (`:401`) |
| **Create visible label rows for tiers 2–10** | ❌ **The gap** | Marked `// TODO-VERIFY-API` (`:35-39`, `:424`) |

This plan closes exactly one gap (the visible rows) and wires the existing pipeline to run end-to-end over all 206 families, with tests pinning the pure-logic pieces.

---

## How modes (construction/handover) are handled

The mode axis is **already fully automated** — this plan reuses it, it does not rebuild it.

| Piece | Where | Role |
|---|---|---|
| `TagMode { DC, Handover, Custom }` | `ParamRegistry.cs:1425` | The three modes. DC = Design & Construction; Handover = FM. |
| `HANDOVER_MODE_{DC,HANDOVER,CUSTOM}_BOOL` | `ParamRegistry.cs:1001-1011` | Mutually-exclusive mode gates (on ProjectInformation / type). |
| Paired CSVs `..._ARCH.csv` + `..._ARCH_DesignConstruction.csv` | `Data/` | Different T4–T10 content per mode; same family keys. |
| `HandoverModeHelper.GetAllTagConfigCsvsForAllModes(doc)` | `HandoverModeHelper.cs:155` | Resolves the CSV set for every mode that ships. |
| `TagConfigPlanResolver.LoadAllPerMode(doc)` | `TagConfigPlanResolver.cs:82` | `mode → (family → TierPlan)`. |
| `FamilyLabelAuthor.AuthorLabelsMulti(fdoc, modePlans, opts)` | `FamilyLabelAuthor.cs:115` | Binds params + OR-merges `and(state, modeGate)` formulas per param. |
| `SetPatternMode_{Handover,DC,Custom}` | `StingCommandHandler.cs:9186` | Runtime mode flip on selected types — no re-author. |

**Formula shape when a slot is dual-wired** (`FamilyLabelAuthor.cs:336-392`):

```
if( or( and(TAG_PARA_STATE_4_BOOL, HANDOVER_MODE_HANDOVER_BOOL),
        and(TAG_PARA_STATE_4_BOOL, HANDOVER_MODE_DC_BOOL) ), PARAM, "")
```

**Consequence for the cloner (Task 4):** it must materialize one physical row per **merged `TierRow`** (union across modes), and it binds/labels rows only — every mode and depth gate is applied afterward by `AuthorLabelsMulti`. The cloner never reads a mode. Empty (wrong-mode / below-depth) rows resolve to `""`; verify in the spike (Task 1) that empty rows collapse rather than leave gaps.

---

## CHOSEN PATH — Route B (Template families, styled)

**Spike verdict (Task 1, run 2026-07-03):** real STING tag families contain **one API-opaque annotation Label (`TextElement`), zero Dimensions**. The Revit API cannot create, read-bind, rebind, or copy-retarget a Label. Therefore in-place row automation is impossible; the clone tasks below (Task 2, Task 4) are **SUPERSEDED**. We take **Route B**: author the label geometry **once per tag shape** in the Family Editor, then let the already-built pipeline bind params + write mode×depth formulas + regenerate all 206.

**Cross-doc copy spike (2026-07-03):** copying labels between family docs was tested to see if one master label stack could propagate to all 52 templates. Result: the **view-based** `ElementTransformUtils.CopyElements(sourceView, ids, destView, …)` overload is required (the document overload rejects view-specific annotations), and even then Revit blocks the paste with *"Can't paste Labels across Families of different Categories."* Since the 52 shapes are 52 distinct tag categories, **cross-category propagation is impossible** — the ~92-placement shortcut is dead. Same-category copy does work, but each category already collapses to one template, so it yields no saving. **Full per-row authoring cost (~3,000 label placements) stands** unless the styling granularity is reduced (see options).

**Per-tier styling implication:** a single Label element carries **one** text style. Because tiers need different bold/italic/colour/size (`TierRow.Style/Color/Size`), each styled row is its **own** stacked Label element, formula-gated to `""` when inactive (empty labels collapse, leaving no gap). So a template's authoring cost = one Label element per required content row (union across modes).

**STYLING DECISION (2026-07-03):** full **per-row** styling — one Label element per content row, each with its exact `TierRow.Style/Color/Size`. Cost accepted: ~52 templates × ~59 rows ≈ ~3,000 one-time label placements (no copy shortcut — cross-category paste is blocked). To make that tractable, B1.5 builds an in-Revit authoring aid.

### Route B task spine

- **B1 — Template inventory + authoring spec (automatable, DONE for sample):** from `CategoryTemplateMap` (`TagFamilyCreatorCommand.cs:46`) enumerate the distinct tag shapes; from the per-mode CSVs compute, per shape, the ordered union of content rows with each row's param + style + colour + size + prefix/suffix. Spec at `docs/superpowers/plans/tag-tier-authoring-spec.md` (Generic Tag fully worked). Extend to all 52 shapes.
- **B1.5 — Authoring-aid command (automatable, HIGH VALUE):** `TierTemplate_Prep` — for the currently-open template family: (a) `AddSharedParameters` for the shape's full required-param union; (b) place numbered guide detail-lines / reference text at the correct stacked Y positions (pitch from spec); (c) `TaskDialog`/exported checklist listing, per guide slot, the exact param + style + colour + size to bind. Turns each ~59-label job into "click Label tool → snap to guide N → pick pre-named param → set listed style." Params/formulas are already automatable; this only accelerates the irreducible manual label placement.
- **B2 — [MANUAL-IN-REVIT] Author master templates:** for each shape, run `TierTemplate_Prep`, then stack one Label element per guide slot per the checklist, bind each to its content param. Save to the template source folder. One-time; 52 templates, not 206.
- **B3 — Regeneration (mostly built):** `CreateTagFamiliesCommand` already opens template → `AddSharedParameters` → `AuthorLabelsMulti` (binds + mode×depth formulas) → saves per family. Point it at the new templates; regenerate the 206. No per-family label work.
- **B4 — Audit + smoke test:** `TierAutomation_Audit` (Task 5, adapt to count Label elements per required row) → in-Revit depth + mode switch verification (Task 7).

Tasks 3 (CSV parsing tests) and 8 (rollout/changelog) carry over unchanged. Tasks 2 and 4 (clone engine) are retained below only as a record of the rejected approach — **do not implement them**.

---

## File Structure

**Create:**
- `StingTools/Tags/TierRowCloner.cs` — the new capability: clone tier-1 dimension-label into tiers 2–10, offset + rebind. Single responsibility; Revit-API-dependent.
- `StingTools/Tags/TierCoverageAuditor.cs` — read-only per-family tier-coverage report (which tiers have a bound+gated+visible row). Revit-API-dependent.
- `StingTools/Tags/TagTierCommands.cs` — two `IExternalCommand`s: `TierAutomation_Apply` (retrofit families) and `TierAutomation_Audit` (report). Thin dispatch over the two engines above.
- `StingTools.Tags.Tests/TierRowGeometryTests.cs` — xUnit tests for the pure-logic offset/label-plan math extracted from the cloner.
- `StingTools.Tags.Tests/TagConfigCsvReaderTierTests.cs` — xUnit tests pinning `TagConfigCsvReader` tier parsing.

**Modify:**
- `StingTools/Tags/TierRowCloner.cs` hosts a pure-logic helper `TierRowGeometry` (static, Revit-free) so the geometry math is unit-testable without Revit. (Kept in the same file per "files that change together live together"; the test project compile-links only the Revit-free type — see Task 2.)
- `StingTools.Tags.Tests/StingTools.Tags.Tests.csproj` — add `<Compile Include>` entries for the Revit-free helper(s).
- `StingTools/Tags/TagFamilyCreatorCommand.cs:1144` (`CreateTagFamiliesCommand.Execute`) — after the existing `AuthorLabelsMulti` authoring call, call `TierRowCloner.EnsureRows(...)` so newly generated families get all physical rows (Task 6).

**Not touched:** `FamilyLabelAuthor.cs`, `TagConfigCsvReader.cs`, `PerFamilyTierMap.cs`, `ParagraphDepthCommand.cs`, `ParamRegistry.cs` — reused as-is.

---

## Task 1: SPIKE — prove dimension-label cloning survives inside a family doc

**This task gates the whole plan.** If cloning works, we retrofit the 206 families in place (no manual templates). If it fails, we switch to the template-family fallback (Task 1b). Do this first, throwaway code is fine, but capture the verdict in the plan.

**Files:**
- Scratch: a temporary command or LINQPad-style snippet run inside Revit against ONE existing tag `.rfa` opened as a family document.

- [ ] **Step 1: Write the spike probe**

Create a throwaway `[Transaction(TransactionMode.Manual)]` command `TierSpikeCommand` in a scratch file `StingTools/Tags/_TierSpikeCommand.cs`. Open (or use the active) family document and attempt to clone the tier-1 dimension:

```csharp
using System;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Core;

namespace StingTools.Tags
{
    [Transaction(TransactionMode.Manual)]
    public class TierSpikeCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData c, ref string message, ElementSet e)
        {
            Document fdoc = c.Application.ActiveUIDocument.Document;
            if (!fdoc.IsFamilyDocument)
            { TaskDialog.Show("Spike", "Open a tag .rfa as a family first."); return Result.Cancelled; }

            var dims = new FilteredElementCollector(fdoc).OfClass(typeof(Dimension)).Cast<Dimension>().ToList();
            Dimension src = dims.FirstOrDefault(d => d.FamilyLabel?.Definition?.Name == ParamRegistry.TAG1);
            if (src == null)
            { TaskDialog.Show("Spike", $"No dimension bound to {ParamRegistry.TAG1}. Found {dims.Count} dims."); return Result.Cancelled; }

            string report;
            using (var tx = new Transaction(fdoc, "STING Tier Spike"))
            {
                tx.Start();
                XYZ offset = new XYZ(0, -3.0 / 304.8, 0); // 3 mm down, feet
                var ids = ElementTransformUtils.CopyElements(fdoc, new[] { src.Id }, offset);
                Dimension copy = ids.Count == 1 ? fdoc.GetElement(ids[0]) as Dimension : null;
                bool copied = copy != null;
                bool rebound = false;
                if (copied)
                {
                    // Bind to a real content param that exists in the family (e.g. a
                    // Handover T4 row). Pick the first family param that is NOT TAG1.
                    FamilyParameter target = fdoc.FamilyManager.Parameters
                        .Cast<FamilyParameter>()
                        .FirstOrDefault(p => p.Definition.Name != ParamRegistry.TAG1
                                          && p.Definition.Name.EndsWith("_TXT"));
                    try { copy.FamilyLabel = target; rebound = target != null; } catch (Exception ex) { report = ex.Message; }
                }
                report = $"copied={copied} rebound={rebound} newIds={ids.Count}";
                tx.Commit();
            }
            TaskDialog.Show("Spike", report);
            return Result.Succeeded;
        }
    }
}
```

- [ ] **Step 2: Build**

Run: `dotnet build StingTools/StingTools.csproj -p:RevitApiPath="C:\Program Files\Autodesk\Revit 2025"`
Expected: PASS (0 errors).

- [ ] **Step 3: [VERIFY-IN-REVIT] Run the spike**

Deploy to GOLD, open Revit, open one existing tag `.rfa` (e.g. a duct tag) as a family, run `TierSpikeCommand`. Record the dialog output.
Expected (success case): `copied=True rebound=True newIds=1`, and a second label row appears offset below tier 1 in the family view.

- [ ] **Step 4: Record the verdict**

Edit this plan file, replacing the line below with the observed result:

```
SPIKE VERDICT (2026-07-03, run in Revit 2025 on a real STING tag family):
  - Family has 266 params; ASS_TAG_1_TXT present (all tier/content params ARE bound).
  - Annotation elements: 1x TextElement (the Label). ZERO Dimension, ZERO TextNote.
  - The visible label is an API-opaque annotation Label — cannot be read/created/rebound/retargeted.
  - Conclusion: CLONE path IMPOSSIBLE. STING's dimension-based TryRebindPrimaryLabel is a no-op on real families.
DECISION: neither original branch. Choose between:
  - Route A (COMBINED-STRING): assemble the full depth×mode multi-line tag into the ONE existing
    label's parameter at tag-write time (project-side). Zero family edits, works on all 206 as-is.
    Trade-off: single text style for the whole tag (no per-tier bold/colour/size). Needs its own
    spike: does a Label render a TEXT param value containing line breaks as multiple lines?
  - Route B (TEMPLATE): author N master template families (one per CategoryTemplateMap shape) by hand
    with a proper multi-parameter Label, regenerate the 206. Keeps per-tier styling. Manual once.
```

- [ ] **Step 5: Delete the scratch command, commit the verdict**

```bash
rm StingTools/Tags/_TierSpikeCommand.cs
git add docs/superpowers/plans/2026-07-03-tag-tier-automation.md
git commit -m "spike: verify dimension-label cloning feasibility for tier automation"
```

---

## Task 1b: FALLBACK ONLY — master template families (run only if spike FAILED)

Skip entirely if the spike succeeded. This is the one-time manual investment that makes everything downstream automatic.

**Files:**
- Create: master `.rfa` templates in `TagFamilyConfig.GetOutputDirectory()`'s template source folder — one per distinct value in `CategoryTemplateMap` (`TagFamilyCreatorCommand.cs:46`), e.g. `Duct Tag`, `Pipe Tag`, `Door Tag`, `Generic Tag`, etc. (bounded set, ~10–20, not 206).

- [ ] **Step 1: [MANUAL-IN-REVIT] Author one master template per shape**

For each distinct template, in the Revit Family Editor: place 10 stacked dimension-labels (3 mm vertical pitch), bind row 1 → `ASS_TAG_1_TXT`, row 2 → `ASS_TAG_2_TXT`, … row 10 → `ASS_TAG_10_TXT` via each dimension's Label field. Save.

- [ ] **Step 2: Document the template inventory**

List the authored templates in this plan under a `## Template Inventory` heading so downstream regeneration knows the source set.

- [ ] **Step 3: Commit the templates**

```bash
git add "StingTools/Data/**/*.rfa"
git commit -m "feat: master tag templates with 10 pre-placed tier rows (API label-creation fallback)"
```

Then proceed to Task 3 (the CLONE-specific Tasks 2, 4, 5 are replaced by regeneration from these templates in Task 6).

---

## Task 2: Pure-logic row helpers (Revit-free) — TDD  ⚠️ SUPERSEDED (clone path rejected by spike; kept for record)

The Revit-free half of `TierRowCloner.cs`: the pitch constant + `OffsetMm`, and the cross-mode param union `RequiredParamsForFamily` (the piece worth pinning — it dedups and orders content params across DC + Handover + Custom plans). Both are Revit-API-free and unit-testable; the Revit-dependent class lives in a separate `TierRowCloner.Revit.cs` (Task 4) that the test project does **not** compile-link.

**Files:**
- Create: `StingTools/Tags/TierRowCloner.cs` (Revit-free types only)
- Test: `StingTools.Tags.Tests/TierRowClonerLogicTests.cs`
- Modify: `StingTools.Tags.Tests/StingTools.Tags.Tests.csproj`

- [ ] **Step 1: Write the failing test**

`StingTools.Tags.Tests/TierRowClonerLogicTests.cs`:

```csharp
using System.Collections.Generic;
using StingTools.Core;
using StingTools.Tags;
using Xunit;

public class TierRowClonerLogicTests
{
    [Theory]
    [InlineData(2, -3.0)]  // 2nd physical row sits 1 pitch below tier 1
    [InlineData(10, -27.0)]
    public void OffsetMm_is_pitch_times_index_minus_one(int rowIndex, double expectedMm)
        => Assert.Equal(expectedMm, TierRowGeometry.OffsetMm(rowIndex, pitchMm: 3.0), 3);

    [Fact]
    public void RequiredParamsForFamily_unions_modes_dedups_and_preserves_order()
    {
        var dc = new TierPlan();
        dc.T4Rows.Add(new TierRow { Parameter = "ASS_DESIGN_OPTION_TXT" });
        dc.T4Rows.Add(new TierRow { Parameter = "SHARED_TXT" });

        var handover = new TierPlan();
        handover.T4Rows.Add(new TierRow { Parameter = "COMM_STATE_TXT" });
        handover.T4Rows.Add(new TierRow { Parameter = "SHARED_TXT" }); // dup across modes

        var perMode = new Dictionary<string, Dictionary<string, TierPlan>>
        {
            ["DesignConstruction"] = new() { ["FAM"] = dc },
            ["Handover"]           = new() { ["FAM"] = handover },
        };

        List<string> got = TierRowCloner.RequiredParamsForFamily("FAM", perMode);

        Assert.Equal(new[] { "ASS_DESIGN_OPTION_TXT", "SHARED_TXT", "COMM_STATE_TXT" }, got);
    }

    [Fact]
    public void RequiredParamsForFamily_returns_empty_for_unknown_family()
        => Assert.Empty(TierRowCloner.RequiredParamsForFamily("NOPE",
            new Dictionary<string, Dictionary<string, TierPlan>>()));
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test StingTools.Tags.Tests/StingTools.Tags.Tests.csproj --filter TierRowClonerLogicTests`
Expected: FAIL — types not compiled into the test project.

- [ ] **Step 3: Write minimal implementation**

`StingTools/Tags/TierRowCloner.cs` (Revit-free). Note `RequiredParamsForFamily` is defined **here** (not in the `.Revit.cs` file) so it is unit-testable — it uses only `TierPlan`/`TierRow` which are Revit-free (`PerFamilyTierMap.cs`):

```csharp
using System;
using System.Collections.Generic;
using StingTools.Core;

namespace StingTools.Tags
{
    /// <summary>Revit-free geometry constants for stacking cloned rows.</summary>
    public static class TierRowGeometry
    {
        public const double DefaultPitchMm = 3.0;

        /// <summary>Vertical offset (mm, negative = downward) of the Nth physical row vs. row 1.</summary>
        public static double OffsetMm(int rowIndex, double pitchMm = DefaultPitchMm)
            => -(rowIndex - 1) * pitchMm;
    }

    public static partial class TierRowCloner
    {
        /// <summary>
        /// Ordered, de-duplicated union of TierRow.Parameter for one family across
        /// ALL mode plans. perMode = TagConfigPlanResolver.LoadAllPerMode(doc) output.
        /// </summary>
        public static List<string> RequiredParamsForFamily(
            string familyName, Dictionary<string, Dictionary<string, TierPlan>> perMode)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var ordered = new List<string>();
            if (perMode == null) return ordered;
            foreach (var modeEntry in perMode)
            {
                if (!modeEntry.Value.TryGetValue(familyName, out TierPlan plan)) continue;
                foreach (var rows in new[] { plan.T4Rows, plan.T5Rows, plan.T6Rows, plan.T7Rows, plan.T8Rows, plan.T9Rows, plan.T10Rows })
                    foreach (TierRow r in rows)
                        if (!string.IsNullOrWhiteSpace(r.Parameter) && seen.Add(r.Parameter))
                            ordered.Add(r.Parameter);
            }
            return ordered;
        }
    }
}
```

> `TierRowCloner` is declared `partial` so the Revit-dependent `EnsureRows` (Task 4) lives in `TierRowCloner.Revit.cs` under the same class. The test project compile-links only `TierRowCloner.cs` (Revit-free), so `RequiredParamsForFamily` is testable without the Revit API.

Add to `StingTools.Tags.Tests/StingTools.Tags.Tests.csproj` inside the existing `<Compile Include>` `<ItemGroup>` (also add the `TierPlan`/`TierRow` link if Task 3 hasn't yet):

```xml
<Compile Include="..\StingTools\Tags\TierRowCloner.cs" Link="Tags\TierRowCloner.cs" />
<Compile Include="..\StingTools\Core\PerFamilyTierMap.cs" Link="Core\PerFamilyTierMap.cs" />
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test StingTools.Tags.Tests/StingTools.Tags.Tests.csproj --filter TierRowClonerLogicTests`
Expected: PASS (4 cases). Confirm `TierRow.Parameter` and `TierPlan.T4Rows..T10Rows` names against `PerFamilyTierMap.cs:22-70` before running; fix the test to match real property names, not the parser.

- [ ] **Step 5: Commit**

```bash
git add StingTools/Tags/TierRowCloner.cs StingTools.Tags.Tests/TierRowClonerLogicTests.cs StingTools.Tags.Tests/StingTools.Tags.Tests.csproj
git commit -m "feat: Revit-free row-union + geometry helpers for tier cloning + tests"
```

---

## Task 3: Pin TagConfigCsvReader tier parsing with tests

Lock current CSV→TierPlan behaviour so downstream refactors can't silently break tier content. `TagConfigCsvReader`/`TierPlan`/`TierRow` are Revit-free (`Core/TagConfigCsvReader.cs`, `Core/PerFamilyTierMap.cs`).

**Files:**
- Test: `StingTools.Tags.Tests/TagConfigCsvReaderTierTests.cs`
- Modify: `StingTools.Tags.Tests/StingTools.Tags.Tests.csproj` (compile-link the reader + POCOs if not already linked)

- [ ] **Step 1: Confirm the reader + POCOs are Revit-free and linkable**

Run: `grep -n "Autodesk.Revit" StingTools/Core/TagConfigCsvReader.cs StingTools/Core/PerFamilyTierMap.cs`
Expected: no matches (empty output). If matches exist, STOP — extract the pure parse into a Revit-free type first and note it here.

- [ ] **Step 2: Write the failing test**

`StingTools.Tags.Tests/TagConfigCsvReaderTierTests.cs`:

```csharp
using System.Collections.Generic;
using System.Linq;
using StingTools.Core;
using Xunit;

public class TagConfigCsvReaderTierTests
{
    // Minimal fixture mirroring the real CSV column layout:
    // Col0=Family, Col1=Tier, Col2=Parameter, Col3=Prefix, Col4=Suffix,
    // Col5=Spc, Col6=Brk, ... Col9=Name, Col11=Style, Col12=Color, Col13=Size
    private static readonly string[] Lines = new[]
    {
        "Family,Tier,Parameter,Prefix,Suffix,Spc,Brk,x,y,Name,z,Style,Color,Size",
        "M_HVAC_EQP,T4,ASS_STATUS_TXT,[,],1,1,,,Status,,BOLD,ORANGE,3.0",
        "M_HVAC_EQP,T5,ASS_REV_TXT,rev,,0,0,,,Revision,,ITALIC,BLUE,2.5",
    };

    [Fact]
    public void Parse_groups_rows_by_family()
    {
        Dictionary<string, TierPlan> plans = TagConfigCsvReader.Parse(Lines, "fixture.csv");
        Assert.True(plans.ContainsKey("M_HVAC_EQP"));
    }

    [Fact]
    public void Parse_populates_T4_row_fields()
    {
        TierPlan plan = TagConfigCsvReader.Parse(Lines, "fixture.csv")["M_HVAC_EQP"];
        TierRow row = plan.T4Rows.Single();
        Assert.Equal("ASS_STATUS_TXT", row.Parameter);
        Assert.Equal("BOLD", row.Style);
        Assert.Equal("ORANGE", row.Color);
        Assert.Equal(3.0, row.Size, 3);
    }
}
```

> Before running: open `StingTools/Core/TagConfigCsvReader.cs` and confirm the exact column indices (the survey reported Col1=Tier, Col2=Parameter, Col3–4=Prefix/Suffix, Col5–6=Spc/Brk, Col9=Name, Col11–14=Style/Color/Size/…). Adjust the fixture header/columns to match the real parser BEFORE asserting — this test documents real behaviour, not assumed behaviour.

- [ ] **Step 3: Run test to verify it fails (then passes once linked)**

Run: `dotnet test StingTools.Tags.Tests/StingTools.Tags.Tests.csproj --filter TagConfigCsvReaderTierTests`
Expected first run: FAIL (types not compiled into test project). Add compile-links:

```xml
<Compile Include="..\StingTools\Core\TagConfigCsvReader.cs" Link="Core\TagConfigCsvReader.cs" />
<Compile Include="..\StingTools\Core\PerFamilyTierMap.cs" Link="Core\PerFamilyTierMap.cs" />
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test StingTools.Tags.Tests/StingTools.Tags.Tests.csproj --filter TagConfigCsvReaderTierTests`
Expected: PASS. If a column index assumption was wrong, fix the fixture to match the parser and re-run — do not change the parser.

- [ ] **Step 5: Commit**

```bash
git add StingTools.Tags.Tests/TagConfigCsvReaderTierTests.cs StingTools.Tags.Tests/StingTools.Tags.Tests.csproj
git commit -m "test: pin TagConfigCsvReader tier-plan parsing"
```

---

## Task 4: TierRowCloner engine (CLONE path) — mode-plan-driven  ⚠️ SUPERSEDED (labels are API-opaque `TextElement`s; do not implement)

The Revit-dependent engine. **It creates physical rows only** — it does NOT bind params or write formulas (that is `FamilyLabelAuthor.AuthorLabelsMulti`'s job, run immediately after in Task 6). For one family document it: (1) computes the required content-param set = **union of `TierRow.Parameter` across all mode plans** for that family; (2) clones tier-1's dimension once per still-missing param, stacked downward; (3) binds each clone's `FamilyLabel` to its content param. Idempotent: skip params that already have a bound dimension.

> **Depends on the spike verdict.** Task 1 confirms whether a `FamilyLabel` binds to the **content param directly** (assumed here) or to a **per-row display container** that the formula feeds. If the spike shows the latter, the `target` resolution below changes to the display-container name — the clone + offset mechanics are unaffected.

**Files:**
- Create: `StingTools/Tags/TierRowCloner.Revit.cs` (the Revit-dependent `TierRowCloner` class; NOT compile-linked into the test project)

- [ ] **Step 1: Implement the engine**

`StingTools/Tags/TierRowCloner.Revit.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using StingTools.Core;

namespace StingTools.Tags
{
    /// <summary>
    /// Materializes one physical dimension-label row per required content param
    /// (union across ALL tag modes) by cloning the tier-1 dimension and rebinding
    /// each copy. Creates VISIBLE rows only — mode/depth formulas are applied
    /// afterward by FamilyLabelAuthor.AuthorLabelsMulti. Closes the Revit-API gap.
    /// </summary>
    public static partial class TierRowCloner   // RequiredParamsForFamily lives in TierRowCloner.cs (Task 2)
    {
        public sealed class Result
        {
            public int RowsCreated { get; set; }
            public int RowsAlreadyPresent { get; set; }
            public int RowsFailed { get; set; }
            public List<string> Warnings { get; } = new List<string>();
        }

        /// <param name="requiredParams">
        /// Ordered, de-duplicated content-param names this family must display,
        /// = union of TierRow.Parameter across every mode plan for this family.
        /// Caller builds this from TagConfigPlanResolver.LoadAllPerMode(doc).
        /// </param>
        public static Result EnsureRows(Document fdoc, IReadOnlyList<string> requiredParams,
            double pitchMm = TierRowGeometry.DefaultPitchMm)
        {
            var result = new Result();
            if (!fdoc.IsFamilyDocument)
            { result.Warnings.Add("Not a family document."); return result; }

            var dims = new FilteredElementCollector(fdoc).OfClass(typeof(Dimension)).Cast<Dimension>().ToList();

            Dimension tier1 = dims.FirstOrDefault(d => d.FamilyLabel?.Definition?.Name == ParamRegistry.TAG1);
            if (tier1 == null)
            { result.Warnings.Add($"No tier-1 dimension bound to {ParamRegistry.TAG1}; cannot clone."); return result; }

            var boundParamNames = new HashSet<string>(
                dims.Select(d => d.FamilyLabel?.Definition?.Name).Where(n => n != null));

            using (var tx = new Transaction(fdoc, "STING Tier Rows — clone per content param"))
            {
                tx.Start();
                int rowIndex = 1; // tier-1 occupies slot 1; clones stack below
                foreach (string paramName in requiredParams)
                {
                    if (paramName == ParamRegistry.TAG1) continue;
                    if (boundParamNames.Contains(paramName)) { result.RowsAlreadyPresent++; continue; }

                    FamilyParameter fp = fdoc.FamilyManager.get_Parameter(paramName);
                    if (fp == null) { result.Warnings.Add($"Row {paramName}: param not bound; run AuthorLabelsMulti bind pass first."); result.RowsFailed++; continue; }

                    try
                    {
                        rowIndex++;
                        double dz = -(rowIndex - 1) * pitchMm / 304.8; // stack downward, mm→ft
                        ICollection<ElementId> ids = ElementTransformUtils.CopyElements(fdoc, new[] { tier1.Id }, new XYZ(0, dz, 0));
                        Dimension copy = ids.Count == 1 ? fdoc.GetElement(ids.First()) as Dimension : null;
                        if (copy == null) { result.Warnings.Add($"Row {paramName}: copy did not yield a single dimension."); result.RowsFailed++; continue; }
                        copy.FamilyLabel = fp;
                        result.RowsCreated++;
                    }
                    catch (Exception ex)
                    {
                        result.Warnings.Add($"Row {paramName}: {ex.Message}");
                        result.RowsFailed++;
                    }
                }
                tx.Commit();
            }

            StingLog.Info($"TierRowCloner {fdoc.Title}: created={result.RowsCreated} present={result.RowsAlreadyPresent} failed={result.RowsFailed}");
            return result;
        }
    }
}
```

> `RequiredParamsForFamily` is the partial-class half defined in the Revit-free `TierRowCloner.cs` (Task 2) — do not redefine it here.

- [ ] **Step 2: Build**

Run: `dotnet build StingTools/StingTools.csproj -p:RevitApiPath="C:\Program Files\Autodesk\Revit 2025"`
Expected: PASS (0 errors). Fix any API signature mismatch (`get_Parameter`, `ElementTransformUtils.CopyElements` overload) against the referenced Revit version before continuing. Confirm `TierPlan.T4Rows..T10Rows` property names against `PerFamilyTierMap.cs:38-70`.

- [ ] **Step 3: Run the pure-logic tests still green**

Run: `dotnet test StingTools.Tags.Tests/StingTools.Tags.Tests.csproj`
Expected: PASS (Tasks 2–3 tests unaffected; the Revit-dependent file is not linked into the test project).

- [ ] **Step 4: Commit**

```bash
git add StingTools/Tags/TierRowCloner.Revit.cs
git commit -m "feat: TierRowCloner — clone tier-1 dimension-label into tiers 2..10"
```

---

## Task 5: TierCoverageAuditor (read-only report)

Report, per family in a folder, how many tier rows are bound/visible — so the user can see coverage before and after applying. Read-only, no transactions.

**Files:**
- Create: `StingTools/Tags/TierCoverageAuditor.cs`

- [ ] **Step 1: Implement the auditor**

```csharp
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using StingTools.Core;

namespace StingTools.Tags
{
    public static class TierCoverageAuditor
    {
        public sealed class FamilyCoverage
        {
            public string Family { get; set; }
            public int RequiredRows { get; set; }        // union of content params across modes
            public int RowsWithVisibleLabel { get; set; } // required params that have a bound dimension
            public int ParamsBound { get; set; }          // required params present on the family
            public List<string> MissingRows { get; } = new List<string>();
        }

        /// <param name="requiredParams">
        /// TierRowCloner.RequiredParamsForFamily(family, perMode) — the content
        /// params this family must display across all modes.
        /// </param>
        public static FamilyCoverage Audit(Document fdoc, IReadOnlyList<string> requiredParams)
        {
            var cov = new FamilyCoverage { Family = fdoc.Title, RequiredRows = requiredParams.Count };
            if (!fdoc.IsFamilyDocument) return cov;

            var boundLabelParams = new HashSet<string>(
                new FilteredElementCollector(fdoc).OfClass(typeof(Dimension)).Cast<Dimension>()
                    .Select(d => d.FamilyLabel?.Definition?.Name).Where(n => n != null));

            var famParamNames = new HashSet<string>(
                fdoc.FamilyManager.Parameters.Cast<FamilyParameter>().Select(p => p.Definition.Name));

            foreach (string p in requiredParams)
            {
                if (famParamNames.Contains(p)) cov.ParamsBound++;
                if (boundLabelParams.Contains(p)) cov.RowsWithVisibleLabel++;
                else cov.MissingRows.Add(p);
            }
            return cov;
        }
    }
}
```

- [ ] **Step 2: Build**

Run: `dotnet build StingTools/StingTools.csproj -p:RevitApiPath="C:\Program Files\Autodesk\Revit 2025"`
Expected: PASS.

- [ ] **Step 3: Commit**

```bash
git add StingTools/Tags/TierCoverageAuditor.cs
git commit -m "feat: TierCoverageAuditor — per-family tier-row coverage report"
```

---

## Task 6: Commands + wire into family generation

Surface two commands and make new-family generation call the cloner so future families are born complete.

**Files:**
- Create: `StingTools/Tags/TagTierCommands.cs`
- Modify: `StingTools/Tags/TagFamilyCreatorCommand.cs` (in `CreateTagFamiliesCommand.Execute`, after the existing `TryRebindLabel(famDoc)` call, add the cloner call)

- [ ] **Step 1: Implement the commands**

`StingTools/Tags/TagTierCommands.cs`:

```csharp
using System.IO;
using System.Linq;
using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Core;

namespace StingTools.Tags
{
    /// <summary>Retrofit all tier/mode rows into every tag .rfa in a chosen folder.</summary>
    [Transaction(TransactionMode.Manual)]
    public class TierAutomationApplyCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData c, ref string message, ElementSet e)
        {
            Document projDoc = c.Application.ActiveUIDocument?.Document;
            if (projDoc == null) { TaskDialog.Show("Tier Automation", "Open the project first (needed to resolve mode plans)."); return Result.Cancelled; }
            var app = c.Application.Application;
            string dir = TagFamilyConfig.GetOutputDirectory();
            if (!Directory.Exists(dir)) { TaskDialog.Show("Tier Automation", $"No family folder: {dir}"); return Result.Cancelled; }

            // Resolve ALL modes once from the project (DC + Handover + Custom).
            Dictionary<string, Dictionary<string, TierPlan>> perMode = TagConfigPlanResolver.LoadAllPerMode(projDoc);
            string sharedParamFile = StingToolsApp.FindDataFile("MR_PARAMETERS.txt");

            int fams = 0, rows = 0;
            var sb = new StringBuilder();
            foreach (string rfa in Directory.GetFiles(dir, "*.rfa"))
            {
                Document fdoc = null;
                string famName = Path.GetFileNameWithoutExtension(rfa);
                try
                {
                    fdoc = app.OpenDocumentFile(rfa);

                    // 1) Build one ModePlan per mode that carries this family, then
                    //    bind params + write mode×depth formulas via the existing author.
                    var modePlans = new List<FamilyLabelAuthor.ModePlan>();
                    foreach (var m in perMode)
                        if (m.Value.TryGetValue(famName, out TierPlan plan))
                            modePlans.Add(new FamilyLabelAuthor.ModePlan {
                                Mode = m.Key,
                                GateParam = HandoverModeHelper.ModeSelectorBool.TryGetValue(m.Key, out var g) ? g : null,
                                Plan = plan });

                    if (modePlans.Count > 0)
                        FamilyLabelAuthor.AuthorLabelsMulti(fdoc, modePlans,
                            new FamilyLabelAuthor.Options { App = app, SharedParamFile = sharedParamFile, PreserveHandEdits = true, FamilyName = famName });

                    // 2) Materialize the physical rows (params are now bound).
                    var required = TierRowCloner.RequiredParamsForFamily(famName, perMode);
                    var res = TierRowCloner.EnsureRows(fdoc, required);

                    fams++; rows += res.RowsCreated;
                    fdoc.SaveAs(rfa, new SaveAsOptions { OverwriteExistingFile = true });
                    sb.AppendLine($"{famName}: need {required.Count} rows → +{res.RowsCreated} (present {res.RowsAlreadyPresent}, failed {res.RowsFailed})");
                }
                catch (System.Exception ex) { sb.AppendLine($"{famName}: ERROR {ex.Message}"); StingLog.Error("TierAutomationApply", ex); }
                finally { fdoc?.Close(false); }
            }
            TaskDialog.Show("Tier Automation", $"Families: {fams}  Rows created: {rows}\n\n{sb}");
            return Result.Succeeded;
        }
    }

    [Transaction(TransactionMode.ReadOnly)]
    public class TierAutomationAuditCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData c, ref string message, ElementSet e)
        {
            Document projDoc = c.Application.ActiveUIDocument?.Document;
            if (projDoc == null) { TaskDialog.Show("Tier Coverage Audit", "Open the project first (needed to resolve mode plans)."); return Result.Cancelled; }
            var app = c.Application.Application;
            string dir = TagFamilyConfig.GetOutputDirectory();
            Dictionary<string, Dictionary<string, TierPlan>> perMode = TagConfigPlanResolver.LoadAllPerMode(projDoc);
            var sb = new StringBuilder();
            foreach (string rfa in Directory.GetFiles(dir, "*.rfa"))
            {
                Document fdoc = null;
                string famName = Path.GetFileNameWithoutExtension(rfa);
                try
                {
                    fdoc = app.OpenDocumentFile(rfa);
                    var required = TierRowCloner.RequiredParamsForFamily(famName, perMode);
                    var cov = TierCoverageAuditor.Audit(fdoc, required);
                    sb.AppendLine($"{famName}: rows {cov.RowsWithVisibleLabel}/{cov.RequiredRows}, params {cov.ParamsBound}/{cov.RequiredRows}, missing [{string.Join(",", cov.MissingRows)}]");
                }
                catch (System.Exception ex) { sb.AppendLine($"{famName}: ERROR {ex.Message}"); }
                finally { fdoc?.Close(false); }
            }
            TaskDialog.Show("Tier Coverage Audit", sb.ToString());
            return Result.Succeeded;
        }
    }
}
```

> **Ordering (already encoded in the code above):** `EnsureRows` can only set `FamilyLabel` on params that are bound, so `AuthorLabelsMulti` (bind + mode×depth formulas) runs **first**, then `EnsureRows` materializes the physical rows. `perMode` is resolved once from the active project (`LoadAllPerMode` needs a project `Document`, not a family doc). `HandoverModeHelper.ModeSelectorBool` maps mode name → gate param (`HandoverModeHelper.cs:31`). Confirm the exact `FamilyLabelAuthor.Options` property names against `FamilyLabelAuthor.cs:50-62` before building.

- [ ] **Step 2: Wire generation to auto-clone**

`CreateTagFamiliesCommand.Execute` already loads `perMode` (`TagConfigPlanResolver.LoadAllPerMode`, ~`:1160`) and authors via `AuthorLabelsMulti`. Immediately after that authoring call (params now bound + formulas written), add the row materialization so generated families get their physical rows too:

```csharp
// Close the visible-row gap: materialize one physical dimension-row per
// content param this family needs (union across all modes). Params are
// already bound by AuthorLabelsMulti above.
try
{
    var required = TierRowCloner.RequiredParamsForFamily(famName, perMode);
    TierRowCloner.EnsureRows(famDoc, required);
}
catch (System.Exception ex) { StingLog.Error("EnsureRows during CreateTagFamilies", ex); }
```

Use the family-name variable already in scope at that point (the survey shows `TagFamilyConfig.GetFamilyName(bic)` resolves it); match its local name.

- [ ] **Step 3: Register command tags**

Add `TierAutomation_Apply` → `TierAutomationApplyCommand` and `TierAutomation_Audit` → `TierAutomationAuditCommand` to the dispatcher (`UI/StingCommandHandler.cs`) and a CREATE-tab button pair in `UI/StingDockPanel.xaml` following the existing tag-command button pattern. (Match an existing `RunCommand<T>` entry for exact syntax.)

- [ ] **Step 4: Build**

Run: `dotnet build StingTools/StingTools.csproj -p:RevitApiPath="C:\Program Files\Autodesk\Revit 2025"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add StingTools/Tags/TagTierCommands.cs StingTools/Tags/TagFamilyCreatorCommand.cs StingTools/UI/StingCommandHandler.cs StingTools/UI/StingDockPanel.xaml
git commit -m "feat: TierAutomation Apply/Audit commands + auto-clone on family generation"
```

---

## Task 7: [VERIFY-IN-REVIT] End-to-end smoke test on real families

**Files:** none (runtime verification).

- [ ] **Step 1: Deploy**

Close Revit. Run `dotnet build StingTools/StingTools.csproj -c Release -p:RevitApiPath="C:\Program Files\Autodesk\Revit 2025"`. Copy `StingTools.dll` + deps to `C:\Dev\STING_PLACEMENT_GOLD`. Reopen Revit.

- [ ] **Step 2: Baseline audit**

Run `TierAutomation_Audit` against the tag-family folder. Expected: most families report `rows 1/N` (N = their required content-param count; only tier-1 currently has a visible row).

- [ ] **Step 3: Apply on a 3-family subset**

Copy 3 representative `.rfa`s (a duct tag, a door tag, a generic tag) to a scratch folder, point `GetOutputDirectory` there (or run apply on that folder), run `TierAutomation_Apply`. Expected dialog: each family reports `need N rows → +M` where N = its union of content params across DC + Handover (varies per family — a plumbing/HVAC tag needs more than a door tag). Confirm `failed 0` for each.

- [ ] **Step 4: Load one family into a project and verify depth + mode gating**

Load an applied family into a test project, place a tag.
- **Depth:** run `SetParagraphDepthCommand` at depth 1 → only tier-1 row shows; depth 5 → tiers 1–5 rows show; depth 10 → all rows show.
- **Mode (construction/handover):** with depth ≥ 5, run `SetPatternMode_DC` → the tag shows the Design/Construction content (Design intent, Performance, Material); run `SetPatternMode_Handover` → the same physical rows now show Commissioning / Cost / Carbon content; wrong-mode rows collapse to empty with no visible gaps.

Expected: rows appear/disappear per depth; content swaps per mode with no re-author; no gaps between visible rows.

- [ ] **Step 5: Re-audit + record result**

Run `TierAutomation_Audit` again. Expected: subset families now report the full row count with `missing []`. Record any families with `failed` rows and their warnings in this plan under `## Smoke Test Results`.

- [ ] **Step 6: Commit the results note**

```bash
git add docs/superpowers/plans/2026-07-03-tag-tier-automation.md
git commit -m "docs: record tier-automation smoke-test results"
```

---

## Task 8: Full rollout + changelog

- [ ] **Step 1: [VERIFY-IN-REVIT] Apply across all 206 families**

Back up the family folder (`git`-tracked or zip). Run `TierAutomation_Apply` over the full folder. Review the summary for `ERROR`/`failed` lines; triage any failures (usually a family missing tier-1 binding — bind first via the `FamilyLabelAuthor.AuthorLabels` pre-pass from Task 6 Step 1 note).

- [ ] **Step 2: Log the phase in CHANGELOG**

Append a `#### Completed (Phase 195 — Tag Tier Automation)` section to `docs/CHANGELOG.md` summarising: the cloner approach, the closed API gap, the two commands, the tests, and the smoke-test outcome. (Per CLAUDE.md, log work in CHANGELOG, not in CLAUDE.md.)

- [ ] **Step 3: Commit**

```bash
git add docs/CHANGELOG.md
git commit -m "docs: changelog — Phase 195 tag tier automation"
```

- [ ] **Step 4: STOP and report**

Per the phase-boundary protocol (memory `feedback-phase-boundary-protocol`): commit + push, then STOP and report results to the user before any further phase. Do not open a PR or merge without explicit approval.

---

## Self-Review Notes

- **Spike-gated:** Task 1 decides CLONE vs TEMPLATE before any real code — the plan does not assume the API workaround succeeds.
- **Idempotency:** `EnsureRows` skips content params that already have a bound dimension, so re-running is safe (matches the "self-heal" ethos in `FamilyLabelAuthor`).
- **Reuse over rebuild:** binding + gate formulas + CSV tier content + depth driver are all reused (`FamilyLabelAuthor`, `TagConfigCsvReader`, `SetParagraphDepthCommand`); only the visible-row creation and two commands are new.
- **Testable surface is honest:** only the Revit-free math + CSV parsing are unit-tested (Tasks 2–3); Revit-runtime behaviour is explicitly `[VERIFY-IN-REVIT]`, not faked.
- **Open risk to confirm in Task 1:** copied dimensions carry geometric references (reference planes/lines). If a copy's references are invalid or shared such that repositioning distorts tier 1, the CLONE path fails → TEMPLATE fallback (Task 1b). This is the single largest uncertainty and is deliberately resolved first.
- **Column-index assumption (Task 3):** the CSV column map is from a code survey, not verified line-by-line; Task 3 Step 2 requires confirming indices against `TagConfigCsvReader.cs` before asserting.
- **Modes are reused, not rebuilt:** construction/handover (DC / Handover / Custom) is handled entirely by the existing `AuthorLabelsMulti` OR-merged formulas + `SetPatternMode_*` runtime flip. The new code only *creates physical rows* sized to the cross-mode param union; it writes no mode logic. Task 7 Step 4 verifies the mode switch end-to-end.
- **Row-count is per-family, not fixed:** because a tier is a group of rows and modes add distinct content, physical row count varies per family (union across modes). Any place that assumed "9 rows" or "N/10" was corrected to be param-driven.
