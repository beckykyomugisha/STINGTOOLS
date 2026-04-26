// StingTools — Drawing Template Manager
//
// DrawingTypePresentation is the shared application step for batch
// generators (BatchSections, BatchElevations, BatchSheets, fabrication
// composer). Given a freshly-created View and a resolved DrawingType,
// it applies scale / detail level / view template, runs the annotation
// pass from the rule pack, and (optionally) sets the view's crop
// margin per the crop strategy.
//
// Batch commands should call Apply(...) inside their active Transaction
// after the view has been created but before it is placed on a sheet.
// Null drawingType is a no-op so adding the call is zero-regression.

using System;
using System.Linq;
using Autodesk.Revit.DB;

namespace StingTools.Core.Drawing
{
    public static class DrawingTypePresentation
    {
        public sealed class ApplyResult
        {
            public bool ScaleApplied       { get; set; }
            public bool DetailLevelApplied { get; set; }
            public bool TemplateApplied    { get; set; }
            public bool PackApplied        { get; set; }
            public bool CropApplied        { get; set; }
            public bool TokenProfileApplied { get; set; }   // Phase 135 — Step 7.5
            public AnnotationRunStats Annotation { get; set; }
            public System.Collections.Generic.List<string> Warnings { get; } = new System.Collections.Generic.List<string>();

            // Phase 137 — STING-Managed View Templates
            public ElementId ManagedTemplateId { get; set; } = ElementId.InvalidElementId;
            public bool ManagedTemplateCreated { get; set; }
            public bool ManagedTemplateUpdated { get; set; }
        }

