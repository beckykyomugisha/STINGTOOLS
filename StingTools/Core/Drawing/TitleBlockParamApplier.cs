// StingTools — Drawing Template Manager · Bonus (4)
//
// TitleBlockParamApplier walks DrawingType.TitleBlockParams at
// sheet-creation time and writes each entry onto the title-block
// FamilyInstance hosted by the sheet. The value template supports
// two substitution kinds:
//
//   ${ParamName}   → read from ProjectInformation by parameter name.
//                    Missing / empty param → substituted with empty
//                    string (does not abort the run).
//   {disc} / {lvl} / {seq:Dn} / {mark} / ...
//                    → caller-supplied token dict. Lets a fabrication
//                    pipeline that already resolved {disc}=P /
//                    {lvl}=L02 flow those values straight into
//                    "Sheet Status" / "Sheet Code" title-block cells.
//
// Unknown tokens are left as literal text so shops can mix custom
// markers the applier does not know about.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Autodesk.Revit.DB;

namespace StingTools.Core.Drawing
{
    public sealed class TitleBlockApplyResult
    {
        public int ParamsWritten { get; set; }
        public int ParametersDeclared { get; set; }
        public List<string> ParametersMissing { get; } = new List<string>();
        public List<string> Warnings { get; } = new List<string>();
    }

    public static class TitleBlockParamApplier
    {
        private static readonly Regex _projInfo =
            new Regex(@"\$\{([A-Za-z0-9_]+)\}", RegexOptions.Compiled);
        private static readonly Regex _token =
            new Regex(@"\{([A-Za-z0-9_]+(?::D\d+)?)\}", RegexOptions.Compiled);
        private static readonly Regex _seqFmt =
            new Regex(@"^(\w+):D(\d+)$", RegexOptions.Compiled);

        /// <summary>
        /// Apply dt.TitleBlockParams to the title-block instance on the
        /// given sheet. Returns counts + warnings; never throws.
        /// </summary>
        /// <param name="tokens">
        /// Optional token dict — e.g. {"disc":"P","lvl":"L02","seq":"0003"}.
        /// Null/empty = only ${ProjectInfo} substitution runs.
        /// </param>
        public static TitleBlockApplyResult Apply(
            Document doc, ViewSheet sheet, DrawingType dt,
            IDictionary<string, string> tokens = null)
        {
            var r = new TitleBlockApplyResult();
            if (doc == null || sheet == null || dt?.TitleBlockParams == null
                || dt.TitleBlockParams.Count == 0) return r;

            // GAP-A: walk every title-block instance on the sheet, not just
            // the first. Sheets that host more than one TB (front + back,
            // landscape + portrait variants) used to leave the second
            // instance with stale values; now every TB receives the same
            // declarative payload.
            var tbs = FindAllTitleBlockInstances(doc, sheet);
            if (tbs.Count == 0)
            {
                r.Warnings.Add($"Sheet '{sheet.SheetNumber}' has no title block to stamp.");
                return r;
            }

            foreach (var tb in tbs)
            {
                // GAP-M: a secondary title block (e.g. a North arrow or a
                // fabrication-only stamp on the same sheet) typically has
                // zero of the declared keys. Skip silently rather than
                // emitting a "no parameter" warning per key per secondary TB.
                if (tbs.Count > 1 && !TitleBlockHasAnyKey(tb, dt.TitleBlockParams.Keys))
                    continue;

            foreach (var kv in dt.TitleBlockParams)
            {
                var paramName = kv.Key;
                if (string.IsNullOrWhiteSpace(paramName)) continue;

                string resolved;
                try
                {
                    // ACC-07: a null/empty template still resolves to the
                    // empty string and writes through, ensuring cloned
                    // sheets don't carry stale prior values forward.
                    resolved = ResolveTemplate(doc, kv.Value ?? "", tokens);
                }
                catch (Exception ex)
                {
                    r.Warnings.Add($"Resolve '{paramName}': {ex.Message}");
                    continue;
                }

                try
                {
                    var p = tb.LookupParameter(paramName);
                    if (p == null)
                    {
                        r.Warnings.Add($"Title block has no parameter '{paramName}'.");
                        continue;
                    }
                    if (p.IsReadOnly)
                    {
                        r.Warnings.Add($"Parameter '{paramName}' is read-only.");
                        continue;
                    }
                    switch (p.StorageType)
                    {
                        case StorageType.String:
                            // ACC-07: always set, even for empty string,
                            // so cloned/template sheets reset stale text.
                            p.Set(resolved ?? string.Empty);
                            break;
                        case StorageType.Integer:
                            if (string.IsNullOrEmpty(resolved)) p.Set(0);
                            else if (int.TryParse(resolved, out var iv)) p.Set(iv);
                            else r.Warnings.Add($"'{paramName}' expects integer; '{resolved}' not parsable.");
                            break;
                        case StorageType.Double:
                            if (string.IsNullOrEmpty(resolved)) p.Set(0.0);
                            else if (double.TryParse(resolved, out var dv)) p.Set(dv);
                            else r.Warnings.Add($"'{paramName}' expects number; '{resolved}' not parsable.");
                            break;
                        default:
                            r.Warnings.Add($"'{paramName}' has unsupported storage type {p.StorageType}.");
                            continue;
                    }
                    r.ParamsWritten++;
                }
                catch (Exception ex)
                {
                    r.Warnings.Add($"Write '{paramName}': {ex.Message}");
                }
            }
            } // end per-TB block (GAP-M)
            return r;
        }

