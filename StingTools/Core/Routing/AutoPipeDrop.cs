// StingTools v4 MVP — AutoPipeDrop.
//
// For each plumbing fixture, finds the nearest pipe of a matching
// system (domestic cold, domestic hot, sanitary etc.) within search
// radius and creates a vertical pipe drop via Pipe.Create, tagging
// the run with PLM_PPE_FAB_METHOD_TXT and PLM_PPE_HANGER_TYPE_TXT.

using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Fabrication;
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

            // Route A: Fabrication content (ITM, LOD 400). Only when a
            // FabricationConfiguration is loaded and the user hasn't
            // opted out via PreferFabricationContent.
            if (PreferFabricationContent && FabricationServiceLocator.HasFabContent(Doc))
            {
                var btn = FabricationServiceLocator.FindButton(
                    Doc, BuiltInCategory.OST_PipeCurves,
                    serviceNameHint: "Pipe");
                if (btn != null)
                {
                    try
                    {
                        // FabricationPart.Create(doc, button, level, serviceIndex)
                        // returns a FabricationPart stick at origin; we then
                        // align it to from→to via AlignPartByConnectors after
                        // creation (driven by DropEngineBase wire-up stage).
                        var svc = FabricationServiceLocator.GetConfig(Doc)
                                      .GetAllLoadedServices()[btn.ServiceIndex];
                        var part = FabricationPart.Create(Doc, svc, btn.GroupIndex, btn.ButtonIndex, levelId);
                        if (part != null)
                        {
                            // Translate the part to the drop midpoint so the
                            // subsequent ConnectTo stage has a reasonable
                            // starting position. The ACO refiner will clean
                            // up any residual offset in Phase C.
                            try
                            {
                                var mid = new XYZ((from.X + to.X) * 0.5,
                                                  (from.Y + to.Y) * 0.5,
                                                  (from.Z + to.Z) * 0.5);
                                var bb = part.get_BoundingBox(null);
                                if (bb != null)
                                {
                                    var cur = (bb.Min + bb.Max) * 0.5;
                                    ElementTransformUtils.MoveElement(Doc, part.Id, mid - cur);
                                }
                            }
                            catch (Exception ex)
                            { result.Warnings.Add($"FabricationPart align: {ex.Message}"); }

                            TrySetString(part, "PLM_PPE_FAB_METHOD_TXT", "SHOP"); // ITM = shop
                            TrySetString(part, "PLM_PPE_HANGER_TYPE_TXT", HangerType);
                            return part.Id;
                        }
                    }
                    catch (Exception ex)
                    {
                        // Swallow and fall through to the design-intent path.
                        result.Warnings.Add($"FabricationPart.Create failed ({btn}): {ex.Message}; falling back to Pipe.Create");
                    }
                }
                else
                {
                    result.Warnings.Add("PreferFabricationContent=true but no Pipe-category button found; using design pipe.");
                }
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
