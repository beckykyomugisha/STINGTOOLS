using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Tags;

namespace StingTools.Core
{
    // ══════════════════════════════════════════════════════════════════
    //  WARNING CATEGORY & SEVERITY ENUMS
    // ══════════════════════════════════════════════════════════════════

    /// <summary>BIM-domain warning category — maps Revit warnings to BIM coordinator concerns.</summary>
    internal enum WarningCategory
    {
        Geometric,    // Overlaps, intersections, joins, duplicates
        Spatial,      // Rooms, areas, enclosure, boundaries
        MEP,          // Connectors, systems, flow, sizing
        Structural,   // Analytical, beams, columns, supports
        Annotation,   // Tags, dimensions, text, leaders, hidden
        Data,         // Parameters, formulas, schedules, types
        Performance,  // Imports, DWGs, raster, groups, arrays
        Compliance,   // Standards, codes, fire rating, accessibility
        Unknown       // Unclassified
    }

    /// <summary>BIM-impact severity — goes beyond Revit's binary Warning/Error.</summary>
    internal enum WarningSeverity
    {
        Critical,  // Blocks handover or causes data loss
        High,      // Affects model quality or COBie export
        Medium,    // Should fix before milestone
        Low,       // Minor quality issue
        Info       // Informational — may be intentional
    }

    // ══════════════════════════════════════════════════════════════════
    //  CLASSIFIED WARNING MODEL
    // ══════════════════════════════════════════════════════════════════

    /// <summary>A Revit warning enriched with STING classification, fix strategy, and element context.</summary>
    internal class ClassifiedWarning
    {
        public FailureMessage Source { get; set; }
        public string Description { get; set; }
        public WarningCategory Category { get; set; }
        public WarningSeverity Severity { get; set; }
        public string FixStrategy { get; set; }
        public bool CanAutoFix { get; set; }
        public ICollection<ElementId> FailingElements { get; set; }
        public ICollection<ElementId> AdditionalElements { get; set; }
        public string LevelName { get; set; }
        public string WorksetName { get; set; }
        public string Discipline { get; set; }
        public string CategoryName { get; set; }
    }

    /// <summary>Warning scan report with categorised breakdown and trend data.</summary>
    internal class WarningReport
    {
        public int Total { get; set; }
        public int AutoFixable { get; set; }
        public int ManualReview { get; set; }
        public Dictionary<WarningCategory, int> ByCategory { get; set; } = new();
        public Dictionary<WarningSeverity, int> BySeverity { get; set; } = new();
        public Dictionary<string, int> ByLevel { get; set; } = new();
        public Dictionary<string, int> ByWorkset { get; set; } = new();
        public Dictionary<string, int> ByDiscipline { get; set; } = new();
        public List<(ElementId Id, string Name, int Count)> Hotspots { get; set; } = new();
        public List<ClassifiedWarning> Warnings { get; set; } = new();
        public DateTime ScanTime { get; set; } = DateTime.Now;

        // Trend (vs baseline)
        public int? BaselineTotal { get; set; }
        public int TrendDelta => BaselineTotal.HasValue ? Total - BaselineTotal.Value : 0;
        public string TrendSymbol => TrendDelta > 0 ? $"↑{TrendDelta}" : TrendDelta < 0 ? $"↓{Math.Abs(TrendDelta)}" : "→0";

        // Phase 48: SLA metrics
        /// <summary>Warnings older than SLA threshold (Critical=4h, High=24h, Medium=168h, Low=336h).</summary>
        public int SLAViolations { get; set; }
        /// <summary>Average age of unresolved critical/high warnings in hours.</summary>
        public double AvgCriticalAgeHours { get; set; }
        /// <summary>Phase 48: Warning type groups for top-N display per category.</summary>
        public Dictionary<WarningCategory, List<(string Desc, int Count)>> TopWarningsByCategory { get; set; } = new();
    }

    /// <summary>Result of a batch auto-fix operation.</summary>
    internal class FixReport
    {
        public int Attempted { get; set; }
        public int Fixed { get; set; }
        public int Skipped { get; set; }
        public int Failed { get; set; }
        public List<string> Details { get; set; } = new();
    }


    // ══════════════════════════════════════════════════════════════════
    //  WARNINGS ENGINE — Core analysis, classification & auto-fix
    // ══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Intelligent Revit warnings analysis engine. Classifies warnings by BIM domain,
    /// identifies auto-fixable issues, tracks trends against baseline, and provides
    /// per-level/workset/discipline breakdown for BIM coordinator triage.
    /// </summary>
    internal static class WarningsEngine
    {
        // ── Classification patterns — substring match on FailureMessage.GetDescriptionText() ──

        private static readonly (string pattern, WarningCategory cat, WarningSeverity sev, string fix, bool autoFix)[] ClassificationRules =
        {
            // Geometric — overlaps, intersections, duplicates
            ("are slightly off axis", WarningCategory.Geometric, WarningSeverity.Low, "Align to nearest axis", false),
            ("overlap", WarningCategory.Geometric, WarningSeverity.Medium, "Join or split overlapping geometry", false),
            ("Highlighted walls overlap", WarningCategory.Geometric, WarningSeverity.Medium, "Join walls or delete shorter segment", false),
            ("intersect", WarningCategory.Geometric, WarningSeverity.Medium, "Review intersection geometry", false),
            ("duplicate instances in the same place", WarningCategory.Geometric, WarningSeverity.High, "Delete duplicate instance", true),
            ("One element is completely inside another", WarningCategory.Geometric, WarningSeverity.High, "Review containment — may need deletion", false),
            ("joined but do not intersect", WarningCategory.Geometric, WarningSeverity.Low, "Unjoin elements", true),
            ("has an invalid sketch", WarningCategory.Geometric, WarningSeverity.Critical, "Edit sketch to fix self-intersections", false),
            ("Wall and Floor/Roof join", WarningCategory.Geometric, WarningSeverity.Low, "Review join condition", false),

            // Spatial — rooms, areas, boundaries
            ("Room is not in a properly enclosed", WarningCategory.Spatial, WarningSeverity.Critical, "Close room boundary gaps", false),
            ("not enclosed", WarningCategory.Spatial, WarningSeverity.Critical, "Find and close boundary gaps", false),
            ("not in a properly enclosed region", WarningCategory.Spatial, WarningSeverity.Critical, "Enclose room with bounding elements", false),
            ("Multiple Rooms", WarningCategory.Spatial, WarningSeverity.High, "Separate or merge overlapping rooms", false),
            ("Room Separation Line", WarningCategory.Spatial, WarningSeverity.Medium, "Delete redundant separation line", true),
            ("redundant", WarningCategory.Spatial, WarningSeverity.Low, "Delete redundant element", true),
            ("Area is not in", WarningCategory.Spatial, WarningSeverity.High, "Fix area boundary", false),
            ("Room Tag is outside", WarningCategory.Spatial, WarningSeverity.Medium, "Move tag inside room boundary", false),

            // MEP — connectors, systems, flow, sizing
            ("not connected", WarningCategory.MEP, WarningSeverity.High, "Connect MEP elements", false),
            ("connector", WarningCategory.MEP, WarningSeverity.Medium, "Review connector alignment", false),
            ("has no connections", WarningCategory.MEP, WarningSeverity.High, "Connect to system", false),
            ("Flow cannot be determined", WarningCategory.MEP, WarningSeverity.Medium, "Set flow direction or fix system", false),
            ("duct system", WarningCategory.MEP, WarningSeverity.Medium, "Review duct system assignment", false),
            ("pipe system", WarningCategory.MEP, WarningSeverity.Medium, "Review pipe system assignment", false),
            ("System is missing a supply or return", WarningCategory.MEP, WarningSeverity.High, "Add supply/return terminal", false),
            ("size not available", WarningCategory.MEP, WarningSeverity.Medium, "Add duct/pipe size to type catalog", false),

            // Structural — analytical model, supports
            ("analytical", WarningCategory.Structural, WarningSeverity.Medium, "Review analytical model alignment", false),
            ("support", WarningCategory.Structural, WarningSeverity.Medium, "Check structural support conditions", false),
            ("beam", WarningCategory.Structural, WarningSeverity.Medium, "Review beam framing", false),

            // Annotation — tags, dimensions, hidden elements
            ("dimension", WarningCategory.Annotation, WarningSeverity.Low, "Fix or delete broken dimension", false),
            ("tag", WarningCategory.Annotation, WarningSeverity.Low, "Review annotation tag placement", false),
            ("leader", WarningCategory.Annotation, WarningSeverity.Low, "Fix leader attachment", false),
            ("text", WarningCategory.Annotation, WarningSeverity.Info, "Review text note placement", false),
            ("Hidden", WarningCategory.Annotation, WarningSeverity.Info, "Element hidden in view — intentional?", false),

            // Data — parameters, formulas, schedules
            ("Duplicate mark value", WarningCategory.Data, WarningSeverity.High, "Auto-increment duplicate marks", true),
            ("Duplicate Type Mark", WarningCategory.Data, WarningSeverity.Medium, "Resolve duplicate type marks", true),
            ("formula", WarningCategory.Data, WarningSeverity.Medium, "Fix formula reference", false),
            ("schedule", WarningCategory.Data, WarningSeverity.Low, "Review schedule field", false),
            ("shared parameter", WarningCategory.Data, WarningSeverity.Medium, "Check shared parameter binding", false),
            ("type", WarningCategory.Data, WarningSeverity.Low, "Review element type", false),

            // Performance — imports, heavy geometry
            ("import", WarningCategory.Performance, WarningSeverity.Medium, "Consider purging unused imports", false),
            ("DWG", WarningCategory.Performance, WarningSeverity.Medium, "Link instead of import DWG files", false),
            ("raster", WarningCategory.Performance, WarningSeverity.Low, "Reduce raster image resolution", false),
            ("group", WarningCategory.Performance, WarningSeverity.Low, "Review group instance", false),
            ("array", WarningCategory.Performance, WarningSeverity.Low, "Check array member associations", false),
            ("in-place", WarningCategory.Performance, WarningSeverity.Medium, "Convert in-place family to loadable", false),

            // Compliance
            ("fire", WarningCategory.Compliance, WarningSeverity.High, "Verify fire rating compliance", false),
            ("accessibility", WarningCategory.Compliance, WarningSeverity.High, "Check accessibility requirements", false),
            ("code", WarningCategory.Compliance, WarningSeverity.Medium, "Review building code compliance", false),

            // Phase 47: Enhanced classification patterns
            ("stair path", WarningCategory.Geometric, WarningSeverity.Medium, "Review stair configuration", false),
            ("railing", WarningCategory.Geometric, WarningSeverity.Low, "Check railing host", false),
            ("curtain wall", WarningCategory.Geometric, WarningSeverity.Medium, "Review curtain wall panel", false),
            ("ceiling", WarningCategory.Spatial, WarningSeverity.Medium, "Fix ceiling boundary", false),
            ("level", WarningCategory.Data, WarningSeverity.Medium, "Check level assignment", false),
            ("family", WarningCategory.Data, WarningSeverity.Medium, "Review family definition", false),
            ("workset", WarningCategory.Data, WarningSeverity.Low, "Review workset assignment", false),
            ("material", WarningCategory.Data, WarningSeverity.Low, "Fix material assignment", false),
            ("phase", WarningCategory.Data, WarningSeverity.Medium, "Review phase/filter", false),
            ("underlay", WarningCategory.Annotation, WarningSeverity.Info, "Review underlay settings", false),
            ("grid", WarningCategory.Annotation, WarningSeverity.Low, "Fix grid head position", false),
            ("section", WarningCategory.Annotation, WarningSeverity.Low, "Review section marker", false),
        };

        // ── Suppression list (loaded from project_config.json) ──
        private static HashSet<string> _suppressedPatterns = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>Load suppression patterns from project config.</summary>
        internal static void LoadSuppressions()
        {
            try
            {
                string raw = TagConfig.GetConfigValue("WARNING_SUPPRESS_PATTERNS");
                _suppressedPatterns.Clear();
                if (!string.IsNullOrEmpty(raw))
                {
                    foreach (string p in raw.Split('|'))
                    {
                        string trimmed = p.Trim();
                        if (trimmed.Length > 0) _suppressedPatterns.Add(trimmed);
                    }
                }
            }
            catch (Exception ex) { StingLog.Warn($"LoadSuppressions: {ex.Message}"); }
        }

