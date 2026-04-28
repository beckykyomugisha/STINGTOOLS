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
                        // Phase 139.8 — view-bounded room collection. Revit's
                        // FilteredElementCollector(doc, view.Id) doesn't work
                        // reliably for non-3D entities like Rooms. Plan views
                        // expose `view.GenLevel`; collect rooms on that level.
                        // Fall back to bbox-intersection for sections / 3D.
                        if (view is ViewPlan plan && plan.GenLevel != null)
                        {
                            var levelId = plan.GenLevel.Id;
                            foreach (var el in new FilteredElementCollector(doc)
                                .OfCategory(BuiltInCategory.OST_Rooms)
                                .WhereElementIsNotElementType())
                            {
                                if (!(el is Autodesk.Revit.DB.Architecture.Room r) || r.Area <= 0) continue;
                                if (r.LevelId == levelId) ids.Add(r.Id);
                            }
                        }
                        else
                        {
                            // Unknown view type → bbox intersection of room
                            // bbox vs view crop / outline.
                            BoundingBoxXYZ vb = null;
                            try { vb = view.CropBox; } catch { }
                            foreach (var el in new FilteredElementCollector(doc)
                                .OfCategory(BuiltInCategory.OST_Rooms)
                                .WhereElementIsNotElementType())
                            {
                                if (!(el is Autodesk.Revit.DB.Architecture.Room r) || r.Area <= 0) continue;
                                var rb = r.get_BoundingBox(null);
                                if (rb == null) continue;
                                if (vb == null
                                    || (rb.Max.X >= vb.Min.X && rb.Min.X <= vb.Max.X
                                     && rb.Max.Y >= vb.Min.Y && rb.Min.Y <= vb.Max.Y))
                                    ids.Add(r.Id);
                            }
                        }
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
        /// PC-23 — run a user-selected mask of validators. Empty / null mask
        /// runs the legacy pair (Clearance + Maintenance) so existing call
        /// sites keep working unchanged. Recognised tokens (case-insensitive):
        /// "Clearance", "Maintenance", "Connectivity", "Fill", "Spec",
        /// "Termination", "Slope", "Separation".
        /// </summary>
        public static List<ValidationResult> RunValidators(Document doc, ISet<string> mask = null)
        {
            var all = new List<ValidationResult>();
            if (doc == null) return all;

            bool wants(string token) => mask == null || mask.Count == 0 || mask.Contains(token);

            if (wants("Clearance"))
                Try(() => new ClearanceValidator().Validate(doc), all, "ClearanceValidator");
            if (wants("Maintenance"))
                Try(() => new MaintenanceClashValidator().Validate(doc), all, "MaintenanceClashValidator");

            // The remaining validators may live in v4/v6 modules — invoke via
            // reflection so a missing assembly never crashes the panel.
            if (wants("Connectivity")) RunValidatorReflect("StingTools.Core.Validation.ConnectivityValidator",  doc, all);
            if (wants("Fill"))         RunValidatorReflect("StingTools.Core.Validation.FillValidator",          doc, all);
            if (wants("Spec"))         RunValidatorReflect("StingTools.Core.Validation.SpecValidator",          doc, all);
            if (wants("Termination"))  RunValidatorReflect("StingTools.Core.Validation.TerminationValidator",   doc, all);
            if (wants("Slope"))        RunValidatorReflect("StingTools.Core.Validation.SlopeValidator",         doc, all);
            if (wants("Separation"))   RunValidatorReflect("StingTools.Core.Validation.SeparationValidator",    doc, all);

            return all;
        }

        private static void Try(Func<IList<ValidationResult>> action, List<ValidationResult> sink, string name)
        {
            try { sink.AddRange(action()); }
            catch (Exception ex) { StingLog.Warn($"{name}: {ex.Message}"); }
        }

        private static void RunValidatorReflect(string typeFullName, Document doc, List<ValidationResult> sink)
        {
            try
            {
                var t = Type.GetType(typeFullName + ", StingTools");
                if (t == null) return;
                var ctor = t.GetConstructor(Type.EmptyTypes);
                if (ctor == null) return;
                var inst = ctor.Invoke(null);
                var m = t.GetMethod("Validate", new[] { typeof(Document) });
                if (m == null) return;
                var res = m.Invoke(inst, new object[] { doc }) as IEnumerable<ValidationResult>;
                if (res != null) sink.AddRange(res);
            }
            catch (Exception ex) { StingLog.Warn($"{typeFullName} reflect: {ex.Message}"); }
        }
    }
}
