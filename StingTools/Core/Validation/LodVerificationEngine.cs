using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Autodesk.Revit.DB;
using Newtonsoft.Json;
using StingTools.Core;

namespace StingTools.Core.Validation
{
    // ─────────────────────────────────────────────────────────────────────────
    // Phase 192 (B1) — LOD Verification Engine.
    //
    // Verifies element maturity against a milestone LOD (200/300/350/400) using
    // the STING_LOD_MATRIX.json matrix (+ project overlay). This is a PARAMETER
    // + NAMING + GEOMETRY-PRESENCE maturity proxy, NOT a geometric survey: STING
    // can verify that geometry exists, is not a placeholder family, and carries
    // the parameters expected at the LOD — it cannot verify dimensional accuracy.
    // The command output states this limitation.
    // ─────────────────────────────────────────────────────────────────────────

    // LodCheck / LodCategoryRule / LodMilestone / LodMatrix / ResolvedLodCheck and
    // the LodTally pass/skip accounting now live in LodMatrixModel.cs — same
    // namespace, no Autodesk.Revit.* dependency, so StingTools.Tags.Tests can
    // <Compile Include> them and assert the "empty scope is not a pass" rule
    // against the real types. Everything below needs a Revit Document.

    public class LodElementResult
    {
        public ElementId ElementId { get; set; }
        public string Category { get; set; } = "";
        public string Discipline { get; set; } = "";
        public bool Pass { get; set; }
        public List<string> Reasons { get; set; } = new List<string>();
    }

    /// <summary>
    /// One LOD run. The pass / fail / SKIP accounting — including
    /// <see cref="LodTally.NoElementsInScope"/> and <see cref="LodTally.SkippedNoRule"/>
    /// — lives on the Revit-free <see cref="LodTally"/> base in LodMatrixModel.cs so
    /// it is unit-testable; this class adds the element-level detail that needs Revit.
    /// </summary>
    public class LodVerificationResult : LodTally
    {
        public List<LodElementResult> Elements { get; set; } = new List<LodElementResult>();
        public Dictionary<string, (int total, int pass)> ByCategory { get; set; }
            = new Dictionary<string, (int, int)>(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, (int total, int pass)> ByDiscipline { get; set; }
            = new Dictionary<string, (int, int)>(StringComparer.OrdinalIgnoreCase);
        public bool ClashCheckRequestedButNotVerifiable { get; set; }
    }

    // ── Registry: corporate baseline + project overlay, merged by id/category ──
    public static class LodMatrixRegistry
    {
        private const string CorporateFileName = "STING_LOD_MATRIX.json";
        private const string ProjectFileName = "lod_matrix.json";

        private static readonly ConcurrentDictionary<string, LodMatrix> _cache
            = new ConcurrentDictionary<string, LodMatrix>(StringComparer.OrdinalIgnoreCase);

        private static string DocKey(Document doc)
        {
            try { return Path.GetDirectoryName(doc?.PathName ?? "") ?? ""; }
            catch { return ""; }
        }

        public static string ProjectOverlayPath(Document doc)
        {
            string dir = DocKey(doc);
            if (string.IsNullOrEmpty(dir)) return null;
            return StingPaths.MetaFile(doc, "_BIM_COORD", ProjectFileName);
        }

        public static LodMatrix Get(Document doc) => _cache.GetOrAdd(DocKey(doc), _ => Load(doc));

        public static void Reload(Document doc = null)
        {
            if (doc == null) _cache.Clear();
            else _cache.TryRemove(DocKey(doc), out _);
        }

        public static void InvalidateCache(Document doc) => Reload(doc);

