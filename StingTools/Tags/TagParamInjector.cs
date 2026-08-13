// TagParamInjector.cs — shared shared-parameter inject guard for the tag path.
//
// Phase 196. CreateTagFamilies (and the sibling family-param inject paths) used
// to call FamilyManager.AddParameter from the shared-parameter file with no
// conflict handling. When the loaded MR_PARAMETERS.txt declares a gate param as
// one type (e.g. Text) but the family/template already holds it as another
// (e.g. Yes/No), Revit raises an Error-severity failure at commit → the
// unrecoverable "cannot be added (Text) — conflicts with existing Yes/No" modal.
//
// LoadSharedParamsCommand already solved this for the PROJECT: it (a) pre-skips
// GUID/name/type conflicts before AddParameter (its "step 3b") and (b) installs
// BindingWarningSwallower as a commit-time safety net. This helper gives every
// FAMILY-doc inject site the same two protections, scoped to the family
// document's SharedParameterElements, so all three call sites share identical
// logic and reuse the same BindingWarningSwallower (StingTools.Tags).
//
// Contract: a gate already present in the family as Yes/No is kept; the Text
// re-add is skipped. The family stays complete + correct and GateToken emits the
// bare if(GATE, …). No parameter types, data files, GateToken or formulas change.

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using StingTools.Core;

namespace StingTools.Tags
{
    internal static class TagParamInjector
    {
        public enum InjectResult { Added, SkippedExists, SkippedConflict, Failed }

        /// <summary>Per-family-document index of existing SharedParameterElements
        /// keyed by GUID and by name (with data-type id), mirroring
        /// LoadSharedParamsCommand step 3b but scoped to the family doc.</summary>
        public sealed class Index
        {
            public readonly Dictionary<Guid, (string name, string typeId)> ByGuid = new();
            public readonly Dictionary<string, (Guid guid, string typeId)> ByName =
                new(StringComparer.OrdinalIgnoreCase);
        }

        public static Index BuildIndex(Document famDoc)
        {
            var idx = new Index();
            if (famDoc == null) return idx;
            try
            {
                foreach (var spe in new FilteredElementCollector(famDoc)
                             .OfClass(typeof(SharedParameterElement)).Cast<SharedParameterElement>())
                {
                    try
                    {
                        Guid g = spe.GuidValue;
                        InternalDefinition d = spe.GetDefinition();
                        string name = d?.Name ?? "";
                        string typeId = null;
                        try { typeId = d?.GetDataType()?.TypeId; }
                        catch (Exception ex) { StingLog.Warn($"TagParamInjector GetDataType '{name}': {ex.Message}"); }
                        idx.ByGuid[g] = (name, typeId);
                        if (!string.IsNullOrEmpty(name)) idx.ByName[name] = (g, typeId);
                    }
                    catch (Exception ex) { StingLog.Warn($"TagParamInjector inspect SPE: {ex.Message}"); }
                }
            }
            catch (Exception ex) { StingLog.Warn($"TagParamInjector collect SPEs: {ex.Message}"); }
            return idx;
        }

        /// <summary>Add one shared parameter to the family, pre-skipping
        /// type/name/GUID conflicts (keep the family's existing definition).
        /// Callers must install <see cref="InstallSwallower"/> on the transaction
        /// as the commit-time safety net for anything that still slips through.</summary>
        public static InjectResult EnsureFamilyParam(
            FamilyManager fm, ExternalDefinition extDef, Index idx, ForgeTypeId group, bool isInstance)
        {
            if (fm == null || extDef == null) return InjectResult.Failed;

            // Already a FamilyParameter by name → nothing to do.
            foreach (FamilyParameter fp in fm.Parameters)
                if (string.Equals(fp?.Definition?.Name, extDef.Name, StringComparison.OrdinalIgnoreCase))
                    return InjectResult.SkippedExists;

            if (idx != null && IsConflict(extDef, idx, out string reason))
            {
                StingLog.Warn($"TagParamInjector: skip '{extDef.Name}' — {reason}; keeping the family's existing definition.");
                return InjectResult.SkippedConflict;
            }

            try { fm.AddParameter(extDef, group, isInstance); return InjectResult.Added; }
            catch (Exception ex)
            {
                StingLog.Warn($"TagParamInjector AddParameter '{extDef.Name}': {ex.Message}");
                return InjectResult.Failed;
            }
        }

        // Conflict test, mirroring LoadSharedParamsCommand step 3b at family scope.
        private static bool IsConflict(ExternalDefinition d, Index idx, out string reason)
        {
            reason = null;
            string newTypeId = null;
            try { newTypeId = d.GetDataType()?.TypeId; }
            catch (Exception ex) { StingLog.Warn($"TagParamInjector GetDataType def '{d.Name}': {ex.Message}"); }

            bool guidExists = idx.ByGuid.TryGetValue(d.GUID, out var existing);
            bool nameExists = idx.ByName.TryGetValue(d.Name, out var existingByName);
            if (!guidExists && !nameExists) return false; // brand-new param — safe to add

            bool typeIndeterminate = guidExists
                ? (existing.typeId == null || newTypeId == null)
                : (existingByName.typeId == null || newTypeId == null);
            bool nameMismatch = guidExists
                && !string.Equals(existing.name, d.Name, StringComparison.OrdinalIgnoreCase);
            bool typeMismatch = guidExists
                ? (!typeIndeterminate && !string.Equals(existing.typeId, newTypeId, StringComparison.Ordinal))
                : (!typeIndeterminate && !string.Equals(existingByName.typeId, newTypeId, StringComparison.Ordinal));
            bool nameOwnedByOtherGuid = !guidExists && nameExists && existingByName.guid != d.GUID;

            if (nameMismatch || typeMismatch || typeIndeterminate || nameOwnedByOtherGuid)
            {
                if (nameOwnedByOtherGuid) reason = $"name held by a different GUID ({existingByName.guid})";
                else if (nameMismatch)    reason = $"GUID already held by '{existing.name}'";
                else if (typeMismatch)    reason = $"family holds type {Short(guidExists ? existing.typeId : existingByName.typeId)}, file has {Short(newTypeId)}";
                else                      reason = "data type indeterminate on either side";
                return true;
            }
            return false; // same identity already present (not yet a FamilyParameter) — safe to add
        }

        private static string Short(string t)
        {
            if (string.IsNullOrEmpty(t)) return "?";
            int dot = t.LastIndexOf('.');
            string s = dot >= 0 && dot < t.Length - 1 ? t.Substring(dot + 1) : t;
            int dash = s.IndexOf('-');
            return dash > 0 ? s.Substring(0, dash) : s;
        }

        /// <summary>Install the shared-parameter-conflict failure swallower on a
        /// transaction (call BEFORE tx.Start()). Reuses BindingWarningSwallower —
        /// never forks it — so any Error-severity "shared parameter … conflicts"
        /// that still reaches commit is dismissed instead of shown as a modal.</summary>
        public static void InstallSwallower(Transaction tx)
        {
            if (tx == null) return;
            try
            {
                var o = tx.GetFailureHandlingOptions();
                o.SetFailuresPreprocessor(new BindingWarningSwallower());
                o.SetClearAfterRollback(true);
                tx.SetFailureHandlingOptions(o);
            }
            catch (Exception ex) { StingLog.Warn($"TagParamInjector InstallSwallower: {ex.Message}"); }
        }
    }
}
