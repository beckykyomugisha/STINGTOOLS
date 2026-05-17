// Phase 139.2 — Plaster / finish-face offset resolver.
//
// At placement time we want sockets and switches to sit on the room-side
// finish face, not the structural core face. This helper inspects the
// host wall's CompoundStructure and sums any layer that walks like a
// finish layer (function or material name match) so the engine can
// shift the candidate position by exactly that distance along the wall
// normal.
//
// All return values are in Revit internal units (feet). 0.0 = no offset.

using System;
using System.Text.RegularExpressions;
using Autodesk.Revit.DB;

namespace StingTools.Core.Placement
{
    public static class PlasterOffsetResolver
    {
        private const double MmToFt = 1.0 / 304.8;

        // Word-boundary regex compiled once. The substring fallback only
        // matters when CompoundStructureLayer.Function is missing/Unknown,
        // so we only need to recognise the common AEC finish materials.
        private static readonly Regex FinishMaterialPattern = new Regex(
            @"\b(plaster|skim|render|plasterboard|gypsum|MF\s+ceiling|MF\s+lining)\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>Resolve an interior-side finish-face offset for a wall host.</summary>
        public static double Resolve(Wall wall, PlacementRule rule)
        {
            if (rule == null) return 0.0;
            string mode = (rule.PlasterOffsetMode ?? "None").Trim();
            if (string.Equals(mode, "None", StringComparison.OrdinalIgnoreCase) || string.IsNullOrEmpty(mode))
                return 0.0;
            if (string.Equals(mode, "Fixed", StringComparison.OrdinalIgnoreCase))
                return rule.PlasterOffsetFixedMm * MmToFt;
            if (!string.Equals(mode, "Auto", StringComparison.OrdinalIgnoreCase))
                return 0.0;
            if (wall == null) return 0.0;

            try
            {
                var wt = wall.WallType;
                if (wt == null) return 0.0;
                CompoundStructure cs = null;
                try { cs = wt.GetCompoundStructure(); } catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
                if (cs == null) return 0.0;
                return SumFinishThicknessFt(cs, wall.Document);
            }
            catch (Exception ex)
            {
                StingLog.Warn($"PlasterOffsetResolver.Resolve(Wall): {ex.Message}");
                return 0.0;
            }
        }

        /// <summary>Resolve a finish-face offset for a ceiling host (BESA pendant alignment).</summary>
        public static double ResolveForCeiling(Ceiling ceiling, PlacementRule rule)
        {
            if (rule == null) return 0.0;
            string mode = (rule.PlasterOffsetMode ?? "None").Trim();
            if (string.Equals(mode, "None", StringComparison.OrdinalIgnoreCase) || string.IsNullOrEmpty(mode))
                return 0.0;
            if (string.Equals(mode, "Fixed", StringComparison.OrdinalIgnoreCase))
                return rule.PlasterOffsetFixedMm * MmToFt;
            if (!string.Equals(mode, "Auto", StringComparison.OrdinalIgnoreCase))
                return 0.0;
            if (ceiling == null) return 0.0;

            try
            {
                var ct = ceiling.Document.GetElement(ceiling.GetTypeId()) as CeilingType;
                if (ct == null) return 0.0;
                CompoundStructure cs = null;
                try { cs = ct.GetCompoundStructure(); } catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
                if (cs == null) return 0.0;
                return SumFinishThicknessFt(cs, ceiling.Document);
            }
            catch (Exception ex)
            {
                StingLog.Warn($"PlasterOffsetResolver.ResolveForCeiling: {ex.Message}");
                return 0.0;
            }
        }

        // ── Internal ────────────────────────────────────────────────

        private static double SumFinishThicknessFt(CompoundStructure cs, Document doc)
        {
            double totalFt = 0.0;
            try
            {
                var layers = cs.GetLayers();
                if (layers == null) return 0.0;
                foreach (var layer in layers)
                {
                    if (layer == null) continue;
                    if (!IsFinishLayer(layer, doc)) continue;
                    totalFt += Math.Max(0.0, layer.Width); // Width is in feet (Revit internal).
                }
            }
            catch (Exception ex)
            {
                StingLog.Warn($"PlasterOffsetResolver.SumFinishThicknessFt: {ex.Message}");
            }
            return totalFt;
        }

        private static bool IsFinishLayer(CompoundStructureLayer layer, Document doc)
        {
            try
            {
                var fn = layer.Function;
                if (fn == MaterialFunctionAssignment.Finish1
                 || fn == MaterialFunctionAssignment.Finish2
                 || fn == MaterialFunctionAssignment.Membrane)
                    return true;

                // Phase 139.5 Q4 — drywall partitions sometimes flag steel
                // studs as Function = Substrate; we must NOT treat the studs
                // as finish (would push fixtures 100+ mm into the wall).
                // Only honour Substrate when the material name explicitly
                // matches the finish pattern (plasterboard, gypsum, etc.).
                bool isFinishMaterial = false;
                if (doc != null && layer.MaterialId != null && layer.MaterialId != ElementId.InvalidElementId)
                {
                    if (doc.GetElement(layer.MaterialId) is Material mat
                        && !string.IsNullOrEmpty(mat.Name))
                        isFinishMaterial = FinishMaterialPattern.IsMatch(mat.Name);
                }
                if (fn == MaterialFunctionAssignment.Substrate)
                    return isFinishMaterial; // strict — only finish-named substrate counts.

                return isFinishMaterial;
            }
            catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
            return false;
        }
    }
}
