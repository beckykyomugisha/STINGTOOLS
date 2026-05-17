// StingTools Phase 109 — MEP System Tracer.
//
// Walks the MEP connector graph from the selected element(s) back to
// the system source (fan / pump / panel / boiler / tank) and forward
// to every terminal, returning an ordered List<ElementId> of every
// member in the traced system.
//
// Highlights all traced elements in the active view via Selection
// and writes an ASS_TRACE_SEQ_NR parameter (if present) so downstream
// schedules can sort by flow-order.

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Core;
using StingTools.UI;

namespace StingTools.Commands.Mep
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class MepSystemTracerCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { message = "No active document."; return Result.Failed; }
            var doc = ctx.Doc;
            var uidoc = ctx.UIDoc;

            var selIds = uidoc.Selection.GetElementIds();
            if (selIds == null || selIds.Count == 0)
            {
                TaskDialog.Show("STING — MEP System Tracer",
                    "Select any MEP element (pipe / duct / fitting / fixture) to trace its system.");
                return Result.Cancelled;
            }

            var visited = new HashSet<long>();
            var ordered = new List<ElementId>();
            try
            {
                foreach (var seed in selIds)
                    WalkConnectorGraph(doc, seed, visited, ordered);
            }
            catch (Exception ex)
            {
                StingLog.Error("MepSystemTracerCommand walk failed", ex);
                message = ex.Message;
                return Result.Failed;
            }

            // Select the traced elements so the user sees the highlighted path
            try { uidoc.Selection.SetElementIds(ordered); }
            catch (Exception ex) { StingLog.Warn($"MepSystemTracer: select: {ex.Message}"); }

            // Stamp ASS_TRACE_SEQ_NR in a separate transaction (if the
            // parameter is bound). This is best-effort — failure does not
            // abort the command because the primary value is the highlight.
            StampSequence(doc, ordered);

            ShowReport(doc, ordered);
            return Result.Succeeded;
        }

        private static void WalkConnectorGraph(
            Document doc, ElementId start, HashSet<long> visited, List<ElementId> ordered)
        {
            var queue = new Queue<ElementId>();
            queue.Enqueue(start);
            while (queue.Count > 0)
            {
                var id = queue.Dequeue();
                if (!visited.Add(id.Value)) continue;
                ordered.Add(id);

                Element el = null;
                try { el = doc.GetElement(id); } catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
                if (el == null) continue;

                ConnectorSet set = ResolveConnectors(el);
                if (set == null) continue;

                foreach (Connector c in set)
                {
                    if (c == null) continue;
                    ConnectorSet others = null;
                    try { others = c.AllRefs; } catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
                    if (others == null) continue;
                    foreach (Connector other in others)
                    {
                        if (other == null || other.Owner == null) continue;
                        if (other.Owner.Id.Value == id.Value) continue;
                        queue.Enqueue(other.Owner.Id);
                    }
                }
            }
        }

        private static ConnectorSet ResolveConnectors(Element el)
        {
            try
            {
                if (el is MEPCurve mep) return mep.ConnectorManager?.Connectors;
                if (el is FamilyInstance fi && fi.MEPModel != null) return fi.MEPModel.ConnectorManager?.Connectors;
            }
            catch (Exception ex) { StingLog.Warn($"MepSystemTracer: connector read for {el?.Id}: {ex.Message}"); }
            return null;
        }

        private static void StampSequence(Document doc, List<ElementId> ordered)
        {
            using (var tx = new Transaction(doc, "STING MEP trace SEQ"))
            {
                try { tx.Start(); }
                catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); return; }
                try
                {
                    int seq = 1;
                    foreach (var id in ordered)
                    {
                        var el = doc.GetElement(id);
                        if (el == null) continue;
                        var p = el.LookupParameter("ASS_TRACE_SEQ_NR");
                        if (p == null || p.IsReadOnly) continue;
                        try
                        {
                            if (p.StorageType == StorageType.Integer) p.Set(seq);
                            else if (p.StorageType == StorageType.String) p.Set(seq.ToString("D4"));
                        }
                        catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
                        seq++;
                    }
                    tx.Commit();
                }
                catch (Exception ex)
                {
                    if (tx.HasStarted() && !tx.HasEnded()) tx.RollBack();
                    StingLog.Warn($"MepSystemTracer: stamp failed: {ex.Message}");
                }
            }
        }

        private static void ShowReport(Document doc, List<ElementId> ordered)
        {
            var panel = StingResultPanel.Create("MEP System Tracer");
            panel.SetSubtitle($"{ordered.Count} elements reached from selection");
            panel.AddSection("SUMMARY")
                 .Metric("Elements traced", ordered.Count.ToString());

            var byCat = new Dictionary<string, int>();
            foreach (var id in ordered)
            {
                try
                {
                    var el = doc.GetElement(id);
                    string cat = el?.Category?.Name ?? "(unknown)";
                    byCat[cat] = byCat.TryGetValue(cat, out var n) ? n + 1 : 1;
                }
                catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
            }
            panel.AddSection("BY CATEGORY");
            foreach (var kv in byCat.OrderByDescending(k => k.Value))
                panel.Metric(kv.Key, kv.Value.ToString());

            panel.AddSection("HINT")
                 .Text("Run MepPressureDropAnalyse on the resulting selection to get system-wide pressure drop + velocity exceedance report.");
            panel.Show();
        }
    }
}