        public static ApplyResult Apply(Document doc, View view, DrawingType dt, bool runAnnotation = true)
        {
            var r = new ApplyResult();
            if (doc == null || view == null || dt == null) return r;
            if (view.IsTemplate) return r;

            // Week 3 — stamp the DrawingType id so the Project Browser
            // organizer, the style-propagation IUpdater, and downstream
            // audits all know which profile produced this view. No-op
            // on projects where the shared param has not been bound;
            // no-op when user has locked the view's style.
            if (DrawingTypeStamper.IsLocked(view))
            {
                r.Warnings.Add($"View {view.Id} is style-locked; presentation skipped.");
                return r;
            }
            DrawingTypeStamper.Stamp(view, dt.Id);

            // Scale -------------------------------------------------------
            if (dt.Scale > 0)
            {
                try
                {
                    // Only views that expose Scale (plans, sections,
                    // elevations, drafting, 3D) accept an int; schedules
                    // and legends throw.
                    view.Scale = dt.Scale;
                    r.ScaleApplied = true;
                }
                catch (Exception ex) { r.Warnings.Add($"Scale 1:{dt.Scale}: {ex.Message}"); }
            }

            // Phase 136 — resolve the pack EARLY so its DetailLevel /
            // ViewTemplate can be used as a fallback when the DrawingType
            // does not set its own. Pack VG/filter application still
            // happens later (after crop) so there's no behaviour regression.
            ViewStylePack earlyPack = null;
            if (!string.IsNullOrWhiteSpace(dt.ViewStylePackId))
            {
                try { earlyPack = ViewStylePackRegistry.Get(doc, dt.ViewStylePackId); }
                catch (Exception ex) { r.Warnings.Add($"Pack lookup '{dt.ViewStylePackId}': {ex.Message}"); }
            }

            // Detail level — DrawingType wins, pack falls through ---------
            var effectiveDetail = !string.IsNullOrWhiteSpace(dt.DetailLevel)
                ? dt.DetailLevel
                : earlyPack?.DetailLevel;
            if (!string.IsNullOrWhiteSpace(effectiveDetail))
            {
                try
                {
                    ViewDetailLevel parsed;
                    switch (effectiveDetail.Trim().ToLowerInvariant())
                    {
                        case "coarse": parsed = ViewDetailLevel.Coarse; break;
                        case "fine":   parsed = ViewDetailLevel.Fine;   break;
                        default:       parsed = ViewDetailLevel.Medium; break;
                    }
                    view.DetailLevel = parsed;
                    r.DetailLevelApplied = true;
                }
                catch (Exception ex) { r.Warnings.Add($"DetailLevel {effectiveDetail}: {ex.Message}"); }
            }

            // View template — DrawingType wins, pack falls through --------
            var effectiveTemplate = !string.IsNullOrWhiteSpace(dt.ViewTemplateName)
                ? dt.ViewTemplateName
                : earlyPack?.ViewTemplate;
            if (!string.IsNullOrWhiteSpace(effectiveTemplate))
            {
                try
                {
                    var tpl = new FilteredElementCollector(doc)
                        .OfClass(typeof(View))
                        .Cast<View>()
                        .FirstOrDefault(v => v.IsTemplate
                            && string.Equals(v.Name, effectiveTemplate, StringComparison.OrdinalIgnoreCase));
                    if (tpl != null)
                    {
                        view.ViewTemplateId = tpl.Id;
                        r.TemplateApplied = true;
                    }
                    else
                    {
                        r.Warnings.Add($"View template '{effectiveTemplate}' not found in project.");
                    }
                }
                catch (Exception ex) { r.Warnings.Add($"ViewTemplate: {ex.Message}"); }
            }

            // Crop strategy (bonus) -------------------------------------
            if (dt.Crop != null)
            {
                try
                {
                    var cropWarns = DrawingCropApplier.Apply(doc, view, dt);
                    r.Warnings.AddRange(cropWarns);
                    r.CropApplied = true;
                }
                catch (Exception ex) { r.Warnings.Add($"CropApplier: {ex.Message}"); }
            }

            // View Style Pack — Phase 137 routing: managed vs external --
            // Reuse the early-resolved pack (Phase 136) so we only walk
            // the registry + extends-chain once per Apply call.
            ViewStylePack resolvedPack = earlyPack;
            if (resolvedPack != null)
            {
                try
                {
                    if (resolvedPack.IsManaged)
                    {
                        // MANAGED MODE — auto-generate / sync a Revit
                        // template, bind the view to it. Caller already
                        // owns an open transaction.
                        var syncResult = ManagedTemplateSyncer.EnsureTemplate(
                            doc, resolvedPack, view.ViewType);
                        r.Warnings.AddRange(syncResult.Warnings);

                        if (syncResult.TemplateId != ElementId.InvalidElementId)
                        {
                            try { view.ViewTemplateId = syncResult.TemplateId; }
                            catch (Exception ex) { r.Warnings.Add($"Bind managed template: {ex.Message}"); }

                            r.ManagedTemplateId = syncResult.TemplateId;
                            r.ManagedTemplateCreated = syncResult.Created;
                            r.ManagedTemplateUpdated = syncResult.Updated;
                            r.PackApplied = true;
                            // In managed mode, VG/filters are on the
                            // template itself — no per-view applier call.
                        }
                        else
                        {
                            // Could not create template (no seed) —
                            // gracefully fall through to per-view apply.
                            r.Warnings.Add(
                                $"Pack '{resolvedPack.Id}' is managed but no seed template exists. " +
                                "Falling back to per-view overrides for this view.");
                            var packStats = ViewStylePackApplier.Apply(doc, view, resolvedPack);
                            r.PackApplied = true;
                            r.Warnings.AddRange(packStats.Warnings);
                        }
                    }
                    else
                    {
                        // EXTERNAL MODE — existing behaviour unchanged.
                        var packStats = ViewStylePackApplier.Apply(doc, view, resolvedPack);
                        r.PackApplied = true;
                        r.Warnings.AddRange(packStats.Warnings);
                    }
                }
                catch (Exception ex) { r.Warnings.Add($"ViewStylePack: {ex.Message}"); }
            }
            else if (!string.IsNullOrWhiteSpace(dt.ViewStylePackId))
            {
                r.Warnings.Add($"ViewStylePack '{dt.ViewStylePackId}' not found.");
            }

            // Token Profile (Phase 135) — Step 7.5 -----------------------
            // Runs between the pack apply and the annotation pass so any
            // auto-tags AnnotationRunner emits inherit the active style
            // preset, paragraph depth, section visibility, and segment
            // mask. No-op when neither the profile nor the pack supplies
            // any tag-appearance value.
            if (dt.TokenProfile != null
                || resolvedPack?.TagColorScheme != null
                || resolvedPack?.DefaultTagStyle != null
                || (resolvedPack?.CategoryTagStyles != null && resolvedPack.CategoryTagStyles.Count > 0))
            {
                try
                {
                    var tpRes = TokenProfileApplier.Apply(doc, view, dt, resolvedPack);
                    r.TokenProfileApplied = tpRes.ViewParamWrites + tpRes.ElementWrites
                                          + tpRes.TypeWrites > 0 || tpRes.PresentationApplied;
                    r.Warnings.AddRange(tpRes.Warnings);
                }
                catch (Exception ex) { r.Warnings.Add($"TokenProfileApplier: {ex.Message}"); }
            }

            // Annotation pass --------------------------------------------
            if (runAnnotation && dt.Annotation != null)
            {
                try { r.Annotation = AnnotationRunner.Apply(doc, view, dt); }
                catch (Exception ex) { r.Warnings.Add($"AnnotationRunner: {ex.Message}"); }
            }

            return r;
        }
    }
}
