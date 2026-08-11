// ══════════════════════════════════════════════════════════════════════════
//  SiteCutFillTakeoffCommand.cs — B-2. The earthwork path.
//
//  Before this, earthworks could not reach the bill at all except as a
//  hand-typed row: STING_DEFAULT_COST_RATES.csv priced Toposolid at 60 per m²
//  — AREA, not volume — no take-off rule mentioned Toposolid, and no command
//  anywhere read Revit's cut/fill. On a site with 27.75 m of fall that is
//  plausibly the largest single cost item on the project, missing entirely.
//
//  ── PARAMETER NAMES WERE MEASURED, NOT ASSUMED ────────────────────────────
//
//  The obvious guesses do not exist. There is no GRADED_REGION_CUT, no
//  CUT_VOLUME, no TOPOSOLID_CUT. Probed against the shipped RevitAPI.dll for
//  Revit 2025 (25.4.30.0), 2026 (26.4.0.0) and 2027 (27.1.0.0) — all eleven
//  parameters below are present in all three:
//
//      VOLUME_CUT                       cut volume
//      VOLUME_FILL                      fill volume
//      VOLUME_NET                       net (Revit's own cut − fill)
//      TOTAL_EXCAVATION_VOLUME          all excavations on a host
//      EXCAVATION_VOLUME                one excavation
//      EXCAVATION_VOLUME_ON_TOPOSOLID   excavation attributed to a toposolid
//      INDIVIDUAL_EXCAVATION_VOLUME     per-excavator contribution
//      TOPOSOLID_ELEVATION_AT_TOP/_BOTTOM, TOPOSOLID_ATTR_THICKNESS_PARAM
//
//  So cut and fill ARE reachable as parameters and no geometry is synthesised.
//  Where a parameter is absent on a given element the element is reported as a
//  NAMED SKIP with the reason — never as a zero. A zero earthworks row reads as
//  "flat site"; a named skip reads as "not measured", and only one of those is
//  true.
//
//  ── WHAT IT EMITS ─────────────────────────────────────────────────────────
//
//  Four measured rows in m³, because a QS prices them separately and a single
//  "earthworks" figure is not a bill item:
//
//      EARTH-01  Excavate to reduce levels
//      EARTH-02  Cart away surplus
//      EARTH-03  Imported fill
//      EARTH-04  Fill deposited and compacted in layers
//
//  Rows 02 and 03 are DERIVED from cut and fill, and the derivation makes no
//  invented assumption by default:
//
//      reuse fraction r  = SITE_CUT_REUSE_FRACTION   (default 1.0)
//      bulking factor b  = SITE_CART_BULKING_FACTOR  (default 1.0)
//
//      imported fill = max(0, F − C·r)
//      cart away     = (C·(1−r) + max(0, C·r − F)) · b
//      compact       = F
//
//  At the defaults these reduce to max(0, F−C) and max(0, C−F) — pure Revit
//  numbers with nothing added. A project that knows its soil suitability and
//  measures cart-away loose sets the two knobs; the tool does not guess them,
//  because a fabricated reuse percentage would move the largest cost item on
//  the job and look like a measurement.
//
//  ── ACCURACY ─────────────────────────────────────────────────────────────
//
//  Revit's cut/fill is roughly ±2 %. Every output carries that; nothing is
//  presented to millimetre precision.
// ══════════════════════════════════════════════════════════════════════════
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.BOQ;
using StingTools.Core;
using StingTools.UI;

