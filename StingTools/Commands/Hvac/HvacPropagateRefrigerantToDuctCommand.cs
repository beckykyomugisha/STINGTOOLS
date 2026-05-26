// StingTools — Refrigerant capacity → connected supply-duct flow auto-stamp.
//
// Phase 187e closes the second half of the refrigerant ↔ duct linkage
// gap. HvacRefrigerantSizeCommand surfaces ducted IDUs (FCU / ducted
// VRF / AHU) so the user knows they need duct sizing too; this command
// completes the loop:
//
//   1. Walk every mechanical-equipment instance that looks like a
//      ducted refrigerant indoor unit (or AHU).
//   2. Read its HVC_CAPACITY_KW (or pull from connector if missing).
//   3. Compute required supply airflow Q_ls = cap_W / (ρ·cp·ΔT)
//      with ΔT = 11 K cooling supply (CIBSE Guide B3).
//   4. Walk the equipment's HVAC connectors downstream into the duct
//      tree, stamping HVC_FLOW_LS on every duct in the served system.
//   5. Stamp HVC_LOAD_SOURCE_TXT with provenance so Hvac_AutoSizeDuct
//      can credit the linkage.
//
// Mirror of HvacPropagateLoadsCommand's structure but the flow source
// is the equipment's CAPACITY, not the served space's PEAK. Useful
// where BlockLoad hasn't been run (e.g. early-design refrigerant pass).

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Core;
using StingTools.Core.Climate;
using StingTools.UI;

