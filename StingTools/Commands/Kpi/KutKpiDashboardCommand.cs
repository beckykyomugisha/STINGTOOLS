// KutKpiDashboardCommand.cs — Kampala Temple KPI dashboard (proposal §4.6).
//
// A real, visual KPI dashboard for the monthly BIM status report. Gathers the
// §4.6 KPI set from the existing engines (no new metric infrastructure):
//
//   - Tag / naming / metadata compliance %      (ComplianceScan)
//   - Per-discipline compliance                 (ComplianceScan.ByDisc)
//   - Open clash count + burn-down by severity  (clashes.json via ClashPersistence)
//   - Model-health score (0-100 composite)      (compliance + clash + warnings + stale)
//   - Revision %, stale, sheet compliance       (ComplianceScan)
//   - Compliance trend vs prior snapshots       (ComplianceTrendTracker)
//
// Renders through the reusable StingResultPanel (RAG bars + metrics + tables),
// persists a KPI snapshot to _BIM_COORD/kpi/kut_kpi_log.jsonl for fortnight-on-
// fortnight burn-down, and writes an HTML + CSV report for attachment to the
// monthly status report. Read-only; no Revit transaction.
//
// Exchange-punctuality, review-comment close-out and as-built capture currency
// are surfaced with their data source (workflow log / ReviewComments_Import /
// construction-stage LOD gate) — they are tracked outside the model, so the
// dashboard states the source rather than fabricating a number.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json;
using StingTools.Core;
using StingTools.Core.Clash;
using StingTools.Core.Storage;
using StingTools.Core.Twin;
using StingTools.ExLink;
using StingTools.UI;

namespace StingTools.Commands.Kpi
{
    /// <summary>One persisted KPI snapshot (JSONL line) for trend / burn-down.</summary>
    public sealed class KutKpiSnapshot
    {
        public string Ts { get; set; } = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ");
        public double CompliancePct { get; set; }
        public double StrictPct { get; set; }
        public double RevisionPct { get; set; }
        public double SheetCompliancePct { get; set; }
        public int TotalElements { get; set; }
        public int Untagged { get; set; }
        public int Stale { get; set; }
        public int Warnings { get; set; }
        public int OpenClashes { get; set; }
        public double HealthScore { get; set; }
        public Dictionary<string, int> OpenClashBySeverity { get; set; } = new Dictionary<string, int>();

        // Owner-system coverage (Fohlio / SpecLink / Niagara).
        public int FfeTotal { get; set; }
        public int FfeLinked { get; set; }
        public int FfeStale { get; set; }
        public double FfeLinkedPct => FfeTotal > 0 ? 100.0 * FfeLinked / FfeTotal : 100.0;
        public int SpecTotal { get; set; }
        public int SpecAssigned { get; set; }
        public double SpecCoveragePct => SpecTotal > 0 ? 100.0 * SpecAssigned / SpecTotal : 0;
        public int BmsPoints { get; set; }
        public int BmsNoEndpoint { get; set; }
    }

    public static class KutKpiEngine
    {
        // Health-score normalisation caps (documented, tunable).
        private const double ClashCap = 200.0, WarningCap = 500.0, StaleCap = 100.0;

