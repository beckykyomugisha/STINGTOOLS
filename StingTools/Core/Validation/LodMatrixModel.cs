// LodMatrixModel.cs — the Revit-free half of the LOD verification engine.
//
// WHY THIS FILE EXISTS, SEPARATELY FROM LodVerificationEngine.cs
// The engine's Verify() needs a Revit Document and Elements, so it cannot run on
// a CI runner. The parts that decide WHAT a gate means — the matrix data model,
// inheritance resolution, and the pass/skip tally — have no Revit dependency at
// all, and they are where the interesting failure lives:
//
//   Verify() drops an element whose category resolves to no check BEFORE counting
//   it. With no "*" fallback in the matrix, every element is dropped, Total stays
//   0, and OverallPct returns 100.0 — a green gate over nothing. That is the same
//   failure class as the eleven workflow presets that executed zero steps and
//   reported success (#630): an empty list standing in for an error.
//
// Splitting those parts out lets StingTools.Tags.Tests <Compile Include> this file
// and assert the behaviour against the REAL shipped types, with no Revit stub and
// no mock. Follows the pattern StingTools.Clash.Tests and StingTools.Tags.Tests
// already use. Add nothing here that touches Autodesk.Revit.*.

using System;
using System.Collections.Generic;
using System.Linq;

namespace StingTools.Core.Validation
{
    public class LodCheck
    {
        public bool? RequireGeometry { get; set; }
        public bool? ForbidPlaceholderFamilies { get; set; }
        public bool? RequireTypeNotGeneric { get; set; }
        public bool? RequireManufacturerType { get; set; }
        public bool? RequireNoUnresolvedClash { get; set; }
        public List<string> RequiredParams { get; set; }
        public List<string> RequiredDims { get; set; }
        public string Inherit { get; set; }
    }

    public class LodCategoryRule
    {
        public string Category { get; set; }
        public Dictionary<string, LodCheck> Checks { get; set; }
    }

