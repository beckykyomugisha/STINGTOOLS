// ══════════════════════════════════════════════════════════════════════════
//  BOQTemplateLibraryExtensions.cs — Phase 4 of the BOQ & Cost Manager.
//  Additive extensions to StingTools.Temp.BOQTemplateLibrary. Never mutates
//  the existing class — placed in its own file to keep the library stable.
//
//  Adds:
//    (1) SelectBestTemplate(list, category, element)  — element-aware variant scoring
//    (2) ResolveForElement(template, element, doc)    — full token substitution
//    (3) BuildResolvabilityReport(doc)                — project-wide audit
//  Plus BOQNarrativeEnhancer: MEP / structural context appenders and
//  diff annotations for the snapshot-comparison report.
// ══════════════════════════════════════════════════════════════════════════
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Mechanical;
using Autodesk.Revit.DB.Plumbing;
using StingTools.Core;
using StingTools.Temp;

namespace StingTools.BOQ
{
    internal static class BOQTemplateLibraryExtensions
    {
        private static readonly Regex _tokenRx = new Regex(@"\[([a-zA-Z0-9_]+)\]", RegexOptions.Compiled);

        // ── (1) Element-aware template selection ──────────────────────────

        /// <summary>
        /// Scoring rules (additive — highest score wins):
        ///   +1  category matches template.Category (base requirement)
        ///   +2  disc code matches template.DiscContains
        ///   +3  family name contains template.FamilyContains
        ///   +2  type name contains template.TypeContains
        ///   +1  MEP system name contains template.SystemContains
        ///   +2  higher Version beats competing same-score template
        /// Tie-break: project source beats company beats builtin.
        /// </summary>
        internal static BOQTemplate SelectBestTemplate(List<BOQTemplate> all, string category, Element el = null)
        {
            if (all == null || all.Count == 0 || string.IsNullOrEmpty(category)) return null;

            string famName = GetFamilyName(el).ToLowerInvariant();
            string typeName = (el?.Name ?? "").ToLowerInvariant();
            string disc = ParameterHelpers.GetString(el, ParamRegistry.DISC) ?? "";
            string sys = GetSystemName(el).ToLowerInvariant();

            int BestScore = int.MinValue;
            BOQTemplate best = null;
            foreach (var t in all)
            {
                if (string.IsNullOrEmpty(t.Category)) continue;
                string tcat = t.Category.ToLowerInvariant();
                string ccat = category.ToLowerInvariant();
                // Loose category match — either direction
                if (!tcat.Equals(ccat) && !tcat.Contains(ccat) && !ccat.Contains(tcat)) continue;

                int score = 1;
                if (!string.IsNullOrEmpty(t.DiscContains) && disc.Equals(t.DiscContains, StringComparison.OrdinalIgnoreCase)) score += 2;
                if (!string.IsNullOrEmpty(t.FamilyContains) && famName.Contains(t.FamilyContains.ToLowerInvariant())) score += 3;
                if (!string.IsNullOrEmpty(t.TypeContains) && typeName.Contains(t.TypeContains.ToLowerInvariant())) score += 2;
                if (!string.IsNullOrEmpty(t.SystemContains) && sys.Contains(t.SystemContains.ToLowerInvariant())) score += 1;
                if (t.Version > 0) score += Math.Min(2, t.Version);

                // Source priority as tie-break — project > company > builtin
                int sourceRank = t.Source == "project" ? 3 : t.Source == "company" ? 2 : 1;

                if (score > BestScore
                    || (score == BestScore && (best == null || sourceRank > SourceRank(best))))
                {
                    BestScore = score;
                    best = t;
                }
            }
            return best;
        }

        private static int SourceRank(BOQTemplate t)
            => t?.Source == "project" ? 3 : t?.Source == "company" ? 2 : 1;

        // ── (2) Full token resolution for a specific element ──────────────

