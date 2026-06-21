// ============================================================================
// MepFixtureBuilder.cs — Phase: MEP-from-DWG V1.
//
// Places MEP fixtures from a MepDetectionResult. For each detected block it
// resolves a FamilySymbol (by category + optional family/type hint), activates
// it, and places an unhosted/level-based instance at the block insertion point
// with the block rotation and a mounting-height-driven Z. When no symbol
// resolves the fixture is SKIPPED and counted — no synthetic geometry is ever
// created (mirrors FixturePlacementEngine's resolve-or-skip contract).
//
// Placed instances are workset-assigned and ISO 19650 auto-tagged via the same
// path native Placement-Center output uses, so they flow into tagging / BOQ /
// validation unchanged. Host-snapping (nearest wall/ceiling) is V2.
// ============================================================================
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using StingTools.Core;
using StingTools.Model;   // Units, ModelWorksetAssigner, ModelEngine.AutoTagCreatedElements

namespace StingTools.Core.Cad.Mep
{
    public class MepBuildResult
    {
        public int Placed { get; set; }
        public int SkippedNoSymbol { get; set; }
        public int Failed { get; set; }
        public List<ElementId> CreatedIds { get; } = new List<ElementId>();
        public List<string> Warnings { get; } = new List<string>();
        /// <summary>Per-category placed / skipped-no-symbol counts (audit).</summary>
        public Dictionary<string, (int placed, int skipped)> ByCategory { get; }
            = new Dictionary<string, (int, int)>(StringComparer.OrdinalIgnoreCase);
        public int Tagged { get; set; }

        public void Bump(string cat, bool placed)
        {
            ByCategory.TryGetValue(cat ?? "", out var v);
            ByCategory[cat ?? ""] = placed ? (v.placed + 1, v.skipped) : (v.placed, v.skipped + 1);
        }
    }

    public class MepFixtureBuilder
    {
        private readonly Document _doc;
        private readonly Dictionary<string, FamilySymbol> _symbolCache
            = new Dictionary<string, FamilySymbol>(StringComparer.OrdinalIgnoreCase);

        public MepFixtureBuilder(Document doc) => _doc = doc ?? throw new ArgumentNullException(nameof(doc));

        /// <summary>Place all detected fixtures on the target level. One transaction.</summary>
        public MepBuildResult Place(MepDetectionResult detection, Level level)
        {
            var result = new MepBuildResult();
            if (detection == null || detection.Fixtures.Count == 0 || level == null) return result;

            using (var tx = new Transaction(_doc, "STING MODEL: Place MEP Fixtures from DWG"))
            {
                tx.Start();
                try
                {
                    int i = 0;
                    foreach (var fx in detection.Fixtures)
                    {
                        i++;
                        var symbol = ResolveSymbol(fx.Rule, result);
                        if (symbol == null)
                        {
                            result.SkippedNoSymbol++;
                            result.Bump(fx.Category, placed: false);
                            continue;
                        }

                        try
                        {
                            if (!symbol.IsActive) { symbol.Activate(); _doc.Regenerate(); }

                            var bp = fx.Block.InsertionPoint;
                            var p = new XYZ(bp.X, bp.Y, level.Elevation + StingTools.Model.Units.Mm(fx.MountingHeightMm));
                            var inst = _doc.Create.NewFamilyInstance(p, symbol, level, StructuralType.NonStructural);

                            if (Math.Abs(fx.Block.Rotation) > 1e-6)
                            {
                                var axis = Line.CreateBound(p, p + XYZ.BasisZ);
                                ElementTransformUtils.RotateElement(_doc, inst.Id, axis, fx.Block.Rotation);
                            }

                            ModelWorksetAssigner.Assign(_doc, inst);
                            StampMetadata(inst, fx);

                            result.CreatedIds.Add(inst.Id);
                            result.Placed++;
                            result.Bump(fx.Category, placed: true);
                        }
                        catch (Exception ex)
                        {
                            result.Failed++;
                            if (result.Warnings.Count < 30)
                                result.Warnings.Add($"'{fx.BlockName}' ({fx.Category}): {ex.Message}");
                        }

                        if (i % 50 == 0 && EscapeChecker.IsEscapePressed())
                        {
                            result.Warnings.Add($"Cancelled by user after {i} fixtures.");
                            break;
                        }
                    }
                    tx.Commit();
                }
                catch (Exception ex)
                {
                    tx.RollBack();
                    StingLog.Error("MepFixtureBuilder.Place", ex);
                    result.Warnings.Add($"Placement batch failed (rolled back): {ex.Message}");
                    result.CreatedIds.Clear();
                    return result;
                }
            }

            // ISO 19650 auto-tag — same path native Placement-Center output uses,
            // so DWG-placed fixtures flow into tagging / BOQ / validation unchanged.
            if (result.CreatedIds.Count > 0)
            {
                try { result.Tagged = ModelEngine.AutoTagCreatedElements(_doc, result.CreatedIds); }
                catch (Exception ex) { StingLog.Warn($"MEP fixture auto-tag: {ex.Message}"); }
            }
            return result;
        }

