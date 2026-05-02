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

            // A single param written to multiple TB instances on the same sheet
            // counts once toward ParamsWritten — track by name so a sheet with
            // 2 TBs and 11 params reports 11, not 22.
            var writtenKeys = new HashSet<string>(StringComparer.Ordinal);

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
                    bool wrote = false;
                    switch (p.StorageType)
                    {
                        case StorageType.String:
                            // ACC-07: always set, even for empty string,
                            // so cloned/template sheets reset stale text.
                            p.Set(resolved ?? string.Empty);
                            wrote = true;
                            break;
                        case StorageType.Integer:
                            if (string.IsNullOrEmpty(resolved)) { p.Set(0); wrote = true; }
                            else if (int.TryParse(resolved, out var iv)) { p.Set(iv); wrote = true; }
                            else r.Warnings.Add($"'{paramName}' expects integer; '{resolved}' not parsable.");
                            break;
                        case StorageType.Double:
                            if (string.IsNullOrEmpty(resolved)) { p.Set(0.0); wrote = true; }
                            else if (double.TryParse(resolved, out var dv)) { p.Set(dv); wrote = true; }
                            else r.Warnings.Add($"'{paramName}' expects number; '{resolved}' not parsable.");
                            break;
                        default:
                            r.Warnings.Add($"'{paramName}' has unsupported storage type {p.StorageType}.");
                            continue;
                    }
                    if (wrote) writtenKeys.Add(paramName);
                }
                catch (Exception ex)
                {
                    r.Warnings.Add($"Write '{paramName}': {ex.Message}");
                }
            }
            } // end per-TB block (GAP-M)
            r.ParamsWritten = writtenKeys.Count;
            return r;
        }

        // GAP-M: cheap test to skip secondary title blocks that don't carry
        // any of the declared keys. The first match short-circuits.
        private static bool TitleBlockHasAnyKey(FamilyInstance tb, IEnumerable<string> keys)
        {
            try
            {
                foreach (var k in keys)
                {
                    if (string.IsNullOrWhiteSpace(k)) continue;
                    if (tb.LookupParameter(k) != null) return true;
                }
            }
            catch { /* defensive — a TB we cannot even probe is silently skipped rather than producing N false-positive "no parameter" warnings (GAP-M intent). */ return false; }
            return false;
        }

        /// <summary>
        /// FIX-7: clear every previously-applied STING title-block param on
        /// a sheet whose profile has changed. Reads the prior DrawingTypeId
        /// from the sheet stamp, looks up the previous profile, and writes
        /// empty strings for every key it declared so a subsequent
        /// <see cref="Apply"/> with a different / smaller key set doesn't
        /// leave stale values. Idempotent — no-op when the stamp is empty
        /// or the previous profile cannot be resolved.
        /// </summary>
        /// <remarks>Must be called inside an active Revit transaction.</remarks>
        public static int ClearStaleKeysFromPriorProfile(Document doc, ViewSheet sheet)
            => ClearStaleKeysFromPriorProfile(doc, sheet, priorIdOverride: null);

        /// <summary>
        /// Same as <see cref="ClearStaleKeysFromPriorProfile(Document,ViewSheet)"/>
        /// but lets the caller skip the second <see cref="DrawingTypeStamper.Read"/>
        /// when it's already loaded the prior id.
        /// </summary>
        /// <remarks>Must be called inside an active Revit transaction.</remarks>
        public static int ClearStaleKeysFromPriorProfile(Document doc, ViewSheet sheet, string priorIdOverride)
        {
            if (doc == null || sheet == null) return 0;
            var priorId = priorIdOverride ?? DrawingTypeStamper.Read(sheet);
            if (string.IsNullOrEmpty(priorId)) return 0;
            var priorDt = DrawingTypeRegistry.Get(doc, priorId);
            if (priorDt?.TitleBlockParams == null || priorDt.TitleBlockParams.Count == 0) return 0;
            var tbs = FindAllTitleBlockInstances(doc, sheet);
            if (tbs.Count == 0) return 0;
            int cleared = 0;
            foreach (var tb in tbs)
            foreach (var k in priorDt.TitleBlockParams.Keys)
            {
                if (string.IsNullOrWhiteSpace(k)) continue;
                try
                {
                    var p = tb.LookupParameter(k);
                    if (p == null || p.IsReadOnly) continue;
                    switch (p.StorageType)
                    {
                        case StorageType.String:  p.Set(string.Empty); cleared++; break;
                        case StorageType.Integer: p.Set(0); cleared++; break;
                        case StorageType.Double:  p.Set(0.0); cleared++; break;
                    }
                }
                catch { /* per-key failure — continue */ }
            }
            return cleared;
        }

        /// <summary>
        /// ACC-04: list every <c>${ProjectInfo}</c> parameter referenced by
        /// any title-block-params template in <paramref name="dt"/> that is
        /// not currently bound on the document's ProjectInformation. Used
        /// by <see cref="DrawingTypeValidator"/> at preflight so missing
        /// shared parameters surface before generation rather than as
        /// silently-empty title-block cells.
        /// </summary>
        public static List<string> FindMissingProjectInfoParams(Document doc, DrawingType dt)
        {
            var missing = new List<string>();
            if (doc == null || dt?.TitleBlockParams == null || dt.TitleBlockParams.Count == 0) return missing;
            var pi = doc.ProjectInformation;
            if (pi == null) return missing;
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var kv in dt.TitleBlockParams)
            {
                var template = kv.Value ?? string.Empty;
                foreach (Match m in _projInfo.Matches(template))
                {
                    var name = m.Groups[1].Value;
                    if (string.IsNullOrEmpty(name) || !seen.Add(name)) continue;
                    var p = pi.LookupParameter(name);
                    if (p == null) missing.Add(name);
                }
            }
            return missing;
        }

        /// <summary>
        /// Read-only resolution of every <c>TitleBlockParams</c> entry in
        /// <paramref name="dt"/>. Substitutes <c>${PRJ_ORG_*}</c> against
        /// the document's <see cref="ProjectInformation"/> and
        /// <c>{token}</c>s against the supplied dict. Returns a fresh
        /// dictionary mapping each param name to its resolved value
        /// without writing anything to the model — used by the editor's
        /// preview column and by drift detection.
        /// </summary>
        public static Dictionary<string, string> Peek(
            Document doc, DrawingType dt, IDictionary<string, string> tokens = null)
        {
            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            if (dt?.TitleBlockParams == null || dt.TitleBlockParams.Count == 0) return result;
            foreach (var kv in dt.TitleBlockParams)
            {
                if (string.IsNullOrWhiteSpace(kv.Key)) continue;
                try { result[kv.Key] = ResolveTemplate(doc, kv.Value ?? "", tokens) ?? ""; }
                catch { result[kv.Key] = ""; }
            }
            return result;
        }

        // ── Internals ──

        private static string ResolveTemplate(
            Document doc, string template, IDictionary<string, string> tokens)
        {
            if (string.IsNullOrEmpty(template)) return "";

            // ${ProjectInfo} substitution first, so a user can pass
            // "${PRJ_ORG_PROJECT_CODE}-{disc}" and both land.
            var s = _projInfo.Replace(template, m =>
            {
                var name = m.Groups[1].Value;
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

        private static List<FamilyInstance> FindAllTitleBlockInstances(Document doc, ViewSheet sheet)
        {
            try
            {
                return new FilteredElementCollector(doc, sheet.Id)
                    .OfCategory(BuiltInCategory.OST_TitleBlocks)
                    .OfClass(typeof(FamilyInstance))
                    .Cast<FamilyInstance>()
                    .ToList();
            }
            catch { return new List<FamilyInstance>(); }
        }
    }
}
