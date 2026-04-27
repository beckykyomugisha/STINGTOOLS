// ClashDetectionCommands.cs — Phase 106 rule-based clash, clearance and naming audit.
//
// All classes live in the StingTools.Clash namespace (separate from the existing
// StingTools.Core.Clash utility types and from the pre-existing clash-related
// IExternalCommand classes in StingTools.Temp). This file is designed to compile
// cleanly alongside both, without introducing duplicate type definitions.
//
// Written to be minimally viable but correct:
//   - broad-phase via Revit's BoundingBoxIntersectsFilter (task constraint)
//   - narrow-phase via axis-aligned bounding box overlap with a 25 mm tolerance
//   - results persisted as JSON next to the model in the project CLASHES folder
//   - every catch logs via StingTools.Core.StingLog.Warn — no bare catches
//
// The LiveClashUpdater class here is a deliberate stub. The task asks that it
// compile and expose Register/Unregister today, with the real IUpdater triggers
// deferred to a follow-on phase so that opening a model that doesn't use clash
// detection can't fall over on a missing UpdaterRegistry registration.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json;
using StingTools.Core;

namespace StingTools.Clash
{
    // ──────────────────────────────────────────────────────────────────────
    // Data classes
    // ──────────────────────────────────────────────────────────────────────

    /// <summary>Single clash record, serialised to the clashes_*.json report.</summary>
    public sealed class ClashResult
    {
        public string clash_id { get; set; }
        public long element_a_id { get; set; }
        public long element_b_id { get; set; }
        public string element_a_name { get; set; }
        public string element_b_name { get; set; }
        public string discipline_a { get; set; }
        public string discipline_b { get; set; }
        public double overlap_mm { get; set; }
        public double centroid_x { get; set; }
        public double centroid_y { get; set; }
        public double centroid_z { get; set; }
        public DateTime detected_at { get; set; }
        public string source { get; set; }      // "host" or "link"
        public string link_name { get; set; }   // populated for cross-model clashes
    }

    /// <summary>Results of one clash run, handed to subscribers of ClashEvents.</summary>
    public sealed class ClashSession
    {
        public List<ClashResult> Results { get; } = new List<ClashResult>();
        public DateTime RunAt { get; set; } = DateTime.UtcNow;
        public string JsonReportPath { get; set; }
    }

    /// <summary>
    /// Unordered pair of element ids — equality is based on the sorted (min, max) pair
    /// so (A,B) and (B,A) compare equal and hash to the same value.
    /// </summary>
    public readonly struct ClashIdentity : IEquatable<ClashIdentity>
    {
        public readonly long IdLow;
        public readonly long IdHigh;

        public ClashIdentity(ElementId a, ElementId b)
        {
            long av = a?.Value ?? 0L;
            long bv = b?.Value ?? 0L;
            if (av <= bv) { IdLow = av; IdHigh = bv; }
            else          { IdLow = bv; IdHigh = av; }
        }

        public bool Equals(ClashIdentity other) => IdLow == other.IdLow && IdHigh == other.IdHigh;
        public override bool Equals(object obj) => obj is ClashIdentity o && Equals(o);
        public override int GetHashCode() => HashCode.Combine(IdLow, IdHigh);
        public override string ToString() => $"({IdLow},{IdHigh})";
    }

    // ──────────────────────────────────────────────────────────────────────
    // Events
    // ──────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Lightweight event bus for clash-run completion. The BCC Clashes tab
    /// subscribes here to refresh itself after a run.
    /// </summary>
    public static class ClashEvents
    {
        public static event Action<ClashSession> ClashDetectionCompleted;

        internal static void RaiseCompleted(ClashSession session)
        {
            if (session == null) return;
            var handler = ClashDetectionCompleted;
            if (handler == null) return;
            try { handler.Invoke(session); }
            catch (Exception ex) { StingLog.Warn($"ClashEvents.ClashDetectionCompleted handler threw: {ex.Message}"); }
        }
    }

    // ──────────────────────────────────────────────────────────────────────
    // AABB broad-phase helper — uses Revit's built-in filter (task constraint).
    // ──────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Broad-phase sweep using BoundingBoxIntersectsFilter. Returns candidate
    /// pairs (A, B) where the expanded bounding box of A intersects the bounding
    /// box of any element in setB. We do not use RBush here — the task requires
    /// the Revit-native filter so clash logic stays inside the Revit API envelope.
    /// </summary>
    public static class AabbSweep
    {
        private const double MmPerFoot = 304.8;

        public static IEnumerable<(ElementId A, ElementId B)> BroadPhase(
            Document doc,
            IList<ElementId> setA,
            IList<ElementId> setB,
            double toleranceMm)
        {
            if (doc == null || setA == null || setB == null || setA.Count == 0 || setB.Count == 0)
                yield break;

            double tolFt = Math.Max(0.0, toleranceMm) / MmPerFoot;
            var bHashSet = new HashSet<long>(setB.Select(id => id?.Value ?? 0L));

            foreach (var idA in setA)
            {
                Element elA = null;
                try { elA = doc.GetElement(idA); }
                catch (Exception ex) { StingLog.Warn($"AabbSweep.GetElement(A={idA?.Value}): {ex.Message}"); continue; }
                if (elA == null) continue;

                BoundingBoxXYZ bb;
                try { bb = elA.get_BoundingBox(null); }
                catch (Exception ex) { StingLog.Warn($"AabbSweep.get_BoundingBox(A={idA?.Value}): {ex.Message}"); continue; }
                if (bb == null) continue;

                var min = new XYZ(bb.Min.X - tolFt, bb.Min.Y - tolFt, bb.Min.Z - tolFt);
                var max = new XYZ(bb.Max.X + tolFt, bb.Max.Y + tolFt, bb.Max.Z + tolFt);
                var outline = new Outline(min, max);
                var filter = new BoundingBoxIntersectsFilter(outline);

                FilteredElementCollector coll;
                try { coll = new FilteredElementCollector(doc, setB).WherePasses(filter); }
                catch (Exception ex) { StingLog.Warn($"AabbSweep.Collector(A={idA?.Value}): {ex.Message}"); continue; }

                foreach (var hit in coll)
                {
                    if (hit == null || hit.Id == null) continue;
                    if (hit.Id.Value == idA.Value) continue;           // self
                    if (!bHashSet.Contains(hit.Id.Value)) continue;    // defence in depth
                    yield return (idA, hit.Id);
                }
            }
        }
    }

    // ──────────────────────────────────────────────────────────────────────
    // LiveClashUpdater — stub IUpdater. Registered at app start so the type
    // is wired, but no AddTrigger call is made yet: that belongs to the live
    // detection phase and would otherwise fire on every edit in models that
    // never use clash detection.
    // ──────────────────────────────────────────────────────────────────────

