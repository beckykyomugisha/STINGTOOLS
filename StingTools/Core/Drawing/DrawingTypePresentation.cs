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
            public AnnotationRunStats Annotation { get; set; }
            public System.Collections.Generic.List<string> Warnings { get; } = new System.Collections.Generic.List<string>();
        }

        public static ApplyResult Apply(Document doc, View view, DrawingType dt, bool runAnnotation = true)
        {
            var r = new ApplyResult();
            if (doc == null || view == null || dt == null) return r;
            if (view.IsTemplate) return r;

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

            // Detail level -----------------------------------------------
            if (!string.IsNullOrWhiteSpace(dt.DetailLevel))
            {
                try
                {
                    ViewDetailLevel parsed;
                    switch (dt.DetailLevel.Trim().ToLowerInvariant())
                    {
                        case "coarse": parsed = ViewDetailLevel.Coarse; break;
                        case "fine":   parsed = ViewDetailLevel.Fine;   break;
                        default:       parsed = ViewDetailLevel.Medium; break;
                    }
                    view.DetailLevel = parsed;
                    r.DetailLevelApplied = true;
                }
                catch (Exception ex) { r.Warnings.Add($"DetailLevel {dt.DetailLevel}: {ex.Message}"); }
            }

            // View template ----------------------------------------------
            if (!string.IsNullOrWhiteSpace(dt.ViewTemplateName))
            {
                try
                {
                    var tpl = new FilteredElementCollector(doc)
                        .OfClass(typeof(View))
                        .Cast<View>()
                        .FirstOrDefault(v => v.IsTemplate
                            && string.Equals(v.Name, dt.ViewTemplateName, StringComparison.OrdinalIgnoreCase));
                    if (tpl != null)
                    {
                        view.ViewTemplateId = tpl.Id;
                        r.TemplateApplied = true;
                    }
                    else
                    {
                        r.Warnings.Add($"View template '{dt.ViewTemplateName}' not found in project.");
                    }
                }
                catch (Exception ex) { r.Warnings.Add($"ViewTemplate: {ex.Message}"); }
            }

            // View Style Pack (shared graphic overrides) ---------------
            if (!string.IsNullOrWhiteSpace(dt.ViewStylePackId))
            {
                try
                {
                    var pack = ViewStylePackRegistry.Get(doc, dt.ViewStylePackId);
                    if (pack != null)
                    {
                        var packStats = ViewStylePackApplier.Apply(doc, view, pack);
                        r.PackApplied = true;
                        r.Warnings.AddRange(packStats.Warnings);
                    }
                    else r.Warnings.Add($"ViewStylePack '{dt.ViewStylePackId}' not found.");
                }
                catch (Exception ex) { r.Warnings.Add($"ViewStylePack: {ex.Message}"); }
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
