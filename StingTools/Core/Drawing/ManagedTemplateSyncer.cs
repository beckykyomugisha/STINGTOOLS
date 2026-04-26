// StingTools — Drawing Template Manager · Phase 137
//
// ManagedTemplateSyncer auto-generates and maintains Revit view
// templates from a managed-mode ViewStylePack. One template per
// (pack.Id, ViewType) pair, named STING:<pack-id>:<viewType>.
// Stamped with two shared parameters for drift detection:
//   STING_PACK_ID_TXT       — pack id
//   STING_PACK_CHECKSUM_TXT — SHA-256 of the pack JSON (managedFields scope)
//
// EnsureTemplate is the single entry point. It is idempotent:
//   * absent  → create from a seed template (no public Revit API
//               for creating a template from scratch — IsTemplate is
//               read-only — so we copy a sibling template of the
//               right ViewType and rename the copy)
//   * present + checksum matches → no-op
//   * present + checksum drifted → re-apply the pack settings + restamp
//
// Must be called inside an open Transaction (the caller — typically
// DrawingTypePresentation.Apply — already has one).

using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Autodesk.Revit.DB;
using Newtonsoft.Json;

namespace StingTools.Core.Drawing
{
    public sealed class EnsureResult
    {
        public ElementId TemplateId { get; set; } = ElementId.InvalidElementId;
        public bool Created { get; set; }
        public bool Updated { get; set; }
        public List<string> Warnings { get; } = new List<string>();
    }

    public static class ManagedTemplateSyncer
    {
        // Default fields written when ManagedFields is null/empty.
        private static readonly string[] DefaultManagedFields =
            new[] { "vg", "filters", "detailLevel", "discipline", "phaseFilter" };

        /// <summary>
        /// Ensure a Revit view template exists for (pack, targetViewType).
        /// Creates by copying a seed template (no API for creating one
        /// from scratch); diffs via SHA-256 checksum; re-applies on
        /// drift. Caller must own an open Transaction.
        /// </summary>
        public static EnsureResult EnsureTemplate(
            Document doc, ViewStylePack pack, ViewType targetViewType)
        {
            var result = new EnsureResult();
            if (doc == null || pack == null) return result;
            if (string.IsNullOrWhiteSpace(pack.Id))
            {
                result.Warnings.Add("Pack has no id — cannot manage template.");
                return result;
            }

            // STEP A — checksum
            string checksum = ComputePackChecksum(pack);

            // STEP B — find existing
            string templateName = $"STING:{pack.Id}:{targetViewType}";
            var existing = new FilteredElementCollector(doc)
                .OfClass(typeof(View))
                .Cast<View>()
                .FirstOrDefault(v => v.IsTemplate
                                  && v.ViewType == targetViewType
                                  && string.Equals(v.Name, templateName, StringComparison.Ordinal));

            // STEP C — create if absent
            if (existing == null)
            {
                var seed = new FilteredElementCollector(doc)
                    .OfClass(typeof(View))
                    .Cast<View>()
                    .FirstOrDefault(v => v.IsTemplate && v.ViewType == targetViewType);
                if (seed == null)
                {
                    result.Warnings.Add(
                        $"No seed template of type {targetViewType} found. " +
                        "Create at least one Revit view template of this type, " +
                        "then STING will manage it automatically.");
                    return result;
                }

                ICollection<ElementId> copiedIds;
                try
                {
                    copiedIds = ElementTransformUtils.CopyElement(doc, seed.Id, XYZ.Zero);
                }
                catch (Exception ex)
                {
                    result.Warnings.Add($"Template copy failed: {ex.Message}");
                    return result;
                }
                if (copiedIds == null || copiedIds.Count == 0)
                {
                    result.Warnings.Add("Template copy returned no element.");
                    return result;
                }
                existing = doc.GetElement(copiedIds.First()) as View;
                if (existing == null)
                {
                    result.Warnings.Add("Template copy did not yield a View.");
                    return result;
                }
                try { existing.Name = templateName; }
                catch
                {
                    // Name collision fallback — guarantee a unique name
                    existing.Name = templateName + "_" + Guid.NewGuid().ToString("N").Substring(0, 6);
                }
                result.Created = true;
            }

            // STEP D — checksum match? skip
            string storedChecksum = ReadStringParam(existing, "STING_PACK_CHECKSUM_TXT");
            if (!result.Created && string.Equals(storedChecksum, checksum, StringComparison.Ordinal))
            {
                result.TemplateId = existing.Id;
                return result;
            }

            // STEP E — apply pack settings
            ApplyPackToTemplate(doc, existing, pack, result);
            result.Updated = true;

            // STEP F — restamp
            StampTemplate(existing, pack.Id, checksum, result);
            result.TemplateId = existing.Id;
            return result;
        }

        // ── Pack → template settings application ────────────────────────

