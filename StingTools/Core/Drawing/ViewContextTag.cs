// StingTools — Drawing Template Manager · the production context tag, and what a
// re-applied profile does to a scope-box crop
//
// DrawingProducer stamps every view it makes with STING_VIEW_CONTEXT_TAG_TXT:
//
//     <level>::<room>::<tag>[::<scope box name>]
//
// The scope box is last and is the only part that may itself contain "::" (every
// STING box name does: STING-AREA::A01::L02). Level names, room ids and tags do not.
//
// Why this file exists. A view produced FOR a scope box is cropped to it by passing the
// box as ApplyOptions.ContextScopeBox. Everything that re-applies the drawing type
// later — Sync Styles, Produce & Export's style phase, a production re-run's refresh,
// the drift heal — called DrawingTypePresentation.Apply WITHOUT that option, so the
// profile's own crop kind ran instead: a TightBbox profile re-cropped a box view to the
// model extents, a ScopeBox profile swapped in its named box. A re-sync silently
// changed or dropped the crop the view was produced with.
//
// Now Apply recovers the box when none is passed (DecideRecovery): a view whose tag
// names a box keeps the box it is currently cropped to; if that has been lost, the box
// the tag names is put back. A view whose tag names no box is untouched by this — its
// crop is the profile's, as before.
//
// Revit-free: StingTools.Tags.Tests compiles this file.

using System;

namespace StingTools.Core.Drawing
{
    public enum CropRecovery
    {
        /// <summary>The view was not produced for a scope box: apply the profile's crop.</summary>
        UseProfile,
        /// <summary>Keep the scope box the view is cropped to now.</summary>
        KeepAssigned,
        /// <summary>The view lost its box: re-assign the one the context tag names.</summary>
        RestoreFromTag,
        /// <summary>The tag names a box that no longer exists and the view has none: leave the crop alone.</summary>
        LeaveAlone,
    }

    public static class ViewContextTag
    {
        public const string Separator = "::";

        /// <summary>The tag DrawingProducer stamps. The box, when present, is appended last.</summary>
        public static string Compose(string level, string room, string tag, string scopeBox)
        {
            var t = $"{level ?? ""}{Separator}{room ?? ""}{Separator}{tag ?? ""}";
            return string.IsNullOrEmpty(scopeBox) ? t : t + Separator + scopeBox;
        }

        /// <summary>
        /// The scope-box name a context tag ends with, or null when it names none. The
        /// first three segments are level, room and tag; whatever follows the third
        /// separator is the box name, "::" and all.
        /// </summary>
        public static string ScopeBoxName(string contextTag)
        {
            if (string.IsNullOrEmpty(contextTag)) return null;
            int pos = 0;
            for (int i = 0; i < 3; i++)
            {
                int at = contextTag.IndexOf(Separator, pos, StringComparison.Ordinal);
                if (at < 0) return null;
                pos = at + Separator.Length;
            }
            var box = contextTag.Substring(pos);
            return box.Length == 0 ? null : box;
        }

        /// <summary>
        /// What a profile re-apply does to the crop of a view whose context tag names
        /// <paramref name="tagBoxName"/> (null: none), that is cropped now to a box
        /// (<paramref name="hasAssignedBox"/>), where <paramref name="tagBoxExists"/>
        /// says the named box is still in the model.
        /// </summary>
        public static CropRecovery Decide(string tagBoxName, bool hasAssignedBox, bool tagBoxExists)
        {
            if (string.IsNullOrEmpty(tagBoxName)) return CropRecovery.UseProfile;
            if (hasAssignedBox) return CropRecovery.KeepAssigned;
            return tagBoxExists ? CropRecovery.RestoreFromTag : CropRecovery.LeaveAlone;
        }
    }
}
