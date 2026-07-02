# Tag Tier Automation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Automatically populate tier rows 2–10 (containers `ASS_TAG_2_TXT … ASS_TAG_10_TXT`) as visible, depth-gated label rows across all 206 tag families, with **zero per-family manual Family-Editor work**, reusing the existing `FamilyLabelAuthor` tier pipeline.

**Architecture:** The heavy lifting already exists — `FamilyLabelAuthor.AuthorLabels/AuthorLabelsMulti` binds tier container params, applies the `if(TAG_PARA_STATE_N_BOOL, PARAM, "")` visibility formulas, and best-effort rebinds the primary label; `TagConfigCsvReader` parses per-family `TierPlan`s from the `STING_TAG_CONFIG_v5_0_*.csv` files; `SetParagraphDepthCommand` drives depth 1–10 via the `TAG_PARA_STATE_N_BOOL` gates. The **only** missing capability is creating the *visible label rows* for tiers 2–10 inside a family that ships with one. STING implements tag labels as **`Dimension` elements with `FamilyLabel`** (`FamilyLabelAuthor.cs:416-427`), and the Revit API **can** copy dimensions. So the primary strategy is: **clone the tier-1 dimension-label N times, offset each vertically, and rebind each copy to its tier container param** — all via the API, retrofitting the existing 206 families in place. If the clone proves unstable (dimensions carry geometric references that may not survive copy), fall back to a bounded set of master template families (~the distinct entries in `CategoryTemplateMap`) authored once with 10 pre-placed rows, then regenerated automatically.

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
- `StingTools/Tags/TagFamilyCreatorCommand.cs:1144` (`CreateTagFamiliesCommand.Execute`) — after `TryRebindLabel`, call `TierRowCloner.EnsureTierRows(...)` so newly generated families get all rows (Task 6).

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
                    FamilyParameter t2 = fdoc.FamilyManager.get_Parameter(ParamRegistry.TAG2);
                    try { copy.FamilyLabel = t2; rebound = t2 != null; } catch (Exception ex) { report = ex.Message; }
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
SPIKE VERDICT (fill in): [ copied? / rebound? / row visible & repositioned? / references valid on reload? ]
DECISION: [ CLONE path (Task 2–5) | TEMPLATE fallback (Task 1b then 3–6) ]
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

## Task 2: Pure-logic tier-row geometry (CLONE path) — TDD

Extract the offset/label-plan math into a Revit-free static helper so it's unit-testable. The cloner (Task 4) calls it.

**Files:**
- Create: `StingTools/Tags/TierRowCloner.cs` (host file; add only the Revit-free `TierRowGeometry` type in this task)
- Test: `StingTools.Tags.Tests/TierRowGeometryTests.cs`
- Modify: `StingTools.Tags.Tests/StingTools.Tags.Tests.csproj`

- [ ] **Step 1: Write the failing test**

`StingTools.Tags.Tests/TierRowGeometryTests.cs`:

```csharp
using StingTools.Tags;
using Xunit;

public class TierRowGeometryTests
{
    [Theory]
    [InlineData(2, -3.0)]   // tier 2 sits 1 pitch (3 mm) below tier 1
    [InlineData(3, -6.0)]
    [InlineData(10, -27.0)] // tier 10 is 9 pitches below tier 1
    public void OffsetMm_is_pitch_times_tier_minus_one(int tier, double expectedMm)
        => Assert.Equal(expectedMm, TierRowGeometry.OffsetMm(tier, pitchMm: 3.0), 3);

    [Fact]
    public void TierParamName_maps_tier_to_container()
    {
        Assert.Equal("ASS_TAG_2_TXT", TierRowGeometry.TierParamName(2));
        Assert.Equal("ASS_TAG_10_TXT", TierRowGeometry.TierParamName(10));
    }

    [Theory]
    [InlineData(1, false)] // tier 1 already exists — never cloned
    [InlineData(2, true)]
    [InlineData(10, true)]
    [InlineData(11, false)] // out of range
    public void ShouldClone_is_true_only_for_tiers_2_to_10(int tier, bool expected)
        => Assert.Equal(expected, TierRowGeometry.ShouldClone(tier));
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test StingTools.Tags.Tests/StingTools.Tags.Tests.csproj --filter TierRowGeometryTests`
Expected: FAIL — `TierRowGeometry` does not exist / not compiled into the test project.

- [ ] **Step 3: Write minimal implementation**

