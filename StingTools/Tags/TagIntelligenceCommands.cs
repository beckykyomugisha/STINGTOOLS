using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json;
using StingTools.Core;

namespace StingTools.Tags
{
    // ═══════════════════════════════════════════════════════════════════
    //  TagIntelligenceCommands.cs — Advanced tagging intelligence layer
    //
    //  8 commands + 1 internal helper class providing:
    //    1. Configurable tag rule engine (JSON-driven conditions/actions)
    //    2. Deep tag quality analysis with scoring
    //    3. Batch command chain executor (workflow presets)
    //    4. Configurable tag format (separator, padding, segment order)
    //    5. Tag version control (snapshot/diff tracking)
    //    6. Tag propagation to similar elements
    //    7. Analytics dashboard (distribution, coverage, trends)
    //    8. Smart tag suggestion from contextual analysis
    // ═══════════════════════════════════════════════════════════════════

    #region Shared Helper

    /// <summary>
    /// Shared logic for tag intelligence commands: element collection, rule
    /// serialization, snapshot I/O, and quality scoring utilities.
    /// </summary>
    internal static class TagIntelligenceHelper
    {
        // ── Element collection ───────────────────────────────────────

        /// <summary>
        /// Collect all taggable elements from the document (categories in DiscMap).
        /// </summary>
        public static List<Element> CollectTaggable(Document doc)
        {
            var known = new HashSet<string>(TagConfig.DiscMap.Keys);
            return new FilteredElementCollector(doc)
                .WhereElementIsNotElementType()
                .Where(e => known.Contains(ParameterHelpers.GetCategoryName(e)))
                .ToList();
        }

        /// <summary>
        /// Collect taggable elements from the active selection, falling back to
        /// all taggable elements in the document when nothing is selected.
        /// </summary>
        public static List<Element> CollectTargets(UIDocument uidoc, out string scopeLabel)
        {
            Document doc = uidoc.Document;
            var selIds = uidoc.Selection.GetElementIds();
            if (selIds.Count > 0)
            {
                scopeLabel = $"{selIds.Count} selected elements";
                var known = new HashSet<string>(TagConfig.DiscMap.Keys);
                return selIds
                    .Select(id => doc.GetElement(id))
                    .Where(e => e != null && known.Contains(ParameterHelpers.GetCategoryName(e)))
                    .ToList();
            }

            scopeLabel = "all taggable elements";
            return CollectTaggable(doc);
        }

        // ── Rule model ───────────────────────────────────────────────

        /// <summary>A single tag rule: condition → action.</summary>
        public class TagRule
        {
            public string Name { get; set; } = "";
            public string ConditionType { get; set; } = "Category";   // Category | Parameter | FamilyName
            public string ConditionValue { get; set; } = "";
            public string ConditionParam { get; set; } = "";          // for Parameter condition type
            public string SetDisc { get; set; }
            public string SetSys { get; set; }
            public string SetFunc { get; set; }
            public string SetProd { get; set; }
        }

        /// <summary>Wrapper for rule list serialization.</summary>
        public class TagRuleSet
        {
            public List<TagRule> Rules { get; set; } = new List<TagRule>();
        }

        // ── Rule I/O ─────────────────────────────────────────────────

        public static string GetRulesPath(Document doc)
        {
            string dir = Path.GetDirectoryName(doc.PathName);
            if (string.IsNullOrEmpty(dir)) dir = StingToolsApp.DataPath ?? Path.GetTempPath();
            return Path.Combine(dir, "project_config.json");
        }

        public static TagRuleSet LoadRules(string configPath)
        {
            if (!File.Exists(configPath)) return new TagRuleSet();
            try
            {
                string json = File.ReadAllText(configPath);
                var root = JsonConvert.DeserializeObject<Dictionary<string, object>>(json);
                if (root != null && root.TryGetValue("TAG_RULES", out object rulesObj))
                {
                    string rulesJson = JsonConvert.SerializeObject(rulesObj);
                    return JsonConvert.DeserializeObject<TagRuleSet>(rulesJson) ?? new TagRuleSet();
                }
            }
            catch (Exception ex)
            {
                StingLog.Warn($"LoadRules: {ex.Message}");
            }
            return new TagRuleSet();
        }

        public static void SaveRules(string configPath, TagRuleSet ruleSet)
        {
            Dictionary<string, object> root;
            if (File.Exists(configPath))
            {
                try
                {
                    string existing = File.ReadAllText(configPath);
                    root = JsonConvert.DeserializeObject<Dictionary<string, object>>(existing)
                        ?? new Dictionary<string, object>();
                }
                catch
                {
                    root = new Dictionary<string, object>();
                }
            }
            else
            {
                root = new Dictionary<string, object>();
            }

            root["TAG_RULES"] = ruleSet;
            File.WriteAllText(configPath, JsonConvert.SerializeObject(root, Formatting.Indented));
        }

        /// <summary>
        /// Evaluate whether a rule matches an element.
        /// </summary>
        public static bool RuleMatches(TagRule rule, Element el)
        {
            switch (rule.ConditionType)
            {
                case "Category":
                    return string.Equals(ParameterHelpers.GetCategoryName(el),
                        rule.ConditionValue, StringComparison.OrdinalIgnoreCase);

                case "FamilyName":
                    return ParameterHelpers.GetFamilyName(el)
                        .IndexOf(rule.ConditionValue, StringComparison.OrdinalIgnoreCase) >= 0;

                case "Parameter":
                    string val = ParameterHelpers.GetString(el, rule.ConditionParam);
                    return string.Equals(val, rule.ConditionValue, StringComparison.OrdinalIgnoreCase);

                default:
                    return false;
            }
        }

        // ── Snapshot I/O ─────────────────────────────────────────────

        /// <summary>Tag snapshot entry for version control.</summary>
        public class TagSnapshot
        {
            public DateTime Timestamp { get; set; }
            public Dictionary<string, string> Tags { get; set; } = new Dictionary<string, string>();
        }

        public static string GetSnapshotPath(Document doc)
        {
            string dir = Path.GetDirectoryName(doc.PathName);
            if (string.IsNullOrEmpty(dir)) dir = Path.GetTempPath();
            return Path.Combine(dir, "STING_Tag_Snapshots.json");
        }

        public static List<TagSnapshot> LoadSnapshots(string path)
        {
            if (!File.Exists(path)) return new List<TagSnapshot>();
            try
            {
                string json = File.ReadAllText(path);
                return JsonConvert.DeserializeObject<List<TagSnapshot>>(json) ?? new List<TagSnapshot>();
            }
            catch (Exception ex)
            {
                StingLog.Warn($"LoadSnapshots: {ex.Message}");
                return new List<TagSnapshot>();
            }
        }

        public static void SaveSnapshots(string path, List<TagSnapshot> snapshots)
        {
            File.WriteAllText(path, JsonConvert.SerializeObject(snapshots, Formatting.Indented));
        }

        /// <summary>Build a snapshot of current tag values keyed by ElementId.</summary>
        public static TagSnapshot BuildSnapshot(Document doc)
        {
            var snapshot = new TagSnapshot { Timestamp = DateTime.Now, Tags = new Dictionary<string, string>() };
            foreach (Element el in CollectTaggable(doc))
            {
                string tag = ParameterHelpers.GetString(el, ParamRegistry.TAG1);
                if (!string.IsNullOrEmpty(tag))
                    snapshot.Tags[el.Id.ToString()] = tag;
            }
            return snapshot;
        }

        // ── Quality scoring ──────────────────────────────────────────