        public static KutKpiSnapshot Gather(Document doc)
        {
            var cs = ComplianceScan.Scan(doc, forceRefresh: true);
            int warnings = 0;
            try { warnings = doc.GetWarnings()?.Count ?? 0; } catch { }

            // Open clashes from the latest STING clash run.
            int openClashes = 0;
            var bySev = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            try
            {
                string outDir = OutputLocationHelper.GetOutputDirectory(doc);
                if (!string.IsNullOrEmpty(outDir))
                {
                    var run = ClashPersistence.Load(Path.Combine(outDir, "clashes.json"));
                    if (run?.Clashes != null)
                    {
                        foreach (var c in run.Clashes)
                        {
                            if (IsResolved(c.State)) continue;
                            openClashes++;
                            string sev = string.IsNullOrEmpty(c.Severity) ? "Unclassified" : c.Severity;
                            bySev.TryGetValue(sev, out int n); bySev[sev] = n + 1;
                        }
                    }
                }
            }
            catch (Exception ex) { StingLog.Warn("KUT KPI clash load: " + ex.Message); }

            // ── Owner-system coverage: Fohlio (FF&E) / SpecLink (CSI) / Niagara (BMS) ──
            // Single combined pass over taggable categories: SpecLink CSI coverage (all
            // taggable cats) + Fohlio FF&E coverage (mapped cats only). Folds what were
            // two separate collector iterations into one (#13).
            int ffeTotal = 0, ffeLinked = 0, ffeStale = 0;
            int specTotal = 0, specAssigned = 0;
            try
            {
                var map = FohlioMap.Load(doc);
                var ffeCats = new HashSet<string>(map.Categories ?? new List<string>(), StringComparer.OrdinalIgnoreCase);
                var writeParams = map.Columns.Where(c => c.WriteBack && !FohlioMap.IsPseudo(c.Param)).Select(c => c.Param).ToList();

                var coll = new FilteredElementCollector(doc).WhereElementIsNotElementType();
                var catEnums = SharedParamGuids.AllCategoryEnums;
                if (catEnums != null && catEnums.Length > 0)
                    coll = coll.WherePasses(new ElementMulticategoryFilter(new List<BuiltInCategory>(catEnums)));

                foreach (var el in coll)
                {
                    if (el.Category == null) continue;
                    string cat = ParameterHelpers.GetCategoryName(el);

                    // SpecLink CSI coverage
                    specTotal++;
                    if (!string.IsNullOrEmpty(ParameterHelpers.GetString(el, ParamRegistry.CSI_SECTION))) specAssigned++;

                    // Fohlio FF&E coverage (mapped categories only)
                    if (ffeCats.Contains(cat))
                    {
                        ffeTotal++;
                        if (!string.IsNullOrEmpty(ParameterHelpers.GetString(el, ParamRegistry.FOHLIO_REF))) ffeLinked++;
                        var snap = StingFohlioSnapshotSchema.Read(el);
                        if (snap != null && snap.CapturedUtcTicks > 0)
                        {
                            Dictionary<string, string> sv = null;
                            try { sv = JsonConvert.DeserializeObject<Dictionary<string, string>>(snap.SnapshotJson); } catch { }
                            sv = sv ?? new Dictionary<string, string>();
                            foreach (var p in writeParams)
                            {
                                sv.TryGetValue(p, out string snapV);
                                if (!string.Equals(ParameterHelpers.GetString(el, p), snapV ?? "", StringComparison.Ordinal)) { ffeStale++; break; }
                            }
                        }
                    }
                }
            }
            catch (Exception ex) { StingLog.Warn("KUT KPI coverage scan: " + ex.Message); }

            int bmsPoints = 0, bmsNoEndpoint = 0;
            try
            {
                var devs = new IoTDeviceRegistry(doc).All().ToList();
                bmsPoints = devs.Count;
                bmsNoEndpoint = devs.Count(d => string.IsNullOrEmpty(d.EndpointAddress));
            }
            catch (Exception ex) { StingLog.Warn("KUT KPI BMS: " + ex.Message); }

            double compliance = cs.TotalElements > 0 ? cs.CompliancePercent : 0;
            double clashClean = 100.0 * (1.0 - Math.Min(1.0, openClashes / ClashCap));
            double warnClean  = 100.0 * (1.0 - Math.Min(1.0, warnings / WarningCap));
            double staleClean = 100.0 * (1.0 - Math.Min(1.0, cs.StaleCount / StaleCap));
            double health = 0.40 * compliance + 0.25 * clashClean + 0.20 * warnClean + 0.15 * staleClean;

            return new KutKpiSnapshot
            {
                CompliancePct      = Math.Round(compliance, 1),
                StrictPct          = Math.Round(cs.StrictPercent, 1),
                RevisionPct        = Math.Round(cs.RevisionPercent, 1),
                SheetCompliancePct = Math.Round(cs.SheetCompliancePct, 1),
                TotalElements      = Math.Max(0, cs.TotalElements),
                Untagged           = cs.Untagged,
                Stale              = cs.StaleCount,
                Warnings           = warnings,
                OpenClashes        = openClashes,
                HealthScore        = Math.Round(Math.Max(0, Math.Min(100, health)), 1),
                OpenClashBySeverity = bySev,
                FfeTotal = ffeTotal, FfeLinked = ffeLinked, FfeStale = ffeStale,
                SpecTotal = specTotal, SpecAssigned = specAssigned,
                BmsPoints = bmsPoints, BmsNoEndpoint = bmsNoEndpoint,
            };
        }

