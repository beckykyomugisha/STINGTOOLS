// PumpSelector — pump duty point calculation and catalogue matching.
// Phase 179c.
//
// CalculateDutyPoint:
//   • Static head  = highest fixture level Z − pump inlet Z (metres)
//   • Friction head = sum of ResistanceKpa along critical path / ρg
//   • Flow          = design flow Qd from WaterSupplySizer for the system
//   • +20% safety margin on total head
//
// SelectPump:
//   • Loads STING_PUMP_CATALOGUE.json from the data directory
//   • Matches pumps where RatedFlow ≥ duty.Flow AND RatedHead ≥ duty.Head
//   • Ranks by efficiency desc, then by rated size asc (closest oversize)
//   • Returns top 3 candidates + best match
//
// WritePumpData writes PLM_PUMP_DUTY_HEAD_M, PLM_PUMP_DUTY_FLOW_LPS,
// PLM_PUMP_MODEL_TXT onto the pump element. Caller owns the transaction.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Plumbing;
using Newtonsoft.Json;
using StingTools.Core;

namespace StingTools.Core.Plumbing
{
    // ──────────────────────────────────────────────────────────────────────
    // Data model
    // ──────────────────────────────────────────────────────────────────────

    public class PumpDutyPoint
    {
        public double FlowLps       { get; set; }
        public double HeadM         { get; set; }
        public double EfficiencyPct { get; set; }
        public string SystemName    { get; set; } = "";
        public double StaticHeadM   { get; set; }
        public double FrictionHeadM { get; set; }
    }

    public class PumpMatch
    {
        public string Manufacturer          { get; set; } = "";
        public string Model                 { get; set; } = "";
        public string Series                { get; set; } = "";
        public double RatedFlowLps          { get; set; }
        public double RatedHeadM            { get; set; }
        public double PowerKw               { get; set; }
        public double EfficiencyPct         { get; set; }
        public bool   DutyPointWithinCurve  { get; set; }
        public double MarginPct             { get; set; }
        public string CatalogueRef          { get; set; } = "";
        public string Notes                 { get; set; } = "";
    }

    public class PumpSelectionResult
    {
        public PumpDutyPoint   Duty              { get; set; }
        public List<PumpMatch> Candidates        { get; } = new List<PumpMatch>();
        public PumpMatch       BestMatch         { get; set; }
        public List<string>    Warnings          { get; } = new List<string>();
        public double          TotalIndexHeadM   { get; set; }
        public double          StaticHeadM       { get; set; }
        public double          FrictionHeadM     { get; set; }
    }

    // Internal JSON catalogue shape
    internal class PumpCatalogueFile
    {
        [JsonProperty("pumps")]
        public List<PumpCatalogueEntry> Pumps { get; set; } = new List<PumpCatalogueEntry>();
    }

    internal class PumpCatalogueEntry
    {
        [JsonProperty("manufacturer")]  public string Manufacturer   { get; set; } = "";
        [JsonProperty("model")]         public string Model          { get; set; } = "";
        [JsonProperty("series")]        public string Series         { get; set; } = "";
        [JsonProperty("ratedFlowLps")]  public double RatedFlowLps   { get; set; }
        [JsonProperty("ratedHeadM")]    public double RatedHeadM     { get; set; }
        [JsonProperty("powerKw")]       public double PowerKw        { get; set; }
        [JsonProperty("efficiencyPct")] public double EfficiencyPct  { get; set; }
        [JsonProperty("catalogueRef")]  public string CatalogueRef   { get; set; } = "";
        [JsonProperty("notes")]         public string Notes          { get; set; } = "";
    }

    // ──────────────────────────────────────────────────────────────────────
    // Engine
    // ──────────────────────────────────────────────────────────────────────

    public static class PumpSelector
    {
        private const double FtToM  = 0.3048;
        private const double RhoG   = 9.807;   // kPa per metre of water head
        private const double Margin = 1.20;    // +20% safety margin on total head

        /// <summary>
        /// Full duty point calculation using PipeNetwork + WaterSupplySizer output.
        /// </summary>
        public static PumpDutyPoint CalculateDutyPoint(
            Document doc, PipeNetwork network, string systemName)
        {
            if (doc == null || network == null)
                return new PumpDutyPoint { SystemName = systemName };

            try
            {
                // Static head: highest fixture node Z − lowest node (pump inlet proxy)
                double maxZFt  = network.RootNodes.Max(n => n.Position?.Z ?? 0);
                double minZFt  = network.Nodes.Min(n  => n.Position?.Z ?? 0);
                double staticM = (maxZFt - minZFt) * FtToM;

                // Friction head: sum of resistance along critical path, converted from kPa → m
                var critPath      = PipeNetworkBuilder.FindCriticalPath(network);
                double frictionKpa = critPath.Sum(e => e.ResistanceKpa);
                double frictionM   = frictionKpa / RhoG;

                // Design flow from supply sizer for this system
                double flowLps = EstimateDesignFlow(doc, systemName);

                double totalHeadM = (staticM + frictionM) * Margin;

                return new PumpDutyPoint
                {
                    SystemName    = systemName,
                    StaticHeadM   = staticM,
                    FrictionHeadM = frictionM,
                    HeadM         = totalHeadM,
                    FlowLps       = flowLps
                };
            }
            catch (Exception ex)
            {
                StingLog.Error("PumpSelector.CalculateDutyPoint", ex);
                return new PumpDutyPoint { SystemName = systemName };
            }
        }