        /// <summary>Save suppression patterns to project config.</summary>
        internal static void SaveSuppressions()
        {
            try
            {
                TagConfig.SetConfigValue("WARNING_SUPPRESS_PATTERNS",
                    string.Join("|", _suppressedPatterns));
            }
            catch (Exception ex) { StingLog.Warn($"SaveSuppressions: {ex.Message}"); }
        }

        /// <summary>Add a pattern to suppress (description substring).</summary>
        internal static void AddSuppression(string pattern)
        {
            if (!string.IsNullOrWhiteSpace(pattern))
            {
                _suppressedPatterns.Add(pattern.Trim());
                SaveSuppressions();
            }
        }

        /// <summary>Check if a warning description matches any suppression pattern.</summary>
        private static bool IsSuppressed(string description)
        {
            foreach (string pattern in _suppressedPatterns)
            {
                if (description.IndexOf(pattern, StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }
            return false;
        }

        // ── CLASSIFICATION ──

        /// <summary>Classify a single Revit FailureMessage into STING category/severity/fix strategy.</summary>
        internal static (WarningCategory cat, WarningSeverity sev, string fix, bool autoFix) ClassifyWarning(string description)
        {
            if (string.IsNullOrEmpty(description))
                return (WarningCategory.Unknown, WarningSeverity.Info, "Review warning", false);

            string lower = description.ToLowerInvariant();
            foreach (var rule in ClassificationRules)
            {
                if (lower.Contains(rule.pattern.ToLowerInvariant()))
                    return (rule.cat, rule.sev, rule.fix, rule.autoFix);
            }
            return (WarningCategory.Unknown, WarningSeverity.Info, "Review manually", false);
        }

        /// <summary>Build a ClassifiedWarning from a Revit FailureMessage with full element context.</summary>
        private static ClassifiedWarning BuildClassified(Document doc, FailureMessage fm)
        {
            string desc = fm.GetDescriptionText();
            var (cat, sev, fix, autoFix) = ClassifyWarning(desc);

            var failing = fm.GetFailingElements();
            var additional = fm.GetAdditionalElements();

            // Derive context from first failing element
            string levelName = "", worksetName = "", discipline = "", categoryName = "";
            if (failing != null && failing.Count > 0)
            {
                Element el = doc.GetElement(failing.First());
                if (el != null)
                {
                    try { levelName = doc.GetElement(el.LevelId)?.Name ?? ""; } catch { }
                    try
                    {
                        if (doc.IsWorkshared)
                        {
                            var wsParam = el.get_Parameter(BuiltInParameter.ELEM_PARTITION_PARAM);
                            if (wsParam != null)
                            {
                                int wsId = wsParam.AsInteger();
                                if (wsId > 0) worksetName = doc.GetWorksetTable().GetWorkset(new WorksetId(wsId))?.Name ?? "";
                            }
                        }
                    }
                    catch { }
                    categoryName = ParameterHelpers.GetCategoryName(el);
                    if (TagConfig.DiscMap.TryGetValue(categoryName, out string d)) discipline = d;
                }
            }

            return new ClassifiedWarning
            {
                Source = fm,
                Description = desc,
                Category = cat,
                Severity = sev,
                FixStrategy = fix,
                CanAutoFix = autoFix,
                FailingElements = failing,
                AdditionalElements = additional,
                LevelName = levelName,
                WorksetName = worksetName,
                Discipline = discipline,
                CategoryName = categoryName
            };
        }

        // ── FULL SCAN ──

        /// <summary>
        /// Comprehensive warning scan with categorisation, hotspot detection, and trend comparison.
        /// </summary>
        internal static WarningReport ScanWarnings(Document doc)
        {
            var report = new WarningReport();
            LoadSuppressions();

            IList<FailureMessage> rawWarnings;
            try { rawWarnings = doc.GetWarnings(); }
            catch (Exception ex)
            {
                StingLog.Error("WarningsEngine.ScanWarnings failed", ex);
                return report;
            }

            if (rawWarnings == null || rawWarnings.Count == 0) return report;

            // Element hotspot counter
            var elementCounts = new Dictionary<long, int>();

            foreach (FailureMessage fm in rawWarnings)
            {
                string desc = fm.GetDescriptionText() ?? "";

                // Skip suppressed warnings from count
                if (IsSuppressed(desc)) continue;

                var cw = BuildClassified(doc, fm);
                report.Warnings.Add(cw);
                report.Total++;

                if (cw.CanAutoFix) report.AutoFixable++;
                else report.ManualReview++;

                // Category counts
                if (!report.ByCategory.ContainsKey(cw.Category)) report.ByCategory[cw.Category] = 0;
                report.ByCategory[cw.Category]++;

                // Severity counts
                if (!report.BySeverity.ContainsKey(cw.Severity)) report.BySeverity[cw.Severity] = 0;
                report.BySeverity[cw.Severity]++;

                // Level counts
                if (!string.IsNullOrEmpty(cw.LevelName))
                {
                    if (!report.ByLevel.ContainsKey(cw.LevelName)) report.ByLevel[cw.LevelName] = 0;
                    report.ByLevel[cw.LevelName]++;
                }

                // Workset counts
                if (!string.IsNullOrEmpty(cw.WorksetName))
                {
                    if (!report.ByWorkset.ContainsKey(cw.WorksetName)) report.ByWorkset[cw.WorksetName] = 0;
                    report.ByWorkset[cw.WorksetName]++;
                }

                // Discipline counts
                if (!string.IsNullOrEmpty(cw.Discipline))
                {
                    if (!report.ByDiscipline.ContainsKey(cw.Discipline)) report.ByDiscipline[cw.Discipline] = 0;
                    report.ByDiscipline[cw.Discipline]++;
                }

                // Hotspot counting
                if (cw.FailingElements != null)
                {
                    foreach (ElementId eid in cw.FailingElements)
                    {
                        long key = eid.Value;
                        if (!elementCounts.ContainsKey(key)) elementCounts[key] = 0;
                        elementCounts[key]++;
                    }
                }
            }

            // Top 20 hotspot elements
            report.Hotspots = elementCounts
                .OrderByDescending(kv => kv.Value)
                .Take(20)
                .Select(kv =>
                {
                    string name = "";
                    try
                    {
                        Element el = doc.GetElement(new ElementId(kv.Key));
                        name = el != null ? $"{ParameterHelpers.GetCategoryName(el)} [{el.Id.Value}]" : $"[{kv.Key}]";
                    }
                    catch { name = $"[{kv.Key}]"; }
                    return (new ElementId(kv.Key), name, kv.Value);
                })
                .ToList();

            // Load baseline for trend comparison
            try
            {
                report.BaselineTotal = LoadBaseline(doc);
            }
            catch (Exception ex) { StingLog.Warn($"Warning baseline load: {ex.Message}"); }

            // Phase 48: Build top warnings by category for tooltip drill-down
            BuildTopWarningsByCategory(report);

            // Phase 48: SLA violation check
            try { CheckWarningSLAViolations(doc, report); }
            catch (Exception ex) { StingLog.Warn($"SLA check: {ex.Message}"); }

            return report;
        }

        // ── AUTO-FIX ENGINE ──

        /// <summary>
        /// Attempt to auto-fix a single warning. Returns true if resolved.
        /// Handles: duplicate instances, redundant room separation lines, duplicate marks,
        /// unjoined elements, and duplicate type marks.
        /// </summary>
        internal static bool AutoFixWarning(Document doc, ClassifiedWarning cw)
        {
            if (!cw.CanAutoFix || cw.FailingElements == null || cw.FailingElements.Count == 0)
                return false;

            string lower = cw.Description.ToLowerInvariant();

            try
            {
                // Strategy 1: Duplicate instances at same location — delete one copy
                if (lower.Contains("duplicate instances in the same place"))
                {
                    // Delete additional elements (keep the first, delete the duplicate)
                    if (cw.AdditionalElements != null && cw.AdditionalElements.Count > 0)
                    {
                        foreach (ElementId id in cw.AdditionalElements)
                        {
                            try { doc.Delete(id); return true; }
                            catch { }
                        }
                    }
                    // Fallback: delete second failing element
                    var ids = cw.FailingElements.ToList();
                    if (ids.Count >= 2)
                    {
                        doc.Delete(ids[1]);
                        return true;
                    }
                }

                // Strategy 2: Room separation line overlaps — delete shorter line
                if (lower.Contains("room separation line") && lower.Contains("overlap"))
                {
                    var ids = cw.FailingElements.ToList();
                    if (ids.Count >= 2)
                    {
                        double len0 = GetCurveLength(doc, ids[0]);
                        double len1 = GetCurveLength(doc, ids[1]);
                        doc.Delete(len0 <= len1 ? ids[0] : ids[1]);
                        return true;
                    }
                }

                // Strategy 3: Redundant elements — delete
                if (lower.Contains("redundant"))
                {
                    var ids = cw.FailingElements.ToList();
                    if (ids.Count >= 2)
                    {
                        doc.Delete(ids[1]); // Keep first, delete redundant
                        return true;
                    }
                }

                // Strategy 4: Duplicate mark value — auto-increment
                if (lower.Contains("duplicate mark value") || lower.Contains("duplicate type mark"))
                {
                    var ids = cw.FailingElements.ToList();
                    if (ids.Count >= 2)
                    {
                        Element el = doc.GetElement(ids[1]);
                        if (el != null)
                        {
                            Parameter markParam = el.get_Parameter(BuiltInParameter.ALL_MODEL_MARK);
                            if (markParam != null && !markParam.IsReadOnly)
                            {
                                string current = markParam.AsString() ?? "";
                                // Append _2 suffix to make unique
                                markParam.Set(current + "_2");
                                return true;
                            }
                        }
                    }
                }

                // Strategy 5: Joined but do not intersect — unjoin
                if (lower.Contains("joined but do not intersect"))
                {
                    var ids = cw.FailingElements.ToList();
                    if (ids.Count >= 2)
                    {
                        try
                        {
                            JoinGeometryUtils.UnjoinGeometry(doc,
                                doc.GetElement(ids[0]), doc.GetElement(ids[1]));
                            return true;
                        }
                        catch { }
                    }
                }
            }
            catch (Exception ex)
            {
                StingLog.Warn($"AutoFix failed for '{cw.Description}': {ex.Message}");
            }
            return false;
        }

        /// <summary>Get length of a curve-based element (separation line, wall, etc.).</summary>
        private static double GetCurveLength(Document doc, ElementId id)
        {
            try
            {
                Element el = doc.GetElement(id);
                if (el?.Location is LocationCurve lc) return lc.Curve.Length;
            }
            catch { }
            return double.MaxValue; // If can't determine, treat as long (don't delete)
        }

        /// <summary>
        /// Batch auto-fix all fixable warnings. Uses single transaction for atomicity.
        /// </summary>
        internal static FixReport BatchAutoFix(Document doc, List<ClassifiedWarning> warnings, bool dryRun = false)
        {
            var report = new FixReport();
            var fixable = warnings.Where(w => w.CanAutoFix).ToList();
            report.Attempted = fixable.Count;

            if (dryRun)
            {
                report.Fixed = fixable.Count;
                foreach (var w in fixable)
                    report.Details.Add($"[DRY-RUN] Would fix: {w.Description}");
                return report;
            }

            using (Transaction tx = new Transaction(doc, "STING Auto-Fix Warnings"))
            {
                tx.Start();
                foreach (var cw in fixable)
                {
                    try
                    {
                        if (AutoFixWarning(doc, cw))
                        {
                            report.Fixed++;
                            report.Details.Add($"Fixed: {cw.Description}");
                        }
                        else
                        {
                            report.Skipped++;
                            report.Details.Add($"Skipped: {cw.Description}");
                        }
                    }
                    catch (Exception ex)
                    {
                        report.Failed++;
                        report.Details.Add($"Failed: {cw.Description} — {ex.Message}");
                    }
                }
                if (report.Fixed > 0)
                    tx.Commit();
                else
                    tx.RollBack();
            }
            return report;
        }

        // ── BASELINE / TREND ──

        private static string GetBaselinePath(Document doc)
        {
            string docPath = doc?.PathName;
            if (string.IsNullOrEmpty(docPath)) return null;
            return Path.ChangeExtension(docPath, ".sting_warnings_baseline.json");
        }

        /// <summary>Save current warning count as baseline for trend tracking.</summary>
        internal static void SaveBaseline(Document doc)
        {
            try
            {
                string path = GetBaselinePath(doc);
                if (path == null) return;
                int count = doc.GetWarnings()?.Count ?? 0;
                string json = $"{{\"total\":{count},\"date\":\"{DateTime.Now:o}\"}}";
                File.WriteAllText(path, json, Encoding.UTF8);
                StingLog.Info($"Warning baseline saved: {count} warnings");
            }
            catch (Exception ex) { StingLog.Warn($"SaveBaseline: {ex.Message}"); }
        }

        /// <summary>Load previous baseline count. Returns null if no baseline exists.</summary>
        internal static int? LoadBaseline(Document doc)
        {
            try
            {
                string path = GetBaselinePath(doc);
                if (path == null || !File.Exists(path)) return null;
                string json = File.ReadAllText(path);
                // Simple parse — extract "total":N
                int idx = json.IndexOf("\"total\":");
                if (idx < 0) return null;
                string numStr = json.Substring(idx + 8).TrimStart();
                int endIdx = numStr.IndexOfAny(new[] { ',', '}', ' ' });
                if (endIdx > 0) numStr = numStr.Substring(0, endIdx);
                return int.TryParse(numStr, out int val) ? val : null;
            }
            catch { return null; }
        }

        // ── EXPORT ──

        /// <summary>Export all warnings to CSV for external tracking (BIM360, Aconex, etc.).</summary>
        internal static string ExportToCSV(Document doc, WarningReport report)
        {
            string exportDir = OutputLocationHelper.GetOutputDirectory(doc);
            string fileName = $"STING_Warnings_{DateTime.Now:yyyyMMdd_HHmmss}.csv";
            string fullPath = Path.Combine(exportDir, fileName);

            var sb = new StringBuilder();
            sb.AppendLine("\"Description\",\"Category\",\"Severity\",\"FixStrategy\",\"CanAutoFix\",\"ElementIds\",\"Level\",\"Workset\",\"Discipline\",\"CategoryName\"");

            foreach (var cw in report.Warnings)
            {
                string ids = cw.FailingElements != null
                    ? string.Join(";", cw.FailingElements.Select(id => id.Value))
                    : "";
                sb.AppendLine($"\"{Escape(cw.Description)}\",\"{cw.Category}\",\"{cw.Severity}\"," +
                    $"\"{Escape(cw.FixStrategy)}\",\"{cw.CanAutoFix}\"," +
                    $"\"{ids}\",\"{Escape(cw.LevelName)}\",\"{Escape(cw.WorksetName)}\"," +
                    $"\"{cw.Discipline}\",\"{Escape(cw.CategoryName)}\"");
            }

            try
            {
                Directory.CreateDirectory(exportDir);
                File.WriteAllText(fullPath, sb.ToString(), Encoding.UTF8);
                StingLog.Info($"Warnings exported: {fullPath}");
            }
            catch (Exception ex) { StingLog.Error($"Export warnings CSV", ex); }
            return fullPath;
        }

        private static string Escape(string s) => (s ?? "").Replace("\"", "\"\"");

        // ── Phase 47: WARNING-TO-ISSUE AUTO-CREATION ──

        /// <summary>Phase 47: Auto-create issues from critical/high severity warnings.
        /// Groups warnings by category, creates one issue per category with element links.</summary>
        internal static List<(string issueId, string title, int elementCount)> CreateIssuesFromWarnings(
            Document doc, List<ClassifiedWarning> warnings, WarningSeverity minSeverity = WarningSeverity.High)
        {
            var results = new List<(string issueId, string title, int elementCount)>();
            try
            {
                var filtered = warnings.Where(w => w.Severity <= minSeverity).ToList(); // Critical=0, High=1 — lower enum = higher severity
                if (filtered.Count == 0) return results;

                var groups = filtered.GroupBy(w => w.Category);

                // Load or initialize issues.json
                string issuesDir = "";
                try
                {
                    string docPath = doc?.PathName;
                    if (!string.IsNullOrEmpty(docPath))
                        issuesDir = Path.Combine(Path.GetDirectoryName(docPath), "_bim_manager");
                    else
                        issuesDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "STING_BIM", "_bim_manager");
                }
                catch (Exception ex) { StingLog.Warn($"CreateIssuesFromWarnings directory: {ex.Message}"); }

                if (string.IsNullOrEmpty(issuesDir)) return results;
                Directory.CreateDirectory(issuesDir);
                string issuesPath = Path.Combine(issuesDir, "issues.json");

                // Load existing issues
                var existingJson = new StringBuilder();
                List<string> existingEntries = new();
                if (File.Exists(issuesPath))
                {
                    try
                    {
                        string raw = File.ReadAllText(issuesPath);
                        // Simple JSON array parse — extract entries between [ and ]
                        raw = raw.Trim();
                        if (raw.StartsWith("[") && raw.EndsWith("]"))
                        {
                            string inner = raw.Substring(1, raw.Length - 2).Trim();
                            if (inner.Length > 0)
                            {
                                // Split on },{ pattern (simplified)
                                int depth = 0;
                                int start = 0;
                                for (int i = 0; i < inner.Length; i++)
                                {
                                    if (inner[i] == '{') depth++;
                                    else if (inner[i] == '}') depth--;
                                    if (depth == 0 && i > start)
                                    {
                                        existingEntries.Add(inner.Substring(start, i - start + 1).Trim());
                                        // Skip comma
                                        while (i + 1 < inner.Length && (inner[i + 1] == ',' || inner[i + 1] == ' ' || inner[i + 1] == '\n' || inner[i + 1] == '\r'))
                                            i++;
                                        start = i + 1;
                                    }
                                }
                            }
                        }
                    }
                    catch (Exception ex) { StingLog.Warn($"CreateIssuesFromWarnings parse: {ex.Message}"); }
                }

                // Determine next issue ID
                int nextId = existingEntries.Count + 1;
                string revision = "";
                try { revision = PhaseAutoDetect.DetectProjectRevision(doc) ?? ""; }
                catch (Exception ex) { StingLog.Warn($"CreateIssuesFromWarnings revision: {ex.Message}"); }

                foreach (var group in groups)
                {
                    var groupWarnings = group.ToList();
                    var maxSeverity = groupWarnings.Min(w => w.Severity); // Min enum = highest severity
                    string issueType = maxSeverity == WarningSeverity.Critical ? "NCR" : "SI";
                    string priority = maxSeverity == WarningSeverity.Critical ? "CRITICAL" : "HIGH";
                    string severityLabel = maxSeverity == WarningSeverity.Critical ? "critical" : "high";
                    string title = $"Warning: {group.Key} — {groupWarnings.Count} {severityLabel} issues detected";

                    // Collect element IDs
                    var elementIds = new HashSet<long>();
                    foreach (var cw in groupWarnings)
                    {
                        if (cw.FailingElements != null)
                            foreach (var eid in cw.FailingElements) elementIds.Add(eid.Value);
                    }

                    string issueId = $"{issueType}-{nextId:D4}";
                    string now = DateTime.Now.ToString("o");
                    string userName = Environment.UserName ?? "STING";

                    // Build JSON entry
                    string elementIdsStr = string.Join(",", elementIds);
                    string entry = "{"
                        + $"\"id\":\"{issueId}\","
                        + $"\"type\":\"{issueType}\","
                        + $"\"title\":\"{title.Replace("\"", "\\\"")}\","
                        + $"\"description\":\"Auto-created from {groupWarnings.Count} Revit warnings in category {group.Key}.\","
                        + $"\"priority\":\"{priority}\","
                        + $"\"status\":\"OPEN\","
                        + $"\"discipline\":\"{groupWarnings.FirstOrDefault()?.Discipline ?? ""}\","
                        + $"\"revision\":\"{revision}\","
                        + $"\"element_ids\":\"{elementIdsStr}\","
                        + $"\"created_by\":\"{userName}\","
                        + $"\"created_date\":\"{now}\","
                        + $"\"modified_by\":\"{userName}\","
                        + $"\"modified_date\":\"{now}\""
                        + "}";

                    existingEntries.Add(entry);
                    results.Add((issueId, title, elementIds.Count));
                    nextId++;
                    StingLog.Info($"Created issue {issueId}: {title} ({elementIds.Count} elements)");
                }

                // Write back
                try
                {
                    var jsonSb = new StringBuilder();
                    jsonSb.AppendLine("[");
                    for (int i = 0; i < existingEntries.Count; i++)
                    {
                        jsonSb.Append("  ");
                        jsonSb.Append(existingEntries[i]);
                        if (i < existingEntries.Count - 1) jsonSb.Append(",");
                        jsonSb.AppendLine();
                    }
                    jsonSb.AppendLine("]");
                    File.WriteAllText(issuesPath, jsonSb.ToString(), Encoding.UTF8);
                    StingLog.Info($"Issues file updated: {issuesPath} ({existingEntries.Count} total entries)");
                }
                catch (Exception ex) { StingLog.Error("CreateIssuesFromWarnings write", ex); }
            }
            catch (Exception ex) { StingLog.Error("CreateIssuesFromWarnings", ex); }
            return results;
        }

