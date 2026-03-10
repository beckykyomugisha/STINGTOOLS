using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Core;

namespace StingTools.Temp
{
    // ═══════════════════════════════════════════════════════════════════════
    //  TemplateManager — deep intelligence engine for view template automation
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Centralised intelligence engine providing:
    ///   • Pattern-based auto-assignment with level/phase/scope-box awareness
    ///   • Template discovery, view classification, and discipline inference
    ///   • Compliance scoring (weighted 10-point scale)
    ///   • VG snapshot comparison (diff engine)
    ///   • Auto-repair logic for template health
    ///   • Style definition tables (fills, lines, text, dims, objects)
    ///   • Data-driven parameter binding support
    /// </summary>
    internal static class TemplateManager
    {
        // ── Auto-Assignment Rules ─────────────────────────────────────────
        // Evaluated in order — first match wins. More specific patterns first.

        public static readonly (string pattern, string templateName, ViewType viewType)[] AssignmentRules =
        {
            // Floor plans — discipline-specific
            ("Mechanical", "STING - Mechanical Plan", ViewType.FloorPlan),
            ("HVAC", "STING - Mechanical Plan", ViewType.FloorPlan),
            ("Electrical", "STING - Electrical Plan", ViewType.FloorPlan),
            ("Power", "STING - Electrical Plan", ViewType.FloorPlan),
            ("Lighting Plan", "STING - Electrical Plan", ViewType.FloorPlan),
            ("Plumbing", "STING - Plumbing Plan", ViewType.FloorPlan),
            ("Hydraulic", "STING - Plumbing Plan", ViewType.FloorPlan),
            ("Drainage", "STING - Plumbing Plan", ViewType.FloorPlan),
            ("Architectural", "STING - Architectural Plan", ViewType.FloorPlan),
            ("Interior", "STING - Architectural Plan", ViewType.FloorPlan),
            ("Structural", "STING - Structural Plan", ViewType.FloorPlan),
            ("Fire", "STING - Fire Protection Plan", ViewType.FloorPlan),
            ("Sprinkler", "STING - Fire Protection Plan", ViewType.FloorPlan),
            ("Low Voltage", "STING - Low Voltage Plan", ViewType.FloorPlan),
            ("Communication", "STING - Low Voltage Plan", ViewType.FloorPlan),
            ("Security", "STING - Low Voltage Plan", ViewType.FloorPlan),
            ("Data", "STING - Low Voltage Plan", ViewType.FloorPlan),
            ("Coordination", "STING - MEP Coordination", ViewType.FloorPlan),
            ("Combined", "STING - Combined Services", ViewType.FloorPlan),
            ("Demolition", "STING - Demolition Plan", ViewType.FloorPlan),
            ("As-Built", "STING - As-Built Plan", ViewType.FloorPlan),
            ("Existing", "STING - As-Built Plan", ViewType.FloorPlan),
            // RCPs
            ("Lighting", "STING - Lighting RCP", ViewType.CeilingPlan),
            ("Ceiling", "STING - Ceiling RCP", ViewType.CeilingPlan),
            // Sections
            ("Presentation", "STING - Presentation Section", ViewType.Section),
            ("Detail", "STING - Detail Section", ViewType.Section),
            // 3D views
            ("Coordination", "STING - Coordination 3D", ViewType.ThreeD),
            ("Presentation", "STING - Presentation 3D", ViewType.ThreeD),
            // Elevations
            ("Presentation", "STING - Presentation Elevation", ViewType.Elevation),
        };

        /// <summary>Default template per view type when no name pattern matches.</summary>
        private static readonly Dictionary<ViewType, string> DefaultTemplates =
            new Dictionary<ViewType, string>
            {
                { ViewType.FloorPlan, "STING - Architectural Plan" },
                { ViewType.CeilingPlan, "STING - Ceiling RCP" },
                { ViewType.Section, "STING - Working Section" },
                { ViewType.ThreeD, "STING - Coordination 3D" },
                { ViewType.Elevation, "STING - Working Elevation" },
            };

        /// <summary>
        /// Level-aware template overrides. If a view's associated level
        /// name contains a keyword, prefer a specific template.
        /// </summary>
        private static readonly (string levelPattern, string templateName, ViewType viewType)[] LevelOverrides =
        {
            ("Roof", "STING - Architectural Plan", ViewType.FloorPlan),
            ("Basement", "STING - Structural Plan", ViewType.FloorPlan),
            ("Foundation", "STING - Structural Plan", ViewType.FloorPlan),
            ("Plant Room", "STING - Mechanical Plan", ViewType.FloorPlan),
            ("Switch", "STING - Electrical Plan", ViewType.FloorPlan),
            ("Riser", "STING - MEP Coordination", ViewType.FloorPlan),
        };

