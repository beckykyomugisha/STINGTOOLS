// StingTools — Drawing Template Manager · print overrides
//
// DrawingType.Print (colourScheme / lineWeightScale / halftoneLinks) was
// read by exactly two things: the Excel round-trip, which exports and
// re-imports it, and one title-block variant condition that matches on
// colourScheme. Nothing applied it to a view. So 90 shipped profiles
// carried a print block whose lineWeightScale (0.6 – 1.1 on the
// presentation and clarification profiles, whose entire purpose is
// lighter line work) and halftoneLinks flag did nothing at all.
//
// This file gives the two graphical fields effect.
//
//   halftoneLinks   — halftone every Revit link in the view, via a
//                     category override on OST_RvtLinks. Note that
//                     RevitLinkGraphicsSettings (the per-link route) has no
//                     Halftone member on the 2025 API, so the category
//                     override is not a fallback here — it is the route.
//                     It also matches the field's granularity: one boolean
//                     for the whole view, not per link.
//
//   lineWeightScale — folded into the pack's line-weight scale rather than
//                     applied separately, because two independent passes
//                     over the same OverrideGraphicSettings would compound
//                     unpredictably depending on order. See
//                     EffectiveLineWeightScale.
//
// colourScheme stays declarative: it selects title-block variants
// (DrawingDispatcher) and is the documented hook for an export preset. It
// is not a view-level graphic setting in Revit, so there is nothing
// honest to apply — the validator now at least checks it against a closed
// vocabulary (DT-142) so a typo cannot silently mean "no scheme".

using System;
using System.Linq;
using Autodesk.Revit.DB;
using StingTools.Core;

namespace StingTools.Core.Drawing
{
    internal static class DrawingPrintApplier
    {
        /// <summary>
        /// Combined line-weight scale for a profile: the pack's scale times
        /// the profile's print scale. One number, applied once, so the two
        /// cannot compound in an order-dependent way.
        ///
        /// Revit-free, so the combination rule is unit-testable.
        /// </summary>
        internal static double EffectiveLineWeightScale(ViewStylePack pack, DrawingType dt)
        {
            double packScale  = pack?.LineWeightScale ?? 1.0;
            double printScale = dt?.Print?.LineWeightScale ?? 1.0;
            if (packScale  <= 0) packScale  = 1.0;
            if (printScale <= 0) printScale = 1.0;
            return packScale * printScale;
        }

        /// <summary>
        /// Halftone (or restore) every Revit link in the view per
        /// <c>print.halftoneLinks</c>. A false value is NOT written back as
        /// "un-halftone" — that would fight a user's own link settings on
        /// every re-apply. Only true acts.
        /// </summary>
        internal static void ApplyHalftoneLinks(Document doc, View view, DrawingType dt, PackApplyResult r)
        {
            if (doc == null || view == null || dt?.Print == null) return;
            if (!dt.Print.HalftoneLinks) return;

            // A false value is deliberately NOT written back as "un-halftone":
            // that would fight the user's own link settings on every re-apply.
            // Only true acts.
            try
            {
                bool anyLinks = new FilteredElementCollector(doc)
                    .OfClass(typeof(RevitLinkType)).Any();
                if (!anyLinks)
                {
                    r?.Warnings.Add($"print.halftoneLinks is set on '{dt.Id}' but this project has no Revit links.");
                    return;
                }

                var cat = Category.GetCategory(doc, BuiltInCategory.OST_RvtLinks);
                if (cat == null)
                {
                    r?.Warnings.Add("print.halftoneLinks: the Revit Links category is not available in this document.");
                    return;
                }

                var ogs = view.GetCategoryOverrides(cat.Id) ?? new OverrideGraphicSettings();
                ogs.SetHalftone(true);
                view.SetCategoryOverrides(cat.Id, ogs);
                r?.Warnings.Add($"print.halftoneLinks: Revit Links halftoned in '{view.Name}'.");
            }
            catch (Exception ex)
            {
                // Loud, not silent: a print setting that could not be applied
                // changes what an issued drawing looks like.
                r?.Warnings.Add($"print.halftoneLinks could not be applied to '{view.Name}': {ex.Message}");
                StingLog.Warn($"ApplyHalftoneLinks({view?.Name}): {ex.Message}");
            }
        }
    }
}