        private static LodMatrix Load(Document doc)
        {
            LodMatrix matrix = null;
            try
            {
                string corpPath = StingToolsApp.FindDataFile(CorporateFileName);
                if (!string.IsNullOrEmpty(corpPath) && File.Exists(corpPath))
                    matrix = JsonConvert.DeserializeObject<LodMatrix>(File.ReadAllText(corpPath));
            }
            catch (Exception ex) { StingLog.Warn($"LodMatrixRegistry corporate load: {ex.Message}"); }
            matrix = matrix ?? new LodMatrix();

            // Project overlay — milestones win by id, categoryRules win by category,
            // placeholder patterns replaced wholesale when the overlay supplies any.
            try
            {
                string projPath = ProjectOverlayPath(doc);
                if (!string.IsNullOrEmpty(projPath) && File.Exists(projPath))
                {
                    var overlay = JsonConvert.DeserializeObject<LodMatrix>(File.ReadAllText(projPath));
                    if (overlay != null)
                    {
                        foreach (var m in overlay.Milestones ?? new List<LodMilestone>())
                        {
                            if (string.IsNullOrWhiteSpace(m?.Id)) continue;
                            matrix.Milestones.RemoveAll(x => string.Equals(x.Id, m.Id, StringComparison.OrdinalIgnoreCase));
                            matrix.Milestones.Add(m);
                        }
                        foreach (var c in overlay.CategoryRules ?? new List<LodCategoryRule>())
                        {
                            if (string.IsNullOrWhiteSpace(c?.Category)) continue;
                            matrix.CategoryRules.RemoveAll(x => string.Equals(x.Category, c.Category, StringComparison.OrdinalIgnoreCase));
                            matrix.CategoryRules.Add(c);
                        }
                        if (overlay.PlaceholderFamilyPatterns != null && overlay.PlaceholderFamilyPatterns.Count > 0)
                            matrix.PlaceholderFamilyPatterns = overlay.PlaceholderFamilyPatterns;
                        StingLog.Info($"LodMatrixRegistry: project overlay loaded from {projPath}");
                    }
                }
            }
            catch (Exception ex) { StingLog.Warn($"LodMatrixRegistry overlay load: {ex.Message}"); }

            return matrix;
        }
    }

    public static class LodVerificationEngine
    {
        /// <summary>Category names that carry an explicit rule (excludes the "*" default).</summary>
        public static List<string> ExplicitCategories(Document doc) =>
            (LodMatrixRegistry.Get(doc).CategoryRules ?? new List<LodCategoryRule>())
                .Select(r => r.Category)
                .Where(c => !string.IsNullOrEmpty(c) && c != "*")
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

        public static LodMatrix Matrix(Document doc) => LodMatrixRegistry.Get(doc);

        public static LodVerificationResult Verify(Document doc, string milestoneId, IEnumerable<Element> scope)
        {
            var matrix = LodMatrixRegistry.Get(doc);
            var ms = matrix.Milestones?.FirstOrDefault(m =>
                string.Equals(m.Id, milestoneId, StringComparison.OrdinalIgnoreCase));
            var result = new LodVerificationResult();
            if (ms == null)
            {
                StingLog.Warn($"LodVerificationEngine: unknown milestone '{milestoneId}'");
                return result;
            }
            result.MilestoneId = ms.Id;
            result.MilestoneName = ms.Name;
            result.Lod = ms.Lod;
            string lodKey = ms.Lod.ToString();

            var placeholderRx = CompilePatterns(matrix.PlaceholderFamilyPatterns);
            // Cache resolved checks per category so a 50k-element pass resolves
            // inheritance once per category, not per element.
            var resolvedByCat = new Dictionary<string, ResolvedLodCheck>(StringComparer.OrdinalIgnoreCase);

            foreach (var el in scope)
            {
                if (el == null) continue;
                string cat = ParameterHelpers.GetCategoryName(el) ?? "";
                if (!resolvedByCat.TryGetValue(cat, out var check))
                {
                    check = Resolve(matrix, cat, lodKey);
                    resolvedByCat[cat] = check;
                }
                if (check == null)
                {
                    // No rule and no "*" default. Count it rather than dropping it
                    // silently — an element the matrix cannot speak about is a gap
                    // in the matrix, and the report has to say so.
                    result.RecordSkip(cat);
                    continue;
                }

                string disc = ParameterHelpers.GetString(el, ParamRegistry.DISC);
                if (string.IsNullOrEmpty(disc))
                {
                    TagConfig.DiscMap.TryGetValue(cat, out disc);
                    if (string.IsNullOrEmpty(disc)) disc = "(none)";
                }

                var er = EvaluateElement(el, check, placeholderRx);
                er.Category = cat;
                er.Discipline = disc;
                if (check.RequireNoUnresolvedClash) result.ClashCheckRequestedButNotVerifiable = true;

                result.Elements.Add(er);
                result.Total++;
                if (er.Pass) result.Passed++;

                Roll(result.ByCategory, cat, er.Pass);
                Roll(result.ByDiscipline, disc, er.Pass);
            }
            return result;
        }

        private static void Roll(Dictionary<string, (int total, int pass)> d, string key, bool pass)
        {
            if (string.IsNullOrEmpty(key)) key = "(none)";
            d.TryGetValue(key, out var v);
            d[key] = (v.total + 1, v.pass + (pass ? 1 : 0));
        }

        /// <summary>
        /// Resolve the effective check for (category, lod). Delegates to the
        /// Revit-free <see cref="LodRuleResolver"/> so the plugin and the unit
        /// tests exercise exactly the same resolution code, not two copies.
        /// </summary>
        public static ResolvedLodCheck Resolve(LodMatrix matrix, string category, string lodKey)
            => LodRuleResolver.Resolve(matrix, category, lodKey);

