// ══════════════════════════════════════════════════════════════════════════
//  SlabSystemRegistry.cs — MAT-1 slab void systems.
//
//  Rib / waffle / trough / hollow-pot (clay-pot) slabs are ubiquitous in
//  Uganda/East Africa and are modelled SOLID in Revit, so concrete m³, embodied
//  carbon and rebar (ratio × volume) are all over-measured ~30-40%. This
//  registry resolves a slab's SOLID FRACTION (net concrete ÷ gross volume) from a
//  data-driven catalogue (STING_SLAB_SYSTEMS.json + <project>/_BIM_COORD override)
//  by an explicit BLE_SLAB_SYSTEM_TXT parameter or a type-name keyword.
//
//  The resolution logic is Document-free and unit-tested. Parse(json) (Newtonsoft)
//  is the runtime loader; tests build the registry directly from a systems list.
// ══════════════════════════════════════════════════════════════════════════
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace StingTools.Core.Materials
{
    /// <summary>Indicative default dimensions for a void slab system (mm), used by
    /// the MAT-4 parameter-driven calculator when the element itself doesn't carry
    /// BLE_SLAB_* dimension params.</summary>
    public sealed class SlabSystemDims
    {
        public double ToppingMm { get; set; }
        public double RibWidthMm { get; set; }
        public double RibSpacingMm { get; set; }
        public double RibDepthMm { get; set; }
        public double PotWidthMm { get; set; }   // infill block/pot width
        public double PotLengthMm { get; set; }  // infill block/pot length
    }

    public sealed class SlabSystem
    {
        public string Id { get; set; } = "";
        public string Label { get; set; } = "";
        /// <summary>Net concrete volume ÷ gross slab volume. 1.0 = solid. MAT-4 —
        /// this flat fraction is the LAST-RESORT fallback behind real geometry and
        /// the parameter calculator.</summary>
        public double SolidFraction { get; set; } = 1.0;
        public bool Indicative { get; set; } = true;
        /// <summary>Two-way (waffle) vs one-way (ribbed/trough/pot/maxspan).</summary>
        public bool TwoWay { get; set; } = false;
        /// <summary>MAT-4.2 — ribs are supplied PRECAST (maxspan/beam-block), so the
        /// in-situ concrete is the topping only and the ribs bill as a separate
        /// precast line. false = ribs cast in situ (ribbed/waffle/hollow-pot).</summary>
        public bool RibsArePrecast { get; set; } = false;
        public SlabSystemDims Dims { get; set; }
        public List<string> Keywords { get; set; } = new List<string>();
    }

    public readonly struct SlabSystemMatch
    {
        public readonly string Id;
        public readonly string Label;
        public readonly double SolidFraction;
        public readonly bool Indicative;
        public readonly string MatchedOn;   // "param" | "keyword" | "none"
        /// <summary>The full matched system (dims + flags) for the MAT-4 calculator;
        /// null for the Solid sentinel.</summary>
        public readonly SlabSystem System;

        public SlabSystemMatch(string id, string label, double solidFraction, bool indicative, string matchedOn, SlabSystem system = null)
        { Id = id; Label = label; SolidFraction = solidFraction; Indicative = indicative; MatchedOn = matchedOn; System = system; }

        /// <summary>True when this is a void system (solid fraction materially < 1).</summary>
        public bool IsVoidSystem => SolidFraction > 0 && SolidFraction < 0.999;
        /// <summary>MAT-4.2 — the ribs are supplied precast (maxspan/beam-block).</summary>
        public bool RibsArePrecast => System != null && System.RibsArePrecast;

        public static SlabSystemMatch Solid => new SlabSystemMatch("solid", "Solid slab", 1.0, false, "none");
    }

    public sealed class SlabSystemRegistry
    {
        public string ParamName { get; }
        private readonly List<SlabSystem> _systems;

        public SlabSystemRegistry(IEnumerable<SlabSystem> systems, string paramName = "BLE_SLAB_SYSTEM_TXT")
        {
            _systems = (systems ?? Enumerable.Empty<SlabSystem>())
                .Where(s => s != null && !string.IsNullOrWhiteSpace(s.Id)
                            && s.SolidFraction > 0 && s.SolidFraction <= 1.0)
                .ToList();
            ParamName = string.IsNullOrWhiteSpace(paramName) ? "BLE_SLAB_SYSTEM_TXT" : paramName;
        }

        /// <summary>
        /// Resolve the slab system for a type name + optional explicit parameter
        /// value. Priority: explicit param (id or keyword) → type-name keyword →
        /// Solid (1.0). A returned <see cref="SlabSystemMatch.IsVoidSystem"/> says
        /// whether to net the concrete volume.
        /// </summary>
        public SlabSystemMatch Resolve(string typeName, string paramValue = null)
        {
            // 1. Explicit BLE_SLAB_SYSTEM_TXT — match by id first, then keyword.
            if (!string.IsNullOrWhiteSpace(paramValue))
            {
                var byId = _systems.FirstOrDefault(s =>
                    string.Equals(s.Id, paramValue.Trim(), StringComparison.OrdinalIgnoreCase));
                if (byId != null) return Match(byId, "param");
                var byKw = MatchByKeyword(paramValue);
                if (byKw != null) return Match(byKw, "param");
            }

            // 2. Type-name keyword (word-boundary, case-insensitive).
            var hit = MatchByKeyword(typeName);
            if (hit != null) return Match(hit, "keyword");

            // 3. No match → solid.
            return SlabSystemMatch.Solid;
        }

        /// <summary>Convenience — the solid fraction only (1.0 when not a void system).</summary>
        public double SolidFraction(string typeName, string paramValue = null)
            => Resolve(typeName, paramValue).SolidFraction;

        private static SlabSystemMatch Match(SlabSystem s, string on)
            => new SlabSystemMatch(s.Id, s.Label, s.SolidFraction, s.Indicative, on, s);

        private SlabSystem MatchByKeyword(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            string t = text.ToLowerInvariant();
            foreach (var s in _systems)
            {
                if (s.Keywords == null) continue;
                foreach (var kw in s.Keywords)
                {
                    if (string.IsNullOrWhiteSpace(kw)) continue;
                    if (ContainsWord(t, kw.ToLowerInvariant())) return s;
                }
            }
            return null;
        }

        // Word-boundary contains so "rib" doesn't match "fibrous"/"ribbon" but
        // "300 Ribbed Slab" matches "ribbed". Multi-word keywords match as a phrase.
        private static bool ContainsWord(string haystack, string needle)
        {
            if (needle.Length == 0) return false;
            string pattern = @"(?<![a-z0-9])" + Regex.Escape(needle) + @"(?![a-z0-9])";
            return Regex.IsMatch(haystack, pattern);
        }
    }
}