        private static bool IsResolved(string state) =>
            !string.IsNullOrEmpty(state) &&
            (state.Equals("Resolved", StringComparison.OrdinalIgnoreCase) ||
             state.Equals("Void", StringComparison.OrdinalIgnoreCase));

        private static string KpiDir(Document doc)
        {
            string dir = Path.GetDirectoryName(doc?.PathName ?? "");
            if (string.IsNullOrEmpty(dir)) return null;
            string p = Path.Combine(dir, "_BIM_COORD", "kpi");
            Directory.CreateDirectory(p);
            return p;
        }

        public static KutKpiSnapshot LoadPrevious(Document doc)
        {
            try
            {
                string dir = KpiDir(doc);
                string log = dir != null ? Path.Combine(dir, "kut_kpi_log.jsonl") : null;
                if (log == null || !File.Exists(log)) return null;
                var last = File.ReadLines(log).LastOrDefault(l => !string.IsNullOrWhiteSpace(l));
                return last != null ? JsonConvert.DeserializeObject<KutKpiSnapshot>(last) : null;
            }
            catch (Exception ex) { StingLog.Warn("KUT KPI load previous: " + ex.Message); return null; }
        }

        public static void Append(Document doc, KutKpiSnapshot snap)
        {
            try
            {
                string dir = KpiDir(doc);
                if (dir == null) return;
                File.AppendAllText(Path.Combine(dir, "kut_kpi_log.jsonl"),
                    JsonConvert.SerializeObject(snap) + Environment.NewLine);
            }
            catch (Exception ex) { StingLog.Warn("KUT KPI append: " + ex.Message); }
        }