        /// <summary>Resolve a FamilySymbol for a rule: collector by category, narrowed
        /// by optional family/type hint regex; first match wins, else first-for-category.
        /// Returns null (→ skip) when nothing in the project matches — never synthesises.</summary>
        private FamilySymbol ResolveSymbol(MepFixtureRule rule, MepBuildResult result)
        {
            if (rule == null || string.IsNullOrEmpty(rule.Category)) return null;
            string key = $"{rule.Category}|{rule.FamilyHint}|{rule.TypeHint}";
            if (_symbolCache.TryGetValue(key, out var cached)) return cached;

            Regex famRx = BuildRx(rule.FamilyHint, "FamilyHint", rule.Id, result);
            Regex typeRx = BuildRx(rule.TypeHint, "TypeHint", rule.Id, result);

            FamilySymbol picked = null, firstForCategory = null;
            try
            {
                foreach (var fs in new FilteredElementCollector(_doc).OfClass(typeof(FamilySymbol)).Cast<FamilySymbol>())
                {
                    if (fs.Category == null ||
                        !string.Equals(fs.Category.Name, rule.Category, StringComparison.OrdinalIgnoreCase))
                        continue;
                    if (firstForCategory == null) firstForCategory = fs;

                    string famName = fs.Family?.Name ?? "";
                    if (famRx != null && !famRx.IsMatch(famName)) continue;
                    if (typeRx != null && !typeRx.IsMatch(fs.Name ?? "")) continue;

                    picked = fs;
                    break;
                }
            }
            catch (Exception ex) { result.Warnings.Add($"Resolve symbol for '{rule.Category}': {ex.Message}"); }

            var sym = picked ?? firstForCategory;
            _symbolCache[key] = sym;
            return sym;
        }

        private static Regex BuildRx(string pattern, string label, string ruleId, MepBuildResult result)
        {
            if (string.IsNullOrEmpty(pattern)) return null;
            try { return new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant); }
            catch (Exception ex) { result.Warnings.Add($"{label} regex on rule '{ruleId}': {ex.Message}"); return null; }
        }

        /// <summary>Stamp mounting metadata so DWG-placed fixtures match native
        /// Placement-Center output. The mounting height is already encoded in the
        /// instance elevation (Z = level + height). The reference (FFL/Ceiling/
        /// Structure) is stamped to a text param when bound (graceful no-op).
        /// TODO-VERIFY-API: wire the native numeric mounting-height param (MNT_HGT_MM)
        /// in V2 once its exact name + unit (Length vs Number) is confirmed against
        /// the Placement-Center output, to avoid a unit-mismatch write here.</summary>
        private void StampMetadata(Element inst, MepDetectedFixture fx)
        {
            try
            {
                if (!string.IsNullOrEmpty(fx.Rule?.MountingReference))
                    ParameterHelpers.SetString(inst, "MOUNTING_REFERENCE_TXT", fx.Rule.MountingReference, overwrite: true);
            }
            catch (Exception ex) { StingLog.Warn($"MEP fixture stamp {inst?.Id}: {ex.Message}"); }
        }
    }
}
