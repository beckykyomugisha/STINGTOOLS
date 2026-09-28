// TierGateScope — which element types a depth sweep may write tier gates to.
//
// THE PROBLEM THIS SOLVES
//
// Five places write TAG_PARA_STATE_*: TagTypeVariantWriter (per type variant,
// 1..DepthTier — the catalogue's design), TagStyleEngine's style application
// (also per variant), and THREE sweeps that walk every ElementType in the
// project and set them all to one global depth — Set depth, Presentation mode,
// and TagStyleEngine.SetParagraphDepth.
//
// The sweeps overwrite the variants. Measured 2026-09-23 on STING_Tag_Universal:
// all ten gates read 1 while TAG_DEPTH_TIER_INT read 2, so a "_T2" variant was
// rendering as a T10. A state that should not be expressible, produced by two
// writers sharing one set of flags with no rule about who owns them.
//
// THE RULE
//
// A label row reads its gate from the TAGGED ELEMENT's type, proved 2026-09-23:
// ticking TAG_PARA_STATE_6_BOOL on an air terminal's type made the T6 rows draw
// and unticking it removed them, while the tag type's copy was ON throughout and
// drew nothing.
//
// So gates on MODEL types decide what renders, and gates on ANNOTATION types are
// the variant catalogue's own state. A global sweep has business with the first
// and none with the second. Writing them was not merely useless - it destroyed
// the only thing that distinguished one variant from another.
//
// Kept in one place, with the reason, because the same mistake was made
// independently in three commands.

namespace StingTools.Core
{
    public static class TierGateScope
    {
        /// <summary>
        /// May a global depth sweep write tier gates to a type in this category?
        ///
        /// <para><paramref name="isAnnotationCategory"/> is
        /// <c>Category.CategoryType == CategoryType.Annotation</c>. A type with no
        /// category at all is written: it cannot be a tag, and refusing it would
        /// silently drop model types whose category Revit does not report.</para>
        /// </summary>
        public static bool MaySweep(bool isAnnotationCategory) => !isAnnotationCategory;

        /// <summary>
        /// What to tell the user when a sweep wrote no model type at all.
        ///
        /// <para>Before this, such a run reported "N types updated" — N being the
        /// tag types it had just corrupted — and the drawing did not change. The
        /// number was true and the impression was false. Zero model carriers has
        /// exactly one cause worth naming, so name it.</para>
        /// </summary>
        public static string NoModelCarriersAdvice(int annotationTypesSkipped)
            => "No ELEMENT type carries the tier gates, so nothing on a drawing will change.\n\n"
               + "A label row reads its gate from the TAGGED ELEMENT's type, and "
               + "TAG_PARA_STATE_*_BOOL / TAG_DEPTH_TIER_INT ship bound to no model category. "
               + "Bind them Type-scoped to the categories you tag, then run this again.\n\n"
               + (annotationTypesSkipped > 0
                    ? $"{annotationTypesSkipped} tag type(s) were skipped on purpose: their gates "
                      + "belong to the type-variant catalogue and do not affect rendering."
                    : "No tag types were found either.");
    }
}