    public sealed class LiveClashUpdater : IUpdater
    {
        // Deterministic AddInId + UpdaterId — distinct from the existing
        // StingTools.Core.Clash.LiveClashUpdater GUIDs so both can coexist
        // in the UpdaterRegistry if the legacy one is later enabled too.
        private static readonly UpdaterId _updaterId = new UpdaterId(
            new AddInId(new Guid("A1B2C3D4-5678-9ABC-DEF0-123456789ABC")),
            new Guid("BDA5F1C2-4E71-4F7E-8C3A-9D2E1F0A8B62"));

        private static bool _registered;

        public UpdaterId GetUpdaterId() => _updaterId;
        public string GetUpdaterName() => "STING Live Clash Updater (Phase 106 stub)";
        public string GetAdditionalInformation() =>
            "Stub placeholder for live clash re-evaluation. Triggers are not wired yet.";
        public ChangePriority GetChangePriority() => ChangePriority.MEPAccessoriesFittingsSegmentsWires;

        public void Execute(UpdaterData data)
        {
            // Intentional no-op. Live detection will plug in here in a
            // follow-on phase — this class exists today only so the
            // OnStartup wire-up call compiles and registration succeeds.
        }

        /// <summary>
        /// Called from StingToolsApp.OnStartup. Stores the updater id so a
        /// future phase can call UpdaterRegistry.AddTrigger without a second
        /// Register round-trip. No UpdaterRegistry.RegisterUpdater is issued
        /// here — that's deferred until triggers are actually needed, so
        /// models that don't use clash detection pay zero cost.
        /// </summary>
        public static void Register(UIControlledApplication uiApp)
        {
            if (_registered) return;
            if (uiApp == null) { StingLog.Warn("LiveClashUpdater.Register called with null UIControlledApplication"); return; }
            try
            {
                _registered = true;
                StingLog.Info("LiveClashUpdater.Register (Phase 106 stub): updater id reserved; triggers deferred.");
            }
            catch (Exception ex)
            {
                StingLog.Warn($"LiveClashUpdater.Register failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Symmetric teardown. Safe to call more than once and with a null Document.
        /// No-op today because Register does not add triggers or register with
        /// UpdaterRegistry.
        /// </summary>
        public static void Unregister(Document doc)
        {
            if (!_registered) return;
            try
            {
                _registered = false;
                StingLog.Info("LiveClashUpdater.Unregister (Phase 106 stub): no triggers to remove.");
            }
            catch (Exception ex)
            {
                StingLog.Warn($"LiveClashUpdater.Unregister failed: {ex.Message}");
            }
        }
    }

    // ──────────────────────────────────────────────────────────────────────
    // Shared category helpers
    // ──────────────────────────────────────────────────────────────────────

    internal static class ClashCategoryHelpers
    {
        internal const double MmPerFoot = 304.8;
        internal const double BroadPhaseToleranceMm = 25.0;

        internal static readonly BuiltInCategory[] MepCategories = new[]
        {
            BuiltInCategory.OST_PipeCurves,
            BuiltInCategory.OST_PipeFitting,
            BuiltInCategory.OST_PipeAccessory,
            BuiltInCategory.OST_FlexPipeCurves,
            BuiltInCategory.OST_DuctCurves,
            BuiltInCategory.OST_DuctFitting,
            BuiltInCategory.OST_DuctAccessory,
            BuiltInCategory.OST_FlexDuctCurves,
            BuiltInCategory.OST_CableTray,
            BuiltInCategory.OST_CableTrayFitting,
            BuiltInCategory.OST_Conduit,
            BuiltInCategory.OST_ConduitFitting,
            BuiltInCategory.OST_MechanicalEquipment,
            BuiltInCategory.OST_ElectricalEquipment,
            BuiltInCategory.OST_PlumbingFixtures,
        };

        internal static readonly BuiltInCategory[] StructuralCategories = new[]
        {
            BuiltInCategory.OST_StructuralColumns,
            BuiltInCategory.OST_StructuralFraming,
            BuiltInCategory.OST_Floors,
            BuiltInCategory.OST_Walls,
            BuiltInCategory.OST_StructuralFoundation,
        };

        internal static List<Element> CollectMepElements(Document doc)
        {
            var filter = new ElementMulticategoryFilter(MepCategories);
            try
            {
                return new FilteredElementCollector(doc)
                    .WherePasses(filter)
                    .WhereElementIsNotElementType()
                    .ToList();
            }
            catch (Exception ex) { StingLog.Warn($"CollectMepElements: {ex.Message}"); return new List<Element>(); }
        }

        internal static List<Element> CollectStructuralElements(Document doc)
        {
            try
            {
                var results = new List<Element>();
                foreach (var cat in StructuralCategories)
                {
                    var coll = new FilteredElementCollector(doc)
                        .OfCategory(cat)
                        .WhereElementIsNotElementType();
                    if (cat == BuiltInCategory.OST_Walls)
                    {
                        foreach (var e in coll)
                        {
                            // Only walls tagged as structural — avoids treating architectural
                            // partitions as clash obstacles.
                            var p = e.get_Parameter(BuiltInParameter.WALL_STRUCTURAL_SIGNIFICANT);
                            if (p != null && p.AsInteger() == 1) results.Add(e);
                        }
                    }
                    else
                    {
                        results.AddRange(coll);
                    }
                }
                return results;
            }
            catch (Exception ex) { StingLog.Warn($"CollectStructuralElements: {ex.Message}"); return new List<Element>(); }
        }

        internal static string DisciplineFor(Element el)
        {
            if (el == null) return "";
            var cat = el.Category;
            if (cat == null) return "";
            var bic = (BuiltInCategory)cat.Id.Value;
            switch (bic)
            {
                case BuiltInCategory.OST_PipeCurves:
                case BuiltInCategory.OST_PipeFitting:
                case BuiltInCategory.OST_PipeAccessory:
                case BuiltInCategory.OST_FlexPipeCurves:
                case BuiltInCategory.OST_PlumbingFixtures:
                    return "P";
                case BuiltInCategory.OST_DuctCurves:
                case BuiltInCategory.OST_DuctFitting:
                case BuiltInCategory.OST_DuctAccessory:
                case BuiltInCategory.OST_FlexDuctCurves:
                case BuiltInCategory.OST_MechanicalEquipment:
                    return "M";
                case BuiltInCategory.OST_CableTray:
                case BuiltInCategory.OST_CableTrayFitting:
                case BuiltInCategory.OST_Conduit:
                case BuiltInCategory.OST_ConduitFitting:
                case BuiltInCategory.OST_ElectricalEquipment:
                    return "E";
                case BuiltInCategory.OST_StructuralColumns:
                case BuiltInCategory.OST_StructuralFraming:
                case BuiltInCategory.OST_StructuralFoundation:
                case BuiltInCategory.OST_Floors:
                case BuiltInCategory.OST_Walls:
                    return "S";
                default:
                    return "";
            }
        }
    }

    // ──────────────────────────────────────────────────────────────────────
    // Shared AABB narrow-phase — returns overlap depth (mm) when two AABBs
    // actually interpenetrate, else 0. Called after AabbSweep broad-phase.
    // Accepts an optional transform for linked-model boxes.
    // ──────────────────────────────────────────────────────────────────────

    /// <summary>
    /// B1: Local helper exposing the same 8-corner-transform world AABB build
    /// that AabbNarrowPhase uses internally, but as a public-internal entry
    /// point so CrossModelClashCommand can pre-compute world AABBs for its
    /// linked-element cache without paying the transform per pair.
    /// </summary>
    internal static class TransformedAabb
    {
        internal static (XYZ Min, XYZ Max) Build(BoundingBoxXYZ bb, Transform t)
        {
            if (bb == null) return (XYZ.Zero, XYZ.Zero);
            if (t == null || t.IsIdentity) return (bb.Min, bb.Max);
            var corners = new XYZ[8]
            {
                new XYZ(bb.Min.X, bb.Min.Y, bb.Min.Z),
                new XYZ(bb.Max.X, bb.Min.Y, bb.Min.Z),
                new XYZ(bb.Min.X, bb.Max.Y, bb.Min.Z),
                new XYZ(bb.Max.X, bb.Max.Y, bb.Min.Z),
                new XYZ(bb.Min.X, bb.Min.Y, bb.Max.Z),
                new XYZ(bb.Max.X, bb.Min.Y, bb.Max.Z),
                new XYZ(bb.Min.X, bb.Max.Y, bb.Max.Z),
                new XYZ(bb.Max.X, bb.Max.Y, bb.Max.Z),
            };
            double mnX = double.PositiveInfinity, mnY = double.PositiveInfinity, mnZ = double.PositiveInfinity;
            double mxX = double.NegativeInfinity, mxY = double.NegativeInfinity, mxZ = double.NegativeInfinity;
            for (int i = 0; i < 8; i++)
            {
                var w = t.OfPoint(corners[i]);
                if (w.X < mnX) mnX = w.X;
                if (w.Y < mnY) mnY = w.Y;
                if (w.Z < mnZ) mnZ = w.Z;
                if (w.X > mxX) mxX = w.X;
                if (w.Y > mxY) mxY = w.Y;
                if (w.Z > mxZ) mxZ = w.Z;
            }
            return (new XYZ(mnX, mnY, mnZ), new XYZ(mxX, mxY, mxZ));
        }
    }

    internal static class AabbNarrowPhase
    {
        internal static (bool Intersects, double OverlapMm, XYZ CentroidFt) Check(
            BoundingBoxXYZ a, Transform tA,
            BoundingBoxXYZ b, Transform tB)
        {
            if (a == null || b == null) return (false, 0.0, XYZ.Zero);

            var (aMin, aMax) = WorldAabb(a, tA);
            var (bMin, bMax) = WorldAabb(b, tB);

            double ox = Math.Min(aMax.X, bMax.X) - Math.Max(aMin.X, bMin.X);
            double oy = Math.Min(aMax.Y, bMax.Y) - Math.Max(aMin.Y, bMin.Y);
            double oz = Math.Min(aMax.Z, bMax.Z) - Math.Max(aMin.Z, bMin.Z);

            if (ox <= 0 || oy <= 0 || oz <= 0) return (false, 0.0, XYZ.Zero);

            double overlapFt = Math.Min(ox, Math.Min(oy, oz));
            var centroid = new XYZ(
                (Math.Max(aMin.X, bMin.X) + Math.Min(aMax.X, bMax.X)) * 0.5,
                (Math.Max(aMin.Y, bMin.Y) + Math.Min(aMax.Y, bMax.Y)) * 0.5,
                (Math.Max(aMin.Z, bMin.Z) + Math.Min(aMax.Z, bMax.Z)) * 0.5);

            return (true, overlapFt * ClashCategoryHelpers.MmPerFoot, centroid);
        }

        private static (XYZ Min, XYZ Max) WorldAabb(BoundingBoxXYZ bb, Transform t)
        {
            if (t == null || t.IsIdentity) return (bb.Min, bb.Max);

            // Transform the 8 corners of the local AABB into world space and
            // rebuild a world-aligned AABB. This is slightly conservative for
            // rotated links but is correct and fast.
            var corners = new XYZ[8]
            {
                new XYZ(bb.Min.X, bb.Min.Y, bb.Min.Z),
                new XYZ(bb.Max.X, bb.Min.Y, bb.Min.Z),
                new XYZ(bb.Min.X, bb.Max.Y, bb.Min.Z),
                new XYZ(bb.Max.X, bb.Max.Y, bb.Min.Z),
                new XYZ(bb.Min.X, bb.Min.Y, bb.Max.Z),
                new XYZ(bb.Max.X, bb.Min.Y, bb.Max.Z),
                new XYZ(bb.Min.X, bb.Max.Y, bb.Max.Z),
                new XYZ(bb.Max.X, bb.Max.Y, bb.Max.Z),
            };

            double mnX = double.PositiveInfinity, mnY = double.PositiveInfinity, mnZ = double.PositiveInfinity;
            double mxX = double.NegativeInfinity, mxY = double.NegativeInfinity, mxZ = double.NegativeInfinity;
            for (int i = 0; i < 8; i++)
            {
                var w = t.OfPoint(corners[i]);
                if (w.X < mnX) mnX = w.X;
                if (w.Y < mnY) mnY = w.Y;
                if (w.Z < mnZ) mnZ = w.Z;
                if (w.X > mxX) mxX = w.X;
                if (w.Y > mxY) mxY = w.Y;
                if (w.Z > mxZ) mxZ = w.Z;
            }
            return (new XYZ(mnX, mnY, mnZ), new XYZ(mxX, mxY, mxZ));
        }
    }

    // ──────────────────────────────────────────────────────────────────────
    // ClashDetectionCommand — single-model MEP vs structure clash
    // ──────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Rule-based MEP-vs-structure clash detection on the active model only.
    /// Writes results to {project}/12_CLASHES/clash_{timestamp}.json and raises
    /// ClashEvents.ClashDetectionCompleted so subscribers (BCC Clashes tab) can refresh.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public sealed class ClashDetectionCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                if (commandData?.Application?.ActiveUIDocument == null)
                {
                    TaskDialog.Show("Clash Detection", "No active document.");
                    return Result.Failed;
                }
                var uidoc = commandData.Application.ActiveUIDocument;
                var doc = uidoc.Document;
                if (doc == null || doc.IsFamilyDocument)
                {
                    TaskDialog.Show("Clash Detection", "Clash detection requires a project document.");
                    return Result.Failed;
                }

                var mepElements = ClashCategoryHelpers.CollectMepElements(doc);
                var structuralElements = ClashCategoryHelpers.CollectStructuralElements(doc);

                var mepIds = mepElements.Where(e => e?.Id != null).Select(e => e.Id).ToList();
                var structIds = structuralElements.Where(e => e?.Id != null).Select(e => e.Id).ToList();

                var session = new ClashSession { RunAt = DateTime.UtcNow };
                var seen = new HashSet<ClashIdentity>();

                foreach (var (idA, idB) in AabbSweep.BroadPhase(doc, mepIds, structIds, ClashCategoryHelpers.BroadPhaseToleranceMm))
                {
                    var identity = new ClashIdentity(idA, idB);
                    if (!seen.Add(identity)) continue;

                    Element a = null, b = null;
                    try { a = doc.GetElement(idA); } catch (Exception ex) { StingLog.Warn($"ClashDetection.GetElement(A): {ex.Message}"); }
                    try { b = doc.GetElement(idB); } catch (Exception ex) { StingLog.Warn($"ClashDetection.GetElement(B): {ex.Message}"); }
                    if (a == null || b == null) continue;

                    BoundingBoxXYZ bbA = null, bbB = null;
                    try { bbA = a.get_BoundingBox(null); } catch (Exception ex) { StingLog.Warn($"ClashDetection.BBox(A): {ex.Message}"); }
                    try { bbB = b.get_BoundingBox(null); } catch (Exception ex) { StingLog.Warn($"ClashDetection.BBox(B): {ex.Message}"); }
                    if (bbA == null || bbB == null) continue;

                    var narrow = AabbNarrowPhase.Check(bbA, null, bbB, null);
                    if (!narrow.Intersects) continue;

                    session.Results.Add(new ClashResult
                    {
                        clash_id = identity.ToString(),
                        element_a_id = idA.Value,
                        element_b_id = idB.Value,
                        element_a_name = SafeName(a),
                        element_b_name = SafeName(b),
                        discipline_a = ClashCategoryHelpers.DisciplineFor(a),
                        discipline_b = ClashCategoryHelpers.DisciplineFor(b),
                        overlap_mm = Math.Round(narrow.OverlapMm, 2),
                        centroid_x = narrow.CentroidFt.X * ClashCategoryHelpers.MmPerFoot,
                        centroid_y = narrow.CentroidFt.Y * ClashCategoryHelpers.MmPerFoot,
                        centroid_z = narrow.CentroidFt.Z * ClashCategoryHelpers.MmPerFoot,
                        detected_at = DateTime.UtcNow,
                        source = "host",
                        link_name = "",
                    });
                }

                string reportPath = WriteClashReport(doc, session, "clash");
                session.JsonReportPath = reportPath;

                TaskDialog.Show("Clash Detection",
                    $"Scanned {mepElements.Count} MEP vs {structuralElements.Count} structural elements.\n" +
                    $"Clashes: {session.Results.Count}\n\nReport: {(string.IsNullOrEmpty(reportPath) ? "(not written)" : reportPath)}");

                ClashEvents.RaiseCompleted(session);
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Warn($"ClashDetectionCommand failed: {ex.Message}");
                message = ex.Message;
                return Result.Failed;
            }
        }

