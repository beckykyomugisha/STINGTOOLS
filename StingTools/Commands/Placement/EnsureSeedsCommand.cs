// StingTools — Placement_EnsureSeeds command (Item 1).
//
// Stand-alone pre-pass: for every placement-rule category that has no
// manufacturer family loaded, build+load the mapped STING seed family so
// a subsequent run places a swap-ready default instead of silently
// skipping. Mirrors the seed convention used for tag families.
//
// Does NOT open a Revit transaction itself — SymbolLibraryCreator /
// Document.LoadFamily open their own implicit transactions, exactly like
// BuildSeedFamiliesCommand. Model-modifying — verify in Revit before merge.

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Core;
using StingTools.Core.Placement;
using StingTools.UI;

namespace StingTools.Commands.Placement
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class EnsureSeedsCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { message = "No active document."; return Result.Failed; }
            var doc = ctx.Doc;

            // Categories from the merged placement rule library. Each category
            // with no loaded family gets its mapped seed built+loaded.
            List<PlacementRule> rules;
            try { rules = PlacementRuleLoader.Load(doc.PathName); }
            catch (Exception ex)
            {
                StingLog.Error("EnsureSeedsCommand: rule load", ex);
                message = $"Could not load placement rules: {ex.Message}";
                return Result.Failed;
            }

            var cats = rules.Select(r => r?.CategoryFilter ?? "")
                            .Where(c => !string.IsNullOrWhiteSpace(c))
                            .Distinct(StringComparer.OrdinalIgnoreCase)
                            .ToList();
            if (cats.Count == 0)
            {
                TaskDialog.Show("STING — Ensure Seeds",
                    "No placement-rule categories found. Load a rule pack first.");
                return Result.Cancelled;
            }

            SeedEnsurer.SeedEnsureResult res;
            try { res = SeedEnsurer.EnsureSeedsForCategories(doc, cats); }
            catch (Exception ex)
            {
                StingLog.Error("EnsureSeedsCommand: EnsureSeeds", ex);
                message = $"Ensure-seeds failed: {ex.Message}";
                return Result.Failed;
            }

            try
            {
                var panel = StingResultPanel.Create("STING — Ensure Seed Families");
                panel.SetSubtitle($"{res.SeedsBuiltOrLoaded} built/loaded · " +
                                  $"{res.CategoriesAlreadyServed} already served · " +
                                  $"{res.CategoriesSeedless} seedless");
                panel.AddSection("SUMMARY")
                    .Metric("Rule categories",      cats.Count.ToString())
                    .Metric("Seeds built/loaded",   res.SeedsBuiltOrLoaded.ToString())
                    .Metric("Already had a family", res.CategoriesAlreadyServed.ToString())
                    .Metric("Seedless categories",  res.CategoriesSeedless.ToString());
                if (res.Messages.Count > 0)
                {
                    panel.AddSection("DETAIL");
                    foreach (var m in res.Messages.Take(40)) panel.Text(m);
                }
                panel.AddSection("NEXT STEPS")
                    .Text("Run placement — categories without a manufacturer family now place the seed default.")
                    .Text("Once procurement decides, run Placement › Swap to Manufacturer to swap seeds non-destructively.");
                panel.Show();
            }
            catch (Exception ex) { StingLog.Warn($"EnsureSeedsCommand panel: {ex.Message}"); }

            try { ActionAuditLog.Record("Placement_EnsureSeeds",
                $"cats={cats.Count} built={res.SeedsBuiltOrLoaded} served={res.CategoriesAlreadyServed}"); }
            catch (Exception ex) { StingLog.Warn($"audit: {ex.Message}"); }

            return Result.Succeeded;
        }
    }
}
