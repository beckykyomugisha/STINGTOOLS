// StingTools — material callouts (SPECIALIST_TAG_BUILD_SHEET §5, ROADMAP MATTAG-2..4).
//
// Two rule kinds:
//   MaterialTag        — a callout on the face you see (elevations). category = the hosts
//                        to tag ("*" = walls, floors, roofs, ceilings). Curtain walls are
//                        tagged through their panels, stacked walls through their members,
//                        family instances through their own geometry. A painted face that
//                        faces the viewer wins, and reports the PAINT material. One callout
//                        per material within MaterialCalloutPlan.DefaultSpacingPaperMm on
//                        paper; callouts already on the view count, so a re-run adds nothing.
//   MaterialTagLayers  — build-up callouts in a section / detail: every host is cut, each
//                        distinct material of its cut faces gets one callout, heads stacked
//                        in a column beside the element. A host already carrying a material
//                        tag in the view is skipped (idempotent).
//
// The tag family is a Material Tags symbol: the rule's tagFamily, else the pack's
// tagFamilies["Materials"], else the first loaded. Every callout on a material with no
// code (MAT_CODE or Mark) is still placed — the gap shows on the drawing — and counted.
//
// NOT VERIFIED IN REVIT: face references from curtain panels / family-instance geometry,
// cut-face references in section views, and the paint read-back are the Revit-bound
// risks. The decisions (which face, thinning, stacking) are Revit-free and tested.

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using StingTools.Core;

namespace StingTools.Core.Drawing
{
    public static partial class AnnotationRunner
    {
        private static readonly BuiltInCategory[] DefaultMaterialHosts =
        {
            BuiltInCategory.OST_Walls, BuiltInCategory.OST_Floors,
            BuiltInCategory.OST_Roofs, BuiltInCategory.OST_Ceilings,
        };

        private const double CalloutHeadOffsetPaperMm = 12.0;
        private const double BuildUpColumnOffsetPaperMm = 15.0;
        private const double BuildUpPitchPaperMm = 6.0;

        /// <summary>A face that could carry a material tag, with what the callout needs.</summary>
        private sealed class FaceCand
        {
            public Reference Ref;
            public XYZ Point;
            public XYZ Normal;
            public double Dot, Area;
            public bool Painted;
            public ElementId Material = ElementId.InvalidElementId;
        }

        private static void RunMaterialCallouts(Document doc, View view, AnnotationRulePack pack,
            AutoAnnotationRule rule, bool layers, AnnotationRunStats stats, DrawingType drawingType)
        {
            string label = layers ? AnnotationRuleKinds.MaterialTagLayers : AnnotationRuleKinds.MaterialTag;
            if (layers && view.ViewType != ViewType.Section && view.ViewType != ViewType.Detail)
            {
                stats.Warnings.Add($"{label}: '{view.Name}' is not a section or detail — build-up callouts need cut faces; skipped.");
                return;
            }

            var symId = ResolveMaterialTagSymbol(doc, pack, rule, stats, label);
            if (symId == ElementId.InvalidElementId) return;
            if (drawingType != null) symId = ApplyTagSizeVariant(doc, symId, drawingType, "Materials", stats);

            var hosts = CollectMaterialHosts(doc, view, rule, stats, label);
            if (hosts.Count == 0) return;

            bool addLeader = !string.Equals((rule?.LeaderStyle ?? "").Trim(), "NoLeader", StringComparison.OrdinalIgnoreCase);
            var existing = ExistingMaterialCallouts(doc, view, out var taggedHosts);
            double paper = Math.Max(1, view.Scale) / 304.8;   // 1 paper mm in model feet

            int placed = 0, noFace = 0, failed = 0, thinned = 0, skipped = 0;
            var uncoded = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);

