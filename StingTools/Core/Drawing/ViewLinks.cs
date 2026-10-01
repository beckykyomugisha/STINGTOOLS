// StingTools — Drawing Template Manager · linked models in a view (DTW-85)
//
// Drawing production sees linked models (DTW-49), but every annotation
// collector was host-only: a plan of linked MEP got no tags, and a grid chain
// in an MEP model whose grids live in the linked architectural model found no
// grids. These helpers give the annotation passes the loaded links shown in a
// view and the linked elements Revit draws there
// (FilteredElementCollector(hostDoc, viewId, linkInstanceId), Revit 2024+),
// with the transform that maps link coordinates into the host.

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;

namespace StingTools.Core.Drawing
{
    internal sealed class ViewLink
    {
        public RevitLinkInstance Instance;
        public Document Doc;
        public Transform Transform;
        public string Name;
    }

    /// <summary>A straight grid as the view shows it, in host coordinates.</summary>
    internal sealed class ViewGridLine
    {
        public Line Line;
        public Reference Ref;
        public string Name;
        public bool Linked;
    }

    internal static class ViewLinks
    {
        /// <summary>
        /// Loaded link instances visible in <paramref name="view"/>. An unloaded link
        /// is reported once in <paramref name="warnings"/>, never silently dropped.
        /// </summary>
        public static List<ViewLink> InView(Document doc, View view, List<string> warnings = null)
        {
            var list = new List<ViewLink>();
            if (doc == null || view == null) return list;
            try
            {
                foreach (var li in new FilteredElementCollector(doc, view.Id)
                    .OfClass(typeof(RevitLinkInstance))
                    .Cast<RevitLinkInstance>())
                {
                    Document ld = null;
                    try { ld = li.GetLinkDocument(); }
                    catch (Exception ex) { StingLog.Warn($"ViewLinks: link {li.Id} document: {ex.Message}"); }
                    if (ld == null)
                    {
                        warnings?.Add($"Link '{li.Name}' is not loaded — its elements are not annotated on '{view.Name}'.");
                        continue;
                    }
                    Transform xf;
                    try { xf = li.GetTotalTransform(); }
                    catch (Exception ex)
                    {
                        warnings?.Add($"Link '{li.Name}': no transform ({ex.Message}) — its elements are not annotated.");
                        continue;
                    }
                    list.Add(new ViewLink { Instance = li, Doc = ld, Transform = xf, Name = li.Name });
                }
            }
            catch (Exception ex)
            {
                warnings?.Add($"Could not list linked models in '{view.Name}' ({ex.Message}) — linked elements are not annotated.");
            }
            return list;
        }

        /// <summary>Elements of <paramref name="bic"/> from one link that the view draws.</summary>
        public static IList<Element> Visible(Document doc, View view, ViewLink link, BuiltInCategory bic)
            => new FilteredElementCollector(doc, view.Id, link.Instance.Id)
                .OfCategory(bic)
                .WhereElementIsNotElementType()
                .ToElements();

        /// <summary>
        /// Every straight grid the view shows — the host's and each loaded link's —
        /// in host coordinates, each with a reference a dimension can use (a link
        /// reference for a linked grid). Host grids first, so where a host grid and a
        /// linked grid coincide (copy / monitor) the chain keeps the host one.
        /// </summary>
        public static List<ViewGridLine> StraightGrids(Document doc, View view, List<string> warnings, out int arcs)
        {
            arcs = 0;
            var list = new List<ViewGridLine>();
            foreach (var g in new FilteredElementCollector(doc, view.Id)
                .OfCategory(BuiltInCategory.OST_Grids)
                .WhereElementIsNotElementType()
                .OfType<Grid>())
            {
                if (g.Curve is Line line) list.Add(new ViewGridLine { Line = line, Ref = new Reference(g), Name = g.Name });
                else arcs++;
            }

            foreach (var link in InView(doc, view, warnings))
            {
                try
                {
                    foreach (var g in Visible(doc, view, link, BuiltInCategory.OST_Grids).OfType<Grid>())
                    {
                        if (!(g.Curve is Line line)) { arcs++; continue; }
                        list.Add(new ViewGridLine
                        {
                            Line = (Line)line.CreateTransformed(link.Transform),
                            Ref = new Reference(g).CreateLinkReference(link.Instance),
                            Name = g.Name,
                            Linked = true,
                        });
                    }
                }
                catch (Exception ex)
                {
                    warnings?.Add($"Grids in link '{link.Name}' could not be read ({ex.Message}) — not dimensioned.");
                }
            }
            return list;
        }

        /// <summary>
        /// The element a reference points at — through the link when it is a link
        /// reference — so "is this view already dimensioned to grids / levels"
        /// recognises a chain to linked grids.
        /// </summary>
        public static Element Resolve(Document doc, Reference r)
        {
            if (doc == null || r == null) return null;
            var el = doc.GetElement(r);
            if (r.LinkedElementId != null && r.LinkedElementId != ElementId.InvalidElementId
                && el is RevitLinkInstance li)
            {
                var ld = li.GetLinkDocument();
                return ld?.GetElement(r.LinkedElementId);
            }
            return el;
        }
    }
}