namespace StingTools.Commands.Hvac
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class HvacPropagateRefrigerantToDuctCommand : IExternalCommand
    {
        private const double DefaultSupplyDtK = 11.0;
        private const double AirCpJperKgK = 1005.0;
        private const int MaxWalk = 20;   // generous; ducted IDUs have shallow downstream trees

        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            try
            {
                var ctx = ParameterHelpers.GetContext(commandData);
                if (ctx == null) { message = "No active document."; return Result.Failed; }
                var doc = ctx.Doc;

                double rho = 1.20;
                try
                {
                    var site = ClimateRegistry.ActiveSite(doc);
                    if (site != null && site.AirDensityCoolingKgM3() > 0)
                        rho = site.AirDensityCoolingKgM3();
                }
                catch { }

                var iduList = new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_MechanicalEquipment)
                    .WhereElementIsNotElementType()
                    .OfClass(typeof(FamilyInstance))
                    .Cast<FamilyInstance>()
                    .Where(IsLikelyDuctedIdu)
                    .Where(HasDuctConnector)
                    .ToList();
                if (iduList.Count == 0)
                {
                    TaskDialog.Show("STING HVAC — Propagate Refrigerant",
                        "No ducted indoor units (FCU / ducted VRF / AHU) found in the project.");
                    return Result.Cancelled;
                }

                int duStamped = 0, iduSkipped = 0;
                double totalFlowLs = 0;
                var perIduDetails = new List<string>();
                var visited = new HashSet<long>();        // global to avoid double-stamping

                using (var tx = new Transaction(doc, "STING Propagate Refrigerant → Duct"))
                {
                    tx.Start();
                    foreach (var idu in iduList)
                    {
                        try
                        {
                            double capKw = ReadDouble(idu, "HVC_CAPACITY_KW");
                            if (capKw <= 0)
                            {
                                // Fall back to summed connector flow → kW via ΔT·ρ·cp.
                                double connLs = MaxConnectorAirFlowLs(idu);
                                capKw = connLs * 1e-3 * rho * AirCpJperKgK * DefaultSupplyDtK / 1000.0;
                            }
                            if (capKw <= 0) { iduSkipped++; continue; }

                            // Q (m³/s) = Q_W / (ρ·cp·ΔT). Convert to L/s.
                            double flowLs = (capKw * 1000.0 / (rho * AirCpJperKgK * DefaultSupplyDtK)) * 1000.0;
                            string tag = idu.LookupParameter("ASS_TAG_1")?.AsString() ?? $"#{idu.Id.Value}";
                            string provenance = $"{tag}: capacity={capKw:F1} kW → flow={flowLs:F0} L/s @ ΔT={DefaultSupplyDtK} K";

                            // Walk every HVAC connector outward through the duct tree.
                            int stamped = 0;
                            foreach (Connector c in IduHvacConnectors(idu))
                                stamped += WalkAndStamp(c, flowLs, provenance, visited, 0);
                            duStamped += stamped;
                            totalFlowLs += flowLs;
                            if (perIduDetails.Count < 40)
                                perIduDetails.Add($"{tag}: {capKw:F1} kW → {flowLs:F0} L/s · stamped {stamped} ducts");
                        }
                        catch (Exception ex) { StingLog.Warn($"PropagateRefrig {idu.Id}: {ex.Message}"); }
                    }
                    tx.Commit();
                }

                var panel = StingResultPanel.Create("HVAC — Propagate Refrigerant → Duct");
                panel.SetSubtitle($"ρ={rho:F3} kg/m³ · ΔT={DefaultSupplyDtK:F0} K · {iduList.Count} IDUs scanned");
                panel.AddSection("SUMMARY")
                     .Metric("Ducted IDUs found",     iduList.Count.ToString())
                     .Metric("IDUs skipped (no cap)", iduSkipped.ToString())
                     .Metric("Ducts stamped",         duStamped.ToString())
                     .Metric("Total flow propagated", $"{totalFlowLs / 1000:F2} m³/s");
                if (perIduDetails.Count > 0)
                {
                    panel.AddSection("PER IDU (first 40)");
                    foreach (var s in perIduDetails) panel.Text(s);
                }
                panel.Text("Stamps HVC_FLOW_LS on every duct downstream of each IDU's HVAC connector " +
                           "from the equipment's HVC_CAPACITY_KW (CIBSE Guide B3 ΔT=11 K). Use after " +
                           "Hvac_RefrigSize when BlockLoad isn't available — Hvac_AutoSizeDuct then " +
                           "sizes those ducts directly. Visited-set dedupes shared downstream segments.");
                panel.Show();

                try
                {
                    StingHvacPanel.Instance?.PushRunRow(
                        $"Propagate refrig→duct ({duStamped} ducts from {iduList.Count} IDUs)",
                        duStamped > 0 ? "⬤" : "⬡");
                }
                catch (Exception ex) { StingLog.Warn($"Panel push: {ex.Message}"); }

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("HvacPropagateRefrigerantToDuctCommand", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }

        // ── Helpers ─────────────────────────────────────────────────

        private static bool IsLikelyDuctedIdu(FamilyInstance fi)
        {
            string s = ($"{fi.Symbol?.Family?.Name} {fi.Symbol?.Name} {fi.Name}").ToLowerInvariant();
            return s.Contains("ducted") || s.Contains("fcu") || s.Contains("fan coil")
                || s.Contains("ceiling concealed") || s.Contains("ahu") || s.Contains("air handl");
        }

        private static bool HasDuctConnector(FamilyInstance fi)
        {
            try
            {
                var conns = fi.MEPModel?.ConnectorManager?.Connectors;
                if (conns == null) return false;
                foreach (Connector c in conns)
                    if (c.Domain == Domain.DomainHvac) return true;
            }
            catch { }
            return false;
        }

        private static IEnumerable<Connector> IduHvacConnectors(FamilyInstance fi)
        {
            var list = new List<Connector>();
            try
            {
                var conns = fi.MEPModel?.ConnectorManager?.Connectors;
                if (conns == null) return list;
                foreach (Connector c in conns)
                    if (c?.Domain == Domain.DomainHvac) list.Add(c);
            }
            catch { }
            return list;
        }

        private static double MaxConnectorAirFlowLs(FamilyInstance fi)
        {
            try
            {
                double maxLs = 0;
                var conns = fi.MEPModel?.ConnectorManager?.Connectors;
                if (conns == null) return 0;
                foreach (Connector c in conns)
                {
                    try
                    {
                        if (c.Domain != Domain.DomainHvac) continue;
                        double internalVal = c.Flow;
                        double ls = UnitUtils.ConvertFromInternalUnits(internalVal, UnitTypeId.LitersPerSecond);
                        if (ls > maxLs) maxLs = ls;
                    }
                    catch { }
                }
                return maxLs;
            }
            catch { return 0; }
        }

        /// <summary>
        /// Walk downstream of the connector, stamping HVC_FLOW_LS on every
        /// duct encountered. Stops at terminals + other equipment. Returns
        /// stamp count. `visited` is global across all IDUs so a shared
        /// downstream segment isn't stamped twice.
        /// </summary>
        private static int WalkAndStamp(Connector startC, double flowLs,
            string provenance, HashSet<long> visited, int depth)
        {
            if (startC == null || depth > MaxWalk) return 0;
            int stamped = 0;
            try
            {
                var refs = startC.AllRefs;
                if (refs == null) return 0;
                foreach (Connector other in refs)
                {
                    var owner = other?.Owner;
                    if (owner == null) continue;
                    long id = owner.Id.Value;
                    if (!visited.Add(id)) continue;

                    if (owner.Category != null &&
                        (BuiltInCategory)owner.Category.Id.Value == BuiltInCategory.OST_DuctCurves)
                    {
                        try
                        {
                            ParameterHelpers.SetString(owner, "HVC_FLOW_LS",
                                $"{flowLs:F1}", overwrite: true);
                            ParameterHelpers.SetString(owner, "HVC_LOAD_SOURCE_TXT",
                                provenance, overwrite: true);
                            stamped++;
                        }
                        catch (Exception ex) { StingLog.Warn($"WalkAndStamp duct {id}: {ex.Message}"); }
                    }
                    else if (owner.Category != null &&
                             (BuiltInCategory)owner.Category.Id.Value == BuiltInCategory.OST_DuctTerminal)
                    {
                        // Terminal — end of the walk, don't recurse further.
                        continue;
                    }
                    else if (owner.Category != null &&
                             (BuiltInCategory)owner.Category.Id.Value == BuiltInCategory.OST_MechanicalEquipment)
                    {
                        // Hit another piece of equipment — stop here too.
                        continue;
                    }

                    // Recurse through the next element's far-side connector.
                    var ownerConns = ConnectorsOf(owner);
                    if (ownerConns == null) continue;
                    foreach (Connector cm in ownerConns)
                    {
                        if (cm == null || cm.Id == other.Id) continue;
                        stamped += WalkAndStamp(cm, flowLs, provenance, visited, depth + 1);
                    }
                }
            }
            catch (Exception ex) { StingLog.Warn($"WalkAndStamp: {ex.Message}"); }
            return stamped;
        }

        private static IEnumerable<Connector> ConnectorsOf(Element el)
        {
            try
            {
                if (el is MEPCurve mc) return ToList(mc.ConnectorManager?.Connectors);
                if (el is FamilyInstance fi) return ToList(fi.MEPModel?.ConnectorManager?.Connectors);
            }
            catch { }
            return null;
        }

        private static IList<Connector> ToList(ConnectorSet set)
        {
            var list = new List<Connector>();
            if (set == null) return list;
            foreach (Connector c in set) list.Add(c);
            return list;
        }

        private static double ReadDouble(Element el, string name)
        {
            try
            {
                var p = el.LookupParameter(name);
                if (p == null) return 0;
                if (p.StorageType == StorageType.Double) return p.AsDouble();
                if (p.StorageType == StorageType.String &&
                    double.TryParse(p.AsString(),
                        System.Globalization.NumberStyles.Any,
                        System.Globalization.CultureInfo.InvariantCulture,
                        out double v)) return v;
            }
            catch { }
            return 0;
        }
    }
}
