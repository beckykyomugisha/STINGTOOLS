// StingTools — Drawing Template Manager · Phase 168
//
// TitleBlockMigrateCsvToRecipeCommand reads TITLE_BLOCK.csv and emits
// a JSON snippet that maps every non-empty (ParameterName, Discipline)
// pair to a per-discipline DrawingType.titleBlockParams entry. The
// snippet is written to <project>/_BIM_COORD/titleblock_csv_export.json
// so an operator can paste discipline-specific blocks into the editor
// without typing them by hand. Each per-discipline block contains
// only the keys whose value differs from DefaultValue, so the
// resulting recipe is a delta rather than a duplicate of the CSV.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json;
using StingTools.Core;
using StingTools.Core.Drawing;

namespace StingTools.Commands.Drawing
{
    [Transaction(TransactionMode.ReadOnly)]
    public class TitleBlockMigrateCsvToRecipeCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string msg, ElementSet els)
        {
            try
            {
                var doc = data?.Application?.ActiveUIDocument?.Document;
                if (doc == null) { msg = "No document open."; return Result.Failed; }

                string csvPath = LocateCsv(doc, "TITLE_BLOCK.csv");
                if (csvPath == null || !File.Exists(csvPath))
                {
                    TaskDialog.Show("STING — Migrate CSV", "TITLE_BLOCK.csv not found in data/ or project _BIM_COORD/.");
                    return Result.Failed;
                }

                var rows = ReadCsv(csvPath);
                if (rows.Count == 0)
                {
                    TaskDialog.Show("STING — Migrate CSV", $"TITLE_BLOCK.csv at\n{csvPath}\nis empty or unreadable.");
                    return Result.Failed;
                }

                // Header inferred from row 0 — first 2 cols are ParameterName + DefaultValue,
                // remaining cols are discipline codes (ARCH/STR/…).
                var header = rows[0];
                if (header.Count < 3) { TaskDialog.Show("STING — Migrate CSV", "Header row missing discipline columns."); return Result.Failed; }
                var discCols = header.Skip(2).ToList();

                // For each discipline, collect (paramName → value) entries that
                // differ from DefaultValue; map each as a literal title-block
                // template (no token substitution — values move 1:1 to the recipe).
                var perDiscipline = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
                foreach (var d in discCols) perDiscipline[d] = new Dictionary<string, string>(StringComparer.Ordinal);
                var globals = new Dictionary<string, string>(StringComparer.Ordinal);

                for (int r = 1; r < rows.Count; r++)
                {
                    var row = rows[r];
                    if (row.Count == 0) continue;
                    var paramName = row.Count > 0 ? row[0]?.Trim() : null;
                    if (string.IsNullOrEmpty(paramName)) continue;
                    var defVal = row.Count > 1 ? row[1] ?? "" : "";

                    if (!string.IsNullOrEmpty(defVal)) globals[paramName] = defVal;
                    for (int c = 0; c < discCols.Count; c++)
                    {
                        int col = c + 2;
                        var v = col < row.Count ? row[col] : "";
                        if (string.IsNullOrEmpty(v)) continue;
                        if (!string.Equals(v, defVal, StringComparison.Ordinal))
                            perDiscipline[discCols[c]][paramName] = v;
                    }
                }

                // Build output payload — one base block + per-disc deltas.
                var payload = new
                {
                    schema = "sting/titleblock-csv-export/1",
                    sourceCsv = csvPath,
                    exportedUtc = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                    instructions =
                        "Paste 'baselineParams' into a corp-base DrawingType's titleBlockParams " +
                        "(or any profile that should carry the org-wide defaults). Paste each " +
                        "'perDisciplineDelta' block into the matching discipline's recipe to " +
                        "carry the CSV's per-discipline overrides. The Phase 168 editor's " +
                        "'Title block parameters' card on the Drawing Types tab is the safe " +
                        "place to paste keys; values are literal text (no ${PRJ_ORG_*} or {disc} " +
                        "tokens are introduced).",
                    baselineParams = globals,
                    perDisciplineDelta = perDiscipline
                        .Where(kv => kv.Value.Count > 0)
                        .ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase),
                };

                string outDir = ResolveOutputDir(doc);
                Directory.CreateDirectory(outDir);
                string outPath = Path.Combine(outDir, "titleblock_csv_export.json");
                File.WriteAllText(outPath,
                    JsonConvert.SerializeObject(payload, Formatting.Indented));

                int discCount = ((Dictionary<string,Dictionary<string,string>>)payload.perDisciplineDelta).Count;
                TaskDialog.Show("STING — Migrate CSV",
                    $"Exported {globals.Count} baseline params and {discCount} per-discipline delta block(s) to:\n{outPath}\n\n" +
                    "Open the editor's Title block parameters card and paste the relevant block.");
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("TitleBlockMigrateCsvToRecipe", ex);
                msg = ex.Message;
                return Result.Failed;
            }
        }

        private static string LocateCsv(Document doc, string fileName)
        {
            try
            {
                var dataPath = StingToolsApp.FindDataFile(fileName);
                if (!string.IsNullOrEmpty(dataPath) && File.Exists(dataPath)) return dataPath;
            }
            catch { }
            try
            {
                if (!string.IsNullOrEmpty(doc?.PathName))
                {
                    var alongside = Path.Combine(Path.GetDirectoryName(doc.PathName) ?? "",
                        "_BIM_COORD", fileName);
                    if (File.Exists(alongside)) return alongside;
                }
            }
            catch { }
            return null;
        }

        private static string ResolveOutputDir(Document doc)
        {
            try
            {
                if (!string.IsNullOrEmpty(doc?.PathName))
                    return Path.Combine(Path.GetDirectoryName(doc.PathName) ?? "", "_BIM_COORD");
            }
            catch { }
            return Path.Combine(Path.GetTempPath(), "STING");
        }

        private static List<List<string>> ReadCsv(string path)
        {
            var rows = new List<List<string>>();
            foreach (var line in File.ReadAllLines(path))
                rows.Add(StingToolsApp.ParseCsvLine(line));
            return rows;
        }
    }
}