`StingTools/Tags/TierRowCloner.cs`:

```csharp
using System.Globalization;

namespace StingTools.Tags
{
    /// <summary>Revit-free geometry + naming math for tier-row cloning. Unit-tested.</summary>
    public static class TierRowGeometry
    {
        public const int MinClonedTier = 2;
        public const int MaxTier = 10;
        public const double DefaultPitchMm = 3.0;

        public static bool ShouldClone(int tier) => tier >= MinClonedTier && tier <= MaxTier;

        /// <summary>Vertical offset (mm, negative = downward) of a tier row relative to tier 1.</summary>
        public static double OffsetMm(int tier, double pitchMm = DefaultPitchMm)
            => -(tier - 1) * pitchMm;

        public static string TierParamName(int tier)
            => "ASS_TAG_" + tier.ToString(CultureInfo.InvariantCulture) + "_TXT";
    }
}
```

Add to `StingTools.Tags.Tests/StingTools.Tags.Tests.csproj` inside the existing `<ItemGroup>` of `<Compile Include>` entries:

```xml
<Compile Include="..\StingTools\Tags\TierRowCloner.cs" Link="Tags\TierRowCloner.cs" />
```

> Note: `TierRowCloner.cs` will also contain the Revit-dependent `TierRowCloner` class (Task 4). To keep the test project Revit-free, guard that class with `#if !TESTHOST` or split the Revit-dependent class into a partial in a second file (`TierRowCloner.Revit.cs`) that the test csproj does **not** include. Use the second-file split — cleaner than compile symbols. Create `TierRowCloner.Revit.cs` in Task 4; the test project includes only `TierRowCloner.cs`.

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test StingTools.Tags.Tests/StingTools.Tags.Tests.csproj --filter TierRowGeometryTests`
Expected: PASS (all 8 cases).

- [ ] **Step 5: Commit**

```bash
git add StingTools/Tags/TierRowCloner.cs StingTools.Tags.Tests/TierRowGeometryTests.cs StingTools.Tags.Tests/StingTools.Tags.Tests.csproj
git commit -m "feat: Revit-free tier-row geometry helper + tests"
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

## Task 4: TierRowCloner engine (CLONE path)

