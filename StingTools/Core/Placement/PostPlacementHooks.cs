using StingTools.Core;
// PC-17 — Post-placement hooks.
//
// Optional side-effects fired by FixturePlacementEngine after every
// placed instance commits inside the engine's Transaction. Hooks are
// gated by static toggles so PlaceFixturesCommand and the Centre can
// choose which to run per session without touching the engine.
//
// Today's hooks:
//  * RunDataTagPipeline — invoke TagPipelineHelper.RunFullPipeline so
//    the placed instance lands fully tagged, ISO-19650 8-segment.
//  * SeedCobieComponent — copy the rule's StandardRef / Notes onto
//    COBIE_COMPONENT_NAME / COBIE_COMPONENT_DESCRIPTION when present.
//  * AssignMepSystem — for every open connector on the placed instance,
//    find the nearest coincident compatible (same-domain, unconnected)
//    connector within a small search radius and ConnectTo it. Connectors
//    that can't be joined are counted and reported (never silently
//    dropped).

using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;

namespace StingTools.Core.Placement
{
    public static class PostPlacementHooks
    {
        /// <summary>Run TagPipelineHelper.RunFullPipeline on every placed instance.</summary>
        public static bool RunDataTagPipeline { get; set; } = false;

        /// <summary>Set to true when the tag pipeline could not build a valid
        /// population context for this document (rooms/levels/shared-params
        /// missing). Checked by the Fixtures UI to show a tooltip explaining
        /// why data-tagging produced nothing for this session.</summary>
        public static bool TagPipelineMissing { get; set; } = false;

        /// <summary>Seed COBIE_* parameters from the rule's StandardRef / Notes.</summary>
        public static bool SeedCobieComponent { get; set; } = false;

        /// <summary>Auto-connect open MEP connectors to the nearest compatible
        /// connector and report any left open.</summary>
        public static bool AssignMepSystem { get; set; } = false;

        /// <summary>Connectors joined during the current run (reset by BeginRun).</summary>
        public static int MepConnectedCount { get; private set; }

        /// <summary>Connectors left open during the current run (reset by BeginRun).</summary>
        public static int MepLeftOpenCount { get; private set; }

        // Connector.ConnectTo only succeeds for physically coincident connectors,
        // so the search is a tight coincidence tolerance (in Revit internal feet),
        // not a routing radius — a wider search would just return near-but-not-
        // coincident connectors that ConnectTo rejects, inflating the left-open
        // count. 25 mm absorbs minor float drift between snapped origins.
        private const double MepCoincidenceTolFt = 25.0 / 304.8;

        // Per-run tag-pipeline context (built once, reused for every placed
        // instance). Reset by BeginRun so a fresh run picks up manual edits
        // made between runs.
        private static Document _tagDoc;
        private static TokenAutoPopulator.PopulationContext _tagCtx;
        private static HashSet<string> _tagIndex;
        private static Dictionary<string, int> _tagSeq;
        private static List<Temp.FormulaEngine.FormulaDefinition> _tagFormulas;
        private static List<Grid> _tagGrids;

        /// <summary>
        /// Called by the engine at the start of a run. Clears per-run caches
        /// and counters so consecutive runs are independent.
        /// </summary>
        public static void BeginRun()
        {
            _tagDoc = null; _tagCtx = null; _tagIndex = null;
            _tagSeq = null; _tagFormulas = null; _tagGrids = null;
            MepConnectedCount = 0; MepLeftOpenCount = 0;
            TagPipelineMissing = false;
        }

        /// <summary>
        /// Entry point invoked by the engine. Each toggle is independent.
        /// Failures are swallowed at the call site so a hook can never
        /// tank a placement.
        /// </summary>
        public static void RunFor(FamilyInstance fi, PlacementRule rule)
        {
            if (fi == null || rule == null) return;
            if (RunDataTagPipeline) RunTagPipelineSafe(fi);
            if (SeedCobieComponent) SeedCobieSafe(fi, rule);
            if (AssignMepSystem)    AssignMepSafe(fi);
        }

        private static void RunTagPipelineSafe(FamilyInstance fi)
        {
            try
            {
                var doc = fi.Document;
                if (!EnsureTagContext(doc)) return;   // TagPipelineMissing already set
                // Direct typed call — TagPipelineHelper is internal to this
                // assembly. RunFullPipeline takes the full canonical context
                // (population context + tag index + seq counters + formulas +
                // grid lines); there is no 2-arg overload, which is why the
                // old reflection probe silently no-op'd.
                TagPipelineHelper.RunFullPipeline(
                    doc, fi, _tagCtx, _tagIndex, _tagSeq, _tagFormulas, _tagGrids,
                    overwrite: false, skipComplete: true,
                    collisionMode: TagCollisionMode.AutoIncrement);
            }
            catch (Exception ex) { StingLog.Warn($"PostPlacementHooks.RunTagPipeline {fi.Id}: {ex.Message}"); }
        }

        /// <summary>Build the tag-pipeline context once per document/run.</summary>
        private static bool EnsureTagContext(Document doc)
        {
            if (doc == null) return false;
            if (ReferenceEquals(_tagDoc, doc) && _tagCtx != null) return true;
            try
            {
                var (idx, seq) = TagConfig.BuildTagIndexAndCounters(doc);
                var ctx = TokenAutoPopulator.PopulationContext.Build(doc);
                if (ctx == null || !ctx.IsValid())
                {
                    TagPipelineMissing = true;
                    StingLog.Warn("PostPlacementHooks: tag population context invalid — data-tag pipeline skipped this run.");
                    return false;
                }
                _tagIndex = idx;
                _tagSeq = seq;
                _tagCtx = ctx;
                _tagFormulas = TagPipelineHelper.LoadFormulas();
                _tagGrids = TagPipelineHelper.LoadGridLines(doc);
                _tagDoc = doc;
                return true;
            }
            catch (Exception ex)
            {
                TagPipelineMissing = true;
                StingLog.Warn($"PostPlacementHooks.EnsureTagContext: {ex.Message}");
                return false;
            }
        }

