using StingTools.Core;
// StingTools — Drawing Template Manager · Phase 137
//
// ManagedTemplateSyncer mints (or updates) a per-pack-per-viewtype
// view template named "STING:{packId}:{ViewType}" so that when a
// ViewStylePack runs in managed mode every view assigned to the
// template stays in sync with pack edits.
//
// The syncer reuses ViewStylePackApplier internals for VG / filter
// payloads and writes pack scalar fields (scale, detail level,
// discipline, visual style, phase filter, etc.) directly to the
// template view via BuiltInParameter. It binds the template's
// SetTemplateParameterIds list to the fields the pack actually
// declares so non-managed parameters stay un-controlled.
//
// Caller responsibility: open a Transaction before calling EnsureTemplate.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Autodesk.Revit.DB;
using Newtonsoft.Json;

namespace StingTools.Core.Drawing
{
    internal static class ManagedTemplateSyncer
    {
        // C-6: keyed by document so a stale ElementId from a previously
        // open document is never returned to a different document.
        private static readonly object _cacheLock = new object();
        private static readonly Dictionary<string, Dictionary<(string packId, ViewType vt), ElementId>> _cache
            = new Dictionary<string, Dictionary<(string, ViewType), ElementId>>(StringComparer.OrdinalIgnoreCase);

        // C-3: per-document seed-view cache so EnsureTemplate doesn't run
        // two FilteredElementCollector<View> scans for every (pack, viewType)
        // pair on first use. Built lazily on first EnsureTemplate call per
        // document and invalidated on Reload / DocumentClosed.
        private static readonly object _seedCacheLock = new object();
        private static readonly Dictionary<string, Dictionary<ViewType, ElementId>> _seedViewCache
            = new Dictionary<string, Dictionary<ViewType, ElementId>>(StringComparer.OrdinalIgnoreCase);

        // Default field set when ManagedFields is not specified.
        // INT-02: tagColorScheme + defaultTagStyle are now first-class
        // managed fields so a managed pack's tag-appearance defaults
        // propagate via the template instead of resetting on every
        // view assignment.
        private static readonly List<string> DefaultManagedFields = new List<string>
        {
            "scale", "detailLevel", "discipline", "visualStyle", "phaseFilter",
            "tagColorScheme", "defaultTagStyle"
        };