        internal static string SafeName(Element el)
        {
            if (el == null) return "";
            try
            {
                string name = el.Name ?? "";
                string cat = el.Category?.Name ?? "";
                return string.IsNullOrEmpty(cat) ? name : $"{cat}: {name}";
            }
            catch (Exception ex) { StingLog.Warn($"SafeName: {ex.Message}"); return ""; }
        }

        internal static string WriteClashReport(Document doc, ClashSession session, string prefix)
        {
            try
            {
                string folder = ProjectFolderEngine.GetFolderPath(doc, "CLASHES");
                if (string.IsNullOrEmpty(folder)) return "";
                if (!Directory.Exists(folder))
                {
                    try { Directory.CreateDirectory(folder); }
                    catch (Exception ex) { StingLog.Warn($"WriteClashReport.CreateDirectory: {ex.Message}"); return ""; }
                }
                string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                string path = Path.Combine(folder, $"{prefix}_{stamp}.json");
                string json = JsonConvert.SerializeObject(new { session.RunAt, session.Results }, Formatting.Indented);
                File.WriteAllText(path, json);
                return path;
            }
            catch (Exception ex)
            {
                StingLog.Warn($"WriteClashReport failed: {ex.Message}");
                return "";
            }
        }
    }

    // ──────────────────────────────────────────────────────────────────────
    // CrossModelClashCommand — host MEP vs linked structural elements
    // ──────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Same broad-/narrow-phase logic as ClashDetectionCommand, but additionally
    /// iterates every RevitLinkInstance and checks the host MEP elements against
    /// linked elements using the link's total transform.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public sealed class CrossModelClashCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                if (commandData?.Application?.ActiveUIDocument == null)
                {
                    TaskDialog.Show("Cross-Model Clash", "No active document.");
                    return Result.Failed;
                }
                var doc = commandData.Application.ActiveUIDocument.Document;
                if (doc == null || doc.IsFamilyDocument)
                {
                    TaskDialog.Show("Cross-Model Clash", "Cross-model clash requires a project document with links.");
                    return Result.Failed;
                }

