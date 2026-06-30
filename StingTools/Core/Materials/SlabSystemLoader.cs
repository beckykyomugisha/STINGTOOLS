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
                    byId[id.Trim()] = new SlabSystem
                    {
                        Id = id.Trim(),
                        Label = s.Value<string>("label") ?? id,
                        SolidFraction = s.Value<double?>("solidFraction") ?? 1.0,
                        Indicative = s.Value<bool?>("indicative") ?? true,
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
