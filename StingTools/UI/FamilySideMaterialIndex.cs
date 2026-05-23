using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using StingTools.Core;

namespace StingTools.UI
{
    /// <summary>
    /// N6 — Family-side material indexing.
    ///
    /// Project Materials (the FilteredElementCollector view used by the
    /// Browse grid) only shows materials that have been loaded into the
    /// project. Materials defined inside a Family Type's Material
    /// parameter are invisible until an instance is placed.
    ///
    /// This walker iterates every loaded <see cref="Family"/> + its
    /// <see cref="FamilySymbol"/>s in the project, reads each symbol's
    /// Material-storage parameters (BuiltInParameter.MATERIAL_ID_PARAM,
    /// MATERIAL_PARAM, plus shared "Material" parameters), and emits
    /// rows that the MAT > Browse grid can append under a "Family
    /// Materials" tier.
    ///
    /// Used to catch the "Modelical blind spot": a vendor family defines
    /// "Mfr-Concrete-C40" inside its types and nobody knows because no
    /// instance has been placed yet.
    /// </summary>
    public class FamilySideMaterialRow
    {
        public string FamilyName { get; set; }
        public string TypeName { get; set; }
        public string MaterialName { get; set; }  // resolved name (Material element name)
        public ElementId MaterialId { get; set; } // Material id IF loaded; InvalidElementId otherwise
        public string Origin { get; set; }        // STING / BLE / MEP / Other
        public bool   IsLoadedInProject { get; set; }
    }

    public static class FamilySideMaterialIndex
    {
        public static List<FamilySideMaterialRow> Index(Document doc)
        {
            var rows = new List<FamilySideMaterialRow>();
            if (doc == null) return rows;
            try
            {
                var families = new FilteredElementCollector(doc).OfClass(typeof(Family))
                    .Cast<Family>().ToList();
                foreach (var fam in families)
                {
                    try
                    {
                        var symIds = fam.GetFamilySymbolIds();
                        if (symIds == null) continue;
                        foreach (var sid in symIds)
                        {
                            var sym = doc.GetElement(sid) as FamilySymbol;
                            if (sym == null) continue;

                            // Walk every Material-storage parameter on this Type.
                            foreach (Parameter p in sym.GetOrderedParameters())
                            {
                                try
                                {
                                    if (p?.StorageType != StorageType.ElementId) continue;
                                    var def = p.Definition;
                                    if (def == null) continue;
                                    // Only ElementId parameters whose data type is Material.
                                    var dt = def.GetDataType();
                                    bool isMatParam = false;
                                    try
                                    {
                                        isMatParam = dt != null && (
                                            dt == Autodesk.Revit.DB.SpecTypeId.Reference.Material ||
                                            string.Equals(def.Name, "Material", StringComparison.OrdinalIgnoreCase));
                                    }
                                    catch
                                    {
                                        // Older API surfaces — fall back to name-based detection.
                                        isMatParam = string.Equals(def.Name, "Material", StringComparison.OrdinalIgnoreCase);
                                    }
                                    if (!isMatParam) continue;

                                    var mid = p.AsElementId();
                                    if (mid == null || mid.Value <= 0) continue;
                                    var mat = doc.GetElement(mid) as Material;
                                    string matName = mat?.Name ?? "(unresolved)";
                                    string origin =
                                        matName.StartsWith("STING", StringComparison.OrdinalIgnoreCase) ? "STING" :
                                        matName.StartsWith("BLE_",  StringComparison.OrdinalIgnoreCase) ? "BLE" :
                                        matName.StartsWith("MEP_",  StringComparison.OrdinalIgnoreCase) ? "MEP" : "Other";

                                    rows.Add(new FamilySideMaterialRow
                                    {
                                        FamilyName = fam.Name ?? "",
                                        TypeName = sym.Name ?? "",
                                        MaterialName = matName,
                                        MaterialId = mid,
                                        Origin = origin,
                                        IsLoadedInProject = (mat != null),
                                    });
                                }
                                catch (Exception ex) { StingLog.Warn($"FamilySideMat param: {ex.Message}"); }
                            }
                        }
                    }
                    catch (Exception ex) { StingLog.Warn($"FamilySideMat family '{fam?.Name}': {ex.Message}"); }
                }
            }
            catch (Exception ex) { StingLog.Error("FamilySideMaterialIndex.Index", ex); }
            return rows;
        }
    }
}
