// ══════════════════════════════════════════════════════════════════════════
//  FfeTreatment.cs — Phase C (KUT lifecycle). Pure, host-free resolution of how
//  an FF&E (Fohlio-mapped) category is carried in the bill. No Autodesk.Revit.*
//  so it can be unit-tested in StingTools.Boq.Tests.
//
//  KUT default (Phase C.2): FF&E is carried as a TRANSPARENT, separately-totalled
//  Owner-procured category — priced at cost from the Fohlio register, shown as its
//  own subtotal, and EXCLUDED from the construction contractor's prelims / OH&P /
//  contingency (those are not earned on Owner-direct FF&E). This follows NRM1
//  Group 8 (Furniture, Fittings & Equipment) / ICMS FF&E classification — not a
//  "provisional sum" (FF&E here is defined and priced, not deferred).
//  Per-category overrides: "measured" (contractor-supplied, full markups),
//  "ownerSupplied-excluded" (out of this bill), or "pcSum" (the explicit
//  contractual Provisional / Prime-Cost mechanism for those who want it). An item
//  is exactly one of the four — never double-counted.
// ══════════════════════════════════════════════════════════════════════════
using System;
using System.Collections.Generic;

namespace StingTools.BOQ
{
    public static class FfeTreatment
    {
        public const string Ffe      = "ffe";                    // default — transparent Owner-procured FF&E category
        public const string PcSum    = "pcSum";                  // explicit opt-in — contractual provisional / prime-cost sum
        public const string Measured = "measured";
        public const string Excluded = "ownerSupplied-excluded";

        /// <summary>Canonicalise a raw treatment string. Aliases tolerated:
        /// exclud*/ownerSupplied* → excluded; measur* → measured; pc*/provision* →
        /// pcSum. Anything else (incl. empty, "ffe", "owner-procured") → the safe
        /// default, the transparent FF&amp;E category.</summary>
        public static string Normalize(string raw)
        {
            string s = (raw ?? "").Trim().ToLowerInvariant();
            if (s.Contains("exclud") || s.StartsWith("ownersupplied")) return Excluded;
            if (s.StartsWith("measur")) return Measured;
            if (s.StartsWith("pc") || s.Contains("provision")) return PcSum;
            return Ffe;
        }

        /// <summary>Resolve the treatment for a category: per-category override →
        /// map default → the transparent FF&amp;E category. Case-insensitive match.</summary>
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