        private static void ApplyPackToTemplate(
            Document doc, View template, ViewStylePack pack, EnsureResult result)
        {
            var allowed = ResolveManagedFields(pack);

            // vg (category overrides)
            if (allowed.Contains("vg"))
            {
                try
                {
                    var r = ViewStylePackApplier.ApplyCategoryOverridesOnly(doc, template, pack);
                    foreach (var w in r.Warnings) result.Warnings.Add($"vg: {w}");
                }
                catch (Exception ex) { result.Warnings.Add($"vg: {ex.Message}"); }
            }

            // filters
            if (allowed.Contains("filters"))
            {
                try
                {
                    var r = ViewStylePackApplier.ApplyFilterRulesOnly(doc, template, pack);
                    foreach (var w in r.Warnings) result.Warnings.Add($"filters: {w}");
                }
                catch (Exception ex) { result.Warnings.Add($"filters: {ex.Message}"); }
            }

            // detailLevel — pack does not carry one; the editor stores
            // it via the legacy ViewStylePack.DetailLevel property which
            // does not exist on the Core model. The DrawingType profile
            // owns detail level; ApplyDetailLevelFromAux is a no-op.

            // discipline
            if (allowed.Contains("discipline") && !string.IsNullOrWhiteSpace(pack.Discipline))
            {
                try
                {
                    if (Enum.TryParse<ViewDiscipline>(pack.Discipline, true, out var vd))
                        template.Discipline = vd;
                    else result.Warnings.Add($"discipline: '{pack.Discipline}' is not a recognised ViewDiscipline.");
                }
                catch (Exception ex) { result.Warnings.Add($"discipline: {ex.Message}"); }
            }

            // visualStyle
            if (allowed.Contains("visualStyle") && !string.IsNullOrWhiteSpace(pack.VisualStyle))
            {
                try
                {
                    if (Enum.TryParse<DisplayStyle>(pack.VisualStyle, true, out var ds))
                        template.DisplayStyle = ds;
                    else result.Warnings.Add($"visualStyle: '{pack.VisualStyle}' is not a recognised DisplayStyle.");
                }
                catch (Exception ex) { result.Warnings.Add($"visualStyle: {ex.Message}"); }
            }

            // phaseFilter — View base class doesn't expose a PhaseFilter
            // property (only ViewPlan / ViewSection do), so route through
            // the parameter API which works for every view type.
            if (allowed.Contains("phaseFilter") && !string.IsNullOrWhiteSpace(pack.PhaseFilter))
            {
                try
                {
                    var pf = new FilteredElementCollector(doc).OfClass(typeof(PhaseFilter))
                        .Cast<PhaseFilter>()
                        .FirstOrDefault(x => string.Equals(x.Name, pack.PhaseFilter,
                            StringComparison.OrdinalIgnoreCase));
                    if (pf != null)
                    {
                        var pfParam = template.get_Parameter(BuiltInParameter.VIEW_PHASE_FILTER);
                        if (pfParam != null && !pfParam.IsReadOnly) pfParam.Set(pf.Id);
                        else result.Warnings.Add("phaseFilter: parameter is read-only on this view type.");
                    }
                    else result.Warnings.Add($"phaseFilter: '{pack.PhaseFilter}' not found in project.");
                }
                catch (Exception ex) { result.Warnings.Add($"phaseFilter: {ex.Message}"); }
            }

            // phaseName
            if (allowed.Contains("phaseName") && !string.IsNullOrWhiteSpace(pack.PhaseName))
            {
                try
                {
                    var ph = new FilteredElementCollector(doc).OfClass(typeof(Phase))
                        .Cast<Phase>()
                        .FirstOrDefault(x => string.Equals(x.Name, pack.PhaseName,
                            StringComparison.OrdinalIgnoreCase));
                    if (ph != null)
                    {
                        var p = template.get_Parameter(BuiltInParameter.VIEW_PHASE);
                        if (p != null && !p.IsReadOnly) p.Set(ph.Id);
                    }
                    else result.Warnings.Add($"phaseName: '{pack.PhaseName}' not found in project.");
                }
                catch (Exception ex) { result.Warnings.Add($"phaseName: {ex.Message}"); }
            }

            // annotationCrop
            if (allowed.Contains("annotationCrop") && pack.AnnotationCrop.HasValue)
            {
                try
                {
                    var p = template.get_Parameter(BuiltInParameter.VIEWER_ANNOTATION_CROP_ACTIVE);
                    if (p != null && !p.IsReadOnly)
                        p.Set(pack.AnnotationCrop.Value ? 1 : 0);
                }
                catch (Exception ex) { result.Warnings.Add($"annotationCrop: {ex.Message}"); }
            }

            // farClip
            if (allowed.Contains("farClip") && pack.FarClipMm.HasValue)
            {
                try
                {
                    var p = template.get_Parameter(BuiltInParameter.VIEWER_BOUND_OFFSET_FAR);
                    if (p != null && !p.IsReadOnly)
                        p.Set(MmToFeet(pack.FarClipMm.Value));
                }
                catch (Exception ex) { result.Warnings.Add($"farClip: {ex.Message}"); }
            }

            // viewRange (plans only)
            if (allowed.Contains("viewRange") && pack.ViewRange != null && template is ViewPlan vp)
            {
                try
                {
                    var vr = vp.GetViewRange();
                    vr.SetOffset(PlanViewPlane.TopClipPlane,    MmToFeet(pack.ViewRange.TopMm));
                    vr.SetOffset(PlanViewPlane.CutPlane,        MmToFeet(pack.ViewRange.CutPlaneMm));
                    vr.SetOffset(PlanViewPlane.BottomClipPlane, MmToFeet(pack.ViewRange.BottomMm));
                    vr.SetOffset(PlanViewPlane.ViewDepthPlane,  MmToFeet(pack.ViewRange.ViewDepthMm));
                    vp.SetViewRange(vr);
                }
                catch (Exception ex) { result.Warnings.Add($"viewRange: {ex.Message}"); }
            }

            // underlay (plans only)
            if (allowed.Contains("underlay") && pack.Underlay != null && template is ViewPlan vpu)
            {
                try
                {
                    if (!string.Equals(pack.Underlay.BaseLevel, "off", StringComparison.OrdinalIgnoreCase))
                    {
                        var lvl = new FilteredElementCollector(doc).OfClass(typeof(Level))
                            .Cast<Level>()
                            .FirstOrDefault(l => string.Equals(l.Name, pack.Underlay.BaseLevel,
                                StringComparison.OrdinalIgnoreCase));
                        if (lvl != null)
                        {
                            vpu.get_Parameter(BuiltInParameter.VIEW_UNDERLAY_BOTTOM_ID)?.Set(lvl.Id);
                        }
                        else result.Warnings.Add($"underlay: base level '{pack.Underlay.BaseLevel}' not found.");
                    }
                    else
                    {
                        vpu.get_Parameter(BuiltInParameter.VIEW_UNDERLAY_BOTTOM_ID)?.Set(ElementId.InvalidElementId);
                    }
                }
                catch (Exception ex) { result.Warnings.Add($"underlay: {ex.Message}"); }
            }

            // displayOptions — no public API for shadows / sketchyLines /
            // ambientShadows in all Revit versions. Warn so the user
            // knows to set them manually inside Revit's template editor.
            if (allowed.Contains("displayOptions") && pack.DisplayOptions != null)
            {
                result.Warnings.Add(
                    "displayOptions (shadows/sketchyLines/ambientShadows) require manual " +
                    "configuration in Revit's template editor — no public API available.");
            }
        }

