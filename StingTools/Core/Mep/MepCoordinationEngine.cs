// StingTools — MEP Coordination Engine (Phase C).
//
// Closes the create-systems → coordinated-drawing loop. Reads the MEP system
// CLASSIFICATIONS actually present in the model (the same RBS_SYSTEM_CLASSIFICATION
// the Phase A types carry and Phase B stamps onto elements), resolves the matching
// corporate AEC filter from STING_AEC_FILTERS.json (auto-matched by the live
// classification value — no manual enum map to drift), lazy-creates it via
// AecFilterFactory, and applies it to a view with the filter's own colour override.
//
// The result: a view where Supply Air is GSA-blue, Return Air green, CHW navy …
// driven end-to-end by one classification chain (Phase A colour on the type →
// Phase B classification on the element → Phase C filter override on the view).
//
// CALLER OWNS THE ACTIVE TRANSACTION (for ApplyToView).

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Autodesk.Revit.DB;
using StingTools.Core.Drawing;

namespace StingTools.Core.Mep
{
    public sealed class MepCoordRow
    {
        public string Classification { get; set; } = "";
        public string FilterName { get; set; } = "";
        public bool Applied { get; set; }
        public string Note { get; set; } = "";
    }

    public sealed class MepCoordResult
    {
        public List<MepCoordRow> Rows { get; } = new List<MepCoordRow>();
        public List<string> Warnings { get; } = new List<string>();
        public int Applied => Rows.Count(r => r.Applied);
        public int Unmatched => Rows.Count(r => !r.Applied);
    }

    public static class MepCoordinationEngine
    {
        private const string ClassParam = "RBS_SYSTEM_CLASSIFICATION_PARAM";

        /// <summary>
        /// Distinct MEP system classification display-strings present in the model,
        /// read straight off duct + pipe members (e.g. "Supply Air", "Hydronic Return").
        /// </summary>
        public static List<string> PresentClassifications(Document doc)
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (doc == null) return new List<string>();
            var collector = new FilteredElementCollector(doc)
                .WherePasses(new ElementMulticategoryFilter(new[]
                {
                    BuiltInCategory.OST_DuctCurves,
                    BuiltInCategory.OST_PipeCurves
                }))
                .WhereElementIsNotElementType();
            foreach (var el in collector)
            {
                try
                {
                    var p = el.get_Parameter(BuiltInParameter.RBS_SYSTEM_CLASSIFICATION_PARAM);
                    var v = p?.AsValueString();
                    if (!string.IsNullOrWhiteSpace(v)) set.Add(v.Trim());
                }
                catch { }
            }
            return set.OrderBy(s => s).ToList();
        }

        /// <summary>
        /// Apply the system-classification colour filters to <paramref name="view"/>.
        /// Requires an open transaction. Returns per-classification outcome rows.
        /// </summary>
        public static MepCoordResult ApplyToView(Document doc, View view)
        {
            var result = new MepCoordResult();
            if (doc == null || view == null) { result.Warnings.Add("No document / view."); return result; }
            if (!CanOverride(view))
            {
                result.Warnings.Add($"View '{view.Name}' does not allow graphic overrides (schedule / sheet?).");
                return result;
            }

            var present = PresentClassifications(doc);
            if (present.Count == 0)
            {
                result.Warnings.Add("No duct/pipe members carry a system classification yet — run Phase B (MEP_BuildSystems) first.");
                return result;
            }

            var defs = AecFilterRegistry.ListAll(doc);
            var solid = ParameterHelpers.GetSolidFillPattern(doc)?.Id;
            var existing = new HashSet<ElementId>(view.GetFilters());

            foreach (var cls in present)
            {
                var row = new MepCoordRow { Classification = cls };
                result.Rows.Add(row);

                var def = FindFilterForClassification(defs, cls);
                if (def == null)
                {
                    row.Note = "no AEC filter keyed on this classification — add one to STING_AEC_FILTERS.json";
                    continue;
                }
                row.FilterName = def.Name;

                FilterFactoryResult fr;
                try { fr = AecFilterFactory.FindOrCreate(doc, def); }
                catch (Exception ex) { row.Note = $"filter create: {ex.Message}"; result.Warnings.Add($"{cls}: {ex.Message}"); continue; }
                if (fr == null || !fr.Ok) { row.Note = fr?.Error ?? "filter not created"; continue; }

                try
                {
                    if (!existing.Contains(fr.Filter.Id)) { view.AddFilter(fr.Filter.Id); existing.Add(fr.Filter.Id); }
                    var ogs = BuildOverride(def.DefaultOverride, solid);
                    view.SetFilterOverrides(fr.Filter.Id, ogs);
                    view.SetFilterVisibility(fr.Filter.Id, true);
                    row.Applied = true;
                    row.Note = fr.Created ? "filter created + applied" : "filter applied";
                }
                catch (Exception ex)
                {
                    row.Note = $"apply: {ex.Message}";
                    result.Warnings.Add($"{cls}: apply: {ex.Message}");
                }
            }
            return result;
        }

