// StingTools — Design Options Audit.
//
// Three audits, one report:
//   1. Schedules without explicit option-visibility (silent
//      double-count risk on BOQs).
//   2. Tags placed in main-model views pointing at option-only
//      elements ("ghost" tags — visible in some option states only).
//   3. Sheets whose viewports source views that target the same option
//      but the sheet itself has no naming hint of which option — easily
//      lost in client packages.
//
// Read-only — produces a TaskDialog summary plus a CSV under the
// project's _BIM_COORD/audit/ directory.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Core;
using StingTools.Core.DesignOptions;

namespace StingTools.Commands.DesignOptions
{
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class AuditOptionsCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string msg, ElementSet els)
        {
            var ctx = ParameterHelpers.GetContext(data);
            if (ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }
            var doc = ctx.Doc;

            var sets = DesignOptionRegistry.Snapshot(doc);
            if (sets.Count == 0)
            {
                TaskDialog.Show("STING", "No design option sets in this document. Audit not applicable.");
                return Result.Cancelled;
            }

            // ── 1: schedules without option lock ─────────────────────────
            var schedAlerts = new List<string>();
            var schedules = new FilteredElementCollector(doc)
                .OfClass(typeof(ViewSchedule))
                .Cast<ViewSchedule>()
                .Where(s => !s.IsTemplate && !s.IsTitleblockRevisionSchedule)
                .ToList();
            foreach (var s in schedules)
            {
                try
                {
                    var p = s.get_Parameter(BuiltInParameter.VIEWER_OPTION_VISIBILITY);
                    var v = p?.AsElementId();
                    if (v == null || v == ElementId.InvalidElementId)
                        schedAlerts.Add(s.Name);
                }
                catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
            }

            // ── 2: tags in main-model views pointing at option-only elems ─
            var ghostTags = new List<string>();
            var allTags = new FilteredElementCollector(doc)
                .OfClass(typeof(IndependentTag))
                .Cast<IndependentTag>()
                .ToList();
            foreach (var tag in allTags)
            {
                try
                {
                    var view = doc.GetElement(tag.OwnerViewId) as View;
                    if (view == null) continue;
                    var vp = view.get_Parameter(BuiltInParameter.VIEWER_OPTION_VISIBILITY);
                    var viewOpt = vp?.AsElementId();
                    bool viewIsAutomatic = viewOpt == null || viewOpt == ElementId.InvalidElementId;

                    var taggedIds = tag.GetTaggedLocalElementIds();
                    if (taggedIds == null) continue;
                    foreach (var tid in taggedIds)
                    {
                        var el = doc.GetElement(tid);
                        var dopt = el?.DesignOption;
                        if (dopt == null) continue;
                        if (viewIsAutomatic && !dopt.IsPrimary)
                        {
                            ghostTags.Add($"{tag.Id} in '{view.Name}' → element {tid} (option {dopt.Name})");
                            break;
                        }
                        if (!viewIsAutomatic && viewOpt != dopt.Id)
                        {
                            ghostTags.Add($"{tag.Id} in '{view.Name}' (option {doc.GetElement(viewOpt)?.Name}) → element {tid} (option {dopt.Name})");
                            break;
                        }
                    }
                }
                catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
            }

            // ── 3: sheets without option hint in name ────────────────────
            var sheetAlerts = new List<string>();
            var sheets = new FilteredElementCollector(doc)
                .OfClass(typeof(ViewSheet))
                .Cast<ViewSheet>()
                .Where(sh => !sh.IsPlaceholder)
                .ToList();
            foreach (var sh in sheets)
            {
                try
                {
                    var vpIds = sh.GetAllViewports();
                    var optIds = new HashSet<ElementId>();
                    foreach (var vid in vpIds)
                    {
                        var vp = doc.GetElement(vid) as Viewport;
                        var v = vp == null ? null : doc.GetElement(vp.ViewId) as View;
                        var p = v?.get_Parameter(BuiltInParameter.VIEWER_OPTION_VISIBILITY);
                        var oid = p?.AsElementId();
                        if (oid != null && oid != ElementId.InvalidElementId) optIds.Add(oid);
                    }
                    if (optIds.Count == 1)
                    {
                        var only = optIds.First();
                        string oname = doc.GetElement(only)?.Name ?? "?";
                        if (!sh.Name.IndexOf(oname, StringComparison.OrdinalIgnoreCase).Equals(-1)) continue;
                        if (!sh.SheetNumber.IndexOf(oname, StringComparison.OrdinalIgnoreCase).Equals(-1)) continue;
                        sheetAlerts.Add($"{sh.SheetNumber} — {sh.Name} (locked to option '{oname}')");
                    }
                }
                catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
            }

            // ── Compose report ───────────────────────────────────────────
            var sb = new StringBuilder();
            sb.AppendLine($"Schedules without option lock : {schedAlerts.Count}");
            sb.AppendLine($"Ghost tags                    : {ghostTags.Count}");
            sb.AppendLine($"Sheets without option hint    : {sheetAlerts.Count}");
            sb.AppendLine();
            void Section(string title, List<string> list)
            {
                if (list.Count == 0) return;
                sb.AppendLine(title);
                foreach (var x in list.Take(15)) sb.AppendLine("  · " + x);
                if (list.Count > 15) sb.AppendLine($"  · … +{list.Count - 15} more");
                sb.AppendLine();
            }
            Section("Schedules without option lock:", schedAlerts);
            Section("Ghost tags:",                    ghostTags);
            Section("Sheets without option hint:",    sheetAlerts);

            // ── Persist CSV ──────────────────────────────────────────────
            try
            {
                string dir = Path.Combine(OutputLocationHelper.GetOutputDirectory(doc),
                                          "_BIM_COORD", "audit");
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                string path = Path.Combine(dir, $"design_options_audit_{DateTime.Now:yyyyMMdd_HHmmss}.csv");
                var csv = new StringBuilder();
                csv.AppendLine("category,detail");
                foreach (var x in schedAlerts) csv.AppendLine($"schedule_no_option,\"{x.Replace("\"", "\"\"")}\"");
                foreach (var x in ghostTags)   csv.AppendLine($"ghost_tag,\"{x.Replace("\"", "\"\"")}\"");
                foreach (var x in sheetAlerts) csv.AppendLine($"sheet_no_hint,\"{x.Replace("\"", "\"\"")}\"");
                File.WriteAllText(path, csv.ToString());
                sb.AppendLine($"CSV written to: {path}");
            }
            catch (Exception ex) { StingLog.Warn($"AuditOptions CSV: {ex.Message}"); }

            TaskDialog.Show("STING — Design Options Audit", sb.ToString());
            return Result.Succeeded;
        }
    }
}
