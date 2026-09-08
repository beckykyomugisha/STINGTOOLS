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

        /// <summary>
        /// Corporate baseline + project override. A MISSING baseline yields
        /// <see cref="ProductExclusion.None"/> and says so in the log — excluding
        /// nothing is the safe failure, because a policy that silently removes rows
        /// nobody asked it to remove is worse than no policy.
        /// </summary>
        private static ProductExclusion LoadExclusionPolicy(Document doc)
        {
            ProdExclusionDocument corp = null, proj = null;
            try
            {
                string p = StingToolsApp.FindDataFile("STING_PROD_EXCLUSIONS.json");
                if (!string.IsNullOrEmpty(p) && File.Exists(p))
                {
                    corp = ProdExclusionPolicy.Parse(File.ReadAllText(p), out string err);
                    if (corp == null) StingLog.Warn($"Prod exclusions: baseline unreadable ({err}) — excluding nothing.");
                }
                else StingLog.Warn("Prod exclusions: STING_PROD_EXCLUSIONS.json not deployed — excluding nothing.");
            }
            catch (Exception ex) { StingLog.Warn($"Prod exclusions baseline: {ex.Message}"); }

            try
            {
                string p = StingPaths.MetaFile(doc, "_BIM_COORD", "prod_exclusions.json");
                if (!string.IsNullOrEmpty(p) && File.Exists(p))
                {
                    proj = ProdExclusionPolicy.Parse(File.ReadAllText(p), out string err);
                    if (proj == null) StingLog.Warn($"Prod exclusions: project override unreadable ({err}) — ignored.");
                    else StingLog.Info("Prod exclusions: project override applied from " + p);
                }
            }
            catch (Exception ex) { StingLog.Warn($"Prod exclusions override: {ex.Message}"); }

            if (corp == null && proj == null) return ProductExclusion.None;
            return ProdExclusionPolicy.Build(corp, proj);
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

                // The denominator has to be things that can actually HAVE a product
                // code. The first run on a real model reported 0.8% (13/1688) — but
                // 1,187 of those were wall VOIDS (M_GM_OpeningWall_Instance), plus
                // filled regions, rooms and muntin patterns. None can ever carry a
                // PROD code, and counting them as failures pointed the reader at the
                // wrong problem: over real products the figure was 3.1%.
                var exclusion = LoadExclusionPolicy(doc);
                var excludedByCat = new SortedDictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                int excluded = 0, skippedByTagger = 0;

                var rolls = new SortedDictionary<string, CatRoll>(StringComparer.OrdinalIgnoreCase);
                var rows = new List<string> { "Category,Family,Type,PROD,Source,Specific" };
                int scanned = 0, specific = 0, specificEqualsDefault = 0;
                int genericLoadable = 0, genericSystem = 0;

                foreach (Element el in collector)
                {
                    string cat = ParameterHelpers.GetCategoryName(el);
                    if (string.IsNullOrEmpty(cat) || !known.Contains(cat)) continue;

                    // Count what the TAGGER counts. TagPipelineHelper skips these two
                    // before it does anything else, so an audit that reports on them
                    // is measuring a population the plugin never tags — the two
                    // definitions had silently diverged.
                    if (TagConfig.CategorySkipList.Contains(cat)) { skippedByTagger++; continue; }
                    if (TagConfig.CategoryTokenOverrides.TryGetValue(cat, out var ovr)
                        && ovr.TryGetValue("SKIP", out string skipVal)
                        && "true".Equals(skipVal, StringComparison.OrdinalIgnoreCase))
                    { skippedByTagger++; continue; }

                    if (exclusion.Classify(cat, ParameterHelpers.GetFamilyName(el),
                                           ParameterHelpers.GetElementTypeName(el)) != ExclusionVerdict.Included)
                    {
                        excluded++;
                        excludedByCat.TryGetValue(cat, out int ne);
                        excludedByCat[cat] = ne + 1;
                        continue;
                    }

                    scanned++;
                    string prod, source;
                    try { prod = TagConfig.GetFamilyAwareProdCode(el, cat, out source); }
                    catch (Exception ex) { StingLog.Warn($"ProdCoverageAudit resolve {el.Id}: {ex.Message}"); prod = "GEN"; source = "gen"; }

                    bool isSpecific = ProdResolver.IsSpecific(source);
                    if (isSpecific) specific++;
                    if ((source == ProdResolver.Sources.Project || source == ProdResolver.Sources.Corporate) &&
                        string.Equals(prod, TagConfig.CategoryProdDefault(cat), StringComparison.OrdinalIgnoreCase))
                        specificEqualsDefault++;

                    if (!rolls.TryGetValue(cat, out var r)) rolls[cat] = r = new CatRoll();
                    r.Total++;
                    if (isSpecific) r.Specific++;
                    r.BySource[source] = r.BySource.TryGetValue(source, out int n) ? n + 1 : 1;

                    string fam = ParameterHelpers.GetFamilyName(el) ?? "";
                    // GetElementTypeName, NOT GetFamilySymbolName. The latter answers ""
                    // for anything that is not a FamilyInstance, so every wall, roof and
                    // floor reported a BLANK type — while the resolver this is REPORTING
                    // ON matched against "Generic - 200mm" all along (KUT-11 moved it to
                    // GetElementTypeName; the audit was never moved with it).
                    //
                    // It is the field that matters most here. Prod_GenerateRules skips
                    // system families on purpose — a rule keyed on "Basic Wall" matches
                    // every wall in the model — so the ONLY route to a specific PROD code
                    // for a wall is a hand-written rule keyed on its TYPE name, and the
                    // audit was hiding exactly that.
                    string typ = ParameterHelpers.GetElementTypeName(el) ?? "";
                    if (!isSpecific && !string.IsNullOrEmpty(fam)) r.GenericFamilies.Add(fam);

                    // Split the gap by what can actually CLOSE it. Prod_GenerateRules
                    // seeds only LOADABLE families; a system element (wall, roof, floor)
                    // needs a hand-written rule keyed on its type name, because a rule
                    // keyed on "Basic Wall" would match every wall in the model. Telling
                    // a reader to run the seeder for those sends them somewhere that will
                    // silently skip their 98 elements.
                    if (!isSpecific)
                    {
                        if (string.IsNullOrEmpty(ParameterHelpers.GetLoadableFamilyName(el))) genericSystem++;
                        else genericLoadable++;
                    }

                    rows.Add(string.Join(",", Csv(cat), Csv(fam), Csv(typ), Csv(prod), Csv(source), isSpecific ? "Y" : "N"));
                }

                if (scanned == 0)
                {
                    // An empty scope is not a 100% pass and not a silent zero. Say
                    // which of the two zeros this is, or the reader cannot tell an
                    // exclusion policy that ate the model from a model with nothing
                    // in it.
                    TaskDialog.Show("PROD Coverage Audit",
                        excluded + skippedByTagger == 0
                            ? "No taggable elements found."
                            : $"No PRODUCTS to report on. {excluded} element(s) were excluded as not-a-product "
                            + $"and {skippedByTagger} skipped by the tagger's own category rules, leaving nothing "
                            + "to measure. If that is wrong, check STING_PROD_EXCLUSIONS.json.");
                    return Result.Succeeded;
                }

                // CSV report
                string path = null;
                try
                {
                    string projPath = doc.PathName;
                    if (!string.IsNullOrEmpty(projPath))
                    {
                        string dir = StingPaths.Meta(doc, "_BIM_COORD");
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
                sb.AppendLine($"PROD coverage: {specific}/{scanned} PRODUCTS specific ({pct}%).");
                sb.AppendLine("Specific = project/corporate CSV, LPS or sleeve rule. Generic = category default (no rule matched).");
                sb.AppendLine();

                // Publish the denominator. Silently shrinking it would be the same
                // move as the fabricated fallbacks this codebase keeps finding: the
                // number gets better and nobody can see why.
                if (excluded > 0 || skippedByTagger > 0)
                {
                    sb.AppendLine($"Denominator: {scanned + excluded + skippedByTagger} taggable element(s) seen, "
                                + $"{excluded} excluded as not-a-product, {skippedByTagger} skipped by the tagger's "
                                + $"own category rules, {scanned} measured.");
                    if (excluded > 0)
                        sb.AppendLine("  not-a-product: " + string.Join(", ",
                            excludedByCat.OrderByDescending(k => k.Value).Take(6).Select(k => $"{k.Key} {k.Value}")));
                    sb.AppendLine("  A wall opening, a filled region or a room can never carry a PROD code, so counting");
                    sb.AppendLine("  them as failures understates coverage. Policy: Data/STING_PROD_EXCLUSIONS.json.");
                    sb.AppendLine();
                }
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
                sb.AppendLine($"Of the {scanned - specific} generic element(s): {genericLoadable} are LOADABLE families and");
                sb.AppendLine("  Prod_GenerateRules will seed a rule for each; " + genericSystem + " are SYSTEM elements");
                sb.AppendLine("  (walls, roofs, floors) which it deliberately SKIPS — a rule keyed on \"Basic Wall\"");
                sb.AppendLine("  would match every wall in the model. Those need a rule keyed on the TYPE name");
                sb.AppendLine("  instead; the Type column of the CSV below is that name.");
                sb.AppendLine();
                sb.AppendLine("Fix: run Prod_GenerateRules for the loadable ones, hand-write FAMILY_PATTERN rows");
                sb.AppendLine("for the system ones (the pattern matches FAMILY + TYPE), then re-run Tag & Combine");
                sb.AppendLine("(Skip mode) to fill the now-specific PROD codes.");
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
                StingLog.Info($"Prod_CoverageAudit: {specific}/{scanned} specific ({pct}%); "
                            + $"{excluded} not-a-product, {skippedByTagger} tagger-skipped -> {path}");
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
