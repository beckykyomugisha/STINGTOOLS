// ══════════════════════════════════════════════════════════════════════════
//  UgCarbonFactors.cs — WP3. The Uganda/EDGE default embodied-carbon basis.
//
//  Corporate baseline: StingTools/Data/STING_CARBON_FACTORS_UG.json
//  Project override:    <project>/_BIM_COORD/carbon_factors_ug.json (additive
//                       by key — byMaterial / byMaterialClass / byKeyword /
//                       defaultPerM3 entries present in the override win).
//
//  Returns kgCO2e per CUBIC METRE (A1-A3 fossil GWP). CarbonFactorResolver
//  consults this AFTER a verified EPD / a material's own STING_EMB_CARBON_NR
//  parameter, but as the primary DEFAULT ahead of the generic material library —
//  so an un-EPD'd Uganda project still gets a sourced, regionally-appropriate
//  factor instead of 0 or a UK-recycled-content approximation.
// ══════════════════════════════════════════════════════════════════════════
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using Newtonsoft.Json;
using StingTools.Core;

namespace StingTools.BOQ
{
    internal static class UgCarbonFactors
    {
        private sealed class KeywordFactor
        {
            public string contains;
            public double perM3;
            public string note;
        }

        private sealed class TableModel
        {
            public string schemaVersion;
            public double defaultPerM3 = 200;
            public Dictionary<string, double> byMaterialClass = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            public Dictionary<string, double> byMaterial = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            public List<KeywordFactor> byKeyword = new List<KeywordFactor>();
        }

        // Cached per (corporate-path + project-path) document key.
        private static readonly ConcurrentDictionary<string, TableModel> _cache
            = new ConcurrentDictionary<string, TableModel>(StringComparer.OrdinalIgnoreCase);

        private static string ProjectOverridePath(Document doc)
        {
            try
            {
                string parent = Path.GetDirectoryName(doc?.PathName ?? "");
                if (string.IsNullOrEmpty(parent)) return null;
                return Path.Combine(parent, "_BIM_COORD", "carbon_factors_ug.json");
            }
            catch { return null; }
        }

        /// <summary>Drop the cached table for a document so the next lookup reloads.</summary>
        public static void Invalidate(Document doc) => _cache.TryRemove(doc?.PathName ?? "default", out _);

        public static void InvalidateAll() => _cache.Clear();

        private static TableModel Table(Document doc)
        {
            return _cache.GetOrAdd(doc?.PathName ?? "default", _ =>
            {
                var t = new TableModel();
                // 1) Corporate baseline.
                try
                {
                    string corp = StingToolsApp.FindDataFile("STING_CARBON_FACTORS_UG.json");
                    if (corp != null && File.Exists(corp))
                        Merge(t, JsonConvert.DeserializeObject<TableModel>(File.ReadAllText(corp)));
                }
                catch (Exception ex) { StingLog.Warn($"UgCarbonFactors corporate: {ex.Message}"); }

                // 2) Project override (additive; present keys win).
                try
                {
                    string proj = ProjectOverridePath(doc);
                    if (proj != null && File.Exists(proj))
                    {
                        Merge(t, JsonConvert.DeserializeObject<TableModel>(File.ReadAllText(proj)));
                        StingLog.Info("UgCarbonFactors: project carbon-factor override applied.");
                    }
                }
                catch (Exception ex) { StingLog.Warn($"UgCarbonFactors override: {ex.Message}"); }
                return t;
            });
        }

        private static void Merge(TableModel into, TableModel from)
        {
            if (from == null) return;
            if (from.defaultPerM3 > 0) into.defaultPerM3 = from.defaultPerM3;
            if (from.byMaterialClass != null)
                foreach (var kv in from.byMaterialClass) into.byMaterialClass[kv.Key] = kv.Value;
            if (from.byMaterial != null)
                foreach (var kv in from.byMaterial) into.byMaterial[kv.Key] = kv.Value;
            if (from.byKeyword != null && from.byKeyword.Count > 0)
            {
                // Override keyword list replaces specific matches but keeps the
                // corporate tail — prepend override keywords so they win first.
                var merged = new List<KeywordFactor>(from.byKeyword);
                if (into.byKeyword != null) merged.AddRange(into.byKeyword);
                into.byKeyword = merged;
            }
        }

        /// <summary>
        /// SPECIFIC resolution: exact material name → material class → keyword.
        /// Returns Factor=0 when nothing specific matches (caller then tries the
        /// generic library / legacy tiers before falling back to the generic
        /// default via <see cref="GenericDefault"/>).
        /// </summary>
        public static (double Factor, string Source) ResolveSpecific(Document doc, string materialName, string materialClass)
        {
            var t = Table(doc);
            if (t == null) return (0, null);

            if (!string.IsNullOrWhiteSpace(materialName) &&
                t.byMaterial.TryGetValue(materialName.Trim(), out double byName) && byName > 0)
                return (byName, "uganda-edge:material");

            if (!string.IsNullOrWhiteSpace(materialClass) &&
                t.byMaterialClass.TryGetValue(materialClass.Trim(), out double byClass) && byClass > 0)
                return (byClass, $"uganda-edge:class:{materialClass.Trim()}");

            string lc = (materialName ?? "").ToLowerInvariant();
            if (!string.IsNullOrEmpty(lc) && t.byKeyword != null)
            {
                foreach (var k in t.byKeyword)
                {
                    if (k?.contains == null || k.perM3 <= 0) continue;
                    if (lc.Contains(k.contains.ToLowerInvariant()))
                        return (k.perM3, $"uganda-edge:kw:{k.contains.Trim()}");
                }
            }
            return (0, null);
        }

        /// <summary>The generic last-resort default (better than 0) — used only
        /// after every specific tier AND the material library / legacy dict fail.</summary>
        public static (double Factor, string Source) GenericDefault(Document doc)
        {
            var t = Table(doc);
            double d = t?.defaultPerM3 ?? 200;
            return d > 0 ? (d, "uganda-edge:default") : (0, null);
        }
    }
}