        /// <summary>
        /// Resolves every [token] in template.Paragraph against the element's
        /// parameters and Revit geometry. Unknown/blank tokens are stripped
        /// rather than left literal. Runs the standard tidy pass (comma
        /// collapse, double spaces, trailing period). Never throws — on any
        /// failure returns the template paragraph with tokens stripped so a
        /// BOQ row is never left completely empty.
        /// </summary>
        internal static string ResolveForElement(BOQTemplate template, Element el, Document doc)
        {
            if (template == null || string.IsNullOrEmpty(template.Paragraph)) return "";
            try
            {
                string s = _tokenRx.Replace(template.Paragraph, m =>
                {
                    string token = m.Groups[1].Value;
                    string v = ResolveToken(token, el, doc);
                    return string.IsNullOrEmpty(v) ? "" : v;
                });
                s = TidyParagraph(s);
                return s;
            }
            catch (Exception ex)
            {
                StingLog.Warn($"ResolveForElement({template.Category}): {ex.Message}");
                string stripped = _tokenRx.Replace(template.Paragraph, "");
                return TidyParagraph(stripped);
            }
        }

        private static string ResolveToken(string token, Element el, Document doc)
        {
            if (el == null || string.IsNullOrEmpty(token)) return "";
            // (1) Direct shared-parameter lookup by exact name
            string sp = ParameterHelpers.GetString(el, token);
            if (!string.IsNullOrEmpty(sp)) return sp;

            // (2) Built-in parameter + derived-value mapping
            switch (token.ToLowerInvariant())
            {
                case "material": return GetMaterialName(el);
                case "element_type":
                case "type": return el.Name ?? "";
                case "family": return GetFamilyName(el);
                case "location": return GetLocationValue(el, doc);
                case "level": return GetLevelName(doc, el);
                case "length": return FormatMm(GetLengthFt(el) * 304.8);
                case "width":
                case "size": return GetFamilyParamNumber(el, "Width", "FAMILY_WIDTH_PARAM");
                case "height":
                case "sill_height": return GetFamilyParamNumber(el, "Height", "FAMILY_HEIGHT_PARAM");
                case "thickness": return GetFamilyParamNumber(el, "Thickness", "CWP_THICKNESS");
                case "depth": return GetFamilyParamNumber(el, "Depth", "STRUCTURAL_BEAM_CUT_LENGTH");
                case "diameter":
                case "nominal_size": return GetPipeDiameter(el);
                case "dimensions": return GetDuctDimension(el);
                case "manufacturer":
                case "manufacturer_ref": return GetBuiltIn(el, BuiltInParameter.ALL_MODEL_MANUFACTURER);
                case "model":
                case "model_ref": return Fallback(GetBuiltIn(el, BuiltInParameter.ALL_MODEL_MODEL), el.Name);
                case "fire_rating": return GetBuiltIn(el, BuiltInParameter.DOOR_FIRE_RATING) ?? GetFamilyParamString(el, "Fire Rating");
                case "finish": return Fallback(ParameterHelpers.GetString(el, "ASS_FINISH_TXT"), GetFamilyParamString(el, "Finish"));
                case "insulation": return Fallback(GetFamilyParamString(el, "Insulation"), "as specification");
                case "substrate": return GetCompoundInnerMaterial(el);
                case "spacing": return GetFamilyParamNumber(el, "Spacing", null);
                case "standard": return DefaultStandardForDiscipline(el);
                case "concrete_spec": return Fallback(GetFamilyParamString(el, "Concrete Grade"), "C25/30");
                case "reinforcement": return Fallback(GetFamilyParamString(el, "Reinforcement"), "as structural drawings");
                case "section_size": return Fallback(GetFamilyParamString(el, "Section Size"),
                                         GetBuiltIn(el, BuiltInParameter.STRUCTURAL_SECTION_SHAPE));
                case "airflow": return GetDuctAirflow(el);
                case "rating":
                case "voltage": return GetElectricalRating(el);
                case "phases": return Fallback(GetFamilyParamString(el, "Number of Phases"), "");
                case "fixture_type":
                case "equipment_type":
                case "furniture_type":
                case "casework_type":
                case "terminal_type":
                case "door_type":
                case "window_type":
                case "foundation_type": return GetFamilyName(el);
                case "capacity": return Fallback(GetFamilyParamString(el, "Capacity"), GetFamilyParamString(el, "Flow"));
                case "wattage": return Fallback(GetFamilyParamString(el, "Wattage"), GetBuiltIn(el, BuiltInParameter.RBS_ELEC_APPARENT_LOAD));
                case "lux_target": return Fallback(GetFamilyParamString(el, "Lux Level"), "300 lux");
                case "service": return Fallback(GetSystemName(el), "");
                case "description":
                case "notes": return ParameterHelpers.GetString(el, "ASS_DESCRIPTION_TXT");
            }

            // (3) Last-chance lookup for shared params whose name == token (case-insensitive)
            try
            {
                var plist = el.GetOrderedParameters();
                foreach (Parameter p in plist)
                {
                    if (string.Equals(p.Definition?.Name, token, StringComparison.OrdinalIgnoreCase)
                        && p.HasValue && p.StorageType == StorageType.String)
                        return p.AsString() ?? "";
                }
            }
            catch (Exception ex) { StingLog.Warn($"ResolveToken fallback: {ex.Message}"); }
            return "";
        }

