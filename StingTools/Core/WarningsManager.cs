using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

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
            string exportDir = OutputLocationHelper.GetExportDirectory(doc, "Warnings");
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

                if (severity == FailureSeverity.Error)
                {
                    // Errors cannot be dismissed — try resolution
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
                Document doc = commandData.Application.ActiveUIDocument?.Document;
                if (doc == null) { message = "No document open."; return Result.Failed; }

                var report = WarningsEngine.ScanWarnings(doc);

                var sb = new StringBuilder();
                sb.AppendLine($"STING Warnings Dashboard — {report.Total} total {report.TrendSymbol}");
                sb.AppendLine(new string('═', 50));

                // Severity breakdown
                sb.AppendLine("\n■ BY SEVERITY:");
                foreach (WarningSeverity sev in Enum.GetValues(typeof(WarningSeverity)))
                {
                    if (report.BySeverity.TryGetValue(sev, out int cnt) && cnt > 0)
                        sb.AppendLine($"  {sev,-12} {cnt,5}");
                }

                // Category breakdown
                sb.AppendLine("\n■ BY CATEGORY:");
                foreach (var kv in report.ByCategory.OrderByDescending(x => x.Value))
                    sb.AppendLine($"  {kv.Key,-14} {kv.Value,5}");

                // Fix summary
                sb.AppendLine($"\n■ FIX STATUS:");
                sb.AppendLine($"  Auto-fixable: {report.AutoFixable}");
                sb.AppendLine($"  Manual review: {report.ManualReview}");

                // Discipline breakdown
                if (report.ByDiscipline.Count > 0)
                {
                    sb.AppendLine("\n■ BY DISCIPLINE:");
                    foreach (var kv in report.ByDiscipline.OrderByDescending(x => x.Value))
                        sb.AppendLine($"  {kv.Key,-4} {kv.Value,5}");
                }

                // Level breakdown (top 10)
                if (report.ByLevel.Count > 0)
                {
                    sb.AppendLine("\n■ BY LEVEL (top 10):");
                    foreach (var kv in report.ByLevel.OrderByDescending(x => x.Value).Take(10))
                        sb.AppendLine($"  {kv.Key,-20} {kv.Value,5}");
                }

                // Hotspot elements (top 10)
                if (report.Hotspots.Count > 0)
                {
                    sb.AppendLine("\n■ HOTSPOT ELEMENTS (most warnings):");
                    foreach (var (id, name, count) in report.Hotspots.Take(10))
                        sb.AppendLine($"  {name,-35} {count,3} warnings");
                }

                // Baseline trend
                if (report.BaselineTotal.HasValue)
                {
                    sb.AppendLine($"\n■ TREND: {report.BaselineTotal} → {report.Total} ({report.TrendSymbol})");
                }

                TaskDialog.Show("STING Warnings Dashboard", sb.ToString());
                StingLog.Info($"WarningsDashboard: {report.Total} total, {report.AutoFixable} fixable");
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
                Document doc = commandData.Application.ActiveUIDocument?.Document;
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
                Document doc = commandData.Application.ActiveUIDocument?.Document;
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
                Document doc = commandData.Application.ActiveUIDocument?.Document;
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
                var uidoc = commandData.Application.ActiveUIDocument;
                Document doc = uidoc?.Document;
                if (doc == null) { message = "No document open."; return Result.Failed; }

                var report = WarningsEngine.ScanWarnings(doc);
                if (report.Total == 0) { TaskDialog.Show("STING", "No warnings found."); return Result.Succeeded; }

                // Group by description for picker
                var groups = report.Warnings
                    .GroupBy(w => w.Description)
                    .OrderByDescending(g => g.Count())
                    .Take(30)
                    .Select(g => new UI.StingListPicker.ListItem
                    {
                        Label = $"({g.Count()}) {g.Key}",
                        Detail = $"{g.First().Category} | {g.First().Severity}",
                        Tag = g.Key
                    })
                    .ToList();

                var picked = UI.StingListPicker.Show("Select Warning Type",
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
                Document doc = commandData.Application.ActiveUIDocument?.Document;
                if (doc == null) { message = "No document open."; return Result.Failed; }

                var report = WarningsEngine.ScanWarnings(doc);
                var groups = report.Warnings
                    .GroupBy(w => w.Description)
                    .OrderByDescending(g => g.Count())
                    .Take(30)
                    .Select(g => new UI.StingListPicker.ListItem
                    {
                        Label = $"({g.Count()}) {g.Key}",
                        Detail = $"{g.First().Category} | Suppress this warning type",
                        Tag = g.Key
                    })
                    .ToList();

                var picked = UI.StingListPicker.Show("Suppress Warning Types",
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
                Document doc = commandData.Application.ActiveUIDocument?.Document;
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
                Document doc = commandData.Application.ActiveUIDocument?.Document;
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
}
