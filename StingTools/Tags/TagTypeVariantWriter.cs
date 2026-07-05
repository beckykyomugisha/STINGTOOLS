// ============================================================================
// TagTypeVariantWriter.cs — shared type-variant authoring for STING tag families.
//
// Universal-tag pivot (Phase 195): the type variants that STING creates on a tag
// family (PARA_STATE_1..depth gating, TAG_{size}{style}{colour}_BOOL switching,
// LEADER_ARROWHEAD, TAG_DEPTH_TIER_INT cache) are DATA-DRIVEN and completely
// family-independent — they come from tag_style_catalogue.json, not from any
// per-family bespoke spec. That is exactly what makes the recategorise conveyor
// safe: type props are never "lost", they are re-created from the catalogue.
//
// This loop was previously private to MigrateTagFamiliesCommand. It is extracted
// here so PropagateUniversalTagCommand (universal-tag propagation) and the legacy
// MigrateTagFamiliesCommand both author identical variants.
//
// The caller owns the transaction — CreateStandardVariants only mutates the
// FamilyManager and must run inside an open Transaction on the family document.
// ============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using StingTools.Core;

namespace StingTools.Tags
{
    /// <summary>
    /// Data-driven creation of the standard tag type variants on a family's
    /// <see cref="FamilyManager"/>. Family-agnostic — identical for every STING
    /// tag family regardless of category (the universal-tag design relies on
    /// this being reproducible after a recategorise).
    /// </summary>
    public static class TagTypeVariantWriter
    {
        /// <summary>
        /// Create (or update) every standard type variant on <paramref name="fm"/>.
        /// Sets depth-tier STATE bools, the single active style BOOL, the leader
        /// arrowhead (when present in the project) and the depth-tier cache.
        /// Must be called inside an open transaction on the family document.
        /// Returns the number of NEW types created.
        /// </summary>
        public static int CreateStandardVariants(FamilyManager fm,
            List<TypeVariantSpec> variants, Dictionary<string, ElementId> arrowheads)
        {
            int created = 0;
            var paramByName = fm.GetParameters()
                .ToDictionary(p => p.Definition.Name, p => p, StringComparer.OrdinalIgnoreCase);

            var existingTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (FamilyType ft in fm.Types)
                if (ft != null && !string.IsNullOrEmpty(ft.Name)) existingTypes.Add(ft.Name);

            foreach (var spec in variants)
            {
                string typeName = spec.CanonicalTypeName;
                FamilyType target;

                if (existingTypes.Contains(typeName))
                {
                    target = null;
                    foreach (FamilyType ft in fm.Types)
                        if (string.Equals(ft.Name, typeName, StringComparison.OrdinalIgnoreCase))
                        { target = ft; break; }
                    if (target == null) continue;
                }
                else
                {
                    try { target = fm.NewType(typeName); created++; existingTypes.Add(typeName); }
                    catch (Exception ex) { StingLog.Warn($"NewType '{typeName}': {ex.Message}"); continue; }
                }

                fm.CurrentType = target;

                // 1. Depth tiers: PARA_STATE_1..depth = Yes, rest = No.
                // Tag-formula BOOLs are TEXT in MR_PARAMETERS v5.3+ so Revit label
                // Calculated Values can reference them inside if(...); the Integer
                // branch keeps legacy YESNO families migrating cleanly.
                for (int t = 1; t <= 10; t++)
                {
                    string pname = $"TAG_PARA_STATE_{t}_BOOL";
                    if (paramByName.TryGetValue(pname, out var pfp))
                    {
                        try { SetFamilyBool(fm, pfp, t <= spec.DepthTier); }
                        catch (Exception ex) { StingLog.Warn($"Set {pname} on {typeName}: {ex.Message}"); }
                    }
                }

                // 2. Style BOOLs: only the matching combo = Yes
                string activeStyle = ParamRegistry.TagStyleParamName(spec.Size, spec.Style, spec.Colour);
                foreach (string pname in ParamRegistry.AllTagStyleParams)
                {
                    if (!paramByName.TryGetValue(pname, out var pfp)) continue;
                    try { SetFamilyBool(fm, pfp, string.Equals(pname, activeStyle, StringComparison.OrdinalIgnoreCase)); }
                    catch (Exception ex) { StingLog.Warn($"Set {pname} on {typeName}: {ex.Message}"); }
                }

                // 3. Arrowhead (type param LEADER_ARROWHEAD via BuiltInParameter)
                try
                {
                    if (!string.IsNullOrEmpty(spec.Arrowhead) &&
                        !string.Equals(spec.Arrowhead, "None", StringComparison.OrdinalIgnoreCase))
                    {
                        if (arrowheads != null &&
                            arrowheads.TryGetValue(spec.Arrowhead, out var arrowId) && arrowId != ElementId.InvalidElementId)
                        {
                            var arrowFp = fm.get_Parameter(BuiltInParameter.LEADER_ARROWHEAD);
                            if (arrowFp != null && !arrowFp.IsReadOnly)
                            {
                                try { fm.Set(arrowFp, arrowId); }
                                catch (Exception ex) { StingLog.Warn($"Set arrowhead {spec.Arrowhead} on {typeName}: {ex.Message}"); }
                            }
                        }
                        else
                        {
                            StingLog.Warn($"TagTypeVariantWriter: arrowhead '{spec.Arrowhead}' not present in project — skipped for {typeName}");
                        }
                    }
                }
                catch (Exception ex) { StingLog.Warn($"Arrowhead assign {typeName}: {ex.Message}"); }

                // 4. Cache active depth tier on the type for fast reads
                if (paramByName.TryGetValue(ParamRegistry.TAG_DEPTH_TIER, out var depthFp))
                {
                    try { fm.Set(depthFp, spec.DepthTier); }
                    catch (Exception ex2) { StingLog.Warn($"Set TAG_DEPTH_TIER_INT on {typeName}: {ex2.Message}"); }
                }
            }

            return created;
        }

