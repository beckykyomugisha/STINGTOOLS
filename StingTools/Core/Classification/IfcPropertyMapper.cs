using System;
// Pack 126 / Gap K — IFC PropertySet wiring for §5.5 classification.
//
// The five identity / classification parameters added in Phase 120
// (UNICLASS_PR/SS/EF, NBS_CODE, ASSET_RFI_URL) need to surface in
// IFC export so handover packs carry them. Existing IFC export paths
// in COBieDataCommands etc. can call IfcPropertyMapper.Build to get
// a ready-to-write Pset table.
//
// One Pset per category — Pset_ClassificationReference is the IFC4
// canonical for Uniclass; we keep the rest in a vendor-specific
// "Pset_PlanscapeAsset" so reverse-importers can find them
// deterministically.

using System.Collections.Generic;
using Autodesk.Revit.DB;

namespace StingTools.Core.Classification
{
    public static class IfcPropertyMapper
    {
        public class Pset
        {
            public string Name { get; set; } = "";
            public Dictionary<string, string> Properties { get; set; } = new Dictionary<string, string>();
        }

        /// <summary>
        /// Returns the two PropertySets every element should carry on IFC
        /// export when the corresponding STING parameters are populated.
        /// Empty psets are filtered out so the IFC file doesn't bloat.
        /// </summary>
        public static List<Pset> Build(Element el)
        {
            var list = new List<Pset>();
            if (el == null) return list;

            var info = ClassificationReader.Read(el);

            // Pset_ClassificationReference — IFC4 canonical Uniclass
            var classification = new Pset { Name = "Pset_ClassificationReference" };
            if (!string.IsNullOrEmpty(info.UniclassProduct))
                classification.Properties["UniclassPr"]   = info.UniclassProduct;
            if (!string.IsNullOrEmpty(info.UniclassSystem))
                classification.Properties["UniclassSs"]   = info.UniclassSystem;
            if (!string.IsNullOrEmpty(info.UniclassElement))
                classification.Properties["UniclassEf"]   = info.UniclassElement;
            if (classification.Properties.Count > 0) list.Add(classification);

            // Pset_PlanscapeAsset — vendor-specific catch-all (NBS clause + RFI URL)
            var asset = new Pset { Name = "Pset_PlanscapeAsset" };
            if (!string.IsNullOrEmpty(info.NbsCode))     asset.Properties["NbsClause"]    = info.NbsCode;
            if (!string.IsNullOrEmpty(info.AssetRfiUrl)) asset.Properties["RfiUrl"]       = info.AssetRfiUrl;
            // Pack 126 / Gap J — fallback chain provenance lands here too
            // so downstream IFC consumers see how the row was grouped.
            var fb = ClassificationReader.ResolveFallback(el);
            if (!string.IsNullOrEmpty(fb.source) && fb.source != "Native.Family")
                asset.Properties["ClassificationSource"] = fb.source;
            if (asset.Properties.Count > 0) list.Add(asset);

            return list;
        }
    }
}
