// StingTools — AEC/FM Filter Library commands
//
// Three commands for the corporate filter library:
//   * AecFiltersCreate — mints every definition in STING_AEC_FILTERS.json
//                       (and project override) as a ParameterFilterElement
//                       in the active document. Idempotent — already-
//                       existing filters are left untouched.
//   * AecFiltersInspect — read-only diagnostic listing every definition,
//                         which ones are present in the document, and
//                         the validation outcome (categories filterable,
//                         shared params bound, etc.).
//   * AecFiltersReload — clears the registry cache so edits to the JSON
//                        on disk take effect without relaunching Revit.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Core;
using StingTools.Core.Drawing;

namespace StingTools.Commands.Drawing
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class AecFiltersCreateCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string msg, ElementSet els)
        {
            var ctx = ParameterHelpers.GetContext(data);
            if (ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }
            var doc = ctx.Doc;

            var lib = AecFilterRegistry.GetLibrary(doc);
            if (lib?.Filters == null || lib.Filters.Count == 0)
            {
                TaskDialog.Show("STING - AEC Filters",
                    "No filter definitions found.\n\nExpected STING_AEC_FILTERS.json under the data/ directory.");
                return Result.Failed;
            }

            int created = 0, existing = 0, failed = 0;
            var warnings = new List<string>();
            var errors   = new List<string>();

            using (var tx = new Transaction(doc, "STING Create AEC Filters"))
            {
                tx.Start();
                foreach (var def in lib.Filters)
                {
                    if (string.IsNullOrWhiteSpace(def?.Name)) continue;
                    var r = AecFilterFactory.FindOrCreate(doc, def);
                    if (!r.Ok) { failed++; if (!string.IsNullOrEmpty(r.Error)) errors.Add($"{def.Id}: {r.Error}"); continue; }
                    if (r.Created) created++; else existing++;
                    foreach (var w in r.Warnings) warnings.Add($"{def.Id}: {w}");
                }
                tx.Commit();
            }

            // Caches must be invalidated so subsequent ViewStylePack applies
            // see the freshly-minted filters.
            ViewStylePackApplier.InvalidateCache(doc);

            var sb = new StringBuilder();
            sb.AppendLine($"Library: {lib.Filters.Count} filter definitions");
            sb.AppendLine($"  • created : {created}");
            sb.AppendLine($"  • existed : {existing}");
            sb.AppendLine($"  • failed  : {failed}");
            if (warnings.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine($"Warnings ({warnings.Count}):");
                foreach (var w in warnings.Take(40)) sb.AppendLine("  · " + w);
                if (warnings.Count > 40) sb.AppendLine($"  ... +{warnings.Count - 40} more (see StingTools.log)");
            }
            if (errors.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine($"Errors ({errors.Count}):");
                foreach (var e in errors.Take(20)) sb.AppendLine("  ✗ " + e);
                if (errors.Count > 20) sb.AppendLine($"  ... +{errors.Count - 20} more (see StingTools.log)");
            }

            foreach (var w in warnings) StingLog.Warn($"AecFiltersCreate: {w}");
            foreach (var e in errors)   StingLog.Error($"AecFiltersCreate: {e}");

            TaskDialog.Show("STING - AEC Filters", sb.ToString());
            return Result.Succeeded;
        }
    }

    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class AecFiltersInspectCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string msg, ElementSet els)
        {
            var ctx = ParameterHelpers.GetContext(data);
            if (ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }
            var doc = ctx.Doc;

            var lib = AecFilterRegistry.GetLibrary(doc);
            if (lib?.Filters == null || lib.Filters.Count == 0)
            {
                TaskDialog.Show("STING - AEC Filters", "No filter definitions found.");
                return Result.Failed;
            }

            // Build lookup of existing filters in this doc.
            var existingNames = new HashSet<string>(
                new FilteredElementCollector(doc)
                    .OfClass(typeof(ParameterFilterElement))
                    .Cast<ParameterFilterElement>()
                    .Select(f => f.Name),
                StringComparer.OrdinalIgnoreCase);

            int present = lib.Filters.Count(f => existingNames.Contains(f.Name));
            int missing = lib.Filters.Count - present;

            // Tag breakdown.
            var byTag = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var f in lib.Filters)
                if (f.Tags != null)
                    foreach (var t in f.Tags)
                        byTag[t] = byTag.TryGetValue(t, out var c) ? c + 1 : 1;

            var sb = new StringBuilder();
            sb.AppendLine($"AEC Filter Library — schemaUri: {lib.SchemaUri}");
            sb.AppendLine($"Total definitions : {lib.Filters.Count}");
            sb.AppendLine($"Present in doc    : {present}");
            sb.AppendLine($"Missing in doc    : {missing}");
            sb.AppendLine();
            sb.AppendLine("Top tag groups:");
            foreach (var kv in byTag.OrderByDescending(p => p.Value).Take(15))
                sb.AppendLine($"  {kv.Key,-18} {kv.Value,4}");

            if (missing > 0)
            {
                sb.AppendLine();
                sb.AppendLine("Missing — first 25:");
                foreach (var f in lib.Filters.Where(f => !existingNames.Contains(f.Name)).Take(25))
                    sb.AppendLine($"  · [{f.Id}] {f.Name}");
                if (missing > 25) sb.AppendLine($"  ... +{missing - 25} more.");
                sb.AppendLine();
                sb.AppendLine("Run 'AEC Filters: Create' to mint them in this document.");
            }

            TaskDialog.Show("STING - AEC Filters Inspect", sb.ToString());
            return Result.Succeeded;
        }
    }

    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class AecFiltersReloadCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string msg, ElementSet els)
        {
            var ctx = ParameterHelpers.GetContext(data);
            if (ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }
            AecFilterRegistry.Reload(ctx.Doc);
            ViewStylePackApplier.InvalidateCache(ctx.Doc);
            TaskDialog.Show("STING - AEC Filters",
                "Filter library cache cleared.\n\nNext call to AecFilterRegistry will re-read STING_AEC_FILTERS.json from disk.");
            return Result.Succeeded;
        }
    }
}
