// TrapDesigner — fixture trap selection per IPC 2021 / BS EN 12056-2.
// Phase 178c. Returns trap type, seal depth, and maximum un-vented
// branch length, optionally writes back PLM_TRAP_TYPE / PLM_TRAP_SEAL.

using System;
using Autodesk.Revit.DB;
using StingTools.Core;
using StingTools.Standards.BSEN12056;

namespace StingTools.Core.Plumbing
{
    public class TrapSelection
    {
        public ElementId FixtureId       { get; set; }
        public string TrapType           { get; set; } = "P-TRAP";
        public int SealDepthMm           { get; set; } = 50;
        public double MaxBranchLengthM   { get; set; } = 1.5;
        public bool RequiresDeepSeal     { get; set; }
        public string Notes              { get; set; } = "";
    }

    public static class TrapDesigner
    {
        public static TrapSelection SelectTrap(Element fixture)
        {
            var sel = new TrapSelection { FixtureId = fixture?.Id ?? ElementId.InvalidElementId };
            if (fixture == null) return sel;

            string name = "";
            try
            {
                name = ((fixture as FamilyInstance)?.Symbol?.Family?.Name ?? "") + " " +
                       (fixture.Name ?? "");
            }
            catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
            string upper = name.ToUpperInvariant();

            if (upper.Contains("WC") || upper.Contains("TOILET") || upper.Contains("URINAL") || upper.Contains("WATER CLOSET"))
            {
                sel.TrapType = "INTEGRAL";
                sel.SealDepthMm = 50;
                sel.MaxBranchLengthM = 6.0;
                sel.Notes = "Integral water seal — no separate trap required";
            }
            else if (upper.Contains("FLOOR DRAIN") || upper.Contains("GULLY"))
            {
                sel.TrapType = "DEEP-SEAL";
                sel.SealDepthMm = 75;
                sel.MaxBranchLengthM = 0.0;
                sel.RequiresDeepSeal = true;
                sel.Notes = "Deep-seal trap — anti-evaporation";
            }
            else if (upper.Contains("LAB") || upper.Contains("BOTTLE"))
            {
                sel.TrapType = "BOTTLE-TRAP";
                sel.SealDepthMm = 75;
                sel.MaxBranchLengthM = 1.5;
                sel.Notes = "Bottle trap — accessible cleanout";
            }
            else if (upper.Contains("KITCHEN") || upper.Contains("WASHING") || upper.Contains("SINK") || upper.Contains("CLOTHES"))
            {
                sel.TrapType = "P-TRAP";
                sel.SealDepthMm = 75;
                sel.MaxBranchLengthM = 1.7;
                sel.RequiresDeepSeal = true;
            }
            else
            {
                sel.TrapType = "P-TRAP";
                sel.SealDepthMm = 50;
                sel.MaxBranchLengthM = 1.5;
            }

            try
            {
                var range = BSen12056Standards.GetTrapSealRange(name);
                if (sel.SealDepthMm < range.minSealDepthMm)
                    sel.SealDepthMm = range.minSealDepthMm;
            }
            catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
            return sel;
        }

        public static void WriteBack(Document doc, TrapSelection sel)
        {
            if (doc == null || sel == null || sel.FixtureId == ElementId.InvalidElementId) return;
            var el = doc.GetElement(sel.FixtureId);
            if (el == null) return;
            try
            {
                var pType = el.LookupParameter(ParamRegistry.PLM_TRAP_TYPE);
                if (pType != null && !pType.IsReadOnly && pType.StorageType == StorageType.String)
                    pType.Set(sel.TrapType);
                var pSeal = el.LookupParameter(ParamRegistry.PLM_TRAP_SEAL);
                if (pSeal != null && !pSeal.IsReadOnly)
                {
                    if (pSeal.StorageType == StorageType.String) pSeal.Set(sel.SealDepthMm.ToString());
                    else if (pSeal.StorageType == StorageType.Double) pSeal.Set((double)sel.SealDepthMm);
                    else if (pSeal.StorageType == StorageType.Integer) pSeal.Set(sel.SealDepthMm);
                }
            }
            catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
        }
    }
}