The Revit-dependent engine: for one family document, clone tier-1's dimension-label into tiers 2–10, offset by `TierRowGeometry.OffsetMm`, and rebind each to `TierRowGeometry.TierParamName(tier)`. Idempotent: skip tiers whose param already has a bound dimension.

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
    /// Clones the tier-1 dimension-label into tiers 2..10 and rebinds each copy
    /// to its ASS_TAG_N_TXT container. Requires the tier params to already be
    /// bound (FamilyLabelAuthor.BindSharedParameters does this) — this class only
    /// creates the VISIBLE rows, closing the documented Revit-API gap.
    /// </summary>
    public static class TierRowCloner
    {
        public sealed class Result
        {
            public int RowsCreated { get; set; }
            public int RowsAlreadyPresent { get; set; }
            public int RowsFailed { get; set; }
            public List<string> Warnings { get; } = new List<string>();
        }

        public static Result EnsureTierRows(Document fdoc, double pitchMm = TierRowGeometry.DefaultPitchMm)
        {
            var result = new Result();
            if (!fdoc.IsFamilyDocument)
            { result.Warnings.Add("Not a family document."); return result; }

            var dims = new FilteredElementCollector(fdoc).OfClass(typeof(Dimension)).Cast<Dimension>().ToList();

            Dimension tier1 = dims.FirstOrDefault(d => d.FamilyLabel?.Definition?.Name == ParamRegistry.TAG1);
            if (tier1 == null)
            { result.Warnings.Add($"No tier-1 dimension bound to {ParamRegistry.TAG1}; cannot clone."); return result; }

            // Which tier params already have a bound dimension?
            var boundParamNames = new HashSet<string>(
                dims.Select(d => d.FamilyLabel?.Definition?.Name).Where(n => n != null));

            using (var tx = new Transaction(fdoc, "STING Tier Rows — clone 2..10"))
            {
                tx.Start();
                for (int tier = TierRowGeometry.MinClonedTier; tier <= TierRowGeometry.MaxTier; tier++)
                {
                    string paramName = TierRowGeometry.TierParamName(tier);
                    if (boundParamNames.Contains(paramName)) { result.RowsAlreadyPresent++; continue; }

                    FamilyParameter fp = fdoc.FamilyManager.get_Parameter(paramName);
                    if (fp == null) { result.Warnings.Add($"Tier {tier}: param {paramName} not bound; skipped."); result.RowsFailed++; continue; }

                    try
                    {
                        double dz = TierRowGeometry.OffsetMm(tier, pitchMm) / 304.8; // mm→ft
                        XYZ offset = new XYZ(0, dz, 0);
                        ICollection<ElementId> ids = ElementTransformUtils.CopyElements(fdoc, new[] { tier1.Id }, offset);
                        Dimension copy = ids.Count == 1 ? fdoc.GetElement(ids.First()) as Dimension : null;
                        if (copy == null) { result.Warnings.Add($"Tier {tier}: copy did not yield a single dimension."); result.RowsFailed++; continue; }
                        copy.FamilyLabel = fp;
                        result.RowsCreated++;
                    }
                    catch (Exception ex)
                    {
                        result.Warnings.Add($"Tier {tier}: {ex.Message}");
                        result.RowsFailed++;
                    }
                }
                tx.Commit();
            }

            StingLog.Info($"TierRowCloner {opts(fdoc)}: created={result.RowsCreated} present={result.RowsAlreadyPresent} failed={result.RowsFailed}");
            return result;
        }

        private static string opts(Document fdoc) => fdoc.Title ?? "(family)";
    }
}
```

- [ ] **Step 2: Build**

Run: `dotnet build StingTools/StingTools.csproj -p:RevitApiPath="C:\Program Files\Autodesk\Revit 2025"`
Expected: PASS (0 errors). Fix any API signature mismatch (`get_Parameter`, `ElementTransformUtils.CopyElements` overload) against the referenced Revit version before continuing.

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
            public int TiersWithVisibleRow { get; set; } // dimensions bound to ASS_TAG_1..10_TXT
            public int TiersWithBoundParam { get; set; } // family params ASS_TAG_1..10_TXT present
            public List<int> MissingRows { get; } = new List<int>();
        }

        public static FamilyCoverage Audit(Document fdoc)
        {
            var cov = new FamilyCoverage { Family = fdoc.Title };
            if (!fdoc.IsFamilyDocument) return cov;

            var boundLabelParams = new HashSet<string>(
                new FilteredElementCollector(fdoc).OfClass(typeof(Dimension)).Cast<Dimension>()
                    .Select(d => d.FamilyLabel?.Definition?.Name).Where(n => n != null));

            var famParamNames = new HashSet<string>(
                fdoc.FamilyManager.Parameters.Cast<FamilyParameter>().Select(p => p.Definition.Name));

            for (int tier = 1; tier <= TierRowGeometry.MaxTier; tier++)
            {
                string p = TierRowGeometry.TierParamName(tier);
                if (famParamNames.Contains(p)) cov.TiersWithBoundParam++;
                if (boundLabelParams.Contains(p)) cov.TiersWithVisibleRow++;
                else cov.MissingRows.Add(tier);
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
    /// <summary>Retrofit tier rows 2..10 into every tag .rfa in a chosen folder.</summary>
    [Transaction(TransactionMode.Manual)]
    public class TierAutomationApplyCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData c, ref string message, ElementSet e)
        {
            var app = c.Application.Application;
            string dir = TagFamilyConfig.GetOutputDirectory();
            if (!Directory.Exists(dir)) { TaskDialog.Show("Tier Automation", $"No family folder: {dir}"); return Result.Cancelled; }

            int fams = 0, rows = 0;
            var sb = new StringBuilder();
            foreach (string rfa in Directory.GetFiles(dir, "*.rfa"))
            {
                Document fdoc = null;
                try
                {
                    fdoc = app.OpenDocumentFile(rfa);
                    // Ensure tier params are bound first (reuse existing author), then clone rows.
                    var res = TierRowCloner.EnsureTierRows(fdoc);
                    fams++; rows += res.RowsCreated;
                    if (res.RowsCreated > 0)
                        fdoc.SaveAs(rfa, new SaveAsOptions { OverwriteExistingFile = true });
                    sb.AppendLine($"{Path.GetFileName(rfa)}: +{res.RowsCreated} (present {res.RowsAlreadyPresent}, failed {res.RowsFailed})");
                }
                catch (System.Exception ex) { sb.AppendLine($"{Path.GetFileName(rfa)}: ERROR {ex.Message}"); StingLog.Error("TierAutomationApply", ex); }
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
            var app = c.Application.Application;
            string dir = TagFamilyConfig.GetOutputDirectory();
            var sb = new StringBuilder();
            foreach (string rfa in Directory.GetFiles(dir, "*.rfa"))
            {
                Document fdoc = null;
                try
                {
                    fdoc = app.OpenDocumentFile(rfa);
                    var cov = TierCoverageAuditor.Audit(fdoc);
                    sb.AppendLine($"{Path.GetFileName(rfa)}: rows {cov.TiersWithVisibleRow}/10, params {cov.TiersWithBoundParam}/10, missing [{string.Join(",", cov.MissingRows)}]");
                }
                catch (System.Exception ex) { sb.AppendLine($"{Path.GetFileName(rfa)}: ERROR {ex.Message}"); }
                finally { fdoc?.Close(false); }
            }
            TaskDialog.Show("Tier Coverage Audit", sb.ToString());
            return Result.Succeeded;
        }
    }
}
```