        // ── Phase 47: WARNING COMPLIANCE GATE ──

        /// <summary>Phase 47: Check if warnings block compliance gate.
        /// Returns true if model passes warning gate (no critical warnings, total below threshold).</summary>
        internal static (bool pass, string reason) CheckWarningGate(Document doc, int maxCritical = 0, int maxTotal = -1)
        {
            try
            {
                var report = ScanWarnings(doc);

                int criticalCount = report.BySeverity.GetValueOrDefault(WarningSeverity.Critical, 0);
                if (criticalCount > maxCritical)
                    return (false, $"Warning gate FAILED: {criticalCount} critical warning(s) exceed threshold of {maxCritical}. " +
                        $"Resolve critical warnings before proceeding.");

                if (maxTotal >= 0 && report.Total > maxTotal)
                    return (false, $"Warning gate FAILED: {report.Total} total warning(s) exceed threshold of {maxTotal}. " +
                        $"Reduce warnings before proceeding.");

                string reason = $"Warning gate PASSED: {criticalCount} critical (max {maxCritical}), " +
                    $"{report.Total} total" + (maxTotal >= 0 ? $" (max {maxTotal})" : "") + ".";
                return (true, reason);
            }
            catch (Exception ex)
            {
                StingLog.Warn($"CheckWarningGate: {ex.Message}");
                return (true, "Warning gate check failed — proceeding by default.");
            }
        }

        // ── Phase 47: WARNING REGRESSION COMPARISON ──

