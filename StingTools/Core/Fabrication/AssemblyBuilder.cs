// StingTools v4 MVP — AssemblyBuilder.
//
// Wraps Autodesk.Revit.DB.AssemblyInstance.Create with the v4 naming
// convention SP-{DISC}-{SYS}-{LVL}-{SEQ} and writes the source-token
// metadata (ASS_SPOOL_NR_TXT, ASS_FAB_LOC_TXT etc.) onto the new
// AssemblyType.
//
// All Revit API mutations are wrapped in the caller's Transaction —
// the builder itself does NOT open a Transaction so it can be called
// inside the FabricationEngine's per-discipline transaction group.

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;

namespace StingTools.Core.Fabrication
{
    public static class AssemblyBuilder
    {
        /// <summary>
        /// Build a single AssemblyInstance from the supplied element ids.
        /// Caller MUST already be inside an active Transaction. Returns
        /// the new AssemblyInstance ElementId or null on failure (and
        /// pushes a warning into result.Warnings).
        /// </summary>
        public static ElementId Build(
            Document doc,
            string discipline,
            IList<ElementId> elementIds,
            int sequenceNumber,
            FabricationResult result,
            AssemblyGrouper.SpoolMetrics metrics = null)
        {
            if (doc == null || elementIds == null || elementIds.Count == 0)
                return null;

            try
            {
                // Naming SP-{DISC}-{SYS}-{LVL}-{SEQ}
                ElementId firstId = elementIds.First();
                Element first = doc.GetElement(firstId);
                string disc = ShortDiscipline(discipline);
                string sys  = ReadString(first, "ASS_SYSTEM_TYPE_TXT");
                string lvl  = ResolveLevelCode(doc, first);
                string seq  = sequenceNumber.ToString("D4");
                string assyName = $"SP-{disc}-{sys}-{lvl}-{seq}";

                // AssemblyInstance.Create(doc, IList<ElementId>, ElementId categoryId) —
                // verified against Revit 2025 API. Category id taken from
                // the first element so the assembly inherits the
                // discipline. Caller's Transaction is required.
                ElementId catId = first?.Category?.Id ?? new ElementId(BuiltInCategory.OST_GenericModel);
                AssemblyInstance ai = AssemblyInstance.Create(doc, elementIds, catId);
                if (ai == null)
                {
                    result.Warnings.Add($"AssemblyBuilder: Create returned null for {assyName}");
                    return null;
                }

                // Set the AssemblyType name for traceability
                try
                {
                    AssemblyType at = doc.GetElement(ai.GetTypeId()) as AssemblyType;
                    if (at != null) at.Name = assyName;
                }
                catch (Exception ex)
                {
                    result.Warnings.Add($"AssemblyBuilder: rename AssemblyType failed: {ex.Message}");
                }

                // Tag the assembly instance with v4 spool metadata
                TrySetString(ai, AssyParams.SPOOL_NR_TXT,    assyName);
                TrySetString(ai, AssyParams.FAB_LOC_TXT,     ResolveFabLocation(discipline));
                TrySetString(ai, AssyParams.FAB_STATUS_TXT,  "DRAFT");
                TrySetString(ai, AssyParams.BOM_REV_TXT,     "P01");
                TrySetInt   (ai, AssyParams.FAB_SEQ_NR,      sequenceNumber);

                // Level code for the SP-{disc}-{sys}-{lvl}-{seq}
                // convention, duplicated onto the assembly so
                // ShopDrawingComposer can read it without resolving
                // the first member's Level each time.
                TrySetString(ai, "ASS_LVL_COD_TXT", lvl);

                // Phase E — spool metrics from the grouper. Written
                // back to the 8 computed parameters so BOQ, cut-list
                // and MAJ exports don't have to recompute volume,
                // weight, or weld counts independently.
                if (metrics != null)
                {
                    TrySetDouble(ai, AssyParams.LENGTH_TOTAL_MM, metrics.LengthTotalMm);
                    TrySetDouble(ai, AssyParams.WEIGHT_KG,       metrics.WeightKg);
                    TrySetInt   (ai, AssyParams.WELD_COUNT_NR,   metrics.WeldCount);
                    TrySetInt   (ai, AssyParams.FLANGE_COUNT_NR, metrics.FlangeCount);
                    TrySetInt   (ai, AssyParams.FITTING_COUNT_NR, metrics.FittingCount);
                    TrySetInt   (ai, AssyParams.CUT_COUNT_NR,    metrics.CutCount);
                }

                return ai.Id;
            }
            catch (Exception ex)
            {
                result.Warnings.Add($"AssemblyBuilder: Build failed: {ex.Message}");
                StingLog.Warn($"AssemblyBuilder: {ex.Message}");
                return null;
            }
        }

