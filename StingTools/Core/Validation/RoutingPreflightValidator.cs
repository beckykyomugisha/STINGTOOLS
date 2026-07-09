using StingTools.Core;
// StingTools — RoutingPreflightValidator (Task D).
//
// Gates conduit routing by surfacing the two failure modes that otherwise
// only reveal themselves as failed drops or corrupt geometry AFTER routing:
//
//   SIZE.MISMATCH — a conduit connector whose nominal diameter is not in
//     the project's Conduit Standards size list. When routing hits such a
//     connector Revit silently inserts a phantom junction-box reducer, or
//     reports "No auto-route solution found". Reported here so the size can
//     be fixed (or the size added to the standard) before routing.
//
//   CONN.DOMAIN — an electrical MEP device that carries power/other
//     connectors but NO Domain.DomainCableTrayConduit connector. It cannot
//     be conduit-auto-routed as-is; run Routing_PlaceSleeveConnectors to
//     author a conduit terminal (the sleeve method). Info severity — this
//     is a routability hint, not an error.
//
// Read-only. Registered in RunAllValidatorsCommand and run as a pre-flight
// inside AutoDropCommand's electrical group so problems are reported BEFORE
// routing rather than discovered as failed drops.

using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Electrical;

namespace StingTools.Core.Validation
{
    public class RoutingPreflightValidator
    {
        public string Name => "RoutingPreflightValidator";
        private const string ValidatorTag = "RoutingPreflightValidator";
        private const double MmToFt = 1.0 / 304.8;
        private const double SizeTolFt = 1.0 * MmToFt; // 1 mm

        private static readonly BuiltInCategory[] ElecCats =
        {
            BuiltInCategory.OST_ElectricalFixtures,
            BuiltInCategory.OST_ElectricalEquipment,
            BuiltInCategory.OST_LightingFixtures,
            BuiltInCategory.OST_LightingDevices,
            BuiltInCategory.OST_CommunicationDevices,
            BuiltInCategory.OST_DataDevices,
            BuiltInCategory.OST_SecurityDevices,
            BuiltInCategory.OST_FireAlarmDevices,
            BuiltInCategory.OST_NurseCallDevices,
        };

        public List<ValidationResult> Validate(Document doc)
        {
            var results = new List<ValidationResult>();
            if (doc == null) return results;

            var validSizesFt = CollectConduitSizesFt(doc);
            try
            {
                var filter = new ElementMulticategoryFilter(ElecCats);
                var col = new FilteredElementCollector(doc).WherePasses(filter)
                    .WhereElementIsNotElementType();
                foreach (var el in col)
                {
                    try { CheckElement(el, validSizesFt, results); }
                    catch (Exception ex)
                    { StingLog.Warn($"RoutingPreflightValidator: element {el.Id} failed: {ex.Message}"); }
                }
            }
            catch (Exception ex)
            { StingLog.Warn($"RoutingPreflightValidator: scan failed: {ex.Message}"); }
            return results;
        }

        private void CheckElement(Element el, List<double> validSizesFt, List<ValidationResult> results)
        {
            var mgr = ResolveConnectorManager(el);
            if (mgr == null) return;
            ConnectorSet set;
            try { set = mgr.Connectors; } catch { return; }
            if (set == null) return;

            int conduitConns = 0, otherConns = 0;
            foreach (Connector c in set)
            {
                if (c == null) continue;
                Domain d;
                try { d = c.Domain; } catch { continue; }
                if (d == Domain.DomainCableTrayConduit)
                {
                    conduitConns++;
                    CheckConduitSize(el, c, validSizesFt, results);
                }
                else otherConns++;
            }

            // Domain routability hint — has MEP connectors but none is conduit.
            if (conduitConns == 0 && otherConns > 0)
            {
                results.Add(new ValidationResult(
                    el.Id,
                    ValidationSeverity.Info,
                    "CONN.DOMAIN.NOCONDUIT",
                    $"{otherConns} non-conduit connector(s) but no CableTrayConduit connector — " +
                    "not conduit-auto-routable; run Routing_PlaceSleeveConnectors to author a terminal.",
                    ValidatorTag));
            }
        }

        private void CheckConduitSize(Element el, Connector c, List<double> validSizesFt, List<ValidationResult> results)
        {
            if (validSizesFt == null || validSizesFt.Count == 0) return; // no standard to compare against
            double diamFt;
            try
            {
                if (c.Shape != ConnectorProfileType.Round) return; // conduit is round; skip odd shapes
                diamFt = c.Radius * 2.0;
            }
            catch { return; }
            if (diamFt <= 0) return;

            foreach (var s in validSizesFt)
                if (Math.Abs(s - diamFt) <= SizeTolFt) return; // on the list

            results.Add(new ValidationResult(
                el.Id,
                ValidationSeverity.Warning,
                "SIZE.MISMATCH",
                $"Conduit connector Ø{diamFt / MmToFt:F1} mm is not in the project conduit size list — " +
                "routing may insert a phantom reducer or fail with 'No auto-route solution found'.",
                ValidatorTag));
        }

        /// <summary>Collect every nominal conduit diameter (feet) from the
        /// project's Conduit Standards. Empty when no standard is defined —
        /// callers then skip the size check.</summary>
        private static List<double> CollectConduitSizesFt(Document doc)
        {
            var sizes = new List<double>();
            try
            {
                var settings = ConduitSizeSettings.GetConduitSizeSettings(doc);
                if (settings == null) return sizes;
                foreach (KeyValuePair<string, ConduitSizes> std in settings)
                {
                    if (std.Value == null) continue;
                    foreach (ConduitSize sz in std.Value)
                    {
                        try { if (sz.NominalDiameter > 0) sizes.Add(sz.NominalDiameter); }
                        catch { /* skip malformed size */ }
                    }
                }
            }
            catch (Exception ex)
            { StingLog.Warn($"RoutingPreflightValidator: conduit size list read failed: {ex.Message}"); }
            return sizes;
        }

        private static ConnectorManager ResolveConnectorManager(Element el)
        {
            try
            {
                if (el is MEPCurve mep) return mep.ConnectorManager;
                if (el is FamilyInstance fi && fi.MEPModel != null) return fi.MEPModel.ConnectorManager;
            }
            catch (Exception ex)
            { StingLog.Warn($"RoutingPreflightValidator: connector manager read failed for {el?.Id}: {ex.Message}"); }
            return null;
        }
    }
}
