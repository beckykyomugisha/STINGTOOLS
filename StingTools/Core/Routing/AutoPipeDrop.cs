// StingTools v4 MVP — AutoPipeDrop.
//
// For each plumbing fixture, finds the nearest pipe of a matching
// system (domestic cold, domestic hot, sanitary etc.) within search
// radius and creates a vertical pipe drop via Pipe.Create, tagging
// the run with PLM_PPE_FAB_METHOD_TXT and PLM_PPE_HANGER_TYPE_TXT.

using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Plumbing;
using StingTools.Core.Fabrication;

namespace StingTools.Core.Routing
{
    public class AutoPipeDrop : DropEngineBase
    {
        public double SearchRadiusMm  { get; set; } = 4000.0;
        public string FabMethod       { get; set; } = "SITE";
        public string HangerType      { get; set; } = "CLEVIS_ROD";

        public ElementId PipeTypeId       { get; set; }
        public ElementId PipingSystemTypeId { get; set; }

        /// <summary>
        /// When true, and a FabricationConfiguration is loaded in the
        /// document, the drop is created as a FabricationPart (ITM
        /// content, LOD 400) instead of a design-intent Pipe. Defaults
        /// true; falls back automatically when no fab content is loaded.
        /// </summary>
        public bool PreferFabricationContent { get; set; } = true;

        public AutoPipeDrop(Document doc) : base(doc)
        {
            ConnectorDomain = Domain.DomainPiping;
            ServiceId       = "PLM_CWS"; // default — overridable from command
        }

        public DropResult Execute(IList<Element> fixtures)
        {
            var result = new DropResult { Discipline = "Plumbing" };
            if (fixtures == null || fixtures.Count == 0)
            {
                result.Warnings.Add("AutoPipeDrop: no fixtures supplied");
                return result;
            }

            ResolvePipeTypes(result);
            if (PipeTypeId == null || PipeTypeId == ElementId.InvalidElementId)
            {
                result.Warnings.Add("AutoPipeDrop: no PipeType found in project");
                return result;
            }
            if (PipingSystemTypeId == null || PipingSystemTypeId == ElementId.InvalidElementId)
            {
                result.Warnings.Add("AutoPipeDrop: no PipingSystemType found in project");
                return result;
            }

            // Inspect the RoutingPreferenceManager on the chosen PipeType
            // so we can surface "your type has no elbow rule" before we
            // issue N connects that silently no-op at fitting time.
            try
            {
                var pt = Doc.GetElement(PipeTypeId) as PipeType;
                var rpt = RoutingPreferenceInspector.Inspect(pt);
                if (!rpt.IsProductionReady)
                    result.Warnings.Add($"RoutingPreferenceManager gaps: {rpt}");
                else
                    StingLog.Info($"AutoPipeDrop: {rpt}");
            }
            catch (Exception ex)
            { result.Warnings.Add($"RoutingPreferenceInspector: {ex.Message}"); }

            using (var tx = new Transaction(Doc, "STING v4 Auto-pipe drop"))
            {
                try { tx.Start(); }
                catch (Exception ex)
                {
                    result.Warnings.Add($"Transaction start failed: {ex.Message}");
                    return result;
                }

                try
                {
                    foreach (var fx in fixtures)
                    {
                        try
                        {
                            TryDropFromFixture(fx, BuiltInCategory.OST_PipeCurves, SearchRadiusMm, result);
                        }
                        catch (Exception ex)
                        {
                            result.FailedCount++;
                            result.Warnings.Add($"Drop from {fx?.Id}: {ex.Message}");
                        }
                    }
                    tx.Commit();
                }
                catch (Exception ex)
                {
                    if (tx.HasStarted() && !tx.HasEnded()) tx.RollBack();
                    result.Warnings.Add($"AutoPipeDrop fatal: {ex.Message}");
                }
            }
            return result;
        }

        private void ResolvePipeTypes(DropResult result)
        {
            try
            {
                if (PipeTypeId == null || PipeTypeId == ElementId.InvalidElementId)
                {
                    foreach (var el in new FilteredElementCollector(Doc).OfClass(typeof(PipeType)))
                    { if (el is PipeType pt) { PipeTypeId = pt.Id; break; } }
                }
                if (PipingSystemTypeId == null || PipingSystemTypeId == ElementId.InvalidElementId)
                {
                    foreach (var el in new FilteredElementCollector(Doc).OfClass(typeof(PipingSystemType)))
                    { if (el is PipingSystemType pst) { PipingSystemTypeId = pst.Id; break; } }
                }
            }
            catch (Exception ex)
            {
                result.Warnings.Add($"Resolve pipe types: {ex.Message}");
            }
        }

        protected override ElementId CreateRunBetween(XYZ from, XYZ to, Element host, DropResult result)
        {
            if (from == null || to == null) return ElementId.InvalidElementId;
            ElementId levelId = host?.LevelId ?? ElementId.InvalidElementId;
            if (levelId == ElementId.InvalidElementId && Doc.ActiveView != null)
                levelId = Doc.ActiveView.GenLevel?.Id ?? ElementId.InvalidElementId;
            if (levelId == ElementId.InvalidElementId)
            {
                result.Warnings.Add("CreateRunBetween (pipe): no host level; skipping");
                return ElementId.InvalidElementId;
            }

            // Route A (Phase B.2 deferred): FabricationPart.Create with
            // ITM content. The service→palette→button walk uses API
            // surface (FabricationService.PaletteCount + GetButtons,
            // FabricationPart.Create overload) that could not be
            // verified in the Linux sandbox — signatures differ across
            // 2024/2025/2026. Fabrication content is therefore flagged
            // for a verified rewrite in Phase B.2; the routing engine
            // stays on the design-intent path below so v4 still ships.
            if (PreferFabricationContent && FabricationServiceLocator.HasFabContent(Doc))
            {
                result.Warnings.Add(
                    "Fabrication content is loaded in this project, but FabricationPart.Create " +
                    "routing is deferred to Phase B.2 (API verification required). Falling back " +
                    "to design-intent Pipe.Create.");
            }

            // Route B: design-intent Pipe.Create.
            try
            {
                var pipe = Pipe.Create(Doc, PipingSystemTypeId, PipeTypeId, levelId, from, to);
                if (pipe == null) return ElementId.InvalidElementId;
                TrySetString(pipe, "PLM_PPE_FAB_METHOD_TXT",     FabMethod);
                TrySetString(pipe, "PLM_PPE_HANGER_TYPE_TXT",    HangerType);
                return pipe.Id;
            }
            catch (Exception ex)
            {
                result.Warnings.Add($"Pipe.Create failed: {ex.Message}");
                return ElementId.InvalidElementId;
            }
        }
    }
}