        public static string WriteHtml(Document doc, KutKpiSnapshot s, KutKpiSnapshot prev)
        {
            try
            {
                var sb = new StringBuilder();
                sb.Append("<html><head><meta charset='utf-8'><title>KUT KPI</title>");
                sb.Append("<style>body{font-family:Segoe UI,Arial;margin:24px;color:#222}h1{color:#1A237E}");
                sb.Append("table{border-collapse:collapse;margin:8px 0}td,th{border:1px solid #ccc;padding:6px 10px}");
                sb.Append("th{background:#1A237E;color:#fff;text-align:left}</style></head><body>");
                sb.Append($"<h1>Kampala Temple — KPI Dashboard</h1><p>Generated {s.Ts} UTC</p>");
                sb.Append("<table><tr><th>KPI</th><th>Value</th><th>Δ since last</th></tr>");
                Row(sb, "Tag / metadata compliance", $"{s.CompliancePct:F1}%", Delta(s.CompliancePct, prev?.CompliancePct, "pp"));
                Row(sb, "Fully-resolved (strict)", $"{s.StrictPct:F1}%", Delta(s.StrictPct, prev?.StrictPct, "pp"));
                Row(sb, "Model-health score", $"{s.HealthScore:F0}/100", Delta(s.HealthScore, prev?.HealthScore, ""));
                Row(sb, "Open clashes", $"{s.OpenClashes}", Delta(s.OpenClashes, prev?.OpenClashes, "", invert: true));
                Row(sb, "Revision populated", $"{s.RevisionPct:F1}%", Delta(s.RevisionPct, prev?.RevisionPct, "pp"));
                Row(sb, "Sheet ISO 19650 compliance", $"{s.SheetCompliancePct:F1}%", Delta(s.SheetCompliancePct, prev?.SheetCompliancePct, "pp"));
                Row(sb, "Stale elements", $"{s.Stale}", Delta(s.Stale, prev?.Stale, "", invert: true));
                Row(sb, "Model warnings", $"{s.Warnings}", Delta(s.Warnings, prev?.Warnings, "", invert: true));
                Row(sb, "Fohlio FF&E linked", $"{s.FfeLinkedPct:F1}% ({s.FfeLinked}/{s.FfeTotal})", Delta(s.FfeLinkedPct, prev?.FfeLinkedPct, "pp"));
                Row(sb, "FF&E stale (model ≠ Fohlio)", $"{s.FfeStale}", Delta(s.FfeStale, prev?.FfeStale, "", invert: true));
                Row(sb, "SpecLink CSI coverage", $"{s.SpecCoveragePct:F1}% ({s.SpecAssigned}/{s.SpecTotal})", Delta(s.SpecCoveragePct, prev?.SpecCoveragePct, "pp"));
                Row(sb, "BMS points (Niagara)", $"{s.BmsPoints} ({s.BmsNoEndpoint} no endpoint)", Delta(s.BmsPoints, prev?.BmsPoints, ""));
                sb.Append("</table>");

                // SUS-6 — fold the latest EDGE/sustainability snapshot into the monthly
                // management report so it carries the EDGE trend (energy/water/materials
                // savings + level), not just tag/clash KPIs. Read-only; absent => skipped.
                try
                {
                    string projDir = System.IO.Path.GetDirectoryName(doc?.PathName ?? "");
                    var edge = string.IsNullOrEmpty(projDir)
                        ? null
                        : StingTools.Core.Sustainability.EdgeKpiSnapshot.LoadPrevious(projDir);
                    if (edge != null)
                    {
                        sb.Append("<h3>EDGE / sustainability (latest snapshot)</h3>");
                        sb.Append($"<p>As of {Esc(edge.Ts)} UTC &middot; STING-indicative; the EDGE App owns the certified figure.</p>");
                        sb.Append("<table><tr><th>EDGE KPI</th><th>Value</th></tr>");
                        Row2(sb, "EDGE level", $"{Esc(edge.EdgeLevel)} ({(edge.EdgePassed ? "pass" : "below target")})");
                        Row2(sb, "Energy savings", $"{edge.EnergySavingsPct:F1}%");
                        Row2(sb, "Water savings", $"{edge.WaterSavingsPct:F1}%");
                        Row2(sb, "Material embodied-energy savings", $"{edge.MaterialEnergySavingsPct:F1}%");
                        Row2(sb, "Embodied carbon", $"{edge.MaterialCarbonKgM2:F0} kgCO2e/m2");
                        Row2(sb, "Operational carbon", $"{edge.OperationalCarbonKgYr:F0} kgCO2e/yr");
                        sb.Append("</table>");
                    }
                }
                catch (Exception ex) { StingLog.Warn($"KutKpi EDGE fold-in: {ex.Message}"); }

                if (s.OpenClashBySeverity.Count > 0)
                {
                    sb.Append("<h3>Open clashes by severity</h3><table><tr><th>Severity</th><th>Open</th></tr>");
                    foreach (var kv in s.OpenClashBySeverity.OrderByDescending(k => k.Value))
                        sb.Append($"<tr><td>{Esc(kv.Key)}</td><td>{kv.Value}</td></tr>");
                    sb.Append("</table>");
                }
                sb.Append("</body></html>");
                string path = OutputLocationHelper.GetOutputPath(doc, $"STING_KUT_KPI_{Stamp()}.html");
                File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
                return path;
            }
            catch (Exception ex) { StingLog.Warn("KUT KPI html: " + ex.Message); return null; }
        }

