// HC-19 — iHFG (Australasian Health Facility Guidelines) standards stub.
// Provides a room-class design parameter lookup table sourced from:
//   - TAHPI iHFG Part B: Health Facility Briefing and Planning (2020 revision)
//   - FGI Guidelines for Design and Construction of Hospitals (2022 edition)
// This table is used as a FALLBACK by HbnRoomAutoPopulatorCommand when a
// room class is not found in the primary HTM 03-01 / ASHRAE 170 table.
//
// Value tuple: (ACH, DeltaPa, TempC, RhPct, NR)
//   ACH       — air changes per hour (design minimum)
//   DeltaPa   — relative pressure to adjacent corridor in Pa (+ve = positive, -ve = negative)
//   TempC     — dry-bulb set-point °C
//   RhPct     — relative humidity % (design target midpoint)
//   NR        — noise rating string (e.g. "NR-35")

using System;
using System.Collections.Generic;

namespace StingTools.Standards.iHFG
{
    /// <summary>
    /// TAHPI iHFG / FGI 2022 room-class design parameter lookup.
    /// Accessed by reflection from <c>HbnRoomAutoPopulatorCommand</c> as the
    /// iHFG fallback table so the Standards assembly remains optional.
    /// </summary>
    public static class IhfgStandards
    {
        // ── iHFG / FGI 2022 design parameter table ────────────────────────────
        // Key: CLN_ROOM_CLASS_TXT value (case-insensitive).
        // Deliberately exposes the same tuple shape as HbnRoomAutoPopulatorCommand
        // so the reflection cast succeeds without adaptation.
        public static readonly Dictionary<string, (int Ach, int DeltaPa, double TempC, int RhPct, string Nr)>
            RoomDesignTable = new Dictionary<string, (int, int, double, int, string)>(StringComparer.OrdinalIgnoreCase)
        {
            // ── Operating suites (iHFG HPU 0840 / FGI §2.1-3) ────────────────
            { "OPERATING_THEATRE",      (20, 10, 18.0, 50, "NR-25") },
            { "OR_GENERAL",             (20, 10, 18.0, 50, "NR-25") },
            { "OR_CARDIAC",             (25, 15, 18.0, 50, "NR-25") },
            { "OR_NEURO",               (20, 10, 18.0, 50, "NR-25") },
            { "OR_PAEDIATRIC",          (20, 10, 22.0, 50, "NR-25") },
            { "OR_PLASTICS",            (20, 10, 18.0, 50, "NR-25") },
            { "OR_HYBRID_IMAGING",      (25, 15, 18.0, 50, "NR-25") },
            { "OR_ROBOTIC",             (25, 10, 18.0, 50, "NR-25") },
            { "DAY_SURGERY_BAY",        (6,   0, 22.0, 55, "NR-35") },
            { "DAY_SURGERY_RECOVERY",   (6,   0, 22.0, 55, "NR-40") },
            { "PRE_OP",                 (6,   0, 22.0, 55, "NR-40") },
            { "ANAESTHETIC_INDUCTION",  (15, 10, 18.0, 50, "NR-35") },
            { "SCRUB_SINK",             (10, 10, 18.0, 50, "NR-45") },
            { "STERILE_STORE",          (4,   5, 20.0, 50, "NR-55") },
            // ── Critical care (iHFG HPU 0360 / FGI §2.1-2) ───────────────────
            { "ICU_BED_BAY",            (12,  0, 22.0, 50, "NR-35") },
            { "ICU_ISOLATION",          (12, -8, 22.0, 50, "NR-35") },
            { "ICU_POSITIVE_PRESSURE",  (12, 10, 22.0, 50, "NR-35") },
            { "PICU",                   (12,  0, 24.0, 50, "NR-35") },
            { "NICU_BED_BAY",           (12,  0, 26.0, 50, "NR-35") },
            { "NICU_ISOLATION",         (12, -8, 26.0, 50, "NR-35") },
            { "CARDIAC_CARE",           (12,  0, 22.0, 50, "NR-35") },
            { "HIGH_DEPENDENCY",        (10,  0, 22.0, 50, "NR-35") },
            // ── Infection control (iHFG / AS/NZS 3666) ────────────────────────
            { "AIRBORNE_ISO_ROOM",      (12, -8, 22.0, 50, "NR-40") },
            { "PROTECTIVE_ENV_ROOM",    (12, 10, 22.0, 50, "NR-40") },
            { "CONTACT_ISO_ROOM",       (6,  -5, 22.0, 50, "NR-40") },
            { "COHORT_ROOM",            (6,   0, 22.0, 50, "NR-40") },
            // ── General inpatient (iHFG HPU 0620) ─────────────────────────────
            { "INPATIENT_BED_BAY",      (6,   0, 22.0, 50, "NR-40") },
            { "INPATIENT_SINGLE_ROOM",  (6,   0, 22.0, 50, "NR-40") },
            { "INPATIENT_ENSUITE",      (10, -5, 22.0, 60, "NR-50") },
            { "BED_SHOWER_ENSUITE",     (10, -5, 22.0, 60, "NR-50") },
            { "PATIENT_LOUNGE",         (6,   0, 22.0, 55, "NR-40") },
            // ── Emergency (iHFG HPU 0470) ─────────────────────────────────────
            { "ED_TREATMENT_BAY",       (10,  0, 22.0, 50, "NR-40") },
            { "ED_RESUSCITATION",       (15,  5, 22.0, 50, "NR-35") },
            { "ED_TRIAGE",              (10,  0, 22.0, 55, "NR-45") },
            { "ED_WAITING",             (6,   0, 22.0, 60, "NR-45") },
            { "ED_DECONTAMINATION",     (10, -5, 18.0, 50, "NR-50") },
            { "ED_CONSULTATION",        (6,   0, 22.0, 55, "NR-40") },
            // ── Outpatient / Ambulatory (iHFG HPU 0300) ───────────────────────
            { "OPD_CONSULTATION",       (6,   0, 22.0, 55, "NR-40") },
            { "OPD_EXAMINATION",        (6,   0, 22.0, 55, "NR-45") },
            { "OPD_WAITING",            (6,   0, 22.0, 60, "NR-45") },
            { "OPD_PROCEDURE",          (10,  0, 22.0, 50, "NR-40") },
            { "ALLIED_HEALTH",          (6,   0, 22.0, 55, "NR-45") },
            { "PHYSIOTHERAPY",          (6,   0, 22.0, 55, "NR-45") },
            { "OCCUPATIONAL_THERAPY",   (6,   0, 22.0, 55, "NR-45") },
            // ── Endoscopy (iHFG HPU 0450 / FGI §2.1-5) ───────────────────────
            { "ENDOSCOPY_PROCEDURE",    (10,  0, 20.0, 50, "NR-40") },
            { "ENDOSCOPY_REPROCESSING", (10, -5, 20.0, 50, "NR-50") },
            { "BRONCHOSCOPY_ROOM",      (12, -8, 20.0, 50, "NR-40") },
            { "CYSTOSCOPY_ROOM",        (15, 10, 20.0, 50, "NR-35") },
            // ── Imaging / Radiology (iHFG HPU 0560) ───────────────────────────
            { "CT_SCANNER_ROOM",        (10,  0, 20.0, 50, "NR-40") },
            { "MRI_SCANNER_ROOM",       (10,  0, 20.0, 50, "NR-35") },
            { "XRAY_ROOM",              (10,  0, 20.0, 50, "NR-40") },
            { "FLUOROSCOPY_ROOM",       (10,  0, 20.0, 50, "NR-40") },
            { "ULTRASOUND_ROOM",        (6,   0, 22.0, 55, "NR-40") },
            { "MAMMOGRAPHY_ROOM",       (6,   0, 20.0, 55, "NR-40") },
            { "NUCLEAR_MED_SCAN",       (12, -8, 20.0, 50, "NR-40") },
            { "PET_CT_ROOM",            (12, -8, 20.0, 50, "NR-40") },
            { "ANGIOGRAPHY",            (20, 10, 18.0, 50, "NR-25") },  // Classified as procedure suite
            // ── Pharmacy / Sterile services ────────────────────────────────────
            { "PHARMACY_CLEANROOM_ISO5", (100,10, 20.0, 50, "NR-45") },
            { "PHARMACY_CLEANROOM_ISO7", (30,  5, 20.0, 50, "NR-45") },
            { "PHARMACY_CLEANROOM_ISO8", (20,  0, 20.0, 50, "NR-45") },
            { "STERILE_SUPPLY_DIRTY",    (10, -5, 20.0, 50, "NR-50") },
            { "STERILE_SUPPLY_CLEAN",    (10,  5, 20.0, 50, "NR-50") },
            { "STERILE_SUPPLY_STERILE",  (20, 10, 20.0, 50, "NR-50") },
            // ── Maternity (iHFG HPU 0590) ─────────────────────────────────────
            { "LDR_ROOM",               (6,   0, 24.0, 55, "NR-35") },
            { "LDRP_ROOM",              (6,   0, 24.0, 55, "NR-35") },
            { "BIRTH_SUITE",            (6,   0, 24.0, 55, "NR-35") },
            { "SPECIAL_CARE_NURSERY",   (10,  0, 26.0, 50, "NR-35") },
            { "POSTNATAL_BED",          (6,   0, 22.0, 55, "NR-40") },
            { "LACTATION_ROOM",         (6,   0, 22.0, 55, "NR-45") },
            // ── Mental health (iHFG HPU 0680) ─────────────────────────────────
            { "MENTAL_HEALTH_BED",      (6,   0, 22.0, 55, "NR-40") },
            { "PSYCHIATRIC_CONSULT",    (6,   0, 22.0, 55, "NR-40") },
            { "SECLUSION_ROOM",         (10, -5, 22.0, 55, "NR-45") },
            { "PICU_MH",                (12,  0, 22.0, 55, "NR-40") },  // Psychiatric ICU
            { "REHABILITATION_BED",     (6,   0, 22.0, 55, "NR-40") },
            { "GROUP_THERAPY",          (6,   0, 22.0, 55, "NR-45") },
            // ── Mortuary (iHFG HPU 0750) ──────────────────────────────────────
            { "BODY_HOLDING",           (10, -5, 10.0, 50, "NR-55") },
            { "AUTOPSY_ROOM",           (10, -5, 15.0, 50, "NR-55") },
            { "MORTUARY_REFRIGERATION", (10, -5, 10.0, 50, "NR-55") },
            { "FAMILY_VIEWING",         (6,   0, 22.0, 55, "NR-45") },
            // ── Renal / Dialysis (iHFG HPU 0780) ─────────────────────────────
            { "DIALYSIS_STATION",       (6,   0, 22.0, 55, "NR-40") },
            { "RENAL_PROCEDURE",        (6,   0, 22.0, 55, "NR-40") },
            { "HD_WATER_TREATMENT",     (10,  0, 20.0, 50, "NR-55") },
            // ── Support spaces ─────────────────────────────────────────────────
            { "CLINICAL_HANDWASH",      (10,  0, 22.0, 60, "NR-55") },
            { "CLEAN_UTILITY_ROOM",     (10,  5, 22.0, 55, "NR-50") },
            { "DIRTY_UTILITY_ROOM",     (10, -5, 22.0, 55, "NR-55") },
            { "MEDICATION_ROOM",        (6,   5, 22.0, 55, "NR-50") },
            { "STAFF_STATION",          (4,   0, 22.0, 60, "NR-50") },
            { "EQUIPMENT_STORE",        (4,   0, 22.0, 60, "NR-55") },
            { "CLEAN_LINEN_STORE",      (4,   5, 22.0, 50, "NR-55") },
            { "SOILED_LINEN_HOLD",      (10, -5, 22.0, 60, "NR-55") },
            { "WASTE_HOLD",             (10, -5, 22.0, 60, "NR-55") },
            { "CLINICAL_CORRIDOR",      (4,   0, 22.0, 60, "NR-50") },
        };

        /// <summary>
        /// Returns the design parameters for a room class, or <c>null</c> if not found.
        /// Used by the primary HbnRoomAutoPopulator via reflection as a second-chance lookup.
        /// </summary>
        public static (int Ach, int DeltaPa, double TempC, int RhPct, string Nr)?
            GetDesignParams(string roomClass)
        {
            if (string.IsNullOrWhiteSpace(roomClass)) return null;
            return RoomDesignTable.TryGetValue(roomClass.Trim(), out var val) ? val : null;
        }

        /// <summary>
        /// All iHFG room class codes. Useful for populating CLN_ROOM_CLASS_TXT
        /// picker lists in the project setup UI.
        /// </summary>
        public static IReadOnlyCollection<string> AllRoomClasses => RoomDesignTable.Keys;

        /// <summary>Checks whether a room class is covered by the iHFG table.</summary>
        public static bool IsKnownRoomClass(string roomClass) =>
            !string.IsNullOrWhiteSpace(roomClass) && RoomDesignTable.ContainsKey(roomClass.Trim());
    }
}