                var mepElements = ClashCategoryHelpers.CollectMepElements(doc);
                if (mepElements.Count == 0)
                {
                    TaskDialog.Show("Cross-Model Clash", "No MEP elements found in the host model.");
                    return Result.Cancelled;
                }

                var links = new FilteredElementCollector(doc)
                    .OfClass(typeof(RevitLinkInstance))
                    .Cast<RevitLinkInstance>()
                    .Where(li => li != null)
                    .ToList();

                if (links.Count == 0)
                {
                    TaskDialog.Show("Cross-Model Clash", "No linked Revit models present.");
                    return Result.Cancelled;
                }

                var session = new ClashSession { RunAt = DateTime.UtcNow };
                var seen = new HashSet<ClashIdentity>();
                int linkedElementsTotal = 0;

                // B1: Pre-cache host-MEP bboxes once outside the link loop.
                //     Prior code called get_BoundingBox on each MEP element
                //     once per link and again per linked-structural element,
                //     hammering the Revit API on a 2000×1000×3-link model.
                var mepCache = new List<(Element El, BoundingBoxXYZ Bb)>(mepElements.Count);
                foreach (var mepEl in mepElements)
                {
                    BoundingBoxXYZ mepBb = null;
                    try { mepBb = mepEl.get_BoundingBox(null); }
                    catch (Exception ex) { StingLog.Warn($"CrossModelClash.MEP.BBox: {ex.Message}"); }
                    if (mepBb == null) continue;
                    mepCache.Add((mepEl, mepBb));
                }

                var loopWatch = System.Diagnostics.Stopwatch.StartNew();
                int subjectIterations = 0;

