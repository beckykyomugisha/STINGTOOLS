using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json;
using StingTools.Core;
using StingTools.Select;
using StingTools.UI;
using Newtonsoft.Json.Linq;
using System.Text.RegularExpressions;
using Autodesk.Revit.DB.Architecture;
using RegexGroup = System.Text.RegularExpressions.Group;

namespace StingTools.BIMManager
{
    // ════════════════════════════════════════════════════════════════════════════
    //  STING Quality Assurance Commands
    //
    //  G1:  Warning Review — scan, categorise, and resolve Revit warnings
    //  G2:  Rule Engine — custom validation rules from JSON config
    //  G5:  Model Health Dashboard (expanded) — weighted scoring across 12 metrics
    //  G9:  QA Report — unified multi-check compliance report with CSV/JSON export
    //  G11: Setup Validation — verify project setup against BEP/template requirements
    // ════════════════════════════════════════════════════════════════════════════

    #region ── Internal Engine: QualityAssuranceEngine ──

    internal static class QualityAssuranceEngine
    {
        // ── Warning categorisation by severity ──
        internal static readonly Dictionary<string, string> WarningSeverity = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Elements have duplicate \"Mark\" values"] = "High",
            ["Room is not in a properly enclosed region"] = "High",
            ["Highlighted walls overlap"] = "High",
            ["One element is completely inside another"] = "High",
            ["Room Tag is outside of its Room"] = "Medium",
            ["Elements are slightly off axis"] = "Medium",
            ["There are identical instances in the same place"] = "High",
            ["Area is not in a properly enclosed region"] = "High",
            ["Multiple Rooms are in the same enclosed region"] = "High",
            ["Beam or Brace is not joined"] = "Low",
            ["Room Separation Line is slightly off axis"] = "Low",
        };

        internal static string CategorizeSeverity(string warningText)
        {
            foreach (var kvp in WarningSeverity)
                if (warningText.IndexOf(kvp.Key, StringComparison.OrdinalIgnoreCase) >= 0)
                    return kvp.Value;
            return "Info";
        }

        /// <summary>
        /// Collect all Revit warnings grouped by description.
        /// </summary>
        internal static List<WarningGroup> CollectWarnings(Document doc)
        {
            var warnings = doc.GetWarnings();
            var groups = new Dictionary<string, WarningGroup>(StringComparer.OrdinalIgnoreCase);
            foreach (var w in warnings)
            {
                string desc = w.GetDescriptionText() ?? "(no description)";
                if (!groups.TryGetValue(desc, out var g))
                {
                    g = new WarningGroup { Description = desc, Severity = CategorizeSeverity(desc) };
                    groups[desc] = g;
                }
                g.Count++;
                foreach (var eid in w.GetFailingElements())
                    g.ElementIds.Add(eid);
                foreach (var eid in w.GetAdditionalElements())
                    g.ElementIds.Add(eid);
            }
            return groups.Values.OrderByDescending(g => SeverityRank(g.Severity)).ThenByDescending(g => g.Count).ToList();
        }

        internal static int SeverityRank(string severity) => severity switch
        {
            "High" => 3,
            "Medium" => 2,
            "Low" => 1,
            _ => 0
        };

        /// <summary>
        /// Calculate model health score (0-100) across 12 weighted metrics.
        /// </summary>
        internal static ModelHealthResult CalculateModelHealth(Document doc)
        {
            var result = new ModelHealthResult();
            var sw = System.Diagnostics.Stopwatch.StartNew();

            // 1. Warnings count (weight 15)
            int warningCount = doc.GetWarnings().Count;
            result.Metrics.Add(new HealthMetric("Warnings", warningCount == 0 ? 100 : Math.Max(0, 100 - warningCount), 15,
                $"{warningCount} warnings"));

            // 2. Tag compliance (weight 15)
            var comp = ComplianceScan.Scan(doc);
            int tagPct = comp != null ? (int)comp.CompliancePercent : 0;
            result.Metrics.Add(new HealthMetric("Tag Compliance", tagPct, 15, $"{tagPct}% tagged"));

            // 3. Unused views (weight 10)
            var allViews = new FilteredElementCollector(doc).OfClass(typeof(View)).Cast<View>()
                .Where(v => !v.IsTemplate && v.CanBePrinted).ToList();
            var placedIds = new HashSet<ElementId>(new FilteredElementCollector(doc).OfClass(typeof(ViewSheet))
                .Cast<ViewSheet>().SelectMany(s => s.GetAllPlacedViews()));
            int unusedViews = allViews.Count(v => !placedIds.Contains(v.Id));
            int unusedPct = allViews.Count > 0 ? (int)(100.0 * (allViews.Count - unusedViews) / allViews.Count) : 100;
            result.Metrics.Add(new HealthMetric("View Placement", unusedPct, 10, $"{unusedViews} unplaced views"));

            // 4. Room enclosure (weight 10)
            var rooms = new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_Rooms)
                .WhereElementIsNotElementType().Cast<Autodesk.Revit.DB.Architecture.Room>().ToList();
            int enclosed = rooms.Count(r => r.Area > 0);
            int roomPct = rooms.Count > 0 ? (int)(100.0 * enclosed / rooms.Count) : 100;
            result.Metrics.Add(new HealthMetric("Room Enclosure", roomPct, 10, $"{rooms.Count - enclosed} unenclosed rooms"));

            // 5. Workset usage (weight 5)
            bool workshared = doc.IsWorkshared;
            int worksetScore = workshared ? 100 : 50;
            result.Metrics.Add(new HealthMetric("Worksharing", worksetScore, 5, workshared ? "Enabled" : "Not enabled"));

            // 6. Template coverage (weight 10)
            int viewsWithTemplate = allViews.Count(v => v.ViewTemplateId != ElementId.InvalidElementId);
            int templatePct = allViews.Count > 0 ? (int)(100.0 * viewsWithTemplate / allViews.Count) : 100;
            result.Metrics.Add(new HealthMetric("View Templates", templatePct, 10, $"{viewsWithTemplate}/{allViews.Count} views have templates"));

            // 7. Linked models (weight 5)
            var links = new FilteredElementCollector(doc).OfClass(typeof(RevitLinkInstance)).GetElementCount();
            int linkScore = links <= 20 ? 100 : Math.Max(0, 100 - (links - 20) * 5);
            result.Metrics.Add(new HealthMetric("Linked Models", linkScore, 5, $"{links} links loaded"));

            // 8. Import instances (weight 5)
            var imports = new FilteredElementCollector(doc).OfClass(typeof(ImportInstance)).GetElementCount();
            int importScore = imports == 0 ? 100 : Math.Max(0, 100 - imports * 10);
            result.Metrics.Add(new HealthMetric("CAD Imports", importScore, 5, $"{imports} CAD imports"));

            // 9. In-place families (weight 5)
            var inPlace = new FilteredElementCollector(doc).OfClass(typeof(FamilyInstance)).Cast<FamilyInstance>()
                .Count(fi => fi.Symbol?.Family?.IsInPlace == true);
            int inPlaceScore = inPlace == 0 ? 100 : Math.Max(0, 100 - inPlace * 2);
            result.Metrics.Add(new HealthMetric("In-Place Families", inPlaceScore, 5, $"{inPlace} in-place families"));

            // 10. Groups (weight 5)
            var groups = new FilteredElementCollector(doc).OfClass(typeof(Group)).GetElementCount();
            int groupScore = groups <= 5 ? 100 : Math.Max(0, 100 - (groups - 5) * 3);
            result.Metrics.Add(new HealthMetric("Groups", groupScore, 5, $"{groups} groups"));

            // 11. Design options (weight 5)
            var designOpts = new FilteredElementCollector(doc).OfClass(typeof(DesignOption)).GetElementCount();
            int optScore = designOpts == 0 ? 100 : Math.Max(0, 100 - designOpts * 10);
            result.Metrics.Add(new HealthMetric("Design Options", optScore, 5, $"{designOpts} design options"));

            // 12. File size proxy via element count (weight 5)
            int totalElements = new FilteredElementCollector(doc).WhereElementIsNotElementType().GetElementCount();
            int sizeScore = totalElements < 500000 ? 100 : Math.Max(0, 100 - (totalElements - 500000) / 10000);
            result.Metrics.Add(new HealthMetric("Model Size", sizeScore, 5, $"{totalElements:N0} elements"));

            // Calculate weighted score
            double totalWeight = result.Metrics.Sum(m => m.Weight);
            result.OverallScore = totalWeight > 0
                ? (int)(result.Metrics.Sum(m => m.Score * m.Weight) / totalWeight)
                : 0;
            result.Grade = result.OverallScore >= 80 ? "A" : result.OverallScore >= 60 ? "B" : result.OverallScore >= 40 ? "C" : "D";
            sw.Stop();
            result.Duration = sw.Elapsed;
            return result;
        }

        /// <summary>
        /// Validate project setup against expected standards.
        /// </summary>
        internal static List<SetupCheck> ValidateProjectSetup(Document doc)
        {
            var checks = new List<SetupCheck>();

            // 1. Project info fields
            var pi = doc.ProjectInformation;
            checks.Add(new SetupCheck("Project Name", !string.IsNullOrWhiteSpace(pi.Name), pi.Name ?? "(empty)"));
            checks.Add(new SetupCheck("Project Number", !string.IsNullOrWhiteSpace(pi.Number), pi.Number ?? "(empty)"));
            checks.Add(new SetupCheck("Client Name", !string.IsNullOrWhiteSpace(pi.ClientName), pi.ClientName ?? "(empty)"));
            checks.Add(new SetupCheck("Project Address", !string.IsNullOrWhiteSpace(pi.Address), pi.Address ?? "(empty)"));

            // 2. Shared parameters bound
            int stingParams = 0;
            try
            {
                var iter = doc.ParameterBindings.ForwardIterator();
                while (iter.MoveNext())
                {
                    if (iter.Key is InternalDefinition def && (def.Name.StartsWith("ASS_") || def.Name.StartsWith("STING_")))
                        stingParams++;
                }
            }
            catch (Exception ex) { StingLog.Warn($"SetupValidation param check: {ex.Message}"); }
            checks.Add(new SetupCheck("STING Parameters", stingParams > 50, $"{stingParams} STING parameters bound"));

            // 3. Levels defined
            int levels = new FilteredElementCollector(doc).OfClass(typeof(Level)).GetElementCount();
            checks.Add(new SetupCheck("Levels", levels >= 1, $"{levels} levels"));

            // 4. Grids defined
            int grids = new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_Grids)
                .WhereElementIsNotElementType().GetElementCount();
            checks.Add(new SetupCheck("Grids", grids >= 2, $"{grids} grids"));

            // 5. View templates
            int templates = new FilteredElementCollector(doc).OfClass(typeof(View)).Cast<View>().Count(v => v.IsTemplate);
            checks.Add(new SetupCheck("View Templates", templates >= 1, $"{templates} templates"));

            // 6. Worksharing
            checks.Add(new SetupCheck("Worksharing", doc.IsWorkshared, doc.IsWorkshared ? "Enabled" : "Not enabled"));

            // 7. Starting view
            checks.Add(new SetupCheck("Starting View", doc.ActiveView != null, doc.ActiveView?.Name ?? "(none)"));

            // 8. Data files accessible
            string dataPath = StingToolsApp.DataPath;
            bool dataOk = !string.IsNullOrEmpty(dataPath) && Directory.Exists(dataPath);
            checks.Add(new SetupCheck("Data Files", dataOk, dataOk ? dataPath : "Data directory not found"));

            return checks;
        }

        /// <summary>Load custom validation rules from project_config.json or QA_RULES.json.</summary>
        internal static List<ValidationRule> LoadCustomRules(Document doc)
        {
            var rules = new List<ValidationRule>();
            try
            {
                string rulesFile = StingToolsApp.FindDataFile("QA_RULES.json");
                if (!string.IsNullOrEmpty(rulesFile))
                {
                    string json = File.ReadAllText(rulesFile);
                    var arr = Newtonsoft.Json.Linq.JArray.Parse(json);
                    foreach (var item in arr)
                    {
                        rules.Add(new ValidationRule
                        {
                            Name = item.Value<string>("name") ?? "Rule",
                            Description = item.Value<string>("description") ?? "",
                            Category = item.Value<string>("category") ?? "",
                            Parameter = item.Value<string>("parameter") ?? "",
                            Operator = item.Value<string>("operator") ?? "not_empty",
                            Value = item.Value<string>("value") ?? "",
                            Severity = item.Value<string>("severity") ?? "Warning"
                        });
                    }
                }
            }
            catch (Exception ex) { StingLog.Warn($"LoadCustomRules: {ex.Message}"); }
            return rules;
        }

        /// <summary>Run a single validation rule against document elements.</summary>
        internal static RuleResult EvaluateRule(Document doc, ValidationRule rule)
        {
            var result = new RuleResult { Rule = rule };
            try
            {
                IEnumerable<Element> elements;
                if (!string.IsNullOrWhiteSpace(rule.Category))
                {
                    var cat = doc.Settings.Categories.Cast<Category>()
                        .FirstOrDefault(c => c.Name.Equals(rule.Category, StringComparison.OrdinalIgnoreCase));
                    if (cat == null) { result.Skipped = true; return result; }
                    elements = new FilteredElementCollector(doc).OfCategoryId(cat.Id)
                        .WhereElementIsNotElementType().ToList();
                }
                else
                {
                    elements = new FilteredElementCollector(doc).WhereElementIsNotElementType()
                        .Where(e => e.Category != null).Take(50000);
                }

                foreach (var el in elements)
                {
                    result.TotalChecked++;
                    string val = ParameterHelpers.GetString(el, rule.Parameter);
                    bool pass = rule.Operator switch
                    {
                        "not_empty" => !string.IsNullOrWhiteSpace(val),
                        "equals" => val.Equals(rule.Value, StringComparison.OrdinalIgnoreCase),
                        "not_equals" => !val.Equals(rule.Value, StringComparison.OrdinalIgnoreCase),
                        "contains" => val.IndexOf(rule.Value, StringComparison.OrdinalIgnoreCase) >= 0,
                        "starts_with" => val.StartsWith(rule.Value, StringComparison.OrdinalIgnoreCase),
                        "greater_than" => double.TryParse(val, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double d) && double.TryParse(rule.Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double t) && d > t,
                        "less_than" => double.TryParse(val, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double d2) && double.TryParse(rule.Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double t2) && d2 < t2,
                        "matches" => System.Text.RegularExpressions.Regex.IsMatch(val, rule.Value),
                        _ => !string.IsNullOrWhiteSpace(val)
                    };
                    if (pass) result.Passed++;
                    else result.FailedElements.Add(el.Id);
                }
            }
            catch (Exception ex) { StingLog.Warn($"EvaluateRule '{rule.Name}': {ex.Message}"); result.Skipped = true; }
            return result;
        }

        /// <summary>Generate unified QA report combining multiple check types.</summary>
        internal static string GenerateQAReport(Document doc, ModelHealthResult health, List<WarningGroup> warnings,
            List<SetupCheck> setupChecks, List<RuleResult> ruleResults)
        {
            var sb = new StringBuilder();
            sb.AppendLine("╔══════════════════════════════════════════════════════╗");
            sb.AppendLine("║         STING Quality Assurance Report              ║");
            sb.AppendLine($"║  {DateTime.Now:yyyy-MM-dd HH:mm}  Grade: {health.Grade}  Score: {health.OverallScore}/100   ║");
            sb.AppendLine("╚══════════════════════════════════════════════════════╝");
            sb.AppendLine();

            // Model Health
            sb.AppendLine("── Model Health ─────────────────────────────────");
            foreach (var m in health.Metrics)
                sb.AppendLine($"  [{(m.Score >= 80 ? "✓" : m.Score >= 50 ? "~" : "✗")}] {m.Name,-20} {m.Score,3}/100  ({m.Detail})");
            sb.AppendLine();

            // Setup Checks
            sb.AppendLine("── Project Setup ────────────────────────────────");
            foreach (var c in setupChecks)
                sb.AppendLine($"  [{(c.Pass ? "✓" : "✗")}] {c.Name,-20} {c.Detail}");
            sb.AppendLine();

            // Warnings Summary
            sb.AppendLine($"── Warnings ({warnings.Sum(w => w.Count)} total) ────────────────────");
            foreach (var w in warnings.Take(10))
                sb.AppendLine($"  [{w.Severity,-6}] {w.Count,4}× {Truncate(w.Description, 55)}");
            if (warnings.Count > 10) sb.AppendLine($"  ... and {warnings.Count - 10} more warning types");
            sb.AppendLine();

            // Custom Rules
            if (ruleResults.Count > 0)
            {
                sb.AppendLine("── Custom Rules ─────────────────────────────────");
                foreach (var r in ruleResults)
                {
                    if (r.Skipped) { sb.AppendLine($"  [SKIP] {r.Rule.Name}"); continue; }
                    int failCount = r.FailedElements.Count;
                    string status = failCount == 0 ? "PASS" : "FAIL";
                    sb.AppendLine($"  [{status}] {r.Rule.Name,-30} {r.Passed}/{r.TotalChecked} passed ({failCount} failed)");
                }
            }

            return sb.ToString();
        }

        private static string Truncate(string s, int max) =>
            s.Length <= max ? s : s.Substring(0, max - 3) + "...";
    }

    // ── Data types ──

    internal class WarningGroup
    {
        public string Description { get; set; } = "";
        public string Severity { get; set; } = "Info";
        public int Count { get; set; }
        public HashSet<ElementId> ElementIds { get; set; } = new HashSet<ElementId>();
    }

    internal class ModelHealthResult
    {
        public int OverallScore { get; set; }
        public string Grade { get; set; } = "D";
        public List<HealthMetric> Metrics { get; set; } = new List<HealthMetric>();
        public TimeSpan Duration { get; set; }
    }

    internal class HealthMetric
    {
        public string Name { get; set; }
        public int Score { get; set; }
        public int Weight { get; set; }
        public string Detail { get; set; }
        public HealthMetric(string name, int score, int weight, string detail)
        { Name = name; Score = Math.Clamp(score, 0, 100); Weight = weight; Detail = detail; }
    }

    internal class SetupCheck
    {
        public string Name { get; set; }
        public bool Pass { get; set; }
        public string Detail { get; set; }
        public SetupCheck(string name, bool pass, string detail)
        { Name = name; Pass = pass; Detail = detail; }
    }

    internal class ValidationRule
    {
        public string Name { get; set; } = "";
        public string Description { get; set; } = "";
        public string Category { get; set; } = "";
        public string Parameter { get; set; } = "";
        public string Operator { get; set; } = "not_empty";
        public string Value { get; set; } = "";
        public string Severity { get; set; } = "Warning";
    }

    internal class RuleResult
    {
        public ValidationRule Rule { get; set; }
        public int TotalChecked { get; set; }
        public int Passed { get; set; }
        public bool Skipped { get; set; }
        public List<ElementId> FailedElements { get; set; } = new List<ElementId>();
    }

    #endregion

    #region ── G1: Warning Review Command ──

    /// <summary>
    /// Scan and categorise all Revit warnings by severity. Select affected elements.
    /// </summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class WarningReviewCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }
            Document doc = ctx.Doc;

            var groups = QualityAssuranceEngine.CollectWarnings(doc);
            if (groups.Count == 0)
            {
                TaskDialog.Show("Warning Review", "No warnings found in the model. Well done!");
                return Result.Succeeded;
            }

            // Build display list
            var items = groups.Select(g => $"[{g.Severity}] ({g.Count}×) {g.Description}").ToList();
            int total = groups.Sum(g => g.Count);

            var picked = StingListPicker.Show($"Model Warnings — {total} Total",
                "Select a warning type to highlight affected elements:", items);

            if (picked != null)
            {
                int idx = items.IndexOf(picked);
                if (idx >= 0 && idx < groups.Count)
                {
                    var uidoc = ctx.UIDoc;
                    uidoc.Selection.SetElementIds(groups[idx].ElementIds.ToList());
                    StingLog.Info($"WarningReview: Selected {groups[idx].ElementIds.Count} elements for '{groups[idx].Description}'");
                }
            }

            return Result.Succeeded;
        }
    }

    /// <summary>Export all warnings to CSV.</summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class WarningExportCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }
            Document doc = ctx.Doc;

            var groups = QualityAssuranceEngine.CollectWarnings(doc);
            string path = OutputLocationHelper.GetTimestampedPath(doc, "WarningExport", ".csv");

            var sb = new StringBuilder();
            sb.AppendLine("Severity,Count,Description,ElementIds");
            foreach (var g in groups)
            {
                string ids = string.Join(";", g.ElementIds.Select(id => id.Value));
                sb.AppendLine($"{g.Severity},{g.Count},\"{g.Description.Replace("\"", "\"\"")}\",\"{ids}\"");
            }
            File.WriteAllText(path, sb.ToString());
            TaskDialog.Show("Warning Export", $"Exported {groups.Sum(g => g.Count)} warnings to:\n{path}");
            StingLog.Info($"WarningExport: {groups.Count} warning types exported to {path}");
            return Result.Succeeded;
        }
    }

    #endregion

    #region ── G2: Custom Rule Engine ──

    /// <summary>
    /// Evaluate custom validation rules from QA_RULES.json against model elements.
    /// </summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class RunCustomRulesCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }
            Document doc = ctx.Doc;

            var rules = QualityAssuranceEngine.LoadCustomRules(doc);
            if (rules.Count == 0)
            {
                TaskDialog.Show("Custom Rules", "No rules found.\n\nCreate a QA_RULES.json file in the data directory with validation rules.\n\nFormat:\n[\n  {\n    \"name\": \"Mark not empty\",\n    \"category\": \"Mechanical Equipment\",\n    \"parameter\": \"Mark\",\n    \"operator\": \"not_empty\",\n    \"severity\": \"Error\"\n  }\n]");
                return Result.Succeeded;
            }

            var results = new List<RuleResult>();
            var progress = StingProgressDialog.Show("Custom Rules", rules.Count);
            try
            {
                foreach (var rule in rules)
                {
                    if (progress.IsCancelled) break;
                    progress.Increment($"Evaluating: {rule.Name}");
                    results.Add(QualityAssuranceEngine.EvaluateRule(doc, rule));
                }
            }
            finally { progress.Close(); }

            // Build report
            var sb = new StringBuilder();
            sb.AppendLine($"Custom Rule Evaluation — {results.Count} rules\n");
            int passed = 0, failed = 0, skipped = 0;
            foreach (var r in results)
            {
                if (r.Skipped) { skipped++; sb.AppendLine($"[SKIP] {r.Rule.Name}"); continue; }
                if (r.FailedElements.Count == 0) { passed++; sb.AppendLine($"[PASS] {r.Rule.Name} — {r.Passed}/{r.TotalChecked}"); }
                else { failed++; sb.AppendLine($"[FAIL] {r.Rule.Name} — {r.FailedElements.Count} failed of {r.TotalChecked}"); }
            }
            sb.AppendLine($"\nSummary: {passed} passed, {failed} failed, {skipped} skipped");

            TaskDialog.Show("Custom Rules", sb.ToString());
            StingLog.Info($"CustomRules: {passed} passed, {failed} failed, {skipped} skipped");
            return Result.Succeeded;
        }
    }

    #endregion

    #region ── G5: Model Health Dashboard (Expanded) ──

    /// <summary>
    /// Calculate weighted model health score across 12 metrics with grade.
    /// </summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class ModelHealthScanCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }
            Document doc = ctx.Doc;

            var health = QualityAssuranceEngine.CalculateModelHealth(doc);

            var sb = new StringBuilder();
            sb.AppendLine($"Model Health Score: {health.OverallScore}/100  Grade: {health.Grade}");
            sb.AppendLine($"Scan completed in {health.Duration.TotalSeconds:F1}s\n");
            sb.AppendLine("Metric                 Score  Weight  Detail");
            sb.AppendLine("─────────────────────  ─────  ──────  ──────────────────────");
            foreach (var m in health.Metrics)
            {
                string bar = m.Score >= 80 ? "●●●" : m.Score >= 50 ? "●●○" : "●○○";
                sb.AppendLine($"{m.Name,-23} {m.Score,3}    {m.Weight,3}     {bar} {m.Detail}");
            }

            TaskDialog.Show("Model Health Dashboard", sb.ToString());
            StingLog.Info($"ModelHealth: Score={health.OverallScore} Grade={health.Grade}");
            return Result.Succeeded;
        }
    }

    /// <summary>Export model health to JSON.</summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class ModelHealthExportJsonCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }

            var health = QualityAssuranceEngine.CalculateModelHealth(ctx.Doc);
            string path = OutputLocationHelper.GetTimestampedPath(ctx.Doc, "ModelHealth", ".json");

            var obj = new
            {
                timestamp = DateTime.Now.ToString("o"),
                overallScore = health.OverallScore,
                grade = health.Grade,
                metrics = health.Metrics.Select(m => new { m.Name, m.Score, m.Weight, m.Detail })
            };
            File.WriteAllText(path, JsonConvert.SerializeObject(obj, Formatting.Indented));
            TaskDialog.Show("Model Health Export", $"Exported to:\n{path}");
            return Result.Succeeded;
        }
    }

    #endregion

    #region ── G9: Unified QA Report ──

    /// <summary>
    /// Generate comprehensive QA report combining health, warnings, setup, and custom rules.
    /// </summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class QAReportCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }
            Document doc = ctx.Doc;

            var progress = StingProgressDialog.Show("QA Report", 4);
            ModelHealthResult health;
            List<WarningGroup> warnings;
            List<SetupCheck> setupChecks;
            List<RuleResult> ruleResults;

            try
            {
                progress.Increment("Calculating model health...");
                health = QualityAssuranceEngine.CalculateModelHealth(doc);

                progress.Increment("Collecting warnings...");
                warnings = QualityAssuranceEngine.CollectWarnings(doc);

                progress.Increment("Validating setup...");
                setupChecks = QualityAssuranceEngine.ValidateProjectSetup(doc);

                progress.Increment("Running custom rules...");
                var rules = QualityAssuranceEngine.LoadCustomRules(doc);
                ruleResults = rules.Select(r => QualityAssuranceEngine.EvaluateRule(doc, r)).ToList();
            }
            finally { progress.Close(); }

            string report = QualityAssuranceEngine.GenerateQAReport(doc, health, warnings, setupChecks, ruleResults);

            // Show report
            TaskDialog td = new TaskDialog("QA Report");
            td.MainInstruction = $"Model Grade: {health.Grade} ({health.OverallScore}/100)";
            td.MainContent = report;
            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Export to CSV");
            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "Close");

            var result = td.Show();
            if (result == TaskDialogResult.CommandLink1)
            {
                string path = OutputLocationHelper.GetTimestampedPath(doc, "QAReport", ".csv");
                File.WriteAllText(path, report);
                TaskDialog.Show("QA Report", $"Exported to:\n{path}");
            }

            StingLog.Info($"QAReport: Grade={health.Grade} Score={health.OverallScore} Warnings={warnings.Sum(w => w.Count)}");
            return Result.Succeeded;
        }
    }

    #endregion

    #region ── G11: Setup Validation ──

    /// <summary>
    /// Validate project setup against BEP/template requirements.
    /// </summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class SetupValidationCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }

            var checks = QualityAssuranceEngine.ValidateProjectSetup(ctx.Doc);

            var sb = new StringBuilder();
            sb.AppendLine("Project Setup Validation\n");
            int passCount = checks.Count(c => c.Pass);
            sb.AppendLine($"Result: {passCount}/{checks.Count} checks passed\n");
            foreach (var c in checks)
                sb.AppendLine($"  [{(c.Pass ? "PASS" : "FAIL")}] {c.Name,-25} {c.Detail}");

            if (passCount == checks.Count)
                sb.AppendLine("\nAll checks passed. Project is properly configured.");
            else
                sb.AppendLine($"\n{checks.Count - passCount} items need attention before production work begins.");

            TaskDialog.Show("Setup Validation", sb.ToString());
            StingLog.Info($"SetupValidation: {passCount}/{checks.Count} passed");
            return Result.Succeeded;
        }
    }

    #endregion
}
