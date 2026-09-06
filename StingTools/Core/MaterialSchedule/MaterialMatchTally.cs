// ══════════════════════════════════════════════════════════════════════════
//  MaterialMatchTally.cs — did the element's MATERIAL reach the resolver,
//  and did it match anything?
//
//  Material matching landed with a failure mode that looks identical to
//  success-minus-one-pattern: the row still reads "Generic - 225mm, 610 m²,
//  unpriced", exactly as it did before. Three very different causes produce
//  that same output —
//
//    * GetMaterialIds returned nothing, so no material ever reached the
//      resolver (plumbing);
//    * the material arrived and no rule's patterns fit it (data);
//    * the row was never a candidate for material matching at all (fine).
//
//  Reading a log to tell them apart is a round trip. The four scans already
//  in this export — tiling, screed, membrane, room finishes — each publish
//  their own denominator for the same reason: an absent result never explains
//  itself, and the commonest wrong conclusion is that the feature is broken
//  when the model is simply silent.
//
//  So this counts what it saw, and NAMES the materials it could not place —
//  the one output that turns "it didn't work" into a list of patterns to add.
// ══════════════════════════════════════════════════════════════════════════
using System;
using System.Collections.Generic;
using System.Linq;

namespace StingTools.Core.MaterialSchedule
{
    public sealed class MaterialMatchTally
    {
        /// <summary>Rows whose commodity could be decided by material — i.e. rows
        /// carrying no constituent kind of their own.</summary>
        public int RowsInspected;

        /// <summary>Of those, how many actually carried a material name.</summary>
        public int RowsWithMaterial;

        /// <summary>How many resolved through the MATERIAL path specifically.</summary>
        public int MatchedByMaterial;

        /// <summary>Distinct material names on rows that still did not convert.
        /// This is the actionable half: each one is a pattern worth adding.</summary>
        public readonly SortedSet<string> UnplacedMaterials =
            new SortedSet<string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Categories those unplaced rows sat in, so the reader knows
        /// which rule family to extend.</summary>
        public readonly SortedSet<string> UnplacedCategories =
            new SortedSet<string>(StringComparer.OrdinalIgnoreCase);

        public void Reset()
        {
            RowsInspected = RowsWithMaterial = MatchedByMaterial = 0;
            UnplacedMaterials.Clear();
            UnplacedCategories.Clear();
        }

        private const int MaxNamesShown = 8;

        /// <summary>
        /// The note line, or NULL when no row could have been decided by
        /// material — a scan that inspected nothing is not a finding.
        /// </summary>
        public string Summary()
        {
            if (RowsInspected == 0) return null;

            string s = $"Material scan: {RowsInspected} row(s) could be identified by material, "
                     + $"{RowsWithMaterial} carried a material name, {MatchedByMaterial} matched a "
                     + "commodity by it.";

            // The plumbing case, stated as such. This is the one that reads as a
            // broken feature and is not.
            if (RowsWithMaterial == 0)
                s += " NO row carried a material name at all. That is not a naming problem — the "
                   + "material is read off the element itself, so either these elements have no "
                   + "material assigned, or it is not reaching the schedule. Check one element's "
                   + "Material before adding any pattern.";

            if (UnplacedMaterials.Count > 0)
                s += $" {UnplacedMaterials.Count} material(s) were found but match no commodity: "
                   + string.Join(", ", UnplacedMaterials.Take(MaxNamesShown).Select(m => "'" + m + "'"))
                   + (UnplacedMaterials.Count > MaxNamesShown ? ", …" : "")
                   + (UnplacedCategories.Count > 0
                        ? " (in " + string.Join(", ", UnplacedCategories.Take(4)) + ")" : "")
                   + ". Each is a pattern worth adding to STING_SUPPLIER_UNITS.json — or, for one "
                   + "project only, a mapping via Price Commodities → Map to commodity.";

            if (RowsWithMaterial > 0 && UnplacedMaterials.Count == 0 && MatchedByMaterial == 0)
                s += " Every material found was already handled by a constituent kind or a type "
                   + "pattern, so material matching changed nothing here — which is the expected "
                   + "outcome on a well-named model.";

            return s;
        }
    }
}