        /// <summary>
        /// Phase-aware template mapping. Views on certain phases
        /// get specific templates.
        /// </summary>
        private static readonly Dictionary<string, string> PhaseTemplateMap =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "Existing", "STING - As-Built Plan" },
                { "Demolition", "STING - Demolition Plan" },
                { "New Construction", "STING - Architectural Plan" },
            };

        /// <summary>
        /// Returns the best-matching STING template for a view using multi-layer intelligence:
        ///   Layer 1: Explicit name pattern matching (highest priority)
        ///   Layer 2: Level-aware inference (basement→structural, plant room→mechanical)
        ///   Layer 3: Phase-aware inference (demolition phase→demo template)
        ///   Layer 4: Scope box inference (scope box name contains discipline hint)
        ///   Layer 5: View type default (lowest priority)
        /// Returns null if no reasonable assignment can be made.
        /// </summary>
        public static string FindMatchingTemplate(View view)
        {
            if (view == null || view.IsTemplate) return null;

            string viewName = view.Name ?? "";
            ViewType vt = view.ViewType;

            // Layer 1: Explicit name pattern matching
            foreach (var (pattern, templateName, viewType) in AssignmentRules)
            {
                if (vt != viewType) continue;
                if (viewName.IndexOf(pattern, StringComparison.OrdinalIgnoreCase) >= 0)
                    return templateName;
            }

            // Layer 2: Level-aware inference
            try
            {
                if (view is ViewPlan viewPlan)
                {
                    Level level = view.Document.GetElement(viewPlan.GenLevel?.Id ?? ElementId.InvalidElementId) as Level;
                    if (level != null)
                    {
                        string levelName = level.Name ?? "";
                        foreach (var (levelPattern, templateName, viewType) in LevelOverrides)
                        {
                            if (vt != viewType) continue;
                            if (levelName.IndexOf(levelPattern, StringComparison.OrdinalIgnoreCase) >= 0)
                                return templateName;
                        }
                    }
                }
            }
            catch { /* level lookup not available */ }

            // Layer 3: Phase-aware inference
            try
            {
                Parameter phaseParam = view.get_Parameter(BuiltInParameter.VIEW_PHASE);
                if (phaseParam != null && phaseParam.HasValue)
                {
                    ElementId phaseId = phaseParam.AsElementId();
                    if (phaseId != ElementId.InvalidElementId)
                    {
                        Phase phase = view.Document.GetElement(phaseId) as Phase;
                        if (phase != null && PhaseTemplateMap.TryGetValue(phase.Name, out string phaseTmpl))
                        {
                            if (vt == ViewType.FloorPlan || vt == ViewType.CeilingPlan)
                                return phaseTmpl;
                        }
                    }
                }
            }
            catch { /* phase lookup not available */ }

            // Layer 4: Scope box inference
            try
            {
                Parameter scopeParam = view.get_Parameter(BuiltInParameter.VIEWER_VOLUME_OF_INTEREST_CROP);
                if (scopeParam != null && scopeParam.HasValue)
                {
                    ElementId scopeId = scopeParam.AsElementId();
                    if (scopeId != ElementId.InvalidElementId)
                    {
                        Element scopeBox = view.Document.GetElement(scopeId);
                        if (scopeBox != null)
                        {
                            string scopeName = scopeBox.Name ?? "";
                            foreach (var (pattern, templateName, viewType) in AssignmentRules)
                            {
                                if (vt != viewType) continue;
                                if (scopeName.IndexOf(pattern, StringComparison.OrdinalIgnoreCase) >= 0)
                                    return templateName;
                            }
                        }
                    }
                }
            }
            catch { /* scope box not available */ }

            // Layer 5: View type default
            return DefaultTemplates.TryGetValue(vt, out string def) ? def : null;
        }

        /// <summary>Collects all STING view templates indexed by name.</summary>
        public static Dictionary<string, View> GetStingTemplates(Document doc)
        {
            var result = new Dictionary<string, View>(StringComparer.OrdinalIgnoreCase);
            foreach (View v in new FilteredElementCollector(doc)
                .OfClass(typeof(View)).Cast<View>()
                .Where(v => v.IsTemplate && v.Name.StartsWith("STING")))
            {
                result[v.Name] = v;
            }
            return result;
        }

        /// <summary>Collects all non-template views that can have templates assigned.</summary>
        public static List<View> GetAssignableViews(Document doc)
        {
            return new FilteredElementCollector(doc)
                .OfClass(typeof(View)).Cast<View>()
                .Where(v => !v.IsTemplate &&
                            v.ViewType != ViewType.DrawingSheet &&
                            v.ViewType != ViewType.Schedule &&
                            v.ViewType != ViewType.Legend &&
                            v.ViewType != ViewType.Internal &&
                            v.ViewType != ViewType.Undefined &&
                            v.ViewType != ViewType.ProjectBrowser &&
                            v.ViewType != ViewType.SystemBrowser)
                .ToList();
        }

        /// <summary>Extracts the discipline code from a STING template name.</summary>
        public static string GetDisciplineFromTemplateName(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            // Per-discipline presentation accent templates (check BEFORE generic matches)
            if (name.Contains("Presentation Monochrome")) return "PRES_MONO";
            if (name.Contains("Presentation Dark")) return "PRES_DARK";
            if (name.Contains("Presentation Landscape")) return "PRES_LAND";
            if (name.Contains("Presentation Electrical")) return "PRES_E_DISC";
            if (name.Contains("Presentation Architectural")) return "PRES_A";
            if (name.Contains("Presentation Structural")) return "PRES_S";
            if (name.Contains("Presentation Plumbing")) return "PRES_P";
            if (name.Contains("Presentation MEP")) return "PRES_MEP";
            if (name.Contains("Presentation Classic")) return "PRES_C";
            if (name.Contains("Presentation Enhanced")) return "PRES_E";
            if (name.Contains("Presentation Section")) return "SEC_P";
            if (name.Contains("Presentation Elevation")) return "ELEV_P";
            if (name.Contains("Presentation 3D")) return "PRES_3D";
            // Per-discipline 3D templates
            if (name.Contains("3D Monochrome")) return "3D_MONO";
            if (name.Contains("3D Dark")) return "3D_DARK";
            if (name.Contains("3D Architectural")) return "3D_A";
            if (name.Contains("3D Structural")) return "3D_S";
            if (name.Contains("3D Electrical")) return "3D_E";
            if (name.Contains("3D Plumbing")) return "3D_P";
            if (name.Contains("Coordination 3D")) return "MEP_3D";
            // Working discipline plans
            if (name.Contains("Mechanical")) return "M";
            if (name.Contains("Electrical")) return "E";
            if (name.Contains("Plumbing")) return "P";
            if (name.Contains("Architectural")) return "A";
            if (name.Contains("Structural")) return "S";
            if (name.Contains("Fire Protection")) return "FP";
            if (name.Contains("Low Voltage")) return "LV";
            if (name.Contains("MEP Coordination")) return "MEP";
            if (name.Contains("Combined")) return "ALL";
            // Special purpose
            if (name.Contains("Lighting RCP")) return "RCP_LTG";
            if (name.Contains("Ceiling RCP")) return "RCP_CLG";
            if (name.Contains("Working Section")) return "SEC_W";
            if (name.Contains("Detail Section")) return "SEC_D";
            if (name.Contains("Working Elevation")) return "ELEV_W";
            if (name.Contains("Demolition")) return "DEMO";
            if (name.Contains("As-Built")) return "EXIST";
            if (name.Contains("Area")) return "AREA";
            return null;
        }

        // ── Compliance Scoring Engine ──────────────────────────────────

        /// <summary>
        /// Weighted scoring criteria for template compliance.
        /// Each returns 0.0-1.0 multiplied by weight to produce final score.
        /// Total weights = 10.0 → score is out of 10.
        /// </summary>
        public static readonly (string criterion, double weight, string description)[] ComplianceCriteria =
        {
            ("HasTemplate", 1.5, "View has a template assigned"),
            ("IsStingTemplate", 1.0, "Template is a STING-standard template"),
            ("HasFilters", 1.5, "Template has STING filters applied"),
            ("FilterOverrides", 1.0, "Filters have correct discipline colour overrides"),
            ("DetailLevel", 0.5, "Detail level matches template purpose"),
            ("CorrectDiscipline", 1.5, "Template discipline matches view content"),
            ("PhaseCorrect", 0.5, "Template matches view phase"),
            ("VGConsistent", 1.0, "VG settings follow STING standard"),
            ("NoOrphans", 0.5, "No broken filter references"),
            ("ScaleAppropriate", 0.5, "View scale is within standard range"),
        };

        /// <summary>
        /// Score a single view against STING compliance criteria.
        /// Returns (totalScore, maxScore, perCriterion[]).
        /// </summary>
        public static (double score, double max, (string name, double earned, double possible)[] details)
            ScoreViewCompliance(Document doc, View view)
        {
            var details = new List<(string, double, double)>();
            double totalScore = 0;
            double maxScore = 0;

            foreach (var (criterion, weight, _) in ComplianceCriteria)
            {
                maxScore += weight;
                double earned = 0;

                switch (criterion)
                {
                    case "HasTemplate":
                        earned = view.ViewTemplateId != ElementId.InvalidElementId ? weight : 0;
                        break;
                    case "IsStingTemplate":
                        if (view.ViewTemplateId != ElementId.InvalidElementId)
                        {
                            var tmpl = doc.GetElement(view.ViewTemplateId) as View;
                            earned = (tmpl != null && tmpl.Name.StartsWith("STING")) ? weight : 0;
                        }
                        break;
                    case "HasFilters":
                        if (view.ViewTemplateId != ElementId.InvalidElementId)
                        {
                            var tmpl = doc.GetElement(view.ViewTemplateId) as View;
                            if (tmpl != null)
                            {
                                int filterCount = tmpl.GetFilters().Count;
                                earned = filterCount >= 5 ? weight : weight * filterCount / 5.0;
                            }
                        }
                        break;
                    case "FilterOverrides":
                        if (view.ViewTemplateId != ElementId.InvalidElementId)
                        {
                            var tmpl = doc.GetElement(view.ViewTemplateId) as View;
                            if (tmpl != null)
                            {
                                var filters = tmpl.GetFilters();
                                int overridden = 0;
                                foreach (ElementId fid in filters)
                                {
                                    try
                                    {
                                        var ogs = tmpl.GetFilterOverrides(fid);
                                        if (ogs.ProjectionLineColor.IsValid ||
                                            ogs.Halftone ||
                                            ogs.Transparency > 0)
                                            overridden++;
                                    }
                                    catch { }
                                }
                                earned = filters.Count > 0
                                    ? weight * overridden / filters.Count : 0;
                            }
                        }
                        break;
                    case "DetailLevel":
                        earned = view.DetailLevel != ViewDetailLevel.Undefined ? weight : 0;
                        break;
                    case "CorrectDiscipline":
                        string match = FindMatchingTemplate(view);
                        if (view.ViewTemplateId != ElementId.InvalidElementId)
                        {
                            var tmpl = doc.GetElement(view.ViewTemplateId) as View;
                            earned = (tmpl != null && match != null &&
                                string.Equals(tmpl.Name, match, StringComparison.OrdinalIgnoreCase))
                                ? weight : weight * 0.3;
                        }
                        break;
                    case "PhaseCorrect":
                        earned = weight * 0.5; // default partial credit
                        try
                        {
                            Parameter pp = view.get_Parameter(BuiltInParameter.VIEW_PHASE);
                            if (pp != null && pp.HasValue) earned = weight;
                        }
                        catch { }
                        break;
                    case "VGConsistent":
                        earned = view.ViewTemplateId != ElementId.InvalidElementId
                            ? weight * 0.7 : 0;
                        break;
                    case "NoOrphans":
                        earned = weight; // assume good until proven otherwise
                        if (view.ViewTemplateId != ElementId.InvalidElementId)
                        {
                            var tmpl = doc.GetElement(view.ViewTemplateId) as View;
                            if (tmpl != null)
                            {
                                foreach (ElementId fid in tmpl.GetFilters())
                                {
                                    if (doc.GetElement(fid) == null)
                                    { earned = 0; break; }
                                }
                            }
                        }
                        break;
                    case "ScaleAppropriate":
                        try
                        {
                            Parameter scale = view.get_Parameter(BuiltInParameter.VIEW_SCALE);
                            if (scale != null && scale.HasValue)
                            {
                                int s = scale.AsInteger();
                                earned = (s >= 20 && s <= 500) ? weight : weight * 0.3;
                            }
                        }
                        catch { earned = weight * 0.5; }
                        break;
                }

                totalScore += earned;
                details.Add((criterion, earned, weight));
            }

            return (totalScore, maxScore, details.ToArray());
        }

        // ── VG Snapshot Engine (for diff comparison) ──────────────────

        /// <summary>
        /// Captures a complete VG snapshot of a view template for comparison.
        /// Returns a dictionary of filter name → override description.
        /// </summary>
        public static Dictionary<string, string> CaptureVGSnapshot(Document doc, View template)
        {
            var snapshot = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            snapshot["DetailLevel"] = template.DetailLevel.ToString();

            foreach (ElementId filterId in template.GetFilters())
            {
                Element filterEl = doc.GetElement(filterId);
                if (filterEl == null) continue;

                string filterName = filterEl.Name;
                bool visible = template.GetFilterVisibility(filterId);
                var ogs = template.GetFilterOverrides(filterId);

                var desc = new StringBuilder();
                desc.Append(visible ? "Visible" : "Hidden");

                if (ogs.ProjectionLineColor.IsValid)
                    desc.Append($" LineCol=({ogs.ProjectionLineColor.Red},{ogs.ProjectionLineColor.Green},{ogs.ProjectionLineColor.Blue})");
                if (ogs.ProjectionLineWeight > 0)
                    desc.Append($" LineWt={ogs.ProjectionLineWeight}");
                if (ogs.Halftone)
                    desc.Append(" Halftone");
                if (ogs.Transparency > 0)
                    desc.Append($" Trans={ogs.Transparency}%");
                if (ogs.SurfaceForegroundPatternColor.IsValid)
                    desc.Append($" FillCol=({ogs.SurfaceForegroundPatternColor.Red},{ogs.SurfaceForegroundPatternColor.Green},{ogs.SurfaceForegroundPatternColor.Blue})");

                snapshot[$"Filter:{filterName}"] = desc.ToString();
            }

            return snapshot;
        }

        /// <summary>
        /// Compares two VG snapshots and returns the differences.
        /// </summary>
        public static List<(string key, string valueA, string valueB)>
            DiffSnapshots(Dictionary<string, string> snapA, Dictionary<string, string> snapB)
        {
            var diffs = new List<(string, string, string)>();
            var allKeys = new HashSet<string>(snapA.Keys);
            foreach (string k in snapB.Keys) allKeys.Add(k);

            foreach (string key in allKeys.OrderBy(k => k))
            {
                snapA.TryGetValue(key, out string vA);
                snapB.TryGetValue(key, out string vB);
                vA = vA ?? "(not set)";
                vB = vB ?? "(not set)";
                if (!string.Equals(vA, vB, StringComparison.OrdinalIgnoreCase))
                    diffs.Add((key, vA, vB));
            }

            return diffs;
        }

        // ── Auto-Repair Engine ────────────────────────────────────────

        /// <summary>
        /// Diagnoses template health issues and returns actionable fix items.
        /// </summary>
        public static List<(string issue, string severity, Action<Document, View, Transaction> fix)>
            DiagnoseTemplate(Document doc, View template)
        {
            var issues = new List<(string, string, Action<Document, View, Transaction>)>();

            // Check 1: Orphaned filter references
            foreach (ElementId fid in template.GetFilters())
            {
                if (doc.GetElement(fid) == null)
                {
                    ElementId capturedId = fid;
                    issues.Add(($"Orphan filter ID {fid.Value}",
                        "HIGH",
                        (d, v, tx) =>
                        {
                            try { v.RemoveFilter(capturedId); }
                            catch (Exception ex) { StingLog.Warn($"Remove orphan filter ID {capturedId.Value}: {ex.Message}"); }
                        }));
                }
            }

            // Check 2: Missing STING filters
            var stingFilters = new FilteredElementCollector(doc)
                .OfClass(typeof(ParameterFilterElement))
                .Cast<ParameterFilterElement>()
                .Where(f => f.Name.StartsWith("STING"))
                .ToList();

            var appliedIds = new HashSet<ElementId>(template.GetFilters());
            int missingCount = stingFilters.Count(f => !appliedIds.Contains(f.Id));
            if (missingCount > 0)
            {
                issues.Add(($"{missingCount} STING filters not applied",
                    "MEDIUM",
                    (d, v, tx) =>
                    {
                        foreach (var sf in stingFilters)
                        {
                            if (!appliedIds.Contains(sf.Id))
                            {
                                try
                                {
                                    v.AddFilter(sf.Id);
                                    v.SetFilterVisibility(sf.Id, true);
                                }
                                catch (Exception ex) { StingLog.Warn($"Add missing STING filter '{sf.Name}': {ex.Message}"); }
                            }
                        }
                    }));
            }

            // Check 3: Detail level undefined
            if (template.DetailLevel == ViewDetailLevel.Undefined)
            {
                issues.Add(("Detail level is Undefined",
                    "LOW",
                    (d, v, tx) => { v.DetailLevel = ViewDetailLevel.Medium; }));
            }

            // Check 4: No filter overrides (all default)
            bool anyOverride = false;
            foreach (ElementId fid in template.GetFilters())
            {
                if (doc.GetElement(fid) == null) continue;
                try
                {
                    var ogs = template.GetFilterOverrides(fid);
                    if (ogs.ProjectionLineColor.IsValid || ogs.Halftone ||
                        ogs.Transparency > 0)
                    { anyOverride = true; break; }
                }
                catch { }
            }
            if (!anyOverride && template.GetFilters().Count > 0)
            {
                issues.Add(("No filter overrides configured (all default)",
                    "MEDIUM",
                    null)); // needs ConfigureTemplateVG — handled by caller
            }

            return issues;
        }

        /// <summary>Helper for TemplateSetupWizardCommand — runs a sub-step and tracks result.</summary>
        public static int RunWizardStep(ref int step, StringBuilder report,
            string label, Func<Result> action)
        {
            step++;
            var sw = Stopwatch.StartNew();
            try
            {
                Result r = action();
                sw.Stop();
                string status = r == Result.Succeeded ? "OK" : "WARN";
                report.AppendLine($"  {step,2}. {label} — {status} ({sw.Elapsed.TotalSeconds:F1}s)");
                StingLog.Info($"Template Wizard step {step}: {label} — {status}");
                return r == Result.Succeeded ? 1 : 0;
            }
            catch (Exception ex)
            {
                sw.Stop();
                report.AppendLine($"  {step,2}. {label} — FAILED: {ex.Message}");
                StingLog.Error($"Template Wizard step {step}: {label}", ex);
                return 0;
            }
        }

        // ── CSV Record Loaders (MR_SCHEDULES.csv v2.8+) ──────────────────

        /// <summary>
        /// Loads LINE_STYLE records from MR_SCHEDULES.csv and returns parsed definitions.
        /// Fields encoding: "Weight=n, RGB(r,g,b), Pattern=..., Disc=..., Use=..., Std=..."
        /// Falls back to hardcoded LineStyleDefs if CSV not found or parse fails.
        /// </summary>
        public static List<(string name, byte r, byte g, byte b, int weight, string pattern)>
            LoadLineStylesFromCsv()
        {
            var results = new List<(string, byte, byte, byte, int, string)>();
            string csvPath = StingToolsApp.FindDataFile("MR_SCHEDULES.csv");
            if (string.IsNullOrEmpty(csvPath)) return results;
            try
            {
                foreach (string line in File.ReadAllLines(csvPath))
                {
                    if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#")) continue;
                    string[] cols = StingToolsApp.ParseCsvLine(line);
                    if (cols.Length < 8) continue;
                    if (cols[0].Trim() != "LINE_STYLE") continue;

                    string name = "STING - " + cols[3].Trim();
                    string fields = cols[7].Trim();

                    // Parse encoded fields: "Weight=n, RGB(r,g,b), Pattern=..."
                    int weight = 2;
                    byte cr = 0, cg = 0, cb = 0;
                    string patName = null;

                    foreach (string part in fields.Split(','))
                    {
                        string p = part.Trim();
                        if (p.StartsWith("Weight="))
                        {
                            string wStr = p.Substring(7).Trim();
                            if (int.TryParse(wStr, out int w)) weight = w;
                        }
                        else if (p.StartsWith("RGB(") && p.EndsWith(")"))
                        {
                            string rgb = p.Substring(4, p.Length - 5);
                            string[] parts = rgb.Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries);
                            if (parts.Length >= 3 &&
                                byte.TryParse(parts[0], out byte pr) &&
                                byte.TryParse(parts[1], out byte pg) &&
                                byte.TryParse(parts[2], out byte pb))
                            { cr = pr; cg = pg; cb = pb; }
                        }
                        else if (p.StartsWith("Pattern="))
                        {
                            string pat = p.Substring(8).Trim();
                            if (pat != "Solid" && pat.Length > 0) patName = pat;
                        }
                    }
                    results.Add((name, cr, cg, cb, weight, patName));
                }
            }
            catch (Exception ex) { StingLog.Warn($"LoadLineStylesFromCsv: {ex.Message}"); }
            return results;
        }

        /// <summary>
        /// Loads OBJECT_STYLE records from MR_SCHEDULES.csv.
        /// Fields encoding: "ProjW=n, CutW=n, ProjRGB(r,g,b), ..."
        /// </summary>
        public static List<(BuiltInCategory cat, int projWt, int cutWt, byte r, byte g, byte b)>
            LoadObjectStylesFromCsv()
        {
            var results = new List<(BuiltInCategory, int, int, byte, byte, byte)>();
            string csvPath = StingToolsApp.FindDataFile("MR_SCHEDULES.csv");
            if (string.IsNullOrEmpty(csvPath)) return results;
            try
            {
                foreach (string line in File.ReadAllLines(csvPath))
                {
                    if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#")) continue;
                    string[] cols = StingToolsApp.ParseCsvLine(line);
                    if (cols.Length < 8) continue;
                    if (cols[0].Trim() != "OBJECT_STYLE") continue;

                    string catName = cols[4].Trim();
                    string fields = cols[7].Trim();

                    if (!CategoryNameToEnum.TryGetValue(catName, out BuiltInCategory bic))
                        continue;

                    int projWt = 2, cutWt = 3;
                    byte cr = 0, cg = 0, cb = 0;

                    foreach (string part in fields.Split(','))
                    {
                        string p = part.Trim();
                        if (p.StartsWith("ProjW=") && int.TryParse(p.Substring(6).Trim(), out int pw))
                            projWt = pw;
                        else if (p.StartsWith("CutW=") && int.TryParse(p.Substring(5).Trim(), out int cw))
                            cutWt = cw;
                        else if (p.StartsWith("ProjRGB(") && p.EndsWith(")"))
                        {
                            string rgb = p.Substring(8, p.Length - 9);
                            string[] parts = rgb.Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries);
                            if (parts.Length >= 3 &&
                                byte.TryParse(parts[0], out byte pr) &&
                                byte.TryParse(parts[1], out byte pg) &&
                                byte.TryParse(parts[2], out byte pb))
                            { cr = pr; cg = pg; cb = pb; }
                        }
                    }
                    results.Add((bic, projWt, cutWt, cr, cg, cb));
                }
            }
            catch (Exception ex) { StingLog.Warn($"LoadObjectStylesFromCsv: {ex.Message}"); }
            return results;
        }

        /// <summary>
        /// Loads VIEW_TEMPLATE records from MR_SCHEDULES.csv.
        /// Returns template name, discipline, VG scheme, detail level, scale.
        /// </summary>
        public static List<(string name, string discipline, string vgScheme, string detailLevel, string scale)>
            LoadViewTemplatesFromCsv()
        {
            var results = new List<(string, string, string, string, string)>();
            string csvPath = StingToolsApp.FindDataFile("MR_SCHEDULES.csv");
            if (string.IsNullOrEmpty(csvPath)) return results;
            try
            {
                foreach (string line in File.ReadAllLines(csvPath))
                {
                    if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#")) continue;
                    string[] cols = StingToolsApp.ParseCsvLine(line);
                    if (cols.Length < 9) continue;
                    if (cols[0].Trim() != "VIEW_TEMPLATE") continue;

                    string name = cols[1].Trim();       // Source_File = template name
                    string discipline = cols[2].Trim();  // Discipline
                    string fields = cols[7].Trim();      // Fields = encoded settings
                    string filters = cols[8].Trim();     // Filters = VG scheme name

                    // Parse detail level and scale from Fields
                    string detailLevel = "Medium";
                    string scale = "100";
                    foreach (string part in fields.Split(','))
                    {
                        string p = part.Trim();
                        if (p.StartsWith("Detail=")) detailLevel = p.Substring(7).Trim();
                        else if (p.StartsWith("Scale=1:")) scale = p.Substring(8).Trim();
                    }

                    results.Add((name, discipline, filters, detailLevel, scale));
                }
            }
            catch (Exception ex) { StingLog.Warn($"LoadViewTemplatesFromCsv: {ex.Message}"); }
            return results;
        }

        /// <summary>
        /// Loads VIEW_FILTER records from MR_SCHEDULES.csv.
        /// Returns filter name, categories, rule type, parameter, value, override settings.
        /// </summary>
        public static List<(string name, string discipline, string categories, string fields)>
            LoadViewFiltersFromCsv()
        {
            var results = new List<(string, string, string, string)>();
            string csvPath = StingToolsApp.FindDataFile("MR_SCHEDULES.csv");
            if (string.IsNullOrEmpty(csvPath)) return results;
            try
            {
                foreach (string line in File.ReadAllLines(csvPath))
                {
                    if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#")) continue;
                    string[] cols = StingToolsApp.ParseCsvLine(line);
                    if (cols.Length < 8) continue;
                    if (cols[0].Trim() != "VIEW_FILTER") continue;

                    string name = cols[3].Trim();        // Schedule_Name = filter name
                    string discipline = cols[2].Trim();   // Discipline
                    string categories = cols[6].Trim();   // Multi_Categories
                    string fields = cols[7].Trim();       // Fields = rules

                    results.Add((name, discipline, categories, fields));
                }
            }
            catch (Exception ex) { StingLog.Warn($"LoadViewFiltersFromCsv: {ex.Message}"); }
            return results;
        }

        /// <summary>
        /// Loads VG_SCHEME records from MR_SCHEDULES.csv.
        /// Returns scheme name, category, override fields.
        /// </summary>
        public static List<(string schemeName, string category, string fields)>
            LoadVGSchemesFromCsv()
        {
            var results = new List<(string, string, string)>();
            string csvPath = StingToolsApp.FindDataFile("MR_SCHEDULES.csv");
            if (string.IsNullOrEmpty(csvPath)) return results;
            try
            {
                foreach (string line in File.ReadAllLines(csvPath))
                {
                    if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#")) continue;
                    string[] cols = StingToolsApp.ParseCsvLine(line);
                    if (cols.Length < 8) continue;
                    if (cols[0].Trim() != "VG_SCHEME") continue;

                    string schemeName = cols[1].Trim();  // Source_File = scheme name
                    string category = cols[4].Trim();    // Category
                    string fields = cols[7].Trim();      // Fields = VG settings

                    results.Add((schemeName, category, fields));
                }
            }
            catch (Exception ex) { StingLog.Warn($"LoadVGSchemesFromCsv: {ex.Message}"); }
            return results;
        }

        // ── Style Definition Tables (hardcoded fallbacks) ─────────────────

        /// <summary>Fill pattern definitions for ISO 128-2:2020 standard patterns.</summary>
        public static readonly (string name, FillPatternTarget target,
            double angle1Deg, double spacing1Mm,
            double angle2Deg, double spacing2Mm)[] FillPatternDefs =
        {
            // Drafting patterns
            ("STING - Crosshatch", FillPatternTarget.Drafting, 0, 3, 90, 3),
            ("STING - Diagonal Up", FillPatternTarget.Drafting, 45, 3, 0, 0),
            ("STING - Diagonal Down", FillPatternTarget.Drafting, 135, 3, 0, 0),
            ("STING - Diagonal Cross", FillPatternTarget.Drafting, 45, 3, 135, 3),
            ("STING - Horizontal", FillPatternTarget.Drafting, 0, 3, 0, 0),
            ("STING - Vertical", FillPatternTarget.Drafting, 90, 3, 0, 0),
            // Model patterns for materials
            ("STING - Brick Model", FillPatternTarget.Model, 0, 75, 90, 225),
            ("STING - Tile Model", FillPatternTarget.Model, 0, 300, 90, 300),
            ("STING - Insulation Model", FillPatternTarget.Model, 45, 50, 135, 50),
            ("STING - Sand Model", FillPatternTarget.Model, 0, 2, 60, 2),
            ("STING - Earth Model", FillPatternTarget.Model, 45, 5, 0, 0),
            ("STING - Concrete Model", FillPatternTarget.Model, 45, 10, 135, 10),
        };

        /// <summary>
        /// Line style definitions: discipline + status + reference styles.
        /// name, R, G, B, line weight (1-16), line pattern name (null = solid).
        /// </summary>
        public static readonly (string name, byte r, byte g, byte b,
            int weight, string patternName)[] LineStyleDefs =
        {
            // Discipline line styles (matching ISO discipline colours)
            ("STING - Mechanical", 0, 128, 255, 3, null),
            ("STING - Electrical", 255, 200, 0, 3, null),
            ("STING - Plumbing", 0, 180, 0, 3, null),
            ("STING - Architectural", 128, 128, 128, 2, null),
            ("STING - Structural", 200, 0, 0, 4, null),
            ("STING - Fire Protection", 255, 100, 0, 3, null),
            ("STING - Low Voltage", 160, 0, 200, 2, null),
            // Status line styles
            ("STING - Existing", 128, 128, 128, 1, "STING - Dashed"),
            ("STING - Demolition", 255, 0, 0, 2, "STING - Demolition"),
            ("STING - New Work", 0, 0, 0, 3, null),
            ("STING - Temporary", 200, 200, 0, 1, "STING - Dash Dot"),
            // Reference line styles
            ("STING - Centerline", 0, 128, 0, 1, "STING - Center"),
            ("STING - Hidden Line", 128, 128, 128, 1, "STING - Hidden"),
            ("STING - Boundary", 200, 0, 0, 2, "STING - Phase Boundary"),
            ("STING - Fire Boundary", 255, 50, 0, 3, "STING - Fire Compartment"),
            ("STING - Setout", 0, 0, 255, 1, "STING - Setout"),
        };

        /// <summary>Text note type definitions: ISO 3098 / BS 8541 compliant.</summary>
        public static readonly (string name, string font, double sizeMm,
            bool bold, bool italic)[] TextStyleDefs =
        {
            ("STING - Title Large", "Arial", 5.0, true, false),
            ("STING - Title Medium", "Arial", 3.5, true, false),
            ("STING - Title Small", "Arial", 2.5, true, false),
            ("STING - Body", "Arial", 2.0, false, false),
            ("STING - Annotation", "Arial", 1.8, false, false),
            ("STING - Note", "Arial", 2.0, false, true),
            ("STING - Tag Text", "Arial", 2.0, true, false),
            ("STING - Room Name", "Arial", 3.0, true, false),
            ("STING - Room Number", "Arial", 2.5, true, false),
            ("STING - Sheet Title", "Arial", 5.0, true, false),
            ("STING - Sheet Number", "Arial", 3.5, true, false),
            ("STING - Key Note", "Arial", 1.5, false, false),
        };

        /// <summary>Dimension type definitions.</summary>
        public static readonly (string name, double textSizeMm, bool showUnits)[] DimensionStyleDefs =
        {
            ("STING - Linear mm", 2.0, true),
            ("STING - Linear m", 2.0, true),
            ("STING - Angular", 2.0, true),
            ("STING - Ordinate", 2.0, true),
            ("STING - String", 2.0, true),
            ("STING - Detail", 1.8, true),
            ("STING - Structural", 2.5, true),
        };

        /// <summary>Object style overrides: BS 1192 / ISO 19650 drawing standard.</summary>
        public static readonly (BuiltInCategory cat, int projWt, int cutWt,
            byte r, byte g, byte b)[] ObjectStyleDefs =
        {
            // Architectural
            (BuiltInCategory.OST_Walls, 3, 5, 0, 0, 0),
            (BuiltInCategory.OST_Floors, 2, 4, 0, 0, 0),
            (BuiltInCategory.OST_Ceilings, 1, 3, 0, 0, 0),
            (BuiltInCategory.OST_Roofs, 2, 4, 0, 0, 0),
            (BuiltInCategory.OST_Doors, 2, 4, 0, 0, 128),
            (BuiltInCategory.OST_Windows, 1, 3, 0, 128, 255),
            (BuiltInCategory.OST_Stairs, 2, 4, 0, 0, 0),
            (BuiltInCategory.OST_Furniture, 1, 1, 128, 128, 128),
            (BuiltInCategory.OST_Casework, 1, 2, 128, 128, 128),
            (BuiltInCategory.OST_Railings, 1, 2, 0, 0, 0),
            (BuiltInCategory.OST_Ramps, 2, 3, 0, 0, 0),
            // Structural
            (BuiltInCategory.OST_StructuralColumns, 3, 6, 200, 0, 0),
            (BuiltInCategory.OST_StructuralFraming, 3, 5, 200, 0, 0),
            (BuiltInCategory.OST_StructuralFoundation, 3, 6, 200, 0, 0),
            // Mechanical
            (BuiltInCategory.OST_MechanicalEquipment, 2, 3, 0, 128, 255),
            (BuiltInCategory.OST_DuctCurves, 1, 2, 0, 128, 255),
            (BuiltInCategory.OST_DuctFitting, 1, 2, 0, 128, 255),
            (BuiltInCategory.OST_DuctAccessory, 1, 2, 0, 128, 255),
            (BuiltInCategory.OST_DuctTerminal, 1, 2, 0, 128, 255),
            (BuiltInCategory.OST_FlexDuctCurves, 1, 2, 0, 128, 255),
            // Electrical
            (BuiltInCategory.OST_ElectricalEquipment, 2, 3, 255, 200, 0),
            (BuiltInCategory.OST_ElectricalFixtures, 1, 2, 255, 200, 0),
            (BuiltInCategory.OST_LightingFixtures, 1, 2, 255, 200, 0),
            (BuiltInCategory.OST_LightingDevices, 1, 2, 255, 200, 0),
            // Plumbing
            (BuiltInCategory.OST_PlumbingFixtures, 2, 3, 0, 180, 0),
            (BuiltInCategory.OST_PipeCurves, 1, 2, 0, 180, 0),
            (BuiltInCategory.OST_PipeFitting, 1, 2, 0, 180, 0),
            (BuiltInCategory.OST_PipeAccessory, 1, 2, 0, 180, 0),
            (BuiltInCategory.OST_FlexPipeCurves, 1, 2, 0, 180, 0),
            // Fire Protection
            (BuiltInCategory.OST_Sprinklers, 1, 2, 255, 100, 0),
            (BuiltInCategory.OST_FireAlarmDevices, 1, 2, 255, 100, 0),
            // Low Voltage
            (BuiltInCategory.OST_CommunicationDevices, 1, 2, 160, 0, 200),
            (BuiltInCategory.OST_DataDevices, 1, 2, 160, 0, 200),
            (BuiltInCategory.OST_SecurityDevices, 1, 2, 160, 0, 200),
            (BuiltInCategory.OST_NurseCallDevices, 1, 2, 160, 0, 200),
            (BuiltInCategory.OST_TelephoneDevices, 1, 2, 160, 0, 200),
            // Conduits / Cable Trays
            (BuiltInCategory.OST_Conduit, 1, 2, 180, 180, 0),
            (BuiltInCategory.OST_ConduitFitting, 1, 2, 180, 180, 0),
            (BuiltInCategory.OST_CableTray, 1, 2, 180, 180, 0),
            (BuiltInCategory.OST_CableTrayFitting, 1, 2, 180, 180, 0),
        };

        // ── Data-Driven Binding Loader ────────────────────────────────

        /// <summary>
        /// Loads FAMILY_PARAMETER_BINDINGS.csv and returns structured entries.
        /// Each entry: (paramGroup, paramName, guid, dataType, bindingType, description, category, sharedGuid).
        /// </summary>
        public static List<(string group, string name, string guid, string dataType,
            string bindingType, string desc, string category, string sharedGuid)>
            LoadFamilyParameterBindings()
        {
            var entries = new List<(string, string, string, string, string, string, string, string)>();
            string csvPath = StingToolsApp.FindDataFile("FAMILY_PARAMETER_BINDINGS.csv");
            if (string.IsNullOrEmpty(csvPath)) return entries;

            try
            {
                bool headerSkipped = false;
                foreach (string line in File.ReadAllLines(csvPath))
                {
                    if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#")) continue;
                    if (!headerSkipped) { headerSkipped = true; continue; }

                    string[] cols = StingToolsApp.ParseCsvLine(line);
                    if (cols.Length < 8) continue;

                    entries.Add((
                        cols[0].Trim(), cols[1].Trim(), cols[2].Trim(),
                        cols[3].Trim(), cols[4].Trim(), cols[5].Trim(),
                        cols[6].Trim(), cols[7].Trim()));
                }
            }
            catch (Exception ex)
            {
                StingLog.Error("Failed to load FAMILY_PARAMETER_BINDINGS.csv", ex);
            }

            return entries;
        }

        /// <summary>
        /// Loads CATEGORY_BINDINGS.csv and returns parameter→category mappings.
        /// </summary>
        public static Dictionary<string, List<(string category, string bindingType, bool isShared)>>
            LoadCategoryBindings()
        {
            var map = new Dictionary<string, List<(string, string, bool)>>(
                StringComparer.OrdinalIgnoreCase);
            string csvPath = StingToolsApp.FindDataFile("CATEGORY_BINDINGS.csv");
            if (string.IsNullOrEmpty(csvPath)) return map;

            try
            {
                bool headerSkipped = false;
                foreach (string line in File.ReadAllLines(csvPath))
                {
                    if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#")) continue;
                    if (!headerSkipped) { headerSkipped = true; continue; }

                    string[] cols = StingToolsApp.ParseCsvLine(line);
                    if (cols.Length < 4) continue;

                    string paramName = cols[0].Trim();
                    string category = cols[1].Trim();
                    string bindingType = cols[2].Trim();
                    bool isShared = cols[3].Trim().Equals("True", StringComparison.OrdinalIgnoreCase);

                    if (!map.ContainsKey(paramName))
                        map[paramName] = new List<(string, string, bool)>();
                    map[paramName].Add((category, bindingType, isShared));
                }
            }
            catch (Exception ex)
            {
                StingLog.Error("Failed to load CATEGORY_BINDINGS.csv", ex);
            }

            return map;
        }

        /// <summary>
        /// Maps Revit category display names to BuiltInCategory enums.
        /// Covers all 53 STING-supported categories.
        /// </summary>
        public static readonly Dictionary<string, BuiltInCategory> CategoryNameToEnum =
            new Dictionary<string, BuiltInCategory>(StringComparer.OrdinalIgnoreCase)
            {
                { "Air Terminals", BuiltInCategory.OST_DuctTerminal },
                { "Cable Tray Fittings", BuiltInCategory.OST_CableTrayFitting },
                { "Cable Trays", BuiltInCategory.OST_CableTray },
                { "Casework", BuiltInCategory.OST_Casework },
                { "Ceilings", BuiltInCategory.OST_Ceilings },
                { "Communication Devices", BuiltInCategory.OST_CommunicationDevices },
                { "Conduit Fittings", BuiltInCategory.OST_ConduitFitting },
                { "Conduits", BuiltInCategory.OST_Conduit },
                { "Curtain Panels", BuiltInCategory.OST_CurtainWallPanels },
                { "Curtain Systems", BuiltInCategory.OST_CurtainWallPanels },
                { "Curtain Wall Mullions", BuiltInCategory.OST_CurtainWallMullions },
                { "Data Devices", BuiltInCategory.OST_DataDevices },
                { "Doors", BuiltInCategory.OST_Doors },
                { "Duct Accessories", BuiltInCategory.OST_DuctAccessory },
                { "Duct Fittings", BuiltInCategory.OST_DuctFitting },
                { "Ducts", BuiltInCategory.OST_DuctCurves },
                { "Electrical Equipment", BuiltInCategory.OST_ElectricalEquipment },
                { "Electrical Fixtures", BuiltInCategory.OST_ElectricalFixtures },
                { "Fire Alarm Devices", BuiltInCategory.OST_FireAlarmDevices },
                { "Flex Ducts", BuiltInCategory.OST_FlexDuctCurves },
                { "Flex Pipes", BuiltInCategory.OST_FlexPipeCurves },
                { "Floors", BuiltInCategory.OST_Floors },
                { "Furniture", BuiltInCategory.OST_Furniture },
                { "Generic Models", BuiltInCategory.OST_GenericModel },
                { "Lighting Devices", BuiltInCategory.OST_LightingDevices },
                { "Lighting Fixtures", BuiltInCategory.OST_LightingFixtures },
                { "Mechanical Equipment", BuiltInCategory.OST_MechanicalEquipment },
                { "Medical Equipment", BuiltInCategory.OST_SpecialityEquipment },
                { "Nurse Call Devices", BuiltInCategory.OST_NurseCallDevices },
                { "Pipe Accessories", BuiltInCategory.OST_PipeAccessory },
                { "Pipe Fittings", BuiltInCategory.OST_PipeFitting },
                { "Pipes", BuiltInCategory.OST_PipeCurves },
                { "Plumbing Equipment", BuiltInCategory.OST_PlumbingFixtures },
                { "Plumbing Fixtures", BuiltInCategory.OST_PlumbingFixtures },
                { "Ramps", BuiltInCategory.OST_Ramps },
                { "Roofs", BuiltInCategory.OST_Roofs },
                { "Rooms", BuiltInCategory.OST_Rooms },
                { "Security Devices", BuiltInCategory.OST_SecurityDevices },
                { "Specialty Equipment", BuiltInCategory.OST_SpecialityEquipment },
                { "Sprinklers", BuiltInCategory.OST_Sprinklers },
                { "Stairs", BuiltInCategory.OST_Stairs },
                { "Structural Columns", BuiltInCategory.OST_StructuralColumns },
                { "Structural Foundations", BuiltInCategory.OST_StructuralFoundation },
                { "Structural Framing", BuiltInCategory.OST_StructuralFraming },
                { "Telephone Devices", BuiltInCategory.OST_TelephoneDevices },
                { "Walls", BuiltInCategory.OST_Walls },
                { "Windows", BuiltInCategory.OST_Windows },
                { "Railings", BuiltInCategory.OST_Railings },
                { "Columns", BuiltInCategory.OST_Columns },
                { "Furniture Systems", BuiltInCategory.OST_FurnitureSystems },
                { "Parking", BuiltInCategory.OST_Parking },
                { "Planting", BuiltInCategory.OST_Planting },
                { "Site", BuiltInCategory.OST_Site },
                { "Topography", BuiltInCategory.OST_Topography },
                { "Mass", BuiltInCategory.OST_Mass },
                { "Entourage", BuiltInCategory.OST_Entourage },
            };
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  AutoAssignTemplatesCommand — multi-layer intelligent template matching
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Analyses all views using 5-layer intelligence (name pattern, level,
    /// phase, scope box, view type default) and assigns the best-matching
    /// STING template. Reports per-layer hit statistics.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class AutoAssignTemplatesCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }
            Document doc = ctx.Doc;

            var stingTemplates = TemplateManager.GetStingTemplates(doc);
            if (stingTemplates.Count == 0)
            {
                TaskDialog.Show("Auto-Assign Templates",
                    "No STING view templates found.\nRun 'View Templates' first.");
                return Result.Succeeded;
            }

            var views = TemplateManager.GetAssignableViews(doc);
            int assigned = 0, skipped = 0, noMatch = 0, alreadyAssigned = 0;
            var assignments = new List<string>();
            var perTemplate = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            using (Transaction tx = new Transaction(doc, "STING Auto-Assign Templates"))
            {
                tx.Start();

                foreach (View view in views)
                {
                    if (view.ViewTemplateId != ElementId.InvalidElementId)
                    { alreadyAssigned++; continue; }

                    string matchName = TemplateManager.FindMatchingTemplate(view);
                    if (matchName == null) { noMatch++; continue; }

                    if (!stingTemplates.TryGetValue(matchName, out View template))
                    { skipped++; continue; }

                    try
                    {
                        view.ViewTemplateId = template.Id;
                        assigned++;
                        assignments.Add($"  {view.Name} → {matchName}");
                        if (!perTemplate.ContainsKey(matchName))
                            perTemplate[matchName] = 0;
                        perTemplate[matchName]++;
                    }
                    catch (Exception ex)
                    {
                        StingLog.Warn($"Auto-assign '{view.Name}': {ex.Message}");
                        skipped++;
                    }
                }

                tx.Commit();
            }

            var report = new StringBuilder();
            report.AppendLine($"Assigned: {assigned}");
            report.AppendLine($"Already had template: {alreadyAssigned}");
            report.AppendLine($"No match: {noMatch}");
            report.AppendLine($"Skipped: {skipped}");

            if (perTemplate.Count > 0)
            {
                report.AppendLine("\nPer-template breakdown:");
                foreach (var kvp in perTemplate.OrderByDescending(k => k.Value))
                    report.AppendLine($"  {kvp.Value,3}× {kvp.Key}");
            }

            if (assignments.Count > 0 && assignments.Count <= 40)
            {
                report.AppendLine("\nAssignments:");
                foreach (string a in assignments) report.AppendLine(a);
            }
            else if (assignments.Count > 40)
            {
                report.AppendLine($"\n(First 40 of {assignments.Count}):");
                foreach (string a in assignments.Take(40)) report.AppendLine(a);
            }

            TaskDialog.Show("Auto-Assign Templates", report.ToString());
            StingLog.Info($"Auto-Assign: {assigned} assigned, {alreadyAssigned} existing, " +
                $"{noMatch} no match, {skipped} skipped");

            return Result.Succeeded;
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  TemplateAuditCommand — comprehensive compliance audit
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Deep audit of all view templates and views:
    ///   • Template coverage (views without templates, per view type)
    ///   • Filter coverage (templates missing STING filters)
    ///   • VG consistency (correct discipline overrides)
    ///   • Unused templates (templates not assigned to any view)
    ///   • Orphaned filter references (deleted filters still referenced)
    ///   • Compliance scoring (weighted 10-point scale per view)
    ///   • Discipline distribution analysis
    /// </summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class TemplateAuditCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }
            Document doc = ctx.Doc;

            var allTemplates = new FilteredElementCollector(doc)
                .OfClass(typeof(View)).Cast<View>()
                .Where(v => v.IsTemplate).ToList();
            var stingTemplates = allTemplates.Where(v => v.Name.StartsWith("STING")).ToList();
            var otherTemplates = allTemplates.Where(v => !v.Name.StartsWith("STING")).ToList();

            var allViews = TemplateManager.GetAssignableViews(doc);
            var viewsWithTemplate = allViews.Where(v =>
                v.ViewTemplateId != ElementId.InvalidElementId).ToList();
            var viewsWithoutTemplate = allViews.Where(v =>
                v.ViewTemplateId == ElementId.InvalidElementId).ToList();
            var viewsWithSting = viewsWithTemplate.Where(v =>
            {
                var tmpl = doc.GetElement(v.ViewTemplateId) as View;
                return tmpl != null && tmpl.Name.StartsWith("STING");
            }).ToList();

            // Template usage tracking
            var templateUsage = new Dictionary<ElementId, int>();
            foreach (View v in allViews)
            {
                if (v.ViewTemplateId == ElementId.InvalidElementId) continue;
                if (!templateUsage.ContainsKey(v.ViewTemplateId))
                    templateUsage[v.ViewTemplateId] = 0;
                templateUsage[v.ViewTemplateId]++;
            }
            var unusedSting = stingTemplates
                .Where(t => !templateUsage.ContainsKey(t.Id) || templateUsage[t.Id] == 0).ToList();

            // STING filter coverage check
            var stingFilters = new FilteredElementCollector(doc)
                .OfClass(typeof(ParameterFilterElement))
                .Cast<ParameterFilterElement>()
                .Where(f => f.Name.StartsWith("STING")).ToList();

            var templatesWithMissingFilters = new List<string>();
            int totalOrphans = 0;
            foreach (View tmpl in stingTemplates)
            {
                var appliedIds = new HashSet<ElementId>(tmpl.GetFilters());
                int missing = stingFilters.Count(f => !appliedIds.Contains(f.Id));
                if (missing > 0)
                    templatesWithMissingFilters.Add($"  {tmpl.Name} — missing {missing} filters");

                // Check for orphaned filters
                foreach (ElementId fid in tmpl.GetFilters())
                {
                    if (doc.GetElement(fid) == null) totalOrphans++;
                }
            }

            // Compliance scoring (sample up to 50 views)
            double totalScore = 0;
            double maxPossible = 0;
            int scored = 0;
            foreach (View v in allViews.Take(50))
            {
                var (score, max, _) = TemplateManager.ScoreViewCompliance(doc, v);
                totalScore += score;
                maxPossible += max;
                scored++;
            }
            double avgScore = scored > 0 ? totalScore / scored : 0;
            double avgMax = scored > 0 ? maxPossible / scored : 10;

            // View type breakdown
            var typeBreakdown = allViews
                .GroupBy(v => v.ViewType)
                .OrderBy(g => g.Key.ToString())
                .Select(g =>
                {
                    int total = g.Count();
                    int withTmpl = g.Count(v => v.ViewTemplateId != ElementId.InvalidElementId);
                    return $"  {g.Key,-20} {withTmpl,4}/{total,-4} ({(total > 0 ? withTmpl * 100 / total : 0)}%)";
                }).ToList();

            // Discipline distribution
            var discDist = new Dictionary<string, int>();
            foreach (View tmpl in stingTemplates)
            {
                string disc = TemplateManager.GetDisciplineFromTemplateName(tmpl.Name) ?? "Other";
                int usage = templateUsage.ContainsKey(tmpl.Id) ? templateUsage[tmpl.Id] : 0;
                if (!discDist.ContainsKey(disc)) discDist[disc] = 0;
                discDist[disc] += usage;
            }

            // Build report
            var report = new StringBuilder();
            report.AppendLine("STING Template Audit Report");
            report.AppendLine(new string('═', 55));

            report.AppendLine($"\nTemplates:");
            report.AppendLine($"  STING templates:     {stingTemplates.Count}");
            report.AppendLine($"  Other templates:     {otherTemplates.Count}");
            report.AppendLine($"  STING filters:       {stingFilters.Count}");
            report.AppendLine($"  Orphaned filters:    {totalOrphans}");

            report.AppendLine($"\nView Coverage:");
            report.AppendLine($"  Total views:         {allViews.Count}");
            report.AppendLine($"  With template:       {viewsWithTemplate.Count}");
            report.AppendLine($"  With STING template: {viewsWithSting.Count}");
            report.AppendLine($"  Without template:    {viewsWithoutTemplate.Count}");
            if (allViews.Count > 0)
                report.AppendLine($"  Coverage:            {viewsWithTemplate.Count * 100 / allViews.Count}%");

            report.AppendLine($"\nCompliance Score:");
            report.AppendLine($"  Average: {avgScore:F1}/{avgMax:F1} " +
                $"({(avgMax > 0 ? avgScore * 100 / avgMax : 0):F0}%)");
            report.AppendLine($"  (sampled {scored} views)");

            report.AppendLine($"\nBy View Type:");
            foreach (string line in typeBreakdown) report.AppendLine(line);

            if (discDist.Count > 0)
            {
                report.AppendLine($"\nDiscipline Distribution:");
                foreach (var kvp in discDist.OrderByDescending(k => k.Value))
                    report.AppendLine($"  {kvp.Key,-12} {kvp.Value} views");
            }

            if (unusedSting.Count > 0)
            {
                report.AppendLine($"\nUnused STING Templates ({unusedSting.Count}):");
                foreach (View t in unusedSting)
                    report.AppendLine($"  {t.Name}");
            }

            if (templatesWithMissingFilters.Count > 0)
            {
                report.AppendLine($"\nTemplates Missing Filters ({templatesWithMissingFilters.Count}):");
                foreach (string line in templatesWithMissingFilters.Take(15))
                    report.AppendLine(line);
            }

            if (viewsWithoutTemplate.Count > 0 && viewsWithoutTemplate.Count <= 25)
            {
                report.AppendLine($"\nViews Without Template ({viewsWithoutTemplate.Count}):");
                foreach (View v in viewsWithoutTemplate)
                    report.AppendLine($"  [{v.ViewType}] {v.Name}");
            }
            else if (viewsWithoutTemplate.Count > 25)
            {
                report.AppendLine($"\nViews Without Template ({viewsWithoutTemplate.Count}, first 25):");
                foreach (View v in viewsWithoutTemplate.Take(25))
                    report.AppendLine($"  [{v.ViewType}] {v.Name}");
            }

            TaskDialog.Show("Template Audit", report.ToString());
            StingLog.Info($"Template Audit: {stingTemplates.Count} templates, " +
                $"{viewsWithTemplate.Count}/{allViews.Count} covered, " +
                $"score={avgScore:F1}/{avgMax:F1}, orphans={totalOrphans}");

            return Result.Succeeded;
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  TemplateDiffCommand — VG snapshot comparison between two templates
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Compares the VG overrides of two STING view templates side-by-side.
    /// Shows filter-by-filter differences in visibility, colours, line weights,
    /// halftone, transparency, and fill patterns.
    /// </summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class TemplateDiffCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }
            Document doc = ctx.Doc;

            var stingTemplates = TemplateManager.GetStingTemplates(doc);
            if (stingTemplates.Count < 2)
            {
                TaskDialog.Show("Template Diff",
                    "Need at least 2 STING templates to compare.\n" +
                    $"Found: {stingTemplates.Count}");
                return Result.Succeeded;
            }

            // Sort templates alphabetically for consistent ordering
            var sortedNames = stingTemplates.Keys.OrderBy(k => k).ToList();

            // Compare all adjacent pairs, or first vs all others
            var report = new StringBuilder();
            report.AppendLine("STING Template VG Comparison");
            report.AppendLine(new string('═', 55));

            // Compare first template with each other
            string baseName = sortedNames[0];
            var baseSnap = TemplateManager.CaptureVGSnapshot(doc, stingTemplates[baseName]);

            int totalDiffs = 0;
            foreach (string otherName in sortedNames.Skip(1))
            {
                var otherSnap = TemplateManager.CaptureVGSnapshot(doc, stingTemplates[otherName]);
                var diffs = TemplateManager.DiffSnapshots(baseSnap, otherSnap);

                if (diffs.Count > 0)
                {
                    report.AppendLine($"\n{baseName} vs {otherName} ({diffs.Count} diffs):");
                    foreach (var (key, valA, valB) in diffs.Take(15))
                    {
                        report.AppendLine($"  {key}:");
                        report.AppendLine($"    A: {valA}");
                        report.AppendLine($"    B: {valB}");
                    }
                    if (diffs.Count > 15)
                        report.AppendLine($"  ... +{diffs.Count - 15} more differences");
                    totalDiffs += diffs.Count;
                }
                else
                {
                    report.AppendLine($"\n{baseName} vs {otherName}: IDENTICAL");
                }
            }

            report.AppendLine($"\n{new string('─', 55)}");
            report.AppendLine($"Compared {baseName} against {sortedNames.Count - 1} templates");
            report.AppendLine($"Total differences found: {totalDiffs}");

            TaskDialog.Show("Template Diff", report.ToString());
            return Result.Succeeded;
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  TemplateComplianceScoreCommand — weighted per-view scoring
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Scores every view in the project against STING template compliance criteria.
    /// Uses a weighted 10-point scale covering template assignment, filters,
    /// VG overrides, discipline match, phase correctness, and more.
    /// Groups results by score band: Excellent (8-10), Good (6-8), Fair (4-6), Poor (0-4).
    /// </summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class TemplateComplianceScoreCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }
            Document doc = ctx.Doc;
            var views = TemplateManager.GetAssignableViews(doc);

            var excellent = new List<string>();
            var good = new List<string>();
            var fair = new List<string>();
            var poor = new List<string>();
            double totalScore = 0;

            foreach (View v in views)
            {
                var (score, max, _) = TemplateManager.ScoreViewCompliance(doc, v);
                double pct = max > 0 ? score * 10 / max : 0;
                totalScore += pct;

                string entry = $"  {pct:F1}/10 {v.Name}";
                if (pct >= 8) excellent.Add(entry);
                else if (pct >= 6) good.Add(entry);
                else if (pct >= 4) fair.Add(entry);
                else poor.Add(entry);
            }

            double avgScore = views.Count > 0 ? totalScore / views.Count : 0;

            var report = new StringBuilder();
            report.AppendLine("STING Template Compliance Scores");
            report.AppendLine(new string('═', 55));
            report.AppendLine($"\nOverall Average: {avgScore:F1}/10.0");
            report.AppendLine($"Total Views Scored: {views.Count}");
            report.AppendLine($"\nScore Distribution:");
            report.AppendLine($"  Excellent (8-10): {excellent.Count}");
            report.AppendLine($"  Good (6-8):       {good.Count}");
            report.AppendLine($"  Fair (4-6):       {fair.Count}");
            report.AppendLine($"  Poor (0-4):       {poor.Count}");

            report.AppendLine($"\nScoring Criteria (weights):");
            foreach (var (criterion, weight, desc) in TemplateManager.ComplianceCriteria)
                report.AppendLine($"  {weight:F1}pt {desc}");

            if (poor.Count > 0)
            {
                report.AppendLine($"\nLowest-Scoring Views ({Math.Min(poor.Count, 20)}):");
                foreach (string p in poor.Take(20)) report.AppendLine(p);
            }

            if (excellent.Count > 0 && excellent.Count <= 15)
            {
                report.AppendLine($"\nExcellent Views:");
                foreach (string e in excellent) report.AppendLine(e);
            }

            TaskDialog.Show("Compliance Scores", report.ToString());
            StingLog.Info($"Compliance Scores: avg={avgScore:F1}/10, " +
                $"excellent={excellent.Count}, good={good.Count}, " +
                $"fair={fair.Count}, poor={poor.Count}");

            return Result.Succeeded;
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  AutoFixTemplateCommand — one-click template health repair
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Diagnoses and auto-repairs all STING template health issues:
    ///   • Removes orphaned filter references (deleted filters still applied)
    ///   • Adds missing STING filters to templates
    ///   • Sets undefined detail levels to Medium
    ///   • Re-applies VG overrides where missing
    ///   • Reports all actions taken with severity ratings
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class AutoFixTemplateCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }
            Document doc = ctx.Doc;

            var stingTemplates = TemplateManager.GetStingTemplates(doc);
            if (stingTemplates.Count == 0)
            {
                TaskDialog.Show("Auto-Fix Templates",
                    "No STING view templates found.");
                return Result.Succeeded;
            }

            // Build filter + fill pattern lookups for VG re-application
            var filterLookup = new Dictionary<string, ParameterFilterElement>();
            foreach (ParameterFilterElement pfe in new FilteredElementCollector(doc)
                .OfClass(typeof(ParameterFilterElement)).Cast<ParameterFilterElement>())
                filterLookup[pfe.Name] = pfe;

            FillPatternElement solidFill = null;
            try
            {
                solidFill = new FilteredElementCollector(doc)
                    .OfClass(typeof(FillPatternElement)).Cast<FillPatternElement>()
                    .FirstOrDefault(fp => fp.GetFillPattern().IsSolidFill);
            }
            catch { }

            int totalIssues = 0;
            int totalFixed = 0;
            var report = new StringBuilder();
            report.AppendLine("STING Auto-Fix Template Report");
            report.AppendLine(new string('═', 55));

            using (Transaction tx = new Transaction(doc, "STING Auto-Fix Templates"))
            {
                tx.Start();

                foreach (var kvp in stingTemplates)
                {
                    var issues = TemplateManager.DiagnoseTemplate(doc, kvp.Value);
                    if (issues.Count == 0) continue;

                    report.AppendLine($"\n{kvp.Key}:");
                    foreach (var (issue, severity, fix) in issues)
                    {
                        totalIssues++;
                        if (fix != null)
                        {
                            try
                            {
                                fix(doc, kvp.Value, tx);
                                report.AppendLine($"  [{severity}] {issue} — FIXED");
                                totalFixed++;
                            }
                            catch (Exception ex)
                            {
                                report.AppendLine($"  [{severity}] {issue} — fix failed: {ex.Message}");
                            }
                        }
                        else
                        {
                            // Apply VG overrides via ConfigureTemplateVG
                            string disc = TemplateManager.GetDisciplineFromTemplateName(kvp.Key);
                            if (disc != null)
                            {
                                try
                                {
                                    ViewTemplatesCommand.ConfigureTemplateVG(
                                        kvp.Value, disc, filterLookup, solidFill,
                                        kvp.Value.DetailLevel);
                                    report.AppendLine($"  [{severity}] {issue} — FIXED (VG re-applied)");
                                    totalFixed++;
                                }
                                catch (Exception ex)
                                {
                                    report.AppendLine($"  [{severity}] {issue} — VG fix failed: {ex.Message}");
                                }
                            }
                            else
                            {
                                report.AppendLine($"  [{severity}] {issue} — manual fix needed");
                            }
                        }
                    }
                }

                tx.Commit();
            }

            report.AppendLine($"\n{new string('─', 55)}");
            report.AppendLine($"Total issues found: {totalIssues}");
            report.AppendLine($"Auto-fixed: {totalFixed}");
            report.AppendLine($"Remaining: {totalIssues - totalFixed}");

            TaskDialog.Show("Auto-Fix Templates", report.ToString());
            StingLog.Info($"Auto-Fix: {totalIssues} issues, {totalFixed} fixed");

            return Result.Succeeded;
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  SyncTemplateOverridesCommand — re-apply VG overrides
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Re-applies VG overrides to all existing STING view templates.
    /// Restores standard discipline colours, halftone settings, and filter
    /// configurations after manual changes.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class SyncTemplateOverridesCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }
            Document doc = ctx.Doc;

            var stingTemplates = TemplateManager.GetStingTemplates(doc);
            if (stingTemplates.Count == 0)
            {
                TaskDialog.Show("Sync Template Overrides", "No STING view templates found.");
                return Result.Succeeded;
            }

            var filterLookup = new Dictionary<string, ParameterFilterElement>();
            foreach (ParameterFilterElement pfe in new FilteredElementCollector(doc)
                .OfClass(typeof(ParameterFilterElement)).Cast<ParameterFilterElement>())
                filterLookup[pfe.Name] = pfe;

            FillPatternElement solidFill = null;
            try
            {
                solidFill = new FilteredElementCollector(doc)
                    .OfClass(typeof(FillPatternElement)).Cast<FillPatternElement>()
                    .FirstOrDefault(fp => fp.GetFillPattern().IsSolidFill);
            }
            catch { }

            int synced = 0, failed = 0;

            using (Transaction tx = new Transaction(doc, "STING Sync Template Overrides"))
            {
                tx.Start();

                foreach (var kvp in stingTemplates)
                {
                    string disc = TemplateManager.GetDisciplineFromTemplateName(kvp.Key);
                    if (disc == null) { failed++; continue; }

                    ViewDetailLevel dl = disc.StartsWith("PRES") ||
                        disc.StartsWith("SEC_P") || disc == "PRES_3D" || disc == "ELEV_P"
                        ? ViewDetailLevel.Fine : ViewDetailLevel.Medium;

                    try
                    {
                        ViewTemplatesCommand.ConfigureTemplateVG(
                            kvp.Value, disc, filterLookup, solidFill, dl);
                        synced++;
                    }
                    catch (Exception ex)
                    {
                        StingLog.Warn($"Sync VG '{kvp.Key}': {ex.Message}");
                        failed++;
                    }
                }

                tx.Commit();
            }

            TaskDialog.Show("Sync Template Overrides",
                $"Synced VG on {synced} STING templates.\nFailed: {failed}");
            StingLog.Info($"Sync VG: {synced} synced, {failed} failed");

            return Result.Succeeded;
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  CreateFillPatternsCommand — ISO 128-2:2020 fill patterns
    // ═══════════════════════════════════════════════════════════════════════

    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class CreateFillPatternsCommand : IExternalCommand
    {
        private const double MmToFeet = 1.0 / 304.8;
        private const double DegToRad = Math.PI / 180.0;

        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }
            Document doc = ctx.Doc;

            var existing = new HashSet<string>(
                new FilteredElementCollector(doc)
                    .OfClass(typeof(FillPatternElement)).Select(e => e.Name));

            int created = 0, skipped = 0;

            using (Transaction tx = new Transaction(doc, "STING Create Fill Patterns"))
            {
                tx.Start();
                foreach (var (name, target, a1, s1, a2, s2) in TemplateManager.FillPatternDefs)
                {
                    if (existing.Contains(name)) { skipped++; continue; }
                    try
                    {
                        var grids = new List<FillGrid>();
                        double spacing1 = s1 * MmToFeet;
                        double spacing2 = s2 * MmToFeet;
                        if (spacing1 > 0)
                        {
                            var g1 = new FillGrid();
                            g1.Angle = a1 * DegToRad;
                            g1.Offset = spacing1;
                            grids.Add(g1);
                        }
                        if (spacing2 > 0)
                        {
                            var g2 = new FillGrid();
                            g2.Angle = a2 * DegToRad;
                            g2.Offset = spacing2;
                            grids.Add(g2);
                        }
                        if (grids.Count > 0)
                        {
                            var pattern = new FillPattern();
                            pattern.Target = target;
                            pattern.SetFillGrids(grids);
                            pattern.Name = name;
                            FillPatternElement.Create(doc, pattern);
                            created++;
                        }
                    }
                    catch (Exception ex) { StingLog.Warn($"Fill pattern '{name}': {ex.Message}"); skipped++; }
                }
                tx.Commit();
            }

            TaskDialog.Show("Create Fill Patterns",
                $"Created {created} fill patterns.\nSkipped {skipped}.\n" +
                $"Total defined: {TemplateManager.FillPatternDefs.Length}");
            return Result.Succeeded;
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  CreateLineStylesCommand — discipline + status + reference styles
    // ═══════════════════════════════════════════════════════════════════════

    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class CreateLineStylesCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }
            Document doc = ctx.Doc;

            Category linesCat;
            try { linesCat = doc.Settings.Categories.get_Item(BuiltInCategory.OST_Lines); }
            catch { TaskDialog.Show("Line Styles", "Cannot access Lines category."); return Result.Failed; }

            var patternLookup = new Dictionary<string, ElementId>(StringComparer.OrdinalIgnoreCase);
            foreach (LinePatternElement lpe in new FilteredElementCollector(doc)
                .OfClass(typeof(LinePatternElement)).Cast<LinePatternElement>())
                patternLookup[lpe.Name] = lpe.Id;

            var existingSubs = new HashSet<string>(
                linesCat.SubCategories.Cast<Category>().Select(c => c.Name));

            // Load from CSV (79 rows in v2.8), fall back to hardcoded (16)
            var csvStyles = TemplateManager.LoadLineStylesFromCsv();
            var styleDefs = csvStyles.Count > 0
                ? csvStyles
                : TemplateManager.LineStyleDefs
                    .Select(s => (s.name, s.r, s.g, s.b, s.weight, s.patternName)).ToList();
            string source = csvStyles.Count > 0 ? "CSV" : "hardcoded";

            int created = 0, skipped = 0;

            using (Transaction tx = new Transaction(doc, "STING Create Line Styles"))
            {
                tx.Start();
                foreach (var (name, r, g, b, weight, patternName) in styleDefs)
                {
                    if (existingSubs.Contains(name)) { skipped++; continue; }
                    try
                    {
                        Category sub = doc.Settings.Categories.NewSubcategory(linesCat, name);
                        sub.LineColor = new Color(r, g, b);
                        sub.SetLineWeight(weight, GraphicsStyleType.Projection);
                        if (!string.IsNullOrEmpty(patternName) &&
                            patternLookup.TryGetValue(patternName, out ElementId patId))
                            sub.SetLinePatternId(patId, GraphicsStyleType.Projection);
                        created++;
                    }
                    catch (Exception ex) { StingLog.Warn($"Line style '{name}': {ex.Message}"); skipped++; }
                }
                tx.Commit();
            }

            TaskDialog.Show("Create Line Styles",
                $"Created {created} line styles ({source}).\nSkipped {skipped}.\n" +
                $"Total defined: {styleDefs.Count}");
            return Result.Succeeded;
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  CreateObjectStylesCommand — ISO category line weights and colours
    // ═══════════════════════════════════════════════════════════════════════

    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class CreateObjectStylesCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }
            Document doc = ctx.Doc;

            // Load from CSV (62 rows in v2.8), fall back to hardcoded (40)
            var csvStyles = TemplateManager.LoadObjectStylesFromCsv();
            var styleDefs = csvStyles.Count > 0
                ? csvStyles
                : TemplateManager.ObjectStyleDefs.ToList();
            string source = csvStyles.Count > 0 ? "CSV" : "hardcoded";

            int configured = 0, skipped = 0;

            using (Transaction tx = new Transaction(doc, "STING Configure Object Styles"))
            {
                tx.Start();
                foreach (var (cat, projWt, cutWt, r, g, b) in styleDefs)
                {
                    try
                    {
                        Category category = doc.Settings.Categories.get_Item(cat);
                        if (category == null) { skipped++; continue; }
                        category.LineColor = new Color(r, g, b);
                        category.SetLineWeight(projWt, GraphicsStyleType.Projection);
                        category.SetLineWeight(cutWt, GraphicsStyleType.Cut);
                        configured++;
                    }
                    catch (Exception ex) { StingLog.Warn($"Object style '{cat}': {ex.Message}"); skipped++; }
                }
                tx.Commit();
            }

            TaskDialog.Show("Configure Object Styles",
                $"Configured {configured} categories ({source}).\nSkipped {skipped}.\n" +
                $"Total: {styleDefs.Count}\n\n" +
                "Discipline colours: M=Blue, E=Yellow, P=Green, A=Black,\n" +
                "S=Red, FP=Orange, LV=Purple, Conduit=Olive");
            return Result.Succeeded;
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  CreateTextStylesCommand — ISO 3098 text note types
    // ═══════════════════════════════════════════════════════════════════════

    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class CreateTextStylesCommand : IExternalCommand
    {
        private const double MmToFeet = 1.0 / 304.8;

        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }
            Document doc = ctx.Doc;

            TextNoteType baseType = new FilteredElementCollector(doc)
                .OfClass(typeof(TextNoteType)).Cast<TextNoteType>().FirstOrDefault();
            if (baseType == null)
            {
                TaskDialog.Show("Text Styles", "No existing text note type found.");
                return Result.Failed;
            }

            var existingNames = new HashSet<string>(
                new FilteredElementCollector(doc)
                    .OfClass(typeof(TextNoteType)).Select(e => e.Name));

            int created = 0, skipped = 0;

            using (Transaction tx = new Transaction(doc, "STING Create Text Styles"))
            {
                tx.Start();
                foreach (var (name, font, sizeMm, bold, italic) in TemplateManager.TextStyleDefs)
                {
                    if (existingNames.Contains(name)) { skipped++; continue; }
                    try
                    {
                        TextNoteType newType = baseType.Duplicate(name) as TextNoteType;
                        if (newType == null) { skipped++; continue; }

                        Parameter sizeP = newType.get_Parameter(BuiltInParameter.TEXT_SIZE);
                        if (sizeP != null && !sizeP.IsReadOnly) sizeP.Set(sizeMm * MmToFeet);

                        Parameter fontP = newType.get_Parameter(BuiltInParameter.TEXT_FONT);
                        if (fontP != null && !fontP.IsReadOnly) fontP.Set(font);

                        Parameter boldP = newType.get_Parameter(BuiltInParameter.TEXT_STYLE_BOLD);
                        if (boldP != null && !boldP.IsReadOnly) boldP.Set(bold ? 1 : 0);

                        Parameter italP = newType.get_Parameter(BuiltInParameter.TEXT_STYLE_ITALIC);
                        if (italP != null && !italP.IsReadOnly) italP.Set(italic ? 1 : 0);

                        created++;
                    }
                    catch (Exception ex) { StingLog.Warn($"Text style '{name}': {ex.Message}"); skipped++; }
                }
                tx.Commit();
            }

            TaskDialog.Show("Create Text Styles",
                $"Created {created} text types.\nSkipped {skipped}.\n" +
                $"Total: {TemplateManager.TextStyleDefs.Length}");
            return Result.Succeeded;
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  CreateDimensionStylesCommand — ISO dimension types
    // ═══════════════════════════════════════════════════════════════════════

    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class CreateDimensionStylesCommand : IExternalCommand
    {
        private const double MmToFeet = 1.0 / 304.8;

        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }
            Document doc = ctx.Doc;

            DimensionType baseType = new FilteredElementCollector(doc)
                .OfClass(typeof(DimensionType)).Cast<DimensionType>()
                .FirstOrDefault(dt =>
                { try { return dt.Name.Contains("Linear"); } catch { return false; } })
                ?? new FilteredElementCollector(doc)
                    .OfClass(typeof(DimensionType)).Cast<DimensionType>().FirstOrDefault();

            if (baseType == null)
            {
                TaskDialog.Show("Dimension Styles", "No existing dimension type found.");
                return Result.Failed;
            }

            var existingNames = new HashSet<string>(
                new FilteredElementCollector(doc)
                    .OfClass(typeof(DimensionType)).Select(e => e.Name));

            int created = 0, skipped = 0;

            using (Transaction tx = new Transaction(doc, "STING Create Dimension Styles"))
            {
                tx.Start();
                foreach (var (name, textSizeMm, showUnits) in TemplateManager.DimensionStyleDefs)
                {
                    if (existingNames.Contains(name)) { skipped++; continue; }
                    try
                    {
                        DimensionType newType = baseType.Duplicate(name) as DimensionType;
                        if (newType == null) { skipped++; continue; }

                        Parameter sizeP = newType.get_Parameter(BuiltInParameter.TEXT_SIZE);
                        if (sizeP != null && !sizeP.IsReadOnly) sizeP.Set(textSizeMm * MmToFeet);

                        created++;
                    }
                    catch (Exception ex) { StingLog.Warn($"Dim style '{name}': {ex.Message}"); skipped++; }
                }
                tx.Commit();
            }

            TaskDialog.Show("Create Dimension Styles",
                $"Created {created} dimension types.\nSkipped {skipped}.\n" +
                $"Total: {TemplateManager.DimensionStyleDefs.Length}");
            return Result.Succeeded;
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  CreateVGOverridesCommand — comprehensive VG override application
    //  with MEP system colours, phase awareness, workset visibility,
    //  and intelligent per-discipline filter stacking
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Applies comprehensive VG overrides with multi-layer intelligence:
    ///   Layer 1: Discipline filter colours (ISO 19650 standard palette)
    ///   Layer 2: Tag-status QA highlighting (red=missing, orange=incomplete)
    ///   Layer 3: Element status styling (halftone existing, red demolished)
    ///   Layer 4: Phase-aware overrides (demolition cross-hatch, temporary dashed)
    ///   Layer 5: Workset-based visibility (hide linked models, show relevant disciplines)
    ///   Layer 6: MEP system-specific colours (HVAC-SUP=blue, HWS=red, DCW=green)
    ///
    /// Can target: active view (live preview), all STING templates, or user selection.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class CreateVGOverridesCommand : IExternalCommand
    {
        /// <summary>ISO 19650 / BS 1192 discipline colours.</summary>
        private static readonly Dictionary<string, Color> DisciplineColors =
            new Dictionary<string, Color>
            {
                { "STING - Mechanical", new Color(0, 128, 255) },
                { "STING - Electrical", new Color(255, 200, 0) },
                { "STING - Plumbing", new Color(0, 180, 0) },
                { "STING - Architectural", new Color(160, 160, 160) },
                { "STING - Structural", new Color(200, 0, 0) },
                { "STING - Fire Protection", new Color(255, 100, 0) },
                { "STING - Low Voltage", new Color(160, 0, 200) },
                { "STING - Conduits & Cable Trays", new Color(180, 180, 0) },
                { "STING - Rooms & Spaces", new Color(100, 200, 255) },
                { "STING - Generic & Specialty", new Color(128, 128, 128) },
            };

        /// <summary>MEP system-specific colour overrides for coordination views.</summary>
        private static readonly Dictionary<string, Color> SystemColors =
            new Dictionary<string, Color>(StringComparer.OrdinalIgnoreCase)
            {
                { "Supply Air", new Color(0, 100, 255) },
                { "Return Air", new Color(0, 200, 200) },
                { "Exhaust Air", new Color(150, 150, 255) },
                { "Fresh Air", new Color(100, 255, 100) },
                { "Domestic Hot Water", new Color(255, 80, 80) },
                { "Domestic Cold Water", new Color(80, 80, 255) },
                { "Sanitary", new Color(139, 90, 43) },
                { "Storm", new Color(0, 128, 128) },
                { "Fire Protection", new Color(255, 100, 0) },
                { "Heating Hot Water", new Color(255, 50, 50) },
                { "Chilled Water Supply", new Color(50, 50, 255) },
                { "Chilled Water Return", new Color(100, 100, 255) },
                { "Condenser Water", new Color(0, 180, 180) },
                { "Gas", new Color(255, 255, 0) },
            };

        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }
            UIDocument uidoc = ctx.UIDoc;
            Document doc = ctx.Doc;

            // Determine target views
            View activeView = uidoc.ActiveView;
            List<View> targets = new List<View>();

            if (activeView != null && !activeView.IsTemplate)
                targets.Add(activeView);
            else
                targets = new FilteredElementCollector(doc)
                    .OfClass(typeof(View)).Cast<View>()
                    .Where(v => v.IsTemplate && v.Name.StartsWith("STING")).ToList();

            if (targets.Count == 0)
            {
                TaskDialog.Show("VG Overrides", "No target views found.");
                return Result.Succeeded;
            }

            // Collect filters
            var filters = new FilteredElementCollector(doc)
                .OfClass(typeof(ParameterFilterElement))
                .Cast<ParameterFilterElement>()
                .Where(f => f.Name.StartsWith("STING"))
                .ToDictionary(f => f.Name, f => f);

            // Solid fill pattern
            FillPatternElement solidFill = null;
            try
            {
                solidFill = new FilteredElementCollector(doc)
                    .OfClass(typeof(FillPatternElement)).Cast<FillPatternElement>()
                    .FirstOrDefault(fp => fp.GetFillPattern().IsSolidFill);
            }
            catch { }

            // Cross-hatch pattern for demolition
            FillPatternElement crossHatch = null;
            try
            {
                crossHatch = new FilteredElementCollector(doc)
                    .OfClass(typeof(FillPatternElement)).Cast<FillPatternElement>()
                    .FirstOrDefault(fp =>
                    {
                        string n = fp.Name;
                        return n.Contains("Crosshatch") || n.Contains("Cross") || n.Contains("STING - Crosshatch");
                    });
            }
            catch { }

            int viewsConfigured = 0;
            int filtersApplied = 0;
            int schemesApplied = 0;

            // Load VG schemes from CSV for Layer 6
            var csvSchemesList = TemplateManager.LoadVGSchemesFromCsv();
            var csvSchemes = new Dictionary<string, List<(string cat, string fields)>>(
                StringComparer.OrdinalIgnoreCase);
            foreach (var (sName, sCat, sFields) in csvSchemesList)
            {
                if (!csvSchemes.ContainsKey(sName))
                    csvSchemes[sName] = new List<(string, string)>();
                csvSchemes[sName].Add((sCat, sFields));
            }

            using (Transaction tx = new Transaction(doc, "STING Apply VG Overrides"))
            {
                tx.Start();

                foreach (View target in targets)
                {
                    try
                    {
                        var appliedIds = new HashSet<ElementId>(target.GetFilters());

                        foreach (var kvp in filters)
                        {
                            if (!kvp.Key.StartsWith("STING - ")) continue;

                            try
                            {
                                // Add filter if not already applied
                                if (!appliedIds.Contains(kvp.Value.Id))
                                {
                                    target.AddFilter(kvp.Value.Id);
                                    target.SetFilterVisibility(kvp.Value.Id, true);
                                }

                                // Layer 1: Discipline colour override
                                if (DisciplineColors.TryGetValue(kvp.Key, out Color col))
                                {
                                    var ogs = new OverrideGraphicSettings();
                                    ogs.SetProjectionLineColor(col);
                                    ogs.SetProjectionLineWeight(2);
                                    if (solidFill != null)
                                    {
                                        ogs.SetSurfaceForegroundPatternId(solidFill.Id);
                                        ogs.SetSurfaceForegroundPatternColor(col);
                                    }
                                    ogs.SetSurfaceTransparency(20);
                                    target.SetFilterOverrides(kvp.Value.Id, ogs);
                                    filtersApplied++;
                                }

                                // Layer 2: QA highlighting — missing tags = red
                                if (kvp.Key.Contains("Untagged") || kvp.Key.Contains("Missing"))
                                {
                                    var qaOgs = new OverrideGraphicSettings();
                                    qaOgs.SetProjectionLineColor(new Color(255, 0, 0));
                                    qaOgs.SetProjectionLineWeight(4);
                                    if (solidFill != null)
                                    {
                                        qaOgs.SetSurfaceForegroundPatternId(solidFill.Id);
                                        qaOgs.SetSurfaceForegroundPatternColor(new Color(255, 200, 200));
                                    }
                                    target.SetFilterOverrides(kvp.Value.Id, qaOgs);
                                }

                                // Layer 2: QA highlighting — incomplete tags = orange
                                if (kvp.Key.Contains("Incomplete"))
                                {
                                    var warnOgs = new OverrideGraphicSettings();
                                    warnOgs.SetProjectionLineColor(new Color(255, 165, 0));
                                    warnOgs.SetProjectionLineWeight(3);
                                    if (solidFill != null)
                                    {
                                        warnOgs.SetSurfaceForegroundPatternId(solidFill.Id);
                                        warnOgs.SetSurfaceForegroundPatternColor(new Color(255, 235, 200));
                                    }
                                    target.SetFilterOverrides(kvp.Value.Id, warnOgs);
                                }

                                // Layer 3: Status — demolished = red + crosshatch
                                if (kvp.Key.Contains("Status: Demolished"))
                                {
                                    var demoOgs = new OverrideGraphicSettings();
                                    demoOgs.SetProjectionLineColor(new Color(255, 0, 0));
                                    demoOgs.SetProjectionLineWeight(3);
                                    if (crossHatch != null)
                                    {
                                        demoOgs.SetSurfaceForegroundPatternId(crossHatch.Id);
                                        demoOgs.SetSurfaceForegroundPatternColor(new Color(255, 150, 150));
                                    }
                                    else
                                    {
                                        demoOgs.SetHalftone(true);
                                    }
                                    target.SetFilterOverrides(kvp.Value.Id, demoOgs);
                                }

                                // Layer 3: Status — existing = halftone + transparent
                                if (kvp.Key.Contains("Status: Existing"))
                                {
                                    var existOgs = new OverrideGraphicSettings();
                                    existOgs.SetHalftone(true);
                                    existOgs.SetSurfaceTransparency(50);
                                    existOgs.SetProjectionLineColor(new Color(180, 180, 180));
                                    target.SetFilterOverrides(kvp.Value.Id, existOgs);
                                }

                                // Layer 3: Status — temporary = dashed + yellow
                                if (kvp.Key.Contains("Status: Temporary"))
                                {
                                    var tempOgs = new OverrideGraphicSettings();
                                    tempOgs.SetProjectionLineColor(new Color(200, 200, 0));
                                    tempOgs.SetProjectionLineWeight(1);
                                    tempOgs.SetSurfaceTransparency(40);
                                    target.SetFilterOverrides(kvp.Value.Id, tempOgs);
                                }
                            }
                            catch (Exception ex)
                            {
                                StingLog.Warn($"VG filter '{kvp.Key}' on '{target.Name}': {ex.Message}");
                            }
                        }

                        // Layer 5: Workset-based visibility (hide linked models in discipline views)
                        try
                        {
                            if (target.Document.IsWorkshared)
                            {
                                string disc = TemplateManager.GetDisciplineFromTemplateName(target.Name);
                                if (disc != null && disc != "ALL" && disc != "MEP")
                                {
                                    foreach (Workset ws in new FilteredWorksetCollector(doc)
                                        .OfKind(WorksetKind.UserWorkset))
                                    {
                                        // Hide linked model worksets in discipline-specific views
                                        if (ws.Name.StartsWith("Z-Linked"))
                                        {
                                            try { target.SetWorksetVisibility(ws.Id, WorksetVisibility.Hidden); }
                                            catch { }
                                        }
                                    }
                                }
                            }
                        }
                        catch { /* worksharing not available */ }

                        // Layer 6: CSV-driven VG schemes (106 VG_SCHEME rows)
                        // Match template name to a VG scheme and apply category-level overrides
                        try
                        {
                            string disc = TemplateManager.GetDisciplineFromTemplateName(target.Name);
                            string schemeName = null;

                            // Map discipline to default VG scheme
                            if (disc == "M") schemeName = "HVAC Systems";
                            else if (disc == "E") schemeName = "Electrical Systems";
                            else if (disc == "P") schemeName = "Plumbing Systems";
                            else if (disc == "FP") schemeName = "Fire Protection";
                            else if (disc == "S") schemeName = "Structural GA";
                            else if (disc == "A") schemeName = "Standard Architectural";
                            else if (disc == "PRES_C" || disc == "PRES_E") schemeName = "Monochrome";
                            else if (disc == "DEMO") schemeName = "Demolition";

                            if (schemeName != null && csvSchemes.ContainsKey(schemeName))
                            {
                                foreach (var (cat, schFields) in csvSchemes[schemeName])
                                {
                                    if (!TemplateManager.CategoryNameToEnum.TryGetValue(cat,
                                        out BuiltInCategory schemeBic)) continue;
                                    try
                                    {
                                        // Parse VG scheme fields
                                        byte sr = 0, sg = 0, sb = 0;
                                        int transp = 0;
                                        bool halftone = false;

                                        foreach (string p in schFields.Split(','))
                                        {
                                            string pt = p.Trim();
                                            if (pt.StartsWith("SurfRGB(") && pt.EndsWith(")"))
                                            {
                                                string rgb = pt.Substring(8, pt.Length - 9);
                                                string[] parts = rgb.Split(new[] { ' ', ',' },
                                                    StringSplitOptions.RemoveEmptyEntries);
                                                if (parts.Length >= 3 &&
                                                    byte.TryParse(parts[0], out byte pr) &&
                                                    byte.TryParse(parts[1], out byte pg) &&
                                                    byte.TryParse(parts[2], out byte pb))
                                                { sr = pr; sg = pg; sb = pb; }
                                            }
                                            else if (pt.StartsWith("Transp="))
                                            {
                                                string tv = pt.Substring(7).Replace("%", "").Trim();
                                                int.TryParse(tv, out transp);
                                            }
                                            else if (pt.StartsWith("Halftone="))
                                            {
                                                halftone = pt.Substring(9).Trim()
                                                    .Equals("Yes", StringComparison.OrdinalIgnoreCase);
                                            }
                                        }

                                        // Apply per-category overrides
                                        var catOgs = new OverrideGraphicSettings();
                                        if (sr > 0 || sg > 0 || sb > 0)
                                        {
                                            catOgs.SetProjectionLineColor(new Color(sr, sg, sb));
                                            if (solidFill != null)
                                            {
                                                catOgs.SetSurfaceForegroundPatternId(solidFill.Id);
                                                catOgs.SetSurfaceForegroundPatternColor(
                                                    new Color(sr, sg, sb));
                                            }
                                        }
                                        if (transp > 0) catOgs.SetSurfaceTransparency(transp);
                                        if (halftone) catOgs.SetHalftone(true);

                                        Category revCat = doc.Settings.Categories.get_Item(schemeBic);
                                        if (revCat != null)
                                        {
                                            target.SetCategoryOverrides(
                                                new ElementId(schemeBic), catOgs);
                                            schemesApplied++;
                                        }
                                    }
                                    catch { }
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            StingLog.Warn($"VG scheme on '{target.Name}': {ex.Message}");
                        }

                        viewsConfigured++;
                    }
                    catch (Exception ex)
                    {
                        StingLog.Warn($"VG override '{target.Name}': {ex.Message}");
                    }
                }

                tx.Commit();
            }

            string scope = targets.Count == 1 && !targets[0].IsTemplate
                ? $"active view '{targets[0].Name}'"
                : $"{viewsConfigured} STING view templates";

            TaskDialog.Show("VG Overrides",
                $"Applied VG overrides to {scope}.\n" +
                $"Filters configured: {filtersApplied}\n" +
                (schemesApplied > 0 ? $"VG scheme overrides: {schemesApplied}\n" : "") +
                $"\nIntelligence layers applied:\n" +
                "  1. Discipline colour coding (10 colours)\n" +
                "  2. QA highlighting (red=missing, orange=incomplete)\n" +
                "  3. Status styling (halftone existing, crosshatch demolished)\n" +
                "  4. Phase-aware overrides (temporary=dashed yellow)\n" +
                "  5. Workset visibility (hide linked models)\n" +
                (schemesApplied > 0 ? "  6. CSV-driven VG schemes (per-category overrides)\n" : ""));

            return Result.Succeeded;
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  BatchAddFamilyParamsCommand — data-driven family parameter binding
    //  from FAMILY_PARAMETER_BINDINGS.csv (4,686 entries)
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Loads FAMILY_PARAMETER_BINDINGS.csv and binds shared parameters to
    /// project categories using data-driven definitions instead of hardcoded
    /// enums. Complements LoadSharedParamsCommand by handling the full
    /// 4,686 parameter-to-category binding matrix.
    ///
    /// Intelligence layers:
    ///   1. Validates GUIDs against MR_PARAMETERS.txt before binding
    ///   2. Skips already-bound parameters (idempotent)
    ///   3. Groups bindings by parameter for batch efficiency
    ///   4. Handles Type vs Instance binding types from CSV
    ///   5. Reports per-category success with coverage percentage
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class BatchAddFamilyParamsCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }
            Document doc = ctx.Doc;

            // Load the shared parameter file
            string spfPath = StingToolsApp.FindDataFile("MR_PARAMETERS.txt");
            if (string.IsNullOrEmpty(spfPath))
            {
                TaskDialog.Show("Batch Add Family Params",
                    "MR_PARAMETERS.txt not found in data directory.");
                return Result.Failed;
            }

            // Load binding definitions from CSV
            var bindings = TemplateManager.LoadFamilyParameterBindings();
            if (bindings.Count == 0)
            {
                TaskDialog.Show("Batch Add Family Params",
                    "FAMILY_PARAMETER_BINDINGS.csv not found or empty.\n" +
                    "Place it in the data directory alongside the DLL.");
                return Result.Failed;
            }

            // Open shared parameter file
            DefinitionFile defFile;
            try
            {
                ctx.App.Application.SharedParametersFilename = spfPath;
                defFile = ctx.App.Application.OpenSharedParameterFile();
                if (defFile == null)
                {
                    TaskDialog.Show("Batch Add Family Params",
                        "Failed to open shared parameter file.");
                    return Result.Failed;
                }
            }
            catch (Exception ex)
            {
                StingLog.Error("Failed to open shared parameter file", ex);
                TaskDialog.Show("Batch Add Family Params",
                    $"Error opening parameter file: {ex.Message}");
                return Result.Failed;
            }

            // Build definition lookup by name AND by GUID (GAP-004)
            var defLookup = new Dictionary<string, ExternalDefinition>(
                StringComparer.OrdinalIgnoreCase);
            var defByGuid = new Dictionary<Guid, ExternalDefinition>();
            foreach (DefinitionGroup group in defFile.Groups)
            {
                foreach (ExternalDefinition def in group.Definitions)
                {
                    defLookup[def.Name] = def;
                    defByGuid[def.GUID] = def;
                }
            }

            // Group bindings by parameter for batch processing
            var paramGroups = bindings
                .GroupBy(b => b.name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            int totalBound = 0;
            int totalSkipped = 0;
            int totalFailed = 0;
            int paramsProcessed = 0;
            int guidResolved = 0;
            var perCategory = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            using (Transaction tx = new Transaction(doc, "STING Batch Add Family Parameters"))
            {
                tx.Start();

                foreach (var paramGroup in paramGroups)
                {
                    string paramName = paramGroup.Key;

                    // Find the external definition — try name first, then GUID fallback (GAP-004)
                    if (!defLookup.TryGetValue(paramName, out ExternalDefinition extDef))
                    {
                        // GAP-004: Try GUID-based resolution from CSV sharedGuid column
                        string guidStr = paramGroup.First().sharedGuid;
                        if (string.IsNullOrEmpty(guidStr))
                            guidStr = paramGroup.First().guid;

                        if (!string.IsNullOrEmpty(guidStr) && Guid.TryParse(guidStr, out Guid g)
                            && defByGuid.TryGetValue(g, out extDef))
                        {
                            guidResolved++;
                            StingLog.Info($"GAP-004: Resolved '{paramName}' via GUID {g} → '{extDef.Name}'");
                        }
                        else
                        {
                            totalSkipped += paramGroup.Count();
                            continue;
                        }
                    }

                    paramsProcessed++;

                    // Build category set for this parameter
                    CategorySet catSet = ctx.App.Application.Create.NewCategorySet();
                    string bindingType = "Type"; // default

                    foreach (var entry in paramGroup)
                    {
                        bindingType = entry.bindingType;
                        string catName = entry.category;

                        if (TemplateManager.CategoryNameToEnum.TryGetValue(catName,
                            out BuiltInCategory bic))
                        {
                            try
                            {
                                Category cat = doc.Settings.Categories.get_Item(bic);
                                if (cat != null)
                                {
                                    catSet.Insert(cat);
                                    if (!perCategory.ContainsKey(catName))
                                        perCategory[catName] = 0;
                                }
                            }
                            catch { }
                        }
                    }

                    if (catSet.Size == 0)
                    {
                        totalSkipped += paramGroup.Count();
                        continue;
                    }

                    // Check if already bound
                    BindingMap bmap = doc.ParameterBindings;
                    bool alreadyBound = false;
                    try
                    {
                        var existing = bmap.get_Item(extDef);
                        if (existing != null) alreadyBound = true;
                    }
                    catch { }

                    if (alreadyBound)
                    {
                        totalSkipped += paramGroup.Count();
                        continue;
                    }

                    // Create binding
                    try
                    {
                        ElementBinding binding;
                        if (bindingType.Equals("Instance", StringComparison.OrdinalIgnoreCase))
                            binding = ctx.App.Application.Create
                                .NewInstanceBinding(catSet);
                        else
                            binding = ctx.App.Application.Create
                                .NewTypeBinding(catSet);

                        bool success = bmap.Insert(extDef, binding,
                            GroupTypeId.General);

                        if (success)
                        {
                            totalBound += catSet.Size;
                            foreach (Category c in catSet)
                            {
                                if (perCategory.ContainsKey(c.Name))
                                    perCategory[c.Name]++;
                            }
                        }
                        else
                        {
                            totalFailed += paramGroup.Count();
                        }
                    }
                    catch (Exception ex)
                    {
                        StingLog.Warn($"Bind '{paramName}': {ex.Message}");
                        totalFailed += paramGroup.Count();
                    }
                }

                tx.Commit();
            }

            // Build coverage report
            var report = new StringBuilder();
            report.AppendLine($"Batch Add Family Parameters");
            report.AppendLine(new string('═', 50));
            report.AppendLine($"\nCSV entries: {bindings.Count}");
            report.AppendLine($"Unique parameters: {paramGroups.Count}");
            report.AppendLine($"Parameters processed: {paramsProcessed}");
            if (guidResolved > 0)
                report.AppendLine($"Resolved by GUID: {guidResolved}");
            report.AppendLine($"Bindings created: {totalBound}");
            report.AppendLine($"Skipped (exist/missing): {totalSkipped}");
            report.AppendLine($"Failed: {totalFailed}");

            if (perCategory.Count > 0)
            {
                report.AppendLine($"\nPer-category bindings:");
                foreach (var kvp in perCategory.OrderByDescending(k => k.Value).Take(20))
                    report.AppendLine($"  {kvp.Value,3} params → {kvp.Key}");
            }

            TaskDialog.Show("Batch Add Family Params", report.ToString());
            StingLog.Info($"Batch Family Params: {totalBound} bound, " +
                $"{totalSkipped} skipped, {totalFailed} failed");

            return totalBound > 0 ? Result.Succeeded : Result.Failed;
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  FamilyParameterProcessorCommand — batch open .rfa files, add params
    //  and formulas via FamilyManager, save modified copies
    //  (Port of pyRevit Batch Add Family Params tool)
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Opens .rfa family files (single file or folder), reads the family category,
    /// adds shared parameters via FamilyManager.AddParameter(), applies formulas
    /// via FamilyManager.SetFormula(), creates a backup, and saves the modified family.
    ///
    /// Workflow:
    ///   1. User selects a single .rfa file or a folder of .rfa files
    ///   2. For each family file:
    ///      a. Open the family document (app.OpenDocumentFile)
    ///      b. Read OwnerFamily.FamilyCategory → map to CSV category name
    ///      c. Look up applicable parameters from FAMILY_PARAMETER_BINDINGS.csv
    ///      d. Add shared parameters via FamilyManager.AddParameter()
    ///      e. Look up applicable formulas from FORMULAS_WITH_DEPENDENCIES.csv
    ///      f. Apply formulas via FamilyManager.SetFormula()
    ///      g. Backup the original to _param_backups/ subfolder
    ///      h. Save the modified family
    ///   3. Report results per-family and overall summary
    ///
    /// Data sources:
    ///   - MR_PARAMETERS.txt — shared parameter definitions
    ///   - FAMILY_PARAMETER_BINDINGS.csv — 4,686 category-to-parameter bindings
    ///   - FORMULAS_WITH_DEPENDENCIES.csv — 199+ formula definitions
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class FamilyParameterProcessorCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            UIApplication uiApp = commandData.Application;
            Autodesk.Revit.ApplicationServices.Application app = uiApp.Application;

            // ── Step 1: Set up shared parameter file ────────────────────────
            string spfPath = StingToolsApp.FindDataFile("MR_PARAMETERS.txt");
            if (string.IsNullOrEmpty(spfPath))
            {
                TaskDialog.Show("Family Parameter Processor",
                    "MR_PARAMETERS.txt not found in data directory.");
                return Result.Failed;
            }

            string previousSpf = app.SharedParametersFilename;
            app.SharedParametersFilename = spfPath;
            DefinitionFile defFile = app.OpenSharedParameterFile();
            if (defFile == null)
            {
                TaskDialog.Show("Family Parameter Processor",
                    "Failed to open shared parameter file.");
                if (!string.IsNullOrEmpty(previousSpf))
                    app.SharedParametersFilename = previousSpf;
                return Result.Failed;
            }

            // Build definition lookup by name and GUID
            var defByName = new Dictionary<string, ExternalDefinition>(StringComparer.OrdinalIgnoreCase);
            var defByGuid = new Dictionary<Guid, ExternalDefinition>();
            foreach (DefinitionGroup group in defFile.Groups)
            {
                foreach (ExternalDefinition def in group.Definitions)
                {
                    defByName[def.Name] = def;
                    defByGuid[def.GUID] = def;
                }
            }

            // ── Step 2: Load binding and formula data ───────────────────────
            var allBindings = TemplateManager.LoadFamilyParameterBindings();
            if (allBindings.Count == 0)
            {
                TaskDialog.Show("Family Parameter Processor",
                    "FAMILY_PARAMETER_BINDINGS.csv not found or empty.");
                if (!string.IsNullOrEmpty(previousSpf))
                    app.SharedParametersFilename = previousSpf;
                return Result.Failed;
            }

            // Index bindings by category name
            var bindingsByCategory = new Dictionary<string, List<(string group, string name, string guid,
                string dataType, string bindingType, string desc, string category, string sharedGuid)>>(
                StringComparer.OrdinalIgnoreCase);
            foreach (var b in allBindings)
            {
                if (!bindingsByCategory.TryGetValue(b.category, out var list))
                {
                    list = new List<(string, string, string, string, string, string, string, string)>();
                    bindingsByCategory[b.category] = list;
                }
                list.Add(b);
            }

            // Load formulas indexed by parameter name
            var formulasByParam = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            string formulaCsvPath = StingToolsApp.FindDataFile("FORMULAS_WITH_DEPENDENCIES.csv");
            if (!string.IsNullOrEmpty(formulaCsvPath))
            {
                try
                {
                    bool headerSkipped = false;
                    foreach (string line in File.ReadAllLines(formulaCsvPath))
                    {
                        if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#")) continue;
                        if (!headerSkipped) { headerSkipped = true; continue; }
                        string[] cols = StingToolsApp.ParseCsvLine(line);
                        if (cols.Length < 4) continue;
                        string paramName = cols[1].Trim();
                        string formula = cols[3].Trim();
                        if (!string.IsNullOrEmpty(paramName) && !string.IsNullOrEmpty(formula))
                            formulasByParam[paramName] = formula;
                    }
                }
                catch (Exception ex)
                {
                    StingLog.Warn($"Failed to load formulas: {ex.Message}");
                }
            }

            // ── Step 3: User selects single file or folder ──────────────────
            var td = new TaskDialog("Family Parameter Processor");
            td.MainContent = "Select families to process.\n\n" +
                $"Available: {allBindings.Count} parameter bindings across " +
                $"{bindingsByCategory.Count} categories\n" +
                $"Formulas: {formulasByParam.Count} formula definitions\n\n" +
                "Choose how to select families:";
            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink1,
                "Process a single .rfa file",
                "Opens a file dialog to select one family file");
            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink2,
                "Process all .rfa files in a folder",
                "Opens a folder browser to select a directory (processes all .rfa files recursively)");
            td.CommonButtons = TaskDialogCommonButtons.Cancel;

            TaskDialogResult tdResult = td.Show();
            var familyPaths = new List<string>();

            if (tdResult == TaskDialogResult.CommandLink1)
            {
                // Single file
                var dlg = new Microsoft.Win32.OpenFileDialog
                {
                    Title = "Select Revit Family File",
                    Filter = "Revit Family Files (*.rfa)|*.rfa",
                    Multiselect = true
                };
                if (dlg.ShowDialog() == true)
                    familyPaths.AddRange(dlg.FileNames);
            }
            else if (tdResult == TaskDialogResult.CommandLink2)
            {
                // Folder
                var dlg = new Microsoft.Win32.OpenFileDialog
                {
                    Title = "Select any file inside the family folder",
                    Filter = "Revit Family Files (*.rfa)|*.rfa",
                    CheckFileExists = true
                };
                if (dlg.ShowDialog() == true)
                {
                    string folder = Path.GetDirectoryName(dlg.FileName);
                    if (!string.IsNullOrEmpty(folder))
                        familyPaths.AddRange(Directory.GetFiles(folder, "*.rfa", SearchOption.AllDirectories));
                }
            }
            else
            {
                if (!string.IsNullOrEmpty(previousSpf))
                    app.SharedParametersFilename = previousSpf;
                return Result.Cancelled;
            }

            if (familyPaths.Count == 0)
            {
                TaskDialog.Show("Family Parameter Processor", "No family files selected.");
                if (!string.IsNullOrEmpty(previousSpf))
                    app.SharedParametersFilename = previousSpf;
                return Result.Cancelled;
            }

            // ── Step 4: Process each family ─────────────────────────────────
            int totalFamilies = familyPaths.Count;
            int processed = 0;
            int paramsAdded = 0;
            int formulasApplied = 0;
            int skippedNoCategory = 0;
            int skippedExisting = 0;
            int failedParams = 0;
            int failedFormulas = 0;
            var perFamilyResults = new List<string>();

            foreach (string familyPath in familyPaths)
            {
                string fileName = Path.GetFileName(familyPath);
                Document famDoc = null;
                try
                {
                    // Open the family document
                    famDoc = app.OpenDocumentFile(familyPath);
                    if (famDoc == null || !famDoc.IsFamilyDocument)
                    {
                        perFamilyResults.Add($"[SKIP] {fileName} — not a family document");
                        skippedNoCategory++;
                        continue;
                    }

                    // Read family category
                    Family ownerFamily = famDoc.OwnerFamily;
                    string categoryName = ownerFamily?.FamilyCategory?.Name ?? "";
                    if (string.IsNullOrEmpty(categoryName))
                    {
                        perFamilyResults.Add($"[SKIP] {fileName} — no category detected");
                        skippedNoCategory++;
                        famDoc.Close(false);
                        continue;
                    }

                    // Find applicable parameter bindings for this category
                    if (!bindingsByCategory.TryGetValue(categoryName, out var categoryBindings))
                    {
                        perFamilyResults.Add($"[SKIP] {fileName} ({categoryName}) — no bindings defined for this category");
                        skippedNoCategory++;
                        famDoc.Close(false);
                        continue;
                    }

                    // Get existing family parameters
                    FamilyManager fmgr = famDoc.FamilyManager;
                    var existingParams = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach (FamilyParameter fp in fmgr.Parameters)
                    {
                        if (!string.IsNullOrEmpty(fp.Definition?.Name))
                            existingParams.Add(fp.Definition.Name);
                    }

                    int familyAdded = 0;
                    int familySkipped = 0;
                    int familyFormulas = 0;
                    int familyFailed = 0;

                    using (Transaction tx = new Transaction(famDoc, "STING Add Family Parameters"))
                    {
                        tx.Start();

                        // Deduplicate by parameter name (same param may appear for multiple categories)
                        var uniqueParams = categoryBindings
                            .GroupBy(b => b.name, StringComparer.OrdinalIgnoreCase)
                            .ToList();

                        foreach (var paramGroup in uniqueParams)
                        {
                            string paramName = paramGroup.Key;
                            var entry = paramGroup.First();

                            // Skip if already exists
                            if (existingParams.Contains(paramName))
                            {
                                familySkipped++;
                                skippedExisting++;
                                continue;
                            }

                            // Find the shared parameter definition
                            ExternalDefinition extDef = null;
                            if (defByName.TryGetValue(paramName, out extDef))
                            {
                                // Found by name
                            }
                            else if (!string.IsNullOrEmpty(entry.sharedGuid) &&
                                     Guid.TryParse(entry.sharedGuid, out Guid g) &&
                                     defByGuid.TryGetValue(g, out extDef))
                            {
                                // Found by GUID fallback
                            }
                            else if (!string.IsNullOrEmpty(entry.guid) &&
                                     Guid.TryParse(entry.guid, out Guid g2) &&
                                     defByGuid.TryGetValue(g2, out extDef))
                            {
                                // Found by primary GUID
                            }

                            if (extDef == null)
                            {
                                familyFailed++;
                                failedParams++;
                                continue;
                            }

                            try
                            {
                                // Determine instance vs type
                                bool isInstance = entry.bindingType.Equals(
                                    "Instance", StringComparison.OrdinalIgnoreCase);

                                fmgr.AddParameter(extDef, GroupTypeId.General, isInstance);
                                familyAdded++;
                                paramsAdded++;
                                existingParams.Add(paramName); // Track for formula application
                            }
                            catch (Exception ex)
                            {
                                StingLog.Warn($"[{fileName}] Failed to add '{paramName}': {ex.Message}");
                                familyFailed++;
                                failedParams++;
                            }
                        }

                        // ── Apply formulas ───────────────────────────────────
                        // Only apply formulas to parameters that now exist in the family
                        foreach (var paramName in existingParams)
                        {
                            if (!formulasByParam.TryGetValue(paramName, out string formula))
                                continue;

                            FamilyParameter famParam = null;
                            foreach (FamilyParameter fp in fmgr.Parameters)
                            {
                                if (fp.Definition?.Name != null &&
                                    fp.Definition.Name.Equals(paramName, StringComparison.OrdinalIgnoreCase))
                                {
                                    famParam = fp;
                                    break;
                                }
                            }

                            if (famParam == null) continue;

                            // Skip if formula already set
                            if (!string.IsNullOrEmpty(famParam.Formula)) continue;

                            try
                            {
                                fmgr.SetFormula(famParam, formula);
                                familyFormulas++;
                                formulasApplied++;
                            }
                            catch
                            {
                                // Formula may reference params not in this family — expected
                                failedFormulas++;
                            }
                        }

                        tx.Commit();
                    }

                    // ── Backup and save ──────────────────────────────────────
                    if (familyAdded > 0 || familyFormulas > 0)
                    {
                        // Create backup
                        string backupDir = Path.Combine(
                            Path.GetDirectoryName(familyPath), "_param_backups");
                        try
                        {
                            if (!Directory.Exists(backupDir))
                                Directory.CreateDirectory(backupDir);

                            string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                            string backupName = $"{Path.GetFileNameWithoutExtension(familyPath)}_{timestamp}.rfa";
                            string backupPath = Path.Combine(backupDir, backupName);
                            File.Copy(familyPath, backupPath, true);
                        }
                        catch (Exception ex)
                        {
                            StingLog.Warn($"[{fileName}] Backup failed: {ex.Message}");
                        }

                        // Save modified family
                        var saveOpts = new SaveAsOptions { OverwriteExistingFile = true };
                        famDoc.SaveAs(familyPath, saveOpts);
                        processed++;

                        perFamilyResults.Add(
                            $"[OK] {fileName} ({categoryName}) — " +
                            $"{familyAdded} params added, {familyFormulas} formulas applied" +
                            (familySkipped > 0 ? $", {familySkipped} already existed" : "") +
                            (familyFailed > 0 ? $", {familyFailed} failed" : ""));
                    }
                    else
                    {
                        perFamilyResults.Add(
                            $"[--] {fileName} ({categoryName}) — " +
                            $"no changes needed ({familySkipped} params already exist)");
                    }

                    famDoc.Close(false);
                }
                catch (Exception ex)
                {
                    StingLog.Error($"[{fileName}] Processing failed", ex);
                    perFamilyResults.Add($"[FAIL] {fileName} — {ex.Message}");
                    try { famDoc?.Close(false); } catch { }
                }
            }

            // Restore previous shared parameter file
            if (!string.IsNullOrEmpty(previousSpf))
                app.SharedParametersFilename = previousSpf;

            // ── Step 5: Report ──────────────────────────────────────────────
            var report = new StringBuilder();
            report.AppendLine("Family Parameter Processor");
            report.AppendLine(new string('\u2550', 50));
            report.AppendLine($"\nFamilies processed: {processed} of {totalFamilies}");
            report.AppendLine($"Parameters added: {paramsAdded}");
            report.AppendLine($"Formulas applied: {formulasApplied}");
            report.AppendLine($"Already existed (skipped): {skippedExisting}");
            report.AppendLine($"No category match: {skippedNoCategory}");
            if (failedParams > 0)
                report.AppendLine($"Failed parameters: {failedParams}");
            if (failedFormulas > 0)
                report.AppendLine($"Failed formulas: {failedFormulas} (expected — some reference params not in family)");

            report.AppendLine($"\nPer-family results:");
            foreach (string r in perFamilyResults)
                report.AppendLine($"  {r}");

            TaskDialog.Show("Family Parameter Processor", report.ToString());
            StingLog.Info($"Family Processor: {processed} families, {paramsAdded} params, {formulasApplied} formulas");

            return processed > 0 ? Result.Succeeded : Result.Failed;
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  CreateTemplateSchedulesCommand — TPL metadata schedules from CSV
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Creates the 13 TPL_Schedule_Metadata schedules from MR_SCHEDULES.csv.
    /// These track template configuration metadata: filters, styles, phases,
    /// worksets, and audit trail data.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class CreateTemplateSchedulesCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }
            Document doc = ctx.Doc;

            string csvPath = StingToolsApp.FindDataFile("MR_SCHEDULES.csv");
            if (string.IsNullOrEmpty(csvPath))
            {
                TaskDialog.Show("Template Schedules", "MR_SCHEDULES.csv not found.");
                return Result.Failed;
            }

            var tplEntries = new List<(string scheduleName, string category, string fields)>();
            try
            {
                foreach (string line in File.ReadAllLines(csvPath))
                {
                    if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#")) continue;
                    string[] cols = StingToolsApp.ParseCsvLine(line);
                    if (cols.Length < 7) continue;
                    if (cols[0].Trim() != "TPL_Schedule_Metadata") continue;

                    string schedName = cols[2].Trim();
                    string category = cols[3].Trim();
                    string fields = cols[6].Trim();

                    if (!string.IsNullOrEmpty(schedName) && !string.IsNullOrEmpty(fields))
                        tplEntries.Add((schedName, category, fields));
                }
            }
            catch (Exception ex)
            {
                StingLog.Error("Failed to read MR_SCHEDULES.csv", ex);
                TaskDialog.Show("Template Schedules", $"Error reading CSV: {ex.Message}");
                return Result.Failed;
            }

            if (tplEntries.Count == 0)
            {
                TaskDialog.Show("Template Schedules",
                    "No TPL_Schedule_Metadata entries found in MR_SCHEDULES.csv.");
                return Result.Failed;
            }

            var existing = new HashSet<string>(
                new FilteredElementCollector(doc)
                    .OfClass(typeof(ViewSchedule)).Select(e => e.Name));

            int created = 0, skipped = 0;

            using (Transaction tx = new Transaction(doc, "STING Create Template Schedules"))
            {
                tx.Start();
                foreach (var (schedName, category, fields) in tplEntries)
                {
                    string fullName = $"STING - {schedName}";
                    if (existing.Contains(fullName)) { skipped++; continue; }

                    BuiltInCategory bic = BuiltInCategory.OST_GenericModel;
                    if (TemplateManager.CategoryNameToEnum.TryGetValue(category, out BuiltInCategory mapped))
                        bic = mapped;

                    try
                    {
                        ViewSchedule vs = ViewSchedule.CreateSchedule(doc, new ElementId(bic));
                        vs.Name = fullName;

                        string[] fieldNames = fields.Split(',')
                            .Select(f => f.Trim()).Where(f => f.Length > 0).ToArray();
                        ScheduleDefinition sd = vs.Definition;
                        foreach (string fieldName in fieldNames)
                        {
                            try
                            {
                                foreach (SchedulableField sf in sd.GetSchedulableFields())
                                {
                                    if (string.Equals(sf.GetName(doc), fieldName,
                                        StringComparison.OrdinalIgnoreCase))
                                    { sd.AddField(sf); break; }
                                }
                            }
                            catch { }
                        }
                        created++;
                    }
                    catch (Exception ex)
                    {
                        StingLog.Warn($"Template schedule '{fullName}': {ex.Message}");
                        skipped++;
                    }
                }
                tx.Commit();
            }

            TaskDialog.Show("Template Schedules",
                $"Created {created} template metadata schedules.\nSkipped {skipped}.\n" +
                $"TPL entries in CSV: {tplEntries.Count}");
            return Result.Succeeded;
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  TemplateSetupWizardCommand — one-click complete template automation
    //  Enhanced with 15 steps including batch family params + auto-fix
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Executes the complete STING template setup sequence in one click:
    ///   1. Fill Patterns (12 ISO patterns)
    ///   2. Line Patterns (10 ISO 128 patterns)
    ///   3. Line Styles (16 discipline + status + reference)
    ///   4. Object Styles (40 category overrides)
    ///   5. Text Styles (12 text note types)
    ///   6. Dimension Styles (7 dimension types)
    ///   7. View Filters (28+ discipline + system + QA)
    ///   8. View Templates (23 templates with VG)
    ///   9. Apply Filters to Templates
    ///  10. VG Overrides (5-layer intelligence)
    ///  11. Batch Family Parameters (4,686 bindings from CSV)
    ///  12. Template Metadata Schedules (13 from CSV)
    ///  13. Worksets (35 ISO 19650, if workshared)
    ///  14. Auto-Assign Templates (5-layer matching)
    ///  15. Auto-Fix + Compliance Audit
    ///
    /// Each step runs in its own transaction.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class TemplateSetupWizardCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            TaskDialog confirm = new TaskDialog("STING Template Setup Wizard");
            confirm.MainInstruction = "Run complete template setup?";
            confirm.MainContent =
                "This will execute the full STING template automation:\n\n" +
                "  1.  Fill Patterns (12 ISO patterns)\n" +
                "  2.  Line Patterns (10 ISO 128 patterns)\n" +
                "  3.  Line Styles (16 styles)\n" +
                "  4.  Object Styles (40 category overrides)\n" +
                "  5.  Text Styles (12 text note types)\n" +
                "  6.  Dimension Styles (7 types)\n" +
                "  7.  View Filters (28+ with parameter rules)\n" +
                "  8.  View Templates (23 with VG overrides)\n" +
                "  9.  Apply Filters to Templates\n" +
                " 10.  Apply VG Overrides (5 intelligence layers)\n" +
                " 11.  Batch Family Parameters (4,686 bindings)\n" +
                " 12.  Template Metadata Schedules (13 from CSV)\n" +
                " 13.  Worksets (35 ISO 19650, if workshared)\n" +
                " 14.  Auto-Assign Templates (5-layer matching)\n" +
                " 15.  Auto-Fix + Final Audit\n\n" +
                "Each step runs independently.\n" +
                "This may take 1-2 minutes.";
            confirm.CommonButtons = TaskDialogCommonButtons.Ok | TaskDialogCommonButtons.Cancel;
            if (confirm.Show() == TaskDialogResult.Cancel)
                return Result.Cancelled;

            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }
            Document doc = ctx.Doc;
            StingLog.Info("Template Setup Wizard: starting 15-step automation");
            var report = new StringBuilder();
            report.AppendLine("STING Template Setup Wizard Results");
            report.AppendLine(new string('═', 55));

            int stepNum = 0;
            int passed = 0;
            var totalSw = Stopwatch.StartNew();

                // Step 1: Fill Patterns
                passed += TemplateManager.RunWizardStep(ref stepNum, report,
                    "Fill Patterns (12 ISO)",
                    () => RunCommand(new CreateFillPatternsCommand(), commandData, elements));

                // Step 2: Line Patterns
                passed += TemplateManager.RunWizardStep(ref stepNum, report,
                    "Line Patterns (10 ISO 128)",
                    () => RunCommand(new CreateLinePatternsCommand(), commandData, elements));

                // Step 3: Line Styles
                passed += TemplateManager.RunWizardStep(ref stepNum, report,
                    "Line Styles (16 discipline+status)",
                    () => RunCommand(new CreateLineStylesCommand(), commandData, elements));

                // Step 4: Object Styles
                passed += TemplateManager.RunWizardStep(ref stepNum, report,
                    "Object Styles (40 categories)",
                    () => RunCommand(new CreateObjectStylesCommand(), commandData, elements));

                // Step 5: Text Styles
                passed += TemplateManager.RunWizardStep(ref stepNum, report,
                    "Text Styles (12 ISO 3098)",
                    () => RunCommand(new CreateTextStylesCommand(), commandData, elements));

                // Step 6: Dimension Styles
                passed += TemplateManager.RunWizardStep(ref stepNum, report,
                    "Dimension Styles (7 types)",
                    () => RunCommand(new CreateDimensionStylesCommand(), commandData, elements));

                // Step 7: View Filters
                passed += TemplateManager.RunWizardStep(ref stepNum, report,
                    "View Filters (28+ with param rules)",
                    () => RunCommand(new CreateFiltersCommand(), commandData, elements));

                // Step 8: View Templates
                passed += TemplateManager.RunWizardStep(ref stepNum, report,
                    "View Templates (23 with VG)",
                    () => RunCommand(new ViewTemplatesCommand(), commandData, elements));

                // Step 9: Apply Filters
                passed += TemplateManager.RunWizardStep(ref stepNum, report,
                    "Apply Filters to Templates",
                    () => RunCommand(new ApplyFiltersToViewsCommand(), commandData, elements));

                // Step 10: VG Overrides
                passed += TemplateManager.RunWizardStep(ref stepNum, report,
                    "VG Overrides (5-layer intelligence)",
                    () => RunCommand(new CreateVGOverridesCommand(), commandData, elements));

                // Step 11: Batch Family Parameters
                passed += TemplateManager.RunWizardStep(ref stepNum, report,
                    "Batch Family Parameters (CSV-driven)",
                    () => RunCommand(new BatchAddFamilyParamsCommand(), commandData, elements));

                // Step 12: Template Metadata Schedules
                passed += TemplateManager.RunWizardStep(ref stepNum, report,
                    "Template Metadata Schedules",
                    () => RunCommand(new CreateTemplateSchedulesCommand(), commandData, elements));

                // Step 13: Worksets
                if (doc.IsWorkshared)
                {
                    passed += TemplateManager.RunWizardStep(ref stepNum, report,
                        "Worksets (35 ISO 19650)",
                        () => RunCommand(new CreateWorksetsCommand(), commandData, elements));
                }
                else
                {
                    stepNum++;
                    report.AppendLine($"  {stepNum,2}. Worksets — SKIPPED (not workshared)");
                }

                // Step 14: Auto-Assign Templates
                passed += TemplateManager.RunWizardStep(ref stepNum, report,
                    "Auto-Assign Templates (5-layer)",
                    () => RunCommand(new AutoAssignTemplatesCommand(), commandData, elements));

                // Step 15: Auto-Fix
                passed += TemplateManager.RunWizardStep(ref stepNum, report,
                    "Auto-Fix Template Health",
                    () => RunCommand(new AutoFixTemplateCommand(), commandData, elements));

                int failed = stepNum - passed;
                totalSw.Stop();

                if (failed > 0)
                {
                    report.AppendLine(new string('─', 55));
                    report.AppendLine($"  {passed}/{stepNum} succeeded, {failed} failed");
                    report.AppendLine($"  Duration: {totalSw.Elapsed.TotalSeconds:F1}s");
                    report.AppendLine("  Use Ctrl+Z to undo individual steps if needed.");
                }

            report.AppendLine(new string('─', 55));
            report.AppendLine($"  Complete: {passed}/{stepNum} steps succeeded");
            report.AppendLine($"  Duration: {totalSw.Elapsed.TotalSeconds:F1}s");

            TaskDialog td = new TaskDialog("STING Template Setup Wizard");
            td.MainInstruction = $"Template Setup: {passed}/{stepNum} steps complete";
            td.MainContent = report.ToString();
            td.Show();

            StingLog.Info($"Template Wizard complete: {passed}/{stepNum} passed, " +
                $"elapsed={totalSw.Elapsed.TotalSeconds:F1}s");

            return passed > 0 ? Result.Succeeded : Result.Failed;
        }

        /// <summary>Execute an IExternalCommand without capturing ref message in a lambda.</summary>
        private static Result RunCommand(IExternalCommand cmd,
            ExternalCommandData data, ElementSet elems)
        {
            string msg = "";
            return cmd.Execute(data, ref msg, elems);
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  CloneTemplateCommand — duplicate with intelligent discipline detection
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Clones an existing STING view template with intelligent discipline
    /// detection and auto-configuration. The clone process:
    ///
    ///   1. User selects a source template (STING or non-STING)
    ///   2. Engine detects discipline from source name/filters/VG overrides
    ///   3. User picks target discipline (auto-suggested) or custom name
    ///   4. Clone is created with:
    ///      • New discipline-appropriate name ("STING - {Discipline} Plan")
    ///      • VG overrides re-mapped to target discipline colours
    ///      • Correct filters applied for the target discipline
    ///      • Detail level preserved or upgraded per discipline rules
    ///   5. Clone registered in project, ready for assignment
    ///
    /// Intelligence layers:
    ///   Layer 1: Source discipline detected from template name patterns
    ///   Layer 2: Fallback detection from applied filters (which discipline filters are visible)
    ///   Layer 3: Fallback detection from VG overrides (which colour palette is active)
    ///   Layer 4: User override via TaskDialog discipline picker
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class CloneTemplateCommand : IExternalCommand
    {
        /// <summary>Discipline options for the user picker.</summary>
        private static readonly (string code, string label)[] DisciplineOptions =
        {
            ("M", "Mechanical"), ("E", "Electrical"), ("P", "Plumbing"),
            ("A", "Architectural"), ("S", "Structural"), ("FP", "Fire Protection"),
            ("LV", "Low Voltage"), ("MEP", "MEP Coordination"), ("ALL", "Combined Services"),
            ("DEMO", "Demolition"), ("EXIST", "As-Built"), ("AREA", "Area Plan"),
        };

        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }
            Document doc = ctx.Doc;

            // Collect all view templates
            var allTemplates = new FilteredElementCollector(doc)
                .OfClass(typeof(View)).Cast<View>()
                .Where(v => v.IsTemplate)
                .OrderBy(v => v.Name)
                .ToList();

            if (allTemplates.Count == 0)
            {
                TaskDialog.Show("Clone Template", "No view templates found in project.");
                return Result.Succeeded;
            }

            // Let user pick a source template via TaskDialog with command links
            // Show up to 8 STING templates + "Other" option
            var stingTmpl = allTemplates.Where(v => v.Name.StartsWith("STING")).Take(8).ToList();
            var otherTmpl = allTemplates.Where(v => !v.Name.StartsWith("STING")).ToList();

            TaskDialog pickDlg = new TaskDialog("Clone Template — Select Source");
            pickDlg.MainInstruction = "Select source template to clone";
            pickDlg.MainContent = $"Found {stingTmpl.Count} STING templates, " +
                $"{otherTmpl.Count} other templates.\n\n" +
                "The clone will be created with intelligent VG re-configuration.\n" +
                "Select a STING template below or cancel to abort.";

            if (stingTmpl.Count >= 1)
                pickDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink1,
                    stingTmpl[0].Name, "Clone this template");
            if (stingTmpl.Count >= 2)
                pickDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink2,
                    stingTmpl[1].Name, "Clone this template");
            if (stingTmpl.Count >= 3)
                pickDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink3,
                    stingTmpl[2].Name, "Clone this template");
            if (stingTmpl.Count >= 4)
                pickDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink4,
                    stingTmpl[3].Name, "Clone this template");

            pickDlg.CommonButtons = TaskDialogCommonButtons.Cancel;
            TaskDialogResult pickResult = pickDlg.Show();
            if (pickResult == TaskDialogResult.Cancel) return Result.Cancelled;

            View sourceTemplate = null;
            if (pickResult == TaskDialogResult.CommandLink1 && stingTmpl.Count >= 1)
                sourceTemplate = stingTmpl[0];
            else if (pickResult == TaskDialogResult.CommandLink2 && stingTmpl.Count >= 2)
                sourceTemplate = stingTmpl[1];
            else if (pickResult == TaskDialogResult.CommandLink3 && stingTmpl.Count >= 3)
                sourceTemplate = stingTmpl[2];
            else if (pickResult == TaskDialogResult.CommandLink4 && stingTmpl.Count >= 4)
                sourceTemplate = stingTmpl[3];

            if (sourceTemplate == null)
            {
                TaskDialog.Show("Clone Template", "No template selected.");
                return Result.Cancelled;
            }

            // Layer 1: Detect source discipline from name
            string detectedDisc = TemplateManager.GetDisciplineFromTemplateName(sourceTemplate.Name);

            // Layer 2: Fallback — detect from applied filters
            if (detectedDisc == null)
            {
                var appliedFilters = sourceTemplate.GetFilters();
                foreach (ElementId fid in appliedFilters)
                {
                    var filterEl = doc.GetElement(fid);
                    if (filterEl == null) continue;
                    string fn = filterEl.Name;
                    if (fn.Contains("Mechanical")) { detectedDisc = "M"; break; }
                    if (fn.Contains("Electrical")) { detectedDisc = "E"; break; }
                    if (fn.Contains("Plumbing")) { detectedDisc = "P"; break; }
                    if (fn.Contains("Architectural")) { detectedDisc = "A"; break; }
                    if (fn.Contains("Structural")) { detectedDisc = "S"; break; }
                    if (fn.Contains("Fire Protection")) { detectedDisc = "FP"; break; }
                    if (fn.Contains("Low Voltage")) { detectedDisc = "LV"; break; }
                }
            }

            // Layer 3: Fallback — detect from VG override colours
            if (detectedDisc == null)
            {
                foreach (ElementId fid in sourceTemplate.GetFilters())
                {
                    try
                    {
                        var ogs = sourceTemplate.GetFilterOverrides(fid);
                        Color lc = ogs.ProjectionLineColor;
                        if (!lc.IsValid) continue;

                        if (lc.Blue > 200 && lc.Red < 50) { detectedDisc = "M"; break; }
                        if (lc.Red > 200 && lc.Green > 150) { detectedDisc = "E"; break; }
                        if (lc.Green > 150 && lc.Red < 50 && lc.Blue < 50) { detectedDisc = "P"; break; }
                        if (lc.Red > 150 && lc.Green < 50 && lc.Blue < 50) { detectedDisc = "S"; break; }
                    }
                    catch { }
                }
            }

            string detectedLabel = detectedDisc ?? "Unknown";
            foreach (var (code, label) in DisciplineOptions)
            {
                if (code == detectedDisc) { detectedLabel = label; break; }
            }

            // Layer 4: User confirms or overrides discipline
            TaskDialog discDlg = new TaskDialog("Clone Template — Target Discipline");
            discDlg.MainInstruction = "Select target discipline for clone";
            discDlg.MainContent =
                $"Source: {sourceTemplate.Name}\n" +
                $"Detected discipline: {detectedLabel} ({detectedDisc ?? "?"})\n\n" +
                "Pick target discipline for the cloned template:";

            discDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink1,
                $"Same as source ({detectedLabel})",
                $"Clone with {detectedLabel} VG configuration");
            discDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink2,
                "Mechanical (M)", "Blue discipline colour, HVAC focus");
            discDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink3,
                "Electrical (E)", "Yellow discipline colour, power/lighting focus");
            discDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink4,
                "Plumbing (P)", "Green discipline colour, pipework focus");
            discDlg.CommonButtons = TaskDialogCommonButtons.Cancel;

            TaskDialogResult discResult = discDlg.Show();
            if (discResult == TaskDialogResult.Cancel) return Result.Cancelled;

            string targetDisc = detectedDisc ?? "A";
            string targetLabel = detectedLabel;
            if (discResult == TaskDialogResult.CommandLink2) { targetDisc = "M"; targetLabel = "Mechanical"; }
            else if (discResult == TaskDialogResult.CommandLink3) { targetDisc = "E"; targetLabel = "Electrical"; }
            else if (discResult == TaskDialogResult.CommandLink4) { targetDisc = "P"; targetLabel = "Plumbing"; }

            // Build clone name
            string viewTypeStr = "Plan";
            try
            {
                if (sourceTemplate.ViewType == ViewType.CeilingPlan) viewTypeStr = "RCP";
                else if (sourceTemplate.ViewType == ViewType.Section) viewTypeStr = "Section";
                else if (sourceTemplate.ViewType == ViewType.ThreeD) viewTypeStr = "3D";
                else if (sourceTemplate.ViewType == ViewType.Elevation) viewTypeStr = "Elevation";
            }
            catch (Exception ex) { StingLog.Warn($"CloneTemplate: could not read ViewType: {ex.Message}"); }

            string cloneName = $"STING - {targetLabel} {viewTypeStr} (Clone)";

            // Check for name collision
            var existingNames = new HashSet<string>(
                allTemplates.Select(t => t.Name), StringComparer.OrdinalIgnoreCase);
            int suffix = 2;
            string baseName = cloneName;
            while (existingNames.Contains(cloneName))
            {
                cloneName = $"{baseName} {suffix}";
                suffix++;
            }

            // Create the clone
            var filterLookup = new Dictionary<string, ParameterFilterElement>();
            foreach (ParameterFilterElement pfe in new FilteredElementCollector(doc)
                .OfClass(typeof(ParameterFilterElement)).Cast<ParameterFilterElement>())
                filterLookup[pfe.Name] = pfe;

            FillPatternElement solidFill = null;
            try
            {
                solidFill = new FilteredElementCollector(doc)
                    .OfClass(typeof(FillPatternElement)).Cast<FillPatternElement>()
                    .FirstOrDefault(fp => fp.GetFillPattern().IsSolidFill);
            }
            catch { }

            using (Transaction tx = new Transaction(doc, "STING Clone Template"))
            {
                tx.Start();

                try
                {
                    // Duplicate the source template
                    ElementId cloneId = sourceTemplate.Duplicate(ViewDuplicateOption.Duplicate);
                    View clone = doc.GetElement(cloneId) as View;
                    if (clone == null)
                    {
                        tx.RollBack();
                        TaskDialog.Show("Clone Template", "Failed to duplicate template.");
                        return Result.Failed;
                    }

                    clone.Name = cloneName;

                    // Re-configure VG for target discipline
                    ViewDetailLevel dl = targetDisc.StartsWith("PRES") ||
                        targetDisc == "ELEV_P" || targetDisc == "SEC_P" || targetDisc == "PRES_3D"
                        ? ViewDetailLevel.Fine : ViewDetailLevel.Medium;

                    ViewTemplatesCommand.ConfigureTemplateVG(clone, targetDisc,
                        filterLookup, solidFill, dl);

                    tx.Commit();

                    TaskDialog.Show("Clone Template",
                        $"Template cloned successfully.\n\n" +
                        $"Source: {sourceTemplate.Name}\n" +
                        $"Clone: {cloneName}\n" +
                        $"Discipline: {targetLabel} ({targetDisc})\n" +
                        $"Detail level: {dl}\n\n" +
                        "VG overrides re-configured for target discipline.\n" +
                        "Use 'Auto-Assign Templates' to apply to views.");

                    StingLog.Info($"Clone Template: '{sourceTemplate.Name}' → " +
                        $"'{cloneName}' (disc={targetDisc})");
                }
                catch (Exception ex)
                {
                    if (tx.HasStarted()) tx.RollBack();
                    StingLog.Error("Clone Template failed", ex);
                    TaskDialog.Show("Clone Template", $"Clone failed: {ex.Message}");
                    return Result.Failed;
                }
            }

            return Result.Succeeded;
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  BatchVGResetCommand — bulk VG standardisation across all views
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Resets and standardises VG overrides across all views in the project.
    /// Three operating modes:
    ///
    ///   Mode 1 — Reset to Template: Clears all per-element VG overrides
    ///     in views that have a STING template, forcing the template VG to
    ///     take full control. Removes rogue manual colour changes.
    ///
    ///   Mode 2 — Standardise Templates: Re-applies STING standard VG to
    ///     all STING templates (calls SyncTemplateOverrides internally).
    ///     Repairs templates that drifted from the standard.
    ///
    ///   Mode 3 — Full Reset: Combines Mode 1 + Mode 2: first fixes all
    ///     templates, then clears per-element overrides in all views.
    ///
    /// Intelligence:
    ///   • Counts per-element overrides before clearing (shows impact)
    ///   • Groups views by template for efficient batch processing
    ///   • Skips views on sheets (preserves print-ready formatting)
    ///   • Reports per-view override counts and total cleared
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class BatchVGResetCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }
            Document doc = ctx.Doc;

            // Mode selection
            TaskDialog modeDlg = new TaskDialog("Batch VG Reset");
            modeDlg.MainInstruction = "Select VG standardisation mode";
            modeDlg.MainContent =
                "Choose how to standardise Visibility/Graphics across the project:\n\n" +
                "Mode 1 — Clear per-element overrides in views with STING templates\n" +
                "  (removes manual colour changes, forces template VG control)\n\n" +
                "Mode 2 — Re-apply STING standard VG to all STING templates\n" +
                "  (repairs templates that drifted from standard)\n\n" +
                "Mode 3 — Full reset (Mode 2 + Mode 1)\n" +
                "  (fix templates first, then clear element overrides)";

            modeDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink1,
                "Mode 1: Clear Element Overrides",
                "Remove per-element colour/weight overrides in views with STING templates");
            modeDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink2,
                "Mode 2: Standardise Templates",
                "Re-apply STING VG standard to all STING view templates");
            modeDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink3,
                "Mode 3: Full Reset (Recommended)",
                "Fix templates + clear element overrides for complete standardisation");
            modeDlg.CommonButtons = TaskDialogCommonButtons.Cancel;

            TaskDialogResult modeResult = modeDlg.Show();
            if (modeResult == TaskDialogResult.Cancel) return Result.Cancelled;

            bool doTemplates = modeResult == TaskDialogResult.CommandLink2 ||
                               modeResult == TaskDialogResult.CommandLink3;
            bool doElements = modeResult == TaskDialogResult.CommandLink1 ||
                              modeResult == TaskDialogResult.CommandLink3;

            var report = new StringBuilder();
            report.AppendLine("STING Batch VG Reset Report");
            report.AppendLine(new string('═', 55));

            int templatesSynced = 0;
            int viewsReset = 0;
            int elementsCleared = 0;

            // Phase 1: Standardise STING templates
            if (doTemplates)
            {
                report.AppendLine("\nPhase 1: Standardise Templates");

                var filterLookup = new Dictionary<string, ParameterFilterElement>();
                foreach (ParameterFilterElement pfe in new FilteredElementCollector(doc)
                    .OfClass(typeof(ParameterFilterElement)).Cast<ParameterFilterElement>())
                    filterLookup[pfe.Name] = pfe;

                FillPatternElement solidFill = null;
                try
                {
                    solidFill = new FilteredElementCollector(doc)
                    .OfClass(typeof(FillPatternElement)).Cast<FillPatternElement>()
                    .FirstOrDefault(fp => fp.GetFillPattern().IsSolidFill);
                }
                catch { }

                var stingTemplates = TemplateManager.GetStingTemplates(doc);

                using (Transaction tx = new Transaction(doc, "STING Standardise Template VG"))
                {
                    tx.Start();
                    foreach (var kvp in stingTemplates)
                    {
                        string disc = TemplateManager.GetDisciplineFromTemplateName(kvp.Key);
                        if (disc == null) continue;

                        ViewDetailLevel dl = disc.StartsWith("PRES") ||
                            disc == "ELEV_P" || disc == "SEC_P" || disc == "PRES_3D"
                            ? ViewDetailLevel.Fine : ViewDetailLevel.Medium;

                        try
                        {
                            ViewTemplatesCommand.ConfigureTemplateVG(
                                kvp.Value, disc, filterLookup, solidFill, dl);
                            templatesSynced++;
                            report.AppendLine($"  {kvp.Key} — synced ({disc})");
                        }
                        catch (Exception ex)
                        {
                            report.AppendLine($"  {kvp.Key} — FAILED: {ex.Message}");
                        }
                    }
                    tx.Commit();
                }
            }

            // Phase 2: Clear per-element overrides
            if (doElements)
            {
                report.AppendLine("\nPhase 2: Clear Element Overrides");

                var viewsWithSting = TemplateManager.GetAssignableViews(doc)
                    .Where(v =>
                    {
                        if (v.ViewTemplateId == ElementId.InvalidElementId) return false;
                        var tmpl = doc.GetElement(v.ViewTemplateId) as View;
                        return tmpl != null && tmpl.Name.StartsWith("STING");
                    }).ToList();

                // Skip views placed on sheets (preserve print formatting)
                var sheetsViews = new HashSet<ElementId>();
                foreach (ViewSheet sheet in new FilteredElementCollector(doc)
                    .OfClass(typeof(ViewSheet)).Cast<ViewSheet>())
                {
                    foreach (ElementId vpId in sheet.GetAllPlacedViews())
                        sheetsViews.Add(vpId);
                }

                var resetTargets = viewsWithSting
                    .Where(v => !sheetsViews.Contains(v.Id)).ToList();

                int skippedOnSheets = viewsWithSting.Count - resetTargets.Count;
                if (skippedOnSheets > 0)
                    report.AppendLine($"  Skipping {skippedOnSheets} views on sheets");

                using (Transaction tx = new Transaction(doc, "STING Clear Element Overrides"))
                {
                    tx.Start();

                    foreach (View view in resetTargets)
                    {
                        try
                        {
                            // Collect all elements in view that have overrides
                            var collector = new FilteredElementCollector(doc, view.Id);
                            int cleared = 0;
                            var defaultOgs = new OverrideGraphicSettings();

                            foreach (Element el in collector)
                            {
                                try
                                {
                                    var currentOgs = view.GetElementOverrides(el.Id);
                                    // Check if element has any non-default overrides
                                    bool hasOverride =
                                        currentOgs.ProjectionLineColor.IsValid ||
                                        currentOgs.Halftone ||
                                        currentOgs.Transparency > 0 ||
                                        currentOgs.ProjectionLineWeight > 0;

                                    if (hasOverride)
                                    {
                                        view.SetElementOverrides(el.Id, defaultOgs);
                                        cleared++;
                                    }
                                }
                                catch { }
                            }

                            if (cleared > 0)
                            {
                                report.AppendLine($"  {view.Name} — {cleared} overrides cleared");
                                elementsCleared += cleared;
                                viewsReset++;
                            }
                        }
                        catch (Exception ex)
                        {
                            StingLog.Warn($"VG reset '{view.Name}': {ex.Message}");
                        }
                    }

                    tx.Commit();
                }
            }

            report.AppendLine($"\n{new string('─', 55)}");
            if (doTemplates)
                report.AppendLine($"  Templates standardised: {templatesSynced}");
            if (doElements)
            {
                report.AppendLine($"  Views reset: {viewsReset}");
                report.AppendLine($"  Element overrides cleared: {elementsCleared}");
            }

            TaskDialog.Show("Batch VG Reset", report.ToString());
            StingLog.Info($"Batch VG Reset: {templatesSynced} templates synced, " +
                $"{viewsReset} views reset, {elementsCleared} overrides cleared");

            return Result.Succeeded;
        }
    }
}
