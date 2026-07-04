// ══════════════════════════════════════════════════════════════════════════
//  SlabSystemLoader.cs — MAT-1 runtime loader for SlabSystemRegistry.
//
//  Loads STING_SLAB_SYSTEMS.json (corporate) layered with the project override
//  at <project>/_BIM_COORD/STING_SLAB_SYSTEMS.json (additive by id), and resolves
//  a slab Element's solid fraction. Kept separate from SlabSystemRegistry so the
//  pure resolution logic stays Newtonsoft-free and unit-testable.
// ══════════════════════════════════════════════════════════════════════════
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;
using StingTools.Core;

namespace StingTools.Core.Materials
{
    internal static class SlabSystemLoader
    {
        private static readonly ConcurrentDictionary<string, SlabSystemRegistry> _cache
            = new ConcurrentDictionary<string, SlabSystemRegistry>(StringComparer.OrdinalIgnoreCase);

        public static void Invalidate() => _cache.Clear();

        public static SlabSystemRegistry Get(Document doc)
        {
            string key = doc?.PathName ?? "default";
            return _cache.GetOrAdd(key, _ => Build(doc));
        }

        private static SlabSystemRegistry Build(Document doc)
        {
            var byId = new Dictionary<string, SlabSystem>(StringComparer.OrdinalIgnoreCase);
            string paramName = "BLE_SLAB_SYSTEM_TXT";

            // Corporate baseline from the plugin data folder.
            try
            {
                string corp = StingToolsApp.FindDataFile("STING_SLAB_SYSTEMS.json");
                if (!string.IsNullOrEmpty(corp) && File.Exists(corp))
                    paramName = MergeFrom(File.ReadAllText(corp), byId, paramName);
            }
            catch (Exception ex) { StingLog.Warn($"SlabSystemLoader corporate: {ex.Message}"); }

            // Project override (additive by id).
            try
            {
                string proj = ProjectOverridePath(doc);
                if (!string.IsNullOrEmpty(proj) && File.Exists(proj))
                    paramName = MergeFrom(File.ReadAllText(proj), byId, paramName);
            }
            catch (Exception ex) { StingLog.Warn($"SlabSystemLoader override: {ex.Message}"); }

            return new SlabSystemRegistry(byId.Values, paramName);
        }

        private static string MergeFrom(string json, Dictionary<string, SlabSystem> byId, string paramName)
        {
            var root = JObject.Parse(json);
            string pn = root.Value<string>("paramName");
            if (!string.IsNullOrWhiteSpace(pn)) paramName = pn;
            if (root["systems"] is JArray arr)
            {
                foreach (var s in arr.OfType<JObject>())
                {
                    string id = s.Value<string>("id");
                    if (string.IsNullOrWhiteSpace(id)) continue;
                    SlabSystemDims dims = null;
                    if (s["dims"] is JObject d)
                        dims = new SlabSystemDims
                        {
                            ToppingMm = d.Value<double?>("toppingMm") ?? 0,
                            RibWidthMm = d.Value<double?>("ribWidthMm") ?? 0,
                            RibSpacingMm = d.Value<double?>("ribSpacingMm") ?? 0,
                            RibDepthMm = d.Value<double?>("ribDepthMm") ?? 0,
                            PotWidthMm = d.Value<double?>("potWidthMm") ?? 0,
                            PotLengthMm = d.Value<double?>("potLengthMm") ?? 0
                        };
                    byId[id.Trim()] = new SlabSystem
                    {
                        Id = id.Trim(),
                        Label = s.Value<string>("label") ?? id,
                        SolidFraction = s.Value<double?>("solidFraction") ?? 1.0,
                        Indicative = s.Value<bool?>("indicative") ?? true,
                        TwoWay = s.Value<bool?>("twoWay") ?? false,
                        RibsArePrecast = s.Value<bool?>("ribsArePrecast") ?? false,
                        Dims = dims,
                        Keywords = (s["keywords"] as JArray)?.Select(k => (string)k)
                                    .Where(k => !string.IsNullOrWhiteSpace(k)).ToList()
                                   ?? new List<string>()
                    };
                }
            }
            return paramName;
        }

        private static string ProjectOverridePath(Document doc)
        {
            try
            {
                if (doc == null || string.IsNullOrEmpty(doc.PathName)) return null;
                string dir = Path.GetDirectoryName(doc.PathName);
                if (string.IsNullOrEmpty(dir)) return null;
                return Path.Combine(dir, "_BIM_COORD", "STING_SLAB_SYSTEMS.json");
            }
            catch (Exception ex) { StingLog.Warn($"SlabSystemLoader path: {ex.Message}"); return null; }
        }

