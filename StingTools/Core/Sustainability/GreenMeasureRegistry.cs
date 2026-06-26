// StingTools — Green measure registry (Phase 195, spec §6).
//
// Loads STING_GREEN_MEASURES.json + optional project override (merged by id).
// Each measure carries the gate it affects + a cost handle (BOQ rate key or
// CST_* link) so Sustain_LccBenefit can roll capex + lifetime opex/savings into
// the Design Development Cost/Budget Estimate. Pure POCO, no Revit dependency.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace StingTools.Core.Sustainability
{
    public class CostHandle
    {
        public string Type        { get; set; } = "boqRateKey";  // boqRateKey | cstParam
        public string Key         { get; set; } = "";
        public string Unit        { get; set; } = "";
        public double DefaultRate { get; set; }
    }

    public class SavingsModel
    {
        public string Kind  { get; set; } = "";   // coolingReductionPct / waterReductionPct / energyOffsetKwhYr / ...
        public double Value { get; set; }
        public double AnnualSavingPerUnit { get; set; }
        public string Note  { get; set; } = "";
    }

    public class GreenMeasure
    {
        public string Id          { get; set; } = "";
        public string Name        { get; set; } = "";
        public string Gate        { get; set; } = "";   // energy | water | materials
        public string Description { get; set; } = "";
        public CostHandle Cost    { get; set; } = new CostHandle();
        public SavingsModel Savings { get; set; } = new SavingsModel();
    }

    public class GreenMeasureRegistry
    {
        private readonly List<GreenMeasure> _measures = new List<GreenMeasure>();

        public IReadOnlyList<GreenMeasure> All => _measures;
        public IEnumerable<GreenMeasure> ForGate(string gate)
            => _measures.Where(m => string.Equals(m.Gate, gate, StringComparison.OrdinalIgnoreCase));
        public GreenMeasure Get(string id)
            => _measures.FirstOrDefault(m => string.Equals(m.Id, id, StringComparison.OrdinalIgnoreCase));

        public static GreenMeasureRegistry LoadFromJson(string corporateJson, string projectJson = null)
        {
            var reg = new GreenMeasureRegistry();
            if (!string.IsNullOrWhiteSpace(corporateJson)) reg.Apply(corporateJson);
            if (!string.IsNullOrWhiteSpace(projectJson))   reg.Apply(projectJson);
            return reg;
        }

        public static GreenMeasureRegistry LoadFromFiles(string corporatePath, string projectPath)
            => LoadFromJson(SafeRead(corporatePath), SafeRead(projectPath));

        private static string SafeRead(string path)
        {
            try { return !string.IsNullOrEmpty(path) && File.Exists(path) ? File.ReadAllText(path) : null; }
            catch { return null; }
        }

        private void Apply(string json)
        {
            JObject root;
            try { root = JObject.Parse(json); } catch { return; }
            var arr = root["measures"] as JArray;
            if (arr == null) return;
            foreach (var m in arr.OfType<JObject>())
            {
                var measure = ParseMeasure(m);
                int existing = _measures.FindIndex(x =>
                    string.Equals(x.Id, measure.Id, StringComparison.OrdinalIgnoreCase));
                if (existing >= 0) _measures[existing] = measure;
                else _measures.Add(measure);
            }
        }

        private static GreenMeasure ParseMeasure(JObject m)
        {
            var measure = new GreenMeasure
            {
                Id          = (string)m["id"] ?? "",
                Name        = (string)m["name"] ?? (string)m["id"] ?? "",
                Gate        = (string)m["gate"] ?? "",
                Description = (string)m["description"] ?? ""
            };
            if (m["costHandle"] is JObject c)
                measure.Cost = new CostHandle
                {
                    Type        = (string)c["type"] ?? "boqRateKey",
                    Key         = (string)c["key"] ?? "",
                    Unit        = (string)c["unit"] ?? "",
                    DefaultRate = (double?)c["defaultRate"] ?? 0
                };
            if (m["savingsModel"] is JObject s)
                measure.Savings = new SavingsModel
                {
                    Kind  = (string)s["kind"] ?? "",
                    Value = (double?)s["value"] ?? 0,
                    AnnualSavingPerUnit = (double?)s["annualSavingPerUnit"] ?? 0,
                    Note  = (string)s["note"] ?? ""
                };
            return measure;
        }
    }
}
