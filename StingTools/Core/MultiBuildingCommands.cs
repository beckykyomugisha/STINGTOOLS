// Phase 108m — Multi-building site support (items B1-B6).
// One file holds the B1 LOC override helper, the B2 level/grid seeder,
// the B5 PRJ_VOLUME_CODE auto-populator and the B6 federation reviewer.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using StingTools.UI;

namespace StingTools.Core
{
    // ══════════════════════════════════════════════════════════════════════
    //  B1 — LOC vocabulary override
    //  ParamRegistry.LocCodes ships with BLD1..3/EXT/XX. This reads any
    //  extra codes defined in project_config.json under LOC_CODES_EXTRA
    //  and merges them into the validator's accepted set.
    // ══════════════════════════════════════════════════════════════════════

    internal static class LocVocabularyOverride
    {
        public static List<string> GetAllLocCodes()
        {
            var baseCodes = new List<string> { "BLD1", "BLD2", "BLD3", "EXT", "XX" };
            try
            {
                string raw = TagConfig.GetConfigValue("LOC_CODES_EXTRA");
                if (!string.IsNullOrWhiteSpace(raw))
                {
                    var tok = JToken.Parse(raw);
                    if (tok is JArray arr)
                        foreach (var t in arr) baseCodes.Add(t.ToString().Trim().ToUpperInvariant());
                }
            }
            catch (Exception ex) { StingLog.Warn($"LOC override parse: {ex.Message}"); }
            return baseCodes.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }
    }

    // ══════════════════════════════════════════════════════════════════════
    //  B2 — Building-code level / grid seeder
    //  Creates pre-named levels + grids prefixed with the building code.
    //  Example with BUILDING_CODE=BLD2:
    //    BLD2-B01-SSL @ -3.600
    //    BLD2-GF-SSL  @ -0.050
    //    BLD2-GF-FFL  @ +0.000
    //    BLD2-L01-SSL @ +3.950
    //    BLD2-1..BLD2-12  (numeric E-W grid)
    //    BLD2-A..BLD2-K   (letter N-S grid, skipping I/O/Q/Z)
    // ══════════════════════════════════════════════════════════════════════

    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class BuildingCodeSeedCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                var ctx = ParameterHelpers.GetContext(commandData);
                if (ctx?.Doc == null) return Result.Failed;
                var doc = ctx.Doc;

                // Prompt for building code + level count + bay spacing
                string code = StingCommandHandler.GetExtraParam("SeedBuildingCode");
                if (string.IsNullOrWhiteSpace(code)) code = "BLD1";
                code = code.Trim().ToUpperInvariant();

                string levelsStr = StingCommandHandler.GetExtraParam("SeedLevelCount") ?? "4";
                int.TryParse(levelsStr, out int levelCount);
                if (levelCount <= 0 || levelCount > 50) levelCount = 4;

                string gridsXstr = StingCommandHandler.GetExtraParam("SeedGridsX") ?? "6";
                int.TryParse(gridsXstr, out int gridsX);
                if (gridsX <= 0 || gridsX > 40) gridsX = 6;
                string gridsYstr = StingCommandHandler.GetExtraParam("SeedGridsY") ?? "4";
                int.TryParse(gridsYstr, out int gridsY);
                if (gridsY <= 0 || gridsY > 20) gridsY = 4;

                double storey = 3.5;  // metres default
                double bayMm = 6000;  // default bay spacing

                int levelsCreated = 0, gridsCreated = 0;
                using (var tx = new Transaction(doc, $"STING Seed — {code}"))
                {
                    tx.Start();
                    // Levels — FFL per storey + SSL 50 mm below FFL
                    double baseMm = 0;
                    CreateLevel(doc, $"{code}-GF-FFL",  baseMm, ref levelsCreated);
                    CreateLevel(doc, $"{code}-GF-SSL",  baseMm - 50, ref levelsCreated);
                    for (int i = 1; i <= levelCount; i++)
                    {
                        double mm = storey * 1000 * i;
                        string key = $"L{i:00}";
                        CreateLevel(doc, $"{code}-{key}-FFL", mm, ref levelsCreated);
                        CreateLevel(doc, $"{code}-{key}-SSL", mm - 50, ref levelsCreated);
                    }
                    CreateLevel(doc, $"{code}-RF-TOS", storey * 1000 * (levelCount + 1), ref levelsCreated);

                    // Grids — numeric axis (1..N) running E-W, letter axis (A..ZZ skipping IOQZ)
                    string[] letters = { "A","B","C","D","E","F","G","H","J","K","L","M","N","P","R","S","T","U","V","W","X","Y" };
                    for (int x = 1; x <= gridsX; x++)
                        CreateVerticalGrid(doc, $"{code}-{x}", (x - 1) * bayMm / 304.8 /* mm→ft */, ref gridsCreated);
                    for (int y = 0; y < gridsY && y < letters.Length; y++)
                        CreateHorizontalGrid(doc, $"{code}-{letters[y]}", y * bayMm / 304.8, ref gridsCreated);
                    tx.Commit();
                }