        /// <summary>Resolve the slab system for a modelled element: reads its type
        /// name + the BLE_SLAB_SYSTEM_TXT parameter and consults the registry.
        /// Returns Solid (1.0) for non-slabs or unmatched slabs.</summary>
        public static SlabSystemMatch ForElement(Document doc, Element el)
        {
            if (doc == null || el == null) return SlabSystemMatch.Solid;
            try
            {
                var reg = Get(doc);
                string paramVal = ParameterHelpers.GetString(el, reg.ParamName);
                string typeName = ResolveTypeName(doc, el);
                return reg.Resolve(typeName, paramVal);
            }
            catch (Exception ex)
            {
                StingLog.WarnRateLimited("SlabSystem.ForEl", $"SlabSystemLoader.ForElement: {ex.Message}");
                return SlabSystemMatch.Solid;
            }
        }

        /// <summary>The solid fraction to apply to a slab's gross concrete volume
        /// (1.0 for solid/unknown). Convenience wrapper over ForElement.</summary>
        public static double SolidFraction(Document doc, Element el) => ForElement(doc, el).SolidFraction;

        /// <summary>MAT-4 net-concrete resolution outcome.</summary>
        public struct SlabNetResult
        {
            public double NetConcreteM3;
            public string Method;        // "geometry" | "calculator" | "flat" | "solid"
            public int Confidence;       // 95 solid / 90 geometry / 70 calculator / 40 flat
            public bool Indicative;
            public SlabCalcResult Calc;  // valid when Method == "calculator"
            public SlabSystemMatch Match;
            public bool IsVoid => Match.IsVoidSystem;
        }

        /// <summary>
        /// MAT-4 — resolve a slab's NET in-situ concrete by the accuracy priority:
        /// (1) real modelled geometry (summed Solid.Volume materially below the
        /// gross bounding box → voids are modelled); (2) the parameter-driven
        /// calculator from the system dims (the primary path for flat-solid
        /// models); (3) the flat solid-fraction (LAST RESORT, low confidence).
        /// A solid slab returns the gross volume unchanged.
        /// </summary>
        public static SlabNetResult ResolveNetConcrete(Document doc, Element el, double grossVolM3, double grossAreaM2)
        {
            var res = new SlabNetResult { NetConcreteM3 = grossVolM3, Method = "solid", Confidence = 95, Match = SlabSystemMatch.Solid };
            if (doc == null || el == null || grossVolM3 <= 0) return res;
            try
            {
                var match = ForElement(doc, el);
                res.Match = match;
                if (!match.IsVoidSystem) return res;   // solid → unchanged

                // ── (1) Real geometry: summed Solid.Volume vs the gross bbox ──
                double solidM3 = SummedSolidVolumeM3(el);
                double bboxM3 = BoundingBoxVolumeM3(el);
                if (solidM3 > 0 && bboxM3 > 0 && solidM3 < 0.9 * bboxM3 && solidM3 < grossVolM3 * 0.98)
                {
                    // Voids are actually modelled — trust the true solid volume.
                    res.NetConcreteM3 = solidM3;
                    res.Method = "geometry";
                    res.Confidence = 90;
                    res.Indicative = false;
                    return res;
                }

                // ── (2) Parameter-driven calculator ──
                var input = GatherCalcInput(el, match.System);
                var calc = SlabConcreteCalculator.Compute(input);
                if (calc.Valid && grossAreaM2 > 0)
                {
                    res.NetConcreteM3 = calc.InsituConcreteM3PerM2 * grossAreaM2;
                    res.Method = "calculator";
                    res.Confidence = 70;
                    res.Indicative = match.Indicative;
                    res.Calc = calc;
                    return res;
                }

                // ── (3) Flat solid-fraction — last resort ──
                res.NetConcreteM3 = grossVolM3 * match.SolidFraction;
                res.Method = "flat";
                res.Confidence = 40;
                res.Indicative = true;
                return res;
            }
            catch (Exception ex)
            {
                StingLog.WarnRateLimited("SlabNet", $"ResolveNetConcrete {el?.Id}: {ex.Message}");
                return res;
            }
        }

