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
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using Autodesk.Revit.DB;

namespace StingTools.Core.Drawing
{
    public sealed class TitleBlockApplyResult
    {
        public int ParamsWritten { get; set; }
        /// <summary>Phase 169 — count of writes skipped because the live value
        /// already matched the resolved value (idempotent skip-if-equal).</summary>
        public int ParamsSkippedNoOp { get; set; }
        /// <summary>Phase 169 — when populated, lists every (paramName, oldVal,
        /// newVal) the apply did or would have changed. Always populated when
        /// <see cref="ApplyOptions.DryRun"/> is true; otherwise empty.</summary>
        public List<TitleBlockChange> Changes { get; } = new List<TitleBlockChange>();
        public List<string> Warnings { get; } = new List<string>();
    }

    public sealed class TitleBlockChange
    {
        public string SheetNumber { get; set; }
        public string SymbolName { get; set; }
        public string ParamName { get; set; }
        public string OldValue { get; set; }
        public string NewValue { get; set; }
    }

    public sealed class ApplyOptions
    {
        /// <summary>When true, every storage-type write is replaced by a
        /// <see cref="TitleBlockChange"/> entry on the result without
        /// touching the model. The caller is responsible for skipping
        /// the surrounding transaction when only previewing.</summary>
        public bool DryRun { get; set; }
        /// <summary>When true (default), writes that would set the live value
        /// to its current value are skipped — reduces undo-stack noise and
        /// transaction time when re-applying a profile that hasn't changed.</summary>
        public bool SkipIfEqual { get; set; } = true;
    }

    public static class TitleBlockParamApplier
    {
        // Phase 168 — extended grammar:
        //   ${NAME}                 — ProjectInformation read
        //   ${NAME|filter|filter}    — filter chain
        //   ${NAME|default:fallback} — explicit fallback
        //   {key}                   — caller-supplied token
        //   {key:D4}                — zero-pad integer width 4 (legacy)
        //   {key|filter}            — filter chain on token
        //   \${ / \{                — literal $ / { (escape)
        // Filters: upper / lower / title / trim / trunc:N / pad:N / padl:N
        //          / date:fmt / default:value / fallback:${OTHER}.
        private static readonly Regex _projInfo =
            new Regex(@"\$\{([A-Za-z0-9_]+)((?:\|[^}|]*)*)\}", RegexOptions.Compiled);
        private static readonly Regex _token =
            new Regex(@"\{([A-Za-z0-9_]+)(?::D(\d+))?((?:\|[^}|]*)*)\}", RegexOptions.Compiled);
        // Escape sentinels: rare unicode markers we substitute in / out so the
        // primary regexes never see a literal-escaped delimiter.
        private const string ESC_DOLLAR = "STING_ESC_DOLLAR";
        private const string ESC_BRACE  = "STING_ESC_BRACE";

        // Phase 168 - per-batch caches. ProjectInformation lookups and
        // template-resolution results memoize for the duration of a batch;
        // outside a batch every call is uncached (safe default for the
        // editor preview / one-shot Apply paths).
        [ThreadStatic] private static Dictionary<string, string> _piCache;
        [ThreadStatic] private static Dictionary<string, string> _resolveCache;

        /// <summary>
        /// Phase 168 - opt-in batch fast-path. Wrap a multi-sheet sync in
        /// <c>using (TitleBlockParamApplier.Batch()) { ... }</c>; inside the
        /// scope ProjectInformation reads and template resolves are memoized.
        /// </summary>
        public static IDisposable Batch()
        {
            _piCache      = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            _resolveCache = new Dictionary<string, string>(StringComparer.Ordinal);
            return new BatchScope();
        }

        private sealed class BatchScope : IDisposable
        {
            public void Dispose() { _piCache = null; _resolveCache = null; }
        }

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
            => Apply(doc, sheet, dt, tokens, options: null);