        private static void Row(StringBuilder sb, string k, string v, string d)
            => sb.Append($"<tr><td>{Esc(k)}</td><td>{Esc(v)}</td><td>{Esc(d)}</td></tr>");

        // SUS-6 — two-column row for the folded-in EDGE snapshot table.
        private static void Row2(StringBuilder sb, string k, string v)
            => sb.Append($"<tr><td>{Esc(k)}</td><td>{Esc(v)}</td></tr>");

        public static string Delta(double now, double? prev, string unit, bool invert = false)
        {
            if (prev == null) return "—";
            double d = now - prev.Value;
            if (Math.Abs(d) < 0.05) return "0";
            string arrow = (invert ? d < 0 : d > 0) ? "▲" : "▼";
            return $"{arrow} {(d > 0 ? "+" : "")}{d:F1}{unit}";
        }

        private static string Stamp() => DateTime.UtcNow.ToString("yyyyMMddHHmmss");
        private static string Esc(string s) => (s ?? "").Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
    }

    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class KutKpiDashboardCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData cmd, ref string msg, ElementSet els)
        {
            var ctx = ParameterHelpers.GetContext(cmd);
            if (ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }
            Document doc = ctx.Doc;

            var snap = KutKpiEngine.Gather(doc);
            if (snap.TotalElements == 0)
            {
                TaskDialog.Show("KUT KPI Dashboard",
                    "No taggable elements found (or compliance scan still warming up). " +
                    "Load TagConfig / run a tag pass first, then retry.");
                return Result.Succeeded;
            }
            var prev = KutKpiEngine.LoadPrevious(doc);

            var cs = ComplianceScan.GetCached() ?? ComplianceScan.Scan(doc);
            (string trendDir, double trendDelta) = ("unknown", 0);
            try { (trendDir, trendDelta) = ComplianceTrendTracker.GetTrend(doc, 30); } catch { }
            try { ComplianceTrendTracker.RecordSnapshot(doc, cs); } catch { }

            string html = KutKpiEngine.WriteHtml(doc, snap, prev);
            string csv = WriteCsv(doc, snap, prev);
            KutKpiEngine.Append(doc, snap);

            var b = new StingResultPanel.Builder()
                .SetTitle("Kampala Temple — KPI Dashboard")
                .SetSubtitle($"Monthly BIM status report · {snap.Ts} UTC · proposal §4.6")
                .SetOverallPct(snap.CompliancePct);

            // Headline
            b.AddSection("Headline")
             .RAGBar(snap.CompliancePct, $"Tag / metadata compliance {snap.CompliancePct:F1}%")
             .Metric("Model-health score", $"{snap.HealthScore:F0}/100",
                     "compliance 40% · clash 25% · warnings 20% · stale 15%",
                     null)
             .Metric("Open clashes", snap.OpenClashes.ToString(),
                     prev != null ? $"burn-down {KutKpiEngine.Delta(snap.OpenClashes, prev.OpenClashes, "", invert: true)}" : "no prior snapshot");

            // Tag compliance by discipline
            if (cs?.ByDisc != null && cs.ByDisc.Count > 0)
            {
                var rows = cs.ByDisc.OrderBy(k => k.Key)
                    .Select(kv => new[] { kv.Key, kv.Value.Total.ToString(), kv.Value.Tagged.ToString(), $"{kv.Value.CompliancePct:F0}%" })
                    .ToList();
                b.AddSection("Tag compliance by discipline")
                 .Table(new[] { "Disc", "Total", "Tagged", "%" }, rows);
            }

            // Clash burn-down
            b.AddSection("Clash burn-down");
            if (snap.OpenClashBySeverity.Count > 0)
                b.Table(new[] { "Severity", "Open" },
                    snap.OpenClashBySeverity.OrderByDescending(k => k.Value)
                        .Select(kv => new[] { kv.Key, kv.Value.ToString() }).ToList());
            else
                b.Info("No open clashes in the latest STING clash run (or none run yet — see KUT Coordination Cycle).");

            // Other §4.6 KPIs
            b.AddSection("Other §4.6 KPIs")
             .Metric("Revision populated", $"{snap.RevisionPct:F1}%")
             .Metric("Sheet ISO 19650 compliance", $"{snap.SheetCompliancePct:F1}%")
             .Metric("Stale elements", snap.Stale.ToString())
             .Metric("Model warnings", snap.Warnings.ToString())
             // PM-8 — these three are longitudinal / multi-party (exchange calendar,
             // Bluebeam review sessions, site-change telemetry) and live server-side
             // per the audit's Revit-API line; the Revit dashboard points at the
             // source rather than fabricating a number.
             .Metric("Exchange punctuality", "Planscape (server)", "models published on time vs the exchange calendar — longitudinal, tracked server-side")
             .Metric("Review comment close-out", "ReviewComments_Import", "import the Bluebeam session summary to compute closed/total")
             .Metric("As-built capture currency", "Planscape (server)", "days lag between site change and model update — needs site telemetry");

            // PM-8 — real computed delivery + risk KPIs from the model-side engines.
            {
                var sectDel = b.AddSection("Delivery & risk (PM-8)");
                try
                {
                    var risks = StingTools.Commands.Delivery.RiskStore.Load(doc);
                    var rs = StingTools.Core.Delivery.RiskRegister.Summarise(risks, 5);
                    sectDel.Metric("Open risks", rs.OpenCount.ToString(),
                                   risks.Count == 0 ? "none raised — Risk_Raise against an element/zone" : null)
                           .Metric("Open red risks", rs.RedResidualCount.ToString())
                           .Metric("Avg residual (open)", rs.AverageResidualScore.ToString("F1"));
                }
                catch (Exception ex) { StingLog.Warn($"KPI risk roll-up: {ex.Message}"); }

                // Deliverable lifecycle count + a pointer to the computed MIDP drift
                // (Midp_DriftReport joins a plan CSV to the lifecycle and reports the
                // on-programme %; kept there to avoid re-parsing the plan here).
                try
                {
                    string projDir2 = System.IO.Path.GetDirectoryName(doc?.PathName ?? "");
                    string delivJson = string.IsNullOrEmpty(projDir2) ? null
                        : System.IO.Path.Combine(StingPaths.Meta(doc, "_BIM_COORD"), "deliverables.json");
                    int delivCount = 0;
                    if (!string.IsNullOrEmpty(delivJson) && System.IO.File.Exists(delivJson))
                    {
                        try { delivCount = Newtonsoft.Json.Linq.JArray.Parse(System.IO.File.ReadAllText(delivJson)).Count; }
                        catch { /* shape varies — count best-effort */ }
                    }
                    sectDel.Metric("Deliverables tracked", delivCount.ToString(),
                                   "run Midp_DriftReport with a plan CSV for the on-programme %");
                }
                catch (Exception ex) { StingLog.Warn($"KPI MIDP: {ex.Message}"); }
            }

            // Owner-system coverage (Fohlio / SpecLink / Niagara)
            b.AddSection("Owner-system coverage")
             .RAGBar(snap.FfeLinkedPct, $"Fohlio FF&E linked {snap.FfeLinkedPct:F1}% ({snap.FfeLinked}/{snap.FfeTotal})")
             .Metric("FF&E stale (model ≠ Fohlio)", snap.FfeStale.ToString(),
                     snap.FfeTotal == 0 ? "no FF&E in mapped categories" : null)
             .RAGBar(snap.SpecCoveragePct, $"SpecLink CSI coverage {snap.SpecCoveragePct:F1}% ({snap.SpecAssigned}/{snap.SpecTotal})")
             .Metric("BMS points (Niagara)", snap.BmsPoints.ToString(),
                     snap.BmsPoints == 0 ? "none tagged — populate ICT_HEALTHIOT_* on BMS controllers"
                                         : $"{snap.BmsNoEndpoint} without endpoint");

            // Trend
            b.AddSection("Trend (30-day)")
             .Metric("Compliance trend", trendDir, $"{(trendDelta >= 0 ? "+" : "")}{trendDelta:F1} pp over window");

            b.AddSection("Reports");
            if (html != null) b.Metric("HTML report", Path.GetFileName(html), html);
            if (csv != null) b.Metric("CSV", Path.GetFileName(csv), csv);

            b.Show();
            StingLog.Info($"KUT_KpiDashboard: compliance {snap.CompliancePct:F1}%, health {snap.HealthScore:F0}, open clashes {snap.OpenClashes}.");
            return Result.Succeeded;
        }

        private static string WriteCsv(Document doc, KutKpiSnapshot s, KutKpiSnapshot prev)
        {
            try
            {
                var rows = new List<string> { "KPI,Value,DeltaSinceLast" };
                void R(string k, string v, string d) => rows.Add($"\"{k}\",\"{v}\",\"{d}\"");
                R("Tag/metadata compliance %", $"{s.CompliancePct:F1}", KutKpiEngine.Delta(s.CompliancePct, prev?.CompliancePct, "pp"));
                R("Strict %", $"{s.StrictPct:F1}", KutKpiEngine.Delta(s.StrictPct, prev?.StrictPct, "pp"));
                R("Model-health score", $"{s.HealthScore:F0}", KutKpiEngine.Delta(s.HealthScore, prev?.HealthScore, ""));
                R("Open clashes", $"{s.OpenClashes}", KutKpiEngine.Delta(s.OpenClashes, prev?.OpenClashes, "", invert: true));
                R("Revision %", $"{s.RevisionPct:F1}", KutKpiEngine.Delta(s.RevisionPct, prev?.RevisionPct, "pp"));
                R("Sheet compliance %", $"{s.SheetCompliancePct:F1}", KutKpiEngine.Delta(s.SheetCompliancePct, prev?.SheetCompliancePct, "pp"));
                R("Stale elements", $"{s.Stale}", KutKpiEngine.Delta(s.Stale, prev?.Stale, "", invert: true));
                R("Model warnings", $"{s.Warnings}", KutKpiEngine.Delta(s.Warnings, prev?.Warnings, "", invert: true));
                R("Fohlio FF&E linked %", $"{s.FfeLinkedPct:F1}", KutKpiEngine.Delta(s.FfeLinkedPct, prev?.FfeLinkedPct, "pp"));
                R("FF&E stale", $"{s.FfeStale}", KutKpiEngine.Delta(s.FfeStale, prev?.FfeStale, "", invert: true));
                R("SpecLink CSI coverage %", $"{s.SpecCoveragePct:F1}", KutKpiEngine.Delta(s.SpecCoveragePct, prev?.SpecCoveragePct, "pp"));
                R("BMS points (Niagara)", $"{s.BmsPoints}", "");
                R("BMS points without endpoint", $"{s.BmsNoEndpoint}", "");
                foreach (var kv in s.OpenClashBySeverity.OrderByDescending(k => k.Value))
                    R($"Open clashes — {kv.Key}", kv.Value.ToString(), "");
                string path = OutputLocationHelper.GetOutputPath(doc, $"STING_KUT_KPI_{DateTime.UtcNow:yyyyMMddHHmmss}.csv");
                File.WriteAllLines(path, rows, Encoding.UTF8);
                return path;
            }
            catch (Exception ex) { StingLog.Warn("KUT KPI csv: " + ex.Message); return null; }
        }
    }
}
