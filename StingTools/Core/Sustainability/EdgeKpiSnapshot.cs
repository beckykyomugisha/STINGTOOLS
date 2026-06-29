// StingTools — EDGE KPI snapshot (Phase 195, spec §11).
//
// Mirrors KutKpiSnapshot (JSONL POCO with computed % props) for trend / burn-down.
// Persisted to <project>/_BIM_COORD/sustainability/edge_kpi_log.jsonl.
// Pure POCO — no Revit dependency.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;

namespace StingTools.Core.Sustainability
{
    public sealed class EdgeKpiSnapshot
    {
        public string Ts { get; set; } = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ");

        // Design intensities + savings %.
        public double EnergyEuiKwhM2Yr   { get; set; }
        public double EnergySavingsPct    { get; set; }
        public double WaterLPersonDay     { get; set; }
        public double WaterSavingsPct     { get; set; }
        public double MaterialCarbonKgM2  { get; set; }
        public double MaterialEnergyMjM2  { get; set; }
        public double MaterialEnergySavingsPct { get; set; }
        public double GwpReductionPct      { get; set; }

        public string EdgeLevel  { get; set; } = "None";
        public bool   EdgePassed { get; set; }

        public double OperationalCarbonKgYr { get; set; }
        /// <summary>WS H4 — whole-life carbon intensity (embodied A1–A3 + operational
        /// over the study period), kgCO₂e/m².</summary>
        public double WholeLifeCarbonKgM2 { get; set; }
        public int    StudyPeriodYears { get; set; }
        public int    Occupancy   { get; set; }
        public double FloorAreaM2 { get; set; }
        public string SupplyMode  { get; set; } = "grid_tied";
        public string ProxyPath   { get; set; } = "";
        public string Country     { get; set; } = "";
        public string ClimateZone { get; set; } = "";

        /// <summary>WS H5 — the water % the snapshot must record: the SAME inclusive
        /// metric (fixture efficiency + alternative water) the EDGE water gate uses
        /// (AnnualWaterMetricProvider), so the persisted trend agrees with the
        /// on-screen pass/fail. Falls back to the fixture-only % when no result.</summary>
        public static double GateWaterPct(WaterEstimateResult w)
            => w == null ? 0 : w.WaterSavingsInclAltPct;

        // ── Persistence ──────────────────────────────────────────────────

        public static string Dir(string projectDir)
        {
            if (string.IsNullOrEmpty(projectDir)) return null;
            string p = Path.Combine(projectDir, "_BIM_COORD", "sustainability");
            Directory.CreateDirectory(p);
            return p;
        }

        public static void Append(string projectDir, EdgeKpiSnapshot snap)
        {
            try
            {
                string dir = Dir(projectDir);
                if (dir == null || snap == null) return;
                File.AppendAllText(Path.Combine(dir, "edge_kpi_log.jsonl"),
                    JsonConvert.SerializeObject(snap) + Environment.NewLine);
            }
            catch { /* non-fatal — KPI logging never blocks the dashboard */ }
        }

        public static EdgeKpiSnapshot LoadPrevious(string projectDir)
        {
            try
            {
                string dir = Dir(projectDir);
                string log = dir != null ? Path.Combine(dir, "edge_kpi_log.jsonl") : null;
                if (log == null || !File.Exists(log)) return null;
                var last = File.ReadLines(log).LastOrDefault(l => !string.IsNullOrWhiteSpace(l));
                return last != null ? JsonConvert.DeserializeObject<EdgeKpiSnapshot>(last) : null;
            }
            catch { return null; }
        }
    }
}
