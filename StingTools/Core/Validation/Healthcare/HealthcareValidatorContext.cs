using StingTools.Core;
// Healthcare Pack — shared context built once per validator chain run so
// the 8+ healthcare validators that iterate Rooms / clinical FF&E don't
// each re-run their own FilteredElementCollector + LookupParameter pass.
//
// On a 500-room hospital model the saving is roughly 8× the room collector
// cost plus 8× the per-room CLN_ROOM_CLASS_TXT lookup ≈ 50–200 ms. On a
// 2000-element ICU/imaging floor the IoT / clinical-equipment cache saves
// repeated FilteredElementCollector passes too.
//
// Validators that don't take the context still work — they just pay the
// per-validator collector cost. RunAllHealthcareValidators builds the
// context once and passes it through HealthcareValidatorContext.Active
// (a thread-static stash) so individual Validate(doc) implementations can
// opt-in to the cache without an API change.

using Autodesk.Revit.DB;
using System;
using System.Collections.Generic;
using System.Linq;

namespace StingTools.Core.Validation.Healthcare
{
    public class HealthcareValidatorContext
    {
        public Document Document { get; }

        // Rooms cached once with their CLN_ROOM_CLASS_TXT value pre-read.
        // Key = ElementId.Value (long); value = (Element, classCode).
        public Dictionary<long, (Element room, string roomClass)> RoomById { get; } = new();

        // Clinical-room subset (only rooms with a non-empty CLN_ROOM_CLASS_TXT)
        // — most healthcare validators only care about clinical rooms.
        public List<Element> ClinicalRooms { get; } = new();

        // Group rooms by CLN_ROOM_CLASS_TXT for O(1) lookups by class code
        // (used by AdjacencyValidator + Hybrid OR + Pharmacy USP audits).
        public Dictionary<string, List<Element>> RoomsByClass { get; } =
            new Dictionary<string, List<Element>>(StringComparer.OrdinalIgnoreCase);

        // Clinical / medical equipment + nurse-call cache, gathered with one
        // multi-category collector so individual validators don't re-scan.
        public IReadOnlyList<Element> ClinicalEquipment { get; private set; } =
            new List<Element>();

        // Thread-static "active context" so individual validators can opt in
        // without changing their public Validate(Document) signature.
        [ThreadStatic] private static HealthcareValidatorContext _active;
        public static HealthcareValidatorContext Active => _active;
        public static void SetActive(HealthcareValidatorContext ctx) { _active = ctx; }
        public static void ClearActive() { _active = null; }

        private HealthcareValidatorContext(Document doc) { Document = doc; }

        public static HealthcareValidatorContext Build(Document doc)
        {
            var ctx = new HealthcareValidatorContext(doc);
            if (doc == null) return ctx;
            try
            {
                var rooms = new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_Rooms)
                    .WhereElementIsNotElementType().ToElements();
                foreach (var r in rooms)
                {
                    string cls = "";
                    try
                    {
                        var p = r.LookupParameter("CLN_ROOM_CLASS_TXT");
                        if (p != null && p.HasValue && p.StorageType == StorageType.String)
                            cls = (p.AsString() ?? "").Trim();
                    } catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
                    ctx.RoomById[r.Id.Value] = (r, cls);
                    if (!string.IsNullOrEmpty(cls))
                    {
                        ctx.ClinicalRooms.Add(r);
                        if (!ctx.RoomsByClass.TryGetValue(cls, out var list))
                        {
                            list = new List<Element>();
                            ctx.RoomsByClass[cls] = list;
                        }
                        list.Add(r);
                    }
                }
            }
            catch { /* best-effort cache; validators fall back to their own collector */ }

            // Clinical-equipment scan — one multi-category pass.
            try
            {
                var equipCats = new ElementMulticategoryFilter(new[] {
                    BuiltInCategory.OST_MedicalEquipment,
                    BuiltInCategory.OST_NurseCallDevices,
                    BuiltInCategory.OST_SpecialityEquipment
                });
                ctx.ClinicalEquipment = new FilteredElementCollector(doc)
                    .WherePasses(equipCats).WhereElementIsNotElementType()
                    .ToElements().ToList();
            }
            catch { /* fallback to per-validator collectors */ }
            return ctx;
        }
    }
}
