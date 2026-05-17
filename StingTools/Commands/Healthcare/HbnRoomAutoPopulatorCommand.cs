// HC-15 — HBN Room Auto-Populator
// Given a selected Room element with CLN_ROOM_CLASS_TXT set, looks up the
// HTM / HBN design values from the standards tables and auto-populates:
//   CLN_DESIGN_ACH_INT            (air changes per hour target)
//   CLN_DESIGN_PRESSURE_DELTA_PA_INT  (Pa relative pressure to corridor)
//   CLN_DESIGN_TEMP_C_DBL         (°C dry-bulb set-point)
//   CLN_DESIGN_RH_PCT_INT         (% relative humidity)
//   CLN_NOISE_NR_TXT              (NR noise-rating target, e.g. NR-35)
//
// Values sourced from ASHRAE 170-2021, HTM 03-01, HBN facility-type chapters,
// and FGI 2022 (used where HBN is silent). The iHFG fallback table
// (StingTools.Standards.iHFG.IhfgStandards) is consulted when the room class
// is not recognised by the primary HTM/HBN table.

using System;
using System.Collections.Generic;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.UI;
using StingTools.Core;

namespace StingTools.Commands.Healthcare
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class HbnRoomAutoPopulatorCommand : IExternalCommand
    {
        // ── HTM 03-01 / ASHRAE 170 design parameter table ──────────────────────
        // Key: CLN_ROOM_CLASS_TXT value (case-insensitive)
        // Value: (ACH, DeltaPa, TempC, RhPct, NR)
        private static readonly Dictionary<string, (int Ach, int DeltaPa, double TempC, int RhPct, string Nr)>
            RoomDesignTable = new Dictionary<string, (int, int, double, int, string)>(StringComparer.OrdinalIgnoreCase)
        {
            // Operating rooms
            { "OR",              (20, 10, 18, 50, "NR-25") },
            { "OPERATING_THEATRE", (20, 10, 18, 50, "NR-25") },
            { "OR-HYBRID",       (25, 15, 18, 50, "NR-25") },
            { "OR-ORTHO",        (20, 10, 18, 50, "NR-25") },
            // ICU / Critical care
            { "ICU",             (12, 0,  22, 50, "NR-35") },
            { "ICU_BED_BAY",     (12, 0,  22, 50, "NR-35") },
            { "NICU",            (12, 0,  24, 50, "NR-35") },
            { "HDU",             (10, 0,  22, 50, "NR-35") },
            // Isolation / Infection Control
            { "AIIR",            (12, -8, 22, 50, "NR-40") },
            { "ISOLATION",       (12, -8, 22, 50, "NR-40") },
            { "PE_ROOM",         (12, 10, 22, 50, "NR-40") },   // Protective environment
            { "BMTU",            (12, 10, 22, 50, "NR-35") },
            // General ward
            { "WARD",            (6,  0,  22, 50, "NR-40") },
            { "WARD_ROOM",       (6,  0,  22, 50, "NR-40") },
            { "SIDE_ROOM",       (6,  0,  22, 50, "NR-40") },
            // Outpatient / Consulting
            { "OPD",             (6,  0,  22, 55, "NR-40") },
            { "CONSULTING",      (6,  0,  22, 55, "NR-40") },
            { "EXAMINATION",     (6,  0,  22, 55, "NR-45") },
            // Emergency Department
            { "ED",              (10, 0,  22, 50, "NR-40") },
            { "RESUS",           (15, 5,  22, 50, "NR-35") },
            { "TRIAGE",          (10, 0,  22, 55, "NR-45") },
            // Imaging
            { "CT",              (10, 0,  20, 50, "NR-40") },
            { "MRI",             (10, 0,  20, 50, "NR-35") },
            { "XRAY",            (10, 0,  20, 50, "NR-40") },
            { "FLUORO",          (10, 0,  20, 50, "NR-40") },
            { "IMAGING",         (10, 0,  20, 50, "NR-40") },
            { "NUCLEAR_MED",     (12, -8, 20, 50, "NR-40") },
            { "PET_CT",          (12, -8, 20, 50, "NR-40") },
            // Pharmacy / Clean rooms
            { "PHARMACY_ISO5",   (100, 10, 20, 50, "NR-45") },  // ISO 5 cleanroom
            { "PHARMACY_ISO7",   (30,  5,  20, 50, "NR-45") },
            { "PHARMACY_ISO8",   (20,  0,  20, 50, "NR-45") },
            // HSDU / Decon
            { "HSDU_DIRTY",      (10, -5, 20, 50, "NR-50") },
            { "HSDU_CLEAN",      (10,  5, 20, 50, "NR-50") },
            { "HSDU_STERILE",    (20, 10, 20, 50, "NR-50") },
            { "DECONTAMINATION", (10, -5, 18, 50, "NR-50") },
            // Maternity
            { "LDR",             (6,  0,  24, 55, "NR-35") },  // Labour, Delivery, Recovery
            { "LDRP",            (6,  0,  24, 55, "NR-35") },
            { "MATERNITY",       (6,  0,  24, 55, "NR-40") },
            // Mental health
            { "PSY_BED",         (6,  0,  22, 55, "NR-40") },
            { "MENTAL_HEALTH",   (6,  0,  22, 55, "NR-40") },
            { "PSYCHIATRIC",     (6,  0,  22, 55, "NR-40") },
            // Mortuary
            { "MORTUARY",        (10, -5, 15, 50, "NR-55") },
            { "POST_MORTEM",     (10, -5, 15, 50, "NR-55") },
            // Dialysis
            { "DIALYSIS",        (6,  0,  22, 55, "NR-40") },
            { "RENAL",           (6,  0,  22, 55, "NR-40") },
            // Endoscopy
            { "ENDOSCOPY",       (10, 0,  20, 50, "NR-40") },
            { "BRONCHOSCOPY",    (12, -8, 20, 50, "NR-40") },
            // Corridors / Support
            { "CORRIDOR_CLINICAL", (4, 0, 22, 60, "NR-50") },
            { "CLEAN_UTILITY",   (10, 5,  22, 55, "NR-50") },
            { "DIRTY_UTILITY",   (10,-5,  22, 55, "NR-55") },
            { "SLUICE",          (10,-5,  22, 60, "NR-55") },
            { "TREATMENT_ROOM",  (6,  0,  22, 55, "NR-45") },
        };

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                var ctx  = ParameterHelpers.GetContext(commandData);
                var doc  = ctx.Document;
                var uidoc = ctx.UIDocument;

                // Get selected rooms
                var selIds = uidoc.Selection.GetElementIds();
                if (selIds == null || selIds.Count == 0)
                {
                    TaskDialog.Show("HBN Auto-Populate", "Select one or more Room elements, then run this command.");
                    return Result.Cancelled;
                }

                int populated = 0, skipped = 0;
                using (var tx = new Transaction(doc, "STING HBN Room Auto-Populate"))
                {
                    tx.Start();
                    foreach (var id in selIds)
                    {
                        var el = doc.GetElement(id);
                        if (!(el is Room room))
                        {
                            skipped++;
                            continue;
                        }
                        string roomClass = ParameterHelpers.GetString(el, "CLN_ROOM_CLASS_TXT")?.Trim();
                        if (string.IsNullOrEmpty(roomClass))
                        {
                            StingLog.Warn($"HbnAutoPopulate: Room '{room.Name}' has no CLN_ROOM_CLASS_TXT — skipping.");
                            skipped++;
                            continue;
                        }

                        // Look up primary HTM/HBN table, fall back to iHFG stub if not found.
                        if (!RoomDesignTable.TryGetValue(roomClass, out var design))
                        {
                            // Attempt iHFG lookup (StingTools.Standards.iHFG.IhfgStandards).
                            try
                            {
                                var iHfgType = Type.GetType(
                                    "StingTools.Standards.iHFG.IhfgStandards, StingTools.Standards",
                                    throwOnError: false);
                                if (iHfgType != null)
                                {
                                    var mapProp = iHfgType.GetProperty("RoomDesignTable");
                                    if (mapProp?.GetValue(null) is Dictionary<string, (int, int, double, int, string)> iHfgMap
                                        && iHfgMap.TryGetValue(roomClass, out var iHfgVal))
                                    {
                                        design = iHfgVal;
                                        StingLog.Info($"HbnAutoPopulate: Room class '{roomClass}' resolved via iHFG fallback.");
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                StingLog.Warn($"HbnAutoPopulate: iHFG lookup failed for '{roomClass}': {ex.Message}");
                            }
                        }

                        if (design == default)
                        {
                            StingLog.Warn($"HbnAutoPopulate: No HTM/HBN/iHFG entry for room class '{roomClass}' (Room: {room.Name}) — skipping.");
                            skipped++;
                            continue;
                        }

                        // Write the five design parameters.
                        ParameterHelpers.SetInt(el, "CLN_DESIGN_ACH_INT", design.Ach);
                        ParameterHelpers.SetInt(el, "CLN_DESIGN_PRESSURE_DELTA_PA_INT", design.DeltaPa);
                        // SetString for double param (store as text representation; if bound as Double use Set).
                        SetDoubleSafe(el, "CLN_DESIGN_TEMP_C_DBL", design.TempC);
                        ParameterHelpers.SetInt(el, "CLN_DESIGN_RH_PCT_INT", design.RhPct);
                        ParameterHelpers.SetString(el, "CLN_NOISE_NR_TXT", design.Nr, overwrite: true);

                        StingLog.Info($"HbnAutoPopulate: Room '{room.Name}' ({roomClass}) → ACH={design.Ach} ΔPa={design.DeltaPa} T={design.TempC}°C RH={design.RhPct}% NR={design.Nr}");
                        populated++;
                    }
                    tx.Commit();
                }

                TaskDialog.Show("HBN Auto-Populate",
                    $"Populated {populated} room(s) with HTM/HBN/ASHRAE 170 design parameters.\n" +
                    (skipped > 0 ? $"{skipped} element(s) skipped (not a Room, or no CLN_ROOM_CLASS_TXT).\n" : "") +
                    "\nParameters written: CLN_DESIGN_ACH_INT, CLN_DESIGN_PRESSURE_DELTA_PA_INT, " +
                    "CLN_DESIGN_TEMP_C_DBL, CLN_DESIGN_RH_PCT_INT, CLN_NOISE_NR_TXT.");

                return populated > 0 ? Result.Succeeded : Result.Failed;
            }
            catch (Exception ex)
            {
                StingLog.Error("HbnRoomAutoPopulatorCommand failed", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }

        /// <summary>Writes a double value to a parameter that may be stored as Double or as a Text.</summary>
        private static void SetDoubleSafe(Element el, string paramName, double value)
        {
            try
            {
                var p = el.LookupParameter(paramName);
                if (p == null || p.IsReadOnly) return;
                if (p.StorageType == StorageType.Double)
                    p.Set(value);
                else if (p.StorageType == StorageType.String)
                    p.Set(value.ToString("F1", System.Globalization.CultureInfo.InvariantCulture));
            }
            catch (Exception ex)
            {
                StingLog.Warn($"HbnAutoPopulate: SetDouble '{paramName}' failed: {ex.Message}");
            }
        }
    }
}