        // ── helpers ─────────────────────────────────────────────────────

        private static HashSet<string> ResolveManagedFields(ViewStylePack pack)
        {
            IEnumerable<string> source =
                pack.ManagedFields != null && pack.ManagedFields.Count > 0
                    ? pack.ManagedFields
                    : DefaultManagedFields;
            return new HashSet<string>(source.Select(s => (s ?? "").Trim()),
                StringComparer.OrdinalIgnoreCase);
        }

        private static double MmToFeet(double mm) => mm / 304.8;

        /// <summary>SHA-256 of the pack's JSON (managed-fields scope).</summary>
        public static string ComputePackChecksum(ViewStylePack pack)
        {
            try
            {
                // Serialise without the managedChecksum (it is the output)
                // so the hash is stable across re-stamps.
                var prior = pack.ManagedChecksum;
                pack.ManagedChecksum = null;
                var json = JsonConvert.SerializeObject(pack, Formatting.None);
                pack.ManagedChecksum = prior;

                using (var sha = SHA256.Create())
                {
                    var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(json));
                    var sb = new StringBuilder(hash.Length * 2);
                    foreach (var b in hash) sb.Append(b.ToString("x2"));
                    return sb.ToString().Substring(0, 32);
                }
            }
            catch { return ""; }
        }

        private static void StampTemplate(View template, string packId, string checksum, EnsureResult result)
        {
            try
            {
                var pId = template.LookupParameter("STING_PACK_ID_TXT");
                if (pId != null && !pId.IsReadOnly && pId.StorageType == StorageType.String)
                    pId.Set(packId ?? "");
                else if (pId == null)
                    result.Warnings.Add(
                        "STING_PACK_ID_TXT shared parameter is not bound to Views — " +
                        "drift detection on managed templates will not work. " +
                        "Bind via Project Parameters → add to View category.");

                var pCk = template.LookupParameter("STING_PACK_CHECKSUM_TXT");
                if (pCk != null && !pCk.IsReadOnly && pCk.StorageType == StorageType.String)
                    pCk.Set(checksum ?? "");
                else if (pCk == null)
                    result.Warnings.Add(
                        "STING_PACK_CHECKSUM_TXT shared parameter is not bound to Views — " +
                        "managed-template drift detection will not work.");
            }
            catch (Exception ex)
            {
                result.Warnings.Add($"Stamp: {ex.Message}");
            }
        }

        private static string ReadStringParam(Element el, string paramName)
        {
            try
            {
                var p = el?.LookupParameter(paramName);
                if (p == null || p.StorageType != StorageType.String) return null;
                return p.AsString();
            }
            catch { return null; }
        }
    }
}
