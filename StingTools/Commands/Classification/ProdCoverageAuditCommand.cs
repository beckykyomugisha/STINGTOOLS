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
    /// Prod_CoverageAudit — read-only report of how PROD resolves across the
    /// model: per taggable element it records the resolved PROD code and the
    /// SOURCE tier that produced it (project CSV / corporate CSV / LPS / sleeve
    /// / category-default / GEN), then rolls up per category.
    ///
    /// "Specific" = project | corporate | lps | sleeve (a real family-aware code).
    /// "Generic"  = category | gen (the catch-all fallback — i.e. no rule matched).
    ///
    /// This is the tool for "why isn't this product code specific?" — it shows
    /// exactly which categories / families fall through to the generic default,
    /// so a project can add the missing rows to prod_codes.csv. Writes a per-
    /// element CSV to &lt;project&gt;/_BIM_COORD/ and a summary TaskDialog.
    /// </summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class ProdCoverageAuditCommand : IExternalCommand
    {
        private sealed class CatRoll
        {
            public int Total;
            public int Specific;
            public readonly Dictionary<string, int> BySource = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            public readonly HashSet<string> GenericFamilies = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                var ctx = ParameterHelpers.GetContext(commandData);
                if (ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }
                Document doc = ctx.Doc;

                TagConfig.ReloadProdRules(); // reflect any on-disk prod_codes.csv edits this session

                var known = new HashSet<string>(TagConfig.DiscMap.Keys, StringComparer.OrdinalIgnoreCase);
                var collector = new FilteredElementCollector(doc).WhereElementIsNotElementType();
                var catEnums = SharedParamGuids.AllCategoryEnums;
                if (catEnums != null && catEnums.Length > 0)
                    collector.WherePasses(new ElementMulticategoryFilter(new List<BuiltInCategory>(catEnums)));

                var rolls = new SortedDictionary<string, CatRoll>(StringComparer.OrdinalIgnoreCase);
                var rows = new List<string> { "Category,Family,Type,PROD,Source,Specific" };
                int scanned = 0, specific = 0, specificEqualsDefault = 0;

                foreach (Element el in collector)
                {
                    string cat = ParameterHelpers.GetCategoryName(el);
                    if (string.IsNullOrEmpty(cat) || !known.Contains(cat)) continue;

                    scanned++;
                    string prod, source;
                    try { prod = TagConfig.GetFamilyAwareProdCode(el, cat, out source); }
                    catch (Exception ex) { StingLog.Warn($"ProdCoverageAudit resolve {el.Id}: {ex.Message}"); prod = "GEN"; source = "gen"; }

                    bool isSpecific = source == "project" || source == "corporate" || source == "lps" || source == "sleeve";
                    if (isSpecific) specific++;
                    if ((source == "project" || source == "corporate") &&
                        string.Equals(prod, TagConfig.CategoryProdDefault(cat), StringComparison.OrdinalIgnoreCase))
                        specificEqualsDefault++;

                    if (!rolls.TryGetValue(cat, out var r)) rolls[cat] = r = new CatRoll();
                    r.Total++;
                    if (isSpecific) r.Specific++;
                    r.BySource[source] = r.BySource.TryGetValue(source, out int n) ? n + 1 : 1;

                    string fam = ParameterHelpers.GetFamilyName(el) ?? "";
                    string typ = ParameterHelpers.GetFamilySymbolName(el) ?? "";
                    if (!isSpecific && !string.IsNullOrEmpty(fam)) r.GenericFamilies.Add(fam);

                    rows.Add(string.Join(",", Csv(cat), Csv(fam), Csv(typ), Csv(prod), Csv(source), isSpecific ? "Y" : "N"));
                }

                if (scanned == 0)
                {
                    TaskDialog.Show("PROD Coverage Audit", "No taggable elements found.");
                    return Result.Succeeded;
                }

                // CSV report
                string path = null;
                try
                {
                    string projPath = doc.PathName;
                    if (!string.IsNullOrEmpty(projPath))
                    {
                        string dir = Path.Combine(Path.GetDirectoryName(projPath), "_BIM_COORD");
                        Directory.CreateDirectory(dir);
                        path = Path.Combine(dir, $"prod_coverage_{DateTime.Now:yyyyMMdd_HHmmss}.csv");
                    }
                    else
                    {
                        path = OutputLocationHelper.GetOutputPath(doc, $"prod_coverage_{DateTime.Now:yyyyMMdd_HHmmss}.csv");
                    }
                    File.WriteAllLines(path, rows, Encoding.UTF8);
                }
                catch (Exception ex) { StingLog.Warn($"ProdCoverageAudit CSV: {ex.Message}"); }

                // Summary: worst-covered categories first (most generic)
                var sb = new StringBuilder();
                int pct = (int)Math.Round(100.0 * specific / scanned);
                sb.AppendLine($"PROD coverage: {specific}/{scanned} elements specific ({pct}%).");
                sb.AppendLine("Specific = project/corporate CSV, LPS or sleeve rule. Generic = category default (no rule matched).");
                sb.AppendLine();
                sb.AppendLine("Categories with the most GENERIC (unmatched) elements:");
                foreach (var kv in rolls.OrderByDescending(k => k.Value.Total - k.Value.Specific).Take(15))
                {
                    var r = kv.Value;
                    int gen = r.Total - r.Specific;
                    if (gen == 0) continue;
                    string fams = string.Join(", ", r.GenericFamilies.OrderBy(s => s).Take(4));
                    if (r.GenericFamilies.Count > 4) fams += $", +{r.GenericFamilies.Count - 4} more";
                    sb.AppendLine($"  {kv.Key}: {gen}/{r.Total} generic  → add prod_codes.csv rows for: {fams}");
                }
                sb.AppendLine();
                sb.AppendLine("Fix: run Prod_GenerateRules, then add/curate FAMILY_PATTERN rows for the families above,");
                sb.AppendLine("and re-run Tag & Combine (Skip mode) to fill the now-specific PROD codes.");
                sb.AppendLine();
                sb.AppendLine("Note: \"specific %\" counts every project/corporate/LPS/sleeve match (a generous");
                sb.AppendLine($"measure) and does not credit material-suffix differentiation; of those, {specificEqualsDefault}");
                sb.AppendLine("resolve to a code equal to the category default (specific rule, generic-looking code).");
                if (path != null) { sb.AppendLine(); sb.AppendLine("Per-element CSV: " + path); }

                new TaskDialog("PROD Coverage Audit")
                {
                    MainInstruction = $"{pct}% of PROD codes are family-specific",
                    MainContent = sb.ToString()
                }.Show();
                StingLog.Info($"Prod_CoverageAudit: {specific}/{scanned} specific ({pct}%) -> {path}");
                return Result.Succeeded;
            }
            catch (OperationCanceledException) { return Result.Cancelled; }
            catch (Exception ex)
            {
                StingLog.Error("ProdCoverageAuditCommand crashed", ex);
                try { TaskDialog.Show("STING Tools", $"PROD Coverage Audit failed:\n{ex.Message}"); } catch { }
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