        private static string TidyParagraph(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            s = Regex.Replace(s, @"\s+,", ",");
            s = Regex.Replace(s, @",\s*,", ",");
            s = Regex.Replace(s, @"\s{2,}", " ");
            s = s.Trim();
            if (s.Length > 0 && !s.EndsWith(".") && !s.EndsWith(";") && !s.EndsWith(":")) s += ".";
            return s;
        }

        private static string Fallback(string a, string b) => !string.IsNullOrEmpty(a) ? a : (b ?? "");

        // ── Revit helpers used by ResolveToken ────────────────────────────

        private static string GetFamilyName(Element el)
        {
            try
            {
                if (el is FamilyInstance fi) return fi.Symbol?.Family?.Name ?? "";
                var tid = el.GetTypeId();
                if (tid != null && tid.Value > 0)
                {
                    Element t = el.Document.GetElement(tid);
                    if (t is FamilySymbol fs) return fs.Family?.Name ?? "";
                    if (t != null) return t.Name ?? "";
                }
            }
            catch (Exception ex) { StingLog.Warn($"GetFamilyName: {ex.Message}"); }
            return "";
        }

        private static string GetMaterialName(Element el)
        {
            try
            {
                string matName = GetBuiltIn(el, BuiltInParameter.ALL_MODEL_MATERIAL_ASSET_NAME);
                if (!string.IsNullOrEmpty(matName)) return matName;
                var ids = el.GetMaterialIds(false);
                if (ids != null && ids.Count > 0)
                {
                    Material m = el.Document.GetElement(ids.First()) as Material;
                    if (m != null) return m.Name ?? "";
                }
            }
            catch (Exception ex) { StingLog.Warn($"GetMaterialName: {ex.Message}"); }
            return "";
        }

        private static string GetLocationValue(Element el, Document doc)
        {
            string loc = ParameterHelpers.GetString(el, "ASS_LOC_TXT");
            if (!string.IsNullOrEmpty(loc)) return loc;
            try
            {
                var room = ParameterHelpers.GetRoomAtElement(doc, el);
                if (room != null) return room.Name ?? "";
            }
            catch (Exception ex) { StingLog.Warn($"GetLocationValue: {ex.Message}"); }
            return "";
        }

        private static string GetLevelName(Document doc, Element el)
        {
            try
            {
                Parameter lp = el.get_Parameter(BuiltInParameter.SCHEDULE_LEVEL_PARAM);
                if (lp != null && lp.HasValue) return lp.AsValueString() ?? "";
                if (el.LevelId != null && el.LevelId.Value > 0)
                {
                    Element lv = doc.GetElement(el.LevelId);
                    if (lv != null) return lv.Name;
                }
            }
            catch (Exception ex) { StingLog.Warn($"GetLevelName: {ex.Message}"); }
            return "";
        }

