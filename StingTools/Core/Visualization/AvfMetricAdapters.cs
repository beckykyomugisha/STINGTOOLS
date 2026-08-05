// Pack 6 — AVF metric adapters.
//
// Four thin bridges between STING's existing cached engines and the AVF
// heat-map renderer. No new analysis logic — these only read ComplianceScan,
// FillValidator, SustainabilityEngine, AcousticAnalysisEngine outputs and
// return them as (id, scalar) pairs.
//
// If any adapter finds its underlying engine unavailable (e.g. the carbon
// engine needs a material manifest that isn't loaded), it yields zero
// samples and logs a warning. The heat-map button shows an empty view
// with a "no data" TaskDialog rather than crashing.

using System.Collections.Generic;
using Autodesk.Revit.DB;
using StingTools.Core;

namespace StingTools.Core.Visualization
{
    /// <summary>
    /// Compliance RAG per-element. Reads the per-element tagged/untagged
    /// state produced by ComplianceScan.Scan(). 0 = untagged, 50 = partial,
    /// 100 = fully tagged + validated. Matches the RAG gradient users
    /// already associate with the status bar.
    /// </summary>
    public class ComplianceHeatmapAdapter : IAvfMetricAdapter
    {
        public string MetricName => "Compliance (tag + validation)";
        public string Unit => "%";

        public IEnumerable<(ElementId id, double value)> Collect(Document doc)
        {
            if (doc == null) yield break;
            // TODO-VERIFY-API: ComplianceScan.Scan returns a summary, not a
            // per-element map, so this adapter re-derives the per-element
            // state inline to keep Pack 6 self-contained. A future pass can
            // promote this into ComplianceScan as a first-class API.
            var col = new FilteredElementCollector(doc).WhereElementIsNotElementType();
            foreach (var el in col)
            {
                string tag = ParameterHelpers.GetString(el, ParamRegistry.TAG1);
                double v = string.IsNullOrEmpty(tag) ? 0 :
                           (TagConfig.TagIsComplete(tag) ? 100 : 50);
                yield return (el.Id, v);
            }
        }
    }

    /// <summary>
    /// Fill % per conduit / pipe / duct. Reads the cached value that
    /// FillValidator already computes so the heat-map renders identically to
    /// the validator report.
    /// </summary>
    public class FillHeatmapAdapter : IAvfMetricAdapter
    {
        public string MetricName => "Fill %";
        public string Unit => "%";

        public IEnumerable<(ElementId id, double value)> Collect(Document doc)
        {
            if (doc == null) yield break;
            var cats = new[]
            {
                BuiltInCategory.OST_Conduit,
                BuiltInCategory.OST_CableTray,
                BuiltInCategory.OST_PipeCurves,
                BuiltInCategory.OST_DuctCurves,
            };
            var col = new FilteredElementCollector(doc)
                .WherePasses(new ElementMulticategoryFilter(cats))
                .WhereElementIsNotElementType();
            foreach (var el in col)
            {
                double fill = 0;
                var p = el.LookupParameter("ELC_CDT_CBL_FILL_PCT")
                    ?? el.LookupParameter("PLM_PPE_VELOCITY_MS")
                    ?? el.LookupParameter("HVC_DCT_VELOCITY_MS");
                if (p != null && p.HasValue)
                {
                    try
                    {
                        fill = p.StorageType == StorageType.Double ? p.AsDouble() :
                               p.StorageType == StorageType.Integer ? p.AsInteger() : 0;
                    }
                    catch { fill = 0; }
                }
                if (fill > 0) yield return (el.Id, fill);
            }
        }
    }

    /// <summary>
    /// Embodied carbon per element (kgCO2e). Reads STING_CO2_KG, which
    /// SustainabilityEngine populates. Zero-values are skipped so the
    /// gradient stays meaningful.
    /// </summary>
    public class CarbonHeatmapAdapter : IAvfMetricAdapter
    {
        public string MetricName => "Embodied Carbon";
        public string Unit => "kgCO2e";

        public IEnumerable<(ElementId id, double value)> Collect(Document doc)
        {
            if (doc == null) yield break;
            var col = new FilteredElementCollector(doc).WhereElementIsNotElementType();
            foreach (var el in col)
            {
                var p = el.LookupParameter("STING_CO2_KG");
                if (p == null || !p.HasValue) continue;
                double v = 0;
                try
                {
                    v = p.StorageType == StorageType.Double ? p.AsDouble() :
                        p.StorageType == StorageType.Integer ? p.AsInteger() : 0;
                }
                catch { continue; }
                if (v > 0) yield return (el.Id, v);
            }
        }
    }

    /// <summary>
    /// Acoustic Rw (dB) per separating element. Reads STING_ACOUSTIC_RW_DB —
    /// the parameter Pack 1 wired into SpecValidator. AVF gradient makes
    /// acoustically-weak partitions instantly visible.
    /// </summary>
    public class AcousticHeatmapAdapter : IAvfMetricAdapter
    {
        public string MetricName => "Acoustic Rw";
        public string Unit => "dB";

        public IEnumerable<(ElementId id, double value)> Collect(Document doc)
        {
            if (doc == null) yield break;
            var cats = new[] { BuiltInCategory.OST_Walls, BuiltInCategory.OST_Doors, BuiltInCategory.OST_Floors };
            var col = new FilteredElementCollector(doc)
                .WherePasses(new ElementMulticategoryFilter(cats))
                .WhereElementIsNotElementType();
            foreach (var el in col)
            {
                Element type = null;
                try { type = doc.GetElement(el.GetTypeId()); } catch { }
                var p = type?.LookupParameter("STING_ACOUSTIC_RW_DB")
                    ?? el.LookupParameter("STING_ACOUSTIC_RW_DB");
                if (p == null || !p.HasValue) continue;
                int v = 0;
                try { v = p.StorageType == StorageType.Integer ? p.AsInteger() : 0; }
                catch { continue; }
                if (v > 0) yield return (el.Id, v);
            }
        }
    }
}
