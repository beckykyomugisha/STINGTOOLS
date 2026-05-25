// StingTools — Indoor-unit catalogue + selector.
//
// Phase 187h. STING_IDU_CATALOGUE.json holds per-vendor / per-series
// IDU records (capacity, fan, NC, refrigerant connections, MCA).
// IduSelector.Best(catalogue, duty) picks the smallest capacity match
// that satisfies all the supplied constraints.
//
// Layered: corporate baseline + project override at
// <project>/_BIM_COORD/idu_catalogue.json.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using Autodesk.Revit.DB;

namespace StingTools.Core.Refrigerant
{
    public class IduRecord
    {
        public string Id                { get; set; } = "";
        public string VendorSeriesId    { get; set; } = "";
        public string RefrigerantId     { get; set; } = "";
        public string MountingType      { get; set; } = "";   // Ducted / CeilingCassette / WallMounted / FourWay
        public string Label             { get; set; } = "";
        public double NominalCoolingKw  { get; set; }
        public double NominalHeatingKw  { get; set; }
        public double FanLs             { get; set; }
        public double EspPa             { get; set; }
        public int    Nc                { get; set; }
        public double LiqOdMm           { get; set; }
        public double GasOdMm           { get; set; }
        public double Mca               { get; set; }
        public double PowerKw           { get; set; }
        public double WeightKg          { get; set; }
    }

    public class IduCatalogue
    {
        public List<IduRecord> Units { get; } = new();
    }

    public static class IduCatalogueRegistry
    {
        public const string DataFileName = "STING_IDU_CATALOGUE.json";
        public const string ProjectOverrideRelPath = "_BIM_COORD/idu_catalogue.json";

        private static readonly ConcurrentDictionary<string, IduCatalogue> _cache
            = new(StringComparer.OrdinalIgnoreCase);

        public static IduCatalogue Get(Document doc)
        {
            string key = doc?.PathName ?? "<no-doc>";
            return _cache.GetOrAdd(key, _ => Load(doc));
        }

        public static void Reload()              => _cache.Clear();
        public static void Reload(Document doc)  => _cache.TryRemove(doc?.PathName ?? "<no-doc>", out _);

        private static IduCatalogue Load(Document doc)
        {
            var cat = new IduCatalogue();
            try
            {
                string basePath = StingTools.Core.StingToolsApp.FindDataFile(DataFileName);
                if (!string.IsNullOrEmpty(basePath) && File.Exists(basePath))
                    Apply(JObject.Parse(File.ReadAllText(basePath)), cat);
                if (doc != null && !string.IsNullOrEmpty(doc.PathName))
                {
                    string projDir = Path.GetDirectoryName(doc.PathName) ?? "";
                    string projPath = Path.Combine(projDir, ProjectOverrideRelPath);
                    if (File.Exists(projPath))
                        Apply(JObject.Parse(File.ReadAllText(projPath)), cat);
                }
            }
            catch (Exception ex)
            { StingTools.Core.StingLog.Error("IduCatalogueRegistry.Load", ex); }
            return cat;
        }

        private static void Apply(JObject j, IduCatalogue cat)
        {
            var arr = j["units"] as JArray;
            if (arr == null) return;
            foreach (var u in arr.OfType<JObject>())
            {
                cat.Units.Add(new IduRecord
                {
                    Id               = (string)u["id"] ?? "",
                    VendorSeriesId   = (string)u["vendorSeriesId"] ?? "",
                    RefrigerantId    = (string)u["refrigerantId"] ?? "",
                    MountingType     = (string)u["mountingType"] ?? "",
                    Label            = (string)u["label"] ?? "",
                    NominalCoolingKw = (double?)u["nominalCoolingKw"] ?? 0,
                    NominalHeatingKw = (double?)u["nominalHeatingKw"] ?? 0,
                    FanLs            = (double?)u["fanLs"] ?? 0,
                    EspPa            = (double?)u["espPa"] ?? 0,
                    Nc               = (int?)u["nc"] ?? 0,
                    LiqOdMm          = (double?)u["liqOdMm"] ?? 0,
                    GasOdMm          = (double?)u["gasOdMm"] ?? 0,
                    Mca              = (double?)u["mca"] ?? 0,
                    PowerKw          = (double?)u["powerKw"] ?? 0,
                    WeightKg         = (double?)u["weightKg"] ?? 0
                });
            }
        }
    }

    public class IduDuty
    {
        public string VendorSeriesId { get; set; }     // optional filter
        public string RefrigerantId  { get; set; }     // optional filter
        public string MountingType   { get; set; }     // optional filter
        public double DutyKw         { get; set; }     // required cooling capacity
        public double MinFlowLs      { get; set; }     // optional, 0 = no constraint
        public double MinEspPa       { get; set; }     // optional, 0 = no constraint
        public int    MaxNc          { get; set; }     // optional, 0 = no constraint
    }

    public class IduSelection
    {
        public IduRecord Best          { get; set; }
        public double    CapacityRatio { get; set; }   // selected_kW / duty_kW (≥ 1)
        public List<IduRecord> Considered { get; } = new();
        public List<string> RejectionReasons { get; } = new();
    }

    public static class IduSelector
    {
        public static IduSelection Pick(IduCatalogue cat, IduDuty duty)
        {
            var sel = new IduSelection();
            if (cat == null || duty == null || cat.Units.Count == 0) return sel;
            foreach (var u in cat.Units)
            {
                if (!string.IsNullOrEmpty(duty.VendorSeriesId) &&
                    !string.Equals(u.VendorSeriesId, duty.VendorSeriesId, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (!string.IsNullOrEmpty(duty.RefrigerantId) &&
                    !string.Equals(u.RefrigerantId,  duty.RefrigerantId,  StringComparison.OrdinalIgnoreCase))
                    continue;
                if (!string.IsNullOrEmpty(duty.MountingType) &&
                    !string.Equals(u.MountingType,   duty.MountingType,   StringComparison.OrdinalIgnoreCase))
                    continue;

                string reject = null;
                if (u.NominalCoolingKw < duty.DutyKw) reject = $"cap {u.NominalCoolingKw} < {duty.DutyKw} kW";
                else if (duty.MinFlowLs > 0 && u.FanLs < duty.MinFlowLs)
                    reject = $"fan {u.FanLs} L/s < {duty.MinFlowLs} L/s";
                else if (duty.MinEspPa > 0 && u.EspPa < duty.MinEspPa)
                    reject = $"ESP {u.EspPa} Pa < {duty.MinEspPa} Pa";
                else if (duty.MaxNc > 0 && u.Nc > duty.MaxNc)
                    reject = $"NC {u.Nc} > {duty.MaxNc}";

                if (reject != null) sel.RejectionReasons.Add($"{u.Id}: {reject}");
                else sel.Considered.Add(u);
            }
            if (sel.Considered.Count == 0) return sel;
            // Rank: closest oversize (lowest capacity ratio) wins; tiebreak on
            // smallest power then smallest weight.
            var best = sel.Considered
                .OrderBy(u => u.NominalCoolingKw / duty.DutyKw)
                .ThenBy(u => u.PowerKw)
                .ThenBy(u => u.WeightKg)
                .First();
            sel.Best          = best;
            sel.CapacityRatio = best.NominalCoolingKw / duty.DutyKw;
            return sel;
        }
    }
}
