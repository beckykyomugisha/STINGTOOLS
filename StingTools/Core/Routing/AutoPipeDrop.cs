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

namespace StingTools.Core.Routing
{
    public class AutoPipeDrop : DropEngineBase
    {
        public double SearchRadiusMm  { get; set; } = 4000.0;
        public string FabMethod       { get; set; } = "SITE";
        public string HangerType      { get; set; } = "CLEVIS_ROD";

        public ElementId PipeTypeId       { get; set; }
        public ElementId PipingSystemTypeId { get; set; }

        public AutoPipeDrop(Document doc) : base(doc)
        {
            ConnectorDomain = Domain.DomainPiping;
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
            try
            {
                // Pipe.Create(doc, systemTypeId, pipeTypeId, levelId, from, to) —
                // verified against Revit 2025 API. Returns a Pipe whose
                // ConnectorManager exposes two end connectors that the
                // DropEngineBase will wire up post-creation.
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
