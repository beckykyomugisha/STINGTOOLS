using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;

namespace StingTools.Core
{
    /// <summary>
    /// N+2 — Material-driven PROD code suffix lookup.
    ///
    /// Lets <see cref="TagConfig.GetFamilyAwareProdCode"/> append a
    /// material-aware tail to its base PROD when the element's primary
    /// material matches a rule. Closes the "Generic Beam" ambiguity:
    /// steel beam → BM-STL, concrete beam → BM-CON.
    ///
    /// Rules live in <c>Data/STING_MATERIAL_PROD_OVERRIDES.csv</c>.
    /// Columns: Category, MaterialPattern (regex), Suffix
    /// First matching rule wins. Category may be `*` for all categories.
    ///
    /// Element's primary material:
    ///   1) MATERIAL_ID_PARAM (instance Material parameter)
    ///   2) GetMaterialIds(false) first non-zero id (for compound elements)
    ///
    /// Lazy-loaded; <see cref="Reload"/> drops the cache for live edits.
    /// </summary>
    public static class MaterialProdOverrideRegistry
    {
        // Parsing and matching live in the Revit-free MaterialProdOverrideRules so
        // the SHIPPED rule table can be asserted outside Revit — an unanchored
        // pattern handing sheep's wool an EPS code is a data defect, and a data
        // defect needs a data gate. This class keeps only what needs an Element.
        private static List<MaterialProdRule> _rules;
        private static readonly object _lock = new object();

        public static void Reload()
        {
            lock (_lock) { _rules = null; }
        }

        public static string ResolveSuffix(Element el, string categoryName)
        {
            if (el == null) return null;
            EnsureLoaded();
            lock (_lock)
            {
                if (_rules == null || _rules.Count == 0) return null;
                string matName = ReadPrimaryMaterialName(el);
                if (string.IsNullOrEmpty(matName)) return null;
                try
                {
                    return MaterialProdOverrideRules.ResolveSuffix(_rules, matName, categoryName);
                }
                catch (Exception ex) { StingLog.Warn($"MaterialProdOverride match: {ex.Message}"); }
            }
            return null;
        }

        private static string ReadPrimaryMaterialName(Element el)
        {
            try
            {
                // 1. An EXPLICIT material on the instance or type wins outright —
                //    somebody said what this is, and no inference beats that.
                Parameter p = el.LookupParameter("Material") ?? el.get_Parameter(BuiltInParameter.MATERIAL_ID_PARAM);
                if (p != null && p.StorageType == StorageType.ElementId)
                {
                    var mid = p.AsElementId();
                    if (mid != null && mid.Value > 0)
                        return el.Document?.GetElement(mid)?.Name;
                }

                // 2. A COMPOUND element is named by its core, not its skin. Without
                //    this, a 230 mm rendered masonry wall reported gypsum, because
                //    GetMaterialIds returns the finish layer first.
                string layered = ReadCompoundPrimaryMaterial(el);
                if (!string.IsNullOrEmpty(layered)) return layered;

                // 3. Last resort: first material the element admits to.
                var mats = el.GetMaterialIds(false);
                if (mats != null)
                {
                    foreach (var mid in mats)
                    {
                        if (mid != null && mid.Value > 0)
                            return el.Document?.GetElement(mid)?.Name;
                    }
                }
            }
            catch (Exception ex) { StingLog.Warn($"ReadPrimaryMaterialName {el?.Id}: {ex.Message}"); }
            return null;
        }

        /// <summary>
        /// Flatten the element type's <c>CompoundStructure</c> into layers and ask
        /// <see cref="PrimaryMaterialSelector"/> which one names the element.
        ///
        /// <para>Returns null for anything that is not a layered host (a
        /// FamilyInstance, a type with no compound structure), so the caller falls
        /// through to its existing behaviour unchanged.</para>
        /// </summary>
        private static string ReadCompoundPrimaryMaterial(Element el)
        {
            try
            {
                var doc = el?.Document;
                if (doc == null) return null;
                if (!(doc.GetElement(el.GetTypeId()) is HostObjAttributes host)) return null;

                CompoundStructure cs = host.GetCompoundStructure();
                if (cs == null) return null;

                var layers = new List<MaterialLayer>();
                var csLayers = cs.GetLayers();
                for (int i = 0; i < csLayers.Count; i++)
                {
                    var cl = csLayers[i];
                    string name = null;
                    if (cl.MaterialId != null && cl.MaterialId.Value > 0)
                        name = doc.GetElement(cl.MaterialId)?.Name;
                    if (string.IsNullOrWhiteSpace(name)) continue;

                    layers.Add(new MaterialLayer
                    {
                        Index = i,
                        MaterialName = name,
                        // Width is in FEET. Millimetres only because the selector
                        // compares thicknesses to each other — any consistent unit
                        // would do, and mm is what every other STING layer reader uses.
                        ThicknessMm = cl.Width * 304.8,
                        IsStructure = cl.Function == MaterialFunctionAssignment.Structure,
                    });
                }

                return PrimaryMaterialSelector.Select(layers);
            }
            catch (Exception ex)
            {
                StingLog.Warn($"ReadCompoundPrimaryMaterial {el?.Id}: {ex.Message}");
                return null;
            }
        }

        private static void EnsureLoaded()
        {
            lock (_lock)
            {
                if (_rules != null) return;
                _rules = new List<MaterialProdRule>();
                try
                {
                    string path = StingToolsApp.FindDataFile("STING_MATERIAL_PROD_OVERRIDES.csv");
                    if (string.IsNullOrEmpty(path) || !File.Exists(path))
                    {
                        StingLog.Info("MaterialProdOverrideRegistry: no CSV found — material-aware PROD disabled.");
                        return;
                    }
                    var warnings = new List<string>();
                    _rules = MaterialProdOverrideRules.Parse(File.ReadAllLines(path), warnings);
                    foreach (string w in warnings) StingLog.Warn($"MaterialProdOverride {w}");
                    StingLog.Info($"MaterialProdOverrideRegistry: loaded {_rules.Count} rule(s) from {path}");
                }
                catch (Exception ex) { StingLog.Warn($"MaterialProdOverride load: {ex.Message}"); }
            }
        }
    }
}