        private static double GetLengthFt(Element el)
        {
            try
            {
                if (el.Location is LocationCurve lc) return lc.Curve.Length;
                Parameter p = el.LookupParameter("Length") ?? el.get_Parameter(BuiltInParameter.CURVE_ELEM_LENGTH);
                if (p != null && p.HasValue) return p.AsDouble();
            }
            catch (Exception ex) { StingLog.Warn($"GetLengthFt: {ex.Message}"); }
            return 0;
        }

        private static string FormatMm(double mm)
        {
            if (mm <= 0) return "";
            if (mm >= 1000) return $"{mm / 1000.0:F1}m";
            return $"{Math.Round(mm, 0):N0}mm";
        }

        private static string GetFamilyParamNumber(Element el, string paramName, string bipName)
        {
            try
            {
                Parameter p = !string.IsNullOrEmpty(paramName) ? el.LookupParameter(paramName) : null;
                if (p == null && !string.IsNullOrEmpty(bipName))
                {
                    if (Enum.TryParse(bipName, out BuiltInParameter bip))
                        p = el.get_Parameter(bip);
                }
                if (p != null && p.HasValue)
                {
                    if (p.StorageType == StorageType.Double)
                    {
                        double mm = p.AsDouble() * 304.8; // Revit internal feet → mm
                        return FormatMm(mm);
                    }
                    return p.AsString() ?? "";
                }
            }
            catch (Exception ex) { StingLog.Warn($"GetFamilyParamNumber({paramName}): {ex.Message}"); }
            return "";
        }

        private static string GetFamilyParamString(Element el, string paramName)
        {
            try
            {
                Parameter p = el.LookupParameter(paramName);
                if (p != null && p.HasValue)
                {
                    if (p.StorageType == StorageType.String) return p.AsString() ?? "";
                    return p.AsValueString() ?? "";
                }
            }
            catch (Exception ex) { StingLog.Warn($"GetFamilyParamString({paramName}): {ex.Message}"); }
            return "";
        }

        private static string GetBuiltIn(Element el, BuiltInParameter bip)
        {
            try
            {
                Parameter p = el.get_Parameter(bip);
                if (p != null && p.HasValue)
                {
                    if (p.StorageType == StorageType.String) return p.AsString() ?? "";
                    return p.AsValueString() ?? "";
                }
            }
            catch (Exception ex) { StingLog.Warn($"GetBuiltIn({bip}): {ex.Message}"); }
            return "";
        }

        private static string GetPipeDiameter(Element el)
        {
            try
            {
                Parameter p = el.get_Parameter(BuiltInParameter.RBS_PIPE_DIAMETER_PARAM);
                if (p == null) p = el.LookupParameter("Diameter");
                if (p != null && p.HasValue) return FormatMm(p.AsDouble() * 304.8);
            }
            catch (Exception ex) { StingLog.Warn($"GetPipeDiameter: {ex.Message}"); }
            return "";
        }

        private static string GetDuctDimension(Element el)
        {
            try
            {
                Parameter w = el.get_Parameter(BuiltInParameter.RBS_CURVE_WIDTH_PARAM);
                Parameter h = el.get_Parameter(BuiltInParameter.RBS_CURVE_HEIGHT_PARAM);
                if (w != null && h != null && w.HasValue && h.HasValue)
                    return $"{FormatMm(w.AsDouble() * 304.8)} × {FormatMm(h.AsDouble() * 304.8)}";
                Parameter d = el.get_Parameter(BuiltInParameter.RBS_CURVE_DIAMETER_PARAM);
                if (d != null && d.HasValue) return FormatMm(d.AsDouble() * 304.8) + " Ø";
            }
            catch (Exception ex) { StingLog.Warn($"GetDuctDimension: {ex.Message}"); }
            return "";
        }

        private static string GetDuctAirflow(Element el)
        {
            try
            {
                Parameter p = el.get_Parameter(BuiltInParameter.RBS_DUCT_FLOW_PARAM);
                if (p != null && p.HasValue)
                {
                    double ls = p.AsDouble(); // CFM internal → need l/s; Revit stores ft³/s
                    return $"{Math.Round(ls, 0):N0} l/s";
                }
            }
            catch (Exception ex) { StingLog.Warn($"GetDuctAirflow: {ex.Message}"); }
            return "";
        }