        /// <summary>
        /// Compute a quality score (0-100) for a single element's tag state.
        /// Checks: completeness, ISO validity, cross-validation, full resolution.
        /// </summary>
        public static double ScoreElement(Element el)
        {
            double score = 0;
            string tag = ParameterHelpers.GetString(el, ParamRegistry.TAG1);

            // 30 points: tag exists and has 8 segments
            if (TagConfig.TagIsComplete(tag)) score += 30;
            else if (!string.IsNullOrEmpty(tag)) score += 10;

            // 20 points: fully resolved (no placeholders)
            if (!string.IsNullOrEmpty(tag) && TagConfig.TagIsFullyResolved(tag)) score += 20;

            // 10 points per filled token pair (DISC+SYS, LOC+ZONE, FUNC+PROD, LVL+SEQ)
            string disc = ParameterHelpers.GetString(el, ParamRegistry.DISC);
            string sys = ParameterHelpers.GetString(el, ParamRegistry.SYS);
            string loc = ParameterHelpers.GetString(el, ParamRegistry.LOC);
            string zone = ParameterHelpers.GetString(el, ParamRegistry.ZONE);
            string func = ParameterHelpers.GetString(el, ParamRegistry.FUNC);
            string prod = ParameterHelpers.GetString(el, ParamRegistry.PROD);
            string lvl = ParameterHelpers.GetString(el, ParamRegistry.LVL);
            string seq = ParameterHelpers.GetString(el, ParamRegistry.SEQ);

            if (!string.IsNullOrEmpty(disc) && !string.IsNullOrEmpty(sys)) score += 10;
            if (!string.IsNullOrEmpty(loc) && !string.IsNullOrEmpty(zone)) score += 10;
            if (!string.IsNullOrEmpty(func) && !string.IsNullOrEmpty(prod)) score += 10;
            if (!string.IsNullOrEmpty(lvl) && !string.IsNullOrEmpty(seq)) score += 10;

            // 10 points: DISC matches expected category mapping
            string cat = ParameterHelpers.GetCategoryName(el);
            if (!string.IsNullOrEmpty(disc) && TagConfig.DiscMap.TryGetValue(cat, out string expectedDisc)
                && string.Equals(disc, expectedDisc, StringComparison.OrdinalIgnoreCase))
                score += 10;

            return Math.Min(score, 100);
        }

        // ── Format config I/O ────────────────────────────────────────

        /// <summary>Tag format configuration stored in project_config.json.</summary>
        public class TagFormatConfig
        {
            public int NumPad { get; set; } = 4;
            public string Separator { get; set; } = "-";
            public string[] SegmentOrder { get; set; } = { "DISC", "LOC", "ZONE", "LVL", "SYS", "FUNC", "PROD", "SEQ" };
        }

        public static TagFormatConfig LoadFormatConfig(string configPath)
        {
            if (!File.Exists(configPath)) return new TagFormatConfig();
            try
            {
                string json = File.ReadAllText(configPath);
                var root = JsonConvert.DeserializeObject<Dictionary<string, object>>(json);
                if (root != null && root.TryGetValue("TAG_FORMAT", out object fmtObj))
                {
                    string fmtJson = JsonConvert.SerializeObject(fmtObj);
                    return JsonConvert.DeserializeObject<TagFormatConfig>(fmtJson) ?? new TagFormatConfig();
                }
            }
            catch (Exception ex)
            {
                StingLog.Warn($"LoadFormatConfig: {ex.Message}");
            }
            return new TagFormatConfig();
        }

        public static void SaveFormatConfig(string configPath, TagFormatConfig fmt)
        {
            Dictionary<string, object> root;
            if (File.Exists(configPath))
            {
                try
                {
                    string existing = File.ReadAllText(configPath);
                    root = JsonConvert.DeserializeObject<Dictionary<string, object>>(existing)
                        ?? new Dictionary<string, object>();
                }
                catch
                {
                    root = new Dictionary<string, object>();
                }
            }
            else
            {
                root = new Dictionary<string, object>();
            }

            root["TAG_FORMAT"] = fmt;
            File.WriteAllText(configPath, JsonConvert.SerializeObject(root, Formatting.Indented));
        }

        /// <summary>Build a sample tag string from the format config.</summary>
        public static string BuildSampleTag(TagFormatConfig fmt)
        {
            var sampleValues = new Dictionary<string, string>
            {
                { "DISC", "M" }, { "LOC", "BLD1" }, { "ZONE", "Z01" }, { "LVL", "L02" },
                { "SYS", "HVAC" }, { "FUNC", "SUP" }, { "PROD", "AHU" },
                { "SEQ", new string('0', fmt.NumPad - 1) + "3" }
            };

            var parts = new List<string>();
            foreach (string seg in fmt.SegmentOrder)
            {
                if (sampleValues.TryGetValue(seg, out string val))
                    parts.Add(val);
                else
                    parts.Add(seg);
            }
            return string.Join(fmt.Separator, parts);
        }
    }

    #endregion

    // ═══════════════════════════════════════════════════════════════════
    //  1. TagRuleEngineCommand — Configurable tag rules engine
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Configurable tag rule engine. Loads rules from project_config.json defining
    /// conditions (category, parameter value, family name) and actions (set DISC, SYS,
    /// FUNC, PROD tokens). Applies matching rules to selected or all taggable elements.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class TagRuleEngineCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            Document doc = uidoc.Document;

            string configPath = TagIntelligenceHelper.GetRulesPath(doc);
            var ruleSet = TagIntelligenceHelper.LoadRules(configPath);