namespace StingTools.Commands.Site
{
    /// <summary>One measured platform / surface.</summary>
    internal sealed class EarthworkRow
    {
        public long Id;
        public string Name = "";
        public string Kind = "";          // Toposolid | Subdivision | Topography | Graded region
        public double CutM3, FillM3, NetM3, ExcavationM3;
        public bool HasCut, HasFill;
        public string Skip = "";          // non-empty ⇒ not measured, with the reason
    }

    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class SiteCutFillTakeoffCommand : IExternalCommand
    {
        // Revit internal volume unit is ft³.
        private const double Ft3ToM3 = 0.0283168465892;

        /// <summary>Stable prefix so a re-run replaces its own rows instead of
        /// duplicating them, and never touches a QS's hand-authored rows.</summary>
        private const string RowIdPrefix = "STING_EARTHWORK_";

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                var ctx = ParameterHelpers.GetContext(commandData);
                if (ctx?.Doc == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }
                var doc = ctx.Doc;
                SafeWrite.ResetRun();

                var rows = new List<EarthworkRow>();
                CollectToposolids(doc, rows);
                CollectLegacyTopography(doc, rows);

                var measured = rows.Where(r => string.IsNullOrEmpty(r.Skip)).ToList();
                var skipped = rows.Where(r => !string.IsNullOrEmpty(r.Skip)).ToList();

                double cut = measured.Sum(r => r.CutM3);
                double fill = measured.Sum(r => r.FillM3);
                double exc = measured.Sum(r => r.ExcavationM3);

                double reuse = Clamp(TagConfig.GetConfigDouble("SITE_CUT_REUSE_FRACTION", 1.0), 0, 1);
                double bulk = Math.Max(1.0, TagConfig.GetConfigDouble("SITE_CART_BULKING_FACTOR", 1.0));

                double reusable = cut * reuse;
                double imported = Math.Max(0, fill - reusable);
                double cartAway = (cut * (1 - reuse) + Math.Max(0, reusable - fill)) * bulk;

                // Excavation volumes (building pads / foundations cut into the
                // toposolid) are a SEPARATE quantity from reduce-level cut and are
                // reported alongside rather than folded in — adding them would
                // double-count wherever Revit already attributes them to the host.
                var bill = new List<(string Ref, string Name, double Qty, string Nrm2, string RateKey, string Note)>
                {
                    ("EARTH-01", "Excavate to reduce levels", cut, "5", "Earthworks Excavate",
                        "Sum of VOLUME_CUT over measured platforms. Revit cut/fill accuracy approx +/-2%."),
                    ("EARTH-02", "Cart away surplus excavated material", cartAway, "5", "Earthworks Cart Away",
                        $"Derived: reuse fraction {reuse:0.##}, bulking {bulk:0.##}. At defaults this is max(0, cut - fill)."),
                    ("EARTH-03", "Imported fill", imported, "5", "Earthworks Imported Fill",
                        $"Derived: max(0, fill - cut x {reuse:0.##})."),
                    ("EARTH-04", "Fill deposited and compacted in layers", fill, "5", "Earthworks Compact",
                        "Sum of VOLUME_FILL over measured platforms. Revit cut/fill accuracy approx +/-2%."),
                };

                int written = WriteManualRows(doc, bill);
                string csvPath = WriteCsv(doc, rows, bill, reuse, bulk);

                var panel = StingResultPanel.Create("Site — cut / fill take-off");

                if (rows.Count == 0)
                {
                    // Nothing found is unknown, not zero. Same rule as H-3.
                    panel.SetSubtitle("No toposolid or topography found — no earthworks figure can be given");
                    panel.AddSection("NOTHING TO MEASURE")
                         .Text("This model contains no Toposolid and no Topography. That is not an "
                             + "earthworks quantity of zero; it means the site has not been modelled. "
                             + "No bill rows were written.");
                    panel.Show();
                    return Result.Succeeded;
                }

                panel.SetSubtitle($"{measured.Count} platform(s) measured, {skipped.Count} skipped · "
                                + $"cut {cut:N0} m³ · fill {fill:N0} m³ (±2%)");

                panel.AddSection("BILL ROWS (m³, ±2%)");
                foreach (var b in bill)
                    panel.Metric($"{b.Ref}  {b.Name}", $"{b.Qty:N0} m³", b.Note);

                panel.AddSection("BALANCE")
                     .Metric("Cut", $"{cut:N0} m³")
                     .Metric("Fill", $"{fill:N0} m³")
                     .Metric("Net (cut − fill)", $"{cut - fill:N0} m³",
                         "Positive = surplus to remove; negative = shortfall to import")
                     .Metric("Revit's own VOLUME_NET", $"{measured.Sum(r => r.NetM3):N0} m³",
                         "Cross-check — a wide gap against cut − fill means some platform reported one of the pair but not the other")
                     .Metric("Excavation (pads / foundations)", $"{exc:N0} m³",
                         "Reported separately — NOT added to EARTH-01, which would double-count");

                panel.AddSection("PER PLATFORM");
                foreach (var r in measured.OrderByDescending(x => x.CutM3 + x.FillM3).Take(30))
                    panel.Metric($"{r.Kind} · {r.Name}", $"cut {r.CutM3:N0} / fill {r.FillM3:N0} m³",
                        $"id {r.Id}");

                if (skipped.Count > 0)
                {
                    panel.AddSection("NOT MEASURED — named skips, not zeros");
                    foreach (var r in skipped.Take(30))
                        panel.Metric($"{r.Kind} · {r.Name}", $"id {r.Id}", r.Skip);
                }

                panel.AddSection("NEXT")
                     .Text($"{written} bill row(s) written to the manual-row store and will appear on the next "
                         + "BOQ refresh. Re-running this command replaces them rather than duplicating. "
                         + "Verify one platform by hand — area × average depth — and expect agreement "
                         + "within ~5%; a wider gap means the toposolid type has no variable-thickness "
                         + "layer, or the wrong surface was graded.");

                if (!string.IsNullOrEmpty(csvPath)) panel.SetCsvPath(csvPath);
                panel.Show();
                StingLog.Info($"Site cut/fill: {measured.Count} measured, {skipped.Count} skipped, "
                            + $"cut {cut:F1} m3, fill {fill:F1} m3, {written} bill rows");
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("SiteCutFillTakeoffCommand", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }

        // ── collection ───────────────────────────────────────────────────────

        private static void CollectToposolids(Document doc, List<EarthworkRow> rows)
        {
            var hosts = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_Toposolid)
                .WhereElementIsNotElementType()
                .ToList();

            foreach (var el in hosts)
            {
                // Subdivisions first. A platform subdivision with a negative
                // offset excavates its host and is individually schedulable —
                // per-platform quantities are what make the number checkable,
                // and a site total is not.
                var subIds = new List<ElementId>();
                try
                {
                    var ts = el as Toposolid;
                    if (ts != null)
                    {
                        var ids = ts.GetSubDivisionIds();
                        if (ids != null) subIds.AddRange(ids);
                    }
                }
                catch (Exception ex)
                {
                    StingLog.WarnRateLimited("Site.Subdiv", $"GetSubDivisionIds on {el.Id}: {ex.Message}");
                }

                foreach (var sid in subIds)
                {
                    var sub = doc.GetElement(sid);
                    if (sub != null) rows.Add(Measure(sub, "Subdivision"));
                }

                var host = Measure(el, "Toposolid");
                // A host whose subdivisions carry the grading often reports no
                // cut/fill of its own. Say that, rather than logging a bare skip
                // that looks like a defect.
                if (!host.HasCut && !host.HasFill && subIds.Count > 0)
                    host.Skip = $"host carries no cut/fill of its own; {subIds.Count} subdivision(s) measured instead";
                rows.Add(host);
            }
        }

