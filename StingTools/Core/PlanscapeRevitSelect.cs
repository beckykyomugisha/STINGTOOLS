// planscape://revit/select — the Revit half. See PlanscapeProtocol.BuildRevitSelectLink for
// the link and docs/PLANSCAPE_PROTOCOL.md §5 for why it exists: ACC's Issues API cannot pin
// an issue to model objects, so a clash escalated to ACC carries this link instead.
//
// Runs on Revit's API thread (PlanscapeLinkWatcher, an IIdlingJob). Resolution is by
// UniqueId only, via Document.GetElement(string): a UniqueId that is not in a model resolves
// to null, never to some other element, so the worst case is "not found", said out loud.
//
// Where it looks, in order: the active model; then the models linked into it (a KUT MEP
// model usually links the architecture, and the clash's other side lives there). It cannot
// switch to another open model - OpenAndActivateDocument is not permitted from an Idling
// handler - so a model that is open but not active is NAMED, and the user clicks again.
//
// Verified by build only. Nobody has clicked one of these links in a live Revit session.

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace StingTools.Core
{
    internal static class PlanscapeRevitSelect
    {
        private const string Title = "Planscape link — locate elements";

        public static void Handle(UIApplication uiApp, PlanscapeLink link)
        {
            if (!PlanscapeProtocol.TryParseRevitSelect(link, out string modelName, out List<string> uids, out int omitted))
            {
                TaskDialog.Show(Title, $"This link names no elements to locate:\n\n{link?.Raw}");
                return;
            }

            UIDocument uidoc = uiApp?.ActiveUIDocument;
            Document doc = uidoc?.Document;
            if (doc == null || doc.IsFamilyDocument)
            {
                TaskDialog.Show(Title, $"Open the model{Named(modelName)} first, then click the link again.");
                return;
            }

            var hostHits = new List<Element>();
            var linkHits = new List<(RevitLinkInstance inst, Element el)>();
            var missing = new List<string>();

            List<RevitLinkInstance> links = null;
            foreach (string uid in uids)
            {
                Element el = TryGet(doc, uid);
                if (el != null) { hostHits.Add(el); continue; }

                links ??= new FilteredElementCollector(doc).OfClass(typeof(RevitLinkInstance))
                    .Cast<RevitLinkInstance>().ToList();
                bool inLink = false;
                foreach (var inst in links)
                {
                    Document ld = null;
                    try { ld = inst.GetLinkDocument(); } catch (Exception) { /* unloaded link */ }
                    Element lel = ld != null ? TryGet(ld, uid) : null;
                    if (lel == null) continue;
                    linkHits.Add((inst, lel));
                    inLink = true;
                    break;
                }
                if (!inLink) missing.Add(uid);
            }

            if (hostHits.Count == 0 && linkHits.Count == 0)
            {
                TaskDialog.Show(Title, NothingFoundMessage(uiApp, doc, modelName, uids.Count));
                StingLog.Warn($"PlanscapeRevitSelect: none of {uids.Count} element(s) found in '{doc.Title}' or its links ({link.Raw}).");
                return;
            }

            // Select everything found, host and linked, in one call: SetElementIds cannot carry
            // a linked element, SetReferences can (Revit 2023+).
            try
            {
                var refs = new List<Reference>();
                refs.AddRange(hostHits.Select(e => new Reference(e)));
                foreach (var (inst, el) in linkHits)
                {
                    try { refs.Add(new Reference(el).CreateLinkReference(inst)); }
                    catch (Exception ex) { StingLog.Warn($"PlanscapeRevitSelect: link reference for {el.UniqueId}: {ex.Message}"); }
                }
                uidoc.Selection.SetReferences(refs);
            }
            catch (Exception ex)
            {
                StingLog.Warn("PlanscapeRevitSelect: selection failed: " + ex.Message);
            }

            string zoomNote = Show(uidoc, hostHits, linkHits);

            int found = hostHits.Count + linkHits.Count;
            StingLog.Info($"PlanscapeRevitSelect: located {found}/{uids.Count} in '{doc.Title}' " +
                          $"({hostHits.Count} in the model, {linkHits.Count} in links); {missing.Count} missing; {omitted} omitted by the link.");

            // Silent success when everything was found; otherwise say exactly what was not.
            if (missing.Count > 0 || omitted > 0 || zoomNote != null)
            {
                var msg = new System.Text.StringBuilder();
                msg.AppendLine($"Selected {found} of {uids.Count} element(s)" +
                               (linkHits.Count > 0 ? $" ({linkHits.Count} in linked models)." : "."));
                if (missing.Count > 0)
                {
                    msg.AppendLine();
                    msg.AppendLine($"{missing.Count} not found in '{doc.Title}' or its loaded links — deleted since the ACC " +
                                   "model version, or in a model that is neither open nor linked here:");
                    foreach (var m in missing.Take(6)) msg.AppendLine("  " + m);
                    if (missing.Count > 6) msg.AppendLine($"  … and {missing.Count - 6} more");
                }
                if (omitted > 0)
                {
                    msg.AppendLine();
                    msg.AppendLine($"The link itself left out {omitted} further element id(s) to stay within the ACC description limit.");
                }
                if (zoomNote != null) { msg.AppendLine(); msg.AppendLine(zoomNote); }
                TaskDialog.Show(Title, msg.ToString().TrimEnd());
            }
        }

        private static Element TryGet(Document d, string uid)
        {
            try { return d.GetElement(uid); }
            catch (Exception) { return null; }   // a malformed id is "not found", not a crash
        }

        /// <summary>Zoom to what was found. Returns a note when the zoom could not happen.</summary>
        private static string Show(UIDocument uidoc, List<Element> hostHits, List<(RevitLinkInstance inst, Element el)> linkHits)
        {
            try
            {
                if (hostHits.Count > 0)
                {
                    uidoc.ShowElements(hostHits.Select(e => e.Id).ToList());
                    return null;
                }

                // Only linked elements: ShowElements takes host ids, so zoom the active view to
                // their bounding box in host coordinates.
                BoundingBoxXYZ box = null;
                foreach (var (inst, el) in linkHits)
                {
                    var bb = el.get_BoundingBox(null);
                    if (bb == null) continue;
                    var t = inst.GetTotalTransform();
                    foreach (var p in Corners(bb))
                    {
                        var h = t.OfPoint(p);
                        if (box == null) box = new BoundingBoxXYZ { Min = h, Max = h };
                        else
                        {
                            box.Min = new XYZ(Math.Min(box.Min.X, h.X), Math.Min(box.Min.Y, h.Y), Math.Min(box.Min.Z, h.Z));
                            box.Max = new XYZ(Math.Max(box.Max.X, h.X), Math.Max(box.Max.Y, h.Y), Math.Max(box.Max.Z, h.Z));
                        }
                    }
                }
                if (box == null) return "The linked element(s) have no geometry to zoom to; they are selected.";

                var uiView = uidoc.GetOpenUIViews().FirstOrDefault(v => v.ViewId == uidoc.ActiveView?.Id);
                if (uiView == null) return "No open view to zoom; the linked element(s) are selected.";
                var pad = new XYZ(3, 3, 3);   // ~1 m around the elements
                uiView.ZoomAndCenterRectangle(box.Min - pad, box.Max + pad);
                return null;
            }
            catch (Exception ex)
            {
                StingLog.Warn("PlanscapeRevitSelect: zoom failed: " + ex.Message);
                return "The element(s) are selected, but Revit could not zoom to them in a view: " + ex.Message;
            }
        }

        private static IEnumerable<XYZ> Corners(BoundingBoxXYZ b)
        {
            foreach (double x in new[] { b.Min.X, b.Max.X })
                foreach (double y in new[] { b.Min.Y, b.Max.Y })
                    foreach (double z in new[] { b.Min.Z, b.Max.Z })
                        yield return new XYZ(x, y, z);
        }

        private static string NothingFoundMessage(UIApplication uiApp, Document active, string modelName, int count)
        {
            string head = $"None of the {count} element(s) in this link are in '{active.Title}' or the models linked into it.";
            if (string.IsNullOrEmpty(modelName))
                return head + "\n\nOpen the model the issue was raised on, then click the link again.";

            if (PlanscapeProtocol.ModelNameMatches(modelName, active.Title))
                return head + $"\n\nThis is the model the link names ('{modelName}'), so the element(s) have most likely " +
                       "been deleted or replaced since the ACC model version the clash was found in.";

            // Is the named model open, just not active? Revit cannot switch to it from here.
            try
            {
                foreach (Document d in uiApp.Application.Documents)
                {
                    if (d == null || d.IsLinked || d.IsFamilyDocument || d.Equals(active)) continue;
                    if (PlanscapeProtocol.ModelNameMatches(modelName, d.Title))
                        return head + $"\n\n'{d.Title}' is open but not the active model. Switch to it, then click the link again.";
                }
            }
            catch (Exception) { /* enumeration failed: fall through to the generic wording */ }

            return head + $"\n\nThe link is for '{modelName}'. Open that model (or one that links it), then click the link again.";
        }

        private static string Named(string modelName) =>
            string.IsNullOrEmpty(modelName) ? "" : $" '{modelName}'";
    }
}
