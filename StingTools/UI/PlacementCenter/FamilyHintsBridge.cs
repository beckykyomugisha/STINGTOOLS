// Phase 127-C — Family-side hints bridge.
//
// Bridges the centre's Rule view-models to the family-type parameters
// that drive placement and clearance. Two flows:
//
//   Inspect → reads §5.1 placement hints and Pack 2 directional
//             clearances from a sample family-type instance in the
//             selected category. Returns a flat list of named values
//             the centre's right-hand panel renders read-only.
//
//   Push    → writes the centre's currently-edited rule values back to
//             every family type matching the selected category. Wraps
//             the writes in a single TransactionGroup so the operation
//             is undoable as a unit. Confirmed by the caller — this
//             function does not show its own dialog.

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using StingTools.Core;
using StingTools.Core.Placement;

namespace StingTools.UI.PlacementCenter
{
    public static class FamilyHintsBridge
    {
        /// <summary>One row in the family-defaults panel.</summary>
        public class HintRow
        {
            public string Name { get; set; } = "";
            public string Value { get; set; } = "";
            public string Unit { get; set; } = "";
            public string Source { get; set; } = ""; // "type" | "instance" | "(empty)"
        }

        /// <summary>
        /// Read all PLACE_* + STING_*_VARIANT/ROOM_TYPE + Pack 2 clearance
        /// params from a sample family in the given category. Picks the
        /// first FamilyInstance whose category name matches; falls back
        /// to FamilySymbol when no instance exists in the project.
        /// </summary>
        public static List<HintRow> Inspect(Document doc, string categoryName)
        {
            var rows = new List<HintRow>();
            if (doc == null || string.IsNullOrEmpty(categoryName)) return rows;

            try
            {
                Element sampleType = ResolveSampleType(doc, categoryName);
                Element sampleInst = ResolveSampleInstance(doc, categoryName);

                Add(rows, sampleType, sampleInst, "PLACE_HOST_TYPE_TXT",        "");
                Add(rows, sampleType, sampleInst, "PLACE_MOUNT_HEIGHT_MM",      "mm");
                Add(rows, sampleType, sampleInst, "PLACE_OFFSET_X_MM",          "mm");
                Add(rows, sampleType, sampleInst, "PLACE_MIN_SPACING_MM",       "mm");
                Add(rows, sampleType, sampleInst, "PLACE_ANCHOR_TXT",           "");
                Add(rows, sampleType, sampleInst, "PLACE_SIDE_TXT",             "");
                Add(rows, sampleType, sampleInst, "PLACE_PRIORITY_INT",         "");
                Add(rows, sampleType, sampleInst, "PLACE_MAX_PER_ROOM_INT",     "");
                Add(rows, sampleType, sampleInst, "PLACE_SPACING_RULE_TXT",     "");
                Add(rows, sampleType, sampleInst, "PLACE_ORIENTATION_RULE_TXT", "");
                Add(rows, sampleType, sampleInst, "PLACE_LEVEL_HINT_TXT",       "");
                Add(rows, sampleType, sampleInst, "PLACE_GROUP_KEY_TXT",        "");
                Add(rows, sampleType, sampleInst, "PLACE_WEIGHT_KG",            "kg");
                Add(rows, sampleType, sampleInst, "STING_FIXTURE_VARIANT_TXT",  "");
                Add(rows, sampleType, sampleInst, "STING_ROOM_TYPE_FILTER_TXT", "");
                Add(rows, sampleType, sampleInst, "STING_PLACEMENT_NOTES_TXT",  "");

                Add(rows, sampleType, sampleInst, "STING_CLEARANCE_MM",         "mm");
                Add(rows, sampleType, sampleInst, "STING_CLEARANCE_FRONT_MM",   "mm");
                Add(rows, sampleType, sampleInst, "STING_CLEARANCE_BACK_MM",    "mm");
                Add(rows, sampleType, sampleInst, "STING_CLEARANCE_SIDE_MM",    "mm");
                Add(rows, sampleType, sampleInst, "STING_CLEARANCE_TOP_MM",     "mm");

                Add(rows, sampleType, sampleInst, "MNT_ENV_W_MM",               "mm");
                Add(rows, sampleType, sampleInst, "MNT_ENV_D_MM",               "mm");
                Add(rows, sampleType, sampleInst, "MNT_ENV_H_MM",               "mm");
                Add(rows, sampleType, sampleInst, "MNT_ACCESS_DIR_TXT",         "");

                Add(rows, sampleType, sampleInst, "CLASH_PRIORITY_INT",         "");
                Add(rows, sampleType, sampleInst, "CLASH_SOFT_TOLERANCE_MM",    "mm");
                Add(rows, sampleType, sampleInst, "FIRE_SEP_MM",                "mm");
            }
            catch (Exception ex) { StingLog.Warn($"FamilyHintsBridge.Inspect: {ex.Message}"); }
            return rows;
        }

