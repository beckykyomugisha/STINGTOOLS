// StingTools — CableScheduleBuilder.
//
// Auto-generates a Bill of Materials (BOM) from the cable manifest +
// the routed conduit graph. Removes the manual cable-take-off step
// estimators have been doing since the v4 MVP shipped.
//
// Three rollups produced per run:
//
//   1. Cable BOM    — total metres + weight by (CSA × cores × insulation
//                     × conductor) groups. Drives wire reel ordering.
//   2. Conduit BOM  — total metres by conduit type + nominal diameter.
//                     Drives stick / fitting ordering.
//   3. Boxes BOM    — junction-box / pull-box counts by family + size +
//                     IP rating. Drives enclosure ordering.
//
// Output formats:
//
//   * StingResultPanel — interactive read-only view in Revit.
//   * CSV file at <project>/_BIM_COORD/cable_schedule.csv — one row per
//     BOM line, columns matching SAP / standard estimating tools.
//   * Optional Revit schedule view (TODO Phase 183) once we map the
//     three rollups onto a single MEP system schedule.
//
// Invocation: WorkflowEngine tag "Cable_BuildSchedule"; UI button on
// the electrical command handler.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Core;
using StingTools.Core.Electrical;
using StingTools.UI;

namespace StingTools.Commands.Electrical.Routing
{
    public sealed class CableBomLine
    {
        public string Description     { get; set; } = "";
        public string Sku             { get; set; } = "";
        public double TotalLengthM    { get; set; }
        public double TotalWeightKg   { get; set; }
        public int    InstanceCount   { get; set; }
        public string Discipline      { get; set; } = "Electrical";
        public string Notes           { get; set; } = "";
    }

    public sealed class CableScheduleResult
    {
        public List<CableBomLine> CableLines  { get; } = new List<CableBomLine>();
        public List<CableBomLine> ConduitLines { get; } = new List<CableBomLine>();
        public List<CableBomLine> BoxLines     { get; } = new List<CableBomLine>();
        public string CsvPath { get; set; } = "";
        public int TotalCables { get; set; }
        public int TotalConduits { get; set; }
        public int TotalBoxes { get; set; }
        public List<string> Warnings { get; } = new List<string>();
    }

    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class CableScheduleBuilderCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { message = "No active document."; return Result.Failed; }
            var doc = ctx.Doc;

            CableManifest manifest;
            try { manifest = CableManifest.Load(doc); }
            catch (Exception ex) { StingLog.Warn($"Manifest load: {ex.Message}"); manifest = null; }
            if (manifest == null || manifest.Cables == null || manifest.Cables.Count == 0)
            {
                TaskDialog.Show("STING Cable Schedule",
                    "No cable manifest. Add cables / run Auto-Route Conduit before building a schedule.");
                return Result.Cancelled;
            }

            var result = Build(doc, manifest);

            // Persist CSV.
            try
            {
                string outDir = OutputLocationHelper.GetOutputDirectory(doc);
                if (!string.IsNullOrEmpty(outDir))
                {
                    Directory.CreateDirectory(outDir);
                    string csv = Path.Combine(outDir, "cable_schedule.csv");
                    WriteCsv(csv, result);
                    result.CsvPath = csv;
                }
            }
            catch (Exception ex) { StingLog.Warn($"Cable schedule CSV: {ex.Message}"); }

            try { ActionAuditLog.Record("Cable_BuildSchedule",
                $"cables={result.TotalCables} conduits={result.TotalConduits} boxes={result.TotalBoxes}"); }
            catch (Exception ex) { StingLog.Warn($"audit: {ex.Message}"); }