        /// <summary>
        /// Set a tag-formula BOOL on a family type, regardless of whether the parameter
        /// is stored as TEXT ("Yes"/"No") or INTEGER (1/0). TEXT is the v5.3+ default —
        /// YESNO is not allowed as the condition of a Revit label Calculated Value.
        /// </summary>
        public static void SetFamilyBool(FamilyManager fm, FamilyParameter fp, bool value)
        {
            if (fp == null) return;
            switch (fp.StorageType)
            {
                case StorageType.String:
                    fm.Set(fp, value ? "Yes" : "No");
                    break;
                case StorageType.Integer:
                    fm.Set(fp, value ? 1 : 0);
                    break;
                default:
                    StingLog.Warn($"SetFamilyBool: unsupported storage {fp.StorageType} on '{fp.Definition.Name}'");
                    break;
            }
        }

        /// <summary>
        /// Build a lookup of arrowhead display name → ElementType Id (OST_ArrowHeads).
        /// Names are matched case-insensitively against TagStyleCatalogue.Arrowheads.
        /// </summary>
        public static Dictionary<string, ElementId> BuildArrowheadLookup(Document doc)
        {
            var lookup = new Dictionary<string, ElementId>(StringComparer.OrdinalIgnoreCase);
            try
            {
                // Arrowhead ElementType has no BuiltInCategory in Revit 2025 —
                // Category is null and FamilyName is "Arrowhead".
                var types = new FilteredElementCollector(doc)
                    .OfClass(typeof(ElementType))
                    .Cast<ElementType>()
                    .Where(et => string.Equals(et.FamilyName, "Arrowhead", StringComparison.Ordinal))
                    .ToList();

                var wanted = new HashSet<string>(TagStyleCatalogue.Arrowheads, StringComparer.OrdinalIgnoreCase);
                foreach (var et in types)
                {
                    string n = et.Name ?? "";
                    if (wanted.Contains(n)) lookup[n] = et.Id;
                }

                // Best-effort fuzzy matches so the catalogue is not held hostage to exact names
                foreach (string want in TagStyleCatalogue.Arrowheads)
                {
                    if (lookup.ContainsKey(want)) continue;
                    var match = types.FirstOrDefault(et =>
                        et.Name != null &&
                        et.Name.IndexOf(want, StringComparison.OrdinalIgnoreCase) >= 0);
                    if (match != null) lookup[want] = match.Id;
                }
            }
            catch (Exception ex) { StingLog.Warn($"BuildArrowheadLookup: {ex.Message}"); }
            return lookup;
        }
    }
}
