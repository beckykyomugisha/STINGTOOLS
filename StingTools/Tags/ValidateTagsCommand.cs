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
    ///
    /// Reports issues by category with export-to-CSV option.
    /// Checks for: missing tags, incomplete tags (empty segments),
    /// unpopulated containers, missing individual tokens.
    /// </summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class ValidateTagsCommand : IExternalCommand
    {
        private static string[] TokenParams => ParamRegistry.AllTokenParams;

        private static string[] UniversalContainers => new[]
        {
            ParamRegistry.TAG1, ParamRegistry.TAG2, ParamRegistry.TAG3,
            ParamRegistry.TAG4, ParamRegistry.TAG5, ParamRegistry.TAG6,
        };

        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            Document doc = commandData.Application.ActiveUIDocument.Document;

            var collector = new FilteredElementCollector(doc)
                .WhereElementIsNotElementType();

            var knownCategories = new HashSet<string>(TagConfig.DiscMap.Keys);
            int total = 0;
            int fullyValid = 0; // all 8 tokens + TAG_1 complete
            int tag1Valid = 0;
            int tag1Incomplete = 0;
            int tag1Missing = 0;
            int containersEmpty = 0; // TAG_2-6 not populated
            int tokensMissing = 0;
            int isoViolations = 0; // ISO 19650 code violations
            int crossValErrors = 0; // PROD/FUNC/DISC cross-validation errors
            int duplicateTags = 0;
            var issuesByCategory = new Dictionary<string, int>();
            var tokenIssues = new Dictionary<string, int>(); // which tokens are most often empty
            var isoIssueTypes = new Dictionary<string, int>(); // ISO violation types
            var csvRows = new List<string>();
            // Track tag uniqueness
            var tagCounts = new Dictionary<string, int>(StringComparer.Ordinal);

            csvRows.Add("ElementId,Category,TAG_1_Status,EmptyTokens,EmptyContainers,ISOErrors,TAG_1,TAG_2,TAG_3,TAG_4,TAG_5,TAG_6");

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
                    tag1Status = "MISSING";
                    IncrementDict(issuesByCategory, catName);
                }
                else if (TagConfig.TagIsComplete(tag1))
                {
                    tag1Valid++;
                    tag1Status = "VALID";
                }
                else
                {
                    tag1Incomplete++;
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

                // Track tag uniqueness
                if (!string.IsNullOrEmpty(tag1))
                {
                    if (!tagCounts.ContainsKey(tag1)) tagCounts[tag1] = 0;
                    tagCounts[tag1]++;
                }

                // Single-pass ISO validation via ValidateElement (avoids double-counting)
                // Now includes PROD↔DISC, FUNC↔SYS cross-validation
                var elementErrors = ISO19650Validator.ValidateElement(el);
                int elementIsoErrors = elementErrors.Count;
                int crossErrors = elementErrors.Count(e =>
                    e.Contains("mismatch") || e.Contains("typically belongs") || e.Contains("unexpected"));
                crossValErrors += crossErrors;
                if (elementIsoErrors > 0)
                {
                    isoViolations += elementIsoErrors;
                    if (tag1Status == "VALID") tag1Status = "ISO_INVALID";
                    IncrementDict(issuesByCategory, catName);
                    foreach (string err in elementErrors)
                        IncrementDict(isoIssueTypes, err);
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

                // Fully valid = TAG_1 complete + all tokens filled + containers populated + ISO valid
                if (tag1Status == "VALID" && emptyTokenCount == 0 && emptyContainers == 0 && elementIsoErrors == 0)
                    fullyValid++;

                // CSV row
                csvRows.Add($"{el.Id},\"{CsvEsc(catName)}\",{tag1Status},{emptyTokenCount},{emptyContainers},{elementIsoErrors}," +
                    $"\"{CsvEsc(tagValues[0])}\",\"{CsvEsc(tagValues[1])}\",\"{CsvEsc(tagValues[2])}\"," +
                    $"\"{CsvEsc(tagValues[3])}\",\"{CsvEsc(tagValues[4])}\",\"{CsvEsc(tagValues[5])}\"");
            }

            // Build report
            var report = new StringBuilder();
            report.AppendLine("Tag Validation Report (ISO 19650)");
            report.AppendLine(new string('═', 50));
            report.AppendLine();

            report.AppendLine("── ASS_TAG_1 Status ──");
            report.AppendLine($"  Total taggable:   {total}");
            report.AppendLine($"  Valid:            {tag1Valid}");
            report.AppendLine($"  Incomplete:       {tag1Incomplete}");
            report.AppendLine($"  Missing:          {tag1Missing}");
            double tag1Pct = total > 0 ? tag1Valid * 100.0 / total : 0;
            report.AppendLine($"  TAG_1 compliance: {tag1Pct:F1}%");

            // Count duplicate tags
            duplicateTags = tagCounts.Count(kvp => kvp.Value > 1);

            report.AppendLine();
            report.AppendLine("── ISO 19650 Code Validation ──");
            report.AppendLine($"  Token violations:    {isoViolations}");
            report.AppendLine($"  Cross-val errors:    {crossValErrors} (PROD/FUNC/DISC mismatches)");
            report.AppendLine($"  Duplicate tags:      {duplicateTags}");
            if (isoIssueTypes.Count > 0)
            {
                foreach (var kvp in isoIssueTypes.OrderByDescending(x => x.Value).Take(10))
                    report.AppendLine($"    {kvp.Key}: {kvp.Value}x");
            }
            else
            {
                report.AppendLine("    All codes conform to ISO 19650");
            }

            // Show top duplicate tags
            if (duplicateTags > 0)
            {
                report.AppendLine();
                report.AppendLine("── Duplicate Tags (top 5) ──");
                foreach (var kvp in tagCounts.Where(x => x.Value > 1)
                    .OrderByDescending(x => x.Value).Take(5))
                    report.AppendLine($"    {kvp.Key}: {kvp.Value} instances");
            }

            report.AppendLine();
            report.AppendLine("── Full Compliance (tokens + containers + ISO) ──");
            double fullPct = total > 0 ? fullyValid * 100.0 / total : 0;
            report.AppendLine($"  Fully compliant:  {fullyValid}/{total} ({fullPct:F1}%)");
            report.AppendLine($"  Empty tokens:     {tokensMissing}");
            report.AppendLine($"  Empty containers: {containersEmpty} (TAG_2-6)");

            if (tokenIssues.Count > 0)
            {
                report.AppendLine();
                report.AppendLine("── Most Common Empty Tokens ──");
                foreach (var kvp in tokenIssues.OrderByDescending(x => x.Value).Take(8))
                {
                    string shortName = kvp.Key.Replace("ASS_", "").Replace("_TXT", "").Replace("_COD", "");
                    report.AppendLine($"  {shortName,-20} {kvp.Value,5} empty");
                }
            }

            if (issuesByCategory.Count > 0)
            {
                report.AppendLine();
                report.AppendLine("── Issues by Category ──");
                foreach (var kvp in issuesByCategory.OrderByDescending(x => x.Value).Take(15))
                    report.AppendLine($"  {kvp.Key,-25} {kvp.Value} issues");
            }

            // Export CSV
            string csvPath = null;
            try
            {
                string dir = Path.GetDirectoryName(doc.PathName);
                if (string.IsNullOrEmpty(dir)) dir = Path.GetTempPath();
                csvPath = Path.Combine(dir, "STING_Validation_Report.csv");
                File.WriteAllText(csvPath, string.Join(Environment.NewLine, csvRows));
                report.AppendLine();
                report.AppendLine($"── CSV exported to: {csvPath} ──");
            }
            catch (Exception ex)
            {
                StingLog.Warn($"Validation CSV export: {ex.Message}");
            }

            TaskDialog td = new TaskDialog("Validate Tags (ISO 19650)");
            td.MainInstruction = $"TAG_1: {tag1Pct:F1}% | ISO: {fullPct:F1}% | Violations: {isoViolations} | Dupes: {duplicateTags}";
            td.MainContent = report.ToString();
            td.Show();

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