        private static LodElementResult EvaluateElement(Element el, ResolvedLodCheck check, List<Regex> placeholderRx)
        {
            var er = new LodElementResult { ElementId = el.Id, Pass = true };

            if (check.RequireGeometry && !HasGeometry(el))
            { er.Pass = false; er.Reasons.Add("no model geometry (degenerate/empty bounding box)"); }

            string famName = Safe(ParameterHelpers.GetFamilyName(el));
            string typeName = Safe(ParameterHelpers.GetFamilySymbolName(el));

            if (check.ForbidPlaceholderFamilies && MatchesAny(placeholderRx, famName, typeName))
            { er.Pass = false; er.Reasons.Add($"placeholder family/type ('{famName}' / '{typeName}')"); }

            if (check.RequireTypeNotGeneric &&
                (famName.IndexOf("generic", StringComparison.OrdinalIgnoreCase) >= 0 ||
                 typeName.IndexOf("generic", StringComparison.OrdinalIgnoreCase) >= 0))
            { er.Pass = false; er.Reasons.Add($"generic type ('{famName}' / '{typeName}')"); }

            if (check.RequireManufacturerType && MatchesAny(placeholderRx, famName, typeName))
            { er.Pass = false; er.Reasons.Add("type not manufacturer-specified (matches placeholder pattern)"); }

            foreach (var p in check.RequiredParams ?? new List<string>())
            {
                string v = ParameterHelpers.GetString(el, p);
                if (string.IsNullOrEmpty(v))
                { er.Pass = false; er.Reasons.Add($"missing/empty {p}"); continue; }

                // Every date parameter is TEXT, and the BEP mandates YYYY-MM-DD.
                // Nothing enforced it, so a value present but in a local
                // convention passed the completeness check and then could not be
                // sorted or compared once the handover dataset was assembled --
                // by which point the installer has left site. Only parameters the
                // registry documents as ISO are checked; the DD-Mon-YYYY
                // title-block dates are correct as they are. See DateFormatRule.
                if (DateFormatRule.IsDateParam(p) && !DateFormatRule.IsConforming(v))
                { er.Pass = false; er.Reasons.Add($"{p} is '{v}', not {DateFormatRule.Format}"); }
            }

            foreach (var d in check.RequiredDims ?? new List<string>())
            {
                double? v = ReadNumeric(el, d);
                if (!(v.HasValue && v.Value > 0))
                { er.Pass = false; er.Reasons.Add($"{d} not > 0"); }
            }

            return er;
        }

        private static bool HasGeometry(Element el)
        {
            try
            {
                BoundingBoxXYZ bb = el.get_BoundingBox(null);
                if (bb == null) return false;
                const double eps = 1e-6;
                double dx = Math.Abs(bb.Max.X - bb.Min.X);
                double dy = Math.Abs(bb.Max.Y - bb.Min.Y);
                double dz = Math.Abs(bb.Max.Z - bb.Min.Z);
                // Require a non-degenerate extent in at least two axes (a flat
                // sheet element still passes; a zero/point box does not).
                int nonZero = (dx > eps ? 1 : 0) + (dy > eps ? 1 : 0) + (dz > eps ? 1 : 0);
                return nonZero >= 2;
            }
            catch { return false; }
        }

        private static double? ReadNumeric(Element el, string paramName)
        {
            try
            {
                var p = el.LookupParameter(paramName);
                if (p == null || !p.HasValue) return null;
                switch (p.StorageType)
                {
                    case StorageType.Double: return p.AsDouble();
                    case StorageType.Integer: return p.AsInteger();
                    case StorageType.String:
                        return double.TryParse(p.AsString(), out double d) ? d : (double?)null;
                    default: return null;
                }
            }
            catch { return null; }
        }

        private static List<Regex> CompilePatterns(List<string> patterns)
        {
            var list = new List<Regex>();
            foreach (var p in patterns ?? new List<string>())
            {
                try { list.Add(new Regex(p)); }
                catch (Exception ex) { StingLog.Warn($"LOD placeholder pattern '{p}' invalid: {ex.Message}"); }
            }
            return list;
        }

        private static bool MatchesAny(List<Regex> rx, params string[] values)
        {
            foreach (var r in rx)
                foreach (var v in values)
                    if (!string.IsNullOrEmpty(v) && r.IsMatch(v)) return true;
            return false;
        }

        private static string Safe(string s) => s ?? "";
    }
}
