// ══════════════════════════════════════════════════════════════════════════
//  FfeTreatment.cs — Phase C (KUT lifecycle). Pure, host-free resolution of how
//  an FF&E (Fohlio-mapped) category is carried in the bill. No Autodesk.Revit.*
//  so it can be unit-tested in StingTools.Boq.Tests.
//
//  KUT default: a Provisional / Prime-Cost sum that references the Fohlio
//  register (Owner-procured items the contractor does not measure-and-price).
//  Per-category overrides flip a category to a contractor-supplied measured
//  line, or drop it from this bill entirely. An item is exactly one of the
//  three — never double-counted.
// ══════════════════════════════════════════════════════════════════════════
using System;
using System.Collections.Generic;

namespace StingTools.BOQ
{
    public static class FfeTreatment
    {
        public const string PcSum    = "pcSum";                  // default
        public const string Measured = "measured";
        public const string Excluded = "ownerSupplied-excluded";

        /// <summary>Canonicalise a raw treatment string. Aliases tolerated:
        /// pc/provisional → pcSum; measur* → measured; owner*/exclud* → excluded.
        /// Anything else (incl. empty) → the safe default, pcSum.</summary>
        public static string Normalize(string raw)
        {
            string s = (raw ?? "").Trim().ToLowerInvariant();
            if (s.Contains("exclud") || s.StartsWith("owner")) return Excluded;
            if (s.StartsWith("measur")) return Measured;
            return PcSum;
        }

        /// <summary>Resolve the treatment for a category: per-category override →
        /// map default → pcSum. Case-insensitive category match.</summary>
        public static string Resolve(string category, string mapDefault, IDictionary<string, string> byCategory)
        {
            string raw = null;
            if (!string.IsNullOrEmpty(category) && byCategory != null)
                foreach (var kv in byCategory)
                    if (string.Equals(kv.Key, category, StringComparison.OrdinalIgnoreCase)) { raw = kv.Value; break; }
            if (string.IsNullOrWhiteSpace(raw)) raw = mapDefault;
            return Normalize(raw);
        }
    }
}