        // ── helpers ─────────────────────────────────────────────────────────

        private static bool CanOverride(View view)
        {
            try { return view != null && !view.IsTemplate && view.AreGraphicsOverridesAllowed(); }
            catch { return false; }
        }

        private static AecFilterDefinition FindFilterForClassification(
            IReadOnlyList<AecFilterDefinition> defs, string classificationValue)
        {
            if (defs == null) return null;
            foreach (var d in defs)
                if (RuleMatchesClassification(d?.Rule, classificationValue))
                    return d;
            return null;
        }

        private static bool RuleMatchesClassification(AecFilterRule rule, string value)
        {
            if (rule == null) return false;
            if (rule.IsLeaf)
                return string.Equals(rule.Param, ClassParam, StringComparison.OrdinalIgnoreCase)
                    && string.Equals((rule.Value ?? "").Trim(), value, StringComparison.OrdinalIgnoreCase);
            if (rule.Rules != null)
                foreach (var child in rule.Rules)
                    if (RuleMatchesClassification(child, value)) return true;
            return false;
        }

        private static OverrideGraphicSettings BuildOverride(FilterDefaultOverride ov, ElementId solidFill)
        {
            var ogs = new OverrideGraphicSettings();
            if (ov == null) return ogs;

            var proj = ParseHex(ov.ProjColor);
            if (proj != null) ogs.SetProjectionLineColor(proj);
            if (ov.ProjWeight.HasValue && ov.ProjWeight.Value >= 1 && ov.ProjWeight.Value <= 16)
                ogs.SetProjectionLineWeight(ov.ProjWeight.Value);

            var cut = ParseHex(ov.CutColor);
            if (cut != null) ogs.SetCutLineColor(cut);
            if (ov.CutWeight.HasValue && ov.CutWeight.Value >= 1 && ov.CutWeight.Value <= 16)
                ogs.SetCutLineWeight(ov.CutWeight.Value);

            var surf = ParseHex(ov.SurfFgColor);
            if (surf != null && solidFill != null)
            {
                ogs.SetSurfaceForegroundPatternId(solidFill);
                ogs.SetSurfaceForegroundPatternColor(surf);
            }
            var cutFg = ParseHex(ov.CutFgColor) ?? cut ?? proj;
            if (cutFg != null && solidFill != null)
            {
                ogs.SetCutForegroundPatternId(solidFill);
                ogs.SetCutForegroundPatternColor(cutFg);
            }
            if (ov.Transparency.HasValue && ov.Transparency.Value >= 0 && ov.Transparency.Value <= 100)
                ogs.SetSurfaceTransparency(ov.Transparency.Value);
            if (ov.Halftone.HasValue) ogs.SetHalftone(ov.Halftone.Value);
            return ogs;
        }

        private static Color ParseHex(string hex)
        {
            if (string.IsNullOrWhiteSpace(hex)) return null;
            hex = hex.Trim().TrimStart('#');
            if (hex.Length != 6) return null;
            try
            {
                byte r = byte.Parse(hex.Substring(0, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                byte g = byte.Parse(hex.Substring(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                byte b = byte.Parse(hex.Substring(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                return new Color(r, g, b);
            }
            catch { return null; }
        }
    }
}
