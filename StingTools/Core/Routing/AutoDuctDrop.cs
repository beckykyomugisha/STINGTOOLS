// StingTools v4 MVP — AutoDuctDrop.
//
// For each air terminal / HVAC fixture, finds the nearest duct within
// search radius and creates a vertical duct branch via Duct.Create,
// tagging the run with HVC_DCT_FAB_METHOD_TXT (WORKSHOP / SITE),
// HVC_DCT_SEAM_TYPE_TXT (SMACNA A–F codes) and HVC_DCT_MAT_TXT.

using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Fabrication;
using Autodesk.Revit.DB.Mechanical;
using StingTools.Core.Fabrication;

namespace StingTools.Core.Routing
{
    public class AutoDuctDrop : DropEngineBase
    {
        public double SearchRadiusMm  { get; set; } = 5000.0;
        public string FabMethod       { get; set; } = "WORKSHOP";
        /// <summary>
        /// SMACNA seam code: A = Pittsburgh lock, B = Snap lock,
        /// C = Grooved, D = Double-seam, E = Government, F = TDC/TDF flange.
        /// </summary>
        public string SeamType        { get; set; } = "A";
        public string Material        { get; set; } = "GALV_STEEL";

        public ElementId DuctTypeId       { get; set; }
        public ElementId MechanicalSystemTypeId { get; set; }

        /// <summary>
        /// When true, and a FabricationConfiguration is loaded in the
        /// document, the drop is created as a FabricationPart instead
        /// of a design-intent Duct. Falls back automatically when no
        /// fab content is loaded.
        /// </summary>
        public bool PreferFabricationContent { get; set; } = true;

        public AutoDuctDrop(Document doc) : base(doc)
        {
            ConnectorDomain = Domain.DomainHvac;
            ServiceId       = "HVC_SA"; // default — overridable from command
        }

        public DropResult Execute(IList<Element> fixtures)
        {
            var result = new DropResult { Discipline = "HVAC" };
            if (fixtures == null || fixtures.Count == 0)
            {
                result.Warnings.Add("AutoDuctDrop: no fixtures supplied");
                return result;
            }

            ResolveDuctTypes(result);
            if (DuctTypeId == null || DuctTypeId == ElementId.InvalidElementId)
            {
                result.Warnings.Add("AutoDuctDrop: no DuctType found in project");
                return result;
            }
            if (MechanicalSystemTypeId == null || MechanicalSystemTypeId == ElementId.InvalidElementId)
            {
                result.Warnings.Add("AutoDuctDrop: no MechanicalSystemType found");
                return result;
            }

            // Inspect the RoutingPreferenceManager on the chosen DuctType.
            try
            {
                var dt = Doc.GetElement(DuctTypeId) as DuctType;
                var rpt = RoutingPreferenceInspector.Inspect(dt);
                if (!rpt.IsProductionReady)
                    result.Warnings.Add($"RoutingPreferenceManager gaps: {rpt}");
                else
                    StingLog.Info($"AutoDuctDrop: {rpt}");
            }
            catch (Exception ex)
            { result.Warnings.Add($"RoutingPreferenceInspector: {ex.Message}"); }

            using (var tx = new Transaction(Doc, "STING v4 Auto-duct drop"))
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
                            TryDropFromFixture(fx, BuiltInCategory.OST_DuctCurves, SearchRadiusMm, result);
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
                    result.Warnings.Add($"AutoDuctDrop fatal: {ex.Message}");
                }
            }
            return result;
        }

        private void ResolveDuctTypes(DropResult result)
        {
            try
            {
                if (DuctTypeId == null || DuctTypeId == ElementId.InvalidElementId)
                {
                    foreach (var el in new FilteredElementCollector(Doc).OfClass(typeof(DuctType)))
                    { if (el is DuctType dt) { DuctTypeId = dt.Id; break; } }
                }
                if (MechanicalSystemTypeId == null || MechanicalSystemTypeId == ElementId.InvalidElementId)
                {
                    foreach (var el in new FilteredElementCollector(Doc).OfClass(typeof(MechanicalSystemType)))
                    { if (el is MechanicalSystemType mst) { MechanicalSystemTypeId = mst.Id; break; } }
                }
            }
            catch (Exception ex)
            {
                result.Warnings.Add($"Resolve duct types: {ex.Message}");
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
                result.Warnings.Add("CreateRunBetween (duct): no host level; skipping");
                return ElementId.InvalidElementId;
            }
            // Route A: Fabrication content (LOD 400 ITM duct).
            if (PreferFabricationContent && FabricationServiceLocator.HasFabContent(Doc))
            {
                var btn = FabricationServiceLocator.FindButton(
                    Doc, BuiltInCategory.OST_DuctCurves,
                    serviceNameHint: "Duct");
                if (btn != null)
                {
                    try
                    {
                        var svc = FabricationServiceLocator.GetConfig(Doc)
                                      .GetAllLoadedServices()[btn.ServiceIndex];
                        var part = FabricationPart.Create(Doc, svc, btn.GroupIndex, btn.ButtonIndex, levelId);
                        if (part != null)
                        {
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

                            TrySetString(part, "HVC_DCT_FAB_METHOD_TXT", "SHOP");
                            TrySetString(part, "HVC_DCT_SEAM_TYPE_TXT",  SeamType);
                            TrySetString(part, "HVC_DCT_MAT_TXT",        Material);
                            return part.Id;
                        }
                    }
                    catch (Exception ex)
                    {
                        result.Warnings.Add($"FabricationPart.Create failed ({btn}): {ex.Message}; falling back to Duct.Create");
                    }
                }
            }

            try
            {
                // Duct.Create(doc, mechanicalSystemTypeId, ductTypeId, levelId, from, to) —
                // verified against Revit 2025 API. Returned Duct exposes
                // two end connectors that DropEngineBase wires up.
                var duct = Duct.Create(Doc, MechanicalSystemTypeId, DuctTypeId, levelId, from, to);
                if (duct == null) return ElementId.InvalidElementId;
                TrySetString(duct, "HVC_DCT_FAB_METHOD_TXT", FabMethod);
                TrySetString(duct, "HVC_DCT_SEAM_TYPE_TXT",  SeamType);
                TrySetString(duct, "HVC_DCT_MAT_TXT",        Material);
                return duct.Id;
            }
            catch (Exception ex)
            {
                result.Warnings.Add($"Duct.Create failed: {ex.Message}");
                return ElementId.InvalidElementId;
            }
        }
    }
}
