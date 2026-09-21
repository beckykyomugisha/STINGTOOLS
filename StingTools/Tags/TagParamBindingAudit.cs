// TagParamBindingAudit - does every tag family's label parameter reach the
// category that family tags?
//
// Revit-free: parses the shipped tag config and RESOLVED_BINDINGS.csv and
// compares them, so it runs as a test.
//
// WHY THIS EXISTS
//
// A tag label bound to a parameter the host category does not carry renders
// EMPTY. Not an error, not a warning - a blank line on a drawing.
//
// Measured 2026-09-21: 128 of 206 tag families displayed at least one
// discipline parameter that RESOLVED_BINDINGS.csv did not bind to that
// family's own declared category. The shape was always the same - a parameter
// bound to a discipline's MAIN categories but not to the sibling category that
// has its own tag. STR_REBAR_SIZE_MM reached Floors, Structural Columns and
// Structural Foundations but not Structural Area Reinforcement, so the Area
// Reinforcement tag showed a blank where the bar size belongs.
//
// The rule is one sentence: a tag's label parameters must be bound to the
// category that tag tags. This asserts it.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace StingTools.Tags
{
    /// <summary>One family's label parameter that its own category cannot carry.</summary>
    public class UnboundTagParam
    {
        public string FamilyName { get; set; }
        public string HostCategory { get; set; }
        public string Param { get; set; }
        /// <summary>Categories the spec DOES bind it to, for the message.</summary>
        public string BoundTo { get; set; }

        public override string ToString()
            => FamilyName + " [" + HostCategory + "] cannot show " + Param + " (bound to: " + BoundTo + ")";
    }

    /// <summary>Compares the tag config's label rows against the binding spec.</summary>
    public static class TagParamBindingAudit
    {
        private static readonly Regex FamilyLine =
            new Regex(@"^Tag\s+Family\s*#\d+\s*:\s*(?<n>.+?)\s*$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex CategoryLine =
            new Regex(@"Category\s*:\s*(?<c>[^,•|]+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex TierCell = new Regex(@"^T\d$", RegexOptions.Compiled);
        private static readonly Regex ParamCell = new Regex(@"^[A-Z][A-Z0-9_]+$", RegexOptions.Compiled);

        /// <summary>
        /// Parses RESOLVED_BINDINGS.csv into param to categories. A row of
        /// "&lt;ALL&gt;" means universal and lands in <paramref name="universal"/>.
        /// </summary>
        public static Dictionary<string, HashSet<string>> ParseSpec(
            IEnumerable<string> lines, out HashSet<string> universal)
        {
            var spec = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
            universal = new HashSet<string>(StringComparer.Ordinal);

            foreach (var raw in lines ?? Enumerable.Empty<string>())
            {
                string l = raw == null ? null : raw.Trim();
                if (string.IsNullOrEmpty(l) || l.StartsWith("#")) continue;

                int comma = l.IndexOf(',');
                if (comma <= 0) continue;

                string p = l.Substring(0, comma).Trim();
                string v = l.Substring(comma + 1).Trim();
                if (p.Length == 0) continue;

                if (v == "<ALL>") { universal.Add(p); continue; }

                spec[p] = new HashSet<string>(
                    v.Split('|').Select(x => x.Trim()).Where(x => x.Length > 0), StringComparer.Ordinal);
            }
            return spec;
        }

        /// <summary>
        /// Every label parameter a family displays, keyed by normalised family
        /// name, with that family's declared host category. Handles both config
        /// dialects.
        /// </summary>
        public static Dictionary<string, TagFamilyLabels> ParseFamilies(IEnumerable<string> lines)
        {
            var result = new Dictionary<string, TagFamilyLabels>(StringComparer.Ordinal);
            string current = null;

            foreach (var raw in lines ?? Enumerable.Empty<string>())
            {
                string l = raw == null ? null : raw.Trim();
                if (string.IsNullOrEmpty(l)) continue;

                if (l.StartsWith("TAG_FAMILY,", StringComparison.OrdinalIgnoreCase))
                {
                    var f = TagConfigDeclarations.SplitCsv(l);
                    if (f.Count > 3 && f[1].Trim().Length > 0 && f[3].Trim().Length > 0)
                    {
                        string k = TagCategoryNameForms.NormaliseKey(f[1].Trim());
                        if (!result.ContainsKey(k))
                            result[k] = new TagFamilyLabels { Name = f[1].Trim(), Category = f[3].Trim() };
                    }
                    current = null;
                    continue;
                }

                if (l.StartsWith("#")) continue;

                var fm = FamilyLine.Match(l);
                if (fm.Success) { current = fm.Groups["n"].Value.Trim(); continue; }
                if (current == null) continue;

                if (l.StartsWith("TAG7:", StringComparison.OrdinalIgnoreCase) && l.Contains("Category:"))
                {
                    string k = TagCategoryNameForms.NormaliseKey(current);
                    if (!result.ContainsKey(k))
                        result[k] = new TagFamilyLabels
                        {
                            Name = current,
                            Category = CategoryLine.Match(l).Groups["c"].Value.Trim()
                        };
                    continue;
                }

                var cells = TagConfigDeclarations.SplitCsv(l);
                if (cells.Count > 2 && TierCell.IsMatch(cells[1].Trim()) && ParamCell.IsMatch(cells[2].Trim()))
                {
                    string k = TagCategoryNameForms.NormaliseKey(current);
                    TagFamilyLabels e;
                    if (result.TryGetValue(k, out e) && !e.Params.Contains(cells[2].Trim()))
                        e.Params.Add(cells[2].Trim());
                }
            }

            return result;
        }

        /// <summary>
        /// Every label parameter that cannot reach its own family's category.
        ///
        /// <para>Multi-Category families are skipped: they serve many hosts by
        /// design, so "its own category" has no single answer for them.</para>
        /// </summary>
        public static List<UnboundTagParam> Audit(
            Dictionary<string, TagFamilyLabels> families,
            Dictionary<string, HashSet<string>> spec,
            HashSet<string> universal)
        {
            var bad = new List<UnboundTagParam>();
            if (families == null) return bad;

            foreach (var kv in families)
            {
                string cat = kv.Value == null ? null : kv.Value.Category;
                if (string.IsNullOrWhiteSpace(cat)) continue;
                if (cat.StartsWith("Multi", StringComparison.OrdinalIgnoreCase)) continue;

                foreach (string p in kv.Value.Params)
                {
                    if (universal != null && universal.Contains(p)) continue;
                    // Tier gates are family parameters, not shared ones.
                    if (p.StartsWith("TAG_PARA_STATE", StringComparison.Ordinal)) continue;

                    HashSet<string> cats;
                    if (spec == null || !spec.TryGetValue(p, out cats))
                    {
                        bad.Add(new UnboundTagParam
                        {
                            FamilyName = kv.Value.Name, HostCategory = cat, Param = p,
                            BoundTo = "nothing - absent from the spec"
                        });
                        continue;
                    }

                    if (!cats.Contains(cat))
                        bad.Add(new UnboundTagParam
                        {
                            FamilyName = kv.Value.Name, HostCategory = cat, Param = p,
                            BoundTo = string.Join(", ", cats.OrderBy(x => x, StringComparer.Ordinal).Take(4))
                        });
                }
            }
            return bad;
        }
    }

    /// <summary>A family's declared category and the parameters its labels show.</summary>
    public class TagFamilyLabels
    {
        public string Name { get; set; }
        public string Category { get; set; }
        public List<string> Params { get; } = new List<string>();
    }
}
