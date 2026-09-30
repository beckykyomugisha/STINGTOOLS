// StingTools — View templates · temporary base views for the templates drawing types name
//
// A view template can only be made FROM a view of the kind it will be applied to
// (View.CreateViewTemplate), and a template made from a floor plan cannot be applied to
// a section. ViewTemplates phase 3 used to skip every section / elevation / ceiling-plan
// / 3D / schedule template when the model happened to have no view of that kind — on a
// fresh project, that is most of them — and report "create one and re-run".
//
// This decides what to make instead: the temporary views, in creation order, that give
// the kind a base. They are deleted once the templates exist (the template does not
// depend on the view it was made from). A kind with no creatable base is refused with
// the reason.
//
// Revit-free: StingTools.Tags.Tests compiles this file.

using System.Collections.Generic;

namespace StingTools.Core.Drawing
{
    public static class TemporaryBaseViews
    {
        /// <summary>
        /// The view kinds (DrawingViewKind values) to create, in order, so that a view of
        /// <paramref name="kind"/> exists; the last is the base itself. Empty when
        /// <paramref name="have"/> already holds the kind. Null, with
        /// <paramref name="why"/>, when no base can be created.
        /// <paramref name="have"/> is what the model (and earlier temporaries) already hold.
        /// </summary>
        public static List<string> Plan(string kind, ICollection<string> have, out string why)
        {
            why = null;
            var steps = new List<string>();
            bool Has(string k) => have != null && have.Contains(k);
            switch (kind)
            {
                case DrawingViewKind.FloorPlan:
                case DrawingViewKind.Rcp:
                case DrawingViewKind.Section:
                case DrawingViewKind.ThreeD:
                case DrawingViewKind.Drafting:
                case DrawingViewKind.Schedule:
                    if (!Has(kind)) steps.Add(kind);
                    return steps;
                case DrawingViewKind.Detail:
                    // A detail's template is made from a section (ViewTemplates has always
                    // used the section base for both).
                    if (!Has(DrawingViewKind.Section)) steps.Add(DrawingViewKind.Section);
                    return steps;
                case DrawingViewKind.Elevation:
                    // An elevation is hosted by a marker drawn in a plan view.
                    if (Has(kind)) return steps;
                    if (!Has(DrawingViewKind.FloorPlan)) steps.Add(DrawingViewKind.FloorPlan);
                    steps.Add(kind);
                    return steps;
                case DrawingViewKind.Legend:
                    why = "the Revit API cannot create a legend view, so a legend template has no base — duplicate a legend in the model and re-run";
                    return null;
                default:
                    why = $"a {kind} view cannot carry a view template";
                    return null;
            }
        }

        /// <summary>
        /// The kind whose base view a template of <paramref name="kind"/> is made from:
        /// a detail's is a section; every other kind is its own.
        /// </summary>
        public static string BaseKindFor(string kind)
            => kind == DrawingViewKind.Detail ? DrawingViewKind.Section : kind;
    }
}
