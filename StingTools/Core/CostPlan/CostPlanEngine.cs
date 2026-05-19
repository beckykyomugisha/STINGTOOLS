// ══════════════════════════════════════════════════════════════════════════
//  CostPlanEngine.cs — Build / save / load / compare NRM1 cost plans.
//
//  Plans are persisted as JSON sidecars under
//  <project>/_bim_manager/cost_plans/. The header keys (Label, BuildingType,
//  GIFA, Date) form the filename, so two plans with different snapshot
//  labels coexist without collision.
//
//  Comparison against a live BOQ uses the per-discipline / per-NRM2
//  rollups produced by BOQCostManager; the engine maps NRM2 section codes
//  to NRM1 element codes via a fixed table (NRM2 14 = walls ⇒ NRM1 2.5
//  external + 2.7 internal — splits 60/40 by default for office,
//  configurable per building type later).
// ══════════════════════════════════════════════════════════════════════════
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using Newtonsoft.Json;
using StingTools.BIMManager;

namespace StingTools.Core.CostPlan
{
    internal static class CostPlanEngine
    {
        private static readonly JsonSerializerSettings _jsonSettings = new JsonSerializerSettings
        {
            Formatting = Formatting.Indented,
            NullValueHandling = NullValueHandling.Ignore,
            DateFormatString = "yyyy-MM-dd HH:mm:ss",
            Culture = CultureInfo.InvariantCulture
        };

        // ── Build ──────────────────────────────────────────────────────

        /// <summary>
        /// Create a new cost plan for a building type + GIFA target. The
        /// plan is populated with one line per benchmark; the caller can
        /// override individual lines before persistence.
        /// </summary>
        public static CostPlanDocument Create(Document doc, string buildingType,
            double gifaM2, string label = "Concept")
        {
            var registry = CostPlanRegistry.Get(doc);
            var lines = registry.LinesFor(buildingType);

            // Stamp the GIFA quantity onto each line.
            foreach (var line in lines) line.Quantity = gifaM2;

            return new CostPlanDocument
            {
                ProjectName = doc?.ProjectInformation?.Name ?? "",
                BuildingType = buildingType,
                GifaM2 = gifaM2,
                Label = label,
                Lines = lines
            };
        }

        // ── Persistence ────────────────────────────────────────────────

        public static string Save(Document doc, CostPlanDocument plan)
        {
            if (doc == null || plan == null) throw new ArgumentNullException();
            string dir = Path.Combine(BIMManagerEngine.GetBIMManagerDir(doc), "cost_plans");
            Directory.CreateDirectory(dir);
            string safe = MakeSafeFileName(plan.Label);
            string path = Path.Combine(dir,
                $"cost_plan_{safe}_{DateTime.Now:yyyyMMdd_HHmmss}.json");
            try
            {
                File.WriteAllText(path, JsonConvert.SerializeObject(plan, _jsonSettings));
                StingLog.Info($"Cost plan saved: {Path.GetFileName(path)} " +
                              $"({plan.Lines.Count} lines, headline GBP {plan.GrandTotalLikely:N0}).");
            }
            catch (Exception ex)
            {
                StingLog.Error("CostPlanEngine.Save", ex);
                throw;
            }
            return path;
        }

        public static CostPlanDocument Load(string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;
            try { return JsonConvert.DeserializeObject<CostPlanDocument>(File.ReadAllText(path), _jsonSettings); }
            catch (Exception ex)
            {
                StingLog.Warn($"CostPlanEngine.Load({Path.GetFileName(path)}): {ex.Message}");
                return null;
            }
        }

        public static List<string> ListPlans(Document doc)
        {
            try
            {
                string dir = Path.Combine(BIMManagerEngine.GetBIMManagerDir(doc), "cost_plans");
                if (!Directory.Exists(dir)) return new List<string>();
                return Directory.EnumerateFiles(dir, "cost_plan_*.json")
                    .OrderByDescending(File.GetLastWriteTimeUtc)
                    .ToList();
            }
            catch (Exception ex) { StingLog.Warn($"ListPlans: {ex.Message}"); return new List<string>(); }
        }

        // ── Comparison: plan vs live BOQ ────────────────────────────────

        public class CostPlanVariance
        {
            public string ElementCode = "";
            public string ElementName = "";
            public double PlannedLikely;
            public double Actual;
            public double Delta => Actual - PlannedLikely;
            public double DeltaPct => PlannedLikely > 0 ? Delta / PlannedLikely * 100 : 0;
            public string Status; // "Green" | "Amber" | "Red"
        }