            if (!layers)
            {
                var picks = new List<(Element El, FaceCand Face)>();
                foreach (var el in hosts)
                {
                    var faces = FaceCandidates(doc, el, view);
                    int best = FaceChoice.Best(faces.Select(f => f.Dot).ToList(), faces.Select(f => f.Area).ToList(),
                                               faces.Select(f => f.Painted).ToList());
                    if (best < 0) { noFace++; continue; }
                    picks.Add((el, faces[best]));
                }
                var plan = picks.Select(p => ToCandidate(view, p.Face)).ToList();
                var keep = MaterialCalloutPlan.Thin(plan,
                    MaterialCalloutPlan.SpacingFeet(MaterialCalloutPlan.DefaultSpacingPaperMm, view.Scale), existing);
                thinned = picks.Count - keep.Count;

                var diag = (view.RightDirection + view.UpDirection).Normalize();
                foreach (int i in keep)
                {
                    var (el, f) = picks[i];
                    NoteUncoded(doc, f.Material, uncoded);
                    var head = f.Point + diag * (CalloutHeadOffsetPaperMm * paper);
                    if (PlaceMaterialTag(doc, view, symId, f.Ref, addLeader, head, el, label, stats)) placed++; else failed++;
                }
            }
            else
            {
                foreach (var el in hosts)
                {
                    if (taggedHosts.Contains(el.Id)) { skipped++; continue; }
                    var cuts = CutFacesByMaterial(el, view);
                    if (cuts.Count == 0) { noFace++; continue; }

                    var uv = cuts.Select(c => (U: c.Point.DotProduct(view.RightDirection), V: c.Point.DotProduct(view.UpDirection))).ToList();
                    double uMax = uv.Max(p => p.U), vMax = uv.Max(p => p.V);
                    var bb = el.get_BoundingBox(view);
                    if (bb != null)
                        foreach (var corner in new[] { bb.Min, bb.Max })
                        {
                            uMax = Math.Max(uMax, corner.DotProduct(view.RightDirection));
                            vMax = Math.Max(vMax, corner.DotProduct(view.UpDirection));
                        }
                    var heads = MaterialCalloutPlan.StackHeads(uv, uMax + BuildUpColumnOffsetPaperMm * paper,
                                                               vMax, BuildUpPitchPaperMm * paper);
                    for (int k = 0; k < cuts.Count; k++)
                    {
                        var c = cuts[k];
                        NoteUncoded(doc, c.Material, uncoded);
                        var head = c.Point + view.RightDirection * (heads[k].U - uv[k].U)
                                           + view.UpDirection * (heads[k].V - uv[k].V);
                        if (PlaceMaterialTag(doc, view, symId, c.Ref, true, head, el, label, stats)) placed++; else failed++;
                    }
                }
            }

            stats.TagsPlaced += placed;
            stats.Skipped += skipped + thinned;
            if (noFace > 0)
                stats.Warnings.Add(layers
                    ? $"{label}: {noFace} host(s) show no cut face with a reference in '{view.Name}' — no build-up callout."
                    : $"{label}: {noFace} host(s) had no face a material tag could reference — not tagged.");
            if (thinned > 0)
                StingLog.Info($"{label} '{view.Name}': {thinned} callout(s) thinned — the material is already called out nearby.");
            if (failed > 0)
                stats.Warnings.Add($"{label}: {failed} material callout(s) could not be created — see the log.");
            if (uncoded.Count > 0)
                stats.Warnings.Add($"{label}: callouts on {uncoded.Count} material(s) with no code print blank — " +
                                   $"{string.Join(", ", uncoded.Take(6))}{(uncoded.Count > 6 ? " …" : "")}. Run Materials_SyncIdentity.");
        }