        /// <summary>
        /// Returns a no-op IDisposable scope that callers can wrap in a
        /// <c>using</c> statement for symmetry with other batch-mode
        /// helpers. No actual batching is performed — Apply() is
        /// lightweight enough to call per-sheet.
        /// </summary>
        public static System.IDisposable Batch() => new BatchScope();

        private sealed class BatchScope : System.IDisposable
        {
            public void Dispose() { /* intentional no-op */ }
        }

        /// <summary>
        /// Apply dt.TitleBlockParams to a batch of sheets. Returns a flat list of
        /// warnings from all sheets. Never throws.
        /// </summary>
        public static List<string> Batch(
            Document doc, IEnumerable<ElementId> sheetIds, DrawingType dt,
            Dictionary<string, string> tokens)
        {
            var warnings = new List<string>();
            if (sheetIds == null) return warnings;
            foreach (var id in sheetIds)
            {
                try
                {
                    var sheet = doc?.GetElement(id) as ViewSheet;
                    if (sheet == null) continue;
                    var r = Apply(doc, sheet, dt, tokens);
                    warnings.AddRange(r.Warnings);
                }
                catch (Exception ex) { warnings.Add($"Batch sheet {id}: {ex.Message}"); }
            }
            return warnings;
        }

        /// <summary>
        /// Returns a list of ProjectInformation parameter names referenced by
        /// dt.TitleBlockParams that do not exist in the project. Used by
        /// pre-flight validators to surface missing parameters before a run.
        /// </summary>
        public static List<string> FindMissingProjectInfoParams(Document doc, DrawingType dt)
        {
            var missing = new List<string>();
            if (doc == null || dt?.TitleBlockParams == null) return missing;
            try
            {
                var pi = doc.ProjectInformation;
                if (pi == null) return missing;
                foreach (var kv in dt.TitleBlockParams)
                {
                    var ms = _projInfo.Matches(kv.Value ?? "");
                    foreach (System.Text.RegularExpressions.Match m in ms)
                    {
                        var name = m.Groups[1].Value;
                        if (pi.LookupParameter(name) == null && !missing.Contains(name))
                            missing.Add(name);
                    }
                }
            }
            catch { /* defensive */ }
            return missing;
        }

        // ── Internals ──

        private static string ResolveTemplate(
            Document doc, string template, IDictionary<string, string> tokens)
        {
            if (string.IsNullOrEmpty(template)) return "";

            // N+4 — MAT_* tokens take precedence over ProjectInfo lookup so
            // a title-block cell can resolve "${MAT_PRIMARY_NAME}" /
            // "${MAT_LIBRARY_COST_TOTAL}" / "${MAT_LIBRARY_CARBON_TOTAL}"
            // against the live material library state. Falls back to
            // ProjectInfo for everything else.
            var s = _projInfo.Replace(template, m =>
            {
                var name = m.Groups[1].Value;
                if (name.StartsWith("MAT_", StringComparison.Ordinal))
                {
                    string matVal = MaterialTitleBlockTokens.Resolve(doc, name);
                    if (matVal != null) return matVal;
                }
                return ReadProjectInfoParam(doc, name) ?? "";
            });

            // {token} / {token:Dn} substitution from the caller's dict.
            if (tokens != null && tokens.Count > 0)
            {
                s = _token.Replace(s, m =>
                {
                    var raw = m.Groups[1].Value;
                    var fmt = _seqFmt.Match(raw);
                    string key; int width = -1;
                    if (fmt.Success)
                    {
                        key   = fmt.Groups[1].Value;
                        width = int.Parse(fmt.Groups[2].Value);
                    }
                    else key = raw;

                    if (!tokens.TryGetValue(key, out var val)) return m.Value; // unknown → literal
                    if (width > 0 && int.TryParse(val, out var iv))
                        return iv.ToString("D" + width);
                    return val;
                });
            }
            return s;
        }

