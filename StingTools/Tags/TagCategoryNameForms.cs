// TagCategoryNameForms - host category name -> candidate TAG category names.
//
// Revit-free on purpose. This is the half of TagCategoryResolver that decides
// what to look for, separated from the half that looks it up in a Document, so
// it can be tested without Revit. The lookup half stays in TagCategoryResolver.
//
// It exists because the naming rule is not trivial and got it wrong in silence:
// Revit names most tag categories as the SINGULAR host plus " Tags", and a
// single-"s" chop turns "Duct Accessories" into "Accessorie Tags". On
// 2026-09-21 four families with entirely correct declarations - Duct
// Accessories, Pipe Accessories, Assemblies, Structural Trusses - were reported
// UNRESOLVED and read as bad data. The data was fine.

using System;
using System.Collections.Generic;
using System.Linq;

namespace StingTools.Tags
{
    /// <summary>Candidate tag-category names for a declared host category.</summary>
    public static class TagCategoryNameForms
    {

        /// <summary>
        /// The tag-category names to try for a declared host category, best first.
        /// Revit names most tag categories as the SINGULAR host plus " Tags"
        /// (Doors -> Door Tags), so this has to singularise - and English plurals
        /// do not all lose one letter.
        ///
        /// <para>Measured 2026-09-21: chopping a single "s" produced "Accessorie Tags"
        /// and "Trusse Tags", and four families with perfectly correct declarations -
        /// Duct Accessories, Pipe Accessories, Assemblies, Structural Trusses - came
        /// back UNRESOLVED against the real categories Duct Accessory Tags, Pipe
        /// Accessory Tags, Assembly Tags and Structural Truss Tags. The audit blamed
        /// the data; the defect was here.</para>
        ///
        /// <para>Every candidate is TRIED against the document, so an over-eager rule
        /// costs a failed lookup, never a wrong category. That is why all forms are
        /// offered rather than one clever guess.</para>
        /// </summary>
        public static List<string> Candidates(string host)
        {
            var candidates = new List<string>();
            if (string.IsNullOrWhiteSpace(host)) return candidates;
            host = host.Trim();

            void Add(string s)
            {
                if (!string.IsNullOrWhiteSpace(s) &&
                    !candidates.Any(x => string.Equals(x, s, StringComparison.OrdinalIgnoreCase)))
                    candidates.Add(s);
            }

            if (host.EndsWith("Tags", StringComparison.OrdinalIgnoreCase))
                Add(host);                              // already a tag category

            Add(host + " Tags");                        // Furniture -> Furniture Tags

            foreach (string singular in Singularise(host))
                Add(singular + " Tags");

            return candidates;
        }

        /// <summary>
        /// Candidate singular forms of an English plural, most specific rule first.
        /// Returns every plausible form rather than picking one, because the caller
        /// tests each against the document and a miss is free.
        /// </summary>
        private static IEnumerable<string> Singularise(string word)
        {
            if (string.IsNullOrEmpty(word)) yield break;

            // Accessories -> Accessory, Assemblies -> Assembly
            if (word.EndsWith("ies", StringComparison.OrdinalIgnoreCase) && word.Length > 3)
                yield return word.Substring(0, word.Length - 3) + "y";

            // Trusses -> Truss, Boxes -> Box, Arches -> Arch
            if (word.EndsWith("sses", StringComparison.OrdinalIgnoreCase) ||
                word.EndsWith("xes", StringComparison.OrdinalIgnoreCase) ||
                word.EndsWith("ches", StringComparison.OrdinalIgnoreCase) ||
                word.EndsWith("shes", StringComparison.OrdinalIgnoreCase) ||
                word.EndsWith("zes", StringComparison.OrdinalIgnoreCase))
                yield return word.Substring(0, word.Length - 2);

            // Doors -> Door. Last, and never for "ss" (Mass, Glass).
            if (word.EndsWith("s", StringComparison.OrdinalIgnoreCase) &&
                !word.EndsWith("ss", StringComparison.OrdinalIgnoreCase))
                yield return word.Substring(0, word.Length - 1);
        }
    }
}
