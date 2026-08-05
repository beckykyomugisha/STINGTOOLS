// StingTools — Panel / Wire Annotation Reconcile Command.
//
// Cross-checks the wire annotation panel/circuit refs stored on conduit
// elements (ELC_PNL_NAME_TXT / ELC_CIRCUIT_NR_TXT) against the actual
// ElectricalSystem that the conduit is physically connected to in the
// Revit model connector graph.
//
// Flags mismatches where the annotation label says "PANEL-A / 12" but
// the real circuit says "PANEL-B / 14", helping engineers catch stale
// schedules after circuit re-numbering or re-panelling.
//
// Transaction mode: ReadOnly.
// Optionally corrects ELC_PNL_NAME_TXT / ELC_CIRCUIT_NR_TXT via a
// follow-up Manual transaction when the user confirms.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Electrical;
using Autodesk.Revit.UI;
using StingTools.Core;
using StingTools.Core.Electrical;

namespace StingTools.Commands.Electrical
{
    // ────────────────────────────────────────────────────────────────────────────
    //  Data model
    // ────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Describes a mismatch between the annotation label on a conduit element
    /// and the actual panel / circuit it is connected to.
    /// </summary>
    public sealed class WireReconcileItem
    {
        public ElementId ConduitId       { get; set; }
        public string    ConduitName     { get; set; }

        /// <summary>Panel name read from <c>ELC_PNL_NAME_TXT</c> on the conduit.</summary>
        public string    AnnotPanelName  { get; set; }

        /// <summary>Circuit number read from <c>ELC_CIRCUIT_NR_TXT</c> on the conduit.</summary>
        public string    AnnotCircuitNr  { get; set; }

        /// <summary>Panel name from the connected <c>ElectricalSystem.PanelName</c>.</summary>
        public string    ActualPanelName { get; set; }

        /// <summary>Circuit number from the connected <c>ElectricalSystem</c>.</summary>
        public string    ActualCircuitNr { get; set; }

        /// <summary><c>true</c> when either panel name or circuit number differ.</summary>
        public bool Mismatch =>
            !string.Equals(AnnotPanelName,  ActualPanelName, StringComparison.OrdinalIgnoreCase)
         || !string.Equals(AnnotCircuitNr,  ActualCircuitNr,  StringComparison.OrdinalIgnoreCase);
    }

