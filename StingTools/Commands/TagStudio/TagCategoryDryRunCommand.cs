// ============================================================================
// TagCategoryDryRunCommand.cs — declared vs actual category, all loaded tag
// families, read-only.
//
// PropagateUniversalTagCommand recategorises a target to its DECLARED category
// (STING_TAG_CONFIG_v5_0_*.csv) rather than preserving whatever it finds. Before
// running that across 206 families, the operator needs to see what it would
// change — and the answer depends on live FamilyCategory, which no offline read
// of the CSVs can supply.
//
// This command answers it and changes nothing. It also identifies the safe
// smoke-test target: a family whose declared category ALREADY matches, which is
// the control case for the LoadFamily-refuses-a-category-change hypothesis.
// ============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Core;
using StingTools.Tags;

namespace StingTools.Commands.TagStudio
{
    /// <summary>Read-only: declared vs actual tag category for every loaded STING tag family.</summary>
    [Transaction(TransactionMode.ReadOnly)]
    public class TagCategoryDryRunCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                var ctx = ParameterHelpers.GetContext(commandData);
                if (ctx?.Doc == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }
                Document doc = ctx.Doc;

                var families = new FilteredElementCollector(doc)
                    .OfClass(typeof(Family))
                    .Cast<Family>()
                    .Where(f => !string.IsNullOrEmpty(f.Name)
                             && f.Name.IndexOf("STING", StringComparison.OrdinalIgnoreCase) >= 0
                             && f.FamilyCategory?.CategoryType == CategoryType.Annotation)
                    .OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                if (families.Count == 0)
                {
                    TaskDialog.Show("Tag Category Dry Run", "No loaded STING tag families found.");
                    return Result.Cancelled;
                }

                var matches = new List<TagCategoryResolution>();
                var mismatches = new List<TagCategoryResolution>();
                var undeclared = new List<TagCategoryResolution>();
                var unresolvable = new List<TagCategoryResolution>();

                foreach (Family fam in families)
                {
                    var res = TagCategoryResolver.Resolve(doc, fam);
                    if (res.DeclaredHostCategory == null) undeclared.Add(res);
                    else if (res.DeclaredTagCategory == null) unresolvable.Add(res);
                    else if (res.IsMismatch) mismatches.Add(res);
                    else matches.Add(res);
                }

                var sb = new StringBuilder();
                sb.AppendLine("Declared vs actual tag category — read-only, nothing changed");
                sb.AppendLine(new string('=', 68));
                sb.AppendLine($"  Loaded STING tag families:      {families.Count:N0}");
                sb.AppendLine($"  Declared category, ALREADY MATCHES: {matches.Count:N0}");
                sb.AppendLine($"  Declared category, MISMATCH:        {mismatches.Count:N0}  (propagation would recategorise)");
                sb.AppendLine($"  Declared but unresolvable here:     {unresolvable.Count:N0}");
                sb.AppendLine($"  No category declared in the CSVs:    {undeclared.Count:N0}  (propagation preserves current)");
                sb.AppendLine($"  Declared-category map size:         {TagCategoryResolver.DeclaredCount:N0} families");
                sb.AppendLine();

                if (matches.Count > 0)
                {
                    sb.AppendLine("SAFE SMOKE-TEST TARGETS — declared category already matches, so");
                    sb.AppendLine("propagating to one of these changes no category. If the load still");
                    sb.AppendLine("fails on these, the category-change hypothesis is wrong and the");
                    sb.AppendLine("suspect moves to the SaveAs / temp-name path.");
                    foreach (var r in matches.Take(15))
                        sb.AppendLine($"    {r.FamilyName}  [{r.ActualCategory}]");
                    if (matches.Count > 15) sb.AppendLine($"    …(+{matches.Count - 15} more)");
                    sb.AppendLine();
                }

                if (mismatches.Count > 0)
                {
                    sb.AppendLine("WOULD RECATEGORISE:");
                    foreach (var r in mismatches.Take(30))
                        sb.AppendLine($"    {r.FamilyName}:  {r.ActualCategory}  →  {r.DeclaredTagCategory.Name}   (declared '{r.DeclaredHostCategory}')");
                    if (mismatches.Count > 30) sb.AppendLine($"    …(+{mismatches.Count - 30} more — see the CSV export)");
                    sb.AppendLine();
                }

                if (unresolvable.Count > 0)
                {
                    sb.AppendLine("DECLARED BUT UNRESOLVABLE (propagation falls back to current category):");
                    foreach (var r in unresolvable.Take(15))
                        sb.AppendLine($"    {r.FamilyName}: {r.Note}");
                    if (unresolvable.Count > 15) sb.AppendLine($"    …(+{unresolvable.Count - 15} more)");
                    sb.AppendLine();
                }

                string csvPath = WriteCsv(doc, matches, mismatches, unresolvable, undeclared);
                if (csvPath != null) sb.AppendLine($"Full listing: {csvPath}");

                StingLog.Info($"TagCategoryDryRun: loaded={families.Count}, match={matches.Count}, "
                            + $"mismatch={mismatches.Count}, unresolvable={unresolvable.Count}, undeclared={undeclared.Count}");

                var td = new TaskDialog("Tag Category Dry Run")
                {
                    MainInstruction = $"{mismatches.Count:N0} of {families.Count:N0} loaded tag families would be recategorised",
                    MainContent = sb.ToString(),
                };
                td.Show();
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("TagCategoryDryRunCommand crashed", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }

        private static string WriteCsv(Document doc,
            List<TagCategoryResolution> matches, List<TagCategoryResolution> mismatches,
            List<TagCategoryResolution> unresolvable, List<TagCategoryResolution> undeclared)
        {
            try
            {
                string path = StingPaths.ExportFile(doc, "CSV", "STING_TagCategoryDryRun", ".csv");
                var lines = new List<string> { "Verdict,FamilyName,ActualCategory,DeclaredHostCategory,ResolvedTagCategory,Note" };
                void Add(string verdict, IEnumerable<TagCategoryResolution> rows)
                {
                    foreach (var r in rows)
                        lines.Add(string.Join(",",
                            verdict,
                            Q(r.FamilyName), Q(r.ActualCategory), Q(r.DeclaredHostCategory),
                            Q(r.DeclaredTagCategory?.Name), Q(r.Note)));
                }
                Add("MATCH", matches);
                Add("MISMATCH", mismatches);
                Add("UNRESOLVABLE", unresolvable);
                Add("UNDECLARED", undeclared);
                File.WriteAllLines(path, lines);
                return path;
            }
            catch (Exception ex)
            {
                StingLog.Warn($"TagCategoryDryRun: CSV export failed: {ex.Message}");
                return null;
            }
        }

        private static string Q(string s)
            => "\"" + (s ?? "").Replace("\"", "\"\"") + "\"";
    }
}
