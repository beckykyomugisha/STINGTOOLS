using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.UI;
using StingTools.Core;

namespace StingTools.Commands.Electrical.Lighting
{
    // ─────────────────────────────────────────────────────────────────────────
    // Phase 192 (D1) — ComCheck interior-lighting input export.
    //
    // Emits a CSV companion structured for COMcheck interior-lighting entry: one
    // block per space (space type + area + ALLOWED LPD reused from the existing
    // ASHRAE 90.1 LPD engine — NOT re-tabulated here) with its installed fixture
    // schedule, plus an allowed-vs-proposed summary. Designers paste the values
    // into COMcheck (the dialog states this).
    // ─────────────────────────────────────────────────────────────────────────

    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class ComCheckExportCommand : IExternalCommand
    {
        private const double SqFtToM2 = 0.09290304;

        private class FixtureAgg { public string Type; public string Desc; public int Lamps; public double WattsEach; public int Qty; }
        private class SpaceRow
        {
            public string Name, SpaceType;
            public double AreaFt2, AreaM2, LimitWm2;
            public double AllowedW => LimitWm2 * AreaM2;
            public double AllowedWFt2 => LimitWm2 * SqFtToM2;
            public List<FixtureAgg> Fixtures = new List<FixtureAgg>();
            public double ProposedW => Fixtures.Sum(f => f.WattsEach * f.Qty);
        }

        public Result Execute(ExternalCommandData cmd, ref string msg, ElementSet els)
        {
            var ctx = ParameterHelpers.GetContext(cmd);
            if (ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }
            Document doc = ctx.Doc;

            // Reuse the existing ASHRAE 90.1 LPD table (no duplication). ComCheck is US.
            var table = LightingPowerDensityCommand.LoadLpdLimits("ASHRAE_90_1_2019");
            var spaceMap = LoadSpaceMap(doc);

            // Rooms → space rows
            var rooms = new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_Rooms)
                .WhereElementIsNotElementType().OfType<Room>()
                .Where(r => r.Area > 1e-6 && r.Location != null).ToList();
            if (rooms.Count == 0)
            {
                TaskDialog.Show("ComCheck Export", "No placed rooms — ComCheck input is per space.");
                return Result.Succeeded;
            }

            var byRoom = new Dictionary<long, SpaceRow>();
            foreach (var r in rooms)
            {
                string name = r.Name ?? "";
                string spaceType = ParameterHelpers.GetString(r, "HVC_SPACE_TYPE_TXT");
                if (string.IsNullOrEmpty(spaceType)) spaceType = MapSpaceType(spaceMap, name);
                byRoom[r.Id.Value] = new SpaceRow
                {
                    Name = string.IsNullOrEmpty(r.Number) ? name : $"{r.Number} {name}".Trim(),
                    SpaceType = spaceType,
                    AreaFt2 = r.Area,
                    AreaM2 = r.Area * SqFtToM2,
                    LimitWm2 = table.LookupLimit(name),
                };
            }

            // Lighting fixtures → their room, aggregated per type
            int unassigned = 0;
            foreach (var fi in new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_LightingFixtures)
                         .WhereElementIsNotElementType().OfType<FamilyInstance>())
            {
                Room room = null;
                try { room = ParameterHelpers.GetRoomAtElement(doc, fi) as Room; } catch { }
                if (room == null || !byRoom.TryGetValue(room.Id.Value, out var sr)) { unassigned++; continue; }

                string type = ParameterHelpers.GetFamilySymbolName(fi);
                double watts = LightingPowerDensityCommand.ReadWattage(fi);
                var agg = sr.Fixtures.FirstOrDefault(f => f.Type == type && Math.Abs(f.WattsEach - watts) < 0.5);
                if (agg == null)
                {
                    agg = new FixtureAgg
                    {
                        Type = type,
                        Desc = ParameterHelpers.GetFamilyName(fi),
                        Lamps = ReadLamps(fi),
                        WattsEach = watts,
                        Qty = 0,
                    };
                    sr.Fixtures.Add(agg);
                }
                agg.Qty++;
            }

            string path = WriteCsv(doc, byRoom.Values.OrderBy(s => s.Name).ToList(), table.StandardId, unassigned);

            double totAllowed = byRoom.Values.Sum(s => s.AllowedW);
            double totProposed = byRoom.Values.Sum(s => s.ProposedW);
            var sb = new StringBuilder();
            sb.AppendLine($"Spaces: {byRoom.Count}   Standard: {table.StandardId} (allowed LPD reused from LPD engine)");
            sb.AppendLine($"Fixtures not in a room: {unassigned}");
            sb.AppendLine();
            sb.AppendLine($"Total allowed:  {totAllowed:F0} W");
            sb.AppendLine($"Total proposed: {totProposed:F0} W");
            sb.AppendLine($"Project result: {(totProposed <= totAllowed ? "PASS" : "FAIL")}");
            sb.AppendLine();
            sb.AppendLine("This CSV is a COMcheck data-entry companion — open COMcheck, add an");
            sb.AppendLine("Interior Lighting space per row, and paste the area + fixtures. STING does");
            sb.AppendLine("not write the binary .cck file.");
            if (path != null) { sb.AppendLine(); sb.AppendLine($"CSV: {path}"); }

