// StingTools — RTS calibration benchmark.
//
// Phase 187e — fourth deferred-list item. Without access to a live
// TRACE / HAP install we can't run a head-to-head comparison from
// inside the plugin. What we CAN do is ship a regression-grade
// benchmark: synthetic LoadZones matching published worked examples
// (ASHRAE Handbook 2021 Ch.18, Daikin design guide, CIBSE Guide A)
// with the expected block sensible load tabulated per RTS class. The
// benchmark runs the engine under Reactive / Light / Medium / Heavy
// and reports per-case ratios against the published values.
//
// Pass criterion is ±10 % — looser than a true TRACE comparison but
// adequate to catch a regression in the engine math (e.g. a unit
// error, a sign flip in the RTS convolution, a hardcoded latitude).
// Each reference case carries its source citation.
//
// Output: result panel + CSV under <project>/_BIM_COORD/acoustic/
// rts_benchmark_<ts>.csv.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using StingTools.Core;
using StingTools.Core.Climate;
using StingTools.Core.Hvac.Loads;
using StingTools.UI;

namespace StingTools.Commands.Hvac
{
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class HvacRtsBenchmarkCommand : IExternalCommand
    {
        private const double TolerancePct = 10.0;

        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            try
            {
                var ctx = ParameterHelpers.GetContext(commandData);
                if (ctx == null) { message = "No active document."; return Result.Failed; }
                var doc = ctx.Doc;

                var cases = LoadReferenceCases(doc);
                if (cases.Count == 0)
                {
                    TaskDialog.Show("STING HVAC — RTS Benchmark",
                        "No reference cases found in STING_RTS_REFERENCE_CASES.json.");
                    return Result.Cancelled;
                }

                var climateData = ClimateRegistry.Get(doc);
                var profileLib  = LoadProfileRegistry.Get(doc);

                var rows = new List<BenchmarkRow>();
                int pass = 0, fail = 0;
                foreach (var c in cases)
                {
                    var site = climateData.ById(c.ClimateId) ?? climateData.Sites.FirstOrDefault();
                    if (site == null) continue;
                    var zone = BuildSyntheticZone(c, profileLib);

                    foreach (var rtsName in new[] { "Reactive", "Light", "Medium", "Heavy" })
                    {
                        if (!Enum.TryParse(rtsName, out RtsConstructionClass rts)) continue;
                        if (!c.Expected.TryGetValue(rtsName, out double expectedKw)) continue;
                        var results = BlockLoadEngine.Run(new[] { zone }, site, cooling: true, rts: rts);
                        double actualKw = results.Sum(r => r.BlockSensibleW) / 1000.0;
                        double deltaPct = expectedKw > 0
                            ? 100.0 * (actualKw - expectedKw) / expectedKw : 0;
                        bool ok = Math.Abs(deltaPct) <= TolerancePct;
                        if (ok) pass++; else fail++;
                        rows.Add(new BenchmarkRow
                        {
                            CaseId       = c.Id,
                            CaseLabel    = c.Label,
                            RtsClass     = rtsName,
                            ExpectedKw   = expectedKw,
                            ActualKw     = actualKw,
                            DeltaPct     = deltaPct,
                            WithinBand   = ok,
                            Source       = c.Source
                        });
                    }
                }

                string csvPath = WriteCsv(doc, rows);

                var panel = StingResultPanel.Create("HVAC — RTS Benchmark");
                panel.SetSubtitle($"{cases.Count} cases × 4 RTS classes = {rows.Count} comparisons · " +
                                  $"tolerance ±{TolerancePct:F0} %");
                panel.AddSection("SUMMARY")
                     .Metric("Comparisons run",  rows.Count.ToString())
                     .Metric($"Within ±{TolerancePct:F0} %", pass.ToString())
                     .Metric($"Outside ±{TolerancePct:F0} %", fail.ToString())
                     .Metric("CSV",              csvPath ?? "(not written)");

                panel.AddSection("RESULTS");
                foreach (var grp in rows.GroupBy(r => r.CaseId))
                {
                    panel.Text($"• {grp.First().CaseLabel}");
                    foreach (var r in grp)
                        panel.Text($"    {r.RtsClass,-9} expected {r.ExpectedKw,5:F1} kW  actual {r.ActualKw,5:F1} kW  " +
                                   $"Δ {r.DeltaPct,+6:+0.0;-0.0;0.0} %  {(r.WithinBand ? "⬤" : "⬡")}");
                    panel.Text($"    source: {grp.First().Source}");
                }
                panel.Text("Benchmark is a regression check — synthetic single-zone calcs against " +
                           "published worked examples. ±10 % tolerance reflects engine simplifications " +
                           "(single design day, fixed schedules, simplified RTS). Failures usually mean " +
                           "a unit error / sign flip; ALL classes drifting one direction usually means " +
                           "the climate site mismatch.");
                panel.Show();

                try
                {
                    StingHvacPanel.Instance?.PushRunRow(
                        $"RTS benchmark ({pass} pass / {fail} fail)",
                        fail == 0 ? "⬤" : "⬡");
                }
                catch (Exception ex) { StingLog.Warn($"Panel push: {ex.Message}"); }

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("HvacRtsBenchmarkCommand", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }

        // ── Reference cases ─────────────────────────────────────────

        private class ReferenceCase
        {
            public string Id           = "";
            public string Label        = "";
            public string Source       = "";
            public string ClimateId    = "london";
            public string SpaceType    = "Office";
            public double FloorAreaM2;
            public double HeightM;
            public int    OccupantCount;
            public List<(double areaM2, double orientationDeg, double uvalue)> ExteriorWalls = new();
            public double WindowsM2;
            public double WindowOrientationDeg;
            public double WindowSHGC = 0.4;
            public bool   Roof;
            public Dictionary<string, double> Expected = new();
        }

        private class BenchmarkRow
        {
            public string CaseId, CaseLabel, RtsClass, Source;
            public double ExpectedKw, ActualKw, DeltaPct;
            public bool   WithinBand;
        }

        private static List<ReferenceCase> LoadReferenceCases(Document doc)
        {
            var list = new List<ReferenceCase>();
            try
            {
                string basePath = StingTools.Core.StingToolsApp.FindDataFile("STING_RTS_REFERENCE_CASES.json");
                if (!string.IsNullOrEmpty(basePath) && File.Exists(basePath))
                    Parse(File.ReadAllText(basePath), list);
                if (doc != null && !string.IsNullOrEmpty(doc.PathName))
                {
                    string proj = Path.Combine(Path.GetDirectoryName(doc.PathName) ?? "",
                                               "_BIM_COORD", "rts_reference_cases.json");
                    if (File.Exists(proj)) Parse(File.ReadAllText(proj), list);
                }
            }
            catch (Exception ex) { StingLog.Warn($"LoadReferenceCases: {ex.Message}"); }
            return list;
        }

        private static void Parse(string jsonText, List<ReferenceCase> list)
        {
            var j = JObject.Parse(jsonText);
            var arr = j["cases"] as JArray;
            if (arr == null) return;
            foreach (var c in arr.OfType<JObject>())
            {
                var rc = new ReferenceCase
                {
                    Id            = (string)c["id"] ?? "",
                    Label         = (string)c["label"] ?? "",
                    Source        = (string)c["source"] ?? "",
                    ClimateId     = (string)c["climate"] ?? "london",
                    SpaceType     = (string)c["spaceType"] ?? "Office",
                    FloorAreaM2   = (double?)c["floorAreaM2"] ?? 50,
                    HeightM       = (double?)c["heightM"] ?? 3.0,
                    OccupantCount = (int?)c["occupantCount"] ?? 5,
                    WindowsM2     = (double?)c["windowsM2"] ?? 0,
                    WindowOrientationDeg = (double?)c["windowOrientationDeg"] ?? 0,
                    WindowSHGC    = (double?)c["windowSHGC"] ?? 0.4,
                    Roof          = (bool?)c["roof"] ?? false
                };
                var walls = c["exteriorWalls"] as JArray;
                if (walls != null)
                    foreach (var w in walls.OfType<JObject>())
                        rc.ExteriorWalls.Add(((double?)w["areaM2"] ?? 0,
                                               (double?)w["orientationDeg"] ?? 180,
                                               (double?)w["uvalueWm2K"] ?? 0.30));
                var exp = c["expectedBlockKw"] as JObject;
                if (exp != null)
                    foreach (var kv in exp)
                        rc.Expected[kv.Key] = (double)kv.Value;
                list.Add(rc);
            }
        }

        private static LoadZone BuildSyntheticZone(ReferenceCase c, LoadProfileLibrary profileLib)
        {
            var profile = profileLib?.Get(c.SpaceType) ?? new LoadProfile();
            var z = new LoadZone
            {
                Id          = c.Id,
                Name        = c.Label,
                SystemId    = "Benchmark",
                SpaceTypeId = c.SpaceType,
                FloorAreaM2 = c.FloorAreaM2,
                HeightM     = c.HeightM,
                OccupantCount = c.OccupantCount > 0 ? c.OccupantCount
                                                    : profile.OccupantCountFor(c.FloorAreaM2)
            };
            profile.ApplyTo(z);
            foreach (var (area, orient, u) in c.ExteriorWalls)
                z.Envelope.Add(new EnvelopeSegment
                {
                    Kind = SegmentKind.ExteriorWall,
                    AreaM2 = area, UvalueWm2K = u, OrientationDeg = orient
                });
            if (c.WindowsM2 > 0)
                z.Envelope.Add(new EnvelopeSegment
                {
                    Kind = SegmentKind.Window,
                    AreaM2 = c.WindowsM2, UvalueWm2K = 1.4,
                    SHGC = c.WindowSHGC, ShadingFactor = 0.9,
                    OrientationDeg = c.WindowOrientationDeg
                });
            if (c.Roof)
                z.Envelope.Add(new EnvelopeSegment
                {
                    Kind = SegmentKind.Roof,
                    AreaM2 = c.FloorAreaM2, UvalueWm2K = 0.20, OrientationDeg = 0
                });
            return z;
        }

        private static string WriteCsv(Document doc, List<BenchmarkRow> rows)
        {
            try
            {
                string projDir = Path.GetDirectoryName(doc.PathName ?? "") ?? "";
                if (string.IsNullOrEmpty(projDir)) return null;
                string outDir = Path.Combine(projDir, "_BIM_COORD", "acoustic");
                Directory.CreateDirectory(outDir);
                string ts = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
                string csv = Path.Combine(outDir, $"rts_benchmark_{ts}.csv");
                var sb = new StringBuilder();
                sb.AppendLine("CaseId,CaseLabel,RtsClass,ExpectedKw,ActualKw,DeltaPct,WithinBand,Source");
                foreach (var r in rows)
                    sb.AppendLine($"\"{r.CaseId}\",\"{r.CaseLabel}\",{r.RtsClass}," +
                                  $"{r.ExpectedKw:F2},{r.ActualKw:F2},{r.DeltaPct:F2},{r.WithinBand},\"{r.Source}\"");
                File.WriteAllText(csv, sb.ToString());
                return csv;
            }
            catch (Exception ex) { StingLog.Warn($"WriteCsv: {ex.Message}"); return null; }
        }
    }
}