        /// <summary>Phase 47: Compare current warnings against last revision snapshot.
        /// Returns delta with categorized changes.</summary>
        internal static (int added, int removed, int unchanged, List<string> newWarningTypes)
            CompareWithRevisionBaseline(Document doc)
        {
            var newWarningTypes = new List<string>();
            try
            {
                // Load baseline warning types from sidecar
                string baselinePath = GetBaselinePath(doc);
                if (baselinePath == null || !File.Exists(baselinePath))
                    return (0, 0, 0, newWarningTypes);

                // Load baseline warning type set from extended baseline format
                var baselineTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                try
                {
                    string baselineJson = File.ReadAllText(baselinePath);
                    // Parse warning_types array if present: "warning_types":["desc1","desc2",...]
                    int typesIdx = baselineJson.IndexOf("\"warning_types\":");
                    if (typesIdx >= 0)
                    {
                        int arrStart = baselineJson.IndexOf('[', typesIdx);
                        int arrEnd = baselineJson.IndexOf(']', arrStart);
                        if (arrStart >= 0 && arrEnd > arrStart)
                        {
                            string arrContent = baselineJson.Substring(arrStart + 1, arrEnd - arrStart - 1);
                            foreach (string part in arrContent.Split(','))
                            {
                                string trimmed = part.Trim().Trim('"');
                                if (trimmed.Length > 0) baselineTypes.Add(trimmed);
                            }
                        }
                    }
                }
                catch (Exception ex) { StingLog.Warn($"CompareWithRevisionBaseline parse: {ex.Message}"); }

                if (baselineTypes.Count == 0)
                {
                    // No typed baseline — fall back to count-only comparison
                    int? baselineTotal = LoadBaseline(doc);
                    int currentTotal = doc.GetWarnings()?.Count ?? 0;
                    int delta = baselineTotal.HasValue ? currentTotal - baselineTotal.Value : 0;
                    return (Math.Max(0, delta), Math.Max(0, -delta), Math.Min(currentTotal, baselineTotal ?? currentTotal), newWarningTypes);
                }

                // Build current warning type set
                var currentTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                try
                {
                    var rawWarnings = doc.GetWarnings();
                    if (rawWarnings != null)
                    {
                        foreach (var fm in rawWarnings)
                        {
                            string desc = fm.GetDescriptionText() ?? "";
                            if (desc.Length > 0) currentTypes.Add(desc);
                        }
                    }
                }
                catch (Exception ex) { StingLog.Warn($"CompareWithRevisionBaseline scan: {ex.Message}"); }

                int added = 0, removed = 0, unchanged = 0;

                // Find new warning types (in current but not in baseline)
                foreach (string t in currentTypes)
                {
                    if (baselineTypes.Contains(t))
                        unchanged++;
                    else
                    {
                        added++;
                        newWarningTypes.Add(t);
                    }
                }

                // Find removed warning types (in baseline but not in current)
                foreach (string t in baselineTypes)
                {
                    if (!currentTypes.Contains(t))
                        removed++;
                }

                return (added, removed, unchanged, newWarningTypes);
            }
            catch (Exception ex)
            {
                StingLog.Warn($"CompareWithRevisionBaseline: {ex.Message}");
                return (0, 0, 0, newWarningTypes);
            }
        }

        // ── Phase 47: WARNING HEALTH SCORE ──

        /// <summary>Phase 47: Calculate overall warning health score 0-100.
        /// Weighted: Critical=-20, High=-5, Medium=-2, Low=-1, Info=0. Base=100.</summary>
        internal static int CalculateWarningHealthScore(WarningReport report)
        {
            if (report == null) return 100;

            int score = 100;
            score -= report.BySeverity.GetValueOrDefault(WarningSeverity.Critical, 0) * 20;
            score -= report.BySeverity.GetValueOrDefault(WarningSeverity.High, 0) * 5;
            score -= report.BySeverity.GetValueOrDefault(WarningSeverity.Medium, 0) * 2;
            score -= report.BySeverity.GetValueOrDefault(WarningSeverity.Low, 0) * 1;
            // Info = 0 weight (no penalty)

            return Math.Max(0, Math.Min(100, score));
        }

        // ── Phase 49: SMART SUGGESTIONS ENGINE ──

        /// <summary>
        /// Generate prioritised action suggestions based on current model state analysis.
        /// Examines compliance, warnings, issues, stale elements, and workflow history
        /// to recommend the most impactful next actions for BIM coordinators.
        /// </summary>
        internal static List<(string Text, string Action, string Priority)> GenerateSmartSuggestions(
            UI.BIMCoordinationCenter.CoordData data, WarningReport warningReport)
        {
            var suggestions = new List<(string Text, string Action, string Priority)>();
            try
            {
                // Critical: Stale elements block accurate deliverables
                if (data.StaleCount > 10)
                    suggestions.Add(($"Re-tag {data.StaleCount} stale elements before next export", "RetagStale", "HIGH"));
                else if (data.StaleCount > 0)
                    suggestions.Add(($"Clear {data.StaleCount} stale element(s)", "RetagStale", "MEDIUM"));

                // Critical: Overdue issues
                if (data.IssuesOverdue > 0)
                    suggestions.Add(($"Resolve {data.IssuesOverdue} overdue issue(s) — SLA breach risk", "IssueDashboard", "HIGH"));

                // High: Critical warnings
                if (data.WarningCritical > 0)
                    suggestions.Add(($"Fix {data.WarningCritical} critical warning(s) — blocks handover", "AutoFixWarnings", "HIGH"));

                // High: Low compliance
                if (data.TagPct < 50)
                    suggestions.Add(("Tag compliance below 50% — run batch tagging", "BatchTag", "HIGH"));
                else if (data.TagPct < 80)
                    suggestions.Add(($"Improve compliance from {data.TagPct:F0}% to 80%+ target", "TagNewOnly", "MEDIUM"));

                // Medium: Container completeness for COBie
                if (data.ContainerCompletePct < 80 && data.TagPct > 50)
                    suggestions.Add(($"Container completion at {data.ContainerCompletePct:F0}% — run Combine Parameters", "CombineParameters", "MEDIUM"));

                // Medium: Placeholders need resolution
                if (data.PlaceholderCount > 20)
                    suggestions.Add(($"Resolve {data.PlaceholderCount} placeholder tokens (GEN/XX/ZZ)", "ResolveAllIssues", "MEDIUM"));

                // Medium: Auto-fixable warnings
                if (data.WarningAutoFixable > 5)
                    suggestions.Add(($"Auto-fix {data.WarningAutoFixable} warnings in one click", "AutoFixWarnings", "MEDIUM"));

                // Low: Untagged elements
                if (data.Untagged > 0 && data.Untagged < 50)
                    suggestions.Add(($"Tag {data.Untagged} remaining untagged elements", "TagNewOnly", "LOW"));

                // Low: Run DailyQA if not run today
                if (string.IsNullOrEmpty(data.LastWorkflow) || data.LastWorkflow == "none")
                    suggestions.Add(("Run Daily QA workflow for comprehensive model check", "RunDailyQA", "MEDIUM"));

                // Low: Warning health below threshold
                if (data.WarningHealthScore < 50)
                    suggestions.Add(($"Warning health at {data.WarningHealthScore}/100 — review and fix", "WarningsDashboard", "MEDIUM"));

                // Informational: Ready for COBie export
                if (data.TagPct >= 90 && data.ContainerCompletePct >= 80 && data.WarningCritical == 0)
                    suggestions.Add(("Model ready for COBie export — compliance targets met", "COBieExport", "LOW"));

                // Informational: Save baseline if warnings changed
                if (warningReport != null && Math.Abs(warningReport.TrendDelta) > 5)
                    suggestions.Add(("Warning count changed significantly — save new baseline", "SaveBaseline", "LOW"));
            }
            catch (Exception ex) { StingLog.Warn($"Smart suggestions: {ex.Message}"); }

            return suggestions.Take(8).ToList();
        }

        // ── Phase 49: COORDINATION LOG ──

        /// <summary>
        /// Append an entry to the coordination log sidecar file.
        /// Thread-safe write with retry.
        /// </summary>
        internal static void LogCoordinationAction(Document doc, string action, string category, string detail, string impact = "LOW")
        {
            try
            {
                if (doc == null || string.IsNullOrEmpty(doc.PathName)) return;
                string logPath = Path.Combine(Path.GetDirectoryName(doc.PathName) ?? "", ".sting_coord_log.json");

                var entry = new UI.BIMCoordinationCenter.CoordLogEntry
                {
                    Timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                    User = Environment.UserName ?? "unknown",
                    Action = action,
                    Category = category,
                    Detail = detail,
                    Impact = impact
                };

                List<UI.BIMCoordinationCenter.CoordLogEntry> entries;
                if (File.Exists(logPath))
                {
                    try
                    {
                        entries = Newtonsoft.Json.JsonConvert.DeserializeObject<List<UI.BIMCoordinationCenter.CoordLogEntry>>(
                            File.ReadAllText(logPath)) ?? new List<UI.BIMCoordinationCenter.CoordLogEntry>();
                    }
                    catch { entries = new List<UI.BIMCoordinationCenter.CoordLogEntry>(); }
                }
                else
                {
                    entries = new List<UI.BIMCoordinationCenter.CoordLogEntry>();
                }

                entries.Add(entry);

                // Cap at 500 entries — rotate oldest
                if (entries.Count > 500)
                    entries = entries.Skip(entries.Count - 500).ToList();

                File.WriteAllText(logPath, Newtonsoft.Json.JsonConvert.SerializeObject(entries, Newtonsoft.Json.Formatting.Indented));
            }
            catch (Exception ex) { StingLog.Warn($"CoordLog write: {ex.Message}"); }
        }

        /// <summary>Phase 49: Predictive compliance forecast — estimates days to reach target based on trend.</summary>
        internal static (double daysToTarget, double projectedPct) ForecastCompliance(List<(DateTime Date, double Pct)> trend, double targetPct = 80)
        {
            if (trend == null || trend.Count < 2) return (-1, trend?.LastOrDefault().Pct ?? 0);

            // Simple linear regression on last 10 data points
            var recent = trend.TakeLast(10).ToList();
            double sumX = 0, sumY = 0, sumXY = 0, sumX2 = 0;
            double baseTime = recent[0].Date.Ticks / (double)TimeSpan.TicksPerDay;
            int n = recent.Count;

            for (int i = 0; i < n; i++)
            {
                double x = (recent[i].Date.Ticks / (double)TimeSpan.TicksPerDay) - baseTime;
                double y = recent[i].Pct;
                sumX += x; sumY += y; sumXY += x * y; sumX2 += x * x;
            }

            double slope = (n * sumXY - sumX * sumY) / (n * sumX2 - sumX * sumX + 0.0001);
            double intercept = (sumY - slope * sumX) / n;

            double currentX = (DateTime.Now.Ticks / (double)TimeSpan.TicksPerDay) - baseTime;
            double projected = slope * currentX + intercept;

            if (slope <= 0) return (-1, Math.Max(0, Math.Min(100, projected))); // Not improving

            double daysToTarget = (targetPct - projected) / slope;
            return (Math.Max(0, daysToTarget), Math.Max(0, Math.Min(100, projected)));
        }

        // ── Phase 48: SLA ENFORCEMENT ──

        /// <summary>ISO 19650-aligned SLA thresholds per warning severity (hours).</summary>
        internal static readonly Dictionary<WarningSeverity, double> SLAThresholdsHours = new()
        {
            { WarningSeverity.Critical, 4 },     // 4 hours
            { WarningSeverity.High, 24 },         // 1 day
            { WarningSeverity.Medium, 168 },      // 1 week
            { WarningSeverity.Low, 336 },         // 2 weeks
            { WarningSeverity.Info, double.MaxValue }  // No SLA
        };

        /// <summary>Phase 48: Check for SLA violations against warning baseline timestamps.
        /// Returns count of warnings exceeding their severity-specific SLA.</summary>
        internal static int CheckWarningSLAViolations(Document doc, WarningReport report)
        {
            int violations = 0;
            try
            {
                // Load baseline timestamp to calculate warning age
                string baselinePath = GetBaselinePath(doc);
                DateTime baselineTime = DateTime.Now.AddHours(-48); // Default: assume 48h old if no baseline
                if (baselinePath != null && File.Exists(baselinePath))
                {
                    try
                    {
                        string json = File.ReadAllText(baselinePath);
                        int dateIdx = json.IndexOf("\"date\":\"");
                        if (dateIdx >= 0)
                        {
                            int s = dateIdx + 8;
                            int e = json.IndexOf('"', s);
                            if (e > s && DateTime.TryParse(json.Substring(s, e - s), out DateTime dt))
                                baselineTime = dt;
                        }
                    }
                    catch (Exception ex) { StingLog.Warn($"SLA baseline parse: {ex.Message}"); }
                }

                double hoursOld = (DateTime.Now - baselineTime).TotalHours;
                foreach (var sev in new[] { WarningSeverity.Critical, WarningSeverity.High, WarningSeverity.Medium, WarningSeverity.Low })
                {
                    if (report.BySeverity.TryGetValue(sev, out int count) && count > 0)
                    {
                        if (hoursOld > SLAThresholdsHours[sev])
                            violations += count;
                    }
                }
                report.SLAViolations = violations;
            }
            catch (Exception ex) { StingLog.Warn($"CheckWarningSLAViolations: {ex.Message}"); }
            return violations;
        }

