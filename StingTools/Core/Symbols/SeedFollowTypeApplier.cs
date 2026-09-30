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

        /// <summary>
        /// Write <paramref name="value"/> ("" clears) as SeedFollowTypeWrite.Plan says for
        /// the parameter's storage. True when the parameter now holds it — including a clear
        /// on a parameter that was already empty, and a clear Revit refuses (logged once as
        /// Info: the value stays, which is what the rule would leave for a user's value).
        /// </summary>
        private static bool Write(Parameter p, string value)
        {
            if (p == null || p.IsReadOnly) return false;
            value = value ?? "";
            FollowStorage storage;
            switch (p.StorageType)
            {
                case StorageType.String:    storage = FollowStorage.Text; break;
                case StorageType.Integer:   storage = FollowStorage.Integer; break;
                case StorageType.Double:    storage = FollowStorage.Double; break;
                case StorageType.ElementId: storage = FollowStorage.ElementId; break;
                default:                    storage = FollowStorage.Other; break;
            }
            bool unitless = storage == FollowStorage.Double && IsUnitless(p);
            switch (SeedFollowTypeWrite.Plan(storage, unitless, p.HasValue, value, out int i, out double d))
            {
                case FollowWriteAction.SetText:      return p.Set(value);
                case FollowWriteAction.SetInteger:   return p.Set(i);
                case FollowWriteAction.SetDouble:    return p.Set(d);
                case FollowWriteAction.AlreadyClear: return true;
                case FollowWriteAction.Clear:
                    try { p.ClearValue(); return true; }
                    catch (Exception ex)
                    {
                        LogClearUnsupported(p, ex);
                        return true;
                    }
                default: return false;
            }
        }

        private static readonly HashSet<string> _clearUnsupportedLogged = new HashSet<string>(StringComparer.Ordinal);

        private static void LogClearUnsupported(Parameter p, Exception ex)
        {
            string name = p.Definition?.Name ?? "?";
            lock (_clearUnsupportedLogged)
                if (!_clearUnsupportedLogged.Add(name)) return;
            StingLog.Info($"SeedFollowTypeApplier: '{name}' ({p.StorageType}) cannot be cleared by the API ({ex.Message}); "
                        + "its value is left as it is when the new type declares none. Logged once per parameter.");
        }

        private static bool IsUnitless(Parameter p)
        {
            try { return p.Definition?.GetDataType()?.TypeId == SpecTypeId.Number.TypeId; }
            catch (Exception ex) { StingLog.Warn($"SeedFollowTypeApplier spec {p.Definition?.Name}: {ex.Message}"); return false; }
        }
    }
}