            ShowResult(result);
            return Result.Succeeded;
        }

        // ── Build ───────────────────────────────────────────────────────

        public static CableScheduleResult Build(Document doc, CableManifest manifest)
        {
            var r = new CableScheduleResult();
            if (manifest == null || manifest.Cables == null) return r;

            // ── Cables ── group by (CSA, cores, insulation, conductor)
            // so the same wire spec rolls up regardless of which circuit
            // it serves. Length comes from each cable's RouteTrayIds —
            // sum each conduit's actual curve length, then add 5% pull-
            // slack per BS 7671 / IET Wiring Regs §A2 (cable allowance).
            const double pullSlack = 1.05;
            var cableBins = manifest.Cables
                .GroupBy(c => $"{c.CsaMm2}|{c.CoreCount}|{c.InsulationType}|{c.ConductorMaterial}",
                    StringComparer.OrdinalIgnoreCase);

            foreach (var bin in cableBins)
            {
                var members = bin.ToList();
                if (members.Count == 0) continue;
                var ex = members[0];
                double lengthM = 0;
                int instances = 0;
                foreach (var c in members)
                {
                    double cableLength = ResolveCableLength(doc, c) * pullSlack;
                    lengthM += cableLength;
                    instances++;
                }
                double weightKg = lengthM * ex.WeightPerMetreKg;

                r.CableLines.Add(new CableBomLine
                {
                    Description = $"{ex.CoreCount}c × {ex.CsaMm2:F1} mm² {ex.ConductorMaterial} {ex.InsulationType}",
                    Sku         = $"CABLE_{ex.ConductorMaterial}_{ex.CoreCount}C_{ex.CsaMm2:F1}_{ex.InsulationType}",
                    TotalLengthM  = Math.Round(lengthM, 1),
                    TotalWeightKg = Math.Round(weightKg, 1),
                    InstanceCount = instances,
                    Notes = members.Count > 1
                        ? $"{instances} circuits across {members.Select(m => m.PanelName).Distinct().Count()} panel(s) — 5% pull-slack applied"
                        : "5% pull-slack applied",
                });
                r.TotalCables += instances;
            }

            // ── Conduits ── walk the manifest's RouteTrayIds set, dedup
            // (multiple cables share consolidated conduit), then group
            // by ConduitType + diameter. Length comes from the curve.
            var allConduitIds = new HashSet<long>();
            foreach (var c in manifest.Cables)
            {
                if (c.RouteTrayIds == null) continue;
                foreach (long id in c.RouteTrayIds) allConduitIds.Add(id);
            }

            var conduitsByKey = new Dictionary<string, (string desc, double len, int qty)>();
            foreach (long id in allConduitIds)
            {
                Element el = null;
                try { el = doc.GetElement(new ElementId((long)id)); } catch { }
                if (el == null) continue;
                var loc = el.Location as LocationCurve;
                double mm = loc?.Curve?.Length * 304.8 ?? 0;
                if (mm <= 0) continue;

                string typeName = "Conduit";
                try
                {
                    var t = doc.GetElement(el.GetTypeId());
                    if (t != null) typeName = t.Name ?? typeName;
                }
                catch { }
                string diam = ParameterHelpers.GetString(el, "Diameter")
                    ?? ParameterHelpers.GetString(el, "Outside Diameter")
                    ?? "?";
                string key = $"{typeName}|{diam}";
                if (!conduitsByKey.TryGetValue(key, out var entry))
                    entry = ($"{typeName} ⌀ {diam} mm", 0, 0);
                entry = (entry.desc, entry.len + (mm / 1000.0), entry.qty + 1);
                conduitsByKey[key] = entry;
            }
            foreach (var kv in conduitsByKey.OrderBy(k => k.Key))
            {
                r.ConduitLines.Add(new CableBomLine
                {
                    Description   = kv.Value.desc,
                    Sku           = $"CDT_{kv.Key.Replace('|', '_').Replace(' ', '_')}",
                    TotalLengthM  = Math.Round(kv.Value.len, 1),
                    InstanceCount = kv.Value.qty,
                    Notes         = $"{kv.Value.qty} segment(s) — actual curve length",
                });
                r.TotalConduits += kv.Value.qty;
            }

            // ── Boxes ── walk JunctionBoxIds across all cables, dedup
            // (one box can be shared by multiple cables), then group by
            // family/type. Reads ELC_JB_TYPE_TXT + ELC_JB_SIZE_MM +
            // ELC_JB_IP_RATING_TXT for richer SKU.
            var allBoxIds = new HashSet<long>();
            foreach (var c in manifest.Cables)
            {
                if (c.JunctionBoxIds == null) continue;
                foreach (long id in c.JunctionBoxIds) allBoxIds.Add(id);
            }

            var boxesByKey = new Dictionary<string, (string desc, int qty)>();
            foreach (long id in allBoxIds)
            {
                Element el = null;
                try { el = doc.GetElement(new ElementId((long)id)); } catch { }
                if (el == null) continue;

                string famName = "JB";
                string typeName = "default";
                try
                {
                    if (el is FamilyInstance fi)
                    {
                        famName = fi.Symbol?.FamilyName ?? famName;
                        typeName = fi.Symbol?.Name ?? typeName;
                    }
                }
                catch { }
                string jbType = ParameterHelpers.GetString(el, "ELC_JB_TYPE_TXT") ?? typeName;
                string jbSize = ParameterHelpers.GetString(el, "ELC_JB_SIZE_MM") ?? "";
                string jbIp   = ParameterHelpers.GetString(el, "ELC_JB_IP_RATING_TXT") ?? "";

                string key = $"{famName}|{jbType}|{jbSize}|{jbIp}";
                if (!boxesByKey.TryGetValue(key, out var entry))
                {
                    string desc = $"{famName} : {jbType}";
                    if (!string.IsNullOrEmpty(jbSize)) desc += $" ({jbSize}";
                    if (!string.IsNullOrEmpty(jbIp))   desc += $", {jbIp}";
                    if (!string.IsNullOrEmpty(jbSize) || !string.IsNullOrEmpty(jbIp)) desc += ")";
                    entry = (desc, 0);
                }
                entry = (entry.desc, entry.qty + 1);
                boxesByKey[key] = entry;
            }
            foreach (var kv in boxesByKey.OrderBy(k => k.Key))
            {
                r.BoxLines.Add(new CableBomLine
                {
                    Description   = kv.Value.desc,
                    Sku           = $"JB_{kv.Key.Replace('|', '_').Replace(' ', '_').Replace(":", "")}",
                    InstanceCount = kv.Value.qty,
                    Notes         = "auto-placed by JunctionBoxAutoPlacer",
                });
                r.TotalBoxes += kv.Value.qty;
            }

            return r;
        }

        // ── Helpers ─────────────────────────────────────────────────────

        private static double ResolveCableLength(Document doc, StingCable c)
        {
            // Prefer the manifest's stamped TotalLengthM (set by the
            // routing engine + cable sizer). Fall back to walking the
            // RouteTrayIds and summing each curve length when unset.
            if (c.TotalLengthM > 0) return c.TotalLengthM;
            double total = 0;
            if (c.RouteTrayIds != null)
            {
                foreach (long id in c.RouteTrayIds)
                {
                    try
                    {
                        var el = doc.GetElement(new ElementId((long)id));
                        var loc = el?.Location as LocationCurve;
                        if (loc?.Curve != null) total += loc.Curve.Length * 0.3048; // ft → m
                    }
                    catch { }
                }
            }
            return total;
        }

        private static void WriteCsv(string path, CableScheduleResult r)
        {
            var sb = new StringBuilder();
            sb.AppendLine("# STING Cable Schedule — auto-generated; safe to re-export.");
            sb.AppendLine("Section,SKU,Description,Quantity,UnitOfMeasure,TotalLength_m,TotalWeight_kg,Discipline,Notes");
            foreach (var l in r.CableLines)
                sb.AppendLine(CsvRow("CABLE", l, "m"));
            foreach (var l in r.ConduitLines)
                sb.AppendLine(CsvRow("CONDUIT", l, "m"));
            foreach (var l in r.BoxLines)
                sb.AppendLine(CsvRow("BOX", l, "ea"));
            File.WriteAllText(path, sb.ToString());
        }

        private static string CsvRow(string section, CableBomLine l, string uom)
        {
            string Esc(string s) => s == null ? "" : s.Contains(',') ? $"\"{s.Replace("\"", "\"\"")}\"" : s;
            return string.Join(",",
                section,
                Esc(l.Sku),
                Esc(l.Description),
                l.InstanceCount.ToString(CultureInfo.InvariantCulture),
                uom,
                l.TotalLengthM.ToString("F1", CultureInfo.InvariantCulture),
                l.TotalWeightKg.ToString("F1", CultureInfo.InvariantCulture),
                Esc(l.Discipline),
                Esc(l.Notes));
        }

        private static void ShowResult(CableScheduleResult r)
        {
            var panel = StingResultPanel.Create("Cable Schedule");
            panel.SetSubtitle($"{r.TotalCables} cable circuits · {r.TotalConduits} conduit segments · {r.TotalBoxes} boxes");
            panel.AddSection("SUMMARY")
                .Metric("Cable circuits",   r.TotalCables.ToString())
                .Metric("Conduit segments", r.TotalConduits.ToString())
                .Metric("Junction boxes",   r.TotalBoxes.ToString())
                .Metric("Total cable m",    r.CableLines.Sum(l => l.TotalLengthM).ToString("F1"))
                .Metric("Total conduit m",  r.ConduitLines.Sum(l => l.TotalLengthM).ToString("F1"))
                .Metric("Total cable kg",   r.CableLines.Sum(l => l.TotalWeightKg).ToString("F1"))
                .Metric("CSV",              r.CsvPath ?? "(not written)");

            if (r.CableLines.Count > 0)
            {
                panel.AddSection("CABLES");
                foreach (var l in r.CableLines.OrderByDescending(x => x.TotalLengthM).Take(20))
                    panel.Metric(l.Description, $"{l.TotalLengthM:F1} m",
                        $"{l.InstanceCount} ckt · {l.TotalWeightKg:F0} kg");
            }
            if (r.ConduitLines.Count > 0)
            {
                panel.AddSection("CONDUITS");
                foreach (var l in r.ConduitLines.OrderByDescending(x => x.TotalLengthM).Take(20))
                    panel.Metric(l.Description, $"{l.TotalLengthM:F1} m",
                        $"{l.InstanceCount} segments");
            }
            if (r.BoxLines.Count > 0)
            {
                panel.AddSection("JUNCTION BOXES");
                foreach (var l in r.BoxLines.OrderByDescending(x => x.InstanceCount).Take(20))
                    panel.Metric(l.Description, l.InstanceCount.ToString(), l.Notes);
            }
            panel.AddSection("NEXT STEPS")
                .Text("Open the CSV in Excel for procurement / rate enquiry.")
                .Text("Re-run after Apply consolidation / JB placement / swap to keep the BOM in sync.")
                .Text("Cable lengths include 5% pull-slack per BS 7671 / IET Wiring Regs §A2; adjust in code if your project uses a different allowance.");
            panel.Show();
        }
    }
}