> Note the ordering dependency: `TierRowCloner.EnsureTierRows` only creates rows for tier params that are **already bound**. For the retrofit-in-place path over the existing 206, precede the clone with a bind pass. Reuse `FamilyLabelAuthor.AuthorLabels(fdoc, plan, opts)` (which binds params + applies gate formulas) BEFORE `EnsureTierRows`. Wire that call in Step 1's apply loop by resolving the family's `TierPlan` via the same `TagConfigPlanResolver.LoadAll(doc)` path `CreateTagFamiliesCommand` uses (`TagFamilyCreatorCommand.cs:1144+`). If `TagConfigPlanResolver` requires a project `Document` rather than a family doc, load plans once from the active project before the folder loop and index by family name.

- [ ] **Step 2: Wire generation to auto-clone**

In `StingTools/Tags/TagFamilyCreatorCommand.cs`, inside `CreateTagFamiliesCommand.Execute`, immediately after the existing `TryRebindLabel(famDoc);` call (survey: near the per-family save, ~`:1144`+ region), add:

```csharp
// Close the visible-row gap: clone tier-1 label into tiers 2..10.
try { TierRowCloner.EnsureTierRows(famDoc); }
catch (System.Exception ex) { StingLog.Error("EnsureTierRows during CreateTagFamilies", ex); }
```

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

Run `TierAutomation_Audit` against the tag-family folder. Expected: most families report `rows 1/10` (the current single-tier state).

- [ ] **Step 3: Apply on a 3-family subset**

Copy 3 representative `.rfa`s (a duct tag, a door tag, a generic tag) to a scratch folder, point `GetOutputDirectory` there (or run apply on that folder), run `TierAutomation_Apply`. Expected dialog: `Rows created: 27` (9 per family) and per-family `+9`.

- [ ] **Step 4: Load one family into a project and verify depth gating**

Load an applied family into a test project, place a tag, run `SetParagraphDepthCommand` at depth 1 → only tier 1 shows; depth 5 → tiers 1–5 show; depth 10 → all rows show. Expected: rows appear/disappear per depth, correct container values in each row.

- [ ] **Step 5: Re-audit + record result**

Run `TierAutomation_Audit` again. Expected: subset families now `rows 10/10, missing []`. Record any families with `failed` rows and their warnings in this plan under `## Smoke Test Results`.

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
- **Idempotency:** `EnsureTierRows` skips tiers whose param already has a bound dimension, so re-running is safe (matches the "self-heal" ethos in `FamilyLabelAuthor`).
- **Reuse over rebuild:** binding + gate formulas + CSV tier content + depth driver are all reused (`FamilyLabelAuthor`, `TagConfigCsvReader`, `SetParagraphDepthCommand`); only the visible-row creation and two commands are new.
- **Testable surface is honest:** only the Revit-free math + CSV parsing are unit-tested (Tasks 2–3); Revit-runtime behaviour is explicitly `[VERIFY-IN-REVIT]`, not faked.
- **Open risk to confirm in Task 1:** copied dimensions carry geometric references (reference planes/lines). If a copy's references are invalid or shared such that repositioning distorts tier 1, the CLONE path fails → TEMPLATE fallback (Task 1b). This is the single largest uncertainty and is deliberately resolved first.
- **Column-index assumption (Task 3):** the CSV column map is from a code survey, not verified line-by-line; Task 3 Step 2 requires confirming indices against `TagConfigCsvReader.cs` before asserting.