        private static void CollectLegacyTopography(Document doc, List<EarthworkRow> rows)
        {
            // Pre-Toposolid sites, and graded regions, live on OST_Topography.
            // A model may hold both; a project mid-migration certainly will.
            var tops = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_Topography)
                .WhereElementIsNotElementType()
                .ToList();

            foreach (var el in tops)
            {
                bool isRegion = false;
                try
                {
                    var ts = el as Autodesk.Revit.DB.Architecture.TopographySurface;
                    if (ts != null) isRegion = ts.IsSiteSubRegion;
                }
                catch { }
                rows.Add(Measure(el, isRegion ? "Graded region" : "Topography"));
            }
        }

        private static EarthworkRow Measure(Element el, string kind)
        {
            var r = new EarthworkRow
            {
                Id = el.Id?.Value ?? 0,
                Kind = kind,
                Name = SafeName(el)
            };

            r.HasCut = TryVolume(el, BuiltInParameter.VOLUME_CUT, out double c);
            r.HasFill = TryVolume(el, BuiltInParameter.VOLUME_FILL, out double f);
            TryVolume(el, BuiltInParameter.VOLUME_NET, out double n);
            r.CutM3 = c; r.FillM3 = f; r.NetM3 = n;

            // Excavation: prefer the toposolid-attributed value, fall back to the
            // host total. Names verified present in 2025/2026/2027.
            if (!TryVolume(el, BuiltInParameter.EXCAVATION_VOLUME_ON_TOPOSOLID, out double e))
                TryVolume(el, BuiltInParameter.TOTAL_EXCAVATION_VOLUME, out e);
            r.ExcavationM3 = e;

            if (!r.HasCut && !r.HasFill)
                r.Skip = "VOLUME_CUT and VOLUME_FILL are both absent or unset — "
                       + "this surface has no graded region, so Revit computes no cut/fill for it";
            return r;
        }

        /// <summary>
        /// Read a volume parameter in m³. Returns false when the parameter is not
        /// present on this element or holds no value — which is the case that must
        /// surface as a named skip rather than a zero.
        /// </summary>
        private static bool TryVolume(Element el, BuiltInParameter bip, out double m3)
        {
            m3 = 0;
            try
            {
                var p = el.get_Parameter(bip);
                if (p == null || !p.HasValue || p.StorageType != StorageType.Double) return false;
                m3 = p.AsDouble() * Ft3ToM3;
                return true;
            }
            catch { return false; }
        }

        private static string SafeName(Element el)
        {
            try
            {
                string n = el.Name;
                if (!string.IsNullOrWhiteSpace(n)) return n;
            }
            catch { }
            try
            {
                var t = el.Document.GetElement(el.GetTypeId()) as ElementType;
                if (!string.IsNullOrWhiteSpace(t?.Name)) return t.Name;
            }
            catch { }
            return $"(unnamed {el.Id})";
        }

        // ── output ───────────────────────────────────────────────────────────

        /// <summary>
        /// Idempotent: drops any row this command wrote before, keeps everything
        /// else. A QS's hand-authored rows are never touched.
        /// </summary>
        private static int WriteManualRows(Document doc,
            List<(string Ref, string Name, double Qty, string Nrm2, string RateKey, string Note)> bill)
        {
            try
            {
                var store = BOQCostManager.LoadManualStore(doc);
                var kept = (store.ManualRows ?? new List<BOQLineItem>())
                    .Where(x => x?.Id == null || !x.Id.StartsWith(RowIdPrefix, StringComparison.Ordinal))
                    .ToList();

                int added = 0;
                foreach (var b in bill)
                {
                    // A zero row is still written: "excavate 0 m³" is a measured
                    // statement the QS can check, whereas an absent row is silence.
                    // Manual rows are "priced by the author" by design, so without
                    // a rate these would land in the bill at zero — earthworks
                    // present but costing nothing, which is the same invisibility
                    // B-2 is about. Seed the shipped BENCHMARK rate and label it as
                    // one, so the figure is visibly provisional rather than absent
                    // or falsely authoritative.
                    double rateUsd = BenchmarkRate(b.RateKey);
                    double fx = TagConfig.GetConfigDouble("UGX_PER_USD", 3700.0);
                    kept.Add(new BOQLineItem
                    {
                        Id = RowIdPrefix + b.Ref,
                        Source = BOQRowSource.Manual,
                        Category = "Site",
                        ItemName = b.Name,
                        Quantity = Math.Round(b.Qty, 1),
                        Unit = "m³",
                        NRM2Section = b.Nrm2,
                        RateUSD = rateUsd,
                        RateUGX = rateUsd > 0 && fx > 0 ? Math.Round(rateUsd * fx, 0) : 0,
                        RateSource = rateUsd > 0 ? "Benchmark" : "",
                        RateConfidence = rateUsd > 0 ? 40 : 0,
                        Note = b.Note
                             + (rateUsd > 0
                                ? $"  RATE IS A GLOBAL BENCHMARK ({rateUsd:0.##} USD/m³ from STING_DEFAULT_COST_RATES.csv) — override before tender."
                                : "  NO RATE — price this row.")
                             + "  [Site_CutFillTakeoff — re-run to refresh]"
                    });
                    added++;
                }
                BOQCostManager.SaveManualRows(doc, kept, store.ProjectBudgetUGX);
                return added;
            }
            catch (Exception ex)
            {
                StingLog.Error("SiteCutFillTakeoff.WriteManualRows", ex);
                return 0;
            }
        }

        private static string WriteCsv(Document doc, List<EarthworkRow> rows,
            List<(string Ref, string Name, double Qty, string Nrm2, string RateKey, string Note)> bill,
            double reuse, double bulk)
        {
            try
            {
                string dir = StingPaths.Meta(doc, "_BIM_COORD");
                Directory.CreateDirectory(dir);
                string path = Path.Combine(dir, $"site_cutfill_{DateTime.Now:yyyyMMdd_HHmmss}.csv");
                var sb = new StringBuilder();
                sb.AppendLine("# Revit graded cut/fill accuracy is approximately +/-2%. Quantities in m3, in-situ.");
                sb.AppendLine($"# reuse fraction={reuse:0.##}  bulking factor={bulk:0.##}");
                sb.AppendLine("Section,Ref,Description,QuantityM3,Note");
                foreach (var b in bill)
                    sb.AppendLine(string.Join(",", "BILL", b.Ref, Csv(b.Name),
                        b.Qty.ToString("0.0", CultureInfo.InvariantCulture), Csv(b.Note)));
                sb.AppendLine();
                sb.AppendLine("Section,ElementId,Kind,Name,CutM3,FillM3,NetM3,ExcavationM3,Skipped");
                foreach (var r in rows.OrderByDescending(x => x.CutM3 + x.FillM3))
                    sb.AppendLine(string.Join(",", "PLATFORM",
                        r.Id.ToString(CultureInfo.InvariantCulture), Csv(r.Kind), Csv(r.Name),
                        r.CutM3.ToString("0.0", CultureInfo.InvariantCulture),
                        r.FillM3.ToString("0.0", CultureInfo.InvariantCulture),
                        r.NetM3.ToString("0.0", CultureInfo.InvariantCulture),
                        r.ExcavationM3.ToString("0.0", CultureInfo.InvariantCulture),
                        Csv(r.Skip)));
                File.WriteAllText(path, sb.ToString());
                return path;
            }
            catch (Exception ex) { StingLog.Warn($"Site cut/fill CSV: {ex.Message}"); return null; }
        }


        /// <summary>
        /// Read one shipped benchmark rate (USD) from STING_DEFAULT_COST_RATES.csv.
        /// Returns 0 when the row is absent — reported on the line as "NO RATE"
        /// rather than silently becoming free.
        /// </summary>
        private static double BenchmarkRate(string categoryKey)
        {
            try
            {
                string path = StingToolsApp.FindDataFile("STING_DEFAULT_COST_RATES.csv");
                if (string.IsNullOrEmpty(path) || !File.Exists(path)) return 0;
                foreach (var line in File.ReadAllLines(path))
                {
                    if (string.IsNullOrWhiteSpace(line) || line.TrimStart().StartsWith("#")) continue;
                    var f = StingToolsApp.ParseCsvLine(line);
                    if (f == null || f.Length < 2) continue;
                    if (!string.Equals(f[0]?.Trim(), categoryKey, StringComparison.OrdinalIgnoreCase)) continue;
                    if (double.TryParse(f[1]?.Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out double v))
                        return v;
                }
            }
            catch (Exception ex) { StingLog.Warn($"BenchmarkRate('{categoryKey}'): {ex.Message}"); }
            return 0;
        }
        private static string Csv(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.IndexOfAny(new[] { ',', '"', '\n' }) >= 0
                ? "\"" + s.Replace("\"", "\"\"") + "\"" : s;
        }

        private static double Clamp(double v, double lo, double hi)
            => double.IsNaN(v) ? lo : Math.Max(lo, Math.Min(hi, v));
    }
}
