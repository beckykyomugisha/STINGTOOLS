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
                        // 3D / perspective views contain no Room elements —
                        // warn early so the user isn't left with a silent "0 rooms" result.
                        if (view.ViewType == ViewType.ThreeD || view.ViewType == ViewType.Undefined)
                        {
                            StingLog.Warn("PlacementCenterBridge.ResolveScope: active view is a 3D/perspective view — rooms resolve to empty. Switch to a plan or section view, or change scope to Project.");
                        }
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
        /// Run a user-selected mask of validators. Empty / null mask runs the
        /// legacy pair (Clearance + Maintenance) so existing call sites keep
        /// working unchanged. Recognised tokens (case-insensitive):
        /// "Clearance", "Maintenance", "Connectivity", "Fill", "Spec",
        /// "Termination", "Slope", "Separation".
        ///
        /// All eight validators live in this assembly (StingTools.Core.Validation),
        /// so they are called directly with compile-time types — the previous
        /// reflection path silently no-op'd for "Separation" (a static class
        /// with no instance Validate(Document)) and would hide any future
        /// rename behind a runtime miss.
        /// </summary>
        public static List<ValidationResult> RunValidators(Document doc, ISet<string> mask = null)
        {
            var all = new List<ValidationResult>();
            if (doc == null) return all;

            bool wants(string token) => mask == null || mask.Count == 0 || mask.Contains(token);

            if (wants("Clearance"))    Try(() => new ClearanceValidator().Validate(doc),        all, "ClearanceValidator");
            // "Maintenance" maps to the clash validator (maintenance-clearance
            // overlap). MaintenanceAccessValidator covers door/route access and
            // is run by RunAllValidators, not this checklist token.
            if (wants("Maintenance"))  Try(() => new MaintenanceClashValidator().Validate(doc), all, "MaintenanceClashValidator");
            if (wants("Connectivity")) Try(() => new ConnectivityValidator().Validate(doc),     all, "ConnectivityValidator");
            if (wants("Fill"))         Try(() => new FillValidator().Validate(doc),             all, "FillValidator");
            if (wants("Spec"))         Try(() => new SpecValidator().Validate(doc),             all, "SpecValidator");
            if (wants("Termination"))  Try(() => new TerminationValidator().Validate(doc),      all, "TerminationValidator");
            if (wants("Slope"))        Try(() => new SlopeValidator().Validate(doc),            all, "SlopeValidator");
            // SeparationValidator is a static class — call its Validate(Document)
            // entry point directly.
            if (wants("Separation"))   Try(() => SeparationValidator.Validate(doc),             all, "SeparationValidator");

            return all;
        }

        private static void Try(Func<IList<ValidationResult>> action, List<ValidationResult> sink, string name)
        {
            try { sink.AddRange(action()); }
            catch (Exception ex) { StingLog.Warn($"{name}: {ex.Message}"); }
        }
    }
}
