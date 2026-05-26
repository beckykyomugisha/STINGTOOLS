// StingTools — Refrigerant vendor pipe-length envelope tables.
//
// Vendor manuals (Daikin REYQ-T table 6.3.2, Mitsubishi City Multi
// Tbl 5-2, Toshiba SHRMe Tbl 4-1, etc.) publish stricter limits than
// the generic single-fluid envelope shipped in RefrigerantProperties:
//
//   * total pipe length (all branches summed)
//   * first-branch to farthest IDU actual + equivalent length
//   * vertical drop ODU↔IDU and IDU↔IDU
//
// RefrigerantPipeSolver applies these AFTER its Darcy-Weisbach pass
// when a vendor series id is supplied. Falls back to the generic
// envelope from RefrigerantProperties when no match.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using Autodesk.Revit.DB;

namespace StingTools.Core.Refrigerant
{
    public class VendorSeriesLimits
    {
        public string Id                            { get; set; } = "";
        public string RefrigerantId                 { get; set; } = "";
        public string Label                         { get; set; } = "";
        public double TotalPipeLengthM              { get; set; }
        public double ActualOnewayMaxM              { get; set; }
        public double EquivalentOnewayMaxM          { get; set; }
        public double FirstBranchToFarIduActualM    { get; set; }
        public double FirstBranchToFarIduEquivM     { get; set; }
        public double VerticalHighLowOduIduM        { get; set; }
        public double VerticalHighLowIduIduM        { get; set; }
        public string Source                        { get; set; } = "";
    }

    public class VendorLimitsLibrary
    {
        public Dictionary<string, VendorSeriesLimits> ById { get; }
            = new Dictionary<string, VendorSeriesLimits>(StringComparer.OrdinalIgnoreCase);

        public VendorSeriesLimits Get(string id) =>
            string.IsNullOrWhiteSpace(id) ? null
                : (ById.TryGetValue(id, out var v) ? v : null);

        public IEnumerable<VendorSeriesLimits> ForRefrigerant(string refrigerantId)
            => ById.Values.Where(v => string.Equals(v.RefrigerantId, refrigerantId,
                StringComparison.OrdinalIgnoreCase));
    }

    public static class RefrigerantVendorRegistry
    {
        public const string DataFileName = "STING_REFRIG_VENDOR_LIMITS.json";
        public const string ProjectOverrideRelPath = "_BIM_COORD/refrig_vendor_limits.json";

        private static readonly ConcurrentDictionary<string, VendorLimitsLibrary> _cache
            = new ConcurrentDictionary<string, VendorLimitsLibrary>(StringComparer.OrdinalIgnoreCase);

        public static VendorLimitsLibrary Get(Document doc)
        {
            string key = doc?.PathName ?? "<no-doc>";
            return _cache.GetOrAdd(key, _ => Load(doc));
        }

        public static void Reload()              => _cache.Clear();
        public static void Reload(Document doc)  => _cache.TryRemove(doc?.PathName ?? "<no-doc>", out _);

        private static VendorLimitsLibrary Load(Document doc)
        {
            var lib = new VendorLimitsLibrary();
            try
            {
                string basePath = StingTools.Core.StingToolsApp.FindDataFile(DataFileName);
                if (!string.IsNullOrEmpty(basePath) && File.Exists(basePath))
                    Apply(JObject.Parse(File.ReadAllText(basePath)), lib);
                if (doc != null && !string.IsNullOrEmpty(doc.PathName))
                {
                    string projDir = Path.GetDirectoryName(doc.PathName) ?? "";
                    string projPath = Path.Combine(projDir, ProjectOverrideRelPath);
                    if (File.Exists(projPath))
                        Apply(JObject.Parse(File.ReadAllText(projPath)), lib);
                }
            }
            catch (Exception ex)
            { StingTools.Core.StingLog.Error("RefrigerantVendorRegistry.Load", ex); }
            return lib;
        }

        private static void Apply(JObject j, VendorLimitsLibrary lib)
        {
            var arr = j["series"] as JArray;
            if (arr == null) return;
            foreach (var s in arr.OfType<JObject>())
            {
                var v = new VendorSeriesLimits
                {
                    Id                            = (string)s["id"] ?? "",
                    RefrigerantId                 = (string)s["refrigerantId"] ?? "",
                    Label                         = (string)s["label"] ?? "",
                    TotalPipeLengthM              = (double?)s["totalPipeLengthM"]              ?? 0,
                    ActualOnewayMaxM              = (double?)s["actualOnewayMaxM"]              ?? 0,
                    EquivalentOnewayMaxM          = (double?)s["equivalentOnewayMaxM"]          ?? 0,
                    FirstBranchToFarIduActualM    = (double?)s["firstBranchToFarIduActualM"]    ?? 0,
                    FirstBranchToFarIduEquivM     = (double?)s["firstBranchToFarIduEquivM"]     ?? 0,
                    VerticalHighLowOduIduM        = (double?)s["verticalHighLowOduIduM"]        ?? 0,
                    VerticalHighLowIduIduM        = (double?)s["verticalHighLowIduIduM"]        ?? 0,
                    Source                        = (string)s["source"] ?? ""
                };
                if (!string.IsNullOrEmpty(v.Id)) lib.ById[v.Id] = v;
            }
        }
    }
}