        private static string ReadProjectInfoParam(Document doc, string name)
        {
            try
            {
                var pi = doc?.ProjectInformation;
                if (pi == null) return null;
                var p = pi.LookupParameter(name);
                if (p == null) return null;
                switch (p.StorageType)
                {
                    case StorageType.String:  return p.AsString();
                    case StorageType.Integer: return p.AsInteger().ToString();
                    case StorageType.Double:  return p.AsDouble().ToString("0.###");
                    default: return p.AsValueString();
                }
            }
            catch { return null; }
        }

        /// <summary>
        /// Clear any title-block parameter values that were stamped by a
        /// prior DrawingType profile but are not present in the new profile.
        /// Prevents stale corporate metadata from a previous profile bleeding
        /// through when the sheet is re-assigned to a different drawing type.
        ///
        /// Only parameters whose keys are NOT in the new profile's
        /// TitleBlockParams are cleared; the new Apply() call will re-write
        /// the ones that remain. String parameters are set to empty string;
        /// Integer / Double parameters are set to 0.
        ///
        /// Requires an active transaction from the caller. Never throws.
        /// </summary>
        public static void ClearStaleKeysFromPriorProfile(
            Document doc, ViewSheet sheet, string priorDrawingTypeId)
        {
            if (doc == null || sheet == null
                || string.IsNullOrWhiteSpace(priorDrawingTypeId)) return;
            try
            {
                // Resolve the prior drawing type to get its TitleBlockParams keys.
                DrawingType priorDt = null;
                try { priorDt = DrawingTypeRegistry.Get(doc, priorDrawingTypeId); }
                catch (Exception ex) { StingTools.Core.StingLog.Warn($"Suppressed: {ex.Message}"); }

                if (priorDt?.TitleBlockParams == null || priorDt.TitleBlockParams.Count == 0) return;

                var tb = FindTitleBlockInstance(doc, sheet);
                if (tb == null) return;

                foreach (var key in priorDt.TitleBlockParams.Keys)
                {
                    if (string.IsNullOrWhiteSpace(key)) continue;
                    try
                    {
                        var p = tb.LookupParameter(key);
                        if (p == null || p.IsReadOnly) continue;
                        switch (p.StorageType)
                        {
                            case StorageType.String:  p.Set(""); break;
                            case StorageType.Integer: p.Set(0);  break;
                            case StorageType.Double:  p.Set(0.0); break;
                        }
                    }
                    catch (Exception ex)
                    {
                        StingTools.Core.StingLog.Warn(
                            $"ClearStaleKeysFromPriorProfile: '{key}': {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                StingTools.Core.StingLog.Warn(
                    $"TitleBlockParamApplier.ClearStaleKeysFromPriorProfile: {ex.Message}");
            }
        }

        /// <summary>
        /// Resolves the value of each TitleBlockParams entry in <paramref name="dt"/>
        /// using the same ${ProjectInfo} and {token} substitution rules as
        /// <see cref="Apply"/>, but does NOT write anything to the title block.
        /// Returns a dictionary of parameter-name → resolved-value strings.
        /// Used by pre-flight validators and drift detectors to compare what
        /// would be written against what is already on the sheet.
        /// </summary>
        public static Dictionary<string, string> Peek(
            Document doc, DrawingType dt,
            IDictionary<string, string> tokens = null)
        {
            var result = new Dictionary<string, string>();
            if (doc == null || dt?.TitleBlockParams == null) return result;
            foreach (var kv in dt.TitleBlockParams)
            {
                if (string.IsNullOrWhiteSpace(kv.Key)) continue;
                try
                {
                    result[kv.Key] = ResolveTemplate(doc, kv.Value ?? "", tokens);
                }
                catch (Exception ex)
                {
                    StingTools.Core.StingLog.Warn($"TitleBlockParamApplier.Peek '{kv.Key}': {ex.Message}");
                    result[kv.Key] = "";
                }
            }
            return result;
        }

        private static FamilyInstance FindTitleBlockInstance(Document doc, ViewSheet sheet)
        {
            try
            {
                return new FilteredElementCollector(doc, sheet.Id)
                    .OfCategory(BuiltInCategory.OST_TitleBlocks)
                    .OfClass(typeof(FamilyInstance))
                    .Cast<FamilyInstance>()
                    .FirstOrDefault();
            }
            catch { return null; }
        }

    }
}
