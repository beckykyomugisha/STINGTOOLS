// StingTools — the Revit half of SeedFollowTypeCatalog: read an instance's followsType
// values, write the ones the rule says change. Shared by SeedTypeSwapUpdater (a type
// swap) and SeedTypeMigrator (a type rename/merge on Build Seeds).
//
// Text parameters are read and written as strings. Unitless Number and Integer
// parameters (ELC_PNL_MAIN_BRK_A, ELC_PNL_NUM_OF_WAYS_NR, ...) are read as their value,
// so "630" in the seed matches 630.0 in the model. Other specs (Length, ...) are left
// alone: the seed's text value is not in Revit's internal units.

using System;
using System.Collections.Generic;
using System.Globalization;
using Autodesk.Revit.DB;

namespace StingTools.Core.Symbols
{
    public static class SeedFollowTypeApplier
    {
        /// <summary>
        /// Restamps <paramref name="inst"/>'s followsType values for its move to
        /// <paramref name="newTypeName"/>. Returns the number of parameters written; each
        /// failure is reported through <paramref name="warn"/> and does not stop the rest.
        /// </summary>
        public static int Apply(FamilyInstance inst, string familyName, string newTypeName,
            SeedFollowTypeCatalog catalog, string oldTypeName, Action<string> warn)
        {
            if (inst == null || catalog == null) return 0;
            var writes = catalog.AfterTypeChange(familyName, newTypeName, name => Read(inst, name), oldTypeName);
            int n = 0;
            foreach (var w in writes)
            {
                try
                {
                    if (Write(inst.LookupParameter(w.Key), w.Value)) n++;
                    else warn?.Invoke($"{inst.Id}: {w.Key} not set to '{w.Value}' (read-only or no blank value for its type).");
                }
                catch (Exception ex) { warn?.Invoke($"{inst.Id}: {w.Key} not set — {ex.Message}"); }
            }
            return n;
        }

        /// <summary>The instance value as text; null when the element does not carry it (or its spec is not handled).</summary>
        private static string Read(Element el, string name)
        {
            var p = el.LookupParameter(name);
            if (p == null) return null;
            switch (p.StorageType)
            {
                case StorageType.String:
                    return p.AsString() ?? "";
                case StorageType.Integer:
                    return p.HasValue ? p.AsInteger().ToString(CultureInfo.InvariantCulture) : "";
                case StorageType.Double:
                    if (!IsUnitless(p)) return null;
                    return p.HasValue ? p.AsDouble().ToString("R", CultureInfo.InvariantCulture) : "";
                default:
                    return null;
            }
        }

        private static bool Write(Parameter p, string value)
        {
            if (p == null || p.IsReadOnly) return false;
            value = value ?? "";
            switch (p.StorageType)
            {
                case StorageType.String:
                    return p.Set(value);
                case StorageType.Integer:
                    return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int i) && p.Set(i);
                case StorageType.Double:
                    return IsUnitless(p)
                        && double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double d)
                        && p.Set(d);
                default:
                    return false;
            }
        }

        private static bool IsUnitless(Parameter p)
        {
            try { return p.Definition?.GetDataType()?.TypeId == SpecTypeId.Number.TypeId; }
            catch (Exception ex) { StingLog.Warn($"SeedFollowTypeApplier spec {p.Definition?.Name}: {ex.Message}"); return false; }
        }
    }
}
