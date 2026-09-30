// StingTools — Drawing Template Manager · "Duplicate as Dependent" decisions
//
// The production dialog has offered "Duplicate as Dependent" since Phase 137, and
// BatchProduceCommons turned it into ProduceOptions.DuplicateOption — which nothing
// read. Every scope-box / area view was an independent plan, so a level cut into
// six areas was six unrelated views: six copies of every tag, and a template or
// annotation change made six times.
//
// With the option set, a scope-box production on a level now makes (or finds) ONE
// parent plan per (drawing type, level, production rule) and each box's view is a
// dependent of it, cropped to the box. Annotation runs on the parent only: Revit
// shows a primary view's annotation in all its dependents, so tagging each
// dependent would tag the elements in every 2 m overlap strip twice.
//
// Revit-free: StingTools.Tags.Tests compiles this file. DrawingProducer calls it.

using System;

namespace StingTools.Core.Drawing
{
    /// <summary>What production does with a box's view when dependents are in use.</summary>
    public enum DependentViewAction
    {
        /// <summary>Dependents are not in use for this view — the ordinary path.</summary>
        Independent,
        /// <summary>No view yet for this box: duplicate the parent as a dependent.</summary>
        CreateDependent,
        /// <summary>The box already has a dependent of this parent: refresh its crop.</summary>
        ReuseDependent,
        /// <summary>
        /// The box already has a view that is not a dependent of this parent (produced
        /// before the option was chosen, or its parent was replaced). It is kept — a
        /// re-run never deletes a view someone may have dimensioned or placed — and the
        /// caller warns, so converting it is a decision a person makes.
        /// </summary>
        KeepExisting,
    }

    public static class DependentViewPlanner
    {
        /// <summary>
        /// The context tag the parent view is stamped with. It keeps the parent's
        /// identity (drawing type, level, rule, this tag) distinct from a per-level
        /// production of the same type, which has no tag and goes on a sheet — the
        /// parent is a working view and does not.
        /// </summary>
        public const string ParentTag = "STING-DEPENDENT-PARENT";

        /// <summary>
        /// Whether a view is produced as a dependent: only when the option is chosen, the
        /// context carries a scope box (the dependent's crop) AND a level (the parent's
        /// identity), and the rule makes a plan. Sections, elevations and 3D views have
        /// no per-level parent to hang from.
        /// </summary>
        public static bool UsesDependents(bool asDependentOption, bool hasScopeBox, bool hasLevel, string ruleViewType)
        {
            if (!asDependentOption || !hasScopeBox || !hasLevel) return false;
            switch ((ruleViewType ?? "").Trim())
            {
                case "FloorPlan":
                case "RCP":
                case "CeilingPlan":
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>
        /// What to do with the box's view. <paramref name="existingPrimaryId"/> is the
        /// existing view's primary (parent) view id, or a value ≤ 0 when it is not a
        /// dependent; <paramref name="parentId"/> is the parent this run found or made
        /// (≤ 0 when there is none yet).
        /// </summary>
        public static DependentViewAction ForBoxView(bool usesDependents, bool existingFound,
            long existingPrimaryId, long parentId)
        {
            if (!usesDependents) return DependentViewAction.Independent;
            if (!existingFound) return DependentViewAction.CreateDependent;
            if (existingPrimaryId > 0 && existingPrimaryId == parentId) return DependentViewAction.ReuseDependent;
            return DependentViewAction.KeepExisting;
        }

        /// <summary>"Power Layout - Level 1 - Parent" — says what the view is for in the browser.</summary>
        public static string ParentViewName(string drawingTypeName, string levelName, string ruleSuffix = null)
            => $"{(drawingTypeName ?? "").Trim()} - {(levelName ?? "").Trim()} - Parent{ruleSuffix ?? ""}".Trim();

        /// <summary>The warning a kept, non-dependent view earns — one wording, used by producer and tests.</summary>
        public static string KeptIndependentWarning(string viewName, string boxName)
            => $"'{viewName}' for scope box '{boxName}' already exists and is not a dependent of this level's parent view; "
             + "it was kept as it is. Delete it and produce again to make it a dependent.";
    }
}
