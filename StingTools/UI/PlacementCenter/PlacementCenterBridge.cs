// Phase 127-B — Placement Centre bridge.
//
// Thin adapter between the centre's view model and the four engines
// it drives:
//
//   * FixturePlacementEngine.PlaceFixturesInScope — committing run
//   * the same engine, dryRun:true                 — preview-on-canvas
//   * ClearanceValidator + MaintenanceClashValidator — post-run audit
//   * StingProvenanceSchema — to scope validation to "just placed"
//
// Scope resolution (Active view / Selection / Project) lands here so
// the centre's Run / Preview / Validate buttons stay focused on UI
// state and the engine call sites only ever see a List<ElementId> of
// rooms.

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Core;
using StingTools.Core.Placement;
using StingTools.Core.Validation;

namespace StingTools.UI.PlacementCenter
{
    public static class PlacementCenterBridge
    {
        /// <summary>
        /// Resolve the room ElementIds the engine should consider, given
        /// the user's scope choice. Returns an empty list when nothing
        /// matches — caller decides whether that's an error.
        /// </summary>
        public static List<ElementId> ResolveScope(UIDocument uiDoc, string scope)
        {
            var ids = new List<ElementId>();
            if (uiDoc?.Document == null) return ids;
            var doc = uiDoc.Document;

            try
            {
                switch ((scope ?? "ActiveView").ToUpperInvariant())
                {
                    case "SELECTION":
                        foreach (var id in uiDoc.Selection.GetElementIds())
                        {
                            var el = doc.GetElement(id);
                            if (el != null && el.Category != null &&
                                el.Category.Id.Value == (long)BuiltInCategory.OST_Rooms)
                                ids.Add(id);
                        }
                        break;

                    case "PROJECT":
                        foreach (var r in new FilteredElementCollector(doc)
                            .OfCategory(BuiltInCategory.OST_Rooms)
                            .WhereElementIsNotElementType())
                            ids.Add(r.Id);
                        break;

                    case "ACTIVEVIEW":
                    default:
                        var view = doc.ActiveView;
                        if (view == null) break;
                        foreach (var r in new FilteredElementCollector(doc, view.Id)
                            .OfCategory(BuiltInCategory.OST_Rooms)
                            .WhereElementIsNotElementType())
                            ids.Add(r.Id);
                        break;
                }
            }
            catch (Exception ex) { StingLog.Warn($"PlacementCenterBridge.ResolveScope: {ex.Message}"); }
            return ids;
        }

        /// <summary>
        /// Convert centre VMs to the underlying POCO list the engine
        /// consumes. Skips invalid rules so the engine never sees a
        /// rule that fails its own loader's contract.
        /// </summary>
        public static List<PlacementRule> ToRules(IEnumerable<PlacementRuleViewModel> vms)
        {
            var list = new List<PlacementRule>();
            foreach (var vm in vms ?? Enumerable.Empty<PlacementRuleViewModel>())
            {
                if (vm == null || !vm.IsValid) continue;
                list.Add(vm.Model.Clone());
            }
            return list;
        }

        /// <summary>
        /// Filter a validator's findings to the elements just placed by
        /// the most recent run. Reads StingProvenanceSchema (Pack 123/E)
        /// so we can answer "did the run we just made introduce these
        /// findings?" without keeping a separate ID list.
        /// </summary>
        public static List<ValidationResult> FilterToProvenance(
            Document doc,
            IEnumerable<ValidationResult> findings,
            DateTime sinceUtc)
        {
            var kept = new List<ValidationResult>();
            if (findings == null) return kept;
            foreach (var r in findings)
            {
                if (r?.ElementId == null || r.ElementId == ElementId.InvalidElementId)
                {
                    kept.Add(r); // keep aggregate / summary rows
                    continue;
                }
                try
                {
                    var el = doc.GetElement(r.ElementId);
                    var prov = StingTools.Core.Storage.StingProvenanceSchema.Read(el);
                    if (prov != null &&
                        prov.CreatedUtcTicks >= sinceUtc.Ticks)
                        kept.Add(r);
                }
                catch (Exception ex) { StingLog.Warn($"FilterToProvenance {r?.ElementId?.Value}: {ex.Message}"); }
            }
            return kept;
        }

        /// <summary>
        /// Run ClearanceValidator + MaintenanceClashValidator and return
        /// the merged finding list. Caller decides scoping (post-run
        /// filter via FilterToProvenance, or full-project audit).
        /// </summary>
        public static List<ValidationResult> RunValidators(Document doc)
        {
            var all = new List<ValidationResult>();
            if (doc == null) return all;
            try { all.AddRange(new ClearanceValidator().Validate(doc)); }
            catch (Exception ex) { StingLog.Warn($"ClearanceValidator: {ex.Message}"); }
            try { all.AddRange(new MaintenanceClashValidator().Validate(doc)); }
            catch (Exception ex) { StingLog.Warn($"MaintenanceClashValidator: {ex.Message}"); }
            return all;
        }
    }
}
