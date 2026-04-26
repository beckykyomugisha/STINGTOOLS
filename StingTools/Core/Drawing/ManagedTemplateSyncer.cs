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
        private static readonly Dictionary<(string packId, ViewType vt), ElementId> _cache
            = new Dictionary<(string, ViewType), ElementId>();

        // Default field set when ManagedFields is not specified.
        private static readonly List<string> DefaultManagedFields = new List<string>
        {
            "scale", "detailLevel", "discipline", "visualStyle", "phaseFilter"
        };

        public static void InvalidateCache()
        {
            _cache.Clear();
        }

        internal static string GetManagedTemplateName(string packId, ViewType vt)
            => $"STING:{packId}:{vt}";

        public static List<ElementId> GetAllManagedTemplates(Document doc)
        {
            var result = new List<ElementId>();
            if (doc == null) return result;
            foreach (var v in new FilteredElementCollector(doc)
                .OfClass(typeof(View))
                .Cast<View>()
                .Where(v => v.IsTemplate && (v.Name ?? "").StartsWith("STING:", StringComparison.Ordinal)))
            {
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
                var sb = new StringBuilder(bytes.Length * 2);
                foreach (var b in bytes) sb.Append(b.ToString("x2"));
                return sb.ToString().Substring(0, 16);
            }
        }

        internal static string GetStoredChecksum(View template)
        {
            if (template == null) return string.Empty;
            try
            {
                var p = template.LookupParameter(DrawingTypeStamper.PARAM_DRAWING_TYPE_ID);
                var stamp = p?.AsString();
                if (string.IsNullOrEmpty(stamp)) return string.Empty;
                var idx = stamp.IndexOf(";cs=", StringComparison.Ordinal);
                if (idx < 0) return string.Empty;
                return stamp.Substring(idx + 4);
            }
            catch { return string.Empty; }
        }

        public static ElementId EnsureTemplate(Document doc, ViewStylePack pack, ViewType viewType, PackApplyResult result)
        {
            if (doc == null || pack == null || string.IsNullOrEmpty(pack.Id))
                return ElementId.InvalidElementId;
            if (result == null) result = new PackApplyResult();

            // 1. Cache hit
            var key = (pack.Id, viewType);
            if (_cache.TryGetValue(key, out var cachedId))
            {
                if (doc.GetElement(cachedId) is View v && v.IsTemplate)
                    return cachedId;
                _cache.Remove(key);
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
                if (!string.Equals(stored, current, StringComparison.Ordinal))
                {
                    ApplyPackToTemplate(doc, existing, pack, result);
                    SetManagedTemplateParameterIds(doc, existing, pack);
                    StingTools.Core.ParameterHelpers.SetString(existing,
                        DrawingTypeStamper.PARAM_DRAWING_TYPE_ID,
                        $"pack:{pack.Id};cs={current}", overwrite: true);
                }
                _cache[key] = existing.Id;
                return existing.Id;
            }

            // 3. Find a seed template / view
            View seed = new FilteredElementCollector(doc)
                .OfClass(typeof(View))
                .Cast<View>()
                .FirstOrDefault(t => t.IsTemplate &&
                                     t.ViewType == viewType &&
                                     (t.Name ?? "").StartsWith("STING - ", StringComparison.Ordinal));
            if (seed == null)
            {
                seed = new FilteredElementCollector(doc)
                    .OfClass(typeof(View))
                    .Cast<View>()
                    .FirstOrDefault(t => !t.IsTemplate && t.ViewType == viewType);
            }
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
                catch (Exception ex)
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
                $"pack:{pack.Id};cs={checksum}", overwrite: true);

            _cache[key] = newView.Id;
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
                        SetElementIdBip(template, BuiltInParameter.VIEW_UNDERLAY_ID, lvl.Id, r, "underlay.level");
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
                    case "underlay":       bip = BuiltInParameter.VIEW_UNDERLAY_ID; break;
                    // viewRange, vgOverrides, filters, worksetVisibility — no single BIP
                }
                if (!bip.HasValue) continue;
                try
                {
                    var p = template.get_Parameter(bip.Value);
                    if (p != null) paramIds.Add(p.Id);
                }
                catch { }
            }

            try { template.SetTemplateParameterIds(paramIds); }
            catch (Exception ex)
            {
                StingTools.Core.StingLog.Warn(
                    $"ManagedTemplateSyncer.SetTemplateParameterIds failed: {ex.Message}");
            }
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
                case "raytrace":     return (int)DisplayStyle.RayTrace;
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
