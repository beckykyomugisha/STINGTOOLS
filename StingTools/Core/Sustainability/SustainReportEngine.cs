// StingTools — SustainReportEngine (SUS-4).
//
// A shareable HTML EDGE / whole-life sustainability report, mirroring
// HealthDashboardEngine.ExportHtml. The dashboard / EDGE export previously dead-ended
// at an XLSX dump; this turns "report -> certify" into a one-click formatted document
// the assessor sends to the client and attaches to the EDGE / LEED submission.
//
// Honesty-first: a not-computed gate (location/use unset, no fixture data) is shown as
// "not computed", never a fabricated pass; EDGE materials is embodied ENERGY, delegated
// to the EDGE App; every STING figure is labelled indicative (the App owns the certified
// number). Includes the WBLCA A1-A3 prerequisite section (LEED v5 MR) from the
// already-computed carbon.
//
// Revit-facing only for the file write + OutputLocationHelper; the content is built
// from the run result.

using System;
using System.IO;
using System.Linq;
using System.Text;
using Autodesk.Revit.DB;
using StingTools.Core;

namespace StingTools.Core.Sustainability
{
    public static class SustainReportEngine
    {
        private const string Css =
            "body{font-family:Segoe UI,Arial,sans-serif;margin:24px;color:#222;max-width:980px}"
            + "h1{margin-bottom:2px}h2{margin-top:28px;border-bottom:2px solid #eee;padding-bottom:4px}"
            + ".sub{color:#666;margin-bottom:12px}"
            + ".band{display:inline-block;padding:8px 14px;border-radius:6px;font-weight:600;font-size:20px}"
            + ".ok{background:#e8f5e9;color:#2e7d32}.warn{background:#fff3e0;color:#ef6c00}.bad{background:#ffebee;color:#c62828}"
            + "table{border-collapse:collapse;width:100%;margin-top:10px}"
            + "th,td{border:1px solid #ddd;padding:6px 10px;text-align:left}th{background:#f5f5f5}"
            + ".pass{color:#2e7d32;font-weight:600}.fail{color:#c62828;font-weight:600}.na{color:#999}"
            + ".note{background:#fffde7;border-left:4px solid #fbc02d;padding:8px 12px;margin:10px 0}"
            + ".cards{display:flex;gap:14px;flex-wrap:wrap;margin-top:10px}"
            + ".card{flex:1;min-width:180px;border:1px solid #ddd;border-radius:8px;padding:12px}"
            + ".card .v{font-size:26px;font-weight:600}.card .l{color:#666;font-size:13px}";

