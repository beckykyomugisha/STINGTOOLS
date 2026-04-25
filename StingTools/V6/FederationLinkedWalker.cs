// Licensed to Planscape under the STING Tools plug-in LICENSE. See LICENSE.
//
// StingTools/V6/FederationLinkedWalker.cs — S6.3 (N-G7).
//
// Walks all RevitLinkInstance objects in the host document, loads
// each linked document via GetLinkDocument(), and exposes a
// per-model element dictionary plus federated iteration helpers.
// Transforms from link coordinates into host coordinates are
// applied automatically via RevitLinkInstance.GetTotalTransform().
//
// Used by Clash triage (S6.1), As-built reconciliation (S6.5), and
// any BCC dashboard metric that needs "X across all linked models".

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using StingTools.Core;

namespace StingTools.V6
{
    public sealed class LinkedModelView
    {
        public RevitLinkInstance Instance { get; set; }
        public Document LinkedDoc { get; set; }
        public Transform HostToLinkTransform { get; set; }   // inverse of GetTotalTransform
        public Transform LinkToHostTransform { get; set; }   // GetTotalTransform
        public string ModelPath { get; set; } = string.Empty;
    }

    public static class FederationLinkedWalker
    {
        /// <summary>
        /// Enumerate all valid linked models in the host document.
        /// Skips links that are unloaded, placeholder, or stale.
        /// </summary>
        public static List<LinkedModelView> EnumerateLinks(Document hostDoc)
        {
            var result = new List<LinkedModelView>();
            if (hostDoc == null) return result;
            try
            {
                var collector = new FilteredElementCollector(hostDoc)
                    .OfClass(typeof(RevitLinkInstance))
                    .Cast<RevitLinkInstance>();

                foreach (var rli in collector)
                {
                    if (rli == null || !rli.IsValidObject) continue;
                    Document linkedDoc;
                    try { linkedDoc = rli.GetLinkDocument(); }
                    catch (Exception ex)
                    {
                        StingLog.Warn($"FederationLinkedWalker: GetLinkDocument failed for {rli.Name}: {ex.Message}");
                        continue;
                    }
                    if (linkedDoc == null) continue;

                    var tx = rli.GetTotalTransform();
                    result.Add(new LinkedModelView
                    {
                        Instance             = rli,
                        LinkedDoc            = linkedDoc,
                        LinkToHostTransform  = tx,
                        HostToLinkTransform  = tx.Inverse,
                        ModelPath            = linkedDoc.PathName ?? string.Empty,
                    });
                }
            }
            catch (Exception ex)
            {
                StingLog.Error("FederationLinkedWalker.EnumerateLinks failed", ex);
            }
            return result;
        }

        /// <summary>
        /// For each linked model, apply <paramref name="query"/> to its
        /// document and aggregate the results into a (ModelPath, List)
        /// dictionary.
        /// </summary>
        public static Dictionary<string, List<T>> QueryAcrossLinks<T>(
            Document hostDoc, Func<Document, LinkedModelView, IEnumerable<T>> query)
        {
            var output = new Dictionary<string, List<T>>();
            if (hostDoc == null || query == null) return output;
            foreach (var link in EnumerateLinks(hostDoc))
            {
                try
                {
                    var results = query(link.LinkedDoc, link)?.ToList() ?? new List<T>();
                    output[link.ModelPath] = results;
                }
                catch (Exception ex)
                {
                    StingLog.Warn($"FederationLinkedWalker.QueryAcrossLinks for {link.ModelPath} failed: {ex.Message}");
                }
            }
            return output;
        }

        /// <summary>
        /// Collect tagged elements (via ElementMulticategoryFilter with
        /// SharedParamGuids.AllCategoryEnums — N-G1 quick filter) from
        /// the host plus every linked model, transforming bounding
        /// boxes into host coordinates.
        /// </summary>
        public static Dictionary<string, List<(Element el, BoundingBoxXYZ bbInHostFrame)>> CollectFederatedElements(
            Document hostDoc)
        {
            var bag = new Dictionary<string, List<(Element, BoundingBoxXYZ)>>();
            if (hostDoc == null) return bag;

            bag[hostDoc.PathName ?? "(host)"] = CollectFromDoc(hostDoc, Transform.Identity);
            foreach (var link in EnumerateLinks(hostDoc))
            {
                bag[link.ModelPath] = CollectFromDoc(link.LinkedDoc, link.LinkToHostTransform);
            }
            return bag;
        }

        private static List<(Element, BoundingBoxXYZ)> CollectFromDoc(Document doc, Transform tx)
        {
            var list = new List<(Element, BoundingBoxXYZ)>();
            try
            {
                var col = new FilteredElementCollector(doc)
                    .WherePasses(new ElementMulticategoryFilter(SharedParamGuids.AllCategoryEnums))
                    .WhereElementIsNotElementType();
                foreach (var el in col)
                {
                    var bb = el.get_BoundingBox(null);
                    if (bb == null) continue;
                    if (tx != null && !tx.IsIdentity)
                        bb = TransformAabb(bb, tx);
                    list.Add((el, bb));
                }
            }
            catch (Exception ex)
            {
                StingLog.Warn($"CollectFromDoc({doc?.PathName}) failed: {ex.Message}");
            }
            return list;
        }

        private static BoundingBoxXYZ TransformAabb(BoundingBoxXYZ src, Transform tx)
        {
            var corners = new[]
            {
                new XYZ(src.Min.X, src.Min.Y, src.Min.Z),
                new XYZ(src.Max.X, src.Min.Y, src.Min.Z),
                new XYZ(src.Min.X, src.Max.Y, src.Min.Z),
                new XYZ(src.Max.X, src.Max.Y, src.Min.Z),
                new XYZ(src.Min.X, src.Min.Y, src.Max.Z),
                new XYZ(src.Max.X, src.Min.Y, src.Max.Z),
                new XYZ(src.Min.X, src.Max.Y, src.Max.Z),
                new XYZ(src.Max.X, src.Max.Y, src.Max.Z),
            };
            double minx = double.MaxValue, miny = double.MaxValue, minz = double.MaxValue;
            double maxx = double.MinValue, maxy = double.MinValue, maxz = double.MinValue;
            foreach (var c in corners)
            {
                var t = tx.OfPoint(c);
                if (t.X < minx) minx = t.X; if (t.X > maxx) maxx = t.X;
                if (t.Y < miny) miny = t.Y; if (t.Y > maxy) maxy = t.Y;
                if (t.Z < minz) minz = t.Z; if (t.Z > maxz) maxz = t.Z;
            }
            return new BoundingBoxXYZ { Min = new XYZ(minx, miny, minz), Max = new XYZ(maxx, maxy, maxz) };
        }
    }
}
