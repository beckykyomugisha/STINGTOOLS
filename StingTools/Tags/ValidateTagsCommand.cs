using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Core;

namespace StingTools.Tags
{
    /// <summary>
    /// Validate existing tags for completeness across ALL containers:
    ///   - ASS_TAG_1_TXT through ASS_TAG_6_TXT (primary tag + 5 containers)
    ///   - Discipline-specific containers (HVC_EQP_TAG, ELC_EQP_TAG, etc.)
    ///   - Individual tokens (DISC, LOC, ZONE, LVL, SYS, FUNC, PROD, SEQ)
    ///   - Construction STATUS (EXISTING, NEW, DEMOLISHED, TEMPORARY)
    ///   - Phase consistency (STATUS vs Revit phase alignment)
    ///   - Revision tracking (REV token population)
    ///
    /// Reports issues by category with export-to-CSV option.
    /// Checks for: missing tags, incomplete tags (empty segments),
    /// unpopulated containers, missing individual tokens, STATUS gaps,
    /// phase mismatches, duplicate tags, and ISO 19650 code violations.
    ///
    /// Report style uses flowing narrative paragraphs for compliance summaries
    /// to provide context-rich feedback rather than bare statistics.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class ValidateTagsCommand : IExternalCommand
    {
        private static string[] TokenParams => ParamRegistry.AllTokenParams;

        // TAG-01: Valid STATUS values — typos like 'DEMO' or 'EX' are caught
        private static readonly HashSet<string> ValidStatuses = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase)
        {
            "NEW", "EXISTING", "DEMOLISHED", "TEMPORARY"
        };

