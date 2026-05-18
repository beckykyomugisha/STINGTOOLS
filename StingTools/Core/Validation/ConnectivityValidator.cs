// StingTools v4 MVP — ConnectivityValidator.
//
// Walks every MEPCurve / FamilyInstance with electrical, plumbing or
// mechanical connectors and asserts every terminal has a path to a
// system source. The first-pass implementation reports any element
// where one or more connectors are unconnected; full graph traversal
// to a source is a future enhancement.

using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using StingTools.Core;

namespace StingTools.Core.Validation
{
    public class ConnectivityValidator
    {
        public string Name => "ConnectivityValidator";
        private const string ValidatorTag = "ConnectivityValidator";

        public List<ValidationResult> Validate(Document doc)
        {
            var results = new List<ValidationResult>();
            if (doc == null) return results;

            try
            {
                var cats = new[]
                {
                    BuiltInCategory.OST_PipeCurves,
                    BuiltInCategory.OST_DuctCurves,
                    BuiltInCategory.OST_Conduit,
                    BuiltInCategory.OST_CableTray,
                    BuiltInCategory.OST_PlumbingFixtures,
                    BuiltInCategory.OST_DuctTerminal,
                    BuiltInCategory.OST_MechanicalEquipment,
                    BuiltInCategory.OST_ElectricalFixtures,
                    BuiltInCategory.OST_ElectricalEquipment,
                    BuiltInCategory.OST_LightingFixtures,
                };
                var filter = new ElementMulticategoryFilter(cats);
                var col = new FilteredElementCollector(doc).WherePasses(filter)
                    .WhereElementIsNotElementType();

                foreach (var el in col)
                {
                    try { CheckElementConnectors(el, results); }
                    catch (Exception ex)
                    {
                        StingLog.Warn($"ConnectivityValidator: element {el.Id} failed: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                StingLog.Warn($"ConnectivityValidator: scan failed: {ex.Message}");
            }
            return results;
        }

        private void CheckElementConnectors(Element el, List<ValidationResult> results)
        {
            ConnectorManager mgr = ResolveConnectorManager(el);
            if (mgr == null) return;
            ConnectorSet set = mgr.Connectors;
            if (set == null) return;

            int total = 0;
            int unconnected = 0;
            foreach (Connector c in set)
            {
                if (c == null) continue;
                total++;
                bool isConnected = false;
                try { isConnected = c.IsConnected; } catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
                if (!isConnected) unconnected++;
            }
            if (total > 0 && unconnected > 0)
            {
                results.Add(new ValidationResult(
                    el.Id,
                    ValidationSeverity.Warning,
                    "CONN.OPEN",
                    $"{unconnected} of {total} connector(s) open",
                    ValidatorTag));
            }

            // §5.3 — family-declared connector count sanity. When
            // CONN_COUNT_INT says "2 connectors" but the element reports
            // more, flag it so the discrepancy surfaces in the validator
            // report. Families that don't declare anything read 0 and
            // this check is a no-op.
            try
            {
                var routing = Routing.RoutingParamReader.Read(el);
                if (routing.ConnectorCount > 0 && total > 0 && total != routing.ConnectorCount)
                {
                    results.Add(new ValidationResult(
                        el.Id,
                        ValidationSeverity.Info,
                        "CONN.COUNT.MISMATCH",
                        $"Family CONN_COUNT_INT = {routing.ConnectorCount} but {total} connector(s) present",
                        ValidatorTag));
                }
            }
            catch { /* non-fatal */ }
        }

        private ConnectorManager ResolveConnectorManager(Element el)
        {
            try
            {
                if (el is MEPCurve mep) return mep.ConnectorManager;
                if (el is FamilyInstance fi && fi.MEPModel != null) return fi.MEPModel.ConnectorManager;
            }
            catch (Exception ex)
            {
                StingLog.Warn($"ConnectivityValidator: connector manager read failed for {el?.Id}: {ex.Message}");
            }
            return null;
        }
    }
}