                StingResultPanel.Create($"Seeded Building: {code}")
                    .SetSubtitle($"Pre-named levels + grids created per STING multi-building convention.")
                    .AddSection("CREATED")
                    .Metric("Levels", levelsCreated.ToString())
                    .Metric("Grids",  gridsCreated.ToString())
                    .Metric("Building code", code)
                    .Show();

                StingCommandHandler.ClearExtraParam("SeedBuildingCode");
                StingCommandHandler.ClearExtraParam("SeedLevelCount");
                StingCommandHandler.ClearExtraParam("SeedGridsX");
                StingCommandHandler.ClearExtraParam("SeedGridsY");
                return Result.Succeeded;
            }
            catch (Exception ex) { StingLog.Error("BuildingCodeSeedCommand", ex); message = ex.Message; return Result.Failed; }
        }

        private static void CreateLevel(Document doc, string name, double mmAboveZero, ref int count)
        {
            try
            {
                // Check for existing same-named level
                var existing = new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>()
                    .FirstOrDefault(l => string.Equals(l.Name, name, StringComparison.OrdinalIgnoreCase));
                if (existing != null) return;
                double ft = mmAboveZero / 304.8;
                var lvl = Level.Create(doc, ft);
                try { lvl.Name = name; } catch (Exception ex) { StingLog.Warn($"Level rename {name}: {ex.Message}"); }
                count++;
            }
            catch (Exception ex) { StingLog.Warn($"CreateLevel {name}: {ex.Message}"); }
        }

        private static void CreateVerticalGrid(Document doc, string name, double xOffsetFt, ref int count)
        {
            try
            {
                var existing = new FilteredElementCollector(doc).OfClass(typeof(Grid)).Cast<Grid>()
                    .FirstOrDefault(g => string.Equals(g.Name, name, StringComparison.OrdinalIgnoreCase));
                if (existing != null) return;
                var p1 = new XYZ(xOffsetFt, 0, 0);
                var p2 = new XYZ(xOffsetFt, 200, 0);
                var ln = Line.CreateBound(p1, p2);
                var g = Grid.Create(doc, ln);
                try { g.Name = name; } catch (Exception ex) { StingLog.Warn($"Grid rename {name}: {ex.Message}"); }
                count++;
            }
            catch (Exception ex) { StingLog.Warn($"CreateVerticalGrid {name}: {ex.Message}"); }
        }

        private static void CreateHorizontalGrid(Document doc, string name, double yOffsetFt, ref int count)
        {
            try
            {
                var existing = new FilteredElementCollector(doc).OfClass(typeof(Grid)).Cast<Grid>()
                    .FirstOrDefault(g => string.Equals(g.Name, name, StringComparison.OrdinalIgnoreCase));
                if (existing != null) return;
                var p1 = new XYZ(0, yOffsetFt, 0);
                var p2 = new XYZ(200, yOffsetFt, 0);
                var ln = Line.CreateBound(p1, p2);
                var g = Grid.Create(doc, ln);
                try { g.Name = name; } catch (Exception ex) { StingLog.Warn($"Grid rename {name}: {ex.Message}"); }
                count++;
            }
            catch (Exception ex) { StingLog.Warn($"CreateHorizontalGrid {name}: {ex.Message}"); }
        }
    }

    // ══════════════════════════════════════════════════════════════════════
    //  B5 — PRJ_VOLUME_CODE auto-populator
    //  Parses the ISO 19650 file name for the Volume code and writes it
    //  into the shared parameter PRJ_VOLUME_CODE on ProjectInformation.
    //  File name: PRJ-ORG-{VOL}-LVL-TYP-ROLE-NR.rvt
    // ══════════════════════════════════════════════════════════════════════

    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class ProjectVolumeCodeAutoPopulateCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                var ctx = ParameterHelpers.GetContext(commandData);
                if (ctx?.Doc == null) return Result.Failed;
                var doc = ctx.Doc;

                string fn = Path.GetFileNameWithoutExtension(doc.PathName ?? "");
                string code = ParseVolumeCode(fn);
                if (string.IsNullOrEmpty(code))
                {
                    TaskDialog.Show("Volume Code",
                        $"Couldn't parse a volume code from file name \"{fn}\".\n"
                        + "Expected ISO 19650 pattern: {Project}-{Originator}-{Volume}-{Level}-{Type}-{Role}-{Number}.rvt");
                    return Result.Cancelled;
                }

                using (var tx = new Transaction(doc, "STING Set PRJ_VOLUME_CODE"))
                {
                    tx.Start();
                    ParameterHelpers.SetString(doc.ProjectInformation, "PRJ_VOLUME_CODE", code, overwrite: true);
                    tx.Commit();
                }
                TagConfig.SetConfigValue("PRJ_VOLUME_CODE", code);
                StingLog.Info($"PRJ_VOLUME_CODE set to {code} from filename '{fn}'.");
                TaskDialog.Show("Volume Code", $"PRJ_VOLUME_CODE set to {code}\n(parsed from filename).");
                return Result.Succeeded;
            }
            catch (Exception ex) { StingLog.Error("ProjectVolumeCodeAutoPopulate", ex); message = ex.Message; return Result.Failed; }
        }

        public static string ParseVolumeCode(string fileName)
        {
            if (string.IsNullOrEmpty(fileName)) return null;
            var parts = fileName.Split('-');
            if (parts.Length >= 3) return parts[2].Trim().ToUpperInvariant();
            return null;
        }
    }

    // ══════════════════════════════════════════════════════════════════════
    //  B6 — Federation coordination review
    //  Opens every linked RVT in the active doc, verifies that levels +
    //  grids are prefixed with a volume code from the LOC vocabulary,
    //  reports unprefixed ones as findings.
    // ══════════════════════════════════════════════════════════════════════

    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class FederationCoordinationReviewCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                var ctx = ParameterHelpers.GetContext(commandData);
                if (ctx?.Doc == null) return Result.Failed;
                var doc = ctx.Doc;

                var locCodes = new HashSet<string>(LocVocabularyOverride.GetAllLocCodes(), StringComparer.OrdinalIgnoreCase);
                var pattern = new Regex(@"^(?<code>[A-Z]+[A-Z0-9]*)[-_]");

                var findings = new List<string>();
                var links = new FilteredElementCollector(doc).OfClass(typeof(RevitLinkInstance))
                    .Cast<RevitLinkInstance>().ToList();
                int linkCount = links.Count;
                int ok = 0, unprefixed = 0;

                // Host document itself
                AuditDoc(doc, locCodes, pattern, findings, ref ok, ref unprefixed, "host");

                foreach (var li in links)
                {
                    var ld = li.GetLinkDocument();
                    if (ld == null) continue;
                    AuditDoc(ld, locCodes, pattern, findings, ref ok, ref unprefixed, li.Name);
                }

                var rp = StingResultPanel.Create("Federation Coordination Review")
                    .SetSubtitle($"Audited host + {linkCount} link(s). Levels + grids must be prefixed with a volume code.")
                    .AddSection("COVERAGE")
                    .Metric("Links audited", linkCount.ToString())
                    .Metric("Prefixed OK",   ok.ToString())
                    .Metric("Unprefixed",    unprefixed.ToString())
                    .Metric("Pass rate",     $"{(ok + unprefixed > 0 ? 100.0 * ok / (ok + unprefixed) : 0):F1}%");
                if (findings.Count > 0)
                {
                    rp.AddSection($"FINDINGS (first 30 of {findings.Count})");
                    foreach (var f in findings.Take(30)) rp.Text(f);
                }
                rp.Show();
                return unprefixed == 0 ? Result.Succeeded : Result.Failed;
            }
            catch (Exception ex) { StingLog.Error("FederationCoordinationReviewCommand", ex); message = ex.Message; return Result.Failed; }
        }

        private static void AuditDoc(Document d, HashSet<string> locCodes, Regex pattern,
            List<string> findings, ref int ok, ref int unprefixed, string label)
        {
            foreach (var l in new FilteredElementCollector(d).OfClass(typeof(Level)).Cast<Level>())
            {
                var m = pattern.Match(l.Name ?? "");
                if (m.Success && locCodes.Contains(m.Groups["code"].Value)) ok++;
                else { unprefixed++; findings.Add($"• [{label}] Level '{l.Name}' — no recognised volume-code prefix"); }
            }
            foreach (var g in new FilteredElementCollector(d).OfClass(typeof(Grid)).Cast<Grid>())
            {
                var m = pattern.Match(g.Name ?? "");
                if (m.Success && locCodes.Contains(m.Groups["code"].Value)) ok++;
                else { unprefixed++; findings.Add($"• [{label}] Grid '{g.Name}' — no recognised volume-code prefix"); }
            }
        }
    }
}
