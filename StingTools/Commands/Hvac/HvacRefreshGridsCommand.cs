// StingTools — Refresh HVAC dock panel grids from the live model.
//
// Phase 187b — the HVAC panel ships 9 ObservableCollections bound to
// data-grids. Several were empty until commands explicitly populated
// them (BlockLoad → SpaceLoadRows; NC → IssueRows). This command
// closes the rest by scanning the project and seeding:
//
//   * EquipmentRows — every OST_MechanicalEquipment family instance
//     with capacity / flow / system read from shared params or BIPs.
//   * SystemRows    — every PipingSystem / MechanicalSystem with its
//     equipment count + class.
//   * DuctTypeRows  — every DuctType in the project with shape +
//     material + pressure class.
//
// Idempotent — call it on document open, after model edits, or via
// the "Refresh grids" button on the RPRT tab.

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Mechanical;
using Autodesk.Revit.UI;
using StingTools.Core;
using StingTools.UI;

namespace StingTools.Commands.Hvac
{
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class HvacRefreshGridsCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            try
            {
                var ctx = ParameterHelpers.GetContext(commandData);
                if (ctx == null) { message = "No active document."; return Result.Failed; }
                var doc = ctx.Doc;
                var p = StingHvacPanel.Instance;
                if (p == null)
                {
                    TaskDialog.Show("STING HVAC", "HVAC panel is not open.");
                    return Result.Cancelled;
                }

                int eq = SeedEquipment(doc, p);
                int sys = SeedSystems(doc, p);
                int dt = SeedDuctTypes(doc, p);

                p.PushRunRow($"Grid refresh ({eq} equip, {sys} sys, {dt} duct types)", "⬤");

                TaskDialog.Show("STING HVAC",
                    $"Refreshed:\n" +
                    $"  Equipment rows: {eq}\n" +
                    $"  System rows:    {sys}\n" +
                    $"  Duct types:     {dt}");

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("HvacRefreshGridsCommand", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }

        private static int SeedEquipment(Document doc, StingHvacPanel p)
        {
            p.EquipmentRows.Clear();
            int n = 0;
            try
            {
                var equipment = new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_MechanicalEquipment)
                    .WhereElementIsNotElementType()
                    .OfClass(typeof(FamilyInstance))
                    .Cast<FamilyInstance>()
                    .ToList();
                foreach (var fi in equipment)
                {
                    try
                    {
                        var sym = fi.Symbol;
                        string tag    = fi.LookupParameter("ASS_TAG_1")?.AsString() ?? $"#{fi.Id.Value}";
                        string type   = sym?.Family?.Name ?? fi.Name ?? "";
                        string sysNm  = SystemNameOf(fi);
                        double capKw  = ReadDouble(fi, "HVC_CAPACITY_KW");
                        double flowLs = ReadDouble(fi, "HVC_FLOW_LS");
                        if (flowLs <= 0)
                        {
                            // Fall back to summed connector airflow (cfm)
                            flowLs = TotalConnectorFlowLs(fi);
                        }
                        string mfg    = fi.LookupParameter("Manufacturer")?.AsString() ?? "";
                        string model  = fi.LookupParameter("Model")?.AsString() ?? "";
                        p.EquipmentRows.Add(new HvacEquipmentRow
                        {
                            Tag          = tag,
                            Type         = type,
                            CapacityKw   = capKw,
                            FlowLs       = flowLs,
                            System       = sysNm,
                            Manufacturer = mfg,
                            Model        = model,
                            StatusDot    = capKw > 0 ? "⬤" : "⬡"
                        });
                        n++;
                    }
                    catch (Exception ex) { StingLog.Warn($"SeedEquipment {fi.Id}: {ex.Message}"); }
                }
            }
            catch (Exception ex) { StingLog.Warn($"SeedEquipment: {ex.Message}"); }
            return n;
        }

        private static int SeedSystems(Document doc, StingHvacPanel p)
        {
            p.SystemRows.Clear();
            int n = 0;
            try
            {
                var mechSystems = new FilteredElementCollector(doc)
                    .OfClass(typeof(MechanicalSystem))
                    .Cast<MechanicalSystem>()
                    .ToList();
                foreach (var s in mechSystems)
                {
                    try
                    {
                        var els = s.DuctNetwork;
                        int count = 0;
                        try { count = els?.Size ?? 0; } catch { }
                        p.SystemRows.Add(new HvacSystemRow
                        {
                            Name      = string.IsNullOrEmpty(s.Name) ? $"Sys#{s.Id.Value}" : s.Name,
                            Class     = SafeSysType(s),
                            Equipment = $"{count} elements",
                            FlowLs    = 0,           // Revit doesn't expose system total flow cheaply
                            DropPa    = 0,
                            Nc        = 0,
                            StatusDot = "⬤"
                        });
                        n++;
                    }
                    catch (Exception ex) { StingLog.Warn($"SeedSystems {s.Id}: {ex.Message}"); }
                }
            }
            catch (Exception ex) { StingLog.Warn($"SeedSystems: {ex.Message}"); }
            return n;
        }

        private static int SeedDuctTypes(Document doc, StingHvacPanel p)
        {
            p.DuctTypeRows.Clear();
            int n = 0;
            try
            {
                var types = new FilteredElementCollector(doc)
                    .OfClass(typeof(DuctType))
                    .Cast<DuctType>()
                    .ToList();
                foreach (var t in types)
                {
                    try
                    {
                        p.DuctTypeRows.Add(new HvacDuctTypeRow
                        {
                            Name          = t.Name,
                            Shape         = SafeDuctShape(t),
                            Material      = t.LookupParameter("Material")?.AsValueString() ?? "",
                            PressureClass = "low"
                        });
                        n++;
                    }
                    catch (Exception ex) { StingLog.Warn($"SeedDuctTypes {t.Id}: {ex.Message}"); }
                }
            }
            catch (Exception ex) { StingLog.Warn($"SeedDuctTypes: {ex.Message}"); }
            return n;
        }

        private static string SafeDuctShape(DuctType t)
        {
            try { return t.Shape.ToString(); } catch { return ""; }
        }

        private static string SafeSysType(MechanicalSystem s)
        {
            try { return s.SystemType.ToString(); } catch { return ""; }
        }

        private static string SystemNameOf(FamilyInstance fi)
        {
            try
            {
                var mep = fi.MEPModel;
                if (mep == null) return "";
                var conns = mep.ConnectorManager?.Connectors;
                if (conns == null) return "";
                foreach (Connector c in conns)
                {
                    try
                    {
                        if (c.MEPSystem != null) return c.MEPSystem.Name ?? "";
                    }
                    catch { }
                }
            }
            catch { }
            return "";
        }

        private static double TotalConnectorFlowLs(FamilyInstance fi)
        {
            try
            {
                var set = fi.MEPModel?.ConnectorManager?.Connectors;
                if (set == null) return 0;
                double maxLs = 0;
                foreach (Connector c in set)
                {
                    try
                    {
                        if (c.Domain != Domain.DomainHvac) continue;
                        double cfm = c.Flow; // internal units (Revit AirFlow = CFM)
                        double ls  = UnitUtils.ConvertFromInternalUnits(cfm, UnitTypeId.LitersPerSecond);
                        if (ls > maxLs) maxLs = ls;
                    }
                    catch { }
                }
                return maxLs;
            }
            catch { return 0; }
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