        private static string[] UniversalContainers => new[]
        {
            ParamRegistry.TAG1, ParamRegistry.TAG2, ParamRegistry.TAG3,
            ParamRegistry.TAG4, ParamRegistry.TAG5, ParamRegistry.TAG6,
        };

        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }
            Document doc = ctx.Doc;

            // Performance: use ElementMulticategoryFilter to skip non-taggable elements
            var collector = new FilteredElementCollector(doc)
                .WhereElementIsNotElementType();
            var catEnums = SharedParamGuids.AllCategoryEnums;
            if (catEnums != null && catEnums.Length > 0)
                collector.WherePasses(new ElementMulticategoryFilter(new List<BuiltInCategory>(catEnums)));

            var knownCategories = new HashSet<string>(TagConfig.DiscMap.Keys);
            int total = 0;
            int fullyValid = 0; // all 8 tokens + TAG_1 complete
            int tag1Valid = 0;
            int tag1Incomplete = 0;
            int tag1Missing = 0;
            // Three mutually-exclusive compliance buckets
            int bucketFully = 0;    // TagIsComplete AND TagIsFullyResolved
            int bucketPartial = 0;  // non-empty but not fully complete/resolved
            int bucketUntagged = 0; // null or empty
            int containersEmpty = 0; // TAG_2-6 not populated
            int tokensMissing = 0;
            int isoViolations = 0; // ISO 19650 code violations
            int crossValErrors = 0; // PROD/FUNC/DISC cross-validation errors
            int duplicateTags = 0;
            // STATUS and REV tracking
            int statusEmpty = 0;
            int statusMismatch = 0; // STATUS doesn't match Revit phase
            int revEmpty = 0;
            int fullyResolved = 0; // no placeholder segments
            var statusDistribution = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var issuesByCategory = new Dictionary<string, int>();
            var tokenIssues = new Dictionary<string, int>(); // which tokens are most often empty
            var isoIssueTypes = new Dictionary<string, int>(); // ISO violation types
            var csvRows = new List<string>();
            // Track tag uniqueness
            var tagCounts = new Dictionary<string, int>(StringComparer.Ordinal);

            csvRows.Add("ElementId,Category,TAG_1_Status,FullyResolved,EmptyTokens,EmptyContainers,ISOErrors,CrossValErrors,STATUS,REV,TAG_1,TAG_2,TAG_3,TAG_4,TAG_5,TAG_6");

            foreach (Element el in collector)
            {
                string catName = ParameterHelpers.GetCategoryName(el);
                if (string.IsNullOrEmpty(catName) || !knownCategories.Contains(catName))
                    continue;

                total++;
                string tag1 = ParameterHelpers.GetString(el, ParamRegistry.TAG1);

                // Check TAG_1
                string tag1Status;
                if (string.IsNullOrEmpty(tag1))
                {
                    tag1Missing++;
                    bucketUntagged++;
                    tag1Status = "MISSING";
                    IncrementDict(issuesByCategory, catName);
                }
                else if (TagConfig.TagIsComplete(tag1) && TagConfig.TagIsFullyResolved(tag1))
                {
                    tag1Valid++;
                    fullyResolved++;
                    bucketFully++;
                    tag1Status = "VALID";
                }
                else if (TagConfig.TagIsComplete(tag1))
                {
                    tag1Valid++;
                    bucketPartial++;
                    tag1Status = "VALID";
                }
                else
                {
                    // Non-empty but not complete — partially tagged
                    tag1Incomplete++;
                    bucketPartial++;
                    tag1Status = "INCOMPLETE";
                    IncrementDict(issuesByCategory, catName);
                }

                // Check individual tokens
                int emptyTokenCount = 0;
                foreach (string token in TokenParams)
                {
                    string val = ParameterHelpers.GetString(el, token);
                    if (string.IsNullOrEmpty(val))
                    {
                        emptyTokenCount++;
                        tokensMissing++;
                        IncrementDict(tokenIssues, token);
                    }
                }

                // Check STATUS token
                string statusVal = ParameterHelpers.GetString(el, ParamRegistry.STATUS);
                if (string.IsNullOrEmpty(statusVal))
                {
                    statusEmpty++;
                }
                else
                {
                    IncrementDict(statusDistribution, statusVal);

                    // TAG-01: Validate STATUS against allowed values
                    // Catch typos like 'DEMO', 'EX', 'TEMP' etc.
                    if (!ValidStatuses.Contains(statusVal))
                    {
                        isoViolations++;
                        IncrementDict(isoIssueTypes,
                            $"Invalid STATUS '{statusVal}' (expected: NEW/EXISTING/DEMOLISHED/TEMPORARY)");
                        IncrementDict(issuesByCategory, catName);
                    }

                    // Cross-validate STATUS against Revit phase
                    string phaseStatus = PhaseAutoDetect.DetectStatus(doc, el);
                    if (!string.IsNullOrEmpty(phaseStatus) &&
                        !string.Equals(statusVal, phaseStatus, StringComparison.OrdinalIgnoreCase))
                    {
                        statusMismatch++;
                    }
                }

                // Check REV token — GAP-008 fix: validate format via RevisionEngine
                string revVal = ParameterHelpers.GetString(el, ParamRegistry.REV);
                if (string.IsNullOrEmpty(revVal))
                    revEmpty++;
                else
                {
                    string revError = BIMManager.RevisionEngine.ValidateRevisionNumber(revVal);
                    if (revError != null)
                    {
                        isoViolations++;
                        IncrementDict(isoIssueTypes, $"Invalid REV format: {revVal}");
                    }
                }

                // Track tag uniqueness
                if (!string.IsNullOrEmpty(tag1))
                {
                    if (!tagCounts.ContainsKey(tag1)) tagCounts[tag1] = 0;
                    tagCounts[tag1]++;
                }

                // Single-pass ISO validation via ValidateElement
                var elementErrors = ISO19650Validator.ValidateElement(el);
                int elementCrossErrors = elementErrors.Count(e => e.Type == ValidationErrorType.CrossValidation);
                int elementTokenErrors = elementErrors.Count - elementCrossErrors;
                // Count token-level and cross-validation errors separately (no double-counting)
                isoViolations += elementTokenErrors;
                crossValErrors += elementCrossErrors;
                int elementIsoErrors = elementErrors.Count;
                if (elementIsoErrors > 0)
                {
                    if (tag1Status == "VALID") tag1Status = "ISO_INVALID";
                    IncrementDict(issuesByCategory, catName);
                    foreach (var err in elementErrors)
                        IncrementDict(isoIssueTypes, err.Message);
                }

                // Check TAG_2-6 containers
                int emptyContainers = 0;
                var tagValues = new string[6];
                for (int i = 0; i < UniversalContainers.Length; i++)
                {
                    tagValues[i] = ParameterHelpers.GetString(el, UniversalContainers[i]);
                    if (string.IsNullOrEmpty(tagValues[i]) && i > 0) // TAG_2-6
                        emptyContainers++;
                }
                containersEmpty += emptyContainers;

                // Check if this element's tag is fully resolved (no XX/ZZ/0000 placeholders)
                bool elementResolved = !string.IsNullOrEmpty(tag1) && TagConfig.TagIsFullyResolved(tag1);

                // GAP-02: Fully valid = TAG_1 complete + fully resolved + all tokens filled
                // + containers populated + ISO valid + STATUS populated + REV populated
                bool hasStatus = !string.IsNullOrEmpty(statusVal);
                bool hasRev = !string.IsNullOrEmpty(revVal);
                if (tag1Status == "VALID" && elementResolved && emptyTokenCount == 0 &&
                    emptyContainers == 0 && elementIsoErrors == 0 && hasStatus && hasRev)
                    fullyValid++;

                // CSV row (includes FullyResolved and CrossValErrors columns)
                csvRows.Add($"{el.Id},\"{CsvEsc(catName)}\",{tag1Status},{elementResolved}," +
                    $"{emptyTokenCount},{emptyContainers},{elementTokenErrors},{elementCrossErrors}," +
                    $"\"{CsvEsc(statusVal)}\",\"{CsvEsc(revVal)}\"," +
                    $"\"{CsvEsc(tagValues[0])}\",\"{CsvEsc(tagValues[1])}\",\"{CsvEsc(tagValues[2])}\"," +
                    $"\"{CsvEsc(tagValues[3])}\",\"{CsvEsc(tagValues[4])}\",\"{CsvEsc(tagValues[5])}\"");
            }

            // Count duplicate tags
            duplicateTags = tagCounts.Count(kvp => kvp.Value > 1);
            int dupElements = tagCounts.Where(kvp => kvp.Value > 1).Sum(kvp => kvp.Value);

            // Build percentages
            double tag1Pct = total > 0 ? tag1Valid * 100.0 / total : 0;
            double fullPct = total > 0 ? fullyValid * 100.0 / total : 0;
            double resolvedPct = total > 0 ? fullyResolved * 100.0 / total : 0;
            double statusPct = total > 0 ? (total - statusEmpty) * 100.0 / total : 0;
            double revPct = total > 0 ? (total - revEmpty) * 100.0 / total : 0;
            // Three-bucket compliance: fully=1.0, partially=0.5, untagged=0.0
            double compliancePct = total > 0
                ? (bucketFully + 0.5 * bucketPartial) / total * 100.0
                : 0;

            // Export CSV
            string csvPath = null;
            try
            {
                csvPath = OutputLocationHelper.GetOutputPath(doc, "STING_Validation_Report.csv");
                File.WriteAllText(csvPath, string.Join(Environment.NewLine, csvRows));
            }
            catch (Exception ex) { StingLog.Warn($"Validation CSV export: {ex.Message}"); }

            // BIM integration
            if (!string.IsNullOrEmpty(csvPath) && File.Exists(csvPath))
                StingTools.BIMManager.BIMManagerEngine.AutoRegisterExport(doc, csvPath, "RP", "Tag validation report (ISO 19650 compliance)");
            int issuesRaised = StingTools.BIMManager.BIMManagerEngine.AutoRaiseComplianceIssues(doc);
            if (issuesRaised > 0)
                StingLog.Info($"ValidateTagsCommand: auto-raised {issuesRaised} BIM compliance issues");

            // ── Build rich result panel ──
            var panel = UI.StingResultPanel.Create("Validate Tags (ISO 19650)")
                .SetSubtitle($"Compliance: {compliancePct:F1}% | Full: {bucketFully} | Partial: {bucketPartial} | Untagged: {bucketUntagged} | Violations: {isoViolations}")
                .SetOverallPct(compliancePct);
            if (!string.IsNullOrEmpty(csvPath)) panel.SetCsvPath(csvPath);

            // Three-bucket compliance
            panel.AddSection("THREE-BUCKET COMPLIANCE")
                .RAGBar(compliancePct)
                .MetricHighlight("Fully tagged", $"{bucketFully:N0}", "complete + resolved, no placeholders")
                .MetricWarn("Partially tagged", $"{bucketPartial:N0}", "has tag data but incomplete or unresolved")
                .MetricError("Untagged", $"{bucketUntagged:N0}", "no tag at all");

            // ISO 19650 code compliance
            panel.AddSection("ISO 19650 CODE COMPLIANCE", new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(0xC6, 0x28, 0x28)));
            if (isoViolations == 0 && crossValErrors == 0 && duplicateTags == 0)
                panel.Info("All tag codes conform to ISO 19650, CIBSE, and Uniclass 2015. No duplicates found.");
            else
            {
                if (isoViolations > 0) panel.MetricError("Token-level violations", $"{isoViolations:N0}");
                if (crossValErrors > 0) panel.MetricWarn("Cross-validation mismatches", $"{crossValErrors:N0}");
                if (duplicateTags > 0) panel.MetricWarn("Duplicate tags", $"{duplicateTags:N0}", $"{dupElements:N0} elements affected");
                foreach (var kvp in isoIssueTypes.OrderByDescending(x => x.Value).Take(5))
                    panel.Alert($"{kvp.Key}: {kvp.Value}x");
            }

            // STATUS & Phasing
            panel.AddSection("CONSTRUCTION STATUS & PHASING")
                .RAGBar(statusPct, $"{statusPct:F1}% have STATUS");
            if (statusEmpty > 0)
                panel.MetricError("Missing STATUS", $"{statusEmpty:N0}", $"{(100.0 - statusPct):F1}% of model");
            if (statusMismatch > 0)
                panel.MetricWarn("Phase mismatches", $"{statusMismatch:N0}", "STATUS differs from Revit phase");
            if (statusDistribution.Count > 0)
            {
                var statusHeaders = new[] { "Status", "Count" };
                var statusRows = statusDistribution.OrderByDescending(x => x.Value)
                    .Select(x => new[] { x.Key, x.Value.ToString() }).ToList();
                panel.Table(statusHeaders, statusRows);
            }

            // Revision tracking
            panel.AddSection("REVISION TRACKING")
                .RAGBar(revPct, $"{revPct:F1}% have REV");
            if (revEmpty == 0) panel.Info($"All {total:N0} elements have REV assigned.");
            else if (revEmpty == total) panel.Alert("No elements have a revision code assigned.");
            else panel.Metric("Elements with REV", $"{total - revEmpty:N0}", $"{revEmpty:N0} still missing");

            // Token coverage
            if (tokenIssues.Count > 0)
            {
                panel.AddSection("EMPTY TOKENS (TOP 10)");
                foreach (var kvp in tokenIssues.OrderByDescending(x => x.Value).Take(10))
                {
                    string shortName = kvp.Key.Replace("ASS_", "").Replace("_TXT", "").Replace("_COD", "");
                    panel.MetricError(shortName, $"{kvp.Value:N0} empty");
                }
            }

            // Issues by category
            if (issuesByCategory.Count > 0)
            {
                var catHeaders = new[] { "Category", "Issues" };
                var catRows = issuesByCategory.OrderByDescending(x => x.Value).Take(15)
                    .Select(x => new[] { x.Key, x.Value.ToString() }).ToList();
                panel.AddSection("ISSUES BY CATEGORY").Table(catHeaders, catRows);
            }

            // Full compliance summary
            panel.AddSection("FULL COMPLIANCE SUMMARY")
                .RAGBar(fullPct, $"{fullPct:F1}% fully compliant")
                .Metric("Fully valid elements", $"{fullyValid:N0} of {total:N0}");
            if (tokensMissing > 0) panel.Metric("Empty token values", $"{tokensMissing:N0}");
            if (containersEmpty > 0) panel.Metric("Empty containers", $"{containersEmpty:N0}", "TAG_2 through TAG_6");
            string verdict = fullPct >= 95 ? "Excellent — ready for delivery" :
                fullPct >= 75 ? "Good progress — needs attention before delivery" :
                fullPct >= 50 ? "Significant work remains" : "Substantial tagging effort required";
            panel.Text(verdict, fullPct >= 75
                ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x2E, 0x7D, 0x32))
                : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xC6, 0x28, 0x28)));

            // Action buttons
            panel.Action("Create Validation Legend", "Generate a validation status legend view", null);
            bool hasIssues = tag1Missing > 0 || tag1Incomplete > 0 || isoViolations > 0 || duplicateTags > 0;
            if (hasIssues)
            {
                panel.Action("Fix All Issues Now",
                    "Run ResolveAllIssues to auto-fix missing tokens, duplicates, and violations", win =>
                {
                    win.Close();
                    try
                    {
                        var resolver = new ResolveAllIssuesCommand();
                        string resolveMsg = "";
                        resolver.Execute(commandData, ref resolveMsg, elements);
                    }
                    catch (Exception ex)
                    {
                        StingLog.Error("ValidateTagsCommand: ResolveAllIssues failed", ex);
                        TaskDialog.Show("Validate Tags", $"Auto-fix failed: {ex.Message}");
                    }
                });
            }

            int clickedAction = panel.Show();

            if (clickedAction == 0) // Create Validation Legend
            {
                // Build validation legend entries from actual counts
                var legendEntries = new List<LegendBuilder.LegendEntry>();
                if (tag1Valid > 0)
                    legendEntries.Add(new LegendBuilder.LegendEntry
                    {
                        Color = StingColorRegistry.ValidationStatus["VALID"],
                        Label = "Valid",
                        Description = $"{tag1Valid} elements",
                        Bold = true,
                    });
                if (tag1Incomplete > 0)
                    legendEntries.Add(new LegendBuilder.LegendEntry
                    {
                        Color = StingColorRegistry.ValidationStatus["INCOMPLETE"],
                        Label = "Incomplete",
                        Description = $"{tag1Incomplete} elements",
                        Bold = true,
                    });
                if (tag1Missing > 0)
                    legendEntries.Add(new LegendBuilder.LegendEntry
                    {
                        Color = StingColorRegistry.ValidationStatus["MISSING"],
                        Label = "Missing",
                        Description = $"{tag1Missing} elements",
                        Bold = true,
                    });
                if (isoViolations > 0)
                    legendEntries.Add(new LegendBuilder.LegendEntry
                    {
                        Color = StingColorRegistry.ValidationStatus["INVALID"],
                        Label = "ISO Violations",
                        Description = $"{isoViolations} violations",
                        Bold = true,
                    });
                if (duplicateTags > 0)
                    legendEntries.Add(new LegendBuilder.LegendEntry
                    {
                        Color = StingColorRegistry.ValidationStatus["DUPLICATE"],
                        Label = "Duplicates",
                        Description = $"{duplicateTags} duplicate tags",
                        Bold = true,
                    });

                if (legendEntries.Count > 0)
                {
                    using (Transaction ltx = new Transaction(doc, "STING Validation Legend"))
                    {
                        ltx.Start();
                        var legendConfig = new LegendBuilder.LegendConfig
                        {
                            Title = "Tag Validation Status",
                            Subtitle = $"TAG_1: {tag1Pct:F1}% | Full: {fullPct:F1}%",
                            Footer = "STING Tools — ISO 19650 Validation",
                        };
                        LegendBuilder.CreateLegendView(doc, legendEntries, legendConfig);
                        ltx.Commit();
                    }
                }
            }

            return Result.Succeeded;
        }

        private static void IncrementDict(Dictionary<string, int> dict, string key)
        {
            if (dict.ContainsKey(key)) dict[key]++;
            else dict[key] = 1;
        }

        private static string CsvEsc(string v)
        {
            if (string.IsNullOrEmpty(v)) return "";
            return v.Replace("\"", "\"\"");
        }
    }
}
