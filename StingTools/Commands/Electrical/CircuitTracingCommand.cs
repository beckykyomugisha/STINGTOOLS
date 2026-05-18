// StingTools — Interactive circuit tracing (I2).
//
// Upstream / downstream circuit highlight via the SLD hierarchy.
//
// Usage:
//   • Select one electrical panel / equipment → traces ALL descendants (downstream,
//     orange highlight).
//   • Select one electrical fixture / device  → traces path back to source root
//     (upstream, blue highlight).
//
// Traced elements are stamped with ELC_CIRCUIT_TRACE_ACTIVE = "1" so a
// companion clear command can reset the highlights without walking the view.
//
// Also contains ClearCircuitTraceCommand which removes overrides from every
// element stamped by this command in the active view.

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Electrical;
using Autodesk.Revit.UI;
using StingTools.Core;
using StingTools.Core.SLD;

namespace StingTools.Commands.Electrical
{
    /// <summary>
    /// Traces downstream (from a panel) or upstream (from a load) through the
    /// SLD hierarchy, applying coloured graphic overrides and stamping
    /// ELC_CIRCUIT_TRACE_ACTIVE on each traced element.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class CircuitTracingCommand : IExternalCommand
    {
        private const string TraceParam = "ELC_CIRCUIT_TRACE_ACTIVE";

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { message = "No active document."; return Result.Failed; }
            var doc   = ctx.Doc;
            var uidoc = ctx.UIDoc;
            var view  = doc.ActiveView;

            // ── Resolve selected element ──────────────────────────────────────
            var selIds = uidoc.Selection.GetElementIds().ToList();
            if (selIds.Count != 1)
            {
                TaskDialog.Show("STING Circuit Trace",
                    "Select exactly one electrical equipment or fixture element, then run Circuit Trace.");
                return Result.Cancelled;
            }

            var selected = doc.GetElement(selIds[0]) as FamilyInstance;
            if (selected == null)
            {
                TaskDialog.Show("STING Circuit Trace",
                    "Selected element is not a family instance.  " +
                    "Select an electrical panel or a device/fixture.");
                return Result.Cancelled;
            }

            // ── Build SLD hierarchy ───────────────────────────────────────────
            var roots = SLDCircuitTraverser.BuildHierarchyAll(doc);
            if (roots == null || !roots.Any())
            {
                TaskDialog.Show("STING Circuit Trace",
                    "No electrical hierarchy found.  Place panels with downstream circuits first.");
                return Result.Cancelled;
            }

            // ── Find the node for the selected element ────────────────────────
            SLDNode selectedNode = FindNode(roots, selected.Id);
            if (selectedNode == null)
            {
                TaskDialog.Show("STING Circuit Trace",
                    $"Element '{selected.Name ?? selected.Id.ToString()}' was not found in the " +
                    "electrical hierarchy.  Ensure it is connected to a circuit or panel.");
                return Result.Cancelled;
            }

            bool isPanel = selectedNode.IsPanel
                || selected.Category?.Id?.Value == (long)BuiltInCategory.OST_ElectricalEquipment;

            // ── Collect elements to trace and choose colour ───────────────────
            List<ElementId> tracedIds;
            Color traceColor;
            string traceDir;

            if (isPanel)
            {
                // Downstream: all descendants.
                tracedIds = new List<ElementId>();
                CollectDescendants(selectedNode, tracedIds);
                traceColor = new Color(255, 140, 0);   // orange
                traceDir   = "downstream";
            }
            else
            {
                // Upstream: walk parent chain to root.
                tracedIds = new List<ElementId>();
                SLDNode cursor = selectedNode.Parent;
                while (cursor != null)
                {
                    if (cursor.ElementId != null && cursor.ElementId != ElementId.InvalidElementId)
                        tracedIds.Add(cursor.ElementId);
                    cursor = cursor.Parent;
                }
                traceColor = new Color(0, 120, 255);   // blue
                traceDir   = "upstream";
            }

            if (tracedIds.Count == 0)
            {
                TaskDialog.Show("STING Circuit Trace",
                    $"No {traceDir} elements found for '{selected.Name ?? selected.Id.ToString()}'.");
                return Result.Cancelled;
            }

            // ── Build solid-fill pattern for override ─────────────────────────
            var solidFill = new FilteredElementCollector(doc)
                .OfClass(typeof(FillPatternElement))
                .Cast<FillPatternElement>()
                .FirstOrDefault(fp => fp.GetFillPattern().IsSolidFill);

            var ogs = new OverrideGraphicSettings();
            ogs.SetProjectionLineColor(traceColor);
            ogs.SetProjectionLineWeight(5);
            if (solidFill != null)
            {
                ogs.SetSurfaceForegroundPatternId(solidFill.Id);
                ogs.SetSurfaceForegroundPatternColor(traceColor);
                ogs.SetSurfaceTransparency(50);
            }

            int applied = 0;

            using (var tx = new Transaction(doc, "STING Circuit Trace"))
            {
                tx.Start();

                foreach (var id in tracedIds)
                {
                    var el = doc.GetElement(id);
                    if (el == null) continue;

                    try
                    {
                        if (view != null) view.SetElementOverrides(id, ogs);
                        ParameterHelpers.SetString(el, TraceParam, "1", overwrite: true);
                        applied++;
                    }
                    catch (Exception ex)
                    {
                        StingLog.Warn($"CircuitTrace override {id}: {ex.Message}");
                    }
                }

                tx.Commit();
            }

            string startName = selected.Name ?? selected.Id.ToString();
            TaskDialog.Show("STING Circuit Trace",
                $"Traced {applied} element(s) {traceDir} from '{startName}'.\n\n" +
                "Run 'Clear Circuit Trace' to remove highlights.");
            return Result.Succeeded;
        }

