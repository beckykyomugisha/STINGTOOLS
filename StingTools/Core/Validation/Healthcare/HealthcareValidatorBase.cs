using StingTools.Core.Validation;
using StingTools.Core;
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
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

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
            catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); return ""; }
        }

        // Leading numeric token, InvariantCulture — tolerates values that carry a
        // trailing unit/annotation ("12 ACH", "0.6 s", "45 dB") because several
        // healthcare params are registered TEXT rather than Double (HVC_AIR_CHANGES_PER_HR,
        // PER_ACOUSTICS_BACKGROUND_NOISE_DB, PER_ACOUSTICS_RT60_S, PLM_HOTWTR_TEMP_C).
        private static readonly Regex LeadingNumber =
            new Regex(@"[-+]?\d*\.?\d+(?:[eE][-+]?\d+)?", RegexOptions.Compiled);

        /// <summary>Parses the first numeric token in a string, ignoring any trailing
        /// unit/annotation. Returns null for empty input; logs a warning for a
        /// non-empty value that yields no number so a malformed cell is visible.</summary>
        internal static double? TryParseNumericText(string raw, string paramName = null)
        {
            if (string.IsNullOrWhiteSpace(raw)) return null;
            var trimmed = raw.Trim();
            // Fast path: clean numeric string parses identically to the old behaviour.
            if (double.TryParse(trimmed, NumberStyles.Float | NumberStyles.AllowLeadingSign,
                                CultureInfo.InvariantCulture, out var clean)) return clean;
            var m = LeadingNumber.Match(trimmed);
            if (m.Success && double.TryParse(m.Value, NumberStyles.Float | NumberStyles.AllowLeadingSign,
                                             CultureInfo.InvariantCulture, out var v)) return v;
            StingLog.Warn($"GetParamDouble: TEXT value '{raw}' for parameter " +
                          $"'{paramName ?? "?"}' is not numeric; check skipped.");
            return null;
        }

        protected static double? GetParamDouble(Element el, string name)
        {
            try
            {
                var p = el.LookupParameter(name);
                if (p == null || !p.HasValue) return null;
                if (p.StorageType == StorageType.Double) return p.AsDouble();
                if (p.StorageType == StorageType.Integer) return (double)p.AsInteger();
                if (p.StorageType == StorageType.String) return TryParseNumericText(p.AsString(), name);
                return null;
            }
            catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); return null; }
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
            catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); return false; }
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

        /// <summary>Returns Medical Equipment + Nurse Call Devices + Specialty
        /// Equipment in one shot — collected once when the context is active.</summary>
        protected static IEnumerable<Element> GetClinicalEquipmentCached(Document doc)
        {
            var ctx = HealthcareValidatorContext.Active;
            if (ctx != null && ctx.Document == doc) return ctx.ClinicalEquipment;
            try
            {
                var cats = new ElementMulticategoryFilter(new[] {
                    BuiltInCategory.OST_MedicalEquipment,
                    BuiltInCategory.OST_NurseCallDevices,
                    BuiltInCategory.OST_SpecialityEquipment
                });
                return new FilteredElementCollector(doc).WherePasses(cats)
                    .WhereElementIsNotElementType().ToElements();
            }
            catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); return System.Linq.Enumerable.Empty<Element>(); }
        }
    }
}