        private static void SeedCobieSafe(FamilyInstance fi, PlacementRule rule)
        {
            try
            {
                TrySetText(fi, "COBIE_COMPONENT_NAME",        rule.UniclassPr ?? rule.MergeKey ?? "");
                TrySetText(fi, "COBIE_COMPONENT_DESCRIPTION", rule.Notes ?? "");
                TrySetText(fi, "COBIE_TYPE_REFERENCE",        rule.StandardRef ?? "");
            }
            catch (Exception ex) { StingLog.Warn($"PostPlacementHooks.SeedCobie {fi.Id}: {ex.Message}"); }
        }

        private static void AssignMepSafe(FamilyInstance fi)
        {
            try
            {
                var mgr = fi.MEPModel?.ConnectorManager;
                if (mgr == null) return;
                var doc = fi.Document;
                var ownerId = fi.Id;

                int connected = 0, leftOpen = 0;
                foreach (Connector src in mgr.Connectors)
                {
                    if (src == null) continue;
                    bool isConn;
                    try { isConn = src.IsConnected; } catch { continue; }
                    if (isConn) continue;

                    Domain dom;
                    try { dom = src.Domain; } catch { dom = Domain.DomainUndefined; }
                    // Only attempt physical MEP joins; logical/undefined domains
                    // (e.g. electrical-power circuiting) are out of scope here.
                    if (dom != Domain.DomainHvac && dom != Domain.DomainPiping &&
                        dom != Domain.DomainCableTrayConduit)
                    { leftOpen++; continue; }

                    var mate = FindNearestCompatibleConnector(doc, src, dom, ownerId, MepCoincidenceTolFt);
                    if (mate != null && TryConnect(src, mate)) connected++;
                    else leftOpen++;
                }

                MepConnectedCount += connected;
                MepLeftOpenCount += leftOpen;
                if (connected > 0 || leftOpen > 0)
                    StingLog.Info($"PostPlacementHooks.AssignMepSystem: {fi.Id} connected {connected}, {leftOpen} left open.");
            }
            catch (Exception ex) { StingLog.Warn($"PostPlacementHooks.AssignMep {fi.Id}: {ex.Message}"); }
        }

        /// <summary>
        /// Find the nearest unconnected connector of the same domain within
        /// radiusFt of <paramref name="src"/>, on a different element.
        /// Bounded by a bounding-box filter so the search stays local.
        /// </summary>
        private static Connector FindNearestCompatibleConnector(
            Document doc, Connector src, Domain dom, ElementId ownerId, double radiusFt)
        {
            XYZ o;
            try { o = src.Origin; } catch { return null; }
            if (o == null) return null;

            try
            {
                var min = new XYZ(o.X - radiusFt, o.Y - radiusFt, o.Z - radiusFt);
                var max = new XYZ(o.X + radiusFt, o.Y + radiusFt, o.Z + radiusFt);
                var bbFilter = new BoundingBoxIntersectsFilter(new Outline(min, max));

                Connector best = null;
                double bestDist = radiusFt;
                var collector = new FilteredElementCollector(doc)
                    .WherePasses(bbFilter)
                    .WhereElementIsNotElementType();

                foreach (var el in collector)
                {
                    if (el == null || el.Id == ownerId) continue;
                    ConnectorManager cm = null;
                    if (el is MEPCurve mc) cm = mc.ConnectorManager;
                    else if (el is FamilyInstance other) cm = other.MEPModel?.ConnectorManager;
                    if (cm == null) continue;

                    foreach (Connector c in cm.Connectors)
                    {
                        if (c == null) continue;
                        bool conn;
                        try { conn = c.IsConnected; } catch { continue; }
                        if (conn) continue;
                        Domain d;
                        try { d = c.Domain; } catch { continue; }
                        if (d != dom) continue;
                        XYZ co;
                        try { co = c.Origin; } catch { continue; }
                        if (co == null) continue;
                        double dist = co.DistanceTo(o);
                        if (dist < bestDist) { bestDist = dist; best = c; }
                    }
                }
                return best;
            }
            catch (Exception ex) { StingLog.Warn($"PostPlacementHooks.FindConnector: {ex.Message}"); return null; }
        }

        private static bool TryConnect(Connector a, Connector b)
        {
            try
            {
                // ConnectTo refuses mismatched domains / non-coincident
                // connectors; it never throws-and-corrupts, so a failed join
                // simply leaves both ends open (counted as left-open).
                a.ConnectTo(b);
                try { return a.IsConnected && b.IsConnected; } catch { return false; }
            }
            catch (Exception ex) { StingLog.Warn($"PostPlacementHooks.TryConnect: {ex.Message}"); return false; }
        }

        private static void TrySetText(Element el, string paramName, string value)
        {
            if (el == null || string.IsNullOrEmpty(paramName) || value == null) return;
            try
            {
                var p = el.LookupParameter(paramName);
                if (p == null || p.IsReadOnly || p.StorageType != StorageType.String) return;
                if ((p.AsString() ?? "") == value) return;
                p.Set(value);
            }
            catch { }
        }
    }
}
