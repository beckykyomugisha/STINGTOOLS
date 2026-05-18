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

            var tb = FindTitleBlockInstance(doc, sheet);
            if (tb == null)
            {
                r.Warnings.Add($"Sheet '{sheet.SheetNumber}' has no title block to stamp.");
                return r;
            }

            foreach (var kv in dt.TitleBlockParams)
            {
                var paramName = kv.Key;
                if (string.IsNullOrWhiteSpace(paramName)) continue;

                string resolved;
                try
                {
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
                            p.Set(resolved);
                            break;
                        case StorageType.Integer:
                            if (int.TryParse(resolved, out var iv)) p.Set(iv);
                            else r.Warnings.Add($"'{paramName}' expects integer; '{resolved}' not parsable.");
                            break;
                        case StorageType.Double:
                            if (double.TryParse(resolved, out var dv)) p.Set(dv);
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
            return r;
        }

        // ── Batch context ──────────────────────────────────────────────────────────

        /// <summary>
        /// Returns a lightweight disposable batch context. Callers that call
        /// <see cref="Apply"/> in a loop should wrap the loop in a <c>using</c>
        /// block around <see cref="Batch()"/> so future optimisations (e.g.
        /// ProjectInformation caching) can be enabled without API changes.
        /// The current implementation returns a no-op disposable.
        /// </summary>
        public static IDisposable Batch() => new BatchContext();

        private sealed class BatchContext : IDisposable { public void Dispose() { } }

        // ── Preview / audit helpers ─────────────────────────────────────────────

        /// <summary>
        /// Resolves every entry in <paramref name="dt"/>.TitleBlockParams using
        /// ProjectInformation and the caller-supplied <paramref name="tokens"/>
        /// but does NOT write anything. Returns a dictionary of
        /// paramName → resolvedValue that the caller can diff or display.
        /// </summary>
        public static Dictionary<string, string> Peek(
            Document doc, DrawingType dt, IDictionary<string, string> tokens = null)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (doc == null || dt?.TitleBlockParams == null) return result;
            foreach (var kv in dt.TitleBlockParams)
                result[kv.Key] = ResolveTemplate(doc, kv.Value, tokens);
            return result;
        }

        /// <summary>
        /// Scans <paramref name="dt"/>.TitleBlockParams for <c>${paramName}</c>
        /// tokens and returns the names of any ProjectInformation parameters
        /// that do not exist on the current document. Used by the validator
        /// to surface DT-090 warnings pre-flight.
        /// </summary>
        public static List<string> FindMissingProjectInfoParams(Document doc, DrawingType dt)
        {
            var missing = new List<string>();
            if (doc == null || dt?.TitleBlockParams == null) return missing;
            var pi = doc.ProjectInformation;
            foreach (var kv in dt.TitleBlockParams)
            {
                foreach (Match m in _projInfo.Matches(kv.Value ?? ""))
                {
                    var name = m.Groups[1].Value;
                    if (pi?.LookupParameter(name) == null && !missing.Contains(name))
                        missing.Add(name);
                }
            }
            return missing;
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

        /// <summary>
        /// Clears title-block parameter values that were written by a prior DrawingType
        /// profile so they do not persist when the sheet is reassigned to a different profile.
        /// Looks up the prior profile's TitleBlockParams keys and blanks each value on the
        /// title-block instance. No-ops when the prior profile cannot be resolved.
        /// </summary>
        public static void ClearStaleKeysFromPriorProfile(Document doc, ViewSheet sheet, string priorDrawingTypeId)
        {
            if (doc == null || sheet == null || string.IsNullOrEmpty(priorDrawingTypeId)) return;
            try
            {
                var priorDt = DrawingTypeRegistry.Get(doc, priorDrawingTypeId);
                if (priorDt?.TitleBlockParams == null || priorDt.TitleBlockParams.Count == 0) return;
                var tb = FindTitleBlockInstance(doc, sheet);
                if (tb == null) return;
                foreach (var key in priorDt.TitleBlockParams.Keys)
                {
                    try
                    {
                        var p = tb.LookupParameter(key);
                        if (p == null || p.IsReadOnly) continue;
                        if (p.StorageType == StorageType.String)  p.Set(string.Empty);
                        else if (p.StorageType == StorageType.Integer) p.Set(0);
                        else if (p.StorageType == StorageType.Double)  p.Set(0.0);
                    }
                    catch { /* best-effort per-param */ }
                }
            }
            catch (Exception ex) { StingTools.Core.StingLog.Warn($"ClearStaleKeysFromPriorProfile: {ex.Message}"); }
        }
    }
}