        /// <summary>The Material Tags symbol: rule tagFamily → pack tagFamilies["Materials"] → first loaded.</summary>
        private static ElementId ResolveMaterialTagSymbol(Document doc, AnnotationRulePack pack,
            AutoAnnotationRule rule, AnnotationRunStats stats, string label)
        {
            var all = new FilteredElementCollector(doc).OfClass(typeof(FamilySymbol)).Cast<FamilySymbol>()
                .Where(fs => fs.Category?.Id.Value == (long)BuiltInCategory.OST_MaterialTags).ToList();
            foreach (var name in new[] { rule?.TagFamily, pack?.TagFamilies != null && pack.TagFamilies.TryGetValue("Materials", out var m) ? m : null })
            {
                if (string.IsNullOrWhiteSpace(name)) continue;
                var hit = all.FirstOrDefault(fs => string.Equals(fs.FamilyName, name.Trim(), StringComparison.OrdinalIgnoreCase));
                if (hit != null) return hit.Id;
                stats.Warnings.Add($"{label}: material tag family '{name}' is not loaded (or is not a Material Tag) — trying the next choice.");
            }
            var first = all.FirstOrDefault();
            if (first == null)
            {
                stats.Warnings.Add($"{label}: no Material Tag family is loaded — load STING - Material Callout Tag " +
                                   "(SPECIALIST_TAG_BUILD_SHEET §5) or any material tag. Nothing placed.");
                return ElementId.InvalidElementId;
            }
            return first.Id;
        }

        /// <summary>
        /// Hosts to call out. "*" (or blank) = walls, floors, roofs, ceilings. Curtain walls
        /// become their panels and stacked walls their members — the faces you see belong to
        /// those, not to the wall.
        /// </summary>
        private static List<Element> CollectMaterialHosts(Document doc, View view, AutoAnnotationRule rule,
            AnnotationRunStats stats, string label)
        {
            var cats = new List<BuiltInCategory>();
            string key = (rule?.Category ?? "").Trim();
            if (key.Length == 0 || key == "*") cats.AddRange(DefaultMaterialHosts);
            else
            {
                var id = ResolveCategoryId(doc, key);
                if (id == ElementId.InvalidElementId || !Enum.IsDefined(typeof(BuiltInCategory), id.Value))
                {
                    stats.Warnings.Add($"{label}: category '{key}' not found in this document — skipped.");
                    return new List<Element>();
                }
                cats.Add((BuiltInCategory)id.Value);
            }

            var familyRx = RuleFamilyFilter.Compile(rule?.FamilyMatch, out var rxError);
            if (rxError != null) { stats.Warnings.Add($"{label}: {rxError}"); return new List<Element>(); }

            var result = new List<Element>();
            var seen = new HashSet<ElementId>();
            void Add(Element e) { if (e != null && seen.Add(e.Id)) result.Add(e); }

            foreach (var bic in cats)
                foreach (var el in new FilteredElementCollector(doc, view.Id).OfCategory(bic).WhereElementIsNotElementType())
                {
                    if (familyRx != null)
                    {
                        var et = doc.GetElement(el.GetTypeId()) as ElementType;
                        if (!RuleFamilyFilter.Matches(familyRx, et?.FamilyName, et?.Name)) continue;
                    }
                    if (el is Wall w)
                    {
                        try
                        {
                            var kindOfWall = w.WallType?.Kind;
                            if (kindOfWall == WallKind.Curtain && w.CurtainGrid != null)
                            {
                                foreach (var pid in w.CurtainGrid.GetPanelIds()) Add(doc.GetElement(pid));
                                continue;
                            }
                            if (kindOfWall == WallKind.Stacked)
                            {
                                foreach (var mid in w.GetStackedWallMemberIds()) Add(doc.GetElement(mid));
                                continue;
                            }
                        }
                        catch (Exception ex) { StingLog.Warn($"{label}: expanding wall {w.Id}: {ex.Message}"); }
                    }
                    Add(el);
                }
            return result;
        }