                foreach (var link in links)
                {
                    Document linkDoc = null;
                    Transform t = Transform.Identity;
                    string linkName = "";
                    try
                    {
                        linkDoc = link.GetLinkDocument();
                        t = link.GetTotalTransform() ?? Transform.Identity;
                        linkName = link.Name ?? "";
                    }
                    catch (Exception ex) { StingLog.Warn($"CrossModelClash.link({link?.Id?.Value}): {ex.Message}"); }
                    if (linkDoc == null) continue;

                    var linkStructurals = ClashCategoryHelpers.CollectStructuralElements(linkDoc);
                    if (linkStructurals.Count == 0) continue;
                    linkedElementsTotal += linkStructurals.Count;

                    // B1: Build a small in-memory cache of (linkElement, worldAabb)
                    //     for the link, so the inner loop is a pure AABB compare
                    //     against pre-computed world-space boxes. AABB depth tolerance
                    //     is 25 mm (matches the broad-phase tol) so we don't double-
                    //     transform corners 8 times per inner-loop iteration.
                    var linkIds = new List<ElementId>(linkStructurals.Count);
                    var linkBoxes = new Dictionary<long, (Element El, BoundingBoxXYZ LocalBb, XYZ WorldMin, XYZ WorldMax)>(linkStructurals.Count);
                    foreach (var linkEl in linkStructurals)
                    {
                        if (linkEl?.Id == null) continue;
                        BoundingBoxXYZ linkBb = null;
                        try { linkBb = linkEl.get_BoundingBox(null); }
                        catch (Exception ex) { StingLog.Warn($"CrossModelClash.Link.BBox: {ex.Message}"); }
                        if (linkBb == null) continue;
                        // World AABB for broad-phase narrowing per host MEP.
                        var (wMin, wMax) = TransformedAabb.Build(linkBb, t);
                        linkIds.Add(linkEl.Id);
                        linkBoxes[linkEl.Id.Value] = (linkEl, linkBb, wMin, wMax);
                    }
                    if (linkBoxes.Count == 0) continue;

                    // B1: BoundingBoxIntersectsFilter against the link doc to narrow
                    //     candidates per host MEP. The filter operates on the link
                    //     document's local coordinates, so transform the host MEP
                    //     world AABB through the inverse link transform first.
                    Transform inv = null;
                    try { inv = t.Inverse; } catch (Exception ex) { StingLog.Warn($"CrossModelClash.InvTransform({linkName}): {ex.Message}"); }

                    double tolFt = ClashCategoryHelpers.BroadPhaseToleranceMm / ClashCategoryHelpers.MmPerFoot;

                    foreach (var (mepEl, mepBb) in mepCache)
                    {
                        subjectIterations++;
                        // Convert host (world) AABB into the link's local frame.
                        var hostMin = mepBb.Min;
                        var hostMax = mepBb.Max;
                        XYZ probeMin, probeMax;
                        if (inv != null)
                        {
                            var (lMin, lMax) = TransformedAabb.Build(mepBb, inv);
                            probeMin = lMin; probeMax = lMax;
                        }
                        else { probeMin = hostMin; probeMax = hostMax; }

                        // Narrowed candidate list via Revit filter.
                        IList<Element> candidates;
                        try
                        {
                            var outline = new Outline(
                                new XYZ(probeMin.X - tolFt, probeMin.Y - tolFt, probeMin.Z - tolFt),
                                new XYZ(probeMax.X + tolFt, probeMax.Y + tolFt, probeMax.Z + tolFt));
                            var filter = new BoundingBoxIntersectsFilter(outline);
                            candidates = new FilteredElementCollector(linkDoc, linkIds)
                                .WherePasses(filter)
                                .ToElements();
                        }
                        catch (Exception ex)
                        {
                            StingLog.Warn($"CrossModelClash.LinkFilter({linkName}): {ex.Message}");
                            continue;
                        }

                        foreach (var linkEl in candidates)
                        {
                            if (linkEl?.Id == null) continue;
                            if (!linkBoxes.TryGetValue(linkEl.Id.Value, out var entry)) continue;

                            // World-space AABB sweep — short-circuit before paying
                            // the 8-corner narrow-phase transform.
                            if (entry.WorldMax.X < hostMin.X - tolFt || entry.WorldMin.X > hostMax.X + tolFt) continue;
                            if (entry.WorldMax.Y < hostMin.Y - tolFt || entry.WorldMin.Y > hostMax.Y + tolFt) continue;
                            if (entry.WorldMax.Z < hostMin.Z - tolFt || entry.WorldMin.Z > hostMax.Z + tolFt) continue;

                            var identity = new ClashIdentity(mepEl.Id, linkEl.Id);
                            if (!seen.Add(identity)) continue;

                            var narrow = AabbNarrowPhase.Check(mepBb, null, entry.LocalBb, t);
                            if (!narrow.Intersects) continue;

                            session.Results.Add(new ClashResult
                            {
                                clash_id = identity.ToString(),
                                element_a_id = mepEl.Id.Value,
                                element_b_id = linkEl.Id.Value,
                                element_a_name = ClashDetectionCommand.SafeName(mepEl),
                                element_b_name = ClashDetectionCommand.SafeName(linkEl),
                                discipline_a = ClashCategoryHelpers.DisciplineFor(mepEl),
                                discipline_b = ClashCategoryHelpers.DisciplineFor(linkEl),
                                overlap_mm = Math.Round(narrow.OverlapMm, 2),
                                centroid_x = narrow.CentroidFt.X * ClashCategoryHelpers.MmPerFoot,
                                centroid_y = narrow.CentroidFt.Y * ClashCategoryHelpers.MmPerFoot,
                                centroid_z = narrow.CentroidFt.Z * ClashCategoryHelpers.MmPerFoot,
                                detected_at = DateTime.UtcNow,
                                source = "link",
                                link_name = linkName,
                            });
                        }
                    }
                }
                if (subjectIterations > 500)
                    StingLog.Info($"CrossModelClashCommand: {subjectIterations} host-MEP × link iterations, " +
                        $"{session.Results.Count} clashes, {loopWatch.ElapsedMilliseconds} ms");

                string reportPath = ClashDetectionCommand.WriteClashReport(doc, session, "crossclash");
                session.JsonReportPath = reportPath;

                TaskDialog.Show("Cross-Model Clash",
                    $"Host MEP: {mepElements.Count}\n" +
                    $"Links scanned: {links.Count}\n" +
                    $"Linked structural elements: {linkedElementsTotal}\n" +
                    $"Clashes: {session.Results.Count}\n\n" +
                    $"Report: {(string.IsNullOrEmpty(reportPath) ? "(not written)" : reportPath)}");