        /// <summary>Build + write the HTML report. Returns the path. <paramref name="title"/>
        /// lets the LEED command re-skin it for the WBLCA prerequisite.</summary>
        public static string ExportHtml(Document doc, SustainabilityRunResult res, SustainProjectSetup setup,
            string title = "STING Sustainability — EDGE / whole-life report")
        {
            var sb = new StringBuilder();
            bool ready = res?.Readiness?.Ready ?? true;

            sb.Append("<!DOCTYPE html><html><head><meta charset='utf-8'><title>").Append(E(title))
              .Append("</title><style>").Append(Css).Append("</style></head><body>");
            sb.Append("<h1>").Append(E(title)).Append("</h1>");
            sb.Append("<div class='sub'>").Append(E(setup?.DominantBuildingUse ?? "")).Append(" &middot; ")
              .Append(E(setup?.Country ?? "")).Append(" &middot; zone ").Append(E(setup?.ClimateZone ?? "?"))
              .Append(" &middot; generated ").Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm")).Append("</div>");

            // Readiness honesty banner.
            if (res?.Readiness != null && !string.IsNullOrEmpty(res.Readiness.Banner))
                sb.Append("<div class='note'>").Append(ready ? "" : "<b>Generic proxy - not your project.</b> ")
                  .Append(E(res.Readiness.Banner)).Append("</div>");

            // EDGE level + savings cards.
            var edge = res?.Schemes?.FirstOrDefault(s => s.SchemeId == "EDGE");
            sb.Append("<div class='cards'>");
            Card(sb, "EDGE level (STING-determinable)", edge?.AchievedLevel ?? "-",
                edge != null && edge.Passed ? "ok" : "warn");
            Card(sb, "Energy savings", GatePct(ready && (res?.Energy?.Computed ?? false), res?.Energy?.EnergySavingsPct ?? 0), "");
            Card(sb, "Water savings", GatePct(ready && (res?.Water?.Computed ?? false), res?.Water?.WaterSavingsInclAltPct ?? 0), "");
            Card(sb, "Embodied carbon", $"{res?.Materials?.CarbonIntensityKgM2 ?? 0:0} kgCO2e/m&sup2;", "");
            sb.Append("</div>");

            // Energy.
            sb.Append("<h2>Operational energy (indicative)</h2><table>");
            Tr(sb, "Design EUI", $"{res?.Energy?.DesignEuiKwhM2Yr ?? 0:0.0} kWh/m&sup2;&middot;yr");
            Tr(sb, "Baseline EUI", $"{res?.Energy?.BaselineEuiKwhM2Yr ?? 0:0.0} kWh/m&sup2;&middot;yr");
            Tr(sb, "Energy savings vs baseline", GatePct(ready && (res?.Energy?.Computed ?? false), res?.Energy?.EnergySavingsPct ?? 0));
            Tr(sb, "Operational carbon", $"{res?.Energy?.OperationalCarbonKgYr ?? 0:0} kgCO2e/yr");
            sb.Append("</table>");

            // Water.
            sb.Append("<h2>Water (indicative)</h2><table>");
            Tr(sb, "Design", $"{res?.Water?.DesignLPersonDay ?? 0:0.0} L/person&middot;day");
            Tr(sb, "Baseline", $"{res?.Water?.BaselineLPersonDay ?? 0:0.0} L/person&middot;day");
            Tr(sb, "Water savings (incl. alt. water)", GatePct(ready && (res?.Water?.Computed ?? false), res?.Water?.WaterSavingsInclAltPct ?? 0));
            sb.Append("</table>");

            // Materials + whole-life carbon + WBLCA A1-A3 prerequisite.
            sb.Append("<h2>Embodied carbon &amp; whole-life (WBLCA A1-A3 prerequisite)</h2>");
            sb.Append("<div class='note'>EDGE materials savings is embodied <b>ENERGY (MJ)</b>, self-assessed in the EDGE App (delegated). The carbon figures below are STING-indicative whole-life-carbon (A1-A3 GWP), for the LEED v5 MR / RICS WBLCA prerequisite - not the EDGE materials gate.</div>");
            sb.Append("<table>");
            Tr(sb, "Embodied carbon intensity (A1-A3)", $"{res?.Materials?.CarbonIntensityKgM2 ?? 0:0.0} kgCO2e/m&sup2;");
            Tr(sb, "Embodied carbon total (A1-A3)", $"{res?.Materials?.TotalCarbonKg ?? 0:0} kgCO2e");
            Tr(sb, "Biogenic credit included", $"{res?.Materials?.TotalBiogenicCarbonKg ?? 0:0} kgCO2e");
            Tr(sb, "Embodied energy intensity", $"{res?.Materials?.EnergyIntensityMjM2 ?? 0:0} MJ/m&sup2; (EDGE materials track)");
            Tr(sb, "Carbon take-off coverage", E(res?.Materials?.CoverageSummary ?? "-"));
            if (res?.WholeLife != null)
                Tr(sb, $"Whole-life carbon ({res.WholeLife.StudyPeriodYears}-yr: A1-A3 + operational)",
                    $"{res.WholeLife.EmbodiedA1A3Kg + res.WholeLife.OperationalKgPerYr * res.WholeLife.StudyPeriodYears:0} kgCO2e");
            sb.Append("</table>");
            if (res?.Materials?.DominantHotspotImplausible == true || res?.Materials?.CarbonIsIndicative == true)
                sb.Append("<div class='note'><b>Indicative - review quantities.</b> ")
                  .Append(E(res.Materials.CarbonIsIndicative ? "No carbon-stamped lines; figures are database/indicative. "
                                                              : $"'{res.Materials.DominantHotspotMaterial}' is {res.Materials.DominantHotspotSharePct:0}% of the total - likely a quantity/factor error. "))
                  .Append("Map verified EPDs in _BIM_COORD/boq_epd_map.json to strengthen the evidence.</div>");
            if (res?.Materials?.Hotspots?.Count > 0)
            {
                sb.Append("<table><thead><tr><th>Carbon hotspot</th><th>kgCO2e</th><th>%</th></tr></thead><tbody>");
                foreach (var h in res.Materials.Hotspots.Take(10))
                    sb.Append("<tr><td>").Append(E(h.Material)).Append("</td><td>").Append($"{h.CarbonKg:0}")
                      .Append("</td><td>").Append($"{h.SharePct:0}%").Append("</td></tr>");
                sb.Append("</tbody></table>");
            }

            // Scheme gates.
            foreach (var sc in res?.Schemes ?? Enumerable.Empty<SchemeResult>())
            {
                sb.Append("<h2>").Append(E(sc.SchemeName)).Append(" gates (target ").Append(E(sc.TargetLevel))
                  .Append("; achieved ").Append(E(sc.AchievedLevel)).Append(")</h2><table><thead>")
                  .Append("<tr><th>Gate</th><th>Result</th><th>Value</th></tr></thead><tbody>");
                foreach (var g in sc.Gates)
                {
                    string cls = g.Delegated ? "na" : !g.Computed ? "na" : g.Passed ? "pass" : "fail";
                    string r = g.Delegated ? "-> EDGE App" : !g.Computed ? "not computed" : g.Passed ? "PASS" : "below";
                    sb.Append("<tr><td>").Append(E(g.Label)).Append("</td><td class='").Append(cls).Append("'>")
                      .Append(E(r)).Append("</td><td>").Append(g.Computed && !g.Delegated ? $"{g.IndicativeValue:0.0}{E(g.Unit)}" : "-")
                      .Append("</td></tr>");
                }
                sb.Append("</tbody></table>");
            }

            // Design measures (SUS-1).
            var m = res?.Measures;
            if (m != null)
            {
                sb.Append("<h2>Design measures (EDGE App Design-tab inputs)</h2><table>");
                Tr(sb, "WWR overall", $"{m.WwrOverall * 100:0}%");
                Tr(sb, "Wall / Roof / Window U-value", $"{m.WallUvalueWm2K:0.00} / {m.RoofUvalueWm2K:0.00} / {m.WindowUvalueWm2K:0.00} W/m&sup2;K");
                Tr(sb, "Window SHGC", $"{m.WindowShgc:0.00}");
                Tr(sb, "Lighting power density", $"{m.LightingWPerM2:0.0} W/m&sup2;");
                Tr(sb, "AC cooling COP", $"{m.CoolingCop:0.0}");
                sb.Append("</table>");
            }

            // Honesty caveats.
            sb.Append("<h2>How to read this report</h2><div class='note'>");
            sb.Append("All figures are <b>STING-indicative</b> - they pre-compute the savings and build the business case. ");
            sb.Append("The <b>EDGE App owns the certified number</b> (energy / water / embodied-energy %), the baseline, and the audit. ");
            sb.Append("A gate shown <i>not computed</i> means the model lacks the data to evaluate it (e.g. location/use unset, no fixture flows) - it can never count as a pass. ");
            sb.Append("Run the improve loop (Target Seeker / LCC benefit) for the least-cost route to the next level.</div>");

            sb.Append("</body></html>");

            string path = OutputLocationHelper.GetTimestampedPath(doc, "STING_Sustainability_Report", ".html");
            File.WriteAllText(path, sb.ToString());
            StingLog.Info($"SustainReportEngine: wrote {path}");
            return path;
        }

        private static void Card(StringBuilder sb, string label, string value, string cls)
            => sb.Append("<div class='card'><div class='v ").Append(cls).Append("'>").Append(value)
                 .Append("</div><div class='l'>").Append(E(label)).Append("</div></div>");

        private static void Tr(StringBuilder sb, string k, string v)
            => sb.Append("<tr><td>").Append(E(k)).Append("</td><td>").Append(v).Append("</td></tr>");

        private static string GatePct(bool computed, double pct)
            => computed ? $"{pct:0.0}%" : "<span class='na'>not computed - indicative</span>";

        private static string E(string s) => (s ?? string.Empty)
            .Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
    }
}