        public static TitleBlockApplyResult Apply(
            Document doc, ViewSheet sheet, DrawingType dt,
            IDictionary<string, string> tokens, ApplyOptions options)
        {
            var r = new TitleBlockApplyResult();
            options = options ?? new ApplyOptions();
            if (doc == null || sheet == null || dt == null) return r;
            bool hasBase     = dt.TitleBlockParams != null && dt.TitleBlockParams.Count > 0;
            bool hasOverlay  = dt.TitleBlockParamsBySymbol != null && dt.TitleBlockParamsBySymbol.Count > 0;
            if (!hasBase && !hasOverlay) return r;

            // GAP-A: walk every title-block instance on the sheet, not just
            // the first. Sheets that host more than one TB (front + back,
            // landscape + portrait variants) used to leave the second
            // instance with stale values; now every TB receives the same
            // declarative payload (or, in Phase 168, a per-symbol override).
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
            // Phase 168 — collect ${X} references that resolved empty so the
            // caller hears about typos/empty-PI fields instead of silently
            // writing blank cells. One warning per unique name per Apply.
            var unresolvedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var tb in tbs)
            {
                // Phase 168 — resolve effective params for THIS instance:
                //   base TitleBlockParams ⊕ TitleBlockParamsBySymbol[tb.Symbol.Name]
                //                          (per-symbol wins on key collision)
                //   else fall back to TitleBlockParamsBySymbol["*"] when set.
                var effective = ResolveEffectiveParams(dt, tb);
                if (effective.Count == 0) continue;

                // GAP-M: a secondary title block (e.g. a North arrow or a
                // fabrication-only stamp on the same sheet) typically has
                // zero of the declared keys. Skip silently rather than
                // emitting a "no parameter" warning per key per secondary TB.
                if (tbs.Count > 1 && !TitleBlockHasAnyKey(tb, effective.Keys))
                    continue;

            foreach (var kv in effective)
            {
                var paramName = kv.Key;
                if (string.IsNullOrWhiteSpace(paramName)) continue;

                string resolved;
                try
                {
                    // ACC-07: a null/empty template still resolves to the
                    // empty string and writes through, ensuring cloned
                    // sheets don't carry stale prior values forward.
                    resolved = ResolveTemplate(doc, kv.Value ?? "", tokens, unresolvedNames);
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
                    // Phase 169 — read live value first so we can skip-if-equal
                    // and emit dry-run change rows. Reading is cheap (no
                    // transaction) so the cost is negligible vs. an idempotent
                    // re-write that bloats the undo stack.
                    string liveVal = ReadParamAsString(p, doc);
                    bool wrote = false;
                    bool wouldChange = false;
                    string newDisplay = resolved ?? "";

                    switch (p.StorageType)
                    {
                        case StorageType.String:
                            wouldChange = !string.Equals(liveVal ?? "", resolved ?? "", StringComparison.Ordinal);
                            if (!options.DryRun && (wouldChange || !options.SkipIfEqual))
                            {
                                p.Set(resolved ?? string.Empty);
                                wrote = true;
                            }
                            break;
                        case StorageType.Integer:
                            if (string.IsNullOrEmpty(resolved))
                            {
                                wouldChange = liveVal != "0";
                                newDisplay = "0";
                                if (!options.DryRun && (wouldChange || !options.SkipIfEqual)) { p.Set(0); wrote = true; }
                            }
                            else if (TryCoerceInt(resolved, out var iv))
                            {
                                newDisplay = iv.ToString(CultureInfo.InvariantCulture);
                                wouldChange = liveVal != newDisplay;
                                if (!options.DryRun && (wouldChange || !options.SkipIfEqual)) { p.Set(iv); wrote = true; }
                            }
                            else r.Warnings.Add($"'{paramName}' expects integer / Yes-No; '{resolved}' not parsable.");
                            break;
                        case StorageType.Double:
                            if (string.IsNullOrEmpty(resolved))
                            {
                                wouldChange = liveVal != "0";
                                newDisplay = "0";
                                if (!options.DryRun && (wouldChange || !options.SkipIfEqual)) { p.Set(0.0); wrote = true; }
                            }
                            else if (TryCoerceDouble(resolved, out var dv))
                            {
                                newDisplay = dv.ToString("0.###", CultureInfo.InvariantCulture);
                                wouldChange = liveVal != newDisplay;
                                if (!options.DryRun && (wouldChange || !options.SkipIfEqual)) { p.Set(dv); wrote = true; }
                            }
                            else r.Warnings.Add($"'{paramName}' expects number; '{resolved}' not parsable.");
                            break;
                        case StorageType.ElementId:
                            if (TryResolveElementId(doc, tb, paramName, resolved, out var eid))
                            {
                                var newEl = doc.GetElement(eid);
                                newDisplay = newEl?.Name ?? "";
                                wouldChange = !string.Equals(liveVal ?? "", newDisplay, StringComparison.Ordinal);
                                if (!options.DryRun && (wouldChange || !options.SkipIfEqual)) { p.Set(eid); wrote = true; }
                            }
                            else if (string.IsNullOrEmpty(resolved))
                            {
                                wouldChange = !string.IsNullOrEmpty(liveVal);
                                if (!options.DryRun && (wouldChange || !options.SkipIfEqual)) { p.Set(ElementId.InvalidElementId); wrote = true; }
                            }
                            else r.Warnings.Add($"'{paramName}' expects ElementId; '{resolved}' did not resolve.");
                            break;
                        default:
                            r.Warnings.Add($"'{paramName}' has unsupported storage type {p.StorageType}.");
                            continue;
                    }

                    if (options.DryRun)
                    {
                        if (wouldChange)
                            r.Changes.Add(new TitleBlockChange
                            {
                                SheetNumber = sheet.SheetNumber,
                                SymbolName  = tb?.Symbol?.Name,
                                ParamName   = paramName,
                                OldValue    = liveVal ?? "",
                                NewValue    = newDisplay,
                            });
                    }
                    else if (wrote) writtenKeys.Add(paramName);
                    else if (!wouldChange && options.SkipIfEqual) r.ParamsSkippedNoOp++;
                }
                catch (Exception ex)
                {
                    r.Warnings.Add($"Write '{paramName}': {ex.Message}");
                }
            }
            } // end per-TB block (GAP-M)
            foreach (var u in unresolvedNames)
                r.Warnings.Add($"${{{u}}} not bound or empty on ProjectInformation — substituted empty string.");
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
            var priorKeys = AllDeclaredKeys(priorDt);
            if (priorKeys.Count == 0) return 0;
            var tbs = FindAllTitleBlockInstances(doc, sheet);
            if (tbs.Count == 0) return 0;
            int cleared = 0;
            foreach (var tb in tbs)
            foreach (var k in priorKeys)
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
            if (doc == null || dt == null) return missing;
            var pi = doc.ProjectInformation;
            if (pi == null) return missing;
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            void scan(string template)
            {
                if (string.IsNullOrEmpty(template)) return;
                foreach (Match m in _projInfo.Matches(template))
                {
                    var name = m.Groups[1].Value;
                    if (string.IsNullOrEmpty(name) || !seen.Add(name)) continue;
                    var p = pi.LookupParameter(name);
                    if (p == null) missing.Add(name);
                }
            }
            if (dt.TitleBlockParams != null)
                foreach (var kv in dt.TitleBlockParams) scan(kv.Value);
            if (dt.TitleBlockParamsBySymbol != null)
                foreach (var grp in dt.TitleBlockParamsBySymbol.Values)
                    if (grp != null) foreach (var kv in grp) scan(kv.Value);
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
            => Peek(doc, dt, tokens, unresolved: null);