        /// <summary>
        /// Simplified duty calculation from pre-computed head values.
        /// </summary>
        public static PumpDutyPoint CalculateDutyPointSimple(
            double staticHeadM, double frictionHeadM, double flowLps)
        {
            double total = (staticHeadM + frictionHeadM) * Margin;
            return new PumpDutyPoint
            {
                StaticHeadM   = staticHeadM,
                FrictionHeadM = frictionHeadM,
                HeadM         = total,
                FlowLps       = flowLps
            };
        }

        /// <summary>
        /// Match duty point against the pump catalogue.
        /// cataloguePath: optional override path; defaults to STING_PUMP_CATALOGUE.json.
        /// </summary>
        public static PumpSelectionResult SelectPump(
            PumpDutyPoint duty, string cataloguePath = null)
        {
            var result = new PumpSelectionResult
            {
                Duty          = duty,
                StaticHeadM   = duty?.StaticHeadM   ?? 0,
                FrictionHeadM = duty?.FrictionHeadM ?? 0,
                TotalIndexHeadM = duty?.HeadM       ?? 0
            };

            if (duty == null) { result.Warnings.Add("Null duty point."); return result; }

            var entries = LoadCatalogue(cataloguePath, result.Warnings);
            if (entries.Count == 0)
            {
                result.Warnings.Add("Pump catalogue empty or not found — using synthetic fallback.");
                entries = SyntheticCatalogue(duty);
            }

            // Filter: rated flow >= duty AND rated head >= duty
            var candidates = entries
                .Where(e => e.RatedFlowLps >= duty.FlowLps && e.RatedHeadM >= duty.HeadM)
                .OrderByDescending(e => e.EfficiencyPct)
                .ThenBy(e => e.RatedFlowLps)
                .Take(3)
                .Select(e => ToMatch(e, duty))
                .ToList();

            result.Candidates.AddRange(candidates);
            result.BestMatch = candidates.FirstOrDefault();

            if (result.BestMatch == null)
                result.Warnings.Add($"No pump found for Q={duty.FlowLps:F2} L/s, H={duty.HeadM:F1} m — " +
                                    $"check catalogue or review system sizing.");

            return result;
        }

        /// <summary>
        /// Write pump selection data back to a pump element parameter set.
        /// MUST be called within an active Transaction.
        /// </summary>
        public static bool WritePumpData(Document doc, ElementId pumpId,
            PumpMatch match, PumpDutyPoint duty)
        {
            if (doc == null || pumpId == null || match == null || duty == null) return false;
            try
            {
                var el = doc.GetElement(pumpId);
                if (el == null) return false;

                bool ok = true;
                ok &= TryWriteDouble(el, ParamRegistry.PLM_PUMP_DUTY_HEAD_M,   duty.HeadM);
                ok &= TryWriteDouble(el, ParamRegistry.PLM_PUMP_DUTY_FLOW_LPS, duty.FlowLps);
                ok &= TryWriteString(el, ParamRegistry.PLM_PUMP_MODEL,
                    $"{match.Manufacturer} {match.Model}".Trim());
                return ok;
            }
            catch (Exception ex)
            {
                StingLog.Error($"WritePumpData {pumpId.Value}", ex);
                return false;
            }
        }

        // ──────────────────────────────────────────────────────────────────
        // Private helpers
        // ──────────────────────────────────────────────────────────────────

