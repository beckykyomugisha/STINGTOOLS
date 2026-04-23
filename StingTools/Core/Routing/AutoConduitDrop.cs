// StingTools v4 MVP — AutoConduitDrop.
//
// For each selected fixture with an electrical connector, finds the
// nearest conduit or cable tray within search radius and emits a
// vertical drop conduit from the fixture point up to the intercept.
// The created conduit is tagged with ELC_CDT_INSTALL_METHOD_TXT and
// ELC_CDT_FAB_METHOD_TXT for downstream fabrication takeoff.

using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Electrical;

namespace StingTools.Core.Routing
{
    public class AutoConduitDrop : DropEngineBase
    {
        /// <summary>
        /// Default search radius when looking for a cable tray / conduit
        /// above a fixture. 3000mm is conservative for building services.
        /// </summary>
        public double SearchRadiusMm { get; set; } = 3000.0;

        /// <summary>
        /// Installation method written to ELC_CDT_INSTALL_METHOD_TXT
        /// on the created drop. Clipped / suspended / embedded / chased.
        /// </summary>
        public string InstallMethod { get; set; } = "CLIPPED";

        /// <summary>Fabrication method; typically "SITE" for drops.</summary>
        public string FabMethod { get; set; } = "SITE";

        /// <summary>
        /// ConduitType used for the drop. If null, the engine uses the
        /// first available ConduitType it finds in the document.
        /// </summary>
        public ElementId ConduitTypeId { get; set; }

        public AutoConduitDrop(Document doc) : base(doc)
        {
            ConnectorDomain = Domain.DomainCableTrayConduit;
        }

        /// <summary>
        /// Cable-tray / conduit has no NewTakeoffFitting API — wire-up at
        /// the host end uses Connector.ConnectTo with an adjacent tray
        /// connector instead.
        /// </summary>
        protected override bool SupportsTakeoff => false;

        public DropResult Execute(IList<Element> fixtures)
        {
            var result = new DropResult { Discipline = "Electrical" };
            if (fixtures == null || fixtures.Count == 0)
            {
                result.Warnings.Add("AutoConduitDrop: no fixtures supplied");
                return result;
            }

            ResolveConduitType(result);
            if (ConduitTypeId == null || ConduitTypeId == ElementId.InvalidElementId)
            {
                result.Warnings.Add("AutoConduitDrop: no ConduitType found in project");
                return result;
            }

            using (var tx = new Transaction(Doc, "STING v4 Auto-conduit drop"))
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
                            TryDropFromFixture(fx, BuiltInCategory.OST_CableTray, SearchRadiusMm, result);
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
                    result.Warnings.Add($"AutoConduitDrop fatal: {ex.Message}");
                }
            }

            return result;
        }

        private void ResolveConduitType(DropResult result)
        {
            if (ConduitTypeId != null && ConduitTypeId != ElementId.InvalidElementId) return;
            try
            {
                var col = new FilteredElementCollector(Doc).OfClass(typeof(ConduitType));
                foreach (var el in col)
                {
                    if (el is ConduitType ct) { ConduitTypeId = ct.Id; break; }
                }
            }
            catch (Exception ex)
            {
                result.Warnings.Add($"Resolve ConduitType: {ex.Message}");
            }
        }

        protected override ElementId CreateRunBetween(XYZ from, XYZ to, Element host, DropResult result)
        {
            if (from == null || to == null) return ElementId.InvalidElementId;
            if (ConduitTypeId == null || ConduitTypeId == ElementId.InvalidElementId) return ElementId.InvalidElementId;

            ElementId levelId = host?.LevelId ?? ElementId.InvalidElementId;
            if (levelId == ElementId.InvalidElementId && Doc.ActiveView != null)
                levelId = Doc.ActiveView.GenLevel?.Id ?? ElementId.InvalidElementId;

            if (levelId == ElementId.InvalidElementId)
            {
                result.Warnings.Add("CreateRunBetween: no host level; skipping conduit drop");
                return ElementId.InvalidElementId;
            }

            try
            {
                // Conduit.Create(doc, conduitTypeId, from, to, levelId) —
                // verified against Revit 2025 API. Returned Conduit
                // exposes two end connectors that DropEngineBase wires
                // up via Connector.ConnectTo (conduits have no takeoff
                // fitting so SupportsTakeoff is false).
                var cdt = Conduit.Create(Doc, ConduitTypeId, from, to, levelId);
                if (cdt == null) return ElementId.InvalidElementId;

                TrySetString(cdt, "ELC_CDT_INSTALL_METHOD_TXT", InstallMethod);
                TrySetString(cdt, "ELC_CDT_FAB_METHOD_TXT",     FabMethod);
                return cdt.Id;
            }
            catch (Exception ex)
            {
                result.Warnings.Add($"Conduit.Create failed: {ex.Message}");
                return ElementId.InvalidElementId;
            }
        }
    }
}
