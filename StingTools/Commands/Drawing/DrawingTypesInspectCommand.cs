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
                var doc = (data?.Application ?? StingTools.UI.StingCommandHandler.CurrentApp)?.ActiveUIDocument?.Document;
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

                // Phase 183 — LiveProfileSync staged-diff summary. Shows
                // pack / profile edits since last load even when the live
                // views haven't drifted yet (e.g. a pack VG edit that
                // hasn't been re-applied to existing views).
                try
                {
                    var changedProfiles = LiveProfileSync.GetChangedProfileIds(doc);
                    var changedPacks = LiveProfileSync.GetChangedPackIds(doc);
                    var affected = LiveProfileSync.GetAffectedViewIds(doc);
                    if (changedProfiles.Count > 0 || changedPacks.Count > 0)
                    {
                        sb.AppendLine($"Live sync:  {changedProfiles.Count} profile(s) + {changedPacks.Count} pack(s) edited since last load — {affected.Count} view(s) affected. Run 'Sync Styles' to re-apply.");
                    }
                }
                catch (Exception lx) { sb.AppendLine($"Live sync scan failed: {lx.Message}"); }

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

                // The pre-flight's verdict: Error-severity findings fail the step (a
                // pre-flight that passes whatever it finds is not a gate).
                // Only errors in types this project uses fail it (PreflightScope): an error in
                // a type it never produces is reported, not blocking.
                var failingAll = reports.Where(r => r.HasErrors).Select(r => r.DrawingTypeId).ToList();
                var scope = PreflightScope.Split(failingAll, TypesInUse(doc));
                var failing = scope.Blocking;
                string verdict = $"Drawing-type pre-flight: {errors} error(s) in {failingAll.Count} drawing type(s), "
                               + $"{warnings} warning(s), {infos} info"
                               + (failing.Count > 0
                                   ? $" — {failing.Count} used by this project: {string.Join(", ", failing.Take(6))}{(failing.Count > 6 ? $" +{failing.Count - 6} more" : "")} (details in the STING log)"
                                   : "")
                               + (scope.Unused.Count > 0 ? $"; {scope.Unused.Count} in types this project does not produce (not blocking)" : "")
                               + ".";
                if (PresetDialog.Quiet)
                {
                    StingLog.Info("DrawingTypesInspect:\n" + sb);
                    msg = verdict;
                }
                else
                    TaskDialog.Show("STING — Drawing Types", sb.ToString().Length > 10000
                        ? sb.ToString().Substring(0, 10000) + "\n…(truncated)"
                        : sb.ToString());
                if (scope.Fails)
                {
                    msg = verdict;
                    StingLog.Warn(verdict);
                    return Result.Failed;
                }
                if (errors > 0) { msg = verdict; StingLog.Info(verdict); }
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("DrawingTypesInspect", ex);
                msg = ex.Message;
                return Result.Failed;
            }
        }

        /// <summary>
        /// Drawing types this project uses: stamped on any view or sheet, plus what its
        /// routing produces per level for the disciplines it models.
        /// </summary>
        private static IEnumerable<string> TypesInUse(Document doc)
        {
            var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                foreach (var v in new FilteredElementCollector(doc).OfClass(typeof(View)).Cast<View>())
                {
                    var id = ParameterHelpers.GetString(v, DrawingTypeStamper.PARAM_DRAWING_TYPE_ID);
                    if (!string.IsNullOrWhiteSpace(id)) ids.Add(id);
                }
            }
            catch (Exception ex) { StingLog.Warn($"DrawingTypesInspect stamped types: {ex.Message}"); }
            try
            {
                foreach (var t in BatchProduceCommons.RoutePerLevel(doc, DrawingProduceAndExportCommand.DisciplinesModelled(doc)).Types)
                    ids.Add(t.Id);
            }
            catch (Exception ex) { StingLog.Warn($"DrawingTypesInspect routed types: {ex.Message}"); }
            return ids;
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

                // DTW-12: a profile names a LOGICAL title block (STING_TB_SHEET_A1 …)
                // that is never loaded under that name, so comparing it with the
                // loaded families marked every one ✗. Resolve it the way the
                // producer and the Validator do: variant rules (+ "Family:Symbol"),
                // then the resolver's concrete built family.
                var referenced = lib.DrawingTypes
                    .Where(t => t != null && !string.IsNullOrWhiteSpace(t.Id))
                    .Select(t => DrawingTypeRegistry.Get(doc, t.Id) ?? t)
                    .Where(t => !string.IsNullOrWhiteSpace(t.TitleBlockFamily)
                                || (t.TitleBlockVariantRules?.Count ?? 0) > 0)
                    .Select(t => new { Dt = t, Fam = ConcreteTitleBlockFamily(doc, t) })
                    .Where(x => !string.IsNullOrWhiteSpace(x.Fam.concrete))
                    .GroupBy(x => x.Fam.concrete, StringComparer.OrdinalIgnoreCase)
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
                        bool onDisk = false;
                        if (!loaded)
                        {
                            try { onDisk = TitleBlockResolver.BuiltRfaExists(doc, grp.Key); }
                            catch (Exception ex) { StingLog.Warn($"DrawingTypesInspect built-rfa probe '{grp.Key}': {ex.Message}"); }
                            if (!onDisk) missing++;
                        }
                        var logical = grp.Select(x => x.Fam.declared)
                            .Where(d => !string.IsNullOrWhiteSpace(d) && !string.Equals(d, grp.Key, StringComparison.OrdinalIgnoreCase))
                            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                        string from = logical.Count > 0 ? $"  ← {string.Join(", ", logical.Take(3))}{(logical.Count > 3 ? " …" : "")}" : "";
                        string mark = loaded ? "✓" : onDisk ? "○" : "✗";
                        string note = !loaded && onDisk ? "  (built, loads on demand)" : "";
                        sb.AppendLine($"  {mark} {grp.Key}  ({grp.Count()} profile{(grp.Count() == 1 ? "" : "s")}){note}{from}");
                    }
                    if (missing > 0)
                        sb.AppendLine($"  ⚠ {missing} family(ies) neither loaded nor built — sheets created from those profiles will fall back to the first available title block, and populated cells may silently drop. Run TitleBlock_CreateAll.");
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

        /// <summary>DTW-12: the concrete title-block family a profile produces on,
        /// resolved as the Validator does (variant → resolver). <c>declared</c>
        /// is the name before resolution, for the "resolved from" note.</summary>
        private static (string declared, string concrete) ConcreteTitleBlockFamily(Document doc, DrawingType dt)
        {
            string declared = dt?.TitleBlockFamily;
            try { declared = DrawingDispatcher.ResolveTitleBlockVariant(dt).family; }
            catch (Exception ex) { StingLog.Warn($"DrawingTypesInspect variant '{dt?.Id}': {ex.Message}"); }
            if (string.IsNullOrWhiteSpace(declared)) declared = dt?.TitleBlockFamily;
            string concrete = declared;
            try
            {
                var res = TitleBlockResolver.Resolve(doc, dt, declared);
                if (res.IsResolved) concrete = res.Family;
            }
            catch (Exception ex) { StingLog.Warn($"DrawingTypesInspect resolve '{dt?.Id}': {ex.Message}"); }
            return (declared, concrete);
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

                var allMissing = new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal);
                int totalDeclared = 0;

                // Collect unique DrawingType ids so we only call FindMissingProjectInfoParams once per type.
                var seenDtIds = new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal);
                foreach (var sheet in sheets)
                {
                    var dtId = StingTools.Core.Drawing.DrawingTypeStamper.Read(sheet);
                    var dt = DrawingTypeRegistry.Get(doc, dtId);
                    if (dt?.TitleBlockParams == null) continue;
                    totalDeclared += dt.TitleBlockParams.Count;
                    if (!seenDtIds.Add(dtId)) continue;
                    try
                    {
                        var missing = StingTools.Core.Drawing.TitleBlockParamApplier
                            .FindMissingProjectInfoParams(doc, dt);
                        foreach (var m in missing)
                            allMissing.Add(m);
                    }
                    catch { /* per-type failure — continue */ }
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
                var doc = (data?.Application ?? StingTools.UI.StingCommandHandler.CurrentApp)?.ActiveUIDocument?.Document;
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
