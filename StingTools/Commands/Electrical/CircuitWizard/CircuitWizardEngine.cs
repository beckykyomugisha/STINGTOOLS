using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using StingTools.Commands.Electrical.CableSizer;
using StingTools.Commands.Electrical.FaultCurrent;
using StingTools.Commands.Electrical.VoltageDrop;

namespace StingTools.Commands.Electrical.CircuitWizard
{
    /// <summary>
    /// Lightweight POCO carrying just enough to bin-pack circuits without
    /// referencing Revit types directly. The wizard's RefreshUnconnectedElements
    /// builds these from FamilyInstance + ConnectorManager data.
    /// </summary>
    public class UnconnectedElement
    {
        /// <summary>Boxed Autodesk.Revit.DB.ElementId — kept as object so this engine
        /// stays free of Revit using-statements.</summary>
        public object Id            { get; set; }
        public string FamilyName    { get; set; } = "";
        public string Mark          { get; set; } = "";
        public string RoomName      { get; set; } = "";
        public double LoadVA        { get; set; }
        public int    RequiredPoles { get; set; } = 1;
        public double VoltageV      { get; set; } = 230.0;
        public string LoadClass     { get; set; } = "Other";
        public double X { get; set; }
        public double Y { get; set; }
        public double Z { get; set; }
    }

    public class ProposedCircuit
    {
        public string PanelName       { get; set; } = "";
        public string ProposedLabel   { get; set; } = "";
        public string LoadClass       { get; set; } = "Other";
        public string Phase           { get; set; } = "A";
        public double TotalLoadVA     { get; set; }
        public double UtilisationPct  { get; set; }
        public double VoltageV        { get; set; } = 230.0;
        public int    Poles           { get; set; } = 1;
        public double ProposedRatingA { get; set; }
        public double ProposedCsaMm2  { get; set; }
        public List<UnconnectedElement> Elements { get; set; } = new List<UnconnectedElement>();
        public bool   UserModified    { get; set; }
    }

    /// <summary>
    /// Pure circuit-grouping engine. Loads classification patterns from
    /// STING_DEMAND_FACTORS.json. Bin-packs by load class within compatible
    /// voltage / pole groups; assigns proposed phase via greedy least-loaded.
    /// </summary>
    public static class CircuitWizardEngine
    {
        private static JObject _demandFactors;
        private static readonly object _loadLock = new object();

        public static void InvalidateCache() { lock (_loadLock) _demandFactors = null; }

        private static JObject LoadDemandFactors()
        {
            lock (_loadLock)
            {
                if (_demandFactors != null) return _demandFactors;
                try
                {
                    string path = StingTools.Core.StingToolsApp.FindDataFile("STING_DEMAND_FACTORS.json");
                    if (!string.IsNullOrEmpty(path) && File.Exists(path))
                        _demandFactors = JObject.Parse(File.ReadAllText(path));
                }
                catch (Exception ex)
                {
                    StingTools.Core.StingLog.Warn($"CircuitWizardEngine.LoadDemandFactors: {ex.Message}");
                }
                _demandFactors ??= new JObject();
                return _demandFactors;
            }
        }

        /// <summary>
        /// Map a family/category name to a load class via the patterns table.
        /// Returns "Other" when no pattern matches.
        /// </summary>
        public static string ClassifyLoad(string familyName, string categoryName)
        {
            string blob = ($"{familyName} {categoryName}" ?? "").ToLowerInvariant();
            if (string.IsNullOrEmpty(blob)) return "Other";

            try
            {
                var patterns = LoadDemandFactors()["classificationPatterns"] as JObject;
                if (patterns == null) return DefaultClassify(blob);
                foreach (var prop in patterns.Properties())
                {
                    var arr = prop.Value as JArray;
                    if (arr == null) continue;
                    foreach (var p in arr.Select(t => (t.ToString() ?? "").ToLowerInvariant()))
                    {
                        if (!string.IsNullOrEmpty(p) && blob.Contains(p))
                            return prop.Name;
                    }
                }
            }
            catch (Exception ex) { StingTools.Core.StingLog.Warn($"ClassifyLoad: {ex.Message}"); }
            return DefaultClassify(blob);
        }

        private static string DefaultClassify(string blob)
        {
            if (blob.Contains("emerg") || blob.Contains("exit")) return "Emergency";
            if (blob.Contains("light") || blob.Contains("luminaire")) return "Lighting";
            if (blob.Contains("socket") || blob.Contains("receptacle")) return "SmallPower";
            if (blob.Contains("hvac") || blob.Contains("fcu") || blob.Contains("ahu")) return "HVAC";
            return "Other";
        }

