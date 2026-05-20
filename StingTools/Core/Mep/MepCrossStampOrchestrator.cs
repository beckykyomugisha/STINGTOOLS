// MepCrossStampOrchestrator.cs — Phase 188
//
// Bridges Revit's native MEP parameters into STING shared parameters
// so schedules, BOQ paragraph builders, tag containers and downstream
// commands see the data without having to re-query the API.
//
// Every target parameter below already exists in MR_PARAMETERS.txt —
// nothing new is added. The orchestrator only reads native built-in
// params (RBS_DUCT_FLOW_PARAM, RBS_PIPE_FLOW_PARAM,
// RBS_ELEC_APPARENT_CURRENT_PARAM, etc.) and writes their string-form
// equivalents to the STING side.
//
// Closes the cross-discipline visibility gap flagged in the
// "what more stamps" review.

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Electrical;
using Autodesk.Revit.DB.Mechanical;
using Autodesk.Revit.DB.Plumbing;
using StingTools.Core;

namespace StingTools.Core.Mep
{
    public class MepCrossStampResult
    {
        public int DuctsStamped     { get; set; }
        public int PipesStamped     { get; set; }
        public int CircuitsStamped  { get; set; }
        public int FixturesStamped  { get; set; }
        public int InsulationStamped { get; set; }
        public List<string> Warnings { get; } = new List<string>();
    }

    public static class MepCrossStampOrchestrator
    {
        // Revit internal storage: flow in feet³/s (ducts) and ft³/s (pipes too).
        // Conversion factors below get us to project units (m³/h, l/s).
        private const double FtToM      = 0.3048;
        private const double FtCubedToM3 = FtToM * FtToM * FtToM;     // ft³ → m³

        /// <summary>
        /// Walk every MEP-bearing element and stamp the native Revit value
        /// onto its STING shared-param equivalent. Caller owns the
        /// Transaction. Counters returned for reporting.
        /// </summary>
        public static MepCrossStampResult AnalyseModel(Document doc)
        {
            var r = new MepCrossStampResult();
            if (doc == null) return r;
            try
            {
                StampDucts(doc, r);
                StampPipes(doc, r);
                StampCircuits(doc, r);
                StampFixtures(doc, r);
            }
            catch (Exception ex)
            {
                StingLog.Error("MepCrossStampOrchestrator.AnalyseModel", ex);
                r.Warnings.Add(ex.Message);
            }
            return r;
        }

        // ── Ducts ────────────────────────────────────────────────────────
        private static void StampDucts(Document doc, MepCrossStampResult r)
        {
            foreach (var d in new FilteredElementCollector(doc).OfClass(typeof(Duct))
                .WhereElementIsNotElementType().Cast<Duct>())
            {
                try
                {
                    // RBS_DUCT_FLOW_PARAM is stored in Revit internal flow units
                    // (ft³/s). Use UnitUtils-style direct conversion to m³/h.
                    var fp = d.get_Parameter(BuiltInParameter.RBS_DUCT_FLOW_PARAM);
                    if (fp != null && fp.StorageType == StorageType.Double)
                    {
                        double m3s = fp.AsDouble() * FtCubedToM3;
                        double m3h = m3s * 3600.0;
                        ParameterHelpers.SetString(d, "HVC_DUCT_FLOWRATE_M3H",
                            $"{m3h:F1}", overwrite: true);
                        // Also stamp HVC_FLOW_LS (l/s) since downstream sizers
                        // read this. Use a project param if bound, soft-fail
                        // otherwise — saves the next sizing run a re-conversion.
                        try { ParameterHelpers.SetString(d, "HVC_FLOW_LS",
                            $"{m3s * 1000.0:F2}", overwrite: true); } catch { }
                        r.DuctsStamped++;
                    }
                    // MEP system name → ASS_MEP_SYS_NAME_TXT
                    string sysName = d.MEPSystem?.Name ?? "";
                    if (!string.IsNullOrEmpty(sysName))
                        ParameterHelpers.SetString(d, ParamRegistry.MEP_SYS_NAME,
                            sysName, overwrite: true);
                    // Insulation thickness → HVC_DCT_INSULATION_THK_MM
                    var ins = d.get_Parameter(BuiltInParameter.RBS_REFERENCE_INSULATION_THICKNESS);
                    if (ins != null && ins.StorageType == StorageType.Double)
                    {
                        double mm = ins.AsDouble() * 304.8;
                        if (mm > 0.01)
                        {
                            ParameterHelpers.SetString(d, "HVC_DCT_INSULATION_THK_MM",
                                $"{mm:F0}", overwrite: true);
                            r.InsulationStamped++;
                        }
                    }
                }
                catch (Exception ex) { r.Warnings.Add($"Duct {d.Id}: {ex.Message}"); }
            }
        }