        private static double EstimateDesignFlow(Document doc, string systemName)
        {
            try
            {
                // The pump duty is the flow AT THE PUMP, not the max flow on any
                // pipe in the system. We pick the system's largest-diameter pipe
                // (proxy for the main/index leg the pump feeds) and read its
                // flow; falling back to the system max only if no pipe carries
                // a flow value at all. This avoids over-estimating duty on
                // heavily-branched networks where Revit may have computed flow
                // on individual fixture runouts but not on the main.
                double mainFlowLps = 0;
                double maxFlowLps  = 0;
                double mainDiaFt   = 0;
                var pipes = new FilteredElementCollector(doc)
                    .OfClass(typeof(Pipe))
                    .WhereElementIsNotElementType()
                    .Cast<Pipe>()
                    .Where(p =>
                    {
                        if (string.IsNullOrWhiteSpace(systemName)) return true;
                        return (p.MEPSystem?.Name ?? "").IndexOf(systemName,
                            StringComparison.OrdinalIgnoreCase) >= 0;
                    });

                foreach (var p in pipes)
                {
                    try
                    {
                        var flowParam = p.get_Parameter(BuiltInParameter.RBS_PIPE_FLOW_PARAM);
                        if (flowParam == null || !flowParam.HasValue) continue;
                        double flowFt3s = flowParam.AsDouble();
                        double flowLps  = flowFt3s * 28.3168; // ft³/s → L/s
                        if (flowLps > maxFlowLps) maxFlowLps = flowLps;
                        if (p.Diameter > mainDiaFt)
                        {
                            mainDiaFt = p.Diameter;
                            mainFlowLps = flowLps;
                        }
                    }
                    catch { }
                }

                double picked = mainFlowLps > 0 ? mainFlowLps : maxFlowLps;
                return Math.Max(picked, 0.1);
            }
            catch { return 0.5; }
        }

        private static List<PumpCatalogueEntry> LoadCatalogue(string path, List<string> warnings)
        {
            try
            {
                string filePath = path;
                if (string.IsNullOrWhiteSpace(filePath))
                    filePath = StingToolsApp.FindDataFile("STING_PUMP_CATALOGUE.json");

                if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
                    return new List<PumpCatalogueEntry>();

                string json = File.ReadAllText(filePath);
                var catalogue = JsonConvert.DeserializeObject<PumpCatalogueFile>(json);
                return catalogue?.Pumps ?? new List<PumpCatalogueEntry>();
            }
            catch (Exception ex)
            {
                warnings?.Add($"PumpSelector.LoadCatalogue: {ex.Message}");
                return new List<PumpCatalogueEntry>();
            }
        }

        /// <summary>
        /// Synthetic catalogue used when the JSON file is absent.
        /// Provides representative entries across the duty range for the
        /// designer to review — not actual products.
        /// </summary>
        private static List<PumpCatalogueEntry> SyntheticCatalogue(PumpDutyPoint duty)
        {
            double q = duty.FlowLps;
            double h = duty.HeadM;
            return new List<PumpCatalogueEntry>
            {
                new PumpCatalogueEntry
                {
                    Manufacturer = "STING Placeholder",
                    Model        = "PMP-S",
                    Series       = "Standard",
                    RatedFlowLps = q * 1.10,
                    RatedHeadM   = h * 1.10,
                    PowerKw      = q * h * RhoG / (0.65 * 1000.0),
                    EfficiencyPct= 65,
                    CatalogueRef = "VERIFY IN CATALOGUE",
                    Notes        = "Synthetic entry — replace with real pump selection"
                },
                new PumpCatalogueEntry
                {
                    Manufacturer = "STING Placeholder",
                    Model        = "PMP-M",
                    Series       = "Standard",
                    RatedFlowLps = q * 1.25,
                    RatedHeadM   = h * 1.20,
                    PowerKw      = q * h * RhoG / (0.70 * 1000.0),
                    EfficiencyPct= 70,
                    CatalogueRef = "VERIFY IN CATALOGUE",
                    Notes        = "Synthetic entry — replace with real pump selection"
                }
            };
        }

        private static PumpMatch ToMatch(PumpCatalogueEntry e, PumpDutyPoint duty)
        {
            double headMarginPct = duty.HeadM > 0
                ? (e.RatedHeadM - duty.HeadM) / duty.HeadM * 100.0
                : 0;
            return new PumpMatch
            {
                Manufacturer        = e.Manufacturer,
                Model               = e.Model,
                Series              = e.Series,
                RatedFlowLps        = e.RatedFlowLps,
                RatedHeadM          = e.RatedHeadM,
                PowerKw             = e.PowerKw,
                EfficiencyPct       = e.EfficiencyPct,
                DutyPointWithinCurve= true,
                MarginPct           = headMarginPct,
                CatalogueRef        = e.CatalogueRef,
                Notes               = e.Notes
            };
        }

        private static bool TryWriteDouble(Element el, string paramName, double value)
        {
            try
            {
                var p = el.LookupParameter(paramName);
                if (p == null || p.IsReadOnly) return false;
                if (p.StorageType == StorageType.Double) { p.Set(value); return true; }
                if (p.StorageType == StorageType.String) { p.Set(value.ToString("F3")); return true; }
            }
            catch { }
            return false;
        }

        private static bool TryWriteString(Element el, string paramName, string value)
        {
            try
            {
                var p = el.LookupParameter(paramName);
                if (p == null || p.IsReadOnly) return false;
                if (p.StorageType == StorageType.String) { p.Set(value); return true; }
            }
            catch { }
            return false;
        }
    }
}
