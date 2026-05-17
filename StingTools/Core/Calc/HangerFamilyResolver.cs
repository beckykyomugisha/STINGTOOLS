// StingTools v4 MVP — HangerFamilyResolver.
//
// Maps a HangerCandidate's anchor type to a concrete Revit FamilySymbol
// suitable for FamilyInstance.Create. Three lookup tiers:
//
//   Tier 1: project-specific family names authored by the shop
//           (STING_HANGER_CLEVIS_ROD / STING_HANGER_BEAM_CLAMP /
//            STING_HANGER_TRAPEZE / STING_HANGER_GENERIC).
//
//   Tier 2: fabrication-catalogue symbols with keywords in the family
//           name (anvil / b-line / unistrut / tolco / caddy / erico)
//           matching the anchor type — picks the first match.
//
//   Tier 3: any loaded GenericModel family whose name contains the
//           word "hanger" — a last-resort fallback so the placement
//           still produces something visible on the client's machine
//           even if no STING_HANGER_* family is loaded.
//
// Returns null only when the project has **zero** generic model
// families — in that case the caller (PlaceHangersCommand) stays in
// DetailCurve preview mode.
//
// The resolver is stateless but caches the (doc.PathName → symbol)
// map for the lifetime of the plugin so repeated placements don't
// re-walk every family each time.

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using StingTools.Core;

namespace StingTools.Core.Calc
{
    /// <summary>
    /// Resolution outcome for one anchor type.
    /// </summary>
    public class HangerFamilyBinding
    {
        public FamilySymbol Symbol { get; set; }
        public string       Tier   { get; set; } = "";
        public string       Notes  { get; set; } = "";
    }

    public static class HangerFamilyResolver
    {
        private static readonly object _lock = new object();
        private static readonly Dictionary<string, Dictionary<string, FamilySymbol>> _cache
            = new Dictionary<string, Dictionary<string, FamilySymbol>>();

        /// <summary>
        /// Look up a FamilySymbol for the given anchor type on the
        /// supplied document. Returns null when no suitable family is
        /// loaded. Resolution is cached per (doc.PathName, anchorType).
        /// </summary>
        public static HangerFamilyBinding Resolve(Document doc, string anchorType)
        {
            if (doc == null) return null;

            string key = string.IsNullOrEmpty(anchorType) ? "GENERIC" : anchorType.ToUpperInvariant();
            string docKey = doc.PathName ?? "_unsaved_";

            lock (_lock)
            {
                if (!_cache.TryGetValue(docKey, out var perType))
                {
                    perType = new Dictionary<string, FamilySymbol>(StringComparer.OrdinalIgnoreCase);
                    _cache[docKey] = perType;
                }
                if (perType.TryGetValue(key, out var cached) && cached != null && cached.IsValidObject)
                    return new HangerFamilyBinding { Symbol = cached, Tier = "CACHE" };
            }

            var binding = ResolveFresh(doc, key);
            if (binding?.Symbol != null)
            {
                lock (_lock) { _cache[docKey][key] = binding.Symbol; }
            }
            return binding;
        }

        private static HangerFamilyBinding ResolveFresh(Document doc, string anchorType)
        {
            // Candidate Tier-1 family names per anchor type.
            var preferred = PreferredFamilyNames(anchorType);

            // Collect candidate symbols once, then walk the preference
            // list. Scope: GenericModel + Structural Connections +
            // MechanicalEquipment. Hangers tend to live in Generic
            // Model families in the UK supply chain.
            var cats = new[]
            {
                BuiltInCategory.OST_GenericModel,
                BuiltInCategory.OST_StructConnections,
                BuiltInCategory.OST_MechanicalEquipment,
                BuiltInCategory.OST_PipeAccessory,
                BuiltInCategory.OST_DuctAccessory,
            };

            var symbols = new List<FamilySymbol>();
            foreach (var cat in cats)
            {
                try
                {
                    var col = new FilteredElementCollector(doc)
                        .OfCategory(cat)
                        .OfClass(typeof(FamilySymbol));
                    foreach (FamilySymbol fs in col) symbols.Add(fs);
                }
                catch (Exception ex)
                { StingLog.Warn($"HangerFamilyResolver: collector {cat} failed: {ex.Message}"); }
            }
            if (symbols.Count == 0)
                return new HangerFamilyBinding { Notes = "no Generic Model / Connection / Equipment families loaded" };

            // Tier 1 — project-specific STING_HANGER_* exact match.
            foreach (var name in preferred)
            {
                var hit = symbols.FirstOrDefault(s =>
                    string.Equals(s.FamilyName, name, StringComparison.OrdinalIgnoreCase));
                if (hit != null)
                    return new HangerFamilyBinding { Symbol = hit, Tier = "TIER-1", Notes = $"matched {name}" };
            }

            // Tier 2 — vendor catalogue substring match.
            var vendorKeywords = new[] { "ANVIL", "B-LINE", "UNISTRUT", "TOLCO", "CADDY", "ERICO", "LINDAPTER" };
            foreach (var kw in vendorKeywords)
            {
                var hit = symbols.FirstOrDefault(s =>
                    ContainsInsensitive(s.FamilyName, kw) &&
                    AnchorKeywordFor(anchorType).Any(k => ContainsInsensitive(s.FamilyName, k)));
                if (hit != null)
                    return new HangerFamilyBinding { Symbol = hit, Tier = "TIER-2", Notes = $"vendor match: {hit.FamilyName}" };
            }

            // Tier 3 — any family whose name contains "hanger" /
            // "strut" / "clamp" / "trapeze" depending on anchor type.
            foreach (var kw in AnchorKeywordFor(anchorType))
            {
                var hit = symbols.FirstOrDefault(s => ContainsInsensitive(s.FamilyName, kw));
                if (hit != null)
                    return new HangerFamilyBinding { Symbol = hit, Tier = "TIER-3", Notes = $"keyword match on '{kw}'" };
            }

            return new HangerFamilyBinding { Notes = "no hanger-suitable family found; preview mode only" };
        }

        private static string[] PreferredFamilyNames(string anchorType)
        {
            switch (anchorType)
            {
                case "CONCRETE_ANCHOR":
                    return new[] { "STING_HANGER_CLEVIS_ROD", "STING_HANGER_CONCRETE_ANCHOR" };
                case "BEAM_CLAMP":
                    return new[] { "STING_HANGER_BEAM_CLAMP", "STING_HANGER_LINDAPTER" };
                case "TRAPEZE":
                    return new[] { "STING_HANGER_TRAPEZE", "STING_HANGER_MULTI_RACK" };
                default:
                    return new[] { "STING_HANGER_GENERIC", "STING_HANGER_CLEVIS_ROD" };
            }
        }

        private static string[] AnchorKeywordFor(string anchorType)
        {
            switch (anchorType)
            {
                case "CONCRETE_ANCHOR": return new[] { "HANGER", "CLEVIS", "ROD", "ANCHOR" };
                case "BEAM_CLAMP":      return new[] { "CLAMP", "BEAM" };
                case "TRAPEZE":         return new[] { "TRAPEZE", "RACK", "STRUT" };
                default:                return new[] { "HANGER" };
            }
        }

        private static bool ContainsInsensitive(string haystack, string needle)
        {
            if (string.IsNullOrEmpty(haystack) || string.IsNullOrEmpty(needle)) return false;
            return haystack.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        public static void InvalidateCache()
        {
            lock (_lock) { _cache.Clear(); }
        }
    }
}
