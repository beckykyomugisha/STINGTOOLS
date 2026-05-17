// StingTools — SLD riser diagram commands (Phase 177 + Phase 179 enhancements)
//
// Phase 179: DrawRiser / DrawBox / DrawFeeders extracted from
// SLDRiserDiagramCommand into the public static SLDRiserEngine class so
// SLDUpdateRiserCommand can call them directly instead of via reflection.

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Core;
using StingTools.UI;

namespace StingTools.Commands.SLD
{
    public struct RiserOptions
    {
        public string Layout;        // "Horizontal" | "Vertical"
        public bool ShowFaultKa;
        public bool ShowFeederCsa;
        public bool ShowLoadingPct;
    }

    // ── Drawing engine ────────────────────────────────────────────────────────

    /// <summary>
    /// Pure drawing logic for riser diagrams — separated from the command
    /// classes so both generate and update can call it without reflection.
    /// </summary>
    public static class SLDRiserEngine
    {
        private const double MmPerFt = 304.8;
        // Box dimensions and spacing in mm.
        private const double BoxWmm    = 40;
        private const double BoxHmm    = 60;
        private const double HSpacingMm = 80;
        private const double VSpacingMm = 20;

        public static void DrawRiser(Document doc, ViewDrafting view,
            StingTools.Core.SLD.SLDNode root, RiserOptions opts)
        {
            double boxW    = BoxWmm    / MmPerFt;
            double boxH    = BoxHmm    / MmPerFt;
            double hSpacing = HSpacingMm / MmPerFt;
            double vSpacing = VSpacingMm / MmPerFt;

            // Flatten BFS into per-level lists.
            var levels = new List<List<StingTools.Core.SLD.SLDNode>>();
            var queue  = new Queue<(StingTools.Core.SLD.SLDNode n, int lvl)>();
            queue.Enqueue((root, 0));
            while (queue.Count > 0)
            {
                var (n, lvl) = queue.Dequeue();
                while (levels.Count <= lvl) levels.Add(new List<StingTools.Core.SLD.SLDNode>());
                levels[lvl].Add(n);
                foreach (var c in n.Children ?? Enumerable.Empty<StingTools.Core.SLD.SLDNode>())
                    queue.Enqueue((c, lvl + 1));
            }

            bool vertical = string.Equals(opts.Layout, "Vertical", StringComparison.OrdinalIgnoreCase);
            var positions = new Dictionary<long, XYZ>();

            for (int i = 0; i < levels.Count; i++)
            {
                var col = levels[i];
                for (int j = 0; j < col.Count; j++)
                {
                    double x = vertical ? j * (boxW + hSpacing) : i * (boxW + hSpacing);
                    double y = vertical ? -i * (boxH + vSpacing) : -j * (boxH + vSpacing);
                    var node = col[j];
                    if (node.ElementId != null) positions[node.ElementId.Value] = new XYZ(x, y, 0);
                    DrawBox(doc, view, x, y, boxW, boxH, node, opts);
                }
            }

            DrawFeeders(doc, view, root, positions, boxW, boxH);
        }

        private static void DrawBox(Document doc, ViewDrafting view,
            double x, double y, double w, double h,
            StingTools.Core.SLD.SLDNode node, RiserOptions opts)
        {
            try
            {
                var loop = new CurveLoop();
                var p1 = new XYZ(x,     y,     0);
                var p2 = new XYZ(x + w, y,     0);
                var p3 = new XYZ(x + w, y + h, 0);
                var p4 = new XYZ(x,     y + h, 0);
                loop.Append(Line.CreateBound(p1, p2));
                loop.Append(Line.CreateBound(p2, p3));
                loop.Append(Line.CreateBound(p3, p4));
                loop.Append(Line.CreateBound(p4, p1));

                var frt = new FilteredElementCollector(doc)
                    .OfClass(typeof(FilledRegionType)).Cast<FilledRegionType>().FirstOrDefault();
                if (frt != null)
                    FilledRegion.Create(doc, frt.Id, view.Id, new List<CurveLoop> { loop });

                var ts = new FilteredElementCollector(doc)
                    .OfClass(typeof(TextNoteType)).Cast<TextNoteType>().FirstOrDefault();
                if (ts != null)
                {
                    string label = node.Label ?? "(panel)";
                    if (!string.IsNullOrEmpty(node.Rating)) label += $"\n{node.Rating}";
                    TextNote.Create(doc, view.Id, new XYZ(x + w / 2, y + h / 2, 0), label, ts.Id);

                    if (opts.ShowFaultKa && !string.IsNullOrEmpty(node.FaultKa))
                        TextNote.Create(doc, view.Id, new XYZ(x + w / 2, y - h * 0.15, 0),
                            $"Iₖ {node.FaultKa}kA", ts.Id);

                    if (opts.ShowFeederCsa && !string.IsNullOrEmpty(node.CsaMm2))
                        TextNote.Create(doc, view.Id, new XYZ(x + w / 2, y - h * 0.30, 0),
                            $"{node.CsaMm2}mm²", ts.Id);

                    if (opts.ShowLoadingPct && node.LoadKW > 0)
                        TextNote.Create(doc, view.Id, new XYZ(x + w / 2, y - h * 0.45, 0),
                            $"{node.LoadKW:F1}kVA", ts.Id);
                }
            }
            catch (Exception ex) { StingLog.Warn($"DrawBox: {ex.Message}"); }
        }

