// StingTools v4 MVP — PlaceHangersCommand.
//
// Walks the current selection (or all MEP curves in the active view
// when the selection is empty), invokes HangerPlacementEngine.Plan,
// and — when a hanger family is available — calls FamilyInstance.Create
// at every candidate. Result parameters written to each instance:
//   STING_HANGER_HOST_ID       — host run ElementId (string)
//   STING_HANGER_ANCHOR_TXT    — CONCRETE_ANCHOR / BEAM_CLAMP / GENERIC
//   STING_HANGER_STRUT_LEN_MM  — rod length down to run
//   STING_HANGER_SPACING_MM    — from the spacing table
//   STING_HANGER_TRAPEZE_BOOL  — 1 when on a shared rack
//
// The command honours three outcome modes:
//   PREVIEW  — user picked "Preview" in the TaskDialog (or no family
//              is loaded) → DetailCurve crosshairs only
//   APPLY    — user picked "Apply" AND family resolver succeeded →
//              FamilyInstance.Create per candidate
//   FALLBACK → no suitable family loaded → switches to PREVIEW with
//              an explicit warning in the result panel

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Electrical;
using Autodesk.Revit.DB.Mechanical;
using Autodesk.Revit.DB.Plumbing;
using Autodesk.Revit.DB.Structure;
using Autodesk.Revit.UI;
using StingTools.Core;
using StingTools.Core.Calc;
using StingTools.UI;

