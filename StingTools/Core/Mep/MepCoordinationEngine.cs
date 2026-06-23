// StingTools — MEP Coordination Engine (Phase C + Phase D).
//
// Closes the create-systems → coordinated-drawing loop. Reads the MEP systems
// present in the model and resolves a colour filter for each by priority:
//
//   1. Abbreviation-keyed STING filter (Phase D) — keys on ASS_MEP_SYS_NAME_TXT
//      begins-with "<abbr>-" (stamped by Phase B). DISTINGUISHES services that
//      share one MEPSystemClassification (CHWF / LTHWF / CWF are all SupplyHydronic).
//   2. Corporate classification filter (Phase C) — a STING_AEC_FILTERS.json entry
//      keyed on RBS_SYSTEM_CLASSIFICATION_PARAM equals "<display>".
//   3. Auto-authored classification filter (Phase D) — synthesised from the matching
//      Phase A def's colour so a present system ALWAYS colours, even with no curated
//      filter.
//
// One source of truth throughout: Phase A colour on the type → Phase B classification
// + name on the element → Phase D filter override on the view.
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
    /// <summary>Restrict coordination colouring to one MEP domain (for per-discipline views).</summary>
    public enum MepDomain { All, Duct, Pipe }

    /// <summary>A distinct MEP system present in the model.</summary>
    public sealed class PresentSystem
    {
        public string DisplayClassification { get; set; } = ""; // "Supply Air"
        public string EnumClassification { get; set; } = "";     // "SupplyAir"
        public string Abbreviation { get; set; } = "";           // "CHWF" (from ASS_MEP_SYS_NAME_TXT prefix)
        public bool IsDuct { get; set; }
        public string Key => string.IsNullOrEmpty(Abbreviation) ? "cls:" + DisplayClassification : "abbr:" + Abbreviation;
    }

    public sealed class MepCoordRow
    {
        public string Classification { get; set; } = "";
        public string Abbreviation { get; set; } = "";
        public string FilterName { get; set; } = "";
        public string Source { get; set; } = "";   // abbreviation | classification | synthesised | none
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

        // ── model read ───────────────────────────────────────────────────────

        /// <summary>Distinct system classification display-strings present (back-compat).</summary>
        public static List<string> PresentClassifications(Document doc)
            => PresentSystems(doc).Select(s => s.DisplayClassification)
                                  .Where(s => !string.IsNullOrEmpty(s))
                                  .Distinct(StringComparer.OrdinalIgnoreCase)
                                  .OrderBy(s => s).ToList();

        /// <summary>
        /// Distinct MEP systems present on duct + pipe members, each carrying its
        /// display classification, enum classification, and STING abbreviation
        /// (the ASS_MEP_SYS_NAME_TXT prefix Phase B stamped).
        /// </summary>
        public static List<PresentSystem> PresentSystems(Document doc)
        {
            var byKey = new Dictionary<string, PresentSystem>(StringComparer.OrdinalIgnoreCase);
            if (doc == null) return new List<PresentSystem>();

            // Pre-build a system-type-id → classification map once (PERF — avoids a
            // doc.GetElement(typeId) per duct/pipe on large models). MEPSystemType is
            // abstract, so collect the two concrete subclasses.
            var clsByType = new Dictionary<ElementId, string>();
            foreach (var t in new FilteredElementCollector(doc).OfClass(typeof(Autodesk.Revit.DB.Mechanical.MechanicalSystemType))
                         .Cast<MEPSystemType>())
                try { clsByType[t.Id] = t.SystemClassification.ToString(); } catch { }
            foreach (var t in new FilteredElementCollector(doc).OfClass(typeof(Autodesk.Revit.DB.Plumbing.PipingSystemType))
                         .Cast<MEPSystemType>())
                try { clsByType[t.Id] = t.SystemClassification.ToString(); } catch { }

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
                    string display = el.get_Parameter(BuiltInParameter.RBS_SYSTEM_CLASSIFICATION_PARAM)?.AsValueString()?.Trim() ?? "";
                    string abbr = AbbrevFromSysName(el.LookupParameter(MepSystemFilterGenerator.SysNameParam)?.AsString());
                    string enumCls = EnumClassification(el, clsByType);
                    if (string.IsNullOrEmpty(display) && string.IsNullOrEmpty(abbr)) continue;

                    var ps = new PresentSystem
                    {
                        DisplayClassification = display,
                        EnumClassification = enumCls,
                        Abbreviation = abbr,
                        IsDuct = el is Autodesk.Revit.DB.Mechanical.Duct
                    };
                    if (!byKey.ContainsKey(ps.Key)) byKey[ps.Key] = ps;
                    else
                    {
                        // Backfill missing fields from later members.
                        var ex = byKey[ps.Key];
                        if (string.IsNullOrEmpty(ex.DisplayClassification)) ex.DisplayClassification = display;
                        if (string.IsNullOrEmpty(ex.EnumClassification)) ex.EnumClassification = enumCls;
                    }
                }
                catch { }
            }
            return byKey.Values.OrderBy(s => s.IsDuct ? 0 : 1).ThenBy(s => s.DisplayClassification).ToList();
        }

        // ── planning (shared by Apply + Inspect for consistency) ─────────────

        /// <summary>
        /// Resolve, without writing, the filter each present system would get and
        /// from which source. No transaction needed.
        /// </summary>
        public static List<MepCoordRow> BuildPlan(Document doc)
        {
            var rows = new List<MepCoordRow>();
            if (doc == null) return rows;
            var rules = MepSystemTypeRegistry.Get(doc);
            var corporate = AecFilterRegistry.ListAll(doc);
            foreach (var sys in PresentSystems(doc))
            {
                var def = ResolveDef(rules, corporate, sys, out string source);
                rows.Add(new MepCoordRow
                {
                    Classification = sys.DisplayClassification,
                    Abbreviation = sys.Abbreviation,
                    FilterName = def?.Name ?? "",
                    Source = source,
                    Note = def == null ? "no filter & no Phase A colour for this system" : ""
                });
            }
            return rows;
        }

        // ── apply ────────────────────────────────────────────────────────────

        /// <summary>Apply the resolved colour filters to <paramref name="view"/>. Requires an open transaction.</summary>
        public static MepCoordResult ApplyToView(Document doc, View view, MepDomain domain = MepDomain.All)
        {
            var result = new MepCoordResult();
            if (doc == null || view == null) { result.Warnings.Add("No document / view."); return result; }
            if (!CanOverride(view))
            {
                result.Warnings.Add($"View '{view.Name}' does not allow graphic overrides (schedule / sheet?).");
                return result;
            }

            var systems = PresentSystems(doc);
            if (domain == MepDomain.Duct) systems = systems.Where(s => s.IsDuct).ToList();
            else if (domain == MepDomain.Pipe) systems = systems.Where(s => !s.IsDuct).ToList();
            if (systems.Count == 0)
            {
                result.Warnings.Add("No duct/pipe members carry a system yet — run MEP_BuildSystems (Phase B) first.");
                return result;
            }

            var rules = MepSystemTypeRegistry.Get(doc);
            var corporate = AecFilterRegistry.ListAll(doc);
            var solid = ParameterHelpers.GetSolidFillPattern(doc)?.Id;
            var existing = new HashSet<ElementId>(view.GetFilters());

            foreach (var sys in systems)
            {
                var row = new MepCoordRow { Classification = sys.DisplayClassification, Abbreviation = sys.Abbreviation };
                result.Rows.Add(row);

                var def = ResolveDef(rules, corporate, sys, out string source);
                row.Source = source;
                if (def == null) { row.Note = "no filter & no Phase A colour for this system"; continue; }
                row.FilterName = def.Name;

                FilterFactoryResult fr;
                try { fr = AecFilterFactory.FindOrCreate(doc, def); }
                catch (Exception ex) { row.Note = $"filter create: {ex.Message}"; result.Warnings.Add($"{def.Name}: {ex.Message}"); continue; }
                if (fr == null || !fr.Ok) { row.Note = fr?.Error ?? "filter not created"; continue; }

                try
                {
                    if (!existing.Contains(fr.Filter.Id)) { view.AddFilter(fr.Filter.Id); existing.Add(fr.Filter.Id); }
                    view.SetFilterOverrides(fr.Filter.Id, BuildOverride(def.DefaultOverride, solid));
                    view.SetFilterVisibility(fr.Filter.Id, true);
                    row.Applied = true;
                    row.Note = $"{source}: {(fr.Created ? "created + applied" : "applied")}";
                }
                catch (Exception ex)
                {
                    row.Note = $"apply: {ex.Message}";
                    result.Warnings.Add($"{def.Name}: apply: {ex.Message}");
                }
            }
            return result;
        }

        // ── resolution (the Phase D priority chain) ──────────────────────────

        private static AecFilterDefinition ResolveDef(
            MepSystemTypeRules rules, IReadOnlyList<AecFilterDefinition> corporate,
            PresentSystem sys, out string source)
        {
            // 1. Abbreviation-keyed STING filter — distinguishes same-classification services.
            if (!string.IsNullOrEmpty(sys.Abbreviation))
            {
                var d = rules?.Enabled.FirstOrDefault(x =>
                    string.Equals(x.Abbreviation, sys.Abbreviation, StringComparison.OrdinalIgnoreCase)
                    && x.LineColor != null);
                if (d != null) { source = "abbreviation"; return MepSystemFilterGenerator.AbbreviationFilter(d); }
            }

            // 2. Corporate classification filter from STING_AEC_FILTERS.json.
            if (!string.IsNullOrEmpty(sys.DisplayClassification))
            {
                var c = corporate?.FirstOrDefault(f => RuleMatchesClassification(f?.Rule, sys.DisplayClassification));
                if (c != null) { source = "classification"; return c; }
            }

            // 3. Auto-author from the matching Phase A def colour.
            var synth = MepSystemFilterGenerator.SynthesiseForClassification(
                rules, sys.EnumClassification, sys.DisplayClassification);
            if (synth != null) { source = "synthesised"; return synth; }

            source = "none";
            return null;
        }

        // ── helpers ──────────────────────────────────────────────────────────

        private static string AbbrevFromSysName(string sysName)
        {
            if (string.IsNullOrWhiteSpace(sysName)) return "";
            int dash = sysName.IndexOf('-');
            return dash > 0 ? sysName.Substring(0, dash).Trim() : "";
        }

        private static string EnumClassification(Element el, Dictionary<ElementId, string> clsByType)
        {
            try
            {
                var sys = (el as MEPCurve)?.MEPSystem;
                if (sys == null) return "";
                return clsByType.TryGetValue(sys.GetTypeId(), out var cls) ? cls : "";
            }
            catch { return ""; }
        }

        private static bool CanOverride(View view)
        {
            try { return view != null && !view.IsTemplate && view.AreGraphicsOverridesAllowed(); }
            catch { return false; }
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