            // Present mode dialog
            TaskDialog modeDlg = new TaskDialog("Tag Rule Engine");
            modeDlg.MainInstruction = "Tag Rule Engine";
            modeDlg.MainContent =
                $"Rules loaded: {ruleSet.Rules.Count}\n" +
                $"Config: {configPath}\n\n" +
                "Rules define conditions (category, family name, parameter value)\n" +
                "and actions (set DISC, SYS, FUNC, PROD tokens).";
            modeDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink1,
                "Apply Rules",
                "Run all rules against selected or all taggable elements");
            modeDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink2,
                "View Rules",
                "Display current rule definitions");
            modeDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink3,
                "Add Sample Rules",
                "Create a starter set of rules in project_config.json");
            modeDlg.CommonButtons = TaskDialogCommonButtons.Cancel;

            var choice = modeDlg.Show();

            if (choice == TaskDialogResult.CommandLink2)
            {
                ShowRules(ruleSet);
                return Result.Succeeded;
            }

            if (choice == TaskDialogResult.CommandLink3)
            {
                CreateSampleRules(configPath);
                return Result.Succeeded;
            }

            if (choice != TaskDialogResult.CommandLink1)
                return Result.Cancelled;

            if (ruleSet.Rules.Count == 0)
            {
                TaskDialog.Show("Tag Rule Engine", "No rules defined. Use 'Add Sample Rules' to get started.");
                return Result.Succeeded;
            }

            // Apply rules
            string scopeLabel;
            var targets = TagIntelligenceHelper.CollectTargets(uidoc, out scopeLabel);
            if (targets.Count == 0)
            {
                TaskDialog.Show("Tag Rule Engine", "No taggable elements found.");
                return Result.Succeeded;
            }

            StingLog.Info($"TagRuleEngine: applying {ruleSet.Rules.Count} rules to {targets.Count} elements ({scopeLabel})");

            int totalApplied = 0;
            var ruleHits = new Dictionary<string, int>();

            using (Transaction t = new Transaction(doc, "STING Tag Rule Engine"))
            {
                t.Start();

                foreach (Element el in targets)
                {
                    foreach (var rule in ruleSet.Rules)
                    {
                        if (!TagIntelligenceHelper.RuleMatches(rule, el)) continue;

                        bool applied = false;
                        if (!string.IsNullOrEmpty(rule.SetDisc))
                            applied |= ParameterHelpers.SetString(el, ParamRegistry.DISC, rule.SetDisc, true);
                        if (!string.IsNullOrEmpty(rule.SetSys))
                            applied |= ParameterHelpers.SetString(el, ParamRegistry.SYS, rule.SetSys, true);
                        if (!string.IsNullOrEmpty(rule.SetFunc))
                            applied |= ParameterHelpers.SetString(el, ParamRegistry.FUNC, rule.SetFunc, true);
                        if (!string.IsNullOrEmpty(rule.SetProd))
                            applied |= ParameterHelpers.SetString(el, ParamRegistry.PROD, rule.SetProd, true);

                        if (applied)
                        {
                            totalApplied++;
                            string key = rule.Name ?? "(unnamed)";
                            ruleHits[key] = ruleHits.GetValueOrDefault(key) + 1;
                        }
                        break; // first matching rule wins
                    }
                }

                t.Commit();
            }

            var report = new StringBuilder();
            report.AppendLine($"Applied rules to {totalApplied} of {targets.Count} elements.");
            if (ruleHits.Count > 0)
            {
                report.AppendLine();
                foreach (var kvp in ruleHits.OrderByDescending(x => x.Value))
                    report.AppendLine($"  {kvp.Key}: {kvp.Value} hits");
            }

            TaskDialog.Show("Tag Rule Engine", report.ToString());
            StingLog.Info($"TagRuleEngine: {totalApplied} elements updated across {ruleHits.Count} rules");
            return Result.Succeeded;
        }

        private void ShowRules(TagIntelligenceHelper.TagRuleSet ruleSet)
        {
            if (ruleSet.Rules.Count == 0)
            {
                TaskDialog.Show("Tag Rules", "No rules defined.");
                return;
            }

            var sb = new StringBuilder();
            int idx = 0;
            foreach (var r in ruleSet.Rules)
            {
                idx++;
                sb.AppendLine($"Rule {idx}: {r.Name}");
                sb.AppendLine($"  IF {r.ConditionType} = \"{r.ConditionValue}\"" +
                    (r.ConditionType == "Parameter" ? $" (param: {r.ConditionParam})" : ""));
                var actions = new List<string>();
                if (!string.IsNullOrEmpty(r.SetDisc)) actions.Add($"DISC={r.SetDisc}");
                if (!string.IsNullOrEmpty(r.SetSys)) actions.Add($"SYS={r.SetSys}");
                if (!string.IsNullOrEmpty(r.SetFunc)) actions.Add($"FUNC={r.SetFunc}");
                if (!string.IsNullOrEmpty(r.SetProd)) actions.Add($"PROD={r.SetProd}");
                sb.AppendLine($"  THEN {string.Join(", ", actions)}");
                sb.AppendLine();
            }

            TaskDialog.Show("Tag Rules", sb.ToString());
        }

        private void CreateSampleRules(string configPath)
        {
            var ruleSet = new TagIntelligenceHelper.TagRuleSet
            {
                Rules = new List<TagIntelligenceHelper.TagRule>
                {
                    new TagIntelligenceHelper.TagRule
                    {
                        Name = "Mechanical Equipment → M/HVAC",
                        ConditionType = "Category",
                        ConditionValue = "Mechanical Equipment",
                        SetDisc = "M", SetSys = "HVAC", SetFunc = "SUP"
                    },
                    new TagIntelligenceHelper.TagRule
                    {
                        Name = "AHU families → AHU product",
                        ConditionType = "FamilyName",
                        ConditionValue = "AHU",
                        SetProd = "AHU"
                    },
                    new TagIntelligenceHelper.TagRule
                    {
                        Name = "Distribution Boards → E/LV/PWR/DB",
                        ConditionType = "FamilyName",
                        ConditionValue = "Distribution Board",
                        SetDisc = "E", SetSys = "LV", SetFunc = "PWR", SetProd = "DB"
                    },
                    new TagIntelligenceHelper.TagRule
                    {
                        Name = "Lighting Fixtures → E/LTG",
                        ConditionType = "Category",
                        ConditionValue = "Lighting Fixtures",
                        SetDisc = "E", SetSys = "LTG", SetFunc = "GEN"
                    },
                    new TagIntelligenceHelper.TagRule
                    {
                        Name = "Sprinklers → FP/FLS",
                        ConditionType = "Category",
                        ConditionValue = "Sprinklers",
                        SetDisc = "FP", SetSys = "FLS", SetFunc = "SPR", SetProd = "SPR"
                    },
                }
            };

            try
            {
                TagIntelligenceHelper.SaveRules(configPath, ruleSet);
                TaskDialog.Show("Tag Rule Engine",
                    $"Created {ruleSet.Rules.Count} sample rules in:\n{configPath}\n\n" +
                    "Edit the JSON file to customise rules, then use 'Apply Rules' to execute.");
                StingLog.Info($"TagRuleEngine: sample rules saved to {configPath}");
            }
            catch (Exception ex)
            {
                TaskDialog.Show("Tag Rule Engine", $"Failed to save rules:\n{ex.Message}");
                StingLog.Error("TagRuleEngine: failed to save sample rules", ex);
            }
        }
    }

    // ═══════════════════════════════════════════════════════════════════
    //  2. TagQualityAnalyzerCommand — Deep tag quality analysis
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Deep tag quality analysis. Checks all elements for: completeness (8 segments),
    /// ISO 19650 token validation, cross-validation (DISC vs category), duplicate tags,
    /// orphaned tags (tag exists but element missing params), and sequence gaps.
    /// Reports an overall quality score as a percentage.
    /// </summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class TagQualityAnalyzerCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            Document doc = commandData.Application.ActiveUIDocument.Document;
            var tagElements = TagIntelligenceHelper.CollectTaggable(doc);

            if (tagElements.Count == 0)
            {
                TaskDialog.Show("Tag Quality", "No taggable elements found.");
                return Result.Succeeded;
            }

            StingLog.Info($"TagQualityAnalyzer: scanning {tagElements.Count} elements");
            var sw = Stopwatch.StartNew();

            int total = tagElements.Count;
            int complete = 0, incomplete = 0, missing = 0, fullyResolved = 0;
            int isoViolations = 0, crossValErrors = 0, orphanedTags = 0;
            double totalScore = 0;
            var tagCounts = new Dictionary<string, int>(StringComparer.Ordinal);
            var seqByGroup = new Dictionary<string, List<int>>();
            var issuesByCategory = new Dictionary<string, int>();
            var scoreDistribution = new int[11]; // 0-10, 11-20, ..., 91-100

            foreach (Element el in tagElements)
            {
                string tag = ParameterHelpers.GetString(el, ParamRegistry.TAG1);
                string cat = ParameterHelpers.GetCategoryName(el);
                double score = TagIntelligenceHelper.ScoreElement(el);
                totalScore += score;
                scoreDistribution[Math.Min((int)(score / 10), 10)]++;

                // Completeness checks
                if (string.IsNullOrEmpty(tag))
                {
                    missing++;
                    issuesByCategory[cat] = issuesByCategory.GetValueOrDefault(cat) + 1;
                }
                else if (TagConfig.TagIsComplete(tag))
                {
                    complete++;
                    if (TagConfig.TagIsFullyResolved(tag)) fullyResolved++;
                    tagCounts[tag] = tagCounts.GetValueOrDefault(tag) + 1;
                }
                else
                {
                    incomplete++;
                    issuesByCategory[cat] = issuesByCategory.GetValueOrDefault(cat) + 1;
                }

                // ISO 19650 token validation
                string disc = ParameterHelpers.GetString(el, ParamRegistry.DISC);
                if (!string.IsNullOrEmpty(disc))
                {
                    string validation = ISO19650Validator.ValidateToken("DISC", disc);
                    if (validation != null) isoViolations++;

                    // Cross-validation: DISC vs category
                    if (TagConfig.DiscMap.TryGetValue(cat, out string expectedDisc)
                        && !string.Equals(disc, expectedDisc, StringComparison.OrdinalIgnoreCase))
                        crossValErrors++;
                }

                // Orphaned tags: tag container populated but individual tokens empty
                if (!string.IsNullOrEmpty(tag))
                {
                    string[] tokens = ParamRegistry.ReadTokenValues(el);
                    int emptyTokens = tokens.Count(t => string.IsNullOrEmpty(t));
                    if (emptyTokens >= 4) orphanedTags++;
                }

                // Sequence tracking for gap detection
                string seq = ParameterHelpers.GetString(el, ParamRegistry.SEQ);
                if (!string.IsNullOrEmpty(disc) && !string.IsNullOrEmpty(seq)
                    && int.TryParse(seq, out int seqNum))
                {
                    string sys = ParameterHelpers.GetString(el, ParamRegistry.SYS);
                    string lvl = ParameterHelpers.GetString(el, ParamRegistry.LVL);
                    string groupKey = $"{disc}|{sys}|{lvl}";
                    if (!seqByGroup.ContainsKey(groupKey))
                        seqByGroup[groupKey] = new List<int>();
                    seqByGroup[groupKey].Add(seqNum);
                }
            }

            // Detect duplicate tags
            int duplicateTags = tagCounts.Count(kvp => kvp.Value > 1);
            int duplicateElements = tagCounts.Where(kvp => kvp.Value > 1).Sum(kvp => kvp.Value);

            // Detect sequence gaps
            int totalGaps = 0;
            foreach (var kvp in seqByGroup)
            {
                var sorted = kvp.Value.OrderBy(x => x).ToList();
                if (sorted.Count < 2) continue;
                for (int i = 1; i < sorted.Count; i++)
                {
                    int gap = sorted[i] - sorted[i - 1] - 1;
                    if (gap > 0) totalGaps += gap;
                }
            }

            sw.Stop();
            double overallScore = total > 0 ? totalScore / total : 0;

            // Build report
            var report = new StringBuilder();
            report.AppendLine($"Tag Quality Score: {overallScore:F1}%");
            report.AppendLine(new string('═', 40));
            report.AppendLine();

            report.AppendLine($"Elements scanned:     {total:N0}");
            report.AppendLine($"Complete tags:        {complete:N0} ({(total > 0 ? 100.0 * complete / total : 0):F1}%)");
            report.AppendLine($"Fully resolved:       {fullyResolved:N0}");
            report.AppendLine($"Incomplete tags:      {incomplete:N0}");
            report.AppendLine($"Missing tags:         {missing:N0}");
            report.AppendLine($"Duplicate tags:       {duplicateTags:N0} ({duplicateElements:N0} elements)");
            report.AppendLine($"Orphaned tags:        {orphanedTags:N0}");
            report.AppendLine($"ISO 19650 violations: {isoViolations:N0}");
            report.AppendLine($"Cross-val errors:     {crossValErrors:N0}");
            report.AppendLine($"Sequence gaps:        {totalGaps:N0}");
            report.AppendLine();

            // Score distribution histogram
            report.AppendLine("Score Distribution:");
            for (int i = 0; i <= 10; i++)
            {
                int low = i * 10;
                int high = i == 10 ? 100 : low + 9;
                int count = scoreDistribution[i];
                string bar = new string('█', Math.Min(count * 30 / Math.Max(total, 1), 30));
                report.AppendLine($"  {low,3}-{high,3}%: {count,5} {bar}");
            }

            // Top issue categories
            if (issuesByCategory.Count > 0)
            {
                report.AppendLine();
                report.AppendLine("Issues by Category (top 10):");
                foreach (var kvp in issuesByCategory.OrderByDescending(x => x.Value).Take(10))
                    report.AppendLine($"  {kvp.Key,-30} {kvp.Value,5} issues");
            }

            report.AppendLine();
            report.AppendLine($"Analysis completed in {sw.Elapsed.TotalSeconds:F1}s");

            TaskDialog td = new TaskDialog("Tag Quality Analysis");
            td.MainInstruction = $"Quality Score: {overallScore:F1}% — {total:N0} elements";
            td.MainContent = report.ToString();
            td.Show();

            StingLog.Info($"TagQualityAnalyzer: score={overallScore:F1}%, {total} elements, " +
                $"{complete} complete, {missing} missing, {duplicateTags} duplicates, " +
                $"elapsed={sw.Elapsed.TotalSeconds:F1}s");

            return Result.Succeeded;
        }
    }

    // ═══════════════════════════════════════════════════════════════════
    //  3. TagBatchChainCommand — Batch command chain executor
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Batch command chain executor (workflow preset). Runs a configurable sequence:
    /// FamilyStagePopulate, AutoTag, Validate, Combine, ExportCSV. Shows progress
    /// after each step and uses TransactionGroup for atomic rollback on failure.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class TagBatchChainCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            Document doc = commandData.Application.ActiveUIDocument.Document;
            int elementCount = TagIntelligenceHelper.CollectTaggable(doc).Count;

            // Confirm with user
            TaskDialog confirm = new TaskDialog("Tag Batch Chain");
            confirm.MainInstruction = "Run full tagging pipeline?";
            confirm.MainContent =
                $"Taggable elements: {elementCount:N0}\n\n" +
                "Pipeline steps:\n" +
                "  1. Family-Stage Populate (all 9 tokens)\n" +
                "  2. Auto Tag (assign SEQ + build tags)\n" +
                "  3. Validate Tags (ISO 19650 compliance)\n" +
                "  4. Combine Parameters (all 36 containers)\n" +
                "  5. Export CSV (tag audit report)\n\n" +
                "All steps run atomically — rollback on critical failure.";
            confirm.CommonButtons = TaskDialogCommonButtons.Ok | TaskDialogCommonButtons.Cancel;
            if (confirm.Show() == TaskDialogResult.Cancel)
                return Result.Cancelled;

            StingLog.Info($"TagBatchChain: starting pipeline for {elementCount} elements");
            var report = new StringBuilder();
            report.AppendLine("Tag Batch Chain Results");
            report.AppendLine(new string('═', 40));

            int stepNum = 0;
            int passed = 0;
            var totalSw = Stopwatch.StartNew();

            using (TransactionGroup tg = new TransactionGroup(doc, "STING Tag Batch Chain"))
            {
                tg.Start();

                // Step 1: Family-Stage Populate
                passed += RunStep(ref stepNum, report, "Family-Stage Populate",
                    () => RunCommand(new FamilyStagePopulateCommand(), commandData, elements));

                // Step 2: Auto Tag
                passed += RunStep(ref stepNum, report, "Auto Tag",
                    () => RunCommand(new AutoTagCommand(), commandData, elements));

                // Step 3: Validate Tags (ReadOnly — no transaction needed)
                passed += RunStep(ref stepNum, report, "Validate Tags",
                    () => RunCommand(new ValidateTagsCommand(), commandData, elements));

                // Step 4: Combine Parameters
                passed += RunStep(ref stepNum, report, "Combine Parameters",
                    () => RunCommand(new CombineParametersCommand(), commandData, elements));

                // Step 5: Export CSV
                passed += RunStep(ref stepNum, report, "Export CSV",
                    () => RunCommand(new Temp.ExportCSVCommand(), commandData, elements));

                int failed = stepNum - passed;
                totalSw.Stop();

                if (failed > 0)
                {
                    report.AppendLine(new string('─', 40));
                    report.AppendLine($"  {passed}/{stepNum} succeeded, {failed} failed");

                    TaskDialog rollbackDlg = new TaskDialog("Tag Batch Chain — Failures");
                    rollbackDlg.MainInstruction = $"{failed} step(s) failed";
                    rollbackDlg.MainContent = report.ToString() +
                        "\n\nKeep completed steps or rollback all?";
                    rollbackDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink1,
                        "Keep results", $"Commit {passed} successful steps");
                    rollbackDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink2,
                        "Rollback all", "Undo all changes");

                    if (rollbackDlg.Show() == TaskDialogResult.CommandLink2)
                    {
                        tg.RollBack();
                        TaskDialog.Show("Tag Batch Chain", "All changes rolled back.");
                        return Result.Cancelled;
                    }
                }

                tg.Assimilate();
            }

            report.AppendLine(new string('─', 40));
            report.AppendLine($"  Complete: {passed}/{stepNum} steps");
            report.AppendLine($"  Duration: {totalSw.Elapsed.TotalSeconds:F1}s");

            TaskDialog td = new TaskDialog("Tag Batch Chain");
            td.MainInstruction = $"Pipeline: {passed}/{stepNum} steps complete ({totalSw.Elapsed.TotalSeconds:F1}s)";
            td.MainContent = report.ToString();
            td.Show();

            StingLog.Info($"TagBatchChain: {passed}/{stepNum} passed, elapsed={totalSw.Elapsed.TotalSeconds:F1}s");
            return passed > 0 ? Result.Succeeded : Result.Failed;
        }

        private static Result RunCommand(IExternalCommand cmd,
            ExternalCommandData data, ElementSet elems)
        {
            string msg = "";
            return cmd.Execute(data, ref msg, elems);
        }

        private static int RunStep(ref int stepNum, StringBuilder report,
            string label, Func<Result> action)
        {
            stepNum++;
            if (EscapeChecker.IsEscapePressed())
            {
                report.AppendLine($"  {stepNum,2}. {label} — CANCELLED (Escape pressed)");
                StingLog.Info($"TagBatchChain step {stepNum}: {label} — skipped (user cancelled)");
                return 0;
            }

            var sw = Stopwatch.StartNew();
            try
            {
                Result result = action();
                sw.Stop();
                string status = result == Result.Succeeded ? "OK" : "WARN";
                report.AppendLine($"  {stepNum,2}. {label} — {status} ({sw.Elapsed.TotalSeconds:F1}s)");
                StingLog.Info($"TagBatchChain step {stepNum}: {label} — {status} ({sw.Elapsed.TotalSeconds:F1}s)");
                return result == Result.Succeeded ? 1 : 0;
            }
            catch (Exception ex)
            {
                sw.Stop();
                report.AppendLine($"  {stepNum,2}. {label} — FAILED: {ex.Message}");
                StingLog.Error($"TagBatchChain step {stepNum}: {label}", ex);
                return 0;
            }
        }
    }

    // ═══════════════════════════════════════════════════════════════════
    //  4. ConfigurableTagFormatCommand — Configure tag format
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Configure tag format: separator, padding, and segment order. Reads and writes
    /// to project_config.json. Allows changing NumPad (3-6), Separator ("-", "_", "."),
    /// and SegmentOrder array. Shows current format and a preview of a sample tag.
    /// </summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class ConfigurableTagFormatCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            Document doc = commandData.Application.ActiveUIDocument.Document;
            string configPath = TagIntelligenceHelper.GetRulesPath(doc);
            var fmt = TagIntelligenceHelper.LoadFormatConfig(configPath);
            string sampleTag = TagIntelligenceHelper.BuildSampleTag(fmt);

            TaskDialog td = new TaskDialog("Tag Format Configuration");
            td.MainInstruction = "Tag Format Configuration";
            td.MainContent =
                $"Current format settings:\n\n" +
                $"  Separator:     \"{fmt.Separator}\"\n" +
                $"  SEQ Padding:   {fmt.NumPad} digits\n" +
                $"  Segment Order: {string.Join(" | ", fmt.SegmentOrder)}\n\n" +
                $"Sample tag: {sampleTag}\n\n" +
                "Use command links to modify settings.";

            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink1,
                "Change Separator",
                "Switch between - (dash), _ (underscore), . (dot)");
            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink2,
                "Change SEQ Padding",
                $"Current: {fmt.NumPad} digits — cycle through 3, 4, 5, 6");
            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink3,
                "Save Current Settings",
                $"Write to {Path.GetFileName(configPath)}");
            td.CommonButtons = TaskDialogCommonButtons.Cancel;

            var choice = td.Show();

            switch (choice)
            {
                case TaskDialogResult.CommandLink1:
                    // Cycle separator
                    string[] separators = { "-", "_", "." };
                    int sepIdx = Array.IndexOf(separators, fmt.Separator);
                    fmt.Separator = separators[(sepIdx + 1) % separators.Length];
                    SaveAndShow(configPath, fmt);
                    break;

                case TaskDialogResult.CommandLink2:
                    // Cycle padding
                    int[] pads = { 3, 4, 5, 6 };
                    int padIdx = Array.IndexOf(pads, fmt.NumPad);
                    fmt.NumPad = pads[(padIdx + 1) % pads.Length];
                    SaveAndShow(configPath, fmt);
                    break;

                case TaskDialogResult.CommandLink3:
                    SaveAndShow(configPath, fmt);
                    break;

                default:
                    return Result.Cancelled;
            }

            return Result.Succeeded;
        }

        private void SaveAndShow(string configPath, TagIntelligenceHelper.TagFormatConfig fmt)
        {
            try
            {
                TagIntelligenceHelper.SaveFormatConfig(configPath, fmt);
                string preview = TagIntelligenceHelper.BuildSampleTag(fmt);
                TaskDialog.Show("Tag Format",
                    $"Settings saved to:\n{configPath}\n\n" +
                    $"Separator: \"{fmt.Separator}\"\n" +
                    $"Padding:   {fmt.NumPad} digits\n" +
                    $"Order:     {string.Join(" | ", fmt.SegmentOrder)}\n\n" +
                    $"Sample: {preview}");
                StingLog.Info($"TagFormat: saved separator=\"{fmt.Separator}\", numpad={fmt.NumPad}");
            }
            catch (Exception ex)
            {
                TaskDialog.Show("Tag Format", $"Failed to save:\n{ex.Message}");
                StingLog.Error("TagFormat: save failed", ex);
            }
        }
    }

    // ═══════════════════════════════════════════════════════════════════
    //  5. TagVersionControlCommand — Track tag changes over time
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Track tag changes over time. Records tag snapshots to a JSON log file
    /// alongside the project. Shows diff between current tags and last snapshot,
    /// highlighting additions, removals, and changes.
    /// </summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class TagVersionControlCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            Document doc = commandData.Application.ActiveUIDocument.Document;
            string snapshotPath = TagIntelligenceHelper.GetSnapshotPath(doc);
            var snapshots = TagIntelligenceHelper.LoadSnapshots(snapshotPath);

            TaskDialog modeDlg = new TaskDialog("Tag Version Control");
            modeDlg.MainInstruction = "Tag Version Control";
            modeDlg.MainContent =
                $"Snapshot file: {snapshotPath}\n" +
                $"Existing snapshots: {snapshots.Count}\n" +
                (snapshots.Count > 0
                    ? $"Latest: {snapshots.Last().Timestamp:yyyy-MM-dd HH:mm} ({snapshots.Last().Tags.Count} tags)"
                    : "No snapshots recorded yet.");
            modeDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink1,
                "Take Snapshot",
                "Record current tag state for future comparison");
            modeDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink2,
                "Compare with Latest",
                "Show diff between current state and last snapshot");
            modeDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink3,
                "View History",
                "Show all snapshot timestamps and tag counts");
            modeDlg.CommonButtons = TaskDialogCommonButtons.Cancel;

            var choice = modeDlg.Show();

            switch (choice)
            {
                case TaskDialogResult.CommandLink1:
                    TakeSnapshot(doc, snapshotPath, snapshots);
                    break;

                case TaskDialogResult.CommandLink2:
                    CompareWithLatest(doc, snapshots);
                    break;

                case TaskDialogResult.CommandLink3:
                    ShowHistory(snapshots);
                    break;

                default:
                    return Result.Cancelled;
            }

            return Result.Succeeded;
        }

        private void TakeSnapshot(Document doc, string path,
            List<TagIntelligenceHelper.TagSnapshot> snapshots)
        {
            var snapshot = TagIntelligenceHelper.BuildSnapshot(doc);
            snapshots.Add(snapshot);

            try
            {
                TagIntelligenceHelper.SaveSnapshots(path, snapshots);
                TaskDialog.Show("Tag Version Control",
                    $"Snapshot #{snapshots.Count} saved.\n" +
                    $"Timestamp: {snapshot.Timestamp:yyyy-MM-dd HH:mm:ss}\n" +
                    $"Tags recorded: {snapshot.Tags.Count:N0}\n" +
                    $"File: {path}");
                StingLog.Info($"TagVersionControl: snapshot #{snapshots.Count} saved, {snapshot.Tags.Count} tags");
            }
            catch (Exception ex)
            {
                TaskDialog.Show("Tag Version Control", $"Failed to save snapshot:\n{ex.Message}");
                StingLog.Error("TagVersionControl: snapshot save failed", ex);
            }
        }

        private void CompareWithLatest(Document doc,
            List<TagIntelligenceHelper.TagSnapshot> snapshots)
        {
            if (snapshots.Count == 0)
            {
                TaskDialog.Show("Tag Version Control", "No snapshots to compare. Take a snapshot first.");
                return;
            }

            var latest = snapshots.Last();
            var current = TagIntelligenceHelper.BuildSnapshot(doc);

            int added = 0, removed = 0, changed = 0, unchanged = 0;
            var changes = new List<string>();

            // Check current against latest
            foreach (var kvp in current.Tags)
            {
                if (latest.Tags.TryGetValue(kvp.Key, out string oldTag))
                {
                    if (oldTag != kvp.Value)
                    {
                        changed++;
                        if (changes.Count < 20)
                            changes.Add($"  CHANGED [{kvp.Key}]: {oldTag} → {kvp.Value}");
                    }
                    else
                    {
                        unchanged++;
                    }
                }
                else
                {
                    added++;
                    if (changes.Count < 20)
                        changes.Add($"  ADDED   [{kvp.Key}]: {kvp.Value}");
                }
            }

            // Check for removals
            foreach (var kvp in latest.Tags)
            {
                if (!current.Tags.ContainsKey(kvp.Key))
                {
                    removed++;
                    if (changes.Count < 20)
                        changes.Add($"  REMOVED [{kvp.Key}]: {kvp.Value}");
                }
            }

            var report = new StringBuilder();
            report.AppendLine($"Comparison: current vs snapshot from {latest.Timestamp:yyyy-MM-dd HH:mm}");
            report.AppendLine(new string('─', 40));
            report.AppendLine($"  Added:     {added:N0}");
            report.AppendLine($"  Removed:   {removed:N0}");
            report.AppendLine($"  Changed:   {changed:N0}");
            report.AppendLine($"  Unchanged: {unchanged:N0}");

            if (changes.Count > 0)
            {
                report.AppendLine();
                report.AppendLine("Details (first 20):");
                foreach (string line in changes)
                    report.AppendLine(line);
                int totalChanges = added + removed + changed;
                if (totalChanges > 20)
                    report.AppendLine($"  ... and {totalChanges - 20} more");
            }

            TaskDialog.Show("Tag Version Control — Diff", report.ToString());
            StingLog.Info($"TagVersionControl: diff result — added={added}, removed={removed}, " +
                $"changed={changed}, unchanged={unchanged}");
        }

        private void ShowHistory(List<TagIntelligenceHelper.TagSnapshot> snapshots)
        {
            if (snapshots.Count == 0)
            {
                TaskDialog.Show("Tag Version Control", "No snapshot history available.");
                return;
            }

            var sb = new StringBuilder();
            sb.AppendLine($"Snapshot History ({snapshots.Count} entries):");
            sb.AppendLine(new string('─', 40));
            for (int i = snapshots.Count - 1; i >= 0; i--)
            {
                var s = snapshots[i];
                sb.AppendLine($"  #{i + 1}  {s.Timestamp:yyyy-MM-dd HH:mm:ss}  ({s.Tags.Count:N0} tags)");
            }

            TaskDialog.Show("Tag Version Control — History", sb.ToString());
        }
    }

    // ═══════════════════════════════════════════════════════════════════
    //  6. TagPropagationCommand — Propagate tags to similar elements
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Propagate tags from one element to linked/similar elements. Finds elements
    /// with matching family+type but missing tags, and copies token values from the
    /// source. Useful for repetitive MEP equipment (e.g., identical AHUs on each floor).
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class TagPropagationCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            Document doc = uidoc.Document;

            // Require a selection — the source element(s)
            var selIds = uidoc.Selection.GetElementIds();
            if (selIds.Count == 0)
            {
                TaskDialog.Show("Tag Propagation",
                    "Select one or more tagged elements to propagate their tags to similar elements.");
                return Result.Failed;
            }

            var known = new HashSet<string>(TagConfig.DiscMap.Keys);
            var sources = new List<Element>();
            foreach (ElementId id in selIds)
            {
                Element el = doc.GetElement(id);
                if (el == null) continue;
                string tag = ParameterHelpers.GetString(el, ParamRegistry.TAG1);
                if (!string.IsNullOrEmpty(tag) && known.Contains(ParameterHelpers.GetCategoryName(el)))
                    sources.Add(el);
            }

            if (sources.Count == 0)
            {
                TaskDialog.Show("Tag Propagation",
                    "No tagged elements in selection. Select elements that already have tags.");
                return Result.Failed;
            }

            // Build family+type keys for source elements
            var sourceKeys = new Dictionary<string, Element>();
            foreach (Element src in sources)
            {
                string family = ParameterHelpers.GetFamilyName(src);
                string type = ParameterHelpers.GetFamilySymbolName(src);
                string key = $"{family}|{type}";
                if (!sourceKeys.ContainsKey(key))
                    sourceKeys[key] = src;
            }

            // Find matching untagged elements
            var allTaggable = TagIntelligenceHelper.CollectTaggable(doc);
            var targets = new List<(Element target, Element source)>();

            foreach (Element el in allTaggable)
            {
                if (selIds.Contains(el.Id)) continue;
                string tag = ParameterHelpers.GetString(el, ParamRegistry.TAG1);
                if (TagConfig.TagIsComplete(tag)) continue;

                string family = ParameterHelpers.GetFamilyName(el);
                string type = ParameterHelpers.GetFamilySymbolName(el);
                string key = $"{family}|{type}";

                if (sourceKeys.TryGetValue(key, out Element srcEl))
                    targets.Add((el, srcEl));
            }

            if (targets.Count == 0)
            {
                TaskDialog.Show("Tag Propagation",
                    $"No untagged elements matching the selected {sourceKeys.Count} family/type combinations.");
                return Result.Succeeded;
            }

            // Confirm propagation
            TaskDialog confirmDlg = new TaskDialog("Tag Propagation");
            confirmDlg.MainInstruction = $"Propagate tags to {targets.Count} elements?";
            confirmDlg.MainContent =
                $"Source elements: {sources.Count}\n" +
                $"Family/type groups: {sourceKeys.Count}\n" +
                $"Target elements (untagged, matching): {targets.Count}\n\n" +
                "Tokens to copy: DISC, LOC, ZONE, SYS, FUNC, PROD\n" +
                "(LVL and SEQ will NOT be copied — they are element-specific)";
            confirmDlg.CommonButtons = TaskDialogCommonButtons.Ok | TaskDialogCommonButtons.Cancel;
            if (confirmDlg.Show() == TaskDialogResult.Cancel)
                return Result.Cancelled;

            StingLog.Info($"TagPropagation: propagating from {sources.Count} sources to {targets.Count} targets");

            // Tokens to propagate (exclude LVL and SEQ — those are position/instance specific)
            string[] tokensToPropagate = {
                ParamRegistry.DISC, ParamRegistry.LOC, ParamRegistry.ZONE,
                ParamRegistry.SYS, ParamRegistry.FUNC, ParamRegistry.PROD
            };

            int propagated = 0;

            using (Transaction t = new Transaction(doc, "STING Tag Propagation"))
            {
                t.Start();

                foreach (var (target, source) in targets)
                {
                    bool any = false;
                    foreach (string param in tokensToPropagate)
                    {
                        string val = ParameterHelpers.GetString(source, param);
                        if (!string.IsNullOrEmpty(val))
                            any |= ParameterHelpers.SetString(target, param, val, false);
                    }
                    if (any) propagated++;
                }

                t.Commit();
            }

            TaskDialog.Show("Tag Propagation",
                $"Propagated tokens to {propagated} of {targets.Count} target elements\n" +
                $"from {sourceKeys.Count} family/type groups.\n\n" +
                "Run 'Build Tags' or 'Auto Tag' next to assign SEQ numbers and assemble final tags.");

            StingLog.Info($"TagPropagation: {propagated}/{targets.Count} elements updated");
            return Result.Succeeded;
        }
    }

    // ═══════════════════════════════════════════════════════════════════
    //  7. TagAnalyticsDashboardCommand — Advanced analytics
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Advanced tag analytics dashboard. Provides text-based distribution charts,
    /// coverage by discipline/system/level, trend analysis from version history
    /// snapshots, and a quality heatmap by level.
    /// </summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class TagAnalyticsDashboardCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            Document doc = commandData.Application.ActiveUIDocument.Document;
            var tagElements = TagIntelligenceHelper.CollectTaggable(doc);

            if (tagElements.Count == 0)
            {
                TaskDialog.Show("Tag Analytics", "No taggable elements found.");
                return Result.Succeeded;
            }

            StingLog.Info($"TagAnalytics: analyzing {tagElements.Count} elements");

            // Gather distributions
            var byDisc = new Dictionary<string, int>();
            var bySys = new Dictionary<string, int>();
            var byLevel = new Dictionary<string, int>();
            var byStatus = new Dictionary<string, int>();
            var coverageByDisc = new Dictionary<string, (int tagged, int total)>();
            var qualityByLevel = new Dictionary<string, (double totalScore, int count)>();

            foreach (Element el in tagElements)
            {
                string disc = ParameterHelpers.GetString(el, ParamRegistry.DISC);
                string sys = ParameterHelpers.GetString(el, ParamRegistry.SYS);
                string lvl = ParameterHelpers.GetString(el, ParamRegistry.LVL);
                string status = ParameterHelpers.GetString(el, ParamRegistry.STATUS);
                string tag = ParameterHelpers.GetString(el, ParamRegistry.TAG1);
                bool hasTag = TagConfig.TagIsComplete(tag);

                string discKey = string.IsNullOrEmpty(disc) ? "<none>" : disc;
                string sysKey = string.IsNullOrEmpty(sys) ? "<none>" : sys;
                string lvlKey = string.IsNullOrEmpty(lvl) ? "<none>" : lvl;
                string statusKey = string.IsNullOrEmpty(status) ? "<none>" : status;

                byDisc[discKey] = byDisc.GetValueOrDefault(discKey) + 1;
                bySys[sysKey] = bySys.GetValueOrDefault(sysKey) + 1;
                byLevel[lvlKey] = byLevel.GetValueOrDefault(lvlKey) + 1;
                byStatus[statusKey] = byStatus.GetValueOrDefault(statusKey) + 1;

                // Coverage tracking
                if (!coverageByDisc.ContainsKey(discKey))
                    coverageByDisc[discKey] = (0, 0);
                var (t, tot) = coverageByDisc[discKey];
                coverageByDisc[discKey] = (t + (hasTag ? 1 : 0), tot + 1);

                // Quality by level
                double score = TagIntelligenceHelper.ScoreElement(el);
                if (!qualityByLevel.ContainsKey(lvlKey))
                    qualityByLevel[lvlKey] = (0, 0);
                var (ts, c) = qualityByLevel[lvlKey];
                qualityByLevel[lvlKey] = (ts + score, c + 1);
            }

            var report = new StringBuilder();
            report.AppendLine($"Tag Analytics Dashboard — {tagElements.Count:N0} elements");
            report.AppendLine(new string('═', 50));

            // Distribution by Discipline
            report.AppendLine();
            report.AppendLine("Distribution by Discipline:");
            int maxCount = byDisc.Values.Max();
            foreach (var kvp in byDisc.OrderByDescending(x => x.Value))
            {
                int barLen = maxCount > 0 ? kvp.Value * 25 / maxCount : 0;
                string bar = new string('█', barLen);
                report.AppendLine($"  {kvp.Key,-6} {kvp.Value,5}  {bar}");
            }

            // Distribution by System (top 12)
            report.AppendLine();
            report.AppendLine("Distribution by System (top 12):");
            maxCount = bySys.Values.Max();
            foreach (var kvp in bySys.OrderByDescending(x => x.Value).Take(12))
            {
                int barLen = maxCount > 0 ? kvp.Value * 25 / maxCount : 0;
                string bar = new string('█', barLen);
                report.AppendLine($"  {kvp.Key,-8} {kvp.Value,5}  {bar}");
            }

            // Coverage by Discipline
            report.AppendLine();
            report.AppendLine("Tag Coverage by Discipline:");
            foreach (var kvp in coverageByDisc.OrderBy(x => x.Key))
            {
                double pct = kvp.Value.total > 0 ? 100.0 * kvp.Value.tagged / kvp.Value.total : 0;
                int barLen = (int)(pct / 4);
                string bar = new string('█', barLen) + new string('░', 25 - barLen);
                report.AppendLine($"  {kvp.Key,-6} {pct,5:F1}%  {bar}  ({kvp.Value.tagged}/{kvp.Value.total})");
            }

            // Quality heatmap by Level
            report.AppendLine();
            report.AppendLine("Quality Heatmap by Level:");
            foreach (var kvp in qualityByLevel.OrderBy(x => x.Key))
            {
                double avgScore = kvp.Value.count > 0 ? kvp.Value.totalScore / kvp.Value.count : 0;
                string grade = avgScore >= 90 ? "A+" : avgScore >= 80 ? "A" :
                    avgScore >= 70 ? "B" : avgScore >= 60 ? "C" : avgScore >= 50 ? "D" : "F";
                int barLen = (int)(avgScore / 4);
                string bar = new string('█', barLen);
                report.AppendLine($"  {kvp.Key,-6} {avgScore,5:F1}%  [{grade}]  {bar}  ({kvp.Value.count} elements)");
            }

            // Status distribution
            report.AppendLine();
            report.AppendLine("Status Distribution:");
            foreach (var kvp in byStatus.OrderByDescending(x => x.Value))
                report.AppendLine($"  {kvp.Key,-12} {kvp.Value,5}");

            // Trend analysis from snapshots
            string snapshotPath = TagIntelligenceHelper.GetSnapshotPath(doc);
            var snapshots = TagIntelligenceHelper.LoadSnapshots(snapshotPath);
            if (snapshots.Count >= 2)
            {
                report.AppendLine();
                report.AppendLine("Tag Count Trend (from snapshots):");
                foreach (var s in snapshots.TakeLast(10))
                    report.AppendLine($"  {s.Timestamp:yyyy-MM-dd HH:mm}  {s.Tags.Count,6:N0} tags");

                int first = snapshots.First().Tags.Count;
                int last = snapshots.Last().Tags.Count;
                int delta = last - first;
                report.AppendLine($"  Net change: {(delta >= 0 ? "+" : "")}{delta:N0} tags");
            }

            TaskDialog td = new TaskDialog("Tag Analytics Dashboard");
            td.MainInstruction = $"Analytics: {tagElements.Count:N0} elements across " +
                $"{byDisc.Count} disciplines, {bySys.Count} systems, {byLevel.Count} levels";
            td.MainContent = report.ToString();
            td.Show();

            StingLog.Info($"TagAnalytics: {tagElements.Count} elements, " +
                $"{byDisc.Count} disciplines, {bySys.Count} systems, {byLevel.Count} levels");
            return Result.Succeeded;
        }
    }

    // ═══════════════════════════════════════════════════════════════════
    //  8. SmartTagSuggestCommand — AI-like tag suggestion
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Smart tag suggestion engine. For selected elements, analyzes context (family name,
    /// location, system connections, spatial data) and suggests optimal DISC/SYS/FUNC/PROD
    /// codes. Shows suggestions before applying so the user can review.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class SmartTagSuggestCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            Document doc = uidoc.Document;

            string scopeLabel;
            var targets = TagIntelligenceHelper.CollectTargets(uidoc, out scopeLabel);

            if (targets.Count == 0)
            {
                TaskDialog.Show("Smart Tag Suggest", "No taggable elements found.");
                return Result.Succeeded;
            }

            // Limit to manageable batch for suggestion review
            int maxSuggest = 50;
            bool truncated = targets.Count > maxSuggest;
            var batch = truncated ? targets.Take(maxSuggest).ToList() : targets;

            StingLog.Info($"SmartTagSuggest: analyzing {batch.Count} elements ({scopeLabel})");

            // Build room index for spatial analysis
            var roomIndex = SpatialAutoDetect.BuildRoomIndex(doc);
            string projectLoc = SpatialAutoDetect.DetectProjectLoc(doc);

            // Generate suggestions
            var suggestions = new List<(Element el, string disc, string sys, string func, string prod,
                string discSource, string sysSource, string funcSource, string prodSource)>();

            foreach (Element el in batch)
            {
                string cat = ParameterHelpers.GetCategoryName(el);
                string family = ParameterHelpers.GetFamilyName(el);

                // DISC: category map → workset inference → default
                string disc = "";
                string discSrc = "";
                if (TagConfig.DiscMap.TryGetValue(cat, out string mapDisc))
                {
                    disc = mapDisc;
                    discSrc = "Category map";
                }
                else
                {
                    var wsResult = TagIntelligence.InferDiscFromWorkset(el);
                    if (wsResult != null)
                    {
                        disc = wsResult.Value;
                        discSrc = wsResult.Source;
                    }
                    else
                    {
                        disc = "GEN";
                        discSrc = "Default";
                    }
                }

                // SYS: MEP system → connected equipment → category map → adjacent → default
                string sys = "";
                string sysSrc = "";
                string mepSys = TagConfig.GetMepSystemAwareSysCode(el, cat);
                if (mepSys != TagConfig.GetSysCode(cat))
                {
                    sys = mepSys;
                    sysSrc = "MEP system name";
                }
                else
                {
                    var connResult = TagIntelligence.InferSysFromConnectedEquipment(el);
                    if (connResult != null)
                    {
                        sys = connResult.Value;
                        sysSrc = connResult.Source;
                    }
                    else
                    {
                        sys = TagConfig.GetSysCode(cat);
                        sysSrc = "Category map";
                    }
                }

                // FUNC: system → function map
                string func = TagConfig.GetFuncCode(sys);
                string funcSrc = !string.IsNullOrEmpty(func) ? "SYS→FUNC map" : "Default";
                if (string.IsNullOrEmpty(func)) func = "GEN";

                // PROD: family-aware → category map
                string prod = TagConfig.GetFamilyAwareProdCode(el, cat);
                string prodSrc = prod != TagConfig.ProdMap.GetValueOrDefault(cat, "GEN")
                    ? $"Family: {family}" : "Category map";

                suggestions.Add((el, disc, sys, func, prod, discSrc, sysSrc, funcSrc, prodSrc));
            }

            // Build suggestion report
            var report = new StringBuilder();
            report.AppendLine($"Smart Tag Suggestions for {batch.Count} elements");
            if (truncated) report.AppendLine($"(showing first {maxSuggest} of {targets.Count})");
            report.AppendLine(new string('═', 55));

            // Group by suggestion pattern for compact display
            var grouped = suggestions
                .GroupBy(s => $"{s.disc}|{s.sys}|{s.func}|{s.prod}")
                .OrderByDescending(g => g.Count())
                .ToList();

            foreach (var group in grouped)
            {
                var first = group.First();
                string cat = ParameterHelpers.GetCategoryName(first.el);
                report.AppendLine();
                report.AppendLine($"  [{group.Count()} elements] {cat}");
                report.AppendLine($"    DISC={first.disc} ({first.discSource})");
                report.AppendLine($"    SYS ={first.sys} ({first.sysSource})");
                report.AppendLine($"    FUNC={first.func} ({first.funcSource})");
                report.AppendLine($"    PROD={first.prod} ({first.prodSource})");
            }

            // Ask to apply
            TaskDialog td = new TaskDialog("Smart Tag Suggest");
            td.MainInstruction = $"Suggestions for {batch.Count} elements ({grouped.Count} patterns)";
            td.MainContent = report.ToString();
            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink1,
                $"Apply All Suggestions",
                $"Set DISC/SYS/FUNC/PROD on {batch.Count} elements");
            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink2,
                "Review Only (do not apply)",
                "Close after reviewing suggestions");
            td.CommonButtons = TaskDialogCommonButtons.Cancel;

            var choice = td.Show();

            if (choice != TaskDialogResult.CommandLink1)
            {
                StingLog.Info($"SmartTagSuggest: reviewed {suggestions.Count} suggestions, not applied");
                return choice == TaskDialogResult.CommandLink2 ? Result.Succeeded : Result.Cancelled;
            }

            // Apply suggestions
            int applied = 0;
            using (Transaction t = new Transaction(doc, "STING Smart Tag Suggest"))
            {
                t.Start();

                foreach (var (el, disc, sys, func, prod, _, _, _, _) in suggestions)
                {
                    bool any = false;
                    any |= ParameterHelpers.SetString(el, ParamRegistry.DISC, disc, false);
                    any |= ParameterHelpers.SetString(el, ParamRegistry.SYS, sys, false);
                    any |= ParameterHelpers.SetString(el, ParamRegistry.FUNC, func, false);
                    any |= ParameterHelpers.SetString(el, ParamRegistry.PROD, prod, false);
                    if (any) applied++;
                }

                t.Commit();
            }

            TaskDialog.Show("Smart Tag Suggest",
                $"Applied suggestions to {applied} of {batch.Count} elements.\n\n" +
                "Run 'Build Tags' or 'Auto Tag' to assign SEQ numbers and assemble final tags.");

            StingLog.Info($"SmartTagSuggest: applied {applied}/{batch.Count} suggestions");
            return Result.Succeeded;
        }
    }
}
