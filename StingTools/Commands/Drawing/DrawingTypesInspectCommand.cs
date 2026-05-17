// StingTools — Drawing Template Manager
//
// Read-only diagnostic that prints a summary of the resolved Drawing
// Type library plus validation results for every type. Useful while
// the full editor UI is being built — lets users (and reviewers)
// confirm the routing table covers the disciplines they care about
// and that every corporate type's referenced assets are loaded.
//
// Also ships DrawingTypesReload, a zero-side-effect command that
// clears the registry cache so edits to STING_DRAWING_TYPES.json or
// the project's _BIM_COORD/drawing_types.json take effect without
// relaunching Revit.

using System;
using System.Linq;
using System.Text;
using System.Collections.Generic;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Core;
using StingTools.Core.Drawing;
using ValidationSeverity = StingTools.Core.Drawing.ValidationSeverity;

namespace StingTools.Commands.Drawing
{
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class DrawingTypesInspectCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string msg, ElementSet els)
        {
            try
            {
                var doc = data?.Application?.ActiveUIDocument?.Document;
                var lib = DrawingTypeRegistry.GetLibrary(doc);
                var reports = DrawingTypeValidator.ValidateAll(doc);

                var sb = new StringBuilder();
                sb.AppendLine($"STING Drawing Type Library — v{lib.Version}");
                sb.AppendLine($"Types: {lib.DrawingTypes.Count}   Routing rules: {lib.Routing.Count}");
                sb.AppendLine();

                // Types table
                sb.AppendLine("ID                                    Purpose        Disc  Paper  Scale  Origin");
                sb.AppendLine("────────────────────────────────────── ───────────── ───── ────── ─────  ───────");
                foreach (var t in lib.DrawingTypes.OrderBy(t => t.Discipline).ThenBy(t => t.Purpose))
                {
                    sb.AppendLine(string.Format(
                        "{0,-38} {1,-13} {2,-5} {3,-6} 1:{4,-4} {5}",
                        Truncate(t.Id, 38), Truncate(t.Purpose, 13),
                        t.Discipline ?? "*", t.PaperSize ?? "?",
                        t.Scale, t.Origin ?? ""));
                }
                sb.AppendLine();

                // Routing coverage
                sb.AppendLine("Routing rules (first match wins):");
                foreach (var r in lib.Routing)
                {
                    sb.AppendLine($"  {r.Discipline,-3} / {r.Phase,-12} / {r.DocType,-12}  →  {r.DrawingTypeId}");
                }
                sb.AppendLine();

                // Title-block readiness — fast pre-flight summary of which
                // title-block families profiles reference vs. what's loaded
                // in the project, plus whether the TitleBlockRouter has a
                // discipline default configured. Surfaces the silent
                // "first available" fallback risk in one place.
                AppendTitleBlockReadiness(doc, lib, sb);

                // Validation summary
                int errors   = reports.Sum(r => r.Issues.Count(i => i.Severity == ValidationSeverity.Error));
                int warnings = reports.Sum(r => r.Issues.Count(i => i.Severity == ValidationSeverity.Warning));
                int infos    = reports.Sum(r => r.Issues.Count(i => i.Severity == ValidationSeverity.Info));
                sb.AppendLine($"Validation: {errors} error(s), {warnings} warning(s), {infos} info");

                // Drift summary (Week 4) — per-view check against profile
                try
                {
                    var drifts = DrawingDriftDetector.Scan(doc);
                    int actionable = drifts.Count(r => r.AnyActionable);
                    int suppressed = drifts.Count(r => r.AnySuppressed);
                    if (actionable == 0 && suppressed == 0)
                        sb.AppendLine("Drift:      0 view(s) out of sync with their profile");
                    else if (actionable == 0)
                        sb.AppendLine($"Drift:      0 actionable; {suppressed} view(s) have template-controlled fields (informational, no action required)");
                    else
                        sb.AppendLine($"Drift:      {actionable} view(s) drifted — run 'Sync Styles' to resync"
                            + (suppressed > 0 ? $"; {suppressed} additional view(s) have template-controlled fields (informational only)" : ""));

                    // E-2: list any DRIFT_SUPPRESSED_BY_TEMPLATE entries in a
                    // separate "Informational — no action required" section so
                    // users understand why SyncStyles will not touch them.
                    if (suppressed > 0)
                    {
                        sb.AppendLine();
                        sb.AppendLine("Informational — no action required (view template controls these fields):");
                        foreach (var r in drifts.Where(rr => rr.AnySuppressed).Take(10))
                        {
                            sb.AppendLine($"  {r.ViewName}  [{r.DrawingTypeId}]");
                            foreach (var s in r.Suppressed.Take(3))
                                sb.AppendLine($"     · {s}");
                        }
                    }
                }
                catch (Exception dex) { sb.AppendLine($"Drift scan failed: {dex.Message}"); }

                sb.AppendLine();
                foreach (var r in reports.Where(r => r.HasErrors || r.HasWarnings))
                {
                    sb.AppendLine($"  [{r.DrawingTypeId}]");
                    foreach (var i in r.Issues.Where(i => i.Severity != ValidationSeverity.Info))
                        sb.AppendLine($"    {i.Severity,-7} {i.Code}: {i.Message}");
                }

                // GAP-Q: surface Info-level issues (DT-050 / DT-057 / DT-061
                // / DT-137-NOSLOTS) in a collapsed section so authors get
                // a heads-up about non-blocking schema concerns without
                // confusing them with errors / warnings.
                var infoOnlyReports = reports
                    .Where(r => !r.HasErrors && !r.HasWarnings
                                && r.Issues.Any(i => i.Severity == ValidationSeverity.Info))
                    .ToList();
                if (infos > 0)
                {
                    sb.AppendLine();
                    sb.AppendLine($"Info ({infos} item{(infos == 1 ? "" : "s")} — non-blocking, profile-author hints):");
                    foreach (var r in infoOnlyReports.Take(20))
                    {
                        sb.AppendLine($"  [{r.DrawingTypeId}]");
                        foreach (var i in r.Issues.Where(i => i.Severity == ValidationSeverity.Info).Take(5))
                            sb.AppendLine($"    Info    {i.Code}: {i.Message}");
                    }
                    if (infoOnlyReports.Count > 20)
                        sb.AppendLine($"  …(+{infoOnlyReports.Count - 20} more)");
                }

                TaskDialog.Show("STING — Drawing Types", sb.ToString().Length > 10000
                    ? sb.ToString().Substring(0, 10000) + "\n…(truncated)"
                    : sb.ToString());
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("DrawingTypesInspect", ex);
                msg = ex.Message;
                return Result.Failed;
            }
        }

        private static string Truncate(string s, int len)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Length <= len ? s : s.Substring(0, len - 1) + "…";
        }

        private static void AppendTitleBlockReadiness(Document doc, DrawingTypeLibrary lib, StringBuilder sb)
        {
            if (doc == null || lib == null) return;
            try
            {
                var loadedFamilies = new System.Collections.Generic.HashSet<string>(
                    new FilteredElementCollector(doc)
                        .OfCategory(BuiltInCategory.OST_TitleBlocks)
                        .OfClass(typeof(FamilySymbol))
                        .Cast<FamilySymbol>()
                        .Select(fs => fs.FamilyName ?? ""),
                    StringComparer.OrdinalIgnoreCase);

                var referenced = lib.DrawingTypes
                    .Where(t => !string.IsNullOrWhiteSpace(t.TitleBlockFamily))
                    .GroupBy(t => t.TitleBlockFamily, StringComparer.OrdinalIgnoreCase)
                    .OrderBy(g => g.Key)
                    .ToList();

                sb.AppendLine("Title-block readiness:");
                if (referenced.Count == 0)
                {
                    sb.AppendLine("  (no profiles declare a titleBlockFamily)");
                }
                else
                {
                    int missing = 0;
                    foreach (var grp in referenced)
                    {
                        bool loaded = loadedFamilies.Contains(grp.Key);
                        if (!loaded) missing++;
                        sb.AppendLine($"  {(loaded ? "✓" : "✗")} {grp.Key}  ({grp.Count()} profile{(grp.Count() == 1 ? "" : "s")})");
                    }
                    if (missing > 0)
                        sb.AppendLine($"  ⚠ {missing} family(ies) not loaded — sheets created from those profiles will fall back to the first available title block, and populated cells may silently drop.");
                }

                // TitleBlockRouter status
                var router = StingTools.Core.TitleBlockRouter.ByDiscipline ?? new System.Collections.Generic.Dictionary<string, ElementId>();
                int routed = router.Values.Count(id => id != ElementId.InvalidElementId);
                bool hasDefault = StingTools.Core.TitleBlockRouter.DefaultId != ElementId.InvalidElementId;
                sb.AppendLine($"  Router: {routed} discipline override(s); default {(hasDefault ? "set" : "UNSET")} (run Project Setup Wizard to configure).");

                // Parameter cardinality audit — dry-run Apply against every sheet
                // that already exists in the project to surface "param declared but
                // not on the family" mismatches without touching the model.
                AppendParamCardinalitySummary(doc, lib, sb);

                sb.AppendLine();
            }
            catch (Exception ex)
            {
                sb.AppendLine($"Title-block readiness check failed: {ex.Message}");
            }
        }

        private static void AppendParamCardinalitySummary(Document doc, DrawingTypeLibrary lib, StringBuilder sb)
        {
            if (doc == null || lib == null) return;
            try
            {
                // Collect all sheets that have a STING drawing-type stamp so we
                // can run a dry-run Apply against each — giving us which declared
                // param keys are absent from the loaded title-block family.
                var sheets = new FilteredElementCollector(doc)
                    .OfClass(typeof(ViewSheet))
                    .Cast<ViewSheet>()
                    .Where(s => !string.IsNullOrEmpty(StingTools.Core.Drawing.DrawingTypeStamper.Read(s)))
                    .ToList();

                if (sheets.Count == 0) return;

                var dryRun = new DrawingTypePresentation.ApplyOptions { DryRun = true };
                var allMissing = new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal);
                int totalDeclared = 0;

                foreach (var sheet in sheets)
                {
                    var dtId = StingTools.Core.Drawing.DrawingTypeStamper.Read(sheet);
                    var dt = DrawingTypeRegistry.Get(doc, dtId);
                    if (dt == null) continue;
                    try
                    {
                        var result = StingTools.Core.Drawing.TitleBlockParamApplier.Apply(
                            doc, sheet, dt, tokens: null, options: dryRun);
                        totalDeclared += result.ParametersDeclared;
                        foreach (var m in result.ParametersMissing)
                            allMissing.Add(m);
                    }
                    catch { /* per-sheet failure — continue */ }
                }

                if (allMissing.Count > 0)
                {
                    var sample = string.Join(", ", allMissing.OrderBy(k => k).Take(5));
                    sb.AppendLine($"  TB params: {totalDeclared} declared, {allMissing.Count} not found on family: {sample}"
                        + (allMissing.Count > 5 ? " …" : ""));
                }
            }
            catch { /* cardinality audit must never surface an error in a read-only diagnostic */ }
        }
    }

    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class DrawingTypesReloadCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string msg, ElementSet els)
        {
            try
            {
                var doc = data?.Application?.ActiveUIDocument?.Document;
                DrawingTypeRegistry.Reload(doc);
                var lib = DrawingTypeRegistry.GetLibrary(doc);
                TaskDialog.Show("STING — Drawing Types",
                    $"Reloaded — {lib.DrawingTypes.Count} type(s), {lib.Routing.Count} routing rule(s).");
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("DrawingTypesReload", ex);
                msg = ex.Message;
                return Result.Failed;
            }
        }
    }
}