    public class LodMilestone
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public int Lod { get; set; }
    }

    public class LodMatrix
    {
        public string Version { get; set; }
        public string Description { get; set; }
        public List<LodMilestone> Milestones { get; set; } = new List<LodMilestone>();
        public List<LodCategoryRule> CategoryRules { get; set; } = new List<LodCategoryRule>();
        public List<string> PlaceholderFamilyPatterns { get; set; } = new List<string>();
    }

    /// <summary>Effective (inheritance-resolved) check for one (category, LOD).</summary>
    public class ResolvedLodCheck
    {
        public bool RequireGeometry;
        public bool ForbidPlaceholderFamilies;
        public bool RequireTypeNotGeneric;
        public bool RequireManufacturerType;
        public bool RequireNoUnresolvedClash;
        public List<string> RequiredParams = new List<string>();
        public List<string> RequiredDims = new List<string>();

        /// <summary>
        /// KUT-4 — true when this rung asserts NOTHING an element could fail: no flag set,
        /// no required parameter, no required dimension.
        ///
        /// <para><b>Derived, never hardcoded to "100".</b> LOD 100 is the rung that has this
        /// shape today, and the reason is not an oversight in the matrix: the BIMForum LOD
        /// specification defines 100 as CONCEPTUAL — an element "may be graphically
        /// represented with a symbol or other generic representation", and its information
        /// "may be derived from other Model Elements". There is genuinely nothing per-element
        /// to verify, so inventing a requirement would be inventing a standard.</para>
        ///
        /// <para>Deriving it rather than naming the rung buys two things. If someone later
        /// gives 100 a real check it becomes gating automatically, with no second place to
        /// remember. And if someone empties 300 — by a bad merge or a mistyped key that
        /// Newtonsoft leaves at its default — that rung stops silently passing everything and
        /// starts reporting NOT ASSESSED, which is the same class of defect and would
        /// otherwise look like a perfect score.</para>
        /// </summary>
        public bool AssertsNothing =>
            !RequireGeometry && !ForbidPlaceholderFamilies && !RequireTypeNotGeneric
            && !RequireManufacturerType && !RequireNoUnresolvedClash
            && (RequiredParams == null || RequiredParams.Count == 0)
            && (RequiredDims == null || RequiredDims.Count == 0);
    }

    /// <summary>
    /// Pass / fail / SKIP accounting for one LOD run, with no Revit types.
    /// <see cref="LodVerificationResult"/> derives from this and adds the
    /// element-level detail that does need Revit.
    /// </summary>
    public class LodTally
    {
        public string MilestoneId { get; set; } = "";
        public string MilestoneName { get; set; } = "";
        public int Lod { get; set; }
        public int Total { get; set; }
        public int Passed { get; set; }
        public int Failed => Total - Passed;

        /// <summary>
        /// Returns 100.0 when <see cref="Total"/> is 0. That is a trap, kept for
        /// backwards compatibility of existing callers — branch on
        /// <see cref="NoElementsInScope"/> first, never on this value alone.
        /// </summary>
        public double OverallPct => Total > 0 ? 100.0 * Passed / Total : 100.0;

        /// <summary>
        /// Elements dropped because their category resolved to no check and the
        /// matrix carries no "*" fallback. They are OUTSIDE <see cref="Total"/>,
        /// so without this counter they vanish from the report entirely.
        /// </summary>
        public int SkippedNoRule { get; set; }

        /// <summary>Skipped count per category, so a report can name what it dropped.</summary>
        public Dictionary<string, int> SkippedByCategory { get; set; }
            = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// KUT-4 — elements whose rung asserts nothing (see
        /// <see cref="ResolvedLodCheck.AssertsNothing"/>).
        ///
        /// <para>OUTSIDE <see cref="Total"/>, exactly like <see cref="SkippedNoRule"/>, and
        /// for the same reason: counting them as passes would report coverage the rung never
        /// earned. LOD 100 previously passed every element in scope and reported 100%, which
        /// reads identically to a model that genuinely satisfies every requirement — a rung
        /// that cannot fail is not a gate, and a report that cannot tell you so is worse than
        /// no report.</para>
        /// </summary>
        public int NotAssessed { get; set; }

        /// <summary>Not-assessed count per category, so a report can name them.</summary>
        public Dictionary<string, int> NotAssessedByCategory { get; set; }
            = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// True when the rung itself asserts nothing — elements WERE in scope, and every one
        /// of them fell through because there was no requirement to test.
        ///
        /// <para>Distinct from <see cref="NoElementsInScope"/>, which means the scope was
        /// empty. Both must be reported as words rather than as a percentage, but they are
        /// different facts and a reader needs to know which one they are looking at: "there
        /// was nothing to check" is a matrix problem, "there was nothing here" is a model or
        /// filter problem.</para>
        /// </summary>
        public bool RungAssertsNothing => Total == 0 && NotAssessed > 0;

        /// <summary>
        /// True when <see cref="OverallPct"/> means something. The one call every report
        /// should make before printing a number.
        /// </summary>
        public bool HasMeaningfulResult => Total > 0;

        /// <summary>
        /// True when nothing was verified. Callers MUST report this as "no elements
        /// in scope" rather than as a percentage.
        /// </summary>
        public bool NoElementsInScope => Total == 0;

        /// <summary>Record one element the matrix could not speak about.</summary>
        public void RecordSkip(string category)
        {
            SkippedNoRule++;
            string key = string.IsNullOrEmpty(category) ? "(no category)" : category;
            SkippedByCategory.TryGetValue(key, out int n);
            SkippedByCategory[key] = n + 1;
        }

        /// <summary>Record one element whose rung asserted nothing about it.</summary>
        public void RecordNotAssessed(string category)
        {
            NotAssessed++;
            string key = string.IsNullOrEmpty(category) ? "(no category)" : category;
            NotAssessedByCategory.TryGetValue(key, out int n);
            NotAssessedByCategory[key] = n + 1;
        }
    }

    /// <summary>
    /// Matrix rule resolution — category lookup, "*" fallback, and LOD-rung
    /// inheritance with a loop guard. Revit-free on purpose (see file header).
    /// </summary>
    public static class LodRuleResolver
    {
        /// <summary>
        /// Effective check for (category, lod), folding inheritance. Returns null
        /// when the category has no rule AND the matrix has no "*" fallback — the
        /// caller must treat that as "out of scope", not as a pass.
        /// </summary>
        public static ResolvedLodCheck Resolve(LodMatrix matrix, string category, string lodKey)
        {
            var rule = matrix?.CategoryRules?.FirstOrDefault(r =>
                           string.Equals(r.Category, category, StringComparison.OrdinalIgnoreCase))
                       ?? matrix?.CategoryRules?.FirstOrDefault(r => r.Category == "*");
            if (rule?.Checks == null) return null;
            return ResolveCheck(rule.Checks, lodKey, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
        }

        private static ResolvedLodCheck ResolveCheck(Dictionary<string, LodCheck> checks, string lodKey, HashSet<string> seen)
        {
            if (!checks.TryGetValue(lodKey, out var c) || c == null) return null;
            if (!seen.Add(lodKey)) return new ResolvedLodCheck(); // inheritance loop guard

            ResolvedLodCheck baseCheck = null;
            if (!string.IsNullOrEmpty(c.Inherit))
                baseCheck = ResolveCheck(checks, c.Inherit, seen);
            baseCheck = baseCheck ?? new ResolvedLodCheck();

            return new ResolvedLodCheck
            {
                RequireGeometry          = c.RequireGeometry          ?? baseCheck.RequireGeometry,
                ForbidPlaceholderFamilies= c.ForbidPlaceholderFamilies?? baseCheck.ForbidPlaceholderFamilies,
                RequireTypeNotGeneric    = c.RequireTypeNotGeneric    ?? baseCheck.RequireTypeNotGeneric,
                RequireManufacturerType  = c.RequireManufacturerType  ?? baseCheck.RequireManufacturerType,
                RequireNoUnresolvedClash = c.RequireNoUnresolvedClash ?? baseCheck.RequireNoUnresolvedClash,
                RequiredParams = MergeList(baseCheck.RequiredParams, c.RequiredParams),
                RequiredDims   = MergeList(baseCheck.RequiredDims,   c.RequiredDims),
            };
        }

        // "+name" adds to the inherited list; a plain name replaces the inherited list.
        private static List<string> MergeList(List<string> inherited, List<string> level)
        {
            if (level == null || level.Count == 0) return new List<string>(inherited ?? new List<string>());
            var plus = level.Where(s => s != null && s.StartsWith("+")).Select(s => s.Substring(1).Trim());
            var plain = level.Where(s => s != null && !s.StartsWith("+")).Select(s => s.Trim());
            var baseList = plain.Any() ? plain.ToList() : new List<string>(inherited ?? new List<string>());
            baseList.AddRange(plus);
            return baseList.Where(s => !string.IsNullOrEmpty(s))
                           .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }
    }
}
