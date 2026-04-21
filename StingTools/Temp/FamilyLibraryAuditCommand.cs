// Phase 108m — Family library audit. Scans project against
// FAMILY_LIBRARY_MANIFEST.json; reports missing / misnamed families.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using StingTools.Core;
using StingTools.UI;

namespace StingTools.Temp
{
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class FamilyLibraryAuditCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                var ctx = ParameterHelpers.GetContext(commandData);
                if (ctx?.Doc == null) return Result.Failed;
                var doc = ctx.Doc;

                string path = Path.Combine(StingToolsApp.DataPath ?? "", "FAMILY_LIBRARY_MANIFEST.json");
                if (!File.Exists(path))
                {
                    TaskDialog.Show("Family Library Audit", "FAMILY_LIBRARY_MANIFEST.json not found in data folder.");
                    return Result.Failed;
                }
                var manifest = JObject.Parse(File.ReadAllText(path));
                var expected = new List<(string code, string cat, string desc, string group)>();
                foreach (var section in new[] { "architectural", "structural", "mep" })
                {
                    var arr = manifest[section] as JArray;
                    if (arr == null) continue;
                    foreach (var f in arr.OfType<JObject>())
                        expected.Add((f["code"]?.ToString() ?? "", f["category"]?.ToString() ?? "", f["description"]?.ToString() ?? "", section));
                }

                // Collect project family symbols
                var projectFamilies = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var fs in new FilteredElementCollector(doc).OfClass(typeof(FamilySymbol)).Cast<FamilySymbol>())
                    projectFamilies.Add(fs.FamilyName);
                foreach (var wt in new FilteredElementCollector(doc).OfClass(typeof(WallType)).Cast<WallType>())
                    projectFamilies.Add(wt.Name);
                foreach (var ft in new FilteredElementCollector(doc).OfClass(typeof(FloorType)).Cast<FloorType>())
                    projectFamilies.Add(ft.Name);

                int missing = 0, present = 0;
                var missingByGroup = new Dictionary<string, int>();
                var missingList = new List<string>();
                foreach (var e in expected)
                {
                    bool match = projectFamilies.Any(n => n.IndexOf(e.code, StringComparison.OrdinalIgnoreCase) >= 0
                                                        || n.StartsWith(e.code, StringComparison.OrdinalIgnoreCase));
                    if (match) present++;
                    else
                    {
                        missing++;
                        if (!missingByGroup.ContainsKey(e.group)) missingByGroup[e.group] = 0;
                        missingByGroup[e.group]++;
                        if (missingList.Count < 30) missingList.Add($"• [{e.group}] {e.code} — {e.desc}");
                    }
                }

                double coveragePct = expected.Count > 0 ? 100.0 * present / expected.Count : 0;
                var rp = StingResultPanel.Create("Family Library Audit")
                    .SetSubtitle($"Project coverage against the STING 110-family manifest")
                    .AddSection("COVERAGE")
                    .Metric("Expected", expected.Count.ToString())
                    .Metric("Present",  present.ToString())
                    .Metric("Missing",  missing.ToString())
                    .Metric("Coverage", $"{coveragePct:F1}%");
                foreach (var kv in missingByGroup)
                    rp.Metric($"Missing — {kv.Key}", kv.Value.ToString());
                if (missingList.Count > 0)
                {
                    rp.AddSection("MISSING FAMILIES (first 30)");
                    foreach (var m in missingList) rp.Text(m);
                }
                rp.Show();
                StingLog.Info($"Family library audit: {present}/{expected.Count} ({coveragePct:F0}%)");
                return Result.Succeeded;
            }
            catch (Exception ex) { StingLog.Error("FamilyLibraryAuditCommand", ex); message = ex.Message; return Result.Failed; }
        }
    }
}