        /// <summary>
        /// Candidate faces of <paramref name="el"/>. Hosts give their finish faces
        /// (HostObjectUtils); everything else its geometry — instance geometry first,
        /// symbol geometry (transformed) when the instance faces carry no reference.
        /// </summary>
        private static List<FaceCand> FaceCandidates(Document doc, Element el, View view)
        {
            var found = new List<(Reference Ref, Face Face, Transform Tf)>();
            try
            {
                if (el is Wall wall)
                {
                    foreach (var r in HostObjectUtils.GetSideFaces(wall, ShellLayerType.Exterior)
                                 .Concat(HostObjectUtils.GetSideFaces(wall, ShellLayerType.Interior)))
                        if (el.GetGeometryObjectFromReference(r) is Face f) found.Add((r, f, Transform.Identity));
                }
                else if (el is HostObject host)
                {
                    foreach (var r in HostObjectUtils.GetTopFaces(host).Concat(HostObjectUtils.GetBottomFaces(host)))
                        if (el.GetGeometryObjectFromReference(r) is Face f) found.Add((r, f, Transform.Identity));
                }
                else
                {
                    var ge = el.get_Geometry(new Options { ComputeReferences = true, View = view });
                    if (ge != null)
                        foreach (var go in ge)
                        {
                            if (go is Solid s) AddFaces(s, Transform.Identity, found);
                            else if (go is GeometryInstance gi)
                            {
                                int before = found.Count;
                                foreach (var ig in gi.GetInstanceGeometry())
                                    if (ig is Solid si) AddFaces(si, Transform.Identity, found);
                                if (found.Count == before)
                                    foreach (var sg in gi.GetSymbolGeometry())
                                        if (sg is Solid ss) AddFaces(ss, gi.Transform, found);
                            }
                        }
                }
            }
            catch (Exception ex) { StingLog.Warn($"FaceCandidates {el.Id}: {ex.Message}"); }

            var outList = new List<FaceCand>();
            var toViewer = view.ViewDirection;
            foreach (var (r, f, tf) in found)
            {
                try
                {
                    var bb = f.GetBoundingBox();
                    var mid = (bb.Min + bb.Max) * 0.5;
                    var n = tf.OfVector(f.ComputeNormal(mid)).Normalize();
                    var c = new FaceCand
                    {
                        Ref = r, Point = tf.OfPoint(f.Evaluate(mid)), Normal = n,
                        Dot = n.DotProduct(toViewer), Area = f.Area,
                        Material = f.MaterialElementId ?? ElementId.InvalidElementId,
                    };
                    try
                    {
                        if (doc.IsPainted(el.Id, f))
                        {
                            c.Painted = true;
                            c.Material = doc.GetPaintedMaterial(el.Id, f);
                        }
                    }
                    catch (Exception ex) { StingLog.WarnRateLimited("FaceCandidates.Paint", $"{el.Id}: {ex.Message}"); }
                    outList.Add(c);
                }
                catch (Exception ex) { StingLog.WarnRateLimited("FaceCandidates.Face", $"{el.Id}: {ex.Message}"); }
            }
            return outList;
        }

        private static void AddFaces(Solid s, Transform tf, List<(Reference, Face, Transform)> into)
        {
            if (s == null || s.Faces.Size == 0) return;
            foreach (Face f in s.Faces)
                if (f.Reference != null) into.Add((f.Reference, f, tf));
        }

        /// <summary>
        /// The cut faces of <paramref name="el"/> in a section view, one per material (the
        /// largest), with a reference to tag. A cut face is planar and faces the viewer.
        /// </summary>
        private static List<FaceCand> CutFacesByMaterial(Element el, View view)
        {
            var byMat = new Dictionary<long, FaceCand>();
            try
            {
                var ge = el.get_Geometry(new Options { ComputeReferences = true, View = view });
                if (ge == null) return new List<FaceCand>();
                var solids = new List<(Solid S, Transform Tf)>();
                foreach (var go in ge)
                {
                    if (go is Solid s) solids.Add((s, Transform.Identity));
                    else if (go is GeometryInstance gi)
                        foreach (var ig in gi.GetInstanceGeometry()) if (ig is Solid si) solids.Add((si, Transform.Identity));
                }
                foreach (var (s, tf) in solids)
                {
                    if (s == null || s.Faces.Size == 0) continue;
                    foreach (Face f in s.Faces)
                    {
                        if (!(f is PlanarFace pf) || f.Reference == null) continue;
                        var n = tf.OfVector(pf.FaceNormal);
                        if (Math.Abs(n.DotProduct(view.ViewDirection)) < 0.99) continue;   // not a cut face
                        long mat = f.MaterialElementId?.Value ?? -1;
                        if (mat <= 0) continue;
                        if (byMat.TryGetValue(mat, out var had) && had.Area >= f.Area) continue;
                        var bb = f.GetBoundingBox();
                        var mid = (bb.Min + bb.Max) * 0.5;
                        byMat[mat] = new FaceCand
                        {
                            Ref = f.Reference, Point = tf.OfPoint(f.Evaluate(mid)), Normal = n,
                            Dot = 1, Area = f.Area, Material = f.MaterialElementId,
                        };
                    }
                }
            }
            catch (Exception ex) { StingLog.Warn($"CutFacesByMaterial {el.Id}: {ex.Message}"); }
            return byMat.Values.ToList();
        }