        private static void DrawFeeders(Document doc, ViewDrafting view,
            StingTools.Core.SLD.SLDNode root, Dictionary<long, XYZ> positions,
            double boxW, double boxH)
        {
            void Walk(StingTools.Core.SLD.SLDNode n)
            {
                if (n == null) return;
                foreach (var c in n.Children ?? Enumerable.Empty<StingTools.Core.SLD.SLDNode>())
                {
                    try
                    {
                        if (n.ElementId != null && c.ElementId != null
                            && positions.TryGetValue(n.ElementId.Value, out var pa)
                            && positions.TryGetValue(c.ElementId.Value, out var pb))
                        {
                            var start = new XYZ(pa.X + boxW, pa.Y + boxH / 2, 0);
                            var end   = new XYZ(pb.X,        pb.Y + boxH / 2, 0);
                            if (start.DistanceTo(end) > 1e-6)
                                doc.Create.NewDetailCurve(view, Line.CreateBound(start, end));
                        }
                    }
                    catch (Exception ex) { StingLog.Warn($"DrawFeeder: {ex.Message}"); }
                    Walk(c);
                }
            }
            Walk(root);
        }
    }

    // ── Commands ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Generates a horizontal riser diagram in a new ViewDrafting using
    /// FilledRegion boxes for panels and DetailCurve segments for feeders.
    /// Distinct from the vertical SLD generated by SLDGenerator.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class SLDRiserDiagramCommand : IExternalCommand
    {
        private const string RiserDrawingTypeId = "elec-riser-A2-1to100";

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { message = "No active document."; return Result.Failed; }
            var doc = ctx.Doc;

            var root = StingTools.Core.SLD.SLDCircuitTraverser.BuildHierarchy(doc);
            if (root == null)
            {
                TaskDialog.Show("STING Riser", "No SLD hierarchy found.");
                return Result.Cancelled;
            }
            var opts = StingElectricalCommandHandler.CurrentRiserOptions;

            ViewDrafting view = null;
            using (var tx = new Transaction(doc, "STING Generate Riser Diagram"))
            {
                tx.Start();
                view = CreateOrReplaceView(doc,
                    $"STING - Riser Diagram - {DateTime.Now:yyyyMMdd-HHmm}");
                if (view == null)
                { tx.RollBack(); message = "Could not create drafting view."; return Result.Failed; }
                SLDRiserEngine.DrawRiser(doc, view, root, opts);
                StampDrawingType(doc, view, RiserDrawingTypeId);
                tx.Commit();
            }

            if (view != null)
            {
                try { ctx.UIDoc.ActiveView = view; } catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
            }
            try { ComplianceScan.InvalidateCache(); } catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
            TaskDialog.Show("STING Riser", $"Riser diagram generated: '{view?.Name}'.");
            return Result.Succeeded;
        }

        private static ViewDrafting CreateOrReplaceView(Document doc, string name)
        {
            try
            {
                var existing = new FilteredElementCollector(doc)
                    .OfClass(typeof(ViewDrafting)).Cast<ViewDrafting>()
                    .FirstOrDefault(v => string.Equals(v.Name, name,
                        StringComparison.OrdinalIgnoreCase));
                if (existing != null) return existing;
                var vft = new FilteredElementCollector(doc)
                    .OfClass(typeof(ViewFamilyType)).Cast<ViewFamilyType>()
                    .FirstOrDefault(t => t.ViewFamily == ViewFamily.Drafting);
                if (vft == null) return null;
                var v = ViewDrafting.Create(doc, vft.Id);
                try { v.Name = name; } catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
                return v;
            }
            catch (Exception ex) { StingLog.Warn($"CreateOrReplaceView: {ex.Message}"); return null; }
        }

        private static void StampDrawingType(Document doc, ViewDrafting view, string dtId)
        {
            try
            {
                var t = Type.GetType("StingTools.Core.Drawing.DrawingTypeStamper");
                t?.GetMethod("Stamp", new[] { typeof(Element), typeof(string) })
                  ?.Invoke(null, new object[] { view, dtId });
            }
            catch (Exception ex) { StingLog.Warn($"StampDrawingType: {ex.Message}"); }
        }
    }

    /// <summary>
    /// Replaces the contents of an existing "STING - Riser Diagram*" view
    /// in place so any sheet placement is preserved.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class SLDUpdateRiserCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { message = "No active document."; return Result.Failed; }
            var doc = ctx.Doc;

            var view = new FilteredElementCollector(doc)
                .OfClass(typeof(ViewDrafting)).Cast<ViewDrafting>()
                .FirstOrDefault(v => (v.Name ?? "").StartsWith("STING - Riser Diagram",
                    StringComparison.OrdinalIgnoreCase));
            if (view == null)
            {
                TaskDialog.Show("STING Riser",
                    "No existing riser diagram view found. Run Generate first.");
                return Result.Cancelled;
            }

            var root = StingTools.Core.SLD.SLDCircuitTraverser.BuildHierarchy(doc);
            if (root == null)
            {
                TaskDialog.Show("STING Riser", "No SLD hierarchy found in model.");
                return Result.Cancelled;
            }

            using (var tx = new Transaction(doc, "STING Update Riser Diagram"))
            {
                tx.Start();
                var viewElems = new FilteredElementCollector(doc, view.Id).ToElementIds().ToList();
                if (viewElems.Count > 0)
                {
                    try { doc.Delete(viewElems); }
                    catch (Exception ex) { StingLog.Warn($"Riser purge: {ex.Message}"); }
                }
                SLDRiserEngine.DrawRiser(doc, view, root,
                    StingElectricalCommandHandler.CurrentRiserOptions);
                tx.Commit();
            }
            TaskDialog.Show("STING Riser", $"Updated '{view.Name}'.");
            return Result.Succeeded;
        }
    }
}