        /// <summary>
        /// Compare a stored cost plan against current BOQ totals. NRM2
        /// section codes are mapped to NRM1 element codes via a fixed
        /// rule table. Variance &gt; ±10% = Amber, &gt; ±20% = Red.
        /// </summary>
        public static List<CostPlanVariance> Compare(CostPlanDocument plan,
            Dictionary<string, double> boqTotalsByNrm2)
        {
            var variances = new List<CostPlanVariance>();
            if (plan == null) return variances;
            if (boqTotalsByNrm2 == null) boqTotalsByNrm2 = new Dictionary<string, double>();

            // Aggregate BOQ totals into NRM1 buckets via the mapping table.
            var byNrm1 = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            foreach (var kv in boqTotalsByNrm2)
            {
                if (!Nrm2ToNrm1Map.TryGetValue(kv.Key, out var targets)) continue;
                foreach (var t in targets)
                {
                    double share = kv.Value * t.share;
                    byNrm1.TryGetValue(t.nrm1Code, out double cur);
                    byNrm1[t.nrm1Code] = cur + share;
                }
            }

            foreach (var line in plan.Lines)
            {
                byNrm1.TryGetValue(line.ElementCode, out double actual);
                var v = new CostPlanVariance
                {
                    ElementCode = line.ElementCode,
                    ElementName = line.ElementName,
                    PlannedLikely = line.TotalLikely,
                    Actual = Math.Round(actual, 0)
                };
                double absPct = Math.Abs(v.DeltaPct);
                v.Status = absPct > 20 ? "Red" : absPct > 10 ? "Amber" : "Green";
                variances.Add(v);
            }
            return variances;
        }

        /// <summary>
        /// NRM2 section → list of (NRM1 code, share). Shares sum to 1.0
        /// per source section. Conservative split — refine per building
        /// type later by reading project-override mappings from JSON.
        /// </summary>
        private static readonly Dictionary<string, List<(string nrm1Code, double share)>> Nrm2ToNrm1Map =
            new Dictionary<string, List<(string, double)>>(StringComparer.OrdinalIgnoreCase)
            {
                ["4"]  = new List<(string, double)> { ("1",   1.0) },     // Foundations → Substructure
                ["5"]  = new List<(string, double)> { ("2.2", 1.0) },     // Slabs → Upper floors
                ["14"] = new List<(string, double)> { ("2.5", 0.6),       // Walls split external / internal
                                                     ("2.7", 0.4) },
                ["15"] = new List<(string, double)> { ("2.1", 1.0) },     // Frame
                ["17"] = new List<(string, double)> { ("2.3", 0.5),       // Roof + curtain wall
                                                     ("2.5", 0.5) },
                ["19"] = new List<(string, double)> { ("3.3", 1.0) },     // Ceilings
                ["20"] = new List<(string, double)> { ("2.6", 0.5),       // Doors / windows / stairs
                                                     ("2.4", 0.3),
                                                     ("2.8", 0.2) },
                ["22"] = new List<(string, double)> { ("4",   1.0) },     // FF+E
                ["32"] = new List<(string, double)> { ("5.1", 0.5),       // Plumbing
                                                     ("5.3", 0.3),
                                                     ("5.4", 0.2) },
                ["33"] = new List<(string, double)> { ("5.5", 0.2),       // Mechanical
                                                     ("5.6", 0.5),
                                                     ("5.7", 0.3) },
                ["34"] = new List<(string, double)> { ("5.8", 1.0) },     // Electrical
                ["35"] = new List<(string, double)> { ("5.8", 1.0) },     // Lighting (electrical sub-system)
                ["36"] = new List<(string, double)> { ("5.11",1.0) },     // Fire / safety
                ["99"] = new List<(string, double)> { ("12",  1.0) }      // Other
            };

        private static string MakeSafeFileName(string s)
        {
            if (string.IsNullOrEmpty(s)) return "plan";
            var invalid = new HashSet<char>(Path.GetInvalidFileNameChars().Concat(new[] { ' ', '/', '\\' }));
            var sb = new System.Text.StringBuilder(s.Length);
            foreach (char c in s) sb.Append(invalid.Contains(c) ? '-' : c);
            return sb.ToString().Trim('-');
        }
    }
}
