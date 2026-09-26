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
//   • Loads STING_PUMP_CATALOGUE.json from the data directory, layered with
//     the project's _BIM_COORD/pump_catalogue.json (project entries win by
//     manufacturer + model)
//   • An empty catalogue yields NO candidates and a warning. It never
//     invents pumps: the synthetic "STING Placeholder" entries it used to
//     return were written onto pump families as if they were a selection.
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
        /// <summary>Number of catalogue entries considered (0 = no catalogue loaded).</summary>
        public int             CatalogueEntries  { get; set; }
        /// <summary>Files the catalogue was read from, corporate first.</summary>
        public List<string>    CatalogueSources  { get; } = new List<string>();
        /// <summary>True when the duty point has a usable flow and head.</summary>
        public bool            HasValidDuty => Duty != null && Duty.FlowLps > 0 && Duty.HeadM > 0;
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
            => SelectPump(duty, cataloguePath, null);

        /// <summary>
        /// Match against the corporate catalogue layered with the project
        /// override at <c>_BIM_COORD/pump_catalogue.json</c> (when
        /// <paramref name="doc"/> is supplied).
        /// </summary>
        public static PumpSelectionResult SelectPump(
            PumpDutyPoint duty, string cataloguePath, Document doc)
        {
            var result = new PumpSelectionResult
            {
                Duty          = duty,
                StaticHeadM   = duty?.StaticHeadM   ?? 0,
                FrictionHeadM = duty?.FrictionHeadM ?? 0,
                TotalIndexHeadM = duty?.HeadM       ?? 0
            };

            if (duty == null) { result.Warnings.Add("Null duty point."); return result; }
            if (duty.FlowLps <= 0)
            {
                result.Warnings.Add("No design flow for this system — Revit carries no pipe flow on it. " +
                                    "Run the supply sizing (Plumb_SizeSupply) or enter the duty manually.");
                return result;
            }

            var entries = LoadCatalogue(cataloguePath, result.Warnings, result.CatalogueSources);
            string projectPath = ProjectCataloguePath(doc);
            if (!string.IsNullOrEmpty(projectPath) && File.Exists(projectPath))
            {
                var project = LoadCatalogue(projectPath, result.Warnings, result.CatalogueSources);
                entries = MergeCatalogues(entries, project);
            }
            result.CatalogueEntries = entries.Count;
            if (entries.Count == 0)
            {
                result.Warnings.Add("No pump catalogue entries loaded. Add manufacturer data to " +
                                    "STING_PUMP_CATALOGUE.json or the project's _BIM_COORD/pump_catalogue.json. " +
                                    "The duty point is still reported and can be written to the pump.");
                return result;
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
            // match may be null: the duty point is worth recording even when
            // no catalogue entry covers it; the model is written only for a
            // real match.
            if (doc == null || pumpId == null || duty == null) return false;
            if (duty.FlowLps <= 0 || duty.HeadM <= 0) return false;
            try
            {
                var el = doc.GetElement(pumpId);
                if (el == null) return false;

                bool ok = true;
                ok &= TryWriteDouble(el, ParamRegistry.PLM_PUMP_DUTY_HEAD_M,   duty.HeadM);
                ok &= TryWriteDouble(el, ParamRegistry.PLM_PUMP_DUTY_FLOW_LPS, duty.FlowLps);
                if (match != null)
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

                // 0 means "no flow on this system" — the caller reports it.
                // It used to be floored at 0.1 L/s (0.5 on error), which
                // sized a real pump for a flow nobody had calculated.
                return mainFlowLps > 0 ? mainFlowLps : maxFlowLps;
            }
            catch (Exception ex)
            {
                StingLog.Warn($"PumpSelector.EstimateDesignFlow({systemName}): {ex.Message}");
                return 0;
            }
        }

        private static string ProjectCataloguePath(Document doc)
        {
            if (doc == null) return null;
            try { return StingPaths.MetaFile(doc, "_BIM_COORD", "pump_catalogue.json"); }
            catch (Exception ex) { StingLog.Warn($"PumpSelector: project catalogue path: {ex.Message}"); return null; }
        }

        /// <summary>Project entries replace corporate ones with the same manufacturer + model; others are added.</summary>
        internal static List<PumpCatalogueEntry> MergeCatalogues(
            List<PumpCatalogueEntry> corporate, List<PumpCatalogueEntry> project)
        {
            string Key(PumpCatalogueEntry e) =>
                ((e.Manufacturer ?? "").Trim() + "|" + (e.Model ?? "").Trim()).ToUpperInvariant();
            var merged = new Dictionary<string, PumpCatalogueEntry>();
            foreach (var e in corporate ?? new List<PumpCatalogueEntry>()) merged[Key(e)] = e;
            foreach (var e in project   ?? new List<PumpCatalogueEntry>()) merged[Key(e)] = e;
            return merged.Values.ToList();
        }

        private static List<PumpCatalogueEntry> LoadCatalogue(string path, List<string> warnings,
            List<string> sources = null)
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
                var pumps = (catalogue?.Pumps ?? new List<PumpCatalogueEntry>())
                    .Where(e => e != null && e.RatedFlowLps > 0 && e.RatedHeadM > 0).ToList();
                int dropped = (catalogue?.Pumps?.Count ?? 0) - pumps.Count;
                if (dropped > 0)
                    warnings?.Add($"{Path.GetFileName(filePath)}: {dropped} entr{(dropped == 1 ? "y" : "ies")} " +
                                  "without a rated flow and head ignored.");
                sources?.Add(filePath);
                return pumps;
            }
            catch (Exception ex)
            {
                warnings?.Add($"PumpSelector.LoadCatalogue: {ex.Message}");
                return new List<PumpCatalogueEntry>();
            }
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
