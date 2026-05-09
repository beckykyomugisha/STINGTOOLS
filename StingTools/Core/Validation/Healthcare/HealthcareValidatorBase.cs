// Healthcare Pack — H-5 validator base.
//
// All healthcare validators expose the same shape as the existing v4
// validators (Validate(Document) → List<ValidationResult>) so they
// chain into RunAllValidatorsCommand without changes. They are also
// re-aggregated by RunAllHealthcareValidatorsCommand so non-healthcare
// projects don't pay the cost.
//
// Healthcare findings are routed back through WarningsManager via
// existing WARN_BLE_MEDICAL_* / WARN_RGL_STD_MEDICAL_* / WARN_ASS_LEAD_TIME_*
// channels (Phase H-1 §5.0.3) so the existing warnings dashboard
// surfaces them without UI changes.

using Autodesk.Revit.DB;
using System;
using System.Collections.Generic;
using System.Linq;

namespace StingTools.Core.Validation.Healthcare
{
    public abstract class HealthcareValidatorBase
    {
        public abstract string Name { get; }
        public abstract List<ValidationResult> Validate(Document doc);

        protected static string GetParam(Element el, string name)
        {
            try
            {
                var p = el.LookupParameter(name);
                if (p == null || !p.HasValue) return "";
                if (p.StorageType == StorageType.String) return p.AsString() ?? "";
                if (p.StorageType == StorageType.Double) return p.AsDouble().ToString("F4");
                if (p.StorageType == StorageType.Integer) return p.AsInteger().ToString();
                if (p.StorageType == StorageType.ElementId) return p.AsElementId().Value.ToString();
                return p.AsValueString() ?? "";
            }
            catch { return ""; }
        }

        protected static double? GetParamDouble(Element el, string name)
        {
            try
            {
                var p = el.LookupParameter(name);
                if (p == null || !p.HasValue) return null;
                if (p.StorageType == StorageType.Double) return p.AsDouble();
                if (p.StorageType == StorageType.Integer) return (double)p.AsInteger();
                if (p.StorageType == StorageType.String && double.TryParse(p.AsString(), out var v)) return v;
                return null;
            }
            catch { return null; }
        }

        protected static bool GetParamBool(Element el, string name)
        {
            try
            {
                var p = el.LookupParameter(name);
                if (p == null || !p.HasValue) return false;
                if (p.StorageType == StorageType.Integer) return p.AsInteger() != 0;
                if (p.StorageType == StorageType.String)
                {
                    var s = (p.AsString() ?? "").Trim().ToUpperInvariant();
                    return s == "1" || s == "Y" || s == "YES" || s == "TRUE";
                }
                return false;
            }
            catch { return false; }
        }

        // ── Cache-aware helpers (opt-in) ────────────────────────────────────
        // When called inside RunAllHealthcareValidators the active
        // HealthcareValidatorContext supplies pre-collected rooms + the
        // CLN_ROOM_CLASS_TXT lookup; outside the chain (single-validator
        // commands) the helpers fall back to a fresh FilteredElementCollector.

        /// <summary>Returns every Room in the active document.</summary>
        protected static IEnumerable<Element> GetAllRoomsCached(Document doc)
        {
            var ctx = HealthcareValidatorContext.Active;
            if (ctx != null && ctx.Document == doc)
                return ctx.RoomById.Values.Select(t => t.room);
            return new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_Rooms)
                .WhereElementIsNotElementType().ToElements();
        }

        /// <summary>Returns the subset of rooms that have CLN_ROOM_CLASS_TXT populated.</summary>
        protected static IEnumerable<Element> GetClinicalRoomsCached(Document doc)
        {
            var ctx = HealthcareValidatorContext.Active;
            if (ctx != null && ctx.Document == doc) return ctx.ClinicalRooms;
            return GetAllRoomsCached(doc).Where(r => !string.IsNullOrEmpty(GetParam(r, "CLN_ROOM_CLASS_TXT")));
        }

        /// <summary>Reads CLN_ROOM_CLASS_TXT from cache (single dictionary lookup) or
        /// directly from the room element when no context is active.</summary>
        protected static string GetRoomClassCached(Element room)
        {
            if (room == null) return "";
            var ctx = HealthcareValidatorContext.Active;
            if (ctx != null && ctx.RoomById.TryGetValue(room.Id.Value, out var t))
                return t.roomClass ?? "";
            return GetParam(room, "CLN_ROOM_CLASS_TXT");
        }
    }
}