        /// <summary>
        /// PC-11 — clearance / weight / envelope edits the Centre's
        /// new "Clearance" group writes. Optional; values not set are
        /// not pushed.
        /// </summary>
        public class PushExtras
        {
            public double? ClearanceFrontMm;
            public double? ClearanceBackMm;
            public double? ClearanceSideMm;
            public double? ClearanceTopMm;
            public double? ClearanceMm;       // omnidirectional
            public double? WeightKg;
            public double? EnvWMm;
            public double? EnvDMm;
            public double? EnvHMm;
            public double? FireSepMm;
        }

        /// <summary>
        /// Push the rule's geometry + variant values onto every family
        /// type in the category. Returns (typesUpdated, paramsWritten).
        /// PC-11 adds clearance / weight / envelope writes via PushExtras.
        /// </summary>
        public static (int types, int writes) PushRuleToFamilyTypes(
            Document doc, PlacementRuleViewModel vm, PushExtras extras = null)
        {
            int typesUpdated = 0, writes = 0;
            if (doc == null || vm == null || string.IsNullOrEmpty(vm.CategoryFilter))
                return (0, 0);

            var symbols = new FilteredElementCollector(doc)
                .OfClass(typeof(FamilySymbol))
                .Cast<FamilySymbol>()
                .Where(fs => fs.Category != null &&
                             string.Equals(fs.Category.Name, vm.CategoryFilter, StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (symbols.Count == 0) return (0, 0);

            try
            {
                using (var t = new Transaction(doc, "STING — Push placement hints to family types"))
                {
                    t.Start();
                    foreach (var sym in symbols)
                    {
                        bool any = false;

                        // Identity / variant
                        if (TrySetString(sym, "PLACE_ORIENTATION_RULE_TXT", "")) { writes++; any = true; }
                        if (TrySetString(sym, "STING_FIXTURE_VARIANT_TXT",  vm.VariantHint)) { writes++; any = true; }
                        if (TrySetString(sym, "STING_ROOM_TYPE_FILTER_TXT", vm.RoomFilter)) { writes++; any = true; }

                        // Geometry — full rule set (PC-06 expanded: X, Y, Z, rotation, mount).
                        if (vm.MountingHeightMm > 0 &&
                            TrySetLengthMm(sym, "PLACE_MOUNT_HEIGHT_MM", vm.MountingHeightMm)) { writes++; any = true; }
                        if (TrySetLengthMm(sym, "PLACE_OFFSET_X_MM", vm.OffsetXMm)) { writes++; any = true; }
                        if (TrySetLengthMm(sym, "PLACE_OFFSET_Y_MM", vm.OffsetYMm)) { writes++; any = true; }
                        if (TrySetLengthMm(sym, "PLACE_OFFSET_Z_MM", vm.OffsetZMm)) { writes++; any = true; }
                        if (TrySetString(sym, "PLACE_MOUNT_REFERENCE_TXT", vm.MountingReference ?? "FFL")) { writes++; any = true; }
                        if (vm.MinSpacingMm >= 0 &&
                            TrySetLengthMm(sym, "PLACE_MIN_SPACING_MM", vm.MinSpacingMm)) { writes++; any = true; }

                        // Placement-rule discriminators
                        if (TrySetString(sym, "PLACE_ANCHOR_TXT",        vm.Model.AnchorType    ?? "ROOM_CENTRE")) { writes++; any = true; }
                        if (TrySetString(sym, "PLACE_SIDE_TXT",          vm.Model.SideConstraint ?? "EITHER"))     { writes++; any = true; }
                        if (TrySetInteger(sym, "PLACE_PRIORITY_INT",     vm.Priority))     { writes++; any = true; }
                        if (TrySetInteger(sym, "PLACE_MAX_PER_ROOM_INT", vm.MaxPerRoom))   { writes++; any = true; }

                        // Free-text notes + standards
                        if (TrySetString(sym, "STING_PLACEMENT_NOTES_TXT", vm.Notes       ?? "")) { writes++; any = true; }
                        if (TrySetString(sym, "STING_STANDARD_REF_TXT",    vm.StandardRef ?? "")) { writes++; any = true; }
                        if (TrySetString(sym, "STING_UNICLASS_PR_TXT",     vm.UniclassPr  ?? "")) { writes++; any = true; }

                        // PC-11 — clearance / weight / envelope / fire separation
                        if (extras != null)
                        {
                            if (extras.ClearanceMm.HasValue       && TrySetLengthMm(sym, "STING_CLEARANCE_MM",       extras.ClearanceMm.Value))       { writes++; any = true; }
                            if (extras.ClearanceFrontMm.HasValue  && TrySetLengthMm(sym, "STING_CLEARANCE_FRONT_MM", extras.ClearanceFrontMm.Value))  { writes++; any = true; }
                            if (extras.ClearanceBackMm.HasValue   && TrySetLengthMm(sym, "STING_CLEARANCE_BACK_MM",  extras.ClearanceBackMm.Value))   { writes++; any = true; }
                            if (extras.ClearanceSideMm.HasValue   && TrySetLengthMm(sym, "STING_CLEARANCE_SIDE_MM",  extras.ClearanceSideMm.Value))   { writes++; any = true; }
                            if (extras.ClearanceTopMm.HasValue    && TrySetLengthMm(sym, "STING_CLEARANCE_TOP_MM",   extras.ClearanceTopMm.Value))    { writes++; any = true; }
                            if (extras.WeightKg.HasValue          && TrySetDouble (sym, "PLACE_WEIGHT_KG",           extras.WeightKg.Value))          { writes++; any = true; }
                            if (extras.EnvWMm.HasValue            && TrySetLengthMm(sym, "MNT_ENV_W_MM",             extras.EnvWMm.Value))            { writes++; any = true; }
                            if (extras.EnvDMm.HasValue            && TrySetLengthMm(sym, "MNT_ENV_D_MM",             extras.EnvDMm.Value))            { writes++; any = true; }
                            if (extras.EnvHMm.HasValue            && TrySetLengthMm(sym, "MNT_ENV_H_MM",             extras.EnvHMm.Value))            { writes++; any = true; }
                            if (extras.FireSepMm.HasValue         && TrySetLengthMm(sym, "FIRE_SEP_MM",              extras.FireSepMm.Value))         { writes++; any = true; }
                        }

                        if (any) typesUpdated++;
                    }
                    t.Commit();
                }
            }
            catch (Exception ex)
            {
                StingLog.Error("FamilyHintsBridge.PushRuleToFamilyTypes", ex);
            }
            return (typesUpdated, writes);
        }

        private static bool TrySetDouble(Element el, string paramName, double value)
        {
            if (el == null || string.IsNullOrEmpty(paramName)) return false;
            try
            {
                var p = el.LookupParameter(paramName);
                if (p == null || p.IsReadOnly) return false;
                if (p.StorageType == StorageType.Double)
                {
                    if (Math.Abs(p.AsDouble() - value) < 1e-6) return false;
                    p.Set(value); return true;
                }
                if (p.StorageType == StorageType.Integer)
                {
                    int next = (int)Math.Round(value);
                    if (p.AsInteger() == next) return false;
                    p.Set(next); return true;
                }
            }
            catch { }
            return false;
        }

        // ── Helpers ──────────────────────────────────────────────────

        private static FamilySymbol ResolveSampleType(Document doc, string catName)
        {
            try
            {
                return new FilteredElementCollector(doc)
                    .OfClass(typeof(FamilySymbol))
                    .Cast<FamilySymbol>()
                    .FirstOrDefault(fs => fs.Category != null &&
                                          string.Equals(fs.Category.Name, catName, StringComparison.OrdinalIgnoreCase));
            }
            catch { return null; }
        }

        private static Element ResolveSampleInstance(Document doc, string catName)
        {
            try
            {
                return new FilteredElementCollector(doc)
                    .OfClass(typeof(FamilyInstance))
                    .WhereElementIsNotElementType()
                    .Cast<Element>()
                    .FirstOrDefault(e => e.Category != null &&
                                         string.Equals(e.Category.Name, catName, StringComparison.OrdinalIgnoreCase));
            }
            catch { return null; }
        }

        private static void Add(List<HintRow> rows, Element sampleType, Element sampleInst,
                                string paramName, string unit)
        {
            string typeVal = ReadAny(sampleType, paramName);
            string instVal = ReadAny(sampleInst, paramName);
            string val = !string.IsNullOrEmpty(typeVal) ? typeVal :
                         !string.IsNullOrEmpty(instVal) ? instVal : "";
            string src = !string.IsNullOrEmpty(typeVal) ? "type" :
                         !string.IsNullOrEmpty(instVal) ? "instance" : "(empty)";
            rows.Add(new HintRow { Name = paramName, Value = val, Unit = unit, Source = src });
        }

        private static string ReadAny(Element el, string paramName)
        {
            if (el == null) return "";
            try
            {
                var p = el.LookupParameter(paramName);
                if (p == null || !p.HasValue) return "";
                switch (p.StorageType)
                {
                    case StorageType.String:  return p.AsString() ?? "";
                    case StorageType.Integer: return p.AsInteger().ToString(System.Globalization.CultureInfo.InvariantCulture);
                    case StorageType.Double:
                        double v = p.AsDouble();
                        // Length params in Revit are stored in feet; convert to mm
                        // for parameter names ending in _MM. Cheap sniff vs proper
                        // unit lookup.
                        if (paramName.EndsWith("_MM", StringComparison.Ordinal))
                            v *= 304.8;
                        return v.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
                }
            }
            catch { }
            return "";
        }

        private static bool TrySetString(Element el, string paramName, string value)
        {
            if (el == null || string.IsNullOrEmpty(paramName)) return false;
            try
            {
                var p = el.LookupParameter(paramName);
                if (p == null || p.IsReadOnly || p.StorageType != StorageType.String) return false;
                string current = p.AsString() ?? "";
                string target = value ?? "";
                if (current == target) return false;
                p.Set(target);
                return true;
            }
            catch { return false; }
        }

        private static bool TrySetInteger(Element el, string paramName, int value)
        {
            if (el == null || string.IsNullOrEmpty(paramName)) return false;
            try
            {
                var p = el.LookupParameter(paramName);
                if (p == null || p.IsReadOnly || p.StorageType != StorageType.Integer) return false;
                if (p.AsInteger() == value) return false;
                p.Set(value);
                return true;
            }
            catch { return false; }
        }

        private static bool TrySetLengthMm(Element el, string paramName, double valueMm)
        {
            if (el == null) return false;
            try
            {
                var p = el.LookupParameter(paramName);
                if (p == null || p.IsReadOnly) return false;
                if (p.StorageType == StorageType.Double)
                {
                    double feet = valueMm / 304.8;
                    if (Math.Abs(p.AsDouble() - feet) < 1e-6) return false;
                    p.Set(feet);
                    return true;
                }
                if (p.StorageType == StorageType.Integer)
                {
                    int curr = p.AsInteger();
                    int next = (int)Math.Round(valueMm);
                    if (curr == next) return false;
                    p.Set(next);
                    return true;
                }
            }
            catch { }
            return false;
        }
    }
}