        private static string GetElectricalRating(Element el)
        {
            try
            {
                Parameter p = el.get_Parameter(BuiltInParameter.RBS_ELEC_CIRCUIT_RATING_PARAM);
                if (p != null && p.HasValue) return $"{Math.Round(p.AsDouble(), 0):N0} A";
                Parameter v = el.LookupParameter("Voltage");
                if (v != null && v.HasValue) return v.AsValueString() ?? v.AsString();
            }
            catch (Exception ex) { StingLog.Warn($"GetElectricalRating: {ex.Message}"); }
            return "";
        }

        private static string GetSystemName(Element el)
        {
            if (el == null) return "";
            try
            {
                if (el is MEPCurve mc && mc.MEPSystem != null) return mc.MEPSystem.Name ?? "";
                Parameter p = el.get_Parameter(BuiltInParameter.RBS_SYSTEM_NAME_PARAM);
                if (p != null && p.HasValue) return p.AsString() ?? "";
            }
            catch (Exception ex) { StingLog.Warn($"GetSystemName: {ex.Message}"); }
            return "";
        }

        private static string DefaultStandardForDiscipline(Element el)
        {
            string disc = ParameterHelpers.GetString(el, ParamRegistry.DISC) ?? "";
            switch (disc)
            {
                case "A": return "BS EN 1996 workmanship";
                case "S": return "BS EN 1992";
                case "M": return "CIBSE Guide B";
                case "E": return "BS 7671:2018+A1:2020";
                case "P": return "BS EN 1057";
                case "FP": return "BS EN 12845";
                default: return "as specification";
            }
        }

        private static string GetCompoundInnerMaterial(Element el)
        {
            try
            {
                // Walls/Floors/Roofs have CompoundStructure — return the inner (room-side) layer material.
                if (el is HostObject ho)
                {
                    var cs = ho.Document.GetElement(ho.GetTypeId()) as HostObjAttributes;
                    var struct_ = cs?.GetCompoundStructure();
                    if (struct_ != null)
                    {
                        var layers = struct_.GetLayers();
                        if (layers.Count > 0)
                        {
                            Material m = ho.Document.GetElement(layers.Last().MaterialId) as Material;
                            if (m != null) return m.Name;
                        }
                    }
                }
            }
            catch (Exception ex) { StingLog.Warn($"GetCompoundInnerMaterial: {ex.Message}"); }
            return "";
        }

        // ── (3) Project-wide resolvability audit ──────────────────────────

        /// <summary>
        /// Runs the template catalogue against the current project and tallies
        /// how many elements fully resolve their NRM2 paragraph. Delegates
        /// element enumeration + category/token lookups back into this file so
        /// BOQTemplateLibrary (Temp namespace) doesn't need to know about
        /// ParameterHelpers or the Revit API surface.
        /// </summary>
        internal static BOQTemplateLibrary.ResolvabilityReport BuildResolvabilityReport(Document doc)
        {
            if (doc == null) return new BOQTemplateLibrary.ResolvabilityReport();
            var templates = BOQTemplateLibrary.LoadAll(doc, StingToolsApp.DataPath);

            var knownCats = new HashSet<string>(TagConfig.DiscMap.Keys, StringComparer.OrdinalIgnoreCase);
            var report = BOQTemplateLibrary.AuditResolvability(
                doc, templates,
                d =>
                {
                    var list = new List<Element>();
                    var coll = new FilteredElementCollector(d).WhereElementIsNotElementType();
                    var enums = SharedParamGuids.AllCategoryEnums;
                    if (enums != null && enums.Length > 0)
                        coll = coll.WherePasses(new ElementMulticategoryFilter(new List<BuiltInCategory>(enums)));
                    foreach (Element e in coll)
                    {
                        string c = ParameterHelpers.GetCategoryName(e);
                        if (!string.IsNullOrEmpty(c) && knownCats.Contains(c)) list.Add(e);
                    }
                    return list;
                },
                e => ParameterHelpers.GetCategoryName(e),
                (e, tok) => ResolveToken(tok, e, doc));
            return report;
        }
    }