        // ── Pipes ────────────────────────────────────────────────────────
        private static void StampPipes(Document doc, MepCrossStampResult r)
        {
            foreach (var p in new FilteredElementCollector(doc).OfClass(typeof(Pipe))
                .WhereElementIsNotElementType().Cast<Pipe>())
            {
                try
                {
                    var fp = p.get_Parameter(BuiltInParameter.RBS_PIPE_FLOW_PARAM);
                    if (fp != null && fp.StorageType == StorageType.Double)
                    {
                        // Pipe flow in Revit internal is ft³/s. Convert to l/s.
                        double m3s = fp.AsDouble() * FtCubedToM3;
                        double lps = m3s * 1000.0;
                        ParameterHelpers.SetString(p, "HVC_PIPE_FLOWRATE_LPS",
                            $"{lps:F2}", overwrite: true);
                        // PLM_FLOW_LS is read by AutoSizePipe; stamp it too so
                        // the sizer doesn't have to recompute.
                        try { ParameterHelpers.SetString(p, "PLM_FLOW_LS",
                            $"{lps:F2}", overwrite: true); } catch { }
                        r.PipesStamped++;
                    }
                    string sysName = p.MEPSystem?.Name ?? "";
                    if (!string.IsNullOrEmpty(sysName))
                        ParameterHelpers.SetString(p, ParamRegistry.MEP_SYS_NAME,
                            sysName, overwrite: true);
                    // Pipe insulation thickness — same param family as ducts.
                    var ins = p.get_Parameter(BuiltInParameter.RBS_REFERENCE_INSULATION_THICKNESS);
                    if (ins != null && ins.StorageType == StorageType.Double)
                    {
                        double mm = ins.AsDouble() * 304.8;
                        if (mm > 0.01)
                        {
                            try { ParameterHelpers.SetString(p, "PLM_PIPE_INS_THK_MM",
                                $"{mm:F0}", overwrite: false); } catch { /* may not be bound */ }
                            r.InsulationStamped++;
                        }
                    }
                }
                catch (Exception ex) { r.Warnings.Add($"Pipe {p.Id}: {ex.Message}"); }
            }
        }

        // ── Electrical circuits ─────────────────────────────────────────
        private static void StampCircuits(Document doc, MepCrossStampResult r)
        {
            foreach (var sys in new FilteredElementCollector(doc)
                .OfClass(typeof(ElectricalSystem))
                .Cast<ElectricalSystem>())
            {
                try
                {
                    // Apparent current → ELC_CKT_CUR_A (existing)
                    var cp = sys.get_Parameter(BuiltInParameter.RBS_ELEC_APPARENT_CURRENT_PARAM);
                    if (cp != null && cp.StorageType == StorageType.Double)
                    {
                        ParameterHelpers.SetString(sys, "ELC_CKT_CUR_A",
                            $"{cp.AsDouble():F1}", overwrite: true);
                    }
                    // Voltage → ELC_CKT_VLT_V (existing)
                    var vp = sys.get_Parameter(BuiltInParameter.RBS_ELEC_VOLTAGE);
                    if (vp != null && vp.StorageType == StorageType.Double)
                    {
                        ParameterHelpers.SetString(sys, "ELC_CKT_VLT_V",
                            $"{vp.AsDouble():F0}", overwrite: true);
                    }
                    // Phase count → ELC_CKT_PHASE_COUNT_NR (existing)
                    try
                    {
                        int poles = sys.PolesNumber;
                        ParameterHelpers.SetString(sys, "ELC_CKT_PHASE_COUNT_NR",
                            poles >= 3 ? "3" : "1", overwrite: true);
                    }
                    catch (Exception exP) { StingLog.Warn($"Phase poles {sys.Id}: {exP.Message}"); }
                    // Apparent load kW → ELC_CKT_PWR_KW (existing) from
                    // RBS_ELEC_APPARENT_LOAD when present.
                    var lp = sys.get_Parameter(BuiltInParameter.RBS_ELEC_APPARENT_LOAD);
                    if (lp != null && lp.StorageType == StorageType.Double)
                    {
                        // Revit internal is W; convert to kW.
                        ParameterHelpers.SetString(sys, "ELC_CKT_PWR_KW",
                            $"{lp.AsDouble() / 1000.0:F2}", overwrite: true);
                    }
                    // System name + panel name
                    string sysName = sys.Name ?? "";
                    if (!string.IsNullOrEmpty(sysName))
                        ParameterHelpers.SetString(sys, ParamRegistry.MEP_SYS_NAME,
                            sysName, overwrite: true);
                    r.CircuitsStamped++;
                }
                catch (Exception ex) { r.Warnings.Add($"Circuit {sys.Id}: {ex.Message}"); }
            }
        }

        // ── MEP fixtures / equipment (lighting, electrical, plumbing) ───
        private static void StampFixtures(Document doc, MepCrossStampResult r)
        {
            var cats = new[]
            {
                BuiltInCategory.OST_MechanicalEquipment,
                BuiltInCategory.OST_ElectricalEquipment,
                BuiltInCategory.OST_ElectricalFixtures,
                BuiltInCategory.OST_PlumbingFixtures,
                BuiltInCategory.OST_LightingFixtures,
                BuiltInCategory.OST_LightingDevices,
                BuiltInCategory.OST_DuctTerminal,
            };
            var filter = new ElementMulticategoryFilter(cats);
            foreach (var el in new FilteredElementCollector(doc)
                .WhereElementIsNotElementType()
                .WherePasses(filter))
            {
                try
                {
                    // For family instances, MEP system context is reached
                    // via the connectors. Sniff the first connected MEP
                    // system name and stamp it.
                    if (el is FamilyInstance fi)
                    {
                        string sysName = fi.MEPModel?.ConnectorManager?.Connectors?
                            .Cast<Connector>()
                            .Select(c => c.MEPSystem?.Name ?? "")
                            .FirstOrDefault(n => !string.IsNullOrEmpty(n)) ?? "";
                        if (!string.IsNullOrEmpty(sysName))
                            ParameterHelpers.SetString(fi, ParamRegistry.MEP_SYS_NAME,
                                sysName, overwrite: false);
                    }
                    r.FixturesStamped++;
                }
                catch (Exception ex) { r.Warnings.Add($"Fixture {el.Id}: {ex.Message}"); }
            }
        }
    }
}