        private static string DocKey(Document doc)
        {
            if (doc == null) return "__null__";
            try { return string.IsNullOrEmpty(doc.PathName) ? doc.Title : doc.PathName; }
            catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); return "__unknown__"; }
        }

        /// <summary>
        /// Clear all cached managed-template ElementIds and seed views.
        /// Use sparingly — prefer the document-scoped overload.
        /// </summary>
        public static void InvalidateCache()
        {
            lock (_cacheLock) { _cache.Clear(); }
            lock (_seedCacheLock) { _seedViewCache.Clear(); }
        }

        /// <summary>
        /// C-6 / D-3: invalidate the cached managed-template + seed-view
        /// entries for a specific document. Wired to
        /// <see cref="DrawingTypeRegistry.Reload"/> and the document-closed
        /// handler in <c>StingToolsApp</c>.
        /// </summary>
        public static void InvalidateCache(Document doc)
        {
            string key = DocKey(doc);
            lock (_cacheLock)
            {
                if (_cache.ContainsKey(key)) _cache.Remove(key);
            }
            lock (_seedCacheLock)
            {
                if (_seedViewCache.ContainsKey(key)) _seedViewCache.Remove(key);
            }
        }

        private static Dictionary<(string, ViewType), ElementId> GetOrCreateCacheBucket(string docKey)
        {
            lock (_cacheLock)
            {
                if (!_cache.TryGetValue(docKey, out var bucket))
                {
                    bucket = new Dictionary<(string, ViewType), ElementId>();
                    _cache[docKey] = bucket;
                }
                return bucket;
            }
        }

        /// <summary>
        /// C-3: resolve a seed view of the requested type. Prefers existing
        /// "STING - " prefixed templates; falls back to any non-template view
        /// of that type. Result is cached per (docKey, viewType) so repeated
        /// calls during a batch only run the FilteredElementCollector twice
        /// once.
        /// </summary>
        private static View ResolveSeed(Document doc, ViewType viewType)
        {
            if (doc == null) return null;
            string docKey = DocKey(doc);
            ElementId cachedId;
            lock (_seedCacheLock)
            {
                if (_seedViewCache.TryGetValue(docKey, out var docMap)
                    && docMap.TryGetValue(viewType, out cachedId)
                    && cachedId != ElementId.InvalidElementId)
                {
                    if (doc.GetElement(cachedId) is View cached
                        && cached.IsValidObject
                        && cached.ViewType == viewType)
                    {
                        return cached;
                    }
                    docMap.Remove(viewType);
                }
            }

            View seed = new FilteredElementCollector(doc)
                .OfClass(typeof(View))
                .Cast<View>()
                .FirstOrDefault(t => t.IsTemplate
                                     && t.ViewType == viewType
                                     && (t.Name ?? "").StartsWith("STING - ", StringComparison.Ordinal));
            if (seed == null)
            {
                seed = new FilteredElementCollector(doc)
                    .OfClass(typeof(View))
                    .Cast<View>()
                    .FirstOrDefault(t => !t.IsTemplate && t.ViewType == viewType);
            }

            lock (_seedCacheLock)
            {
                if (!_seedViewCache.TryGetValue(docKey, out var docMap))
                {
                    docMap = new Dictionary<ViewType, ElementId>();
                    _seedViewCache[docKey] = docMap;
                }
                docMap[viewType] = seed?.Id ?? ElementId.InvalidElementId;
            }
            return seed;
        }

        internal static string GetManagedTemplateName(string packId, ViewType vt)
            => $"STING:{packId}:{vt}";

        // GAP-O: a managed template's name is exactly STING:{packId}:{ViewType}
        // — three colon-separated segments. Match the structure to avoid
        // sweeping up unrelated user templates like "STING:my-favourite".
        private static readonly System.Text.RegularExpressions.Regex _managedNameRegex
            = new System.Text.RegularExpressions.Regex(@"^STING:[A-Za-z0-9_\-\.]+:[A-Za-z][A-Za-z0-9]+$",
                System.Text.RegularExpressions.RegexOptions.Compiled);

        public static List<ElementId> GetAllManagedTemplates(Document doc)
        {
            var result = new List<ElementId>();
            if (doc == null) return result;
            foreach (var v in new FilteredElementCollector(doc)
                .OfClass(typeof(View))
                .Cast<View>()
                .Where(v => v.IsTemplate && _managedNameRegex.IsMatch(v.Name ?? string.Empty)))
            {
                // GAP-O: belt-and-braces — the stamp must also parse as a
                // managed-pack stamp ("pack=…|cs=…" / legacy "pack:…;cs=…").
                // Catches templates a user named to look like the format
                // but that were never actually written by the syncer.
                var raw = DrawingTypeStamper.ReadRaw(v);
                if (string.IsNullOrEmpty(raw)) continue;
                if (!raw.StartsWith("pack=", StringComparison.Ordinal)
                    && !raw.StartsWith("pack:", StringComparison.Ordinal)) continue;
                result.Add(v.Id);
            }
            return result;
        }

        internal static string ComputePackChecksum(ViewStylePack pack)
        {
            if (pack == null) return string.Empty;
            var fields = pack.ManagedFields ?? DefaultManagedFields;
            var probe = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            foreach (var f in fields)
            {
                switch (f)
                {
                    case "scale":             probe[f] = pack.LineWeightScale; break; // pack carries no Scale itself; placeholder
                    case "detailLevel":       probe[f] = pack.DimensionStyle; break;  // placeholder — pack has no detail-level field
                    case "discipline":        probe[f] = pack.Discipline; break;
                    case "tagColorScheme":    probe[f] = pack.TagColorScheme; break;
                    case "defaultTagStyle":   probe[f] = pack.DefaultTagStyle; break;
                    case "categoryTagStyles": probe[f] = pack.CategoryTagStyles; break;
                    case "visualStyle":       probe[f] = pack.VisualStyle; break;
                    case "phaseFilter":       probe[f] = pack.PhaseFilter; break;
                    case "phase":             probe[f] = pack.Phase; break;
                    case "annotationCrop":    probe[f] = pack.AnnotationCrop; break;
                    case "farClip":           probe[f] = pack.FarClipMm; break;
                    case "viewRange":         probe[f] = pack.ViewRange; break;
                    case "underlay":          probe[f] = pack.Underlay; break;
                    case "background":        probe[f] = pack.Background; break;
                    case "vgOverrides":       probe[f] = pack.VgOverrides; break;
                    case "filters":           probe[f] = pack.Filters; break;
                    case "worksetVisibility": probe[f] = pack.WorksetVisibility; break;
                    case "linkOverrides":     probe[f] = pack.LinkOverrides; break;
                    case "colorFillSchemes":  probe[f] = pack.ColorFillSchemes; break;
                    case "filterEnabled":     probe[f] = pack.FilterEnabled; break;
                    default:                  probe[f] = null; break;
                }
            }
            var json = JsonConvert.SerializeObject(probe);
            using (var sha = SHA256.Create())
            {
                var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(json));
                // ACC-08: full 64-char hex hash. The 16-char prefix was prone
                // to silent collisions on benign edits.
                var sb = new StringBuilder(bytes.Length * 2);
                foreach (var b in bytes) sb.Append(b.ToString("x2"));
                return sb.ToString();
            }
        }

        // ACC-09: stable structured prefix for the stamp format so that
        // typos in ad-hoc edits never silently parse to empty.
        private const string StampPrefix = "pack=";
        private const string StampChecksumKey = "|cs=";

        internal static string FormatStamp(string packId, string checksum)
            => $"{StampPrefix}{packId ?? string.Empty}{StampChecksumKey}{checksum ?? string.Empty}";

        internal static string GetStoredChecksum(View template)
        {
            if (template == null) return string.Empty;
            try
            {
                var p = template.LookupParameter(DrawingTypeStamper.PARAM_DRAWING_TYPE_ID);
                var stamp = p?.AsString();
                if (string.IsNullOrEmpty(stamp)) return string.Empty;
                // ACC-09: accept new "pack=…|cs=…" format and the legacy
                // "pack:…;cs=…" form so existing projects don't drift on
                // first reload.
                var idx = stamp.IndexOf(StampChecksumKey, StringComparison.Ordinal);
                if (idx >= 0) return stamp.Substring(idx + StampChecksumKey.Length);
                idx = stamp.IndexOf(";cs=", StringComparison.Ordinal);
                if (idx >= 0) return stamp.Substring(idx + 4);
                return string.Empty;
            }
            catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); return string.Empty; }
        }

        public static ElementId EnsureTemplate(Document doc, ViewStylePack pack, ViewType viewType, PackApplyResult result)
        {
            if (doc == null || pack == null || string.IsNullOrEmpty(pack.Id))
                return ElementId.InvalidElementId;
            if (result == null) result = new PackApplyResult();

            // 1. Cache hit (C-6 — per-document bucket; IsValidObject guard
            // catches a stale id from a copied / save-as'd document).
            string docKey = DocKey(doc);
            var bucket = GetOrCreateCacheBucket(docKey);
            var key = (pack.Id, viewType);
            ElementId cachedId;
            lock (_cacheLock)
            {
                bucket.TryGetValue(key, out cachedId);
            }
            if (cachedId != ElementId.InvalidElementId)
            {
                if (doc.GetElement(cachedId) is View v && v.IsValidObject && v.IsTemplate)
                    return cachedId;
                StingTools.Core.StingLog.Warn($"ManagedTemplateSyncer: stale cache id evicted for pack '{pack.Id}' / {viewType}");
                lock (_cacheLock) { bucket.Remove(key); }
            }

            var templateName = GetManagedTemplateName(pack.Id, viewType);

            // 2. Find existing
            View existing = new FilteredElementCollector(doc)
                .OfClass(typeof(View))
                .Cast<View>()
                .FirstOrDefault(t => t.IsTemplate &&
                    string.Equals(t.Name, templateName, StringComparison.Ordinal));

            if (existing != null)
            {
                var stored = GetStoredChecksum(existing);
                var current = ComputePackChecksum(pack);
                // Migration: a stored 16-char Base64 hash is legacy and
                // never matches a 64-char hex hash. Re-apply once, write
                // the new format; downstream Sync calls will then short-
                // circuit on identical hashes. Skipping the warning is
                // intentional — this is benign upgrade chatter.
                bool storedIsLegacy = !string.IsNullOrEmpty(stored) && stored.Length != 64;
                if (!string.Equals(stored, current, StringComparison.Ordinal))
                {
                    ApplyPackToTemplate(doc, existing, pack, result);
                    SetManagedTemplateParameterIds(doc, existing, pack);
                    StingTools.Core.ParameterHelpers.SetString(existing,
                        DrawingTypeStamper.PARAM_DRAWING_TYPE_ID,
                        FormatStamp(pack.Id, current), overwrite: true);
                    if (!storedIsLegacy && !string.IsNullOrEmpty(stored))
                        StingTools.Core.StingLog.Info(
                            $"ManagedTemplateSyncer: pack '{pack.Id}' template re-applied (checksum drift).");
                }
                lock (_cacheLock) { bucket[key] = existing.Id; }
                return existing.Id;
            }

            // 3. Find a seed template / view (C-3 — cached per
            // (docKey, viewType) so repeated calls in a batch don't re-scan).
            View seed = ResolveSeed(doc, viewType);
            if (seed == null)
            {
                result.Warnings.Add($"ManagedTemplateSyncer: no seed view of type {viewType} found for pack '{pack.Id}'.");
                return ElementId.InvalidElementId;
            }

            // 4. Copy seed
            ElementId newId;
            try
            {
                var copied = ElementTransformUtils.CopyElement(doc, seed.Id, XYZ.Zero);
                if (copied == null || copied.Count == 0)
                {
                    result.Warnings.Add("ManagedTemplateSyncer: seed copy returned no elements.");
                    return ElementId.InvalidElementId;
                }
                newId = copied.First();
            }
            catch (Exception ex)
            {
                result.Warnings.Add($"ManagedTemplateSyncer: seed copy failed — {ex.Message}");
                return ElementId.InvalidElementId;
            }

            var newView = doc.GetElement(newId) as View;
            if (newView == null)
            {
                result.Warnings.Add("ManagedTemplateSyncer: copy did not yield a View.");
                return ElementId.InvalidElementId;
            }

            try { newView.Name = templateName; }
            catch
            {
                try { newView.Name = templateName + "_(2)"; }
                catch (Exception ex2)
                {
                    result.Warnings.Add($"ManagedTemplateSyncer: rename failed — {ex.Message}");
                    return ElementId.InvalidElementId;
                }
            }

            ApplyPackToTemplate(doc, newView, pack, result);
            SetManagedTemplateParameterIds(doc, newView, pack);

            var checksum = ComputePackChecksum(pack);
            StingTools.Core.ParameterHelpers.SetString(newView,
                DrawingTypeStamper.PARAM_DRAWING_TYPE_ID,
                FormatStamp(pack.Id, checksum), overwrite: true);

            lock (_cacheLock) { bucket[key] = newView.Id; }
            return newView.Id;
        }

        private static void ApplyPackToTemplate(Document doc, View template, ViewStylePack pack, PackApplyResult r)
        {
            if (doc == null || template == null || pack == null) return;
            var fields = pack.ManagedFields ?? DefaultManagedFields;

            foreach (var field in fields)
            {
                try
                {
                    switch (field)
                    {
                        case "vgOverrides":
                            ViewStylePackApplier.ApplyCategoryOverridesOnly(doc, template, pack, r);
                            break;
                        case "filters":
                            ViewStylePackApplier.ApplyFilterRulesOnly(doc, template, pack, r);
                            break;
                        case "discipline":
                            SetIntBip(template, BuiltInParameter.VIEW_DISCIPLINE,
                                ResolveViewDiscipline(pack.Discipline), r, field);
                            break;
                        case "visualStyle":
                            SetIntBip(template, BuiltInParameter.MODEL_GRAPHICS_STYLE,
                                ResolveDisplayStyle(pack.VisualStyle), r, field);
                            break;
                        case "phaseFilter":
                            if (!string.IsNullOrEmpty(pack.PhaseFilter))
                            {
                                var pf = new FilteredElementCollector(doc)
                                    .OfClass(typeof(PhaseFilter))
                                    .Cast<PhaseFilter>()
                                    .FirstOrDefault(p => string.Equals(p.Name, pack.PhaseFilter, StringComparison.OrdinalIgnoreCase));
                                if (pf != null)
                                    SetElementIdBip(template, BuiltInParameter.VIEW_PHASE_FILTER, pf.Id, r, field);
                                else
                                    r.Warnings.Add($"PhaseFilter '{pack.PhaseFilter}' not found.");
                            }
                            break;
                        case "phase":
                            if (!string.IsNullOrEmpty(pack.Phase))
                            {
                                var ph = new FilteredElementCollector(doc)
                                    .OfClass(typeof(Phase))
                                    .Cast<Phase>()
                                    .FirstOrDefault(p => string.Equals(p.Name, pack.Phase, StringComparison.OrdinalIgnoreCase));
                                if (ph != null)
                                    SetElementIdBip(template, BuiltInParameter.VIEW_PHASE, ph.Id, r, field);
                                else
                                    r.Warnings.Add($"Phase '{pack.Phase}' not found.");
                            }
                            break;
                        case "annotationCrop":
                            if (pack.AnnotationCrop.HasValue)
                                SetIntBip(template, BuiltInParameter.VIEWER_ANNOTATION_CROP_ACTIVE,
                                    pack.AnnotationCrop.Value ? 1 : 0, r, field);
                            break;
                        case "farClip":
                            if (pack.FarClipMm.HasValue)
                                SetDoubleBip(template, BuiltInParameter.VIEWER_BOUND_OFFSET_FAR,
                                    pack.FarClipMm.Value / 304.8, r, field);
                            break;
                        case "viewRange":
                            ApplyViewRange(template, pack.ViewRange, r);
                            break;
                        case "worksetVisibility":
                            ViewStylePackApplier.ApplyWorksetVisibility(doc, template, pack, r);
                            break;
                        case "underlay":
                            ApplyUnderlay(doc, template, pack.Underlay, r);
                            break;
                        case "background":
                        case "displayOptions":
                        case "sun":
                            r.Warnings.Add($"Field '{field}' has no public Revit API — skipped.");
                            break;
                        case "tagColorScheme":
                            // INT-02: persist the pack's variable colour
                            // scheme onto the managed template so every view
                            // assigned to it inherits the same scheme.
                            if (!string.IsNullOrEmpty(pack.TagColorScheme))
                                StingTools.Core.ParameterHelpers.SetString(
                                    template, StingTools.Core.ParamRegistry.VIEW_TAG_STYLE,
                                    pack.TagColorScheme, overwrite: true);
                            break;
                        case "defaultTagStyle":
                            // INT-02: pack-level default tag style preset
                            // (canonical "{size}{style}_{color}") flows into
                            // the same TAG_*_BOOL switch logic used by
                            // TokenProfileApplier — but on the template, so
                            // it survives view-template re-assignment.
                            if (!string.IsNullOrEmpty(pack.DefaultTagStyle))
                                StingTools.Core.ParameterHelpers.SetString(
                                    template, "STING_DEFAULT_TAG_STYLE_TXT",
                                    pack.DefaultTagStyle, overwrite: true);
                            break;
                    }
                }
                catch (Exception ex) { r.Warnings.Add($"ApplyPackToTemplate('{field}'): {ex.Message}"); }
            }
        }

        private static void ApplyViewRange(View template, PackViewRange vr, PackApplyResult r)
        {
            if (vr == null) return;
            if (!(template is ViewPlan vp)) return;
            try
            {
                var pvr = vp.GetViewRange();
                if (vr.TopOffsetMm.HasValue)    pvr.SetOffset(PlanViewPlane.TopClipPlane,      vr.TopOffsetMm.Value / 304.8);
                if (vr.CutOffsetMm.HasValue)    pvr.SetOffset(PlanViewPlane.CutPlane,          vr.CutOffsetMm.Value / 304.8);
                if (vr.BottomOffsetMm.HasValue) pvr.SetOffset(PlanViewPlane.BottomClipPlane,   vr.BottomOffsetMm.Value / 304.8);
                if (vr.ViewDepthMm.HasValue)    pvr.SetOffset(PlanViewPlane.ViewDepthPlane,    vr.ViewDepthMm.Value / 304.8);
                vp.SetViewRange(pvr);
            }
            catch (Exception ex) { r.Warnings.Add($"ApplyViewRange: {ex.Message}"); }
        }

        private static void ApplyUnderlay(Document doc, View template, PackUnderlay u, PackApplyResult r)
        {
            if (u == null) return;
            if (!(template is ViewPlan vp)) return;
            try
            {
                if (!string.IsNullOrEmpty(u.LevelName))
                {
                    var lvl = new FilteredElementCollector(doc)
                        .OfClass(typeof(Level))
                        .Cast<Level>()
                        .FirstOrDefault(l => string.Equals(l.Name, u.LevelName, StringComparison.OrdinalIgnoreCase));
                    if (lvl != null)
                        SetElementIdBip(template, BuiltInParameter.VIEW_UNDERLAY_BOTTOM_ID, lvl.Id, r, "underlay.level");
                }
                if (!string.IsNullOrEmpty(u.Orientation))
                {
                    var oriValue = string.Equals(u.Orientation, "LookingDown", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
                    SetIntBip(template, BuiltInParameter.VIEW_UNDERLAY_ORIENTATION, oriValue, r, "underlay.orientation");
                }
            }
            catch (Exception ex) { r.Warnings.Add($"ApplyUnderlay: {ex.Message}"); }
        }

        private static void SetManagedTemplateParameterIds(Document doc, View template, ViewStylePack pack)
        {
            if (template == null || !template.IsTemplate) return;
            var fields = pack.ManagedFields ?? DefaultManagedFields;
            var paramIds = new List<ElementId>();

            foreach (var f in fields)
            {
                BuiltInParameter? bip = null;
                switch (f)
                {
                    case "scale":          bip = BuiltInParameter.VIEW_SCALE; break;
                    case "detailLevel":    bip = BuiltInParameter.VIEW_DETAIL_LEVEL; break;
                    case "discipline":     bip = BuiltInParameter.VIEW_DISCIPLINE; break;
                    case "visualStyle":    bip = BuiltInParameter.MODEL_GRAPHICS_STYLE; break;
                    case "phaseFilter":    bip = BuiltInParameter.VIEW_PHASE_FILTER; break;
                    case "phase":          bip = BuiltInParameter.VIEW_PHASE; break;
                    case "annotationCrop": bip = BuiltInParameter.VIEWER_ANNOTATION_CROP_ACTIVE; break;
                    case "farClip":        bip = BuiltInParameter.VIEWER_BOUND_OFFSET_FAR; break;
                    case "underlay":       bip = BuiltInParameter.VIEW_UNDERLAY_BOTTOM_ID; break;
                    // viewRange, vgOverrides, filters, worksetVisibility — no single BIP
                }
                if (!bip.HasValue) continue;
                try
                {
                    var p = template.get_Parameter(bip.Value);
                    if (p != null) paramIds.Add(p.Id);
                }
                catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
            }

            // Revit's API exposes SetNonControlledTemplateParameterIds (inverse
            // logic). For Phase 137 the value writes done via SetIntBip /
            // SetElementIdBip / SetDoubleBip already pin the managed values
            // onto the template; we leave Revit's "controlled by template"
            // flags at their defaults rather than computing the complement.
        }

        // ── Helpers ──

        private static int ResolveViewDiscipline(string discipline)
        {
            if (string.IsNullOrEmpty(discipline)) return -1;
            switch (discipline.Trim().ToLowerInvariant())
            {
                case "architectural": return 1;
                case "structural":    return 2;
                case "mechanical":    return 4096;
                case "electrical":    return 4097;
                case "plumbing":      return 4098;
                case "coordination":  return 4095;
                default:              return -1;
            }
        }

        private static int ResolveDisplayStyle(string style)
        {
            if (string.IsNullOrEmpty(style)) return -1;
            switch (style.Trim().ToLowerInvariant())
            {
                case "wireframe":    return (int)DisplayStyle.Wireframe;
                case "hiddenline":   return (int)DisplayStyle.HLR;
                case "shaded":       return (int)DisplayStyle.Shading;
                case "shadededges":  return (int)DisplayStyle.ShadingWithEdges;
                case "consistent":   return (int)DisplayStyle.FlatColors;
                case "realistic":    return (int)DisplayStyle.Realistic;
                // Revit API has no DisplayStyle.RayTrace; ray-trace is a
                // separate render pipeline, not a DisplayStyle enum value.
                default:             return -1;
            }
        }

        private static void SetIntBip(View v, BuiltInParameter bip, int value, PackApplyResult r, string label)
        {
            if (value < 0) return;
            try
            {
                var p = v.get_Parameter(bip);
                if (p == null || p.IsReadOnly) return;
                p.Set(value);
            }
            catch (Exception ex) { r.Warnings.Add($"SetIntBip({label}): {ex.Message}"); }
        }

        private static void SetDoubleBip(View v, BuiltInParameter bip, double value, PackApplyResult r, string label)
        {
            try
            {
                var p = v.get_Parameter(bip);
                if (p == null || p.IsReadOnly) return;
                p.Set(value);
            }
            catch (Exception ex) { r.Warnings.Add($"SetDoubleBip({label}): {ex.Message}"); }
        }

        private static void SetElementIdBip(View v, BuiltInParameter bip, ElementId id, PackApplyResult r, string label)
        {
            try
            {
                var p = v.get_Parameter(bip);
                if (p == null || p.IsReadOnly) return;
                p.Set(id);
            }
            catch (Exception ex) { r.Warnings.Add($"SetElementIdBip({label}): {ex.Message}"); }
        }
    }
}
