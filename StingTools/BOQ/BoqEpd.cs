// ══════════════════════════════════════════════════════════════════════════
//  BoqEpd.cs (QS gap G5) — per-material EPD override + carbon data-quality.
//
//  Embodied carbon was indicative (ICE DB + material overrides). This adds an
//  optional per-material EPD reference map at <project>/_BIM_COORD/boq_epd_map.json:
//    { "entries": [
//        { "material": "Concrete C30/37", "a1a3": 264.0, "unit": "m3",
//          "source": "EPD-XYZ-2023 (EcoPlatform)", "quality": "Verified-EPD" } ] }
//  When a material matches, the verified A1–A3 figure overrides the ICE default
//  (highest-priority tier in CarbonFactorResolver). ICE stays the fallback. A
//  carbon data-quality flag (Verified-EPD / Database / Missing) mirrors
//  rate-confidence so a QS sees which carbon figures are defensible.
//
//  Cached per document; BuildBOQDocument calls Invalidate at the top so an edit
//  to the map is picked up on the next Refresh.
// ══════════════════════════════════════════════════════════════════════════
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using Autodesk.Revit.DB;
using Newtonsoft.Json;
using StingTools.Core;

namespace StingTools.BOQ
{
    public class BoqEpdEntry
    {
        public string Material { get; set; } = "";
        public double A1A3 { get; set; }                 // embodied carbon factor (per Unit)
        public string Unit { get; set; } = "m3";         // "m3" (kgCO₂e/m³) | "kg" (kgCO₂e/kg)
        public string Source { get; set; } = "";         // EPD reference / programme
        public string Quality { get; set; } = "Verified-EPD";
    }

    public class BoqEpdMap
    {
        public List<BoqEpdEntry> Entries { get; set; } = new List<BoqEpdEntry>();
    }

    internal static class BoqEpdStore
    {
        // Carbon data-quality bands (mirror rate-confidence tiers).
        public const string QualityVerified = "Verified-EPD";
        public const string QualityDatabase = "Database";
        public const string QualityMissing = "Missing";

        private static readonly ConcurrentDictionary<string, Dictionary<string, BoqEpdEntry>> _cache
            = new ConcurrentDictionary<string, Dictionary<string, BoqEpdEntry>>(StringComparer.OrdinalIgnoreCase);

        private static string PathFor(Document doc)
        {
            try
            {
                string parent = System.IO.Path.GetDirectoryName(doc?.PathName ?? "");
                if (string.IsNullOrEmpty(parent)) return null;
                return System.IO.Path.Combine(parent, "_BIM_COORD", "boq_epd_map.json");
            }
            catch { return null; }
        }

        /// <summary>Drop the cached map for a document so the next lookup reloads.</summary>
        public static void Invalidate(Document doc)
        {
            _cache.TryRemove(doc?.PathName ?? "default", out _);
        }

        private static Dictionary<string, BoqEpdEntry> Map(Document doc)
        {
            return _cache.GetOrAdd(doc?.PathName ?? "default", _ =>
            {
                var map = new Dictionary<string, BoqEpdEntry>(StringComparer.OrdinalIgnoreCase);
                try
                {
                    string path = PathFor(doc);
                    if (path == null || !File.Exists(path)) return map;
                    var loaded = JsonConvert.DeserializeObject<BoqEpdMap>(File.ReadAllText(path));
                    if (loaded?.Entries != null)
                        foreach (var e in loaded.Entries)
                            if (!string.IsNullOrWhiteSpace(e.Material) && e.A1A3 > 0)
                                map[e.Material.Trim()] = e;
                    if (map.Count > 0) StingLog.Info($"BoqEpdStore: loaded {map.Count} EPD override(s).");
                }
                catch (Exception ex) { StingLog.Warn($"BoqEpdStore.Map: {ex.Message}"); }
                return map;
            });
        }

        public static bool TryGet(Document doc, string materialName, out BoqEpdEntry entry)
        {
            entry = null;
            if (string.IsNullOrWhiteSpace(materialName)) return false;
            return Map(doc).TryGetValue(materialName.Trim(), out entry);
        }

        /// <summary>Map a resolver Source string to a carbon data-quality band.</summary>
        public static string QualityForSource(string source)
        {
            if (string.IsNullOrEmpty(source) ||
                source.Equals("none", StringComparison.OrdinalIgnoreCase)) return QualityMissing;
            if (source.StartsWith("epd", StringComparison.OrdinalIgnoreCase)) return QualityVerified;
            return QualityDatabase;
        }

        public static void Save(Document doc, BoqEpdMap map)
        {
            try
            {
                string path = PathFor(doc);
                if (path == null || map == null) return;
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path));
                File.WriteAllText(path, JsonConvert.SerializeObject(map, Formatting.Indented));
                Invalidate(doc);
            }
            catch (Exception ex) { StingLog.Warn($"BoqEpdStore.Save: {ex.Message}"); }
        }
    }
}