                ClashEvents.RaiseCompleted(session);
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Warn($"CrossModelClashCommand failed: {ex.Message}");
                message = ex.Message;
                return Result.Failed;
            }
        }
    }

    // ──────────────────────────────────────────────────────────────────────
    // MEPClearanceValidationCommand — CIBSE Guide W / BS EN 12237 clearance audit
    // ──────────────────────────────────────────────────────────────────────

    /// <summary>
    /// For each Duct and Pipe, report the minimum clear distance to the nearest
    /// non-connected solid element. Duct target: ≥ 200 mm. Pipe target: ≥ 150 mm.
    /// Writes a CSV report to {project}/12_CLASHES/mep_clearance_{timestamp}.csv.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public sealed class MEPClearanceValidationCommand : IExternalCommand
    {
        private const double DuctMinClearMm = 200.0;
        private const double PipeMinClearMm = 150.0;
        private const double SearchRadiusMm = 600.0;   // enough to resolve a fail up to 3x target

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                if (commandData?.Application?.ActiveUIDocument == null)
                {
                    TaskDialog.Show("MEP Clearance", "No active document.");
                    return Result.Failed;
                }
                var doc = commandData.Application.ActiveUIDocument.Document;
                if (doc == null || doc.IsFamilyDocument)
                {
                    TaskDialog.Show("MEP Clearance", "Clearance validation requires a project document.");
                    return Result.Failed;
                }

                // Collect ducts and pipes
                var ducts = new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_DuctCurves)
                    .WhereElementIsNotElementType()
                    .ToList();
                var pipes = new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_PipeCurves)
                    .WhereElementIsNotElementType()
                    .ToList();

                // B2: Pre-collect candidate neighbours into an in-memory list
                //     of (id, bbox, category) once. Prior code instantiated a
                //     FilteredElementCollector per subject element — for 1000
                //     elements that is 1000 collectors. Now: one Revit pass to
                //     build the cache, then a hash-grid query per subject.
                var candidates = new List<(ElementId Id, BoundingBoxXYZ Bb, string CategoryName, BuiltInCategory Bic)>();
                void CollectInto(Element el)
                {
                    if (el?.Id == null) return;
                    BoundingBoxXYZ bb = null;
                    try { bb = el.get_BoundingBox(null); }
                    catch (Exception ex) { StingLog.Warn($"Clearance.PreCache.BBox({el.Id.Value}): {ex.Message}"); }
                    if (bb == null) return;
                    string catName = "";
                    BuiltInCategory bic = BuiltInCategory.INVALID;
                    try
                    {
                        catName = el.Category?.Name ?? "";
                        if (el.Category?.Id != null) bic = (BuiltInCategory)el.Category.Id.Value;
                    }
                    catch (Exception ex) { StingLog.Warn($"Clearance.PreCache.Cat({el.Id.Value}): {ex.Message}"); }
                    candidates.Add((el.Id, bb, catName, bic));
                }
                foreach (var d in ducts) CollectInto(d);
                foreach (var p in pipes) CollectInto(p);
                foreach (var s in ClashCategoryHelpers.CollectStructuralElements(doc)) CollectInto(s);

                // C3: Load the project matrix once so per-pair clearance distances
                //     come from default_clash_matrix.json rather than the hardcoded
                //     constants. Falls back to DuctMinClearMm / PipeMinClearMm when
                //     no cell matches the subject ↔ neighbour categories.
                var matrix = LoadMatrixForClearance();

                var rows = new List<string>();
                rows.Add("element_id,category,level,min_clearance_mm,target_mm,status");

                int passCount = 0, failCount = 0;
                int processed = 0;
                var loopWatch = System.Diagnostics.Stopwatch.StartNew();
                // D7: Cache connected-element ids per subject so the same
                //     Connector.AllRefs walk doesn't re-run for every audit
                //     row. Connector graph is stable for the duration of the
                //     audit pass.
                var connectedCache = new Dictionary<long, HashSet<long>>(ducts.Count + pipes.Count);
                foreach (var d in ducts)
                {
                    if (AuditOne(doc, d, DuctMinClearMm, candidates, matrix, rows, connectedCache)) passCount++; else failCount++;
                    processed++;
                }
                foreach (var p in pipes)
                {
                    if (AuditOne(doc, p, PipeMinClearMm, candidates, matrix, rows, connectedCache)) passCount++; else failCount++;
                    processed++;
                }
                if (processed > 500)
                    StingLog.Info($"MEPClearanceValidationCommand: {processed} elements audited in " +
                        $"{loopWatch.ElapsedMilliseconds} ms (PASS={passCount} FAIL={failCount})");

                string csvPath = WriteCsv(doc, rows);

                TaskDialog.Show("MEP Clearance",
                    $"Ducts: {ducts.Count}\nPipes: {pipes.Count}\n" +
                    $"PASS: {passCount}\nFAIL: {failCount}\n\n" +
                    $"Report: {(string.IsNullOrEmpty(csvPath) ? "(not written)" : csvPath)}");

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Warn($"MEPClearanceValidationCommand failed: {ex.Message}");
                message = ex.Message;
                return Result.Failed;
            }
        }

        /// <summary>
        /// C3: Best-effort matrix loader scoped to this command. Looks first in
        /// the plugin's data\clash directory, then in any sibling fallback. We
        /// intentionally don't reach across into ClashRunCommand's private
        /// FindDataFile — keeping the dependency local. Returns null when the
        /// JSON is missing or unparseable; callers fall back to constants.
        /// </summary>
        private static StingTools.Core.Clash.ClashMatrix LoadMatrixForClearance()
        {
            try
            {
                string dll = System.Reflection.Assembly.GetExecutingAssembly().Location;
                string dllDir = Path.GetDirectoryName(dll) ?? "";
                string[] candidates =
                {
                    Path.Combine(dllDir, "data", "clash", "default_clash_matrix.json"),
                    Path.Combine(dllDir, "data", "default_clash_matrix.json"),
                    Path.Combine(dllDir, "default_clash_matrix.json"),
                };
                foreach (var c in candidates)
                {
                    if (!File.Exists(c)) continue;
                    return StingTools.Core.Clash.ClashMatrix.LoadOrDefault(c);
                }
            }
            catch (Exception ex) { StingLog.Warn($"MEPClearance.LoadMatrix: {ex.Message}"); }
            return StingTools.Core.Clash.ClashMatrix.Default();
        }

        /// <summary>
        /// C3: Resolve target clearance (mm) from the matrix for the given
        /// subject ↔ neighbour category pair. CLEARANCE_xx → xx mm; HARD → 0
        /// (treat as a hard-clash gate so any contact is a fail). Returns
        /// the supplied fallback when no cell matches.
        /// </summary>
        private static double ResolveTargetMm(StingTools.Core.Clash.ClashMatrix matrix,
            string subjectCat, string neighbourCat, double fallbackMm)
        {
            if (matrix == null || string.IsNullOrEmpty(subjectCat) || string.IsNullOrEmpty(neighbourCat))
                return fallbackMm;
            try
            {
                var fa = new StingTools.Core.Clash.ElementFacts { Category = subjectCat };
                var fb = new StingTools.Core.Clash.ElementFacts { Category = neighbourCat };
                var cell = matrix.Match(fa, fb);
                if (cell == null) return fallbackMm;
                if (string.IsNullOrEmpty(cell.Tolerance)) return fallbackMm;
                if (cell.Tolerance.StartsWith("CLEARANCE_", StringComparison.OrdinalIgnoreCase))
                {
                    string suffix = cell.Tolerance.Substring("CLEARANCE_".Length);
                    if (double.TryParse(suffix, System.Globalization.NumberStyles.Number,
                        System.Globalization.CultureInfo.InvariantCulture, out double mm)) return mm;
                }
                if (string.Equals(cell.Tolerance, "HARD", StringComparison.OrdinalIgnoreCase)) return 0.0;
            }
            catch (Exception ex) { StingLog.Warn($"ResolveTargetMm({subjectCat},{neighbourCat}): {ex.Message}"); }
            return fallbackMm;
        }

        private static bool AuditOne(Document doc, Element subject, double targetMm,
            List<(ElementId Id, BoundingBoxXYZ Bb, string CategoryName, BuiltInCategory Bic)> candidates,
            StingTools.Core.Clash.ClashMatrix matrix, List<string> rows,
            Dictionary<long, HashSet<long>> connectedCache)
        {
            if (subject == null) return true;
            BoundingBoxXYZ sBb = null;
            try { sBb = subject.get_BoundingBox(null); } catch (Exception ex) { StingLog.Warn($"Clearance.BBox: {ex.Message}"); }
            if (sBb == null)
            {
                rows.Add($"{subject.Id.Value},{Csv(subject.Category?.Name)},,,{targetMm},SKIP");
                return true;
            }

            double tolFt = SearchRadiusMm / ClashCategoryHelpers.MmPerFoot;
            // B2: In-memory AABB range query against the pre-built candidate
            //     list. No new FilteredElementCollector instantiated here.
            // D7: Reuse cached connector-walk result when present.
            HashSet<long> connectedIds;
            if (connectedCache != null && connectedCache.TryGetValue(subject.Id.Value, out var cachedConn))
                connectedIds = cachedConn;
            else
            {
                connectedIds = GetConnectedElementIds(subject);
                if (connectedCache != null) connectedCache[subject.Id.Value] = connectedIds;
            }
            double subjMinX = sBb.Min.X - tolFt, subjMaxX = sBb.Max.X + tolFt;
            double subjMinY = sBb.Min.Y - tolFt, subjMaxY = sBb.Max.Y + tolFt;
            double subjMinZ = sBb.Min.Z - tolFt, subjMaxZ = sBb.Max.Z + tolFt;
            string subjectCat = subject.Category?.Name ?? "";
            long subjectIdLong = subject.Id.Value;

            // C3: Per-neighbour target so a duct-vs-wall hit honours its matrix
            //     tolerance and a duct-vs-duct hit honours another. The
            //     subject-level fallback (DuctMin/PipeMin) is used when no cell
            //     matches; we additionally compute a worst-case "min target"
            //     to drive the PASS/FAIL gate at the row level.
            double minClearMm = double.PositiveInfinity;
            double rowTargetMm = targetMm;
            string failingNeighbourCat = null;

            foreach (var cand in candidates)
            {
                if (cand.Id == null) continue;
                if (cand.Id.Value == subjectIdLong) continue;
                if (connectedIds.Contains(cand.Id.Value)) continue;
                var cb = cand.Bb;
                // Range overlap check (AABB sweep) before paying gap maths.
                if (cb.Max.X < subjMinX || cb.Min.X > subjMaxX) continue;
                if (cb.Max.Y < subjMinY || cb.Min.Y > subjMaxY) continue;
                if (cb.Max.Z < subjMinZ || cb.Min.Z > subjMaxZ) continue;

                double clearMm = AabbGap(sBb, cb) * ClashCategoryHelpers.MmPerFoot;
                if (clearMm < minClearMm) minClearMm = clearMm;

                // C3: Pull the per-pair target from the matrix; FAIL if this
                //     neighbour's specific target is breached.
                double pairTarget = ResolveTargetMm(matrix, subjectCat, cand.CategoryName, targetMm);
                if (clearMm < pairTarget && (failingNeighbourCat == null || pairTarget > rowTargetMm))
                {
                    rowTargetMm = pairTarget;
                    failingNeighbourCat = cand.CategoryName;
                }
            }

            string lvl = "";
            try { lvl = doc.GetElement(subject.LevelId)?.Name ?? ""; } catch (Exception ex) { StingLog.Warn($"Clearance.Level: {ex.Message}"); }

            string status;
            double reportedMm;
            if (double.IsPositiveInfinity(minClearMm))
            {
                // No neighbours inside search radius — automatically passes
                status = "PASS";
                reportedMm = SearchRadiusMm;
            }
            else
            {
                status = (failingNeighbourCat == null) ? "PASS" : "FAIL";
                reportedMm = Math.Round(minClearMm, 1);
            }

            rows.Add($"{subject.Id.Value},{Csv(subject.Category?.Name)},{Csv(lvl)},{reportedMm},{rowTargetMm},{status}");
            return status == "PASS";
        }

        private static HashSet<long> GetConnectedElementIds(Element el)
        {
            var result = new HashSet<long>();
            try
            {
                var mc = (el as MEPCurve)?.ConnectorManager
                      ?? (el as FamilyInstance)?.MEPModel?.ConnectorManager;
                if (mc == null) return result;
                foreach (Connector c in mc.Connectors)
                {
                    if (c == null) continue;
                    foreach (Connector other in c.AllRefs)
                    {
                        var owner = other?.Owner;
                        if (owner != null && owner.Id != null && owner.Id.Value != el.Id.Value)
                            result.Add(owner.Id.Value);
                    }
                }
            }
            catch (Exception ex) { StingLog.Warn($"GetConnectedElementIds: {ex.Message}"); }
            return result;
        }

        private static double AabbGap(BoundingBoxXYZ a, BoundingBoxXYZ b)
        {
            double dx = Math.Max(0.0, Math.Max(a.Min.X - b.Max.X, b.Min.X - a.Max.X));
            double dy = Math.Max(0.0, Math.Max(a.Min.Y - b.Max.Y, b.Min.Y - a.Max.Y));
            double dz = Math.Max(0.0, Math.Max(a.Min.Z - b.Max.Z, b.Min.Z - a.Max.Z));
            if (dx == 0 && dy == 0 && dz == 0) return 0.0;
            return Math.Sqrt(dx * dx + dy * dy + dz * dz);
        }

        private static string WriteCsv(Document doc, IList<string> rows)
        {
            try
            {
                string folder = ProjectFolderEngine.GetFolderPath(doc, "CLASHES");
                if (string.IsNullOrEmpty(folder)) return "";
                if (!Directory.Exists(folder))
                {
                    try { Directory.CreateDirectory(folder); }
                    catch (Exception ex) { StingLog.Warn($"WriteCsv.CreateDirectory: {ex.Message}"); return ""; }
                }
                string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                string path = Path.Combine(folder, $"mep_clearance_{stamp}.csv");
                File.WriteAllLines(path, rows);
                return path;
            }
            catch (Exception ex) { StingLog.Warn($"WriteCsv failed: {ex.Message}"); return ""; }
        }

        private static string Csv(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            if (s.Contains(',') || s.Contains('"') || s.Contains('\n'))
                return "\"" + s.Replace("\"", "\"\"") + "\"";
            return s;
        }
    }

    // ──────────────────────────────────────────────────────────────────────
    // NamingConventionAuditCommand — BS 1192 / ISO 19650 naming audit
    // ──────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Audits view names, sheet names, and worksets against BS 1192 / ISO 19650
    /// naming conventions. Results are written as a TSV to the model's .bimmanager
    /// directory (alongside issues.json and the other BIM manager sidecars).
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public sealed class NamingConventionAuditCommand : IExternalCommand
    {
        // [DISC]-[TYPE]-[LEVEL]-[SEQ]   e.g. MEP-RCP-L01-001
        private static readonly System.Text.RegularExpressions.Regex ViewPattern =
            new System.Text.RegularExpressions.Regex(@"^[A-Z]{1,3}-[A-Z]{2,4}-[A-Z0-9]{2,4}-\d{3,4}$",
                System.Text.RegularExpressions.RegexOptions.Compiled);

        // [PROJ]-[DISC]-[TYPE]-[SEQ]   e.g. PRJ-MEP-GA-001
        private static readonly System.Text.RegularExpressions.Regex SheetPattern =
            new System.Text.RegularExpressions.Regex(@"^[A-Z0-9]{2,5}-[A-Z]{1,3}-[A-Z]{2,4}-\d{3,4}$",
                System.Text.RegularExpressions.RegexOptions.Compiled);

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                if (commandData?.Application?.ActiveUIDocument == null)
                {
                    TaskDialog.Show("Naming Audit", "No active document.");
                    return Result.Failed;
                }
                var doc = commandData.Application.ActiveUIDocument.Document;
                if (doc == null || doc.IsFamilyDocument)
                {
                    TaskDialog.Show("Naming Audit", "Naming audit requires a project document.");
                    return Result.Failed;
                }

                int viewPass = 0, viewFail = 0, sheetPass = 0, sheetFail = 0, worksetPass = 0, worksetFail = 0;
                var rows = new List<string> { "kind\tid\tname\tstatus\treason" };

                // Views
                var views = new FilteredElementCollector(doc)
                    .OfClass(typeof(View))
                    .Cast<View>()
                    .Where(v => v != null && !v.IsTemplate)
                    .ToList();
                foreach (var v in views)
                {
                    string name = "";
                    try { name = v.Name ?? ""; } catch (Exception ex) { StingLog.Warn($"NamingAudit.ViewName: {ex.Message}"); }
                    bool ok = !string.IsNullOrEmpty(name) && ViewPattern.IsMatch(name);
                    if (ok) viewPass++; else viewFail++;
                    rows.Add($"View\t{v.Id.Value}\t{Tsv(name)}\t{(ok ? "PASS" : "FAIL")}\t{(ok ? "" : "Expected [DISC]-[TYPE]-[LEVEL]-[SEQ]")}");
                }

                // Sheets
                var sheets = new FilteredElementCollector(doc)
                    .OfClass(typeof(ViewSheet))
                    .Cast<ViewSheet>()
                    .Where(s => s != null)
                    .ToList();
                foreach (var s in sheets)
                {
                    string number = "";
                    try { number = s.SheetNumber ?? ""; } catch (Exception ex) { StingLog.Warn($"NamingAudit.SheetNumber: {ex.Message}"); }
                    bool ok = !string.IsNullOrEmpty(number) && SheetPattern.IsMatch(number);
                    if (ok) sheetPass++; else sheetFail++;
                    rows.Add($"Sheet\t{s.Id.Value}\t{Tsv(number)}\t{(ok ? "PASS" : "FAIL")}\t{(ok ? "" : "Expected [PROJ]-[DISC]-[TYPE]-[SEQ]")}");
                }

                // Worksets (only meaningful for workshared models)
                if (doc.IsWorkshared)
                {
                    FilteredWorksetCollector wsColl = null;
                    try { wsColl = new FilteredWorksetCollector(doc).OfKind(WorksetKind.UserWorkset); }
                    catch (Exception ex) { StingLog.Warn($"NamingAudit.Worksets: {ex.Message}"); }
                    if (wsColl != null)
                    {
                        foreach (var ws in wsColl)
                        {
                            string name = ws?.Name ?? "";
                            bool ok = !string.IsNullOrEmpty(name)
                                      && !name.Equals("Workset1", StringComparison.OrdinalIgnoreCase)
                                      && !name.Contains(' ');
                            if (ok) worksetPass++; else worksetFail++;
                            string reason = ok ? "" :
                                (name.Equals("Workset1", StringComparison.OrdinalIgnoreCase) ? "Default name 'Workset1'" :
                                 name.Contains(' ') ? "Contains spaces" : "Empty");
                            rows.Add($"Workset\t{ws?.Id?.IntegerValue ?? 0}\t{Tsv(name)}\t{(ok ? "PASS" : "FAIL")}\t{reason}");
                        }
                    }
                }

                string tsvPath = WriteTsv(doc, rows);

                var report = new StringBuilder();
                report.AppendLine("NAMING CONVENTION AUDIT");
                report.AppendLine($"Views — PASS: {viewPass}  FAIL: {viewFail}");
                report.AppendLine($"Sheets — PASS: {sheetPass}  FAIL: {sheetFail}");
                if (doc.IsWorkshared)
                    report.AppendLine($"Worksets — PASS: {worksetPass}  FAIL: {worksetFail}");
                else
                    report.AppendLine("Worksets — skipped (model is not workshared)");
                report.AppendLine();
                report.AppendLine("Report: " + (string.IsNullOrEmpty(tsvPath) ? "(not written)" : tsvPath));

                TaskDialog.Show("Naming Audit", report.ToString());
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Warn($"NamingConventionAuditCommand failed: {ex.Message}");
                message = ex.Message;
                return Result.Failed;
            }
        }

        private static string WriteTsv(Document doc, IList<string> rows)
        {
            try
            {
                if (string.IsNullOrEmpty(doc?.PathName)) return "";
                string dir = Path.Combine(Path.GetDirectoryName(doc.PathName) ?? "", ".bimmanager");
                if (!Directory.Exists(dir))
                {
                    try { Directory.CreateDirectory(dir); }
                    catch (Exception ex) { StingLog.Warn($"NamingAudit.CreateDirectory: {ex.Message}"); return ""; }
                }
                string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                string path = Path.Combine(dir, $"naming_audit_{stamp}.tsv");
                File.WriteAllLines(path, rows);
                return path;
            }
            catch (Exception ex) { StingLog.Warn($"NamingAudit.WriteTsv: {ex.Message}"); return ""; }
        }

        private static string Tsv(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');
        }
    }
}