            new TaskDialog("ComCheck Export")
            {
                MainInstruction = $"{byRoom.Count} space(s) — {(totProposed <= totAllowed ? "PASS" : "FAIL")} " +
                                  $"({totProposed:F0} / {totAllowed:F0} W)",
                MainContent = sb.ToString()
            }.Show();
            StingLog.Info($"ComCheck_Export: {byRoom.Count} spaces, proposed {totProposed:F0}W / allowed {totAllowed:F0}W");
            return Result.Succeeded;
        }

        private static int ReadLamps(FamilyInstance fi)
        {
            try
            {
                var p = fi.Symbol?.LookupParameter("Number of Lamps") ?? fi.LookupParameter("Number of Lamps");
                if (p != null && p.HasValue && p.StorageType == StorageType.Integer)
                {
                    int n = p.AsInteger();
                    if (n > 0) return n;
                }
            }
            catch { }
            return 1;
        }

        private static string MapSpaceType(List<(string Pattern, string Type)> map, string roomName)
        {
            string n = (roomName ?? "").ToLowerInvariant();
            foreach (var m in map)
                if (!string.IsNullOrEmpty(m.Pattern) && n.Contains(m.Pattern)) return m.Type;
            return "Office - Enclosed"; // safe COMcheck default
        }

        private static List<(string, string)> LoadSpaceMap(Document doc)
        {
            var list = new List<(string, string)>();
            void Parse(string path)
            {
                foreach (var raw in File.ReadAllLines(path))
                {
                    string line = raw.Trim();
                    if (line.Length == 0 || line.StartsWith("#")) continue;
                    var f = StingToolsApp.ParseCsvLine(line);
                    if (f.Length < 2) continue;
                    if (f[0].Trim().Equals("Pattern", StringComparison.OrdinalIgnoreCase)) continue;
                    list.Add((f[0].Trim().ToLowerInvariant(), f[1].Trim()));
                }
            }
            // Project overlay first (wins), then corporate.
            try
            {
                string dir = Path.GetDirectoryName(doc?.PathName ?? "");
                if (!string.IsNullOrEmpty(dir))
                {
                    string p = Path.Combine(dir, "_BIM_COORD", "comcheck_space_map.csv");
                    if (File.Exists(p)) Parse(p);
                }
            }
            catch (Exception ex) { StingLog.Warn($"ComCheck overlay map: {ex.Message}"); }
            try
            {
                string c = StingToolsApp.FindDataFile("STING_COMCHECK_SPACE_MAP.csv");
                if (!string.IsNullOrEmpty(c) && File.Exists(c)) Parse(c);
            }
            catch (Exception ex) { StingLog.Warn($"ComCheck corporate map: {ex.Message}"); }
            // longest pattern first so "open office" beats "office"
            return list.OrderByDescending(m => m.Item1.Length).ToList();
        }

        private static string WriteCsv(Document doc, List<SpaceRow> spaces, string standardId, int unassigned)
        {
            try
            {
                var rows = new List<string>
                {
                    $"# COMcheck Interior Lighting input — allowed LPD per {standardId} (reused from STING LPD engine)",
                    "Space,SpaceType,Area_ft2,Area_m2,AllowedLPD_W_per_ft2,AllowedW,FixtureType,Description,LampsPerFixture,WattsPerFixture,Quantity,FixtureTotalW"
                };
                foreach (var s in spaces)
                {
                    if (s.Fixtures.Count == 0)
                    {
                        rows.Add(string.Join(",", Csv(s.Name), Csv(s.SpaceType), F(s.AreaFt2), F(s.AreaM2),
                            F(s.AllowedWFt2, 3), F(s.AllowedW), "", "", "", "", "0", "0"));
                        continue;
                    }
                    bool first = true;
                    foreach (var fx in s.Fixtures.OrderByDescending(f => f.WattsEach * f.Qty))
                    {
                        rows.Add(string.Join(",",
                            Csv(first ? s.Name : ""), Csv(first ? s.SpaceType : ""),
                            first ? F(s.AreaFt2) : "", first ? F(s.AreaM2) : "",
                            first ? F(s.AllowedWFt2, 3) : "", first ? F(s.AllowedW) : "",
                            Csv(fx.Type), Csv(fx.Desc), fx.Lamps.ToString(), F(fx.WattsEach), fx.Qty.ToString(),
                            F(fx.WattsEach * fx.Qty)));
                        first = false;
                    }
                }
                rows.Add("");
                rows.Add("# SUMMARY,Space,SpaceType,Area_ft2,AllowedW,ProposedW,Result");
                double ta = 0, tp = 0;
                foreach (var s in spaces.OrderBy(s => s.Name))
                {
                    ta += s.AllowedW; tp += s.ProposedW;
                    rows.Add(string.Join(",", "SUMMARY", Csv(s.Name), Csv(s.SpaceType), F(s.AreaFt2),
                        F(s.AllowedW), F(s.ProposedW), s.ProposedW <= s.AllowedW ? "PASS" : "FAIL"));
                }
                rows.Add(string.Join(",", "PROJECT", "", "", "", F(ta), F(tp), tp <= ta ? "PASS" : "FAIL"));
                rows.Add($"# Fixtures not in a room (excluded): {unassigned}");

                string path = OutputLocationHelper.GetOutputPath(doc, $"STING_ComCheck_Lighting_{DateTime.Now:yyyyMMdd}.csv");
                File.WriteAllLines(path, rows, Encoding.UTF8);
                return path;
            }
            catch (Exception ex) { StingLog.Warn($"ComCheck CSV: {ex.Message}"); return null; }
        }

        private static string F(double d, int dp = 1) => Math.Round(d, dp).ToString();
        private static string Csv(string s) => "\"" + (s ?? "").Replace("\"", "\"\"") + "\"";
    }
}
