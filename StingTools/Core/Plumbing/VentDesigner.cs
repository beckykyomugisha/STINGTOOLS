// VentDesigner — vent-pipe sizing per BS EN 12056-2 Annex B (UK)
// or IPC 2021 Table 916.1 (US/intl). Phase 178c.
//
// Returns a list of VentRequirement records (read-only — does not
// create Revit elements). The AutoSizeDrainage command consumes this
// for its "Vent" panel section, and a separate downstream router
// (post Phase 178c, not in this commit) creates pipework.

using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Plumbing;
using StingTools.Core;
using StingTools.Standards.BSEN12056;
using StingTools.Standards.IPC2021;

namespace StingTools.Core.Plumbing
{
    public class VentRequirement
    {
        public ElementId DrainPipeId        { get; set; }
        public int DrainDnMm                { get; set; }
        public double Dfu                   { get; set; }
        public int RecommendedVentDnMm      { get; set; }
        public double MaxVentLengthM        { get; set; }
        public bool RequiresAav             { get; set; }
        public bool RequiresReliefVent      { get; set; }
        public string Notes                 { get; set; } = "";
        public string CodeUsed              { get; set; } = "BS-UK";
    }

    public static class VentDesigner
    {
        public static List<VentRequirement> DesignVents(Document doc, Dictionary<ElementId, double> dfuMap)
        {
            var list = new List<VentRequirement>();
            if (doc == null || dfuMap == null) return list;
            string code = ResolveCode(doc);

            foreach (var kv in dfuMap)
            {
                Pipe pipe = null;
                try { pipe = doc.GetElement(kv.Key) as Pipe; } catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
                if (pipe == null) continue;

                int drainDn = 0;
                try { drainDn = (int)Math.Round(pipe.Diameter * 0.3048 * 1000.0); } catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
                double du = kv.Value;
                if (drainDn <= 0 || du <= 0.001) continue;

                int ventDn = code.StartsWith("IPC", StringComparison.OrdinalIgnoreCase)
                    ? IPCSiAdapter.GetVentSizeMm(drainDn, ventLengthM: 9.0)
                    : BSen12056Standards.GetVentPipeDnMm(drainDn, du);
                if (ventDn <= 0) ventDn = drainDn / 2;

                double maxLenM = code.StartsWith("IPC", StringComparison.OrdinalIgnoreCase)
                    ? IpcMaxVentLengthM(drainDn)
                    : 18.0;

                bool aavReq = false;
                try
                {
                    var p = pipe.LookupParameter(ParamRegistry.PLM_AAV_REQ);
                    if (p != null && p.HasValue && p.StorageType == StorageType.Integer)
                        aavReq = p.AsInteger() == 1;
                }
                catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }

                bool reliefReq = du > 50.0; // proxy for >10-storey stack

                list.Add(new VentRequirement
                {
                    DrainPipeId        = pipe.Id,
                    DrainDnMm          = drainDn,
                    Dfu                = du,
                    RecommendedVentDnMm= ventDn,
                    MaxVentLengthM     = maxLenM,
                    RequiresAav        = aavReq,
                    RequiresReliefVent = reliefReq,
                    CodeUsed           = code,
                    Notes              = reliefReq ? "Tall stack — relief vent recommended" : ""
                });
            }
            return list;
        }

        public static bool AavRequired(VentRequirement req, double actualVentLengthM)
        {
            if (req == null) return false;
            return actualVentLengthM > req.MaxVentLengthM || req.RequiresAav;
        }

        // IPC 2021 Table 916.1 maximum developed vent length (m).
        private static double IpcMaxVentLengthM(int drainDnMm)
        {
            if (drainDnMm <= 32)  return 9;
            if (drainDnMm <= 40)  return 18;
            if (drainDnMm <= 50)  return 18;
            if (drainDnMm <= 75)  return 55;
            if (drainDnMm <= 100) return 107;
            if (drainDnMm <= 125) return 76;
            if (drainDnMm <= 150) return 76;
            return 91;
        }

        private static string ResolveCode(Document doc)
        {
            try
            {
                var p = doc?.ProjectInformation?.LookupParameter(ParamRegistry.PRJ_PLUMBING_CODE);
                if (p != null && p.HasValue && p.StorageType == StorageType.String)
                {
                    var s = p.AsString();
                    if (!string.IsNullOrWhiteSpace(s)) return s.Trim().ToUpperInvariant();
                }
            }
            catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
            return "BS-UK";
        }
    }
}