        private static string ShortDiscipline(string longName)
        {
            if (string.IsNullOrEmpty(longName)) return "GEN";
            switch (longName.ToUpperInvariant())
            {
                case "ELECTRICAL": return "E";
                case "PIPE":
                case "PLUMBING":   return "P";
                case "DUCT":
                case "HVAC":       return "M";
            }
            return longName.Length > 3 ? longName.Substring(0, 3).ToUpperInvariant() : longName.ToUpperInvariant();
        }

        private static string ResolveFabLocation(string discipline)
        {
            switch ((discipline ?? "").ToUpperInvariant())
            {
                case "DUCT":
                case "HVAC":
                case "PIPE_LARGEBORE": return "WORKSHOP";
                default:               return "SITE";
            }
        }

        private static string ResolveLevelCode(Document doc, Element el)
        {
            try
            {
                if (el?.LevelId != null && el.LevelId != ElementId.InvalidElementId)
                {
                    var lvl = doc.GetElement(el.LevelId) as Level;
                    if (lvl != null && !string.IsNullOrEmpty(lvl.Name))
                        return SanitiseLevel(lvl.Name);
                }
            }
            catch (Exception ex) { StingLog.Warn($"AssemblyBuilder: level read failed: {ex.Message}"); }
            return "XX";
        }

        private static string SanitiseLevel(string raw)
        {
            string up = (raw ?? "").ToUpperInvariant();
            if (up.Contains("GROUND") || up.Contains("GF"))  return "GF";
            if (up.Contains("ROOF")   || up.StartsWith("RF")) return "RF";
            if (up.StartsWith("B"))   return "B" + System.Text.RegularExpressions.Regex.Match(up, "\\d+").Value;
            if (up.StartsWith("L"))   return "L" + System.Text.RegularExpressions.Regex.Match(up, "\\d+").Value.PadLeft(2, '0');
            var match = System.Text.RegularExpressions.Regex.Match(up, "\\d+");
            return match.Success ? "L" + match.Value.PadLeft(2, '0') : "XX";
        }

        private static string ReadString(Element el, string param)
        {
            try { return el?.LookupParameter(param)?.AsString() ?? ""; } catch { return ""; }
        }
        private static void TrySetString(Element el, string param, string val)
        {
            try { var p = el.LookupParameter(param);
                  if (p != null && !p.IsReadOnly && p.StorageType == StorageType.String) p.Set(val); }
            catch (Exception ex) { StingLog.Warn($"AssemblyBuilder.SetString {param}: {ex.Message}"); }
        }
        private static void TrySetInt(Element el, string param, int val)
        {
            try { var p = el.LookupParameter(param);
                  if (p == null || p.IsReadOnly) return;
                  if (p.StorageType == StorageType.Integer) p.Set(val);
                  else if (p.StorageType == StorageType.String) p.Set(val.ToString()); }
            catch (Exception ex) { StingLog.Warn($"AssemblyBuilder.SetInt {param}: {ex.Message}"); }
        }
        private static void TrySetDouble(Element el, string param, double val)
        {
            try { var p = el.LookupParameter(param);
                  if (p == null || p.IsReadOnly) return;
                  if (p.StorageType == StorageType.Double)
                      p.Set(val);
                  else if (p.StorageType == StorageType.String)
                      p.Set(val.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)); }
            catch (Exception ex) { StingLog.Warn($"AssemblyBuilder.SetDouble {param}: {ex.Message}"); }
        }
    }
}