        /// <summary>Material callouts already on the view: their material and head position,
        /// plus the hosts they tag (so build-ups are not repeated).</summary>
        private static List<MaterialCalloutCandidate> ExistingMaterialCallouts(Document doc, View view,
            out HashSet<ElementId> taggedHosts)
        {
            var list = new List<MaterialCalloutCandidate>();
            taggedHosts = new HashSet<ElementId>();
            try
            {
                foreach (var t in new FilteredElementCollector(doc, view.Id).OfClass(typeof(IndependentTag))
                             .Cast<IndependentTag>()
                             .Where(t => t.Category?.Id.Value == (long)BuiltInCategory.OST_MaterialTags))
                {
                    XYZ head = null;
                    try { head = t.TagHeadPosition; } catch (Exception ex) { StingLog.WarnRateLimited("MatCallout.Head", ex.Message); }
                    foreach (var r in t.GetTaggedReferences())
                    {
                        taggedHosts.Add(r.ElementId);
                        var host = doc.GetElement(r.ElementId);
                        long mat = -1;
                        try
                        {
                            if (host?.GetGeometryObjectFromReference(r) is Face f)
                                mat = doc.IsPainted(host.Id, f) ? doc.GetPaintedMaterial(host.Id, f).Value : (f.MaterialElementId?.Value ?? -1);
                        }
                        catch (Exception ex) { StingLog.WarnRateLimited("MatCallout.Existing", ex.Message); }
                        if (head != null && mat > 0)
                            list.Add(new MaterialCalloutCandidate
                            {
                                Material = mat, Area = 0,
                                U = head.DotProduct(view.RightDirection), V = head.DotProduct(view.UpDirection),
                            });
                    }
                }
            }
            catch (Exception ex) { StingLog.Warn($"ExistingMaterialCallouts '{view.Name}': {ex.Message}"); }
            return list;
        }

        private static MaterialCalloutCandidate ToCandidate(View view, FaceCand f) => new MaterialCalloutCandidate
        {
            Material = f.Material?.Value ?? -1,
            U = f.Point.DotProduct(view.RightDirection),
            V = f.Point.DotProduct(view.UpDirection),
            Area = f.Area,
            Painted = f.Painted,
        };

        private static bool PlaceMaterialTag(Document doc, View view, ElementId symId, Reference faceRef,
            bool addLeader, XYZ head, Element host, string label, AnnotationRunStats stats)
        {
            try
            {
                var tag = IndependentTag.Create(doc, symId, view.Id, faceRef, addLeader, TagOrientation.Horizontal, head);
                return tag != null;
            }
            catch (Exception ex)
            {
                StingLog.WarnRateLimited("MatCallout.Create", $"{label} on {host?.Id}: {ex.Message}");
                return false;
            }
        }

        /// <summary>A material whose callout would print blank: no MAT_CODE and no Mark.</summary>
        private static void NoteUncoded(Document doc, ElementId materialId, ISet<string> into)
        {
            try
            {
                if (materialId == null || materialId == ElementId.InvalidElementId) return;
                if (!(doc.GetElement(materialId) is Material m)) return;
                string code = ParameterHelpers.GetString(m, "MAT_CODE");
                if (string.IsNullOrWhiteSpace(code)) code = m.get_Parameter(BuiltInParameter.ALL_MODEL_MARK)?.AsString();
                if (string.IsNullOrWhiteSpace(code)) into.Add(m.Name);
            }
            catch (Exception ex) { StingLog.WarnRateLimited("MatCallout.Code", ex.Message); }
        }
    }
}