        // Element BLE_SLAB_* params override the system's indicative defaults.
        private static SlabCalcInput GatherCalcInput(Element el, SlabSystem sys)
        {
            var d = sys?.Dims ?? new SlabSystemDims();
            // Datatype-robust mm reader: BLE_SLAB_*_MM ship as TEXT (matching the
            // existing BLE_*_MM convention) but a project may bind them NUMBER or
            // INTEGER. Values are plain millimetres (no length-unit conversion).
            double P(string name, double def)
            {
                try
                {
                    string sv = ParameterHelpers.GetString(el, name);
                    if (!string.IsNullOrWhiteSpace(sv) &&
                        double.TryParse(sv.Trim(), System.Globalization.NumberStyles.Any,
                            System.Globalization.CultureInfo.InvariantCulture, out double tv) && tv > 0)
                        return tv;
                    var p = el.LookupParameter(name);
                    if (p != null && p.HasValue)
                    {
                        if (p.StorageType == StorageType.Double && p.AsDouble() > 0) return p.AsDouble();
                        if (p.StorageType == StorageType.Integer && p.AsInteger() > 0) return p.AsInteger();
                    }
                }
                catch (Exception ex) { StingLog.WarnRateLimited("SlabNet.Param", $"GatherCalcInput {name}: {ex.Message}"); }
                return def;
            }
            // Block size "LxW" → pot length × width (e.g. "600x440").
            double potW = d.PotWidthMm, potL = d.PotLengthMm;
            string blk = ParameterHelpers.GetString(el, "BLE_SLAB_BLOCK_SIZE_TXT");
            if (!string.IsNullOrWhiteSpace(blk))
            {
                var parts = blk.Split('x', 'X', '*');
                if (parts.Length >= 2
                    && double.TryParse(parts[0].Trim(), out double a)
                    && double.TryParse(parts[1].Trim(), out double b))
                { potL = a; potW = b; }
            }
            return new SlabCalcInput
            {
                ToppingMm = P("BLE_SLAB_TOPPING_MM", d.ToppingMm),
                RibWidthMm = P("BLE_SLAB_RIB_WIDTH_MM", d.RibWidthMm),
                RibSpacingMm = P("BLE_SLAB_RIB_SPACING_MM", d.RibSpacingMm),
                RibDepthMm = P("BLE_SLAB_RIB_DEPTH_MM", d.RibDepthMm),
                PotWidthMm = potW,
                PotLengthMm = potL,
                TwoWay = sys?.TwoWay ?? false,
                RibsArePrecast = sys?.RibsArePrecast ?? false
            };
        }

        private static double SummedSolidVolumeM3(Element el)
        {
            try
            {
                var opt = new Options { ComputeReferences = false, DetailLevel = ViewDetailLevel.Fine };
                var geo = el.get_Geometry(opt);
                return geo != null ? SumSolidVolumeFt3(geo) * 0.0283168 : 0;
            }
            catch (Exception ex) { StingLog.WarnRateLimited("SlabNet.Geo", $"SummedSolidVolumeM3: {ex.Message}"); return 0; }
        }

        private static double SumSolidVolumeFt3(GeometryElement geo)
        {
            double v = 0;
            foreach (GeometryObject g in geo)
            {
                if (g is Solid s && s.Volume > 0) v += s.Volume;
                else if (g is GeometryInstance gi)
                {
                    var inst = gi.GetInstanceGeometry();
                    if (inst != null) v += SumSolidVolumeFt3(inst);
                }
            }
            return v;
        }

        private static double BoundingBoxVolumeM3(Element el)
        {
            try
            {
                var bb = el.get_BoundingBox(null);
                if (bb == null) return 0;
                double ft3 = (bb.Max.X - bb.Min.X) * (bb.Max.Y - bb.Min.Y) * (bb.Max.Z - bb.Min.Z);
                return ft3 > 0 ? ft3 * 0.0283168 : 0;
            }
            catch (Exception ex) { StingLog.WarnRateLimited("SlabNet.Bbox", $"BoundingBoxVolumeM3: {ex.Message}"); return 0; }
        }

        private static string ResolveTypeName(Document doc, Element el)
        {
            try
            {
                var typeId = el.GetTypeId();
                string typeName = (typeId != null && typeId != ElementId.InvalidElementId)
                    ? doc.GetElement(typeId)?.Name : null;
                string elName = el.Name ?? "";
                return string.IsNullOrEmpty(typeName) ? elName : $"{typeName} {elName}";
            }
            catch { return el.Name ?? ""; }
        }
    }
}
