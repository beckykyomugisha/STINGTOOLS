using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Core;

namespace StingTools.Commands.Classification
{
    /// <summary>
    /// Prod_GenerateRules — derive a project-scoped PROD-code rule set LIVE from
    /// the model's own categories + families, and write it to
    /// <c>&lt;project&gt;/_BIM_COORD/prod_codes.csv</c> (the project overlay that
    /// <c>TagConfig.GetProjectProdRules</c> layers over the corporate
    /// STING_PROD_CODES.csv).
    ///
    /// For every taggable category it collects the distinct family names present,
    /// proposes a PROD code via the existing <c>TagConfig.GetFamilyAwareProdCode</c>
    /// resolver (so the seed reflects current intelligence), and emits one
    /// FAMILY_PATTERN row per family. The author then curates the rows.
    ///
    /// NON-DESTRUCTIVE: read-only on the model. If a prod_codes.csv already
    /// exists it is NOT overwritten — the suggestion lands in
    /// prod_codes_suggested_&lt;timestamp&gt;.csv for the author to merge.
    /// </summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class GenerateProdCodeRulesCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                var ctx = ParameterHelpers.GetContext(commandData);
                if (ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }
                Document doc = ctx.Doc;

                string projPath = doc.PathName;
                if (string.IsNullOrEmpty(projPath))
                {
                    TaskDialog.Show("Generate PROD Rules",
                        "Save the project first — the overlay is written to <project>/_BIM_COORD/prod_codes.csv.");
                    return Result.Cancelled;
                }

                var known = new HashSet<string>(TagConfig.DiscMap.Keys, StringComparer.OrdinalIgnoreCase);

                var collector = new FilteredElementCollector(doc).WhereElementIsNotElementType();
                var catEnums = SharedParamGuids.AllCategoryEnums;
                if (catEnums != null && catEnums.Length > 0)
                    collector.WherePasses(new ElementMulticategoryFilter(new List<BuiltInCategory>(catEnums)));

                // One row per distinct (Category, FamilyName); first element wins as the sample.
                const string SEP = "";
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var rows = new List<(string cat, string fam, string prod, string disc, string sample)>();
                int scanned = 0;

                foreach (Element el in collector)
                {
                    string cat = ParameterHelpers.GetCategoryName(el);
                    if (string.IsNullOrEmpty(cat) || !known.Contains(cat)) continue;

                    string fam = ParameterHelpers.GetFamilyName(el);
                    if (string.IsNullOrEmpty(fam)) continue;

                    scanned++;
                    if (!seen.Add(cat + SEP + fam)) continue; // first representative per (cat, family)

                    string prod;
                    try { prod = TagConfig.GetFamilyAwareProdCode(el, cat); }
                    catch (Exception ex) { StingLog.Warn($"GenerateProdRules: PROD resolve failed for {el.Id}: {ex.Message}"); prod = "GEN"; }
                    if (string.IsNullOrEmpty(prod)) prod = "GEN";

                    string disc = TagConfig.DiscMap.TryGetValue(cat, out var d) ? d : "";
                    rows.Add((cat, fam, prod, disc, ParameterHelpers.GetFamilySymbolName(el) ?? ""));
                }

                if (rows.Count == 0)
                {
                    TaskDialog.Show("Generate PROD Rules",
                        "No taggable families found in the model. Place some elements (or check the category map) and re-run.");
                    return Result.Succeeded;
                }

                // _BIM_COORD dir + non-destructive target selection
                string bimCoord = Path.Combine(Path.GetDirectoryName(projPath), "_BIM_COORD");
                Directory.CreateDirectory(bimCoord);
                string target = Path.Combine(bimCoord, "prod_codes.csv");
                bool wroteSuggested = false;
                if (File.Exists(target))
                {
                    target = Path.Combine(bimCoord, $"prod_codes_suggested_{DateTime.Now:yyyyMMdd_HHmmss}.csv");
                    wroteSuggested = true;
                }

                var sb = new StringBuilder();
                sb.AppendLine("PROD_CODE,CATEGORY,FAMILY_PATTERN,DESCRIPTION,DISCIPLINE,SYSTEM,STANDARD_REF");
                sb.AppendLine("# Generated live from the model by Prod_GenerateRules. Curate freely.");
                sb.AppendLine("# FAMILY_PATTERN supports bare substrings, *globs* and a|b alternation (matched against FAMILY + TYPE name, case-insensitive).");
                sb.AppendLine("# Project rows WIN over the corporate STING_PROD_CODES.csv. PROD is filled fill-empty-only by Tag & Combine (non-destructive).");
                foreach (var r in rows.OrderBy(x => x.cat, StringComparer.OrdinalIgnoreCase)
                                      .ThenBy(x => x.fam, StringComparer.OrdinalIgnoreCase))
                {
                    string desc = string.IsNullOrEmpty(r.sample) ? r.fam : $"{r.fam} / {r.sample}";
                    sb.Append(Csv(r.prod)).Append(',')
                      .Append(Csv(r.cat)).Append(',')
                      .Append(Csv(r.fam)).Append(',')   // exact family name → substring match
                      .Append(Csv(desc)).Append(',')
                      .Append(Csv(r.disc)).Append(',')
                      .Append(',')                       // SYSTEM (author fills)
                      .AppendLine();                     // STANDARD_REF (author fills) — trailing empty
                }

                File.WriteAllText(target, sb.ToString(), Encoding.UTF8);
                StingLog.Info($"Prod_GenerateRules: {rows.Count} rules from {scanned} elements -> {target}");

                var msg = new StringBuilder();
                msg.AppendLine($"Derived {rows.Count} PROD rule(s) from {scanned} taggable element(s).");
                msg.AppendLine();
                if (wroteSuggested)
                {
                    msg.AppendLine("An existing prod_codes.csv was preserved (non-destructive).");
                    msg.AppendLine("The suggestion was written as a separate file for you to review/merge:");
                }
                else
                {
                    msg.AppendLine("Written to the project overlay (wins over the corporate baseline):");
                }
                msg.AppendLine(target);
                msg.AppendLine();
                msg.AppendLine("Next: curate the PROD_CODE / FAMILY_PATTERN columns, then run");
                msg.AppendLine("Tag & Combine (Skip mode) — empty PROD values fill from these rules, authored values are preserved.");

                new TaskDialog("Generate PROD Rules")
                {
                    MainInstruction = $"{rows.Count} PROD rules derived",
                    MainContent = msg.ToString()
                }.Show();
                return Result.Succeeded;
            }
            catch (OperationCanceledException) { return Result.Cancelled; }
            catch (Exception ex)
            {
                StingLog.Error("GenerateProdCodeRulesCommand crashed", ex);
                try { TaskDialog.Show("STING Tools", $"Generate PROD Rules failed:\n{ex.Message}"); } catch { }
                return Result.Failed;
            }
        }

        private static string Csv(string s)
        {
            s ??= "";
            return (s.Contains(',') || s.Contains('"') || s.Contains('\n'))
                ? "\"" + s.Replace("\"", "\"\"") + "\""
                : s;
        }
    }
}