        // ── Helpers ──────────────────────────────────────────────────────────

        /// <summary>
        /// Depth-first search through all roots for the node whose
        /// <see cref="SLDNode.ElementId"/> matches <paramref name="targetId"/>.
        /// </summary>
        private static SLDNode FindNode(IEnumerable<SLDNode> roots, ElementId targetId)
        {
            if (targetId == null || targetId == ElementId.InvalidElementId) return null;
            foreach (var root in roots)
            {
                var found = FindNodeRecursive(root, targetId);
                if (found != null) return found;
            }
            return null;
        }

        private static SLDNode FindNodeRecursive(SLDNode node, ElementId targetId)
        {
            if (node == null) return null;
            if (node.ElementId != null && node.ElementId == targetId) return node;
            foreach (var child in node.Children)
            {
                var found = FindNodeRecursive(child, targetId);
                if (found != null) return found;
            }
            return null;
        }

        /// <summary>
        /// Recursively collects <see cref="SLDNode.ElementId"/> for all descendants
        /// of <paramref name="node"/> (not including the node itself).
        /// </summary>
        private static void CollectDescendants(SLDNode node, List<ElementId> ids)
        {
            if (node == null) return;
            foreach (var child in node.Children)
            {
                if (child.ElementId != null && child.ElementId != ElementId.InvalidElementId)
                    ids.Add(child.ElementId);
                CollectDescendants(child, ids);
            }
        }
    }

    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Clears circuit-trace graphic overrides from all elements in the active
    /// view that were stamped with ELC_CIRCUIT_TRACE_ACTIVE = "1".
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class ClearCircuitTraceCommand : IExternalCommand
    {
        private const string TraceParam = "ELC_CIRCUIT_TRACE_ACTIVE";

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { message = "No active document."; return Result.Failed; }
            var doc  = ctx.Doc;
            var view = doc.ActiveView;

            if (view == null)
            {
                TaskDialog.Show("STING Clear Trace", "No active view.");
                return Result.Cancelled;
            }

            // Collect all elements in the view with ELC_CIRCUIT_TRACE_ACTIVE = "1".
            var stamped = new FilteredElementCollector(doc, view.Id)
                .WhereElementIsNotElementType()
                .Cast<Element>()
                .Where(el =>
                {
                    try
                    {
                        var p = el.LookupParameter(TraceParam);
                        return p != null && p.AsString() == "1";
                    }
                    catch { return false; }
                })
                .ToList();

            if (stamped.Count == 0)
            {
                TaskDialog.Show("STING Clear Trace",
                    "No circuit-trace highlights found in the active view.");
                return Result.Cancelled;
            }

            var clearOgs = new OverrideGraphicSettings(); // default = no override

            int cleared = 0;

            using (var tx = new Transaction(doc, "STING Clear Circuit Trace"))
            {
                tx.Start();
                foreach (var el in stamped)
                {
                    try
                    {
                        view.SetElementOverrides(el.Id, clearOgs);
                        ParameterHelpers.SetString(el, TraceParam, "0", overwrite: true);
                        cleared++;
                    }
                    catch (Exception ex)
                    {
                        StingLog.Warn($"ClearTrace {el.Id}: {ex.Message}");
                    }
                }
                tx.Commit();
            }

            TaskDialog.Show("STING Clear Trace",
                $"Removed trace highlights from {cleared} element(s).");
            return Result.Succeeded;
        }
    }
}