    // ══════════════════════════════════════════════════════════════════════
    //  BOQNarrativeEnhancer — post-processing of resolved paragraphs.
    // ══════════════════════════════════════════════════════════════════════

    internal static class BOQNarrativeEnhancer
    {
        /// <summary>
        /// Append an MEP context clause (system name, served area, capacity)
        /// to the resolved paragraph when the element is a duct/pipe/MEP
        /// fixture and the base paragraph does not already name the system.
        /// </summary>
        internal static void EnhanceWithMEPContext(BOQLineItem item, Element el, Document doc)
        {
            if (item == null || el == null) return;
            string cat = item.Category ?? "";
            bool isMep = cat.Contains("Duct") || cat.Contains("Pipe") || cat.Contains("Electrical")
                || cat.Contains("Lighting") || cat.Contains("Mechanical") || cat.Contains("Plumbing");
            if (!isMep) return;

            string system = "";
            try
            {
                if (el is MEPCurve mc && mc.MEPSystem != null) system = mc.MEPSystem.Name ?? "";
                if (string.IsNullOrEmpty(system))
                {
                    Parameter p = el.get_Parameter(BuiltInParameter.RBS_SYSTEM_NAME_PARAM);
                    if (p != null && p.HasValue) system = p.AsString() ?? "";
                }
            }
            catch (Exception ex) { StingLog.Warn($"EnhanceWithMEPContext: {ex.Message}"); }
            if (string.IsNullOrEmpty(system)) return;

            string para = item.ResolvedNRM2Paragraph ?? "";
            if (para.IndexOf(system, StringComparison.OrdinalIgnoreCase) >= 0) return;

            string loc = item.Location ?? "";
            string suffix = $" Serving {system}";
            if (!string.IsNullOrEmpty(loc)) suffix += $" at {loc}";
            suffix += ".";
            item.ResolvedNRM2Paragraph = para.TrimEnd('.').TrimEnd() + "; " + suffix;
        }

        /// <summary>
        /// Append a structural context clause (grid reference + level span) to
        /// the resolved paragraph when the element is structural.
        /// </summary>
        internal static void EnhanceWithStructuralContext(BOQLineItem item, Element el, Document doc)
        {
            if (item == null || el == null) return;
            string disc = item.Discipline ?? "";
            if (!disc.Equals("S", StringComparison.OrdinalIgnoreCase)) return;

            string gridRef = ParameterHelpers.GetString(el, "PRJ_GRID_REF_TXT");
            if (string.IsNullOrEmpty(gridRef)) return;

            string para = item.ResolvedNRM2Paragraph ?? "";
            if (para.IndexOf(gridRef, StringComparison.OrdinalIgnoreCase) >= 0) return;
            item.ResolvedNRM2Paragraph = para.TrimEnd('.').TrimEnd() + $"; at grid reference {gridRef}.";
        }

        /// <summary>
        /// Builds the italic annotation sentence surfaced in the snapshot
        /// comparison report next to each changed category.
        /// </summary>
        internal static string GenerateDiffAnnotation(CategoryDiff diff)
        {
            if (diff == null) return "";
            switch (diff.ChangeType)
            {
                case BOQChangeType.RateRevised:
                    return $"Rate revised {diff.Name} UGX {diff.RateA:N0} → {diff.RateB:N0}/unit.";
                case BOQChangeType.QtyChanged:
                    string direction = diff.QtyB > diff.QtyA ? "increased" : "reduced";
                    return $"{diff.Name} {direction} {diff.QtyA:N1} → {diff.QtyB:N1}.";
                case BOQChangeType.NewItem:
                    return $"Newly modeled — {diff.QtyB:N0} units recorded.";
                case BOQChangeType.ItemRemoved:
                    return "Removed from the model since the earlier snapshot.";
                case BOQChangeType.PSAdded:
                    return "PC sum registered.";
                case BOQChangeType.SourcePromoted:
                    return "Promoted from manual row to modeled element.";
                default:
                    return "";
            }
        }

    }
}