        /// <summary>
        /// Same as <see cref="Peek(Document,DrawingType,IDictionary{string,string})"/>
        /// but populates <paramref name="unresolved"/> with every <c>${X}</c>
        /// name that resolved to empty. Editor + drift detector pass this so
        /// they can surface "(param not bound)" without re-parsing templates.
        /// </summary>
        public static Dictionary<string, string> Peek(
            Document doc, DrawingType dt, IDictionary<string, string> tokens,
            ICollection<string> unresolved)
        {
            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            if (dt == null) return result;
            // Phase 168 — preview the merged base + wildcard ("*") overlay.
            // A per-symbol override that targets a specific FamilySymbol shows
            // up only at write-time when the live symbol is known; the editor
            // preview shows the most-likely value an unspecified TB would get.
            var seed = new Dictionary<string, string>(StringComparer.Ordinal);
            if (dt.TitleBlockParams != null)
                foreach (var kv in dt.TitleBlockParams) seed[kv.Key] = kv.Value;
            if (dt.TitleBlockParamsBySymbol != null
                && dt.TitleBlockParamsBySymbol.TryGetValue("*", out var star)
                && star != null)
                foreach (var kv in star) seed[kv.Key] = kv.Value;
            foreach (var kv in seed)
            {
                if (string.IsNullOrWhiteSpace(kv.Key)) continue;
                try { result[kv.Key] = ResolveTemplate(doc, kv.Value ?? "", tokens, unresolved) ?? ""; }
                catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); result[kv.Key] = ""; }
            }
            return result;
        }

        // ── Internals ──

        /// <summary>
        /// Phase 168 — merge base <c>TitleBlockParams</c> with the
        /// per-symbol overlay for a specific TB instance. Per-symbol entries
        /// win on key collision. Falls back to the <c>"*"</c> wildcard
        /// overlay when the symbol name doesn't match any explicit key.
        /// </summary>
        internal static Dictionary<string, string> ResolveEffectiveParams(DrawingType dt, FamilyInstance tb)
        {
            var merged = new Dictionary<string, string>(StringComparer.Ordinal);
            if (dt?.TitleBlockParams != null)
                foreach (var kv in dt.TitleBlockParams) merged[kv.Key] = kv.Value;
            var overlay = dt?.TitleBlockParamsBySymbol;
            if (overlay != null && overlay.Count > 0)
            {
                Dictionary<string, string> picked = null;
                var symName = tb?.Symbol?.Name;
                if (!string.IsNullOrEmpty(symName))
                {
                    foreach (var kv in overlay)
                        if (string.Equals(kv.Key, symName, StringComparison.OrdinalIgnoreCase))
                        { picked = kv.Value; break; }
                }
                if (picked == null) overlay.TryGetValue("*", out picked);
                if (picked != null)
                    foreach (var kv in picked) merged[kv.Key] = kv.Value;
            }
            return merged;
        }

        /// <summary>
        /// Phase 168 — collect every key declared by either the base map or
        /// any overlay group, used by <see cref="ClearStaleKeysFromPriorProfile"/>
        /// so a profile change clears every formerly-mapped key, not just the
        /// base set.
        /// </summary>
        internal static HashSet<string> AllDeclaredKeys(DrawingType dt)
        {
            var keys = new HashSet<string>(StringComparer.Ordinal);
            if (dt?.TitleBlockParams != null)
                foreach (var k in dt.TitleBlockParams.Keys) keys.Add(k);
            if (dt?.TitleBlockParamsBySymbol != null)
                foreach (var grp in dt.TitleBlockParamsBySymbol.Values)
                    if (grp != null) foreach (var k in grp.Keys) keys.Add(k);
            return keys;
        }

        private static string ResolveTemplate(
            Document doc, string template, IDictionary<string, string> tokens)
            => ResolveTemplate(doc, template, tokens, unresolved: null);

        /// <summary>
        /// Resolve a value template. <paramref name="unresolved"/>, when non-null,
        /// collects every <c>${X}</c> name that resolved to empty so callers
        /// (writer, drift detector) can warn instead of silently substituting "".
        /// </summary>
        private static string ResolveTemplate(
            Document doc, string template, IDictionary<string, string> tokens,
            ICollection<string> unresolved)
        {
            if (string.IsNullOrEmpty(template)) return "";

            // Phase 168 — batch-scoped resolve cache. Key includes the token
            // dict's serialized state so a single Apply call's repeated
            // calls (different params, same tokens) hit the cache. We skip
            // caching when 'unresolved' is non-null because the caller wants
            // the side-effect collection populated each call.
            string cacheKey = null;
            if (_resolveCache != null && unresolved == null)
            {
                cacheKey = template + "" + SerializeTokens(tokens);
                if (_resolveCache.TryGetValue(cacheKey, out var hit)) return hit;
            }

            // Stage 1: protect literal-escaped delimiters from the regexes.
            var s = template
                .Replace("\\${", ESC_DOLLAR + "{")
                .Replace("\\{",  ESC_BRACE  + "{");

            // Stage 2: ${ProjectInfo} substitution + filter chain.
            s = _projInfo.Replace(s, m =>
            {
                var name = m.Groups[1].Value;
                var filters = m.Groups[2].Value;
                var raw = ReadProjectInfoParam(doc, name);
                if (string.IsNullOrEmpty(raw))
                    unresolved?.Add(name);
                return ApplyFilters(raw ?? "", filters, doc, tokens);
            });

            // Stage 3: {token} / {token:Dn} substitution + filter chain.
            if (tokens != null && tokens.Count > 0)
            {
                s = _token.Replace(s, m =>
                {
                    var key      = m.Groups[1].Value;
                    var widthStr = m.Groups[2].Value;       // "" or digit run
                    var filters  = m.Groups[3].Value;       // "" or |a|b…
                    if (!tokens.TryGetValue(key, out var val))
                        return m.Value;                     // unknown → literal pass-through
                    if (!string.IsNullOrEmpty(widthStr)
                        && int.TryParse(widthStr, NumberStyles.Integer, CultureInfo.InvariantCulture, out var width)
                        && width > 0
                        && int.TryParse(val, NumberStyles.Integer, CultureInfo.InvariantCulture, out var iv))
                        val = iv.ToString("D" + width, CultureInfo.InvariantCulture);
                    return ApplyFilters(val ?? "", filters, doc, tokens);
                });
            }

            // Stage 4: restore escape sentinels back to literal text.
            s = s
                .Replace(ESC_DOLLAR + "{", "${")
                .Replace(ESC_BRACE  + "{", "{");
            if (cacheKey != null) _resolveCache[cacheKey] = s;
            return s;
        }

        /// <summary>
        /// Apply a chain of <c>|filter[:arg]</c> to a value. Unknown filters
        /// pass through as no-ops; argument parsing is invariant-culture.
        /// </summary>
        private static string ApplyFilters(string value, string filterChain,
            Document doc, IDictionary<string, string> tokens)
        {
            if (string.IsNullOrEmpty(filterChain)) return value;
            // filterChain has form "|a|b:arg|c" — split on '|' skipping the leader.
            var parts = filterChain.Split('|');
            for (int i = 1; i < parts.Length; i++)
            {
                var f = parts[i];
                if (string.IsNullOrWhiteSpace(f)) continue;
                int colon = f.IndexOf(':');
                string name = (colon < 0 ? f : f.Substring(0, colon)).Trim().ToLowerInvariant();
                string arg  = colon < 0 ? null : f.Substring(colon + 1);
                value = RunFilter(value ?? "", name, arg, doc, tokens);
            }
            return value;
        }

        private static string RunFilter(string value, string name, string arg,
            Document doc, IDictionary<string, string> tokens)
        {
            switch (name)
            {
                case "upper": return value.ToUpperInvariant();
                case "lower": return value.ToLowerInvariant();
                case "title":
                    return string.IsNullOrEmpty(value) ? value
                        : CultureInfo.InvariantCulture.TextInfo.ToTitleCase(value.ToLowerInvariant());
                case "trim":  return value.Trim();
                case "trunc":
                    if (int.TryParse(arg, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) && n >= 0
                        && value.Length > n) return value.Substring(0, n);
                    return value;
                case "pad":
                    if (int.TryParse(arg, NumberStyles.Integer, CultureInfo.InvariantCulture, out var pn))
                        return (value ?? "").PadRight(pn);
                    return value;
                case "padl":
                    if (int.TryParse(arg, NumberStyles.Integer, CultureInfo.InvariantCulture, out var pl))
                        return (value ?? "").PadLeft(pl);
                    return value;
                case "date":
                    if (string.IsNullOrEmpty(value)) return value;
                    if (DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
                        return dt.ToString(arg ?? "yyyy-MM-dd", CultureInfo.InvariantCulture);
                    return value;
                case "default":
                    return string.IsNullOrEmpty(value) ? (arg ?? "") : value;
                case "fallback":
                    if (!string.IsNullOrEmpty(value)) return value;
                    if (string.IsNullOrEmpty(arg)) return value;
                    // arg may itself be ${X} or {x}, recurse.
                    return ResolveTemplate(doc, arg, tokens);
                default:
                    return value; // unknown filter → no-op (forward-compat)
            }
        }

        // Phase 168 — token-dict serialization for the resolve cache key.
        // Keys are sorted so equivalent dicts hash the same regardless of
        // construction order. Values escaped to avoid '|' collisions.
        private static string SerializeTokens(IDictionary<string, string> tokens)
        {
            if (tokens == null || tokens.Count == 0) return "";
            var keys = new List<string>(tokens.Keys);
            keys.Sort(StringComparer.OrdinalIgnoreCase);
            var sb = new System.Text.StringBuilder(64);
            foreach (var k in keys)
            {
                sb.Append(k).Append('=').Append((tokens[k] ?? "").Replace("|", "\\|")).Append('|');
            }
            return sb.ToString();
        }

        // Phase 168 — culture-invariant + Yes/No coercion.
        private static bool TryCoerceInt(string s, out int value)
        {
            value = 0;
            if (string.IsNullOrWhiteSpace(s)) return false;
            var t = s.Trim();
            // Yes/No / true/false / on/off / 1/0
            if (t.Equals("yes", StringComparison.OrdinalIgnoreCase)
                || t.Equals("true", StringComparison.OrdinalIgnoreCase)
                || t.Equals("on",   StringComparison.OrdinalIgnoreCase)
                || t.Equals("y",    StringComparison.OrdinalIgnoreCase))
            { value = 1; return true; }
            if (t.Equals("no",  StringComparison.OrdinalIgnoreCase)
                || t.Equals("false", StringComparison.OrdinalIgnoreCase)
                || t.Equals("off",   StringComparison.OrdinalIgnoreCase)
                || t.Equals("n",     StringComparison.OrdinalIgnoreCase))
            { value = 0; return true; }
            return int.TryParse(t, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
        }

        private static bool TryCoerceDouble(string s, out double value)
        {
            value = 0;
            if (string.IsNullOrWhiteSpace(s)) return false;
            return double.TryParse(s.Trim(),
                NumberStyles.Float | NumberStyles.AllowThousands,
                CultureInfo.InvariantCulture, out value);
        }

        // Phase 168 — ElementId coercion. Currently supports family-type swap
        // by symbol Name (the most useful TB case — e.g. "TYPE_PARAM_FRAME" =>
        // a different FamilySymbol within the title-block family). Returns
        // false when the resolved string doesn't match any candidate.
        private static bool TryResolveElementId(
            Document doc, FamilyInstance tb, string paramName, string resolved, out ElementId id)
        {
            id = ElementId.InvalidElementId;
            if (doc == null || tb == null || string.IsNullOrWhiteSpace(resolved)) return false;
            try
            {
                var fam = tb.Symbol?.Family;
                if (fam != null)
                {
                    foreach (var symId in fam.GetFamilySymbolIds())
                    {
                        if (doc.GetElement(symId) is FamilySymbol fs
                            && string.Equals(fs.Name, resolved, StringComparison.OrdinalIgnoreCase))
                        { id = fs.Id; return true; }
                    }
                }
            }
            catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
            return false;
        }

        // Phase 169 — read any parameter to a stable string for skip-if-equal
        // comparison. ElementId returns the element's Name (matching the
        // convention used by ElementId writes).
        private static string ReadParamAsString(Parameter p, Document doc)
        {
            if (p == null) return null;
            try
            {
                switch (p.StorageType)
                {
                    case StorageType.String:  return p.AsString() ?? "";
                    case StorageType.Integer: return p.AsInteger().ToString(CultureInfo.InvariantCulture);
                    case StorageType.Double:  return p.AsDouble().ToString("0.###", CultureInfo.InvariantCulture);
                    case StorageType.ElementId:
                        var id = p.AsElementId();
                        var el = (id != null && id != ElementId.InvalidElementId) ? doc?.GetElement(id) : null;
                        return el?.Name ?? "";
                }
            }
            catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
            return "";
        }

        private static string ReadProjectInfoParam(Document doc, string name)
        {
            // Phase 168 — batch-scoped cache for ProjectInformation lookups.
            if (_piCache != null && _piCache.TryGetValue(name, out var cached))
                return cached;
            string val = null;
            try
            {
                var pi = doc?.ProjectInformation;
                if (pi != null)
                {
                    var p = pi.LookupParameter(name);
                    if (p != null)
                    {
                        switch (p.StorageType)
                        {
                            case StorageType.String:  val = p.AsString(); break;
                            case StorageType.Integer: val = p.AsInteger().ToString(CultureInfo.InvariantCulture); break;
                            case StorageType.Double:  val = p.AsDouble().ToString("0.###", CultureInfo.InvariantCulture); break;
                            default: val = p.AsValueString(); break;
                        }
                    }
                }
            }
            catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); val = null; }
            if (_piCache != null) _piCache[name] = val;
            return val;
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
            catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); return new List<FamilyInstance>(); }
        }
    }
}
