// ══════════════════════════════════════════════════════════════════════════
//  SupplierUnitPatch.cs — say which commodity a model TYPE belongs to,
//  without being able to say anything else.
//
//  WHY A SEPARATE FILE RATHER THAN THE EXISTING OVERRIDE. The project
//  override at _data/coord/supplier_units.json replaces a rule WHOLESALE by
//  CommodityKey:
//
//      table.Rules.RemoveAll(x => x.CommodityKey == r.CommodityKey);
//      table.Rules.Add(r);
//
//  So a file written to add one type pattern —
//      { "CommodityKey": "roof-sheet", "MatchTypePatterns": ["Generic - 225"] }
//  — also silently sets SourceUnitsPerSupplierUnit to its default of 1.0 and
//  DefaultWastagePct to 0, turning "2.4 m2 per sheet, 10% waste" into "1 m2
//  per sheet, none". The quantities change across the whole schedule and
//  nothing anywhere says why. A UI promising not to do that is a promise; a
//  file shape that cannot express it is a guarantee.
//
//  A patch therefore carries a commodity key and a pattern and NOTHING ELSE.
//  There is nowhere to put a conversion factor, so a pricing dialog cannot
//  restate a measurement rule by accident — and a corporate change to
//  sheets-per-m2 still reaches every project, because the rule itself is
//  never copied.
//
//  Mapping is a PROJECT fact: "Generic - 225mm" is a name in one model.
//  Measurement is a STANDARD. This file holds only the first.
// ══════════════════════════════════════════════════════════════════════════
using System;
using System.Collections.Generic;
using System.Linq;

namespace StingTools.Core.MaterialSchedule
{
    /// <summary>One statement: "this type name belongs to that commodity".</summary>
    public sealed class SupplierUnitPatch
    {
        public string CommodityKey = "";

        /// <summary>A substring matched against the model type name.</summary>
        public string Pattern = "";

        /// <summary>Why, in the author's words. Required — see Validate.</summary>
        public string Why = "";

        public string AddedBy = "";
        public string AddedUtc = "";
    }

    public sealed class SupplierUnitPatchFile
    {
        public string SchemaVersion = "1.0";

        public string Note =
            "Type-to-commodity mappings for THIS project. Each entry says which commodity a model "
          + "type name belongs to, and can say nothing else — conversion factors and wastage are "
          + "measurement standards and live in STING_SUPPLIER_UNITS.json, so a change there still "
          + "reaches this project. Safe to edit by hand; safe to copy to another project only if "
          + "its type names are the same.";

        public List<SupplierUnitPatch> TypePatterns = new List<SupplierUnitPatch>();

        /// <summary>
        /// Problems in the file itself. A pattern is a SUBSTRING test, so a
        /// short one matches far more than its author intends.
        /// </summary>
        public List<string> Validate(SupplierUnitTable table)
        {
            var problems = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var p in TypePatterns ?? new List<SupplierUnitPatch>())
            {
                if (p == null) continue;
                string pat = (p.Pattern ?? "").Trim();
                string key = (p.CommodityKey ?? "").Trim();

                if (key.Length == 0) { problems.Add("a mapping has no commodity key"); continue; }
                if (pat.Length == 0) { problems.Add($"'{key}' has an empty pattern"); continue; }

                if (pat.Length < MinPatternLength)
                    problems.Add($"pattern '{pat}' is too short to be safe — it is matched as a "
                               + "SUBSTRING of every type name, so a fragment this small will claim "
                               + "types nobody meant it to");

                if (table != null && table.ResolveByCommodityKey(key) == null)
                    problems.Add($"'{key}' is not a commodity in the supplier-unit table — check the "
                               + "spelling against STING_SUPPLIER_UNITS.json");

                if (!seen.Add(key + "|" + pat))
                    problems.Add($"'{pat}' is mapped to '{key}' more than once");
            }
            return problems;
        }

        /// <summary>
        /// Three characters. Two ("22") would match "Generic - 225mm" and also
        /// every 2200-wide door in the model.
        /// </summary>
        public const int MinPatternLength = 3;
    }

    public static class SupplierUnitPatcher
    {
        /// <summary>
        /// Add each patch's pattern to its commodity's rule, in place.
        ///
        /// ADDITIVE only. A pattern is appended to MatchTypePatterns and no
        /// other field of the rule is read or written, so this cannot change
        /// what a commodity IS — only which types reach it.
        ///
        /// A patch naming a commodity that does not exist is REPORTED, not
        /// dropped: silently ignoring a typo leaves somebody believing they
        /// mapped a type they did not.
        /// </summary>
        public static List<string> Apply(SupplierUnitTable table, SupplierUnitPatchFile patches)
        {
            var applied = new List<string>();
            if (table?.Rules == null || patches?.TypePatterns == null) return applied;

            foreach (var p in patches.TypePatterns)
            {
                if (p == null) continue;
                string key = (p.CommodityKey ?? "").Trim();
                string pat = (p.Pattern ?? "").Trim();
                if (key.Length == 0 || pat.Length == 0) continue;

                var rule = table.ResolveByCommodityKey(key);
                if (rule == null) continue;   // reported by Validate, not silently healed here

                if (rule.MatchTypePatterns == null)
                    rule.MatchTypePatterns = new List<string>();

                if (!rule.MatchTypePatterns.Any(x =>
                        string.Equals(x, pat, StringComparison.OrdinalIgnoreCase)))
                {
                    rule.MatchTypePatterns.Add(pat);
                    applied.Add($"{pat} → {key}");
                }
            }
            return applied;
        }

        /// <summary>
        /// The line for the export notes, or NULL when the project patches
        /// nothing — which is the default and not worth a line.
        /// </summary>
        public static string Summary(IReadOnlyCollection<string> applied)
        {
            if (applied == null || applied.Count == 0) return null;
            return $"Type mappings: {applied.Count} model type name(s) are mapped to a commodity by "
                 + "this project — " + string.Join("; ", applied.Take(6))
                 + (applied.Count > 6 ? ", …" : "")
                 + ". These change which commodity a row converts to, and therefore its UNIT and "
                 + "quantity. They do not change any conversion factor: those stay in the shipped "
                 + "table, so a corporate correction still reaches this project.";
        }
    }
}