        /// <summary>
        /// Bin-pack elements into proposed circuits.
        ///
        /// Algorithm:
        ///   1. Group by (voltage, poles) — only compatible connectors share circuits
        ///   2. Within each group, sub-group by load class
        ///   3. Greedy bin-pack: add to current circuit until total load exceeds
        ///      maxLoadPct × proposed-rating; spawn new circuit when full
        ///   4. Greedy least-loaded phase assignment across the resulting circuits
        ///   5. Cable-size each circuit via Phase 177 CableSizerEngine
        /// </summary>
        public static List<ProposedCircuit> ProposeCircuits(IEnumerable<UnconnectedElement> elements,
            string targetPanelName, double maxLoadPct, string standard, WireTableSet wireTables)
        {
            var proposals = new List<ProposedCircuit>();
            if (elements == null) return proposals;
            double cap = maxLoadPct <= 0 ? 0.8 : Math.Min(1.0, maxLoadPct);

            var byCompat = elements
                .Where(e => e != null)
                .GroupBy(e => ($"{e.VoltageV:0}V", e.RequiredPoles));

            foreach (var compatGrp in byCompat)
            {
                var byClass = compatGrp.GroupBy(e => string.IsNullOrEmpty(e.LoadClass) ? "Other" : e.LoadClass);
                foreach (var classGrp in byClass)
                {
                    var ordered = classGrp.OrderByDescending(e => e.LoadVA).ToList();
                    ProposedCircuit cur = null;
                    int seq = 1;
                    foreach (var el in ordered)
                    {
                        if (cur == null || WouldExceed(cur, el, cap, standard))
                        {
                            cur = NewCircuit(targetPanelName, classGrp.Key,
                                compatGrp.First().VoltageV, compatGrp.First().RequiredPoles, seq++);
                            proposals.Add(cur);
                        }
                        cur.Elements.Add(el);
                        cur.TotalLoadVA += el.LoadVA;
                        RecalculateCircuit(cur, standard, wireTables);
                    }
                }
            }

            // Greedy least-loaded phase across the proposals.
            BalancePhases(proposals);
            return proposals;
        }

        private static bool WouldExceed(ProposedCircuit cur, UnconnectedElement el,
            double maxLoadPct, string standard)
        {
            double prospectiveVA = cur.TotalLoadVA + el.LoadVA;
            double iA = prospectiveVA / Math.Max(1.0, cur.VoltageV);
            int trial = string.Equals(standard, "NEC", StringComparison.OrdinalIgnoreCase)
                ? VoltageDropEngine.NextStandardBreakerSizeNEC(iA)
                : VoltageDropEngine.NextStandardBreakerSizeBS(iA);
            double allowed = trial * maxLoadPct * cur.VoltageV;
            return prospectiveVA > allowed;
        }

        private static ProposedCircuit NewCircuit(string panelName, string loadClass,
            double voltageV, int poles, int seq)
        {
            return new ProposedCircuit
            {
                PanelName     = panelName,
                ProposedLabel = $"{loadClass} {seq:00}",
                LoadClass     = loadClass,
                Phase         = "A",
                VoltageV      = voltageV,
                Poles         = poles
            };
        }

        public static void RecalculateCircuit(ProposedCircuit circuit, string standard, WireTableSet wireTables)
        {
            if (circuit == null) return;
            circuit.TotalLoadVA = circuit.Elements.Sum(e => e.LoadVA);
            double iA = circuit.TotalLoadVA / Math.Max(1.0, circuit.VoltageV);
            circuit.ProposedRatingA = string.Equals(standard, "NEC", StringComparison.OrdinalIgnoreCase)
                ? VoltageDropEngine.NextStandardBreakerSizeNEC(iA)
                : VoltageDropEngine.NextStandardBreakerSizeBS(iA);
            circuit.UtilisationPct = circuit.ProposedRatingA > 0
                ? (iA / circuit.ProposedRatingA) * 100.0
                : 0;
            // Use a 25 m default run for sizing — real runs aren't known until placed.
            var sized = CableSizerEngine.Calculate(new CableSizeInput
            {
                LoadKW = circuit.TotalLoadVA / 1000.0,
                VoltageV = circuit.VoltageV,
                Phases = circuit.Poles >= 3 ? 3 : 1,
                PowerFactor = 0.85,
                LengthM = 25.0,
                InstallMethod = "C",
                Material = "Cu",
                Insulation = "XLPE90",
                VDLimitPct = 3.0,
                Standard = standard
            });
            circuit.ProposedCsaMm2 = sized.RecommendedCsaMm2;
        }

        private static void BalancePhases(List<ProposedCircuit> proposals)
        {
            if (proposals == null || proposals.Count == 0) return;
            string[] phases = { "A", "B", "C" };
            var bucket = new Dictionary<string, double> { ["A"] = 0, ["B"] = 0, ["C"] = 0 };
            foreach (var p in proposals.OrderByDescending(p => p.TotalLoadVA))
            {
                if (p.Poles >= 3) { p.Phase = "ABC"; continue; }
                string min = bucket.OrderBy(kv => kv.Value).First().Key;
                p.Phase = min;
                bucket[min] += p.TotalLoadVA;
            }
        }
    }
}
