// StingTools — Content Library coverage diagnostic
//
// Content_Coverage is a read-only command that prints the content ledger: how
// many tag families / model-family seeds / catalogues are declared in the
// manifest, how many are built vs still a gap, and the full list of open gaps
// (needs-build tag families + needs-spec model categories) so the team can burn
// them down. Writes a CSV alongside the project for tracking.

using System;
using System.IO;
using System.Linq;
using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Core;
using StingTools.Core.Content;

namespace StingTools.Commands.Content
{
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class ContentCoverageCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var doc = commandData?.Application?.ActiveUIDocument?.Document;
            if (doc == null) { message = "No active document."; return Result.Failed; }

            try
            {
                var m = ContentManifestRegistry.Get(doc);

                int tagBuilt = m.TagFamilies.Count(e => Eq(e.Status, "built"));
                int tagNeed  = m.TagFamilies.Count(e => Eq(e.Status, "needs-build"));
                int seedSpec = m.Symbols.Count(e => Eq(e.Status, "spec"));
                int seedNeed = m.Symbols.Count(e => Eq(e.Status, "needs-spec"));

                var gaps = ContentManifestRegistry.ListMissingSpecs(doc)
                    .OrderBy(e => e.Kind).ThenBy(e => e.Category).ToList();

                var sb = new StringBuilder();
                sb.AppendLine($"STING Content Library  v{m.LibraryVersion}   (rootPrecedence: {m.RootPrecedence})");
                sb.AppendLine();
                sb.AppendLine($"Tag families : {m.TagFamilies.Count,4}   {tagBuilt} built, {tagNeed} needs-build");
                sb.AppendLine($"Model seeds  : {m.Symbols.Count,4}   {seedSpec} buildable (spec), {seedNeed} needs-spec");
                sb.AppendLine($"Catalogues   : {m.SymbolCatalogues.Count,4}");
                sb.AppendLine($"Bundles      : {m.Bundles.Count,4}");
                sb.AppendLine();

                // The REAL seed denominator — engine-required loadable categories,
                // not the 206 tag list.
                if (m.Coverage != null)
                {
                    var cov = m.Coverage;
                    int seedable = cov.SeededLoadableCount + cov.NeedsSpecCount; // exclude system/datum
                    int pct = seedable > 0
                        ? (int)System.Math.Round(100.0 * cov.SeededLoadableCount / seedable) : 100;
                    sb.AppendLine($"SEED COVERAGE (engine-required denominator, NOT the 206 tag list):");
                    sb.AppendLine($"  {cov.SeededLoadableCount}/{seedable} seedable categories covered ({pct}%), {cov.NeedsSpecCount} needs-spec");
                    sb.AppendLine($"  ({cov.EngineRequiredCount} engine-required total; {cov.ExcludedSystemDatum.Count} system/datum excluded by design)");
                    if (cov.NeedsSpec != null && cov.NeedsSpec.Count > 0)
                        sb.AppendLine($"  needs-spec: {string.Join(", ", cov.NeedsSpec)}");
                    sb.AppendLine();
                }

                if (gaps.Count == 0)
                {
                    sb.AppendLine("No open gaps - every declared content item is built or buildable.");
                }
                else
                {
                    sb.AppendLine($"OPEN GAPS ({gaps.Count}) - burn-down list:");
                    foreach (var g in gaps.Take(40))
                    {
                        var note = string.IsNullOrEmpty(g.Notes) ? "" : "  - " + g.Notes;
                        sb.AppendLine($"  [{g.Status}] {g.Kind}: {g.Category}{note}");
                    }
                    if (gaps.Count > 40) sb.AppendLine($"  ... and {gaps.Count - 40} more (see CSV).");
                }

                var csvPath = WriteCsv(doc, m, gaps);
                if (!string.IsNullOrEmpty(csvPath))
                {
                    sb.AppendLine();
                    sb.AppendLine($"Full ledger CSV: {csvPath}");
                }

                TaskDialog.Show("STING Content Coverage", sb.ToString());
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("ContentCoverageCommand", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }

        private static bool Eq(string a, string b)
            => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

        private static string WriteCsv(Document doc, ContentManifest m, System.Collections.Generic.List<ContentEntry> gaps)
        {
            try
            {
                var dir = string.IsNullOrEmpty(doc.PathName) ? null : Path.GetDirectoryName(doc.PathName);
                if (string.IsNullOrEmpty(dir)) return null;
                var outDir = Path.Combine(dir, "_BIM_COORD");
                Directory.CreateDirectory(outDir);
                var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                var path = Path.Combine(outDir, $"content_coverage_{stamp}.csv");

                var sb = new StringBuilder();
                sb.AppendLine("kind,id,category,discipline,status,familyFile,buildSpec,notes");
                foreach (var e in m.AllEntries.OrderBy(x => x.Kind).ThenBy(x => x.Status).ThenBy(x => x.Category))
                    sb.AppendLine(string.Join(",",
                        Csv(e.Kind), Csv(e.Id), Csv(e.Category), Csv(e.Discipline),
                        Csv(e.Status), Csv(e.FamilyFile), Csv(e.BuildSpec), Csv(e.Notes)));
                File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
                return path;
            }
            catch (Exception ex) { StingLog.Warn($"ContentCoverage CSV: {ex.Message}"); return null; }
        }

        private static string Csv(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            if (s.IndexOf(',') >= 0 || s.IndexOf('"') >= 0 || s.IndexOf('\n') >= 0)
                return "\"" + s.Replace("\"", "\"\"") + "\"";
            return s;
        }
    }
}