namespace StingTools.Commands.Routing
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class PlaceHangersCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { message = "No active document."; return Result.Failed; }
            var doc   = ctx.Doc;
            var uidoc = ctx.UIDoc;

            var runs = CollectRuns(doc, uidoc);
            if (runs.Count == 0)
            {
                TaskDialog.Show("STING v4 — Place Hangers",
                    "No pipes / ducts / conduits / cable trays found in scope.\n\n" +
                    "Select runs in the view, or switch to a view showing the " +
                    "MEP network and re-run.");
                return Result.Cancelled;
            }

            var td = new TaskDialog("STING v4 — Place Hangers")
            {
                MainInstruction = "Preview or place?",
                MainContent =
                    $"{runs.Count} run(s) in scope.\n\n" +
                    "PREVIEW: DetailCurve crosshairs at every candidate + report.\n" +
                    "PLACE:   create FamilyInstances via HangerFamilyResolver.\n" +
                    "         Falls back to PREVIEW when no hanger family is loaded.",
                CommonButtons = TaskDialogCommonButtons.Cancel,
            };
            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Preview (dry run)");
            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "Place family instances");
            var choice = td.Show();
            if (choice != TaskDialogResult.CommandLink1 && choice != TaskDialogResult.CommandLink2)
                return Result.Cancelled;
            bool applyMode = (choice == TaskDialogResult.CommandLink2);

            HangerPlacementResult res;
            try
            {
                res = HangerPlacementEngine.Plan(doc, runs);
            }
            catch (Exception ex)
            {
                StingLog.Error("PlaceHangersCommand: plan failed", ex);
                message = ex.Message;
                return Result.Failed;
            }

            int placed  = 0;
            var binding = new Dictionary<string, HangerFamilyBinding>(StringComparer.OrdinalIgnoreCase);
            if (applyMode)
            {
                // Resolve families once per anchor type up-front so we
                // can detect the fall-back condition before opening the
                // placement transaction.
                foreach (var anchor in new[] { "CONCRETE_ANCHOR", "BEAM_CLAMP", "TRAPEZE", "GENERIC" })
                {
                    var b = HangerFamilyResolver.Resolve(doc, anchor);
                    if (b?.Symbol != null) binding[anchor] = b;
                }
                if (binding.Count == 0)
                {
                    applyMode = false;
                    res.Warnings.Add(
                        "No hanger family loaded in project (tried STING_HANGER_* " +
                        "+ Anvil/B-Line/Unistrut/Tolco/Caddy/Erico + any Generic " +
                        "Model with 'hanger' in name). Fell back to preview mode.");
                }
            }

            var view = doc.ActiveView;
            using (var tx = new Transaction(doc,
                applyMode ? "STING v4 Place hangers" : "STING v4 Hanger preview"))
            {
                try
                {
                    tx.Start();
                    if (applyMode)
                        placed = ApplyFamilyInstances(doc, res, binding);
                    else
                        DrawPreview(doc, view, res);
                    tx.Commit();
                }
                catch (Exception ex)
                {
                    if (tx.HasStarted() && !tx.HasEnded()) tx.RollBack();
                    StingLog.Error("PlaceHangersCommand transaction", ex);
                    message = ex.Message;
                    return Result.Failed;
                }
            }

            ShowResult(res, applyMode, placed, binding);
            return Result.Succeeded;
        }

        // ----- Placement ----------------------------------------------------

        private static int ApplyFamilyInstances(Document doc,
            HangerPlacementResult res,
            Dictionary<string, HangerFamilyBinding> binding)
        {
            int placed = 0;
            foreach (var c in res.Candidates)
            {
                if (c.Point == null) continue;

                HangerFamilyBinding b;
                if (!binding.TryGetValue(c.AnchorType ?? "GENERIC", out b) || b?.Symbol == null)
                    binding.TryGetValue("GENERIC", out b);
                if (b?.Symbol == null) continue;

                try
                {
                    if (!b.Symbol.IsActive) b.Symbol.Activate();

                    var fi = doc.Create.NewFamilyInstance(
                        c.Point, b.Symbol, StructuralType.NonStructural);
                    if (fi == null) continue;

                    TrySetString(fi, "STING_HANGER_HOST_ID",      c.HostRun?.Value.ToString() ?? "");
                    TrySetString(fi, "STING_HANGER_ANCHOR_TXT",   c.AnchorType ?? "GENERIC");
                    TrySetDouble(fi, "STING_HANGER_STRUT_LEN_MM", c.StrutRodMm);
                    TrySetDouble(fi, "STING_HANGER_SPACING_MM",   c.MaxSpanMm);
                    TrySetInt   (fi, "STING_HANGER_TRAPEZE_BOOL", c.OnTrapeze ? 1 : 0);
                    TrySetDouble(fi, "STING_HANGER_POINT_LOAD_KG", c.PointLoadKg);
                    TrySetDouble(fi, "STING_HANGER_ROD_DIA_MM",   c.RodDiameterMm);
                    TrySetString(fi, "STING_HANGER_ROD_IMPERIAL", c.RodImperial ?? "");
                    TrySetInt   (fi, "STING_HANGER_COUPLER_BOOL", c.RodNeedsCoupler ? 1 : 0);
                    placed++;
                }
                catch (Exception ex)
                {
                    res.Warnings.Add($"FamilyInstance.Create at {c.Point}: {ex.Message}");
                }
            }
            return placed;
        }

        private static void DrawPreview(Document doc, View view, HangerPlacementResult res)
        {
            if (!(view is ViewPlan) && !(view is ViewSection)) return;
            const double armFt = 0.5;
            foreach (var c in res.Candidates)
            {
                if (c.Point == null) continue;
                try
                {
                    var p = c.Point;
                    var h = Line.CreateBound(
                        new XYZ(p.X - armFt, p.Y, p.Z),
                        new XYZ(p.X + armFt, p.Y, p.Z));
                    var v = Line.CreateBound(
                        new XYZ(p.X, p.Y - armFt, p.Z),
                        new XYZ(p.X, p.Y + armFt, p.Z));
                    doc.Create.NewDetailCurve(view, h);
                    doc.Create.NewDetailCurve(view, v);
                }
                catch (Exception ex)
                { StingLog.Warn($"PlaceHangers: DetailCurve at {c.Point}: {ex.Message}"); }
            }
        }

        // ----- Helpers ------------------------------------------------------

        private static List<Element> CollectRuns(Document doc, Autodesk.Revit.UI.UIDocument uidoc)
        {
            var scope = new List<Element>();
            var sel = uidoc.Selection.GetElementIds();
            if (sel != null && sel.Count > 0)
            {
                foreach (var id in sel)
                {
                    var el = doc.GetElement(id);
                    if (el is Pipe || el is Duct || el is Conduit || el is CableTray)
                        scope.Add(el);
                }
            }
            else
            {
                var view = doc.ActiveView;
                if (view == null) return scope;
                foreach (Type t in new[] { typeof(Pipe), typeof(Duct), typeof(Conduit), typeof(CableTray) })
                {
                    var col = new FilteredElementCollector(doc, view.Id).OfClass(t);
                    foreach (var el in col) scope.Add(el);
                }
            }
            return scope;
        }

        private static void TrySetString(Element el, string param, string val)
        {
            try
            {
                var p = el.LookupParameter(param);
                if (p != null && !p.IsReadOnly && p.StorageType == StorageType.String) p.Set(val ?? "");
            }
            catch { }
        }
        private static void TrySetDouble(Element el, string param, double val)
        {
            try
            {
                var p = el.LookupParameter(param);
                if (p == null || p.IsReadOnly) return;
                if (p.StorageType == StorageType.Double) p.Set(val);
                else if (p.StorageType == StorageType.String)
                    p.Set(val.ToString("F1", System.Globalization.CultureInfo.InvariantCulture));
            }
            catch { }
        }
        private static void TrySetInt(Element el, string param, int val)
        {
            try
            {
                var p = el.LookupParameter(param);
                if (p == null || p.IsReadOnly) return;
                if (p.StorageType == StorageType.Integer) p.Set(val);
                else if (p.StorageType == StorageType.String) p.Set(val.ToString());
            }
            catch { }
        }

        // ----- Result UI ----------------------------------------------------

        private void ShowResult(HangerPlacementResult res, bool applied, int placed,
            Dictionary<string, HangerFamilyBinding> binding)
        {
            var panel = StingResultPanel.Create("v4 Hanger Placement");
            panel.SetSubtitle(applied ? $"Placed {placed} FamilyInstance(s)" : "Preview only");

            panel.AddSection("SUMMARY")
                 .Metric("Runs scanned",      res.RunsScanned.ToString())
                 .Metric("Candidates",        res.CandidatesGenerated.ToString())
                 .Metric("Concrete anchors",  res.ConcreteAnchorCount.ToString())
                 .Metric("Beam clamps",       res.BeamClampCount.ToString())
                 .Metric("Generic",           res.GenericCount.ToString())
                 .Metric("Trapeze groups",    res.TrapezeGroups.ToString())
                 .Metric("Placed",            placed.ToString());

            if (binding != null && binding.Count > 0)
            {
                panel.AddSection("RESOLVED FAMILIES");
                foreach (var kv in binding)
                    panel.Text($"{kv.Key,-18} → {kv.Value.Symbol?.FamilyName}  [{kv.Value.Tier}] {kv.Value.Notes}");
            }

            if (res.Warnings.Count > 0)
            {
                panel.AddSection("WARNINGS");
                foreach (var w in res.Warnings.Take(40)) panel.Text(w);
                if (res.Warnings.Count > 40) panel.Text($"(+{res.Warnings.Count - 40} more — see StingLog)");
            }

            if (res.Candidates.Count > 0)
            {
                panel.AddSection("PER-CANDIDATE (first 40)");
                foreach (var c in res.Candidates.Take(40))
                {
                    panel.Text($"#{c.HostRun.Value} {c.AnchorType} span={c.MaxSpanMm:F0}mm " +
                               $"load={c.PointLoadKg:F0}kg rod=M{c.RodDiameterMm:F0}({c.RodImperial})" +
                               $" util={c.RodUtilizationPct:F0}%" +
                               (c.RodNeedsCoupler ? " [coupler]" : "") +
                               (c.OnTrapeze ? " [trapeze]" : "") +
                               $"  — {c.SpacingBasis}");
                }
                if (res.Candidates.Count > 40)
                    panel.Text($"(+{res.Candidates.Count - 40} more)");
            }
            panel.Show();
        }
    }
}