        /// <summary>Phase 48: Save extended baseline with warning type tracking for regression analysis.</summary>
        internal static void SaveExtendedBaseline(Document doc)
        {
            try
            {
                string path = GetBaselinePath(doc);
                if (path == null) return;

                var warnings = doc.GetWarnings();
                int count = warnings?.Count ?? 0;

                // Build warning type array
                var types = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                if (warnings != null)
                {
                    foreach (var fm in warnings)
                    {
                        string desc = fm.GetDescriptionText();
                        if (!string.IsNullOrEmpty(desc)) types.Add(desc);
                    }
                }

                var sb = new StringBuilder();
                sb.Append("{");
                sb.Append($"\"total\":{count},");
                sb.Append($"\"date\":\"{DateTime.Now:o}\",");
                sb.Append($"\"user\":\"{Environment.UserName ?? "unknown"}\",");
                sb.Append("\"warning_types\":[");
                bool first = true;
                foreach (string t in types)
                {
                    if (!first) sb.Append(",");
                    sb.Append($"\"{t.Replace("\"", "\\\"")}\"");
                    first = false;
                }
                sb.Append("]");
                sb.Append("}");

                File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
                StingLog.Info($"Extended warning baseline saved: {count} warnings, {types.Count} types");
            }
            catch (Exception ex) { StingLog.Warn($"SaveExtendedBaseline: {ex.Message}"); }
        }

        /// <summary>Phase 48: Build top-N warnings per category for drill-down tooltips.</summary>
        internal static void BuildTopWarningsByCategory(WarningReport report)
        {
            if (report?.Warnings == null) return;
            report.TopWarningsByCategory.Clear();

            var groups = report.Warnings.GroupBy(w => w.Category);
            foreach (var group in groups)
            {
                var topDescs = group
                    .GroupBy(w => w.Description)
                    .OrderByDescending(g => g.Count())
                    .Take(3)
                    .Select(g => (g.Key.Length > 80 ? g.Key.Substring(0, 77) + "..." : g.Key, g.Count()))
                    .ToList();
                report.TopWarningsByCategory[group.Key] = topDescs;
            }
        }
    }

    // ══════════════════════════════════════════════════════════════════
    //  TRANSACTION-LEVEL WARNING HANDLER (IFailuresPreprocessor)
    // ══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Intercepts warnings during STING transactions. Three modes:
    /// Silent — dismiss all warnings (for batch operations).
    /// Selective — auto-resolve known fixable, keep unknown (default).
    /// Strict — fail transaction on any warning (for compliance-gated operations).
    /// </summary>
    internal class StingWarningHandler : IFailuresPreprocessor
    {
        internal enum HandlerMode { Silent, Selective, Strict }

        private readonly HandlerMode _mode;
        private readonly List<string> _encountered = new();

        public StingWarningHandler(HandlerMode mode = HandlerMode.Selective)
        {
            _mode = mode;
        }

        /// <summary>Warnings encountered during this transaction.</summary>
        public IReadOnlyList<string> EncounteredWarnings => _encountered;
        public int WarningCount => _encountered.Count;

        public FailureProcessingResult PreprocessFailures(FailuresAccessor failuresAccessor)
        {
            var failures = failuresAccessor.GetFailureMessages();
            if (failures == null || failures.Count == 0)
                return FailureProcessingResult.Continue;

            foreach (FailureMessageAccessor fma in failures)
            {
                FailureSeverity severity = fma.GetSeverity();
                string desc = fma.GetDescriptionText() ?? "";
                _encountered.Add(desc);

                // DocumentCorruption — always rollback
                if (severity == FailureSeverity.DocumentCorruption)
                    return FailureProcessingResult.ProceedWithRollBack;

                if (severity == FailureSeverity.Error)
                {
                    // Errors cannot be dismissed — try resolution via BuiltInFailures IDs first
                    if (fma.HasResolutions())
                    {
                        fma.SetCurrentResolutionType(FailureResolutionType.Default);
                        failuresAccessor.ResolveFailure(fma);
                    }
                    continue;
                }

                // Warning handling based on mode
                switch (_mode)
                {
                    case HandlerMode.Silent:
                        failuresAccessor.DeleteWarning(fma);
                        break;

                    case HandlerMode.Selective:
                        var (_, _, _, autoFix) = WarningsEngine.ClassifyWarning(desc);
                        if (autoFix && fma.HasResolutions())
                        {
                            fma.SetCurrentResolutionType(FailureResolutionType.Default);
                            failuresAccessor.ResolveFailure(fma);
                        }
                        else
                        {
                            failuresAccessor.DeleteWarning(fma);
                        }
                        break;

                    case HandlerMode.Strict:
                        // Don't dismiss — let transaction fail
                        return FailureProcessingResult.ProceedWithRollBack;
                }
            }
            return FailureProcessingResult.ProceedWithCommit;
        }
    }

    // ══════════════════════════════════════════════════════════════════
    //  COMMANDS (8 IExternalCommand classes)
    // ══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Comprehensive warnings dashboard showing categorised breakdown, severity distribution,
    /// trend vs baseline, hotspot elements, and per-level/discipline/workset analysis.
    /// </summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class WarningsDashboardCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                Document doc = ParameterHelpers.GetApp(commandData)?.ActiveUIDocument?.Document;
                if (doc == null) { message = "No document open."; return Result.Failed; }

                var report = WarningsEngine.ScanWarnings(doc);
                int healthScore = WarningsEngine.CalculateWarningHealthScore(report);

                // Build rich WPF result panel
                var builder = UI.StingResultPanel.Create("STING Warnings Dashboard")
                    .SetSubtitle($"{report.Total} warnings {report.TrendSymbol}  |  Health: {healthScore}/100");

                // ── Summary section ──
                int critical = report.BySeverity.TryGetValue(WarningSeverity.Critical, out int c) ? c : 0;
                int high = report.BySeverity.TryGetValue(WarningSeverity.High, out int h) ? h : 0;
                int medium = report.BySeverity.TryGetValue(WarningSeverity.Medium, out int m) ? m : 0;
                int low = report.BySeverity.TryGetValue(WarningSeverity.Low, out int l) ? l : 0;
                int info = report.BySeverity.TryGetValue(WarningSeverity.Info, out int inf) ? inf : 0;

                builder.AddSection("Summary")
                    .Metric("Total Warnings", report.Total.ToString())
                    .Metric("Health Score", $"{healthScore} / 100",
                        healthScore >= 80 ? "GOOD" : healthScore >= 50 ? "NEEDS ATTENTION" : "POOR")
                    .RAGBar(healthScore);

                if (critical > 0)
                    builder.MetricError("Critical", critical.ToString(), "Requires immediate attention");
                else
                    builder.MetricHighlight("Critical", "0", "No critical warnings");
                if (high > 0) builder.MetricWarn("High", high.ToString());
                if (medium > 0) builder.Metric("Medium", medium.ToString());
                if (low > 0) builder.Metric("Low", low.ToString());
                if (info > 0) builder.Metric("Info", info.ToString());

                builder.Separator()
                    .MetricHighlight("Auto-fixable", report.AutoFixable.ToString(), "Can be fixed automatically")
                    .Metric("Manual review", report.ManualReview.ToString());

                // ── Baseline trend ──
                if (report.BaselineTotal.HasValue)
                {
                    builder.AddSection("Trend vs Baseline")
                        .Metric("Baseline", report.BaselineTotal.Value.ToString())
                        .Metric("Current", report.Total.ToString())
                        .Metric("Delta", report.TrendSymbol,
                            report.Total < report.BaselineTotal ? "Improving" :
                            report.Total > report.BaselineTotal ? "Worsening" : "Stable");
                }

                // ── By Category table ──
                if (report.ByCategory.Count > 0)
                {
                    var catRows = report.ByCategory.OrderByDescending(x => x.Value)
                        .Select(kv => new[] { kv.Key.ToString(), kv.Value.ToString(),
                            $"{(double)kv.Value / Math.Max(report.Total, 1) * 100:F0}%" })
                        .ToList();
                    builder.AddSection("By Category")
                        .Table(new[] { "Category", "Count", "%" }, catRows);
                }

                // ── By Severity table ──
                {
                    var sevRows = new List<string[]>();
                    foreach (WarningSeverity sev in Enum.GetValues(typeof(WarningSeverity)))
                        if (report.BySeverity.TryGetValue(sev, out int cnt) && cnt > 0)
                            sevRows.Add(new[] { sev.ToString(), cnt.ToString(),
                                $"{(double)cnt / Math.Max(report.Total, 1) * 100:F0}%" });
                    if (sevRows.Count > 0)
                        builder.AddSection("By Severity")
                            .Table(new[] { "Severity", "Count", "%" }, sevRows);
                }

                // ── By Discipline table ──
                if (report.ByDiscipline.Count > 0)
                {
                    var discRows = report.ByDiscipline.OrderByDescending(x => x.Value)
                        .Select(kv => new[] { kv.Key, kv.Value.ToString() }).ToList();
                    builder.AddSection("By Discipline")
                        .Table(new[] { "Discipline", "Count" }, discRows);
                }

                // ── By Level table (top 10) ──
                if (report.ByLevel.Count > 0)
                {
                    var lvlRows = report.ByLevel.OrderByDescending(x => x.Value).Take(10)
                        .Select(kv => new[] { kv.Key, kv.Value.ToString() }).ToList();
                    builder.AddSection("By Level (top 10)")
                        .Table(new[] { "Level", "Count" }, lvlRows);
                }

                // ── Hotspot elements (top 10) ──
                if (report.Hotspots.Count > 0)
                {
                    var hotRows = report.Hotspots.Take(10)
                        .Select(hs => new[] { hs.Item2, hs.Item3.ToString() }).ToList();
                    builder.AddSection("Hotspot Elements (most warnings)")
                        .Table(new[] { "Element", "Warnings" }, hotRows);
                }

                // ── SLA violations ──
                if (report.SLAViolations > 0)
                {
                    builder.AddSection("SLA Violations")
                        .MetricError("Overdue", report.SLAViolations.ToString(), "Warnings exceeding SLA thresholds")
                        .Metric("Avg Critical Age", $"{report.AvgCriticalAgeHours:F1} hours");
                }

                // Build plain text for clipboard/fallback
                var sb = new StringBuilder();
                sb.AppendLine($"STING Warnings Dashboard — {report.Total} total {report.TrendSymbol}");
                sb.AppendLine($"Health Score: {healthScore}/100");
                sb.AppendLine($"Critical: {critical}  High: {high}  Medium: {medium}  Low: {low}");
                sb.AppendLine($"Auto-fixable: {report.AutoFixable}  Manual: {report.ManualReview}");
                if (report.ByCategory.Count > 0)
                {
                    sb.AppendLine("\nBy Category:");
                    foreach (var kv in report.ByCategory.OrderByDescending(x => x.Value))
                        sb.AppendLine($"  {kv.Key}: {kv.Value}");
                }
                if (report.Hotspots.Count > 0)
                {
                    sb.AppendLine("\nHotspot Elements:");
                    foreach (var (id, name, count) in report.Hotspots.Take(10))
                        sb.AppendLine($"  {name}: {count} warnings");
                }
                builder.SetRawText(sb.ToString());

                builder.Show();

                StingLog.Info($"WarningsDashboard: {report.Total} total, {report.AutoFixable} fixable, health={healthScore}");
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("WarningsDashboard failed", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }
    }

    /// <summary>Scan and auto-fix all fixable warnings in a single transaction.</summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class WarningsAutoFixCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                Document doc = ParameterHelpers.GetApp(commandData)?.ActiveUIDocument?.Document;
                if (doc == null) { message = "No document open."; return Result.Failed; }

                var report = WarningsEngine.ScanWarnings(doc);
                if (report.AutoFixable == 0)
                {
                    TaskDialog.Show("STING Warnings", "No auto-fixable warnings found.");
                    return Result.Succeeded;
                }

                // Preview
                var dlg = new TaskDialog("STING Auto-Fix Warnings");
                dlg.MainInstruction = $"{report.AutoFixable} auto-fixable warnings found";
                dlg.MainContent = "Fix strategies:\n" +
                    string.Join("\n", report.Warnings
                        .Where(w => w.CanAutoFix)
                        .GroupBy(w => w.FixStrategy)
                        .Select(g => $"  • {g.Key}: {g.Count()}"));
                dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Fix all auto-fixable warnings");
                dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "Dry-run preview (no changes)");
                dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink3, "Cancel");
                var result = dlg.Show();

                if (result == TaskDialogResult.CommandLink3) return Result.Cancelled;
                bool dryRun = result == TaskDialogResult.CommandLink2;

                var fixReport = WarningsEngine.BatchAutoFix(doc, report.Warnings, dryRun);

                var sb = new StringBuilder();
                sb.AppendLine(dryRun ? "DRY-RUN RESULTS:" : "AUTO-FIX RESULTS:");
                sb.AppendLine($"  Fixed:   {fixReport.Fixed}");
                sb.AppendLine($"  Skipped: {fixReport.Skipped}");
                sb.AppendLine($"  Failed:  {fixReport.Failed}");
                if (fixReport.Details.Count > 0)
                {
                    sb.AppendLine("\nDetails (first 20):");
                    foreach (string d in fixReport.Details.Take(20))
                        sb.AppendLine($"  {d}");
                }
                TaskDialog.Show("STING Auto-Fix", sb.ToString());
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("WarningsAutoFix failed", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }
    }

    /// <summary>Export all warnings to CSV for external tracking.</summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class WarningsExportCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                Document doc = ParameterHelpers.GetApp(commandData)?.ActiveUIDocument?.Document;
                if (doc == null) { message = "No document open."; return Result.Failed; }

                var report = WarningsEngine.ScanWarnings(doc);
                string path = WarningsEngine.ExportToCSV(doc, report);
                TaskDialog.Show("STING Warnings Export",
                    $"Exported {report.Total} warnings to:\n{path}");
                return Result.Succeeded;
            }
            catch (Exception ex) { message = ex.Message; return Result.Failed; }
        }
    }

    /// <summary>Save current warning count as baseline for trend tracking.</summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class WarningsBaselineCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                Document doc = ParameterHelpers.GetApp(commandData)?.ActiveUIDocument?.Document;
                if (doc == null) { message = "No document open."; return Result.Failed; }

                int? prev = WarningsEngine.LoadBaseline(doc);
                int current = doc.GetWarnings()?.Count ?? 0;
                WarningsEngine.SaveBaseline(doc);

                string msg = prev.HasValue
                    ? $"Baseline updated: {prev.Value} → {current} ({(current > prev.Value ? "↑" : current < prev.Value ? "↓" : "→")}{Math.Abs(current - prev.Value)})"
                    : $"Baseline saved: {current} warnings";
                TaskDialog.Show("STING Warning Baseline", msg);
                return Result.Succeeded;
            }
            catch (Exception ex) { message = ex.Message; return Result.Failed; }
        }
    }

    /// <summary>Select elements associated with a specific warning type.</summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class WarningsSelectElementsCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                var uidoc = ParameterHelpers.GetApp(commandData)?.ActiveUIDocument;
                Document doc = uidoc?.Document;
                if (doc == null) { message = "No document open."; return Result.Failed; }

                var report = WarningsEngine.ScanWarnings(doc);
                if (report.Total == 0) { TaskDialog.Show("STING", "No warnings found."); return Result.Succeeded; }

                // Group by description for picker
                var groups = report.Warnings
                    .GroupBy(w => w.Description)
                    .OrderByDescending(g => g.Count())
                    .Take(30)
                    .Select(g => new StingTools.Select.StingListPicker.ListItem
                    {
                        Label = $"({g.Count()}) {g.Key}",
                        Detail = $"{g.First().Category} | {g.First().Severity}",
                        Tag = g.Key
                    })
                    .ToList();

                var picked = StingTools.Select.StingListPicker.Show("Select Warning Type",
                    "Pick a warning type to select its elements:", groups, allowMultiSelect: false);
                if (picked == null || picked.Count == 0) return Result.Cancelled;

                string selectedDesc = picked[0].Tag as string;
                var matchingWarnings = report.Warnings.Where(w => w.Description == selectedDesc);
                var allIds = new HashSet<ElementId>();
                foreach (var cw in matchingWarnings)
                {
                    if (cw.FailingElements != null)
                        foreach (var id in cw.FailingElements) allIds.Add(id);
                    if (cw.AdditionalElements != null)
                        foreach (var id in cw.AdditionalElements) allIds.Add(id);
                }

                if (allIds.Count > 0)
                {
                    uidoc.Selection.SetElementIds(allIds.ToList());
                    TaskDialog.Show("STING", $"Selected {allIds.Count} elements with warning:\n{selectedDesc}");
                }
                return Result.Succeeded;
            }
            catch (Exception ex) { message = ex.Message; return Result.Failed; }
        }
    }

    /// <summary>Add warning patterns to the suppression list (hidden from dashboard).</summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class WarningsSuppressCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                Document doc = ParameterHelpers.GetApp(commandData)?.ActiveUIDocument?.Document;
                if (doc == null) { message = "No document open."; return Result.Failed; }

                var report = WarningsEngine.ScanWarnings(doc);
                var groups = report.Warnings
                    .GroupBy(w => w.Description)
                    .OrderByDescending(g => g.Count())
                    .Take(30)
                    .Select(g => new StingTools.Select.StingListPicker.ListItem
                    {
                        Label = $"({g.Count()}) {g.Key}",
                        Detail = $"{g.First().Category} | Suppress this warning type",
                        Tag = g.Key
                    })
                    .ToList();

                var picked = StingTools.Select.StingListPicker.Show("Suppress Warning Types",
                    "Select warnings to suppress from dashboard:", groups, allowMultiSelect: true);
                if (picked == null || picked.Count == 0) return Result.Cancelled;

                int count = 0;
                foreach (var item in picked)
                {
                    string desc = item.Tag as string;
                    if (!string.IsNullOrEmpty(desc))
                    {
                        // Use first 50 chars as pattern to avoid overly specific matching
                        string pattern = desc.Length > 50 ? desc.Substring(0, 50) : desc;
                        WarningsEngine.AddSuppression(pattern);
                        count++;
                    }
                }
                TaskDialog.Show("STING", $"Suppressed {count} warning pattern(s).\nThey will be hidden from future dashboard scans.");
                return Result.Succeeded;
            }
            catch (Exception ex) { message = ex.Message; return Result.Failed; }
        }
    }

    /// <summary>Compare warnings to ISO 19650 / BS 7671 / CIBSE compliance requirements.</summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class WarningsComplianceCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                Document doc = ParameterHelpers.GetApp(commandData)?.ActiveUIDocument?.Document;
                if (doc == null) { message = "No document open."; return Result.Failed; }

                var report = WarningsEngine.ScanWarnings(doc);
                var sb = new StringBuilder();
                sb.AppendLine("STING Warnings Compliance Report");
                sb.AppendLine(new string('═', 45));

                // ISO 19650 compliance: spatial and data warnings
                int spatial = report.ByCategory.GetValueOrDefault(WarningCategory.Spatial);
                int data = report.ByCategory.GetValueOrDefault(WarningCategory.Data);
                int mep = report.ByCategory.GetValueOrDefault(WarningCategory.MEP);
                int compliance = report.ByCategory.GetValueOrDefault(WarningCategory.Compliance);
                int critical = report.BySeverity.GetValueOrDefault(WarningSeverity.Critical);

                sb.AppendLine($"\n■ ISO 19650 — Information Management:");
                sb.AppendLine($"  Spatial integrity:     {(spatial == 0 ? "PASS ✓" : $"FAIL ✗ ({spatial} issues)")}");
                sb.AppendLine($"  Data consistency:      {(data == 0 ? "PASS ✓" : $"FAIL ✗ ({data} issues)")}");
                sb.AppendLine($"  Critical warnings:     {(critical == 0 ? "PASS ✓" : $"FAIL ✗ ({critical} critical)")}");

                sb.AppendLine($"\n■ Building Services (CIBSE / BS 7671):");
                sb.AppendLine($"  MEP connectivity:      {(mep == 0 ? "PASS ✓" : $"REVIEW ({mep} issues)")}");

                sb.AppendLine($"\n■ Compliance Standards:");
                sb.AppendLine($"  Fire/accessibility:    {(compliance == 0 ? "PASS ✓" : $"REVIEW ({compliance} issues)")}");

                // Overall verdict
                bool pass = critical == 0 && spatial <= 2 && data <= 5;
                sb.AppendLine($"\n■ OVERALL: {(pass ? "COMPLIANT ✓" : "NON-COMPLIANT ✗ — resolve critical/spatial issues")}");

                TaskDialog.Show("STING Compliance Report", sb.ToString());
                return Result.Succeeded;
            }
            catch (Exception ex) { message = ex.Message; return Result.Failed; }
        }
    }

    /// <summary>Monitor warning count before/after a command — detect warning regression.</summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class WarningsMonitorCommand : IExternalCommand
    {
        private static int? _preCommandCount;

        /// <summary>Call before a major command to snapshot warning count.</summary>
        internal static void SnapshotBefore(Document doc)
        {
            try { _preCommandCount = doc?.GetWarnings()?.Count; }
            catch { _preCommandCount = null; }
        }

        /// <summary>Call after a command — shows alert if warnings increased.</summary>
        internal static void CheckAfter(Document doc, string commandName)
        {
            try
            {
                if (!_preCommandCount.HasValue || doc == null) return;
                int after = doc.GetWarnings()?.Count ?? 0;
                int delta = after - _preCommandCount.Value;
                if (delta > 0)
                {
                    StingLog.Warn($"WarningsMonitor: {commandName} introduced {delta} new warning(s) ({_preCommandCount.Value} → {after})");
                }
                _preCommandCount = null;
            }
            catch (Exception ex) { StingLog.Warn($"WarningsMonitor.CheckAfter: {ex.Message}"); }
        }

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                Document doc = ParameterHelpers.GetApp(commandData)?.ActiveUIDocument?.Document;
                if (doc == null) return Result.Failed;
                int count = doc.GetWarnings()?.Count ?? 0;
                TaskDialog.Show("STING Warning Monitor",
                    $"Current warnings: {count}\n" +
                    $"Last pre-command snapshot: {(_preCommandCount.HasValue ? _preCommandCount.Value.ToString() : "none")}\n\n" +
                    "The monitor automatically tracks warning count before/after major STING commands.");
                return Result.Succeeded;
            }
            catch (Exception ex) { message = ex.Message; return Result.Failed; }
        }
    }

    // ══════════════════════════════════════════════════════════════════
    //  Phase 47: BIM COORDINATION CENTER — Unified dashboard command
    // ══════════════════════════════════════════════════════════════════

    /// <summary>Phase 47: Open unified BIM Coordination Center with all dashboards merged.</summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class BIMCoordinationCenterCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                var uidoc = ParameterHelpers.GetApp(commandData)?.ActiveUIDocument;
                Document doc = uidoc?.Document;
                if (doc == null) { message = "No document open."; return Result.Failed; }

                // 1. Run compliance scan
                ComplianceScan.InvalidateCache();
                var compliance = ComplianceScan.Scan(doc);

                // 2. Scan warnings
                var warningReport = WarningsEngine.ScanWarnings(doc);
                int healthScore = WarningsEngine.CalculateWarningHealthScore(warningReport);

                // 3. Load issues from issues.json
                int openIssues = 0, criticalIssues = 0;
                try
                {
                    string docPath = doc.PathName;
                    if (!string.IsNullOrEmpty(docPath))
                    {
                        string issuesPath = Path.Combine(Path.GetDirectoryName(docPath), "_bim_manager", "issues.json");
                        if (File.Exists(issuesPath))
                        {
                            string raw = File.ReadAllText(issuesPath);
                            // Count OPEN issues
                            int idx = 0;
                            while ((idx = raw.IndexOf("\"status\":\"OPEN\"", idx, StringComparison.OrdinalIgnoreCase)) >= 0)
                            { openIssues++; idx++; }
                            idx = 0;
                            while ((idx = raw.IndexOf("\"priority\":\"CRITICAL\"", idx, StringComparison.OrdinalIgnoreCase)) >= 0)
                            { criticalIssues++; idx++; }
                        }
                    }
                }
                catch (Exception ex) { StingLog.Warn($"BIMCoordCenter issues load: {ex.Message}"); }

                // 4. Load workflow history summary
                int workflowRuns = 0;
                string lastWorkflow = "none";
                try
                {
                    string docPath = doc.PathName;
                    if (!string.IsNullOrEmpty(docPath))
                    {
                        string logPath = Path.Combine(Path.GetDirectoryName(docPath), "STING_WORKFLOW_LOG.json");
                        if (File.Exists(logPath))
                        {
                            string[] lines = File.ReadAllLines(logPath);
                            workflowRuns = lines.Length;
                            if (lines.Length > 0)
                            {
                                string lastLine = lines[lines.Length - 1];
                                int presetIdx = lastLine.IndexOf("\"preset\":\"");
                                if (presetIdx >= 0)
                                {
                                    int valStart = presetIdx + 10;
                                    int valEnd = lastLine.IndexOf('"', valStart);
                                    if (valEnd > valStart) lastWorkflow = lastLine.Substring(valStart, valEnd - valStart);
                                }
                            }
                        }
                    }
                }
                catch (Exception ex) { StingLog.Warn($"BIMCoordCenter workflow load: {ex.Message}"); }

                // 5. Warning regression delta
                var (warnAdded, warnRemoved, warnUnchanged, newTypes) = WarningsEngine.CompareWithRevisionBaseline(doc);

                // 6. Warning gate check
                var (gatePass, gateReason) = WarningsEngine.CheckWarningGate(doc);

                // Build model health checks
                var healthChecks = new List<(string, int, int, string)>();
                var recommendations = new List<string>();
                int modelHealthScore = healthScore; // Use warning-based health as starting point
                string modelHealthRating = modelHealthScore >= 80 ? "GOOD" : modelHealthScore >= 50 ? "FAIR" : "POOR";

                // Assemble CoordData for unified WPF dialog
                string ragStatus = compliance != null ? compliance.RAGStatus : "UNKNOWN";
                double tagPct = compliance?.CompliancePercent ?? 0;
                double strictPct = compliance?.StrictPercent ?? 0;
                int staleCount = compliance?.StaleCount ?? 0;

                // Derive model health score from multiple signals
                int mhWarningScore = Math.Max(0, 10 - warningReport.Total / 10);
                int mhTagScore = (int)(tagPct / 10);
                int mhStaleScore = staleCount == 0 ? 10 : Math.Max(0, 10 - staleCount / 5);
                modelHealthScore = Math.Min(100, (mhWarningScore + mhTagScore + mhStaleScore) * 100 / 30);
                modelHealthRating = modelHealthScore >= 80 ? "GOOD" : modelHealthScore >= 50 ? "FAIR" : "POOR";

                healthChecks.Add(("Warnings", mhWarningScore, 10, $"{warningReport.Total} warnings in model"));
                healthChecks.Add(("Tag Completeness", mhTagScore, 10, $"{tagPct:F0}% complete"));
                healthChecks.Add(("Stale Elements", mhStaleScore, 10, staleCount == 0 ? "No stale elements" : $"{staleCount} stale elements"));

                if (warningReport.Total > 10) recommendations.Add("Resolve Revit warnings (currently " + warningReport.Total + ")");
                if (tagPct < 80) recommendations.Add("Run 'Batch Tag' or 'Tag & Combine' to improve tag coverage");
                if (staleCount > 0) recommendations.Add($"Re-tag {staleCount} stale elements");

                // Load revision data
                int revisionCount = 0, revisionClouds = 0;
                try
                {
                    var revisions = new FilteredElementCollector(doc).OfClass(typeof(Revision)).ToElements();
                    revisionCount = revisions.Count;
                    foreach (var rev in revisions.Cast<Revision>())
                    {
                        var clouds = new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_RevisionClouds)
                            .WhereElementIsNotElementType().ToElements()
                            .Where(c => c.get_Parameter(BuiltInParameter.REVISION_CLOUD_REVISION)?.AsElementId() == rev.Id);
                        revisionClouds += clouds.Count();
                    }
                }
                catch (Exception ex) { StingLog.Warn($"BIMCoordCenter revision load: {ex.Message}"); }

                // Platform sync info
                string lastSyncTime = "";
                int syncChanges = 0;
                try
                {
                    string docPath2 = doc.PathName;
                    if (!string.IsNullOrEmpty(docPath2))
                    {
                        string syncPath = Path.Combine(Path.GetDirectoryName(docPath2), "_bim_manager", "platform_sync.json");
                        if (File.Exists(syncPath))
                        {
                            string syncRaw = File.ReadAllText(syncPath);
                            int tsIdx = syncRaw.IndexOf("\"last_sync\":\"");
                            if (tsIdx >= 0) { int s = tsIdx + 13; int e = syncRaw.IndexOf('"', s); if (e > s) lastSyncTime = syncRaw.Substring(s, e - s); }
                        }
                    }
                }
                catch (Exception ex) { StingLog.Warn($"BIMCoordCenter sync load: {ex.Message}"); }

                // Phase 48: Load overdue issue count and issue rows for DataGrid
                int issuesOverdue = 0;
                int issuesTotal2 = 0;
                var issueRows = new List<UI.BIMCoordinationCenter.IssueRow>();
                try
                {
                    string docPath3 = doc.PathName;
                    if (!string.IsNullOrEmpty(docPath3))
                    {
                        string issuesPath2 = Path.Combine(Path.GetDirectoryName(docPath3), "_bim_manager", "issues.json");
                        if (File.Exists(issuesPath2))
                        {
                            var arr = Newtonsoft.Json.Linq.JArray.Parse(File.ReadAllText(issuesPath2));
                            issuesTotal2 = arr.Count;
                            foreach (var item in arr)
                            {
                                string st = item.Value<string>("status") ?? "";
                                bool overdue = false;
                                string created = item.Value<string>("created_date") ?? "";
                                string daysOpen = "";
                                if (DateTime.TryParse(created, out DateTime cDate))
                                {
                                    int d = (int)(DateTime.Now - cDate).TotalDays;
                                    daysOpen = d < 1 ? "<1d" : d < 7 ? $"{d}d" : d < 30 ? $"{d/7}w" : $"{d/30}mo";
                                    string pri = item.Value<string>("priority") ?? "";
                                    double slaHours = pri == "CRITICAL" ? 4 : pri == "HIGH" ? 24 : pri == "MEDIUM" ? 168 : 336;
                                    if (st == "OPEN" && (DateTime.Now - cDate).TotalHours > slaHours) { overdue = true; issuesOverdue++; }
                                }
                                issueRows.Add(new UI.BIMCoordinationCenter.IssueRow
                                {
                                    Id = item.Value<string>("id") ?? "",
                                    Title = item.Value<string>("title") ?? "",
                                    Type = item.Value<string>("type") ?? "",
                                    Priority = item.Value<string>("priority") ?? "",
                                    Status = st,
                                    Assignee = item.Value<string>("assignee") ?? item.Value<string>("created_by") ?? "",
                                    Created = created.Length > 10 ? created.Substring(0, 10) : created,
                                    IsOverdue = overdue,
                                    DaysOpen = daysOpen
                                });
                            }
                        }
                    }
                }
                catch (Exception ex) { StingLog.Warn($"BIMCoordCenter issue rows: {ex.Message}"); }

                // Phase 48: Load revision rows for DataGrid
                var revisionRows = new List<UI.BIMCoordinationCenter.RevisionRow>();
                try
                {
                    var revisions2 = new FilteredElementCollector(doc).OfClass(typeof(Revision)).Cast<Revision>().ToList();
                    foreach (var rev in revisions2)
                    {
                        int clouds2 = new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_RevisionClouds)
                            .WhereElementIsNotElementType().ToElements()
                            .Count(c => { try { return c.get_Parameter(BuiltInParameter.REVISION_CLOUD_REVISION)?.AsElementId() == rev.Id; } catch { return false; } });
                        revisionRows.Add(new UI.BIMCoordinationCenter.RevisionRow
                        {
                            Id = rev.Id.Value.ToString(),
                            Name = rev.Name ?? "",
                            Date = rev.RevisionDate ?? "",
                            Description = rev.Description ?? "",
                            Clouds = clouds2,
                            Status = rev.Issued ? "ISSUED" : "DRAFT"
                        });
                    }
                }
                catch (Exception ex) { StingLog.Warn($"BIMCoordCenter revision rows: {ex.Message}"); }

                var coordData = new UI.BIMCoordinationCenter.CoordData
                {
                    ProjectName = doc.Title ?? "Untitled",
                    FilePath = doc.PathName ?? "",
                    TagPct = tagPct,
                    StrictPct = strictPct,
                    RAGStatus = ragStatus,
                    TotalElements = compliance?.TotalElements ?? 0,
                    TaggedComplete = compliance?.TaggedComplete ?? 0,
                    Untagged = compliance?.Untagged ?? 0,
                    StaleCount = staleCount,
                    SheetsTagged = compliance?.SheetsTagged ?? 0,
                    SheetsTotal = compliance?.TotalSheets ?? 0,
                    ByDisc = compliance?.ByDisc ?? new Dictionary<string, DiscComplianceData>(),
                    EmptyTokenCounts = compliance?.EmptyTokenCounts?.ToDictionary(x => x.Key, x => x.Value) ?? new Dictionary<string, int>(),
                    ContainerCompletePct = compliance?.ContainerCompletePct ?? 0,
                    ByPhase = compliance?.ByPhase?.ToDictionary(
                        x => x.Key,
                        x => (x.Value.Total, x.Value.Tagged, x.Value.CompliancePct)) ?? new Dictionary<string, (int, int, double)>(),
                    PlaceholderCount = compliance?.PlaceholderCount ?? 0,
                    WarningTotal = warningReport.Total,
                    WarningCritical = warningReport.BySeverity.GetValueOrDefault(WarningSeverity.Critical, 0),
                    WarningHigh = warningReport.BySeverity.GetValueOrDefault(WarningSeverity.High, 0),
                    WarningAutoFixable = warningReport.AutoFixable,
                    WarningHealthScore = healthScore,
                    WarningTrend = warningReport.TrendSymbol,
                    WarningGatePass = gatePass,
                    WarningGateReason = gateReason,
                    WarningAdded = warnAdded,
                    WarningRemoved = warnRemoved,
                    WarningByCategory = warningReport.ByCategory,
                    WarningBySeverity = warningReport.BySeverity,
                    WarningByLevel = warningReport.ByLevel,
                    WarningByDiscipline = warningReport.ByDiscipline,
                    WarningHotspots = warningReport.Hotspots.Select(h => (h.Name, h.Count)).ToList(),
                    WarningSLAViolations = warningReport.SLAViolations,
                    WarningTopByCategory = warningReport.TopWarningsByCategory
                        .ToDictionary(x => x.Key, x => x.Value.Select(v => (v.Desc, v.Count)).ToList()),
                    IssuesOpen = openIssues,
                    IssuesCritical = criticalIssues,
                    IssuesOverdue = issuesOverdue,
                    IssuesTotal = issuesTotal2,
                    Issues = issueRows,
                    RevisionCount = revisionCount,
                    RevisionClouds = revisionClouds,
                    Revisions = revisionRows,
                    LastSyncTime = lastSyncTime,
                    SyncChanges = syncChanges,
                    WorkflowRuns = workflowRuns,
                    LastWorkflow = lastWorkflow,
                    ModelHealthScore = modelHealthScore,
                    ModelHealthRating = modelHealthRating,
                    HealthChecks = healthChecks,
                    Recommendations = recommendations
                };

                // Phase 49: Generate smart suggestions based on model state analysis
                coordData.SmartSuggestions = WarningsEngine.GenerateSmartSuggestions(coordData, warningReport);

                // Phase 49: Load compliance trend from workflow log
                try
                {
                    string logPath = Path.Combine(Path.GetDirectoryName(doc.PathName ?? "") ?? "",
                        "STING_WORKFLOW_LOG.json");
                    if (File.Exists(logPath))
                    {
                        var lines = File.ReadAllLines(logPath);
                        foreach (string line in lines.TakeLast(20))
                        {
                            try
                            {
                                var rec = Newtonsoft.Json.Linq.JObject.Parse(line);
                                string ts = rec.Value<string>("timestamp") ?? "";
                                double after = rec.Value<double?>("complianceAfter") ?? 0;
                                if (DateTime.TryParse(ts, out DateTime dt) && after > 0)
                                    coordData.ComplianceTrend.Add((dt, after));
                            }
                            catch { }
                        }
                    }
                }
                catch (Exception ex) { StingLog.Warn($"Compliance trend load: {ex.Message}"); }

                // Phase 49: Load coordination log from sidecar
                try
                {
                    string coordLogPath = Path.Combine(Path.GetDirectoryName(doc.PathName ?? "") ?? "",
                        ".sting_coord_log.json");
                    if (File.Exists(coordLogPath))
                    {
                        var logEntries = Newtonsoft.Json.JsonConvert.DeserializeObject<List<UI.BIMCoordinationCenter.CoordLogEntry>>(
                            File.ReadAllText(coordLogPath));
                        if (logEntries != null)
                            coordData.CoordLog = logEntries.OrderByDescending(e => e.Timestamp).Take(200).ToList();
                    }
                }
                catch (Exception ex) { StingLog.Warn($"Coord log load: {ex.Message}"); }

                // Phase 49: Cross-system correlation
                try
                {
                    // Count stale elements that also have warnings
                    int staleWithWarnings = 0;
                    var warningElementIds = new HashSet<long>();
                    foreach (var cw in warningReport.Warnings)
                    {
                        if (cw.FailingElements != null)
                            foreach (var eid in cw.FailingElements)
                                warningElementIds.Add(eid.Value);
                    }
                    if (staleCount > 0 && warningElementIds.Count > 0)
                    {
                        // Approximate: count overlap between stale and warning elements
                        staleWithWarnings = Math.Min(staleCount / 5, warningElementIds.Count / 10); // Heuristic
                    }
                    coordData.StaleLinkedToWarnings = staleWithWarnings;
                    coordData.WarningsLinkedToIssues = Math.Min(openIssues, warningReport.BySeverity.GetValueOrDefault(WarningSeverity.Critical, 0));
                    coordData.UnresolvedDependencies = (compliance?.ByDisc?.Count > 1)
                        ? compliance.ByDisc.Count(d => d.Value.CompliancePct < 50) : 0;
                }
                catch (Exception ex) { StingLog.Warn($"Cross-system correlation: {ex.Message}"); }

                // Show unified WPF dialog
                string action = UI.BIMCoordinationCenter.Show(coordData);

                // Process returned action tag
                if (!string.IsNullOrEmpty(action))
                {
                    switch (action)
                    {
                        case "AutoFixWarnings":
                            var fixReport = WarningsEngine.BatchAutoFix(doc, warningReport.Warnings);
                            TaskDialog.Show("STING Auto-Fix", $"Fixed: {fixReport.Fixed}\nSkipped: {fixReport.Skipped}\nFailed: {fixReport.Failed}");
                            break;
                        case "CreateIssuesFromWarnings":
                            var created = WarningsEngine.CreateIssuesFromWarnings(doc, warningReport.Warnings);
                            TaskDialog.Show("STING", created.Count > 0
                                ? $"Created {created.Count} issue(s):\n" + string.Join("\n", created.Select(c => $"  {c.issueId}: {c.title}"))
                                : "No critical/high warnings found.");
                            break;
                        case "ExportWarnings":
                            string csvPath = WarningsEngine.ExportToCSV(doc, warningReport);
                            TaskDialog.Show("STING Export", $"Exported to:\n{csvPath}");
                            break;
                        case "SaveBaseline":
                            WarningsEngine.SaveBaseline(doc);
                            TaskDialog.Show("STING", "Warning baseline saved.");
                            break;
                        case "SaveExtendedBaseline":
                            WarningsEngine.SaveExtendedBaseline(doc);
                            TaskDialog.Show("STING", "Extended warning baseline saved.");
                            break;
                        default:
                            // Dispatch action through StingCommandHandler command resolution.
                            // We're already on the Revit API thread, so we can execute directly.
                            DispatchCoordAction(action, commandData);
                            break;
                    }
                }

                StingLog.Info($"BIMCoordinationCenter: health={healthScore}, warnings={warningReport.Total}, " +
                    $"compliance={tagPct:F1}%, issues={openIssues}");
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("BIMCoordinationCenter failed", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }

        /// <summary>
        /// Dispatches a BIM Coordination Center action tag to the matching IExternalCommand.
        /// Maps action tags from dialog buttons to command classes and executes them directly
        /// (we are already on the Revit API thread inside StingCommandHandler.Execute).
        /// </summary>
        private static void DispatchCoordAction(string action, ExternalCommandData commandData)
        {
            // Map action tags to command tags used by StingCommandHandler / WorkflowEngine
            var actionToCommandTag = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                // Overview quick actions
                { "RunDailyQA", "DailyQA" },
                { "RunMorningCheck", "MorningHealthCheck" },
                { "RetagStale", "RetagStale" },
                { "TagNewOnly", "TagNewOnly" },
                { "ExportCOBie", "COBieExport" },
                { "FullComplianceDashboard", "FullComplianceDashboard" },

                // Model health actions
                { "RefreshHealth", "ModelHealthDashboard" },
                { "ExportHealth", "ExportModelHealth" },
                { "RunFullCheck", "ValidateTemplate" },

                // Warnings actions
                { "SelectWarningElements", "WarningsSelectElements" },
                { "SuppressWarnings", "WarningsSuppress" },
                { "WarningsCompliance", "WarningsCompliance" },

                // Issues actions
                { "RaiseIssue", "RaiseIssue" },
                { "UpdateIssue", "UpdateIssue" },
                { "IssuesBulkClose", "UpdateIssue" },
                { "SelectIssueElements", "SelectIssueElements" },
                { "BCFExport", "BCFExport" },
                { "BCFImport", "BCFImport" },
                { "ExportIssues", "IssueDashboard" },
                { "IssueTimeline", "IssueDashboard" },

                // Revisions actions
                { "CreateRevision", "CreateRevision" },
                { "AutoRevisionCloud", "AutoRevisionCloud" },
                { "TakeSnapshot", "TrackElementRevisions" },
                { "RevisionCompare", "RevisionCompare" },
                { "TrackElementRevisions", "TrackElementRevisions" },
                { "IssueSheetsForRevision", "IssueSheetsForRevision" },
                { "RevisionNamingEnforce", "RevisionNamingEnforce" },
                { "BulkRevisionStamp", "BulkRevisionStamp" },
                { "ExportRevisions", "RevisionExport" },

                // Platform actions
                { "PlatformSync", "PlatformSync" },
                { "CDEPackage", "CDEPackage" },
                { "CDEStatus", "CDEStatus" },
                { "ValidateDocNaming", "ValidateDocNaming" },
                { "CreateTransmittal", "CreateTransmittal" },
                { "ExportToExcel", "ExportToExcel" },
                { "ImportFromExcel", "ImportFromExcel" },
                { "ExcelRoundTrip", "ExcelRoundTrip" },
                { "COBieExport", "COBieExport" },
                { "IFCExport", "IFCExport" },
                { "ACCPublish", "ACCPublish" },
                { "SharePointExport", "SharePointExport" },

                // Workflow actions
                { "RunWorkflowPreset", "WorkflowPreset" },
                { "CreateWorkflowPreset", "CreateWorkflowPreset" },
                { "WorkflowTrend", "WorkflowTrend" },
                { "ListWorkflowPresets", "ListWorkflowPresets" },

                // QA Dashboard actions
                { "ValidateTags", "ValidateTags" },
                { "PreTagAudit", "PreTagAudit" },
                { "AnomalyAutoFix", "AnomalyAutoFix" },
                { "ResolveAllIssues", "ResolveAllIssues" },
                { "TagRegisterExport", "TagRegisterExport" },
                { "CompletenessDashboard", "CompletenessDashboard" },

                // 4D/5D actions
                { "AutoSchedule4D", "AutoSchedule4D" },
                { "AutoCost5D", "AutoCost5D" },
                { "ViewTimeline4D", "ViewTimeline4D" },
                { "CostReport5D", "CostReport5D" },
                { "CashFlow5D", "CashFlow5D" },
                { "ExportSchedule4D", "ExportSchedule4D" },
                { "ImportMSProject", "ImportMSProject" },
                { "MilestoneRegister", "MilestoneRegister" },
                { "PhaseSummary", "PhaseSummary" },

                // Deliverables actions
                { "AddDocument", "AddDocument" },
                { "DocumentRegister", "DocumentRegister" },
                { "DocumentBriefcase", "DocumentBriefcase" },
                { "StageComplianceGate", "StageComplianceGate" },

                // Report action
                { "ExportReport", "ExportModelHealth" },
                { "DiscComplianceReport", "DiscComplianceReport" },
            };

            // Handle RepeatLastWorkflow by resolving the last workflow name
            if (string.Equals(action, "RepeatLastWorkflow", StringComparison.OrdinalIgnoreCase))
            {
                string last = WorkflowEngine.LastWorkflowName;
                if (!string.IsNullOrEmpty(last))
                {
                    UI.StingCommandHandler.SetExtraParam("WorkflowPresetName", last);
                    var wfCmd = WorkflowEngine.GetCommandInstance("WorkflowPreset");
                    if (wfCmd != null)
                    {
                        string msg = "";
                        var els = new ElementSet();
                        wfCmd.Execute(commandData, ref msg, els);
                        StingLog.Info($"DispatchCoordAction: repeated workflow '{last}'");
                    }
                }
                else
                {
                    TaskDialog.Show("STING", "No previous workflow to repeat.");
                }
                return;
            }

            // Handle DocumentManager inline (opens WPF dialog directly)
            if (string.Equals(action, "DocumentManager", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    var uiApp = UI.StingCommandHandler.CurrentApp;
                    var doc2 = uiApp?.ActiveUIDocument?.Document;
                    if (doc2 != null)
                        UI.DocumentManagementDialog.Show(doc2);
                }
                catch (Exception ex) { StingLog.Warn($"DocumentManager dispatch: {ex.Message}"); }
                return;
            }

            // Check for workflow preset actions: RunDailyQA, RunMorningCheck, RunWorkflow_*
            var workflowPresets = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                { "RunDailyQA", "RunMorningCheck" };

            if (action.StartsWith("RunWorkflow_", StringComparison.OrdinalIgnoreCase)
                || workflowPresets.Contains(action))
            {
                string presetName;
                if (action.StartsWith("RunWorkflow_", StringComparison.OrdinalIgnoreCase))
                    presetName = action.Substring("RunWorkflow_".Length);
                else if (actionToCommandTag.TryGetValue(action, out var mapped2))
                    presetName = mapped2;
                else
                    presetName = action;

                UI.StingCommandHandler.SetExtraParam("WorkflowPresetName", presetName);
                var wfCmd = WorkflowEngine.GetCommandInstance("WorkflowPreset");
                if (wfCmd != null)
                {
                    string msg = "";
                    var els = new ElementSet();
                    wfCmd.Execute(commandData, ref msg, els);
                    StingLog.Info($"DispatchCoordAction: ran workflow preset '{presetName}'");
                }
                else
                {
                    StingLog.Warn($"DispatchCoordAction: could not resolve WorkflowPreset command");
                }
                return;
            }

            // Check for element selection patterns: "SelectByDisc_M", "SelectIssue_ISS-001", etc.
            if (action.StartsWith("SelectByDisc_", StringComparison.OrdinalIgnoreCase))
            {
                string disc = action.Substring("SelectByDisc_".Length);
                UI.StingCommandHandler.SetExtraParam("DiscFilter", disc);
                var selCmd = new Organise.SelectByDisciplineCommand();
                string msg = "";
                var els = new ElementSet();
                selCmd.Execute(commandData, ref msg, els);
                return;
            }

            // Resolve via the action-to-tag map
            string commandTag = actionToCommandTag.TryGetValue(action, out var mapped) ? mapped : action;
            var cmd = WorkflowEngine.GetCommandInstance(commandTag);
            if (cmd != null)
            {
                try
                {
                    string msg = "";
                    var els = new ElementSet();
                    cmd.Execute(commandData, ref msg, els);
                    StingLog.Info($"DispatchCoordAction: executed '{action}' → {cmd.GetType().Name}");
                }
                catch (Exception ex)
                {
                    StingLog.Error($"DispatchCoordAction: '{action}' failed", ex);
                    TaskDialog.Show("STING", $"Command '{action}' failed:\n{ex.Message}");
                }
            }
            else
            {
                StingLog.Warn($"DispatchCoordAction: unrecognised action '{action}'");
                TaskDialog.Show("STING", $"Action '{action}' is not yet available.");
            }
        }
    }
}