    // ────────────────────────────────────────────────────────────────────────────
    //  Command
    // ────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Read-only audit command that cross-checks wire annotation
    /// panel/circuit labels against the Revit connector graph.
    /// </summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class PanelWireReconcileCommand : IExternalCommand
    {
        private const int MaxConnectorHops = 5;
        private const int MaxDialogListItems = 10;

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                var uidoc = commandData.Application.ActiveUIDocument;
                var doc   = uidoc?.Document;
                if (doc == null)
                {
                    message = "No active document.";
                    return Result.Failed;
                }

                var view = uidoc.ActiveGraphicalView;
                if (view == null)
                {
                    TaskDialog.Show("STING — Panel Reconcile",
                        "Please activate a graphical view before running this command.");
                    return Result.Cancelled;
                }

                // ── 1. Collect conduits that have wire annotations in the view ──

                var annotatedConduits = new FilteredElementCollector(doc, view.Id)
                    .OfCategory(BuiltInCategory.OST_Conduit)
                    .WhereElementIsNotElementType()
                    .ToElements()
                    .Where(c => AnnotationMarkerRegistry.FindByOwner(
                                    doc, view,
                                    AnnotationMarkerRegistry.WireAnnotationPrefix,
                                    c.UniqueId).Count > 0)
                    .ToList();

                if (annotatedConduits.Count == 0)
                {
                    TaskDialog.Show("STING — Panel Reconcile",
                        "No wire-annotated conduits found in the active view.");
                    return Result.Succeeded;
                }

                // ── 2. Check each conduit ──────────────────────────────────────

                var mismatches = new List<WireReconcileItem>();
                int checked_   = 0;

                foreach (var conduit in annotatedConduits)
                {
                    checked_++;
                    try
                    {
                        var item = Reconcile(doc, conduit);
                        if (item != null && item.Mismatch)
                            mismatches.Add(item);
                    }
                    catch (Exception ex)
                    {
                        StingLog.Warn($"PanelWireReconcile: conduit {conduit.Id}: {ex.Message}");
                    }
                }

                // ── 3. Report ─────────────────────────────────────────────────

                string header = $"{checked_} conduit(s) checked. {mismatches.Count} mismatch(es) found.";

                if (mismatches.Count == 0)
                {
                    TaskDialog.Show("STING — Panel Reconcile", $"{header}\n\nAll wire annotations match circuit assignments. ✓");
                    return Result.Succeeded;
                }

                var sb = new StringBuilder();
                sb.AppendLine(header);
                sb.AppendLine();

                int shown = Math.Min(mismatches.Count, MaxDialogListItems);
                for (int i = 0; i < shown; i++)
                {
                    var m = mismatches[i];
                    sb.AppendLine($"• {m.ConduitName ?? m.ConduitId.ToString()}");
                    sb.AppendLine($"    Annotation: Panel={NonEmpty(m.AnnotPanelName)}  Ckt={NonEmpty(m.AnnotCircuitNr)}");
                    sb.AppendLine($"    Actual:     Panel={NonEmpty(m.ActualPanelName)}  Ckt={NonEmpty(m.ActualCircuitNr)}");
                }

                if (mismatches.Count > MaxDialogListItems)
                    sb.AppendLine($"… and {mismatches.Count - MaxDialogListItems} more (see STING log for full list).");

                // Log full list regardless of truncation
                foreach (var m in mismatches)
                {
                    StingLog.Info($"PanelReconcile mismatch — {m.ConduitName} [{m.ConduitId}]"
                        + $" annot({m.AnnotPanelName}/{m.AnnotCircuitNr})"
                        + $" actual({m.ActualPanelName}/{m.ActualCircuitNr})");
                }

                var td = new TaskDialog("STING — Panel Reconcile")
                {
                    MainContent          = sb.ToString(),
                    CommonButtons        = TaskDialogCommonButtons.Close,
                    DefaultButton        = TaskDialogResult.Close,
                    AllowCancellation    = true,
                };
                td.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Auto-correct parameter values on mismatched conduits");

                var tdResult = td.Show();

                // ── 4. Optional auto-correct ──────────────────────────────────

                if (tdResult == TaskDialogResult.CommandLink1)
                {
                    AutoCorrect(doc, mismatches);
                }

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("PanelWireReconcileCommand", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }

        // ── Core reconcile logic ─────────────────────────────────────────────

        private static WireReconcileItem Reconcile(Document doc, Element conduit)
        {
            string annotPanel  = ParameterHelpers.GetString(conduit, "ELC_PNL_NAME_TXT");
            string annotCircuit = ParameterHelpers.GetString(conduit, "ELC_CIRCUIT_NR_TXT");

            // Walk connector graph to find an ElectricalSystem
            var sys = FindConnectedElectricalSystem(doc, conduit);
            string actualPanel  = "";
            string actualCircuit = "";

            if (sys != null)
            {
                actualPanel  = sys.PanelName ?? "";

                // Circuit number: try named param first, then CircuitNumber property
                try
                {
                    var p = sys.LookupParameter("ELC_CIRCUIT_NR_TXT")
                         ?? sys.LookupParameter("Circuit Number");
                    if (p != null && p.StorageType == StorageType.String)
                        actualCircuit = p.AsString() ?? "";
                    if (string.IsNullOrEmpty(actualCircuit))
                        actualCircuit = sys.CircuitNumber ?? "";
                }
                catch
                {
                    actualCircuit = "";
                }
            }

            var item = new WireReconcileItem
            {
                ConduitId       = conduit.Id,
                ConduitName     = conduit.Name ?? conduit.Id.ToString(),
                AnnotPanelName  = annotPanel,
                AnnotCircuitNr  = annotCircuit,
                ActualPanelName = actualPanel,
                ActualCircuitNr = actualCircuit,
            };

            return item;
        }

        // ── Connector-graph walker (max 5 hops) ───────────────────────────────

        /// <summary>
        /// Walks the connector graph starting from <paramref name="conduit"/>
        /// to find a connected <see cref="ElectricalSystem"/>, within
        /// <see cref="MaxConnectorHops"/> hops. Returns null when not found.
        /// </summary>
        private static ElectricalSystem FindConnectedElectricalSystem(Document doc, Element conduit)
        {
            if (conduit == null) return null;

            var visited = new HashSet<ElementId>();
            var queue   = new Queue<Element>();
            queue.Enqueue(conduit);
            int hops = 0;

            while (queue.Count > 0 && hops < MaxConnectorHops)
            {
                var current = queue.Dequeue();
                if (!visited.Add(current.Id)) continue;
                hops++;

                // Check if current element IS an ElectricalSystem
                if (current is ElectricalSystem es)
                    return es;

                // Enumerate connectors
                ConnectorSet connectors = null;
                try
                {
                    var cm = GetConnectorManager(current);
                    connectors = cm?.Connectors;
                }
                catch { /* element may not have a connector manager */ }

                if (connectors == null) continue;

                foreach (Connector connector in connectors)
                {
                    if (connector == null) continue;
                    try
                    {
                        var allRefs = connector.AllRefs;
                        if (allRefs == null) continue;
                        foreach (Connector refConn in allRefs)
                        {
                            var owner = refConn?.Owner;
                            if (owner == null) continue;
                            if (visited.Contains(owner.Id)) continue;

                            // Direct ElectricalSystem hit
                            if (owner is ElectricalSystem directSys)
                                return directSys;

                            // Keep walking through conduits / fittings / connectors
                            var cat = owner.Category?.Id;
                            if (cat != null && IsElectricalCategory(cat))
                                queue.Enqueue(owner);
                        }
                    }
                    catch { /* skip bad connector */ }
                }
            }

            return null;
        }

        private static ConnectorManager GetConnectorManager(Element el)
        {
            // MEPCurve (conduit, cable tray, etc.) and FamilyInstance both have connectors
            if (el is MEPCurve mc)        return mc.ConnectorManager;
            if (el is FamilyInstance fi)  return fi.MEPModel?.ConnectorManager;
            return null;
        }

        private static bool IsElectricalCategory(ElementId catId)
        {
            var electricalCategories = new[]
            {
                (int)BuiltInCategory.OST_Conduit,
                (int)BuiltInCategory.OST_ConduitFitting,
                (int)BuiltInCategory.OST_CableTray,
                (int)BuiltInCategory.OST_CableTrayFitting,
                (int)BuiltInCategory.OST_ElectricalFixtures,
                (int)BuiltInCategory.OST_ElectricalEquipment,
                (int)BuiltInCategory.OST_LightingDevices,
                (int)BuiltInCategory.OST_LightingFixtures,
            };

            int id = (int)catId.Value;
            foreach (int c in electricalCategories)
                if (c == id) return true;
            return false;
        }

        // ── Auto-correct ──────────────────────────────────────────────────────

        private static void AutoCorrect(Document doc, List<WireReconcileItem> mismatches)
        {
            if (mismatches.Count == 0) return;
            int corrected = 0;

            try
            {
                using var t = new Transaction(doc, "STING Reconcile Wire Panel Refs");
                t.Start();

                foreach (var item in mismatches)
                {
                    try
                    {
                        var conduit = doc.GetElement(item.ConduitId);
                        if (conduit == null) continue;

                        if (!string.IsNullOrEmpty(item.ActualPanelName))
                            ParameterHelpers.SetString(conduit, "ELC_PNL_NAME_TXT", item.ActualPanelName, overwrite: true);

                        if (!string.IsNullOrEmpty(item.ActualCircuitNr))
                            ParameterHelpers.SetString(conduit, "ELC_CIRCUIT_NR_TXT", item.ActualCircuitNr, overwrite: true);

                        corrected++;
                    }
                    catch (Exception ex)
                    {
                        StingLog.Warn($"PanelWireReconcile.AutoCorrect: conduit {item.ConduitId}: {ex.Message}");
                    }
                }

                t.Commit();
                StingLog.Info($"PanelWireReconcile: auto-corrected {corrected}/{mismatches.Count} conduit(s).");
                TaskDialog.Show("STING — Panel Reconcile",
                    $"Auto-correct complete.\n{corrected}/{mismatches.Count} conduit parameter(s) updated.");
            }
            catch (Exception ex)
            {
                StingLog.Error("PanelWireReconcile.AutoCorrect", ex);
                TaskDialog.Show("STING — Panel Reconcile", $"Auto-correct failed: {ex.Message}");
            }
        }

        // ── Utility ───────────────────────────────────────────────────────────

        private static string NonEmpty(string s) =>
            string.IsNullOrWhiteSpace(s) ? "(blank)" : s;
    }
}
