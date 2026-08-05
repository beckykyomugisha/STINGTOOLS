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
//  * AssignMepSystem — placeholder for MEPSystemBuilder.Connect, which
//    requires deeper system traversal; for now records a warning if
//    enabled and the instance has unconnected connectors.

using System;
using Autodesk.Revit.DB;

namespace StingTools.Core.Placement
{
    public static class PostPlacementHooks
    {
        /// <summary>Run TagPipelineHelper.RunFullPipeline on every placed instance.</summary>
        public static bool RunDataTagPipeline { get; set; } = false;

        /// <summary>Set to true when the tag pipeline assembly could not be located
        /// at startup. Checked by the Fixtures UI to show a tooltip explaining why
        /// data-tagging is unavailable for this session.</summary>
        public static bool TagPipelineMissing { get; set; } = false;

        /// <summary>Seed COBIE_* parameters from the rule's StandardRef / Notes.</summary>
        public static bool SeedCobieComponent { get; set; } = false;

        /// <summary>Probe MEP connectors and warn when any are unconnected.</summary>
        public static bool AssignMepSystem { get; set; } = false;

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
                // Reflection guard: the tagging pipeline lives in another
                // assembly module and we don't want a hard dependency
                // edge between Placement and Core. If TagPipelineHelper
                // moves or is renamed we just skip silently.
                var t = Type.GetType("StingTools.Core.TagPipelineHelper, StingTools");
                if (t == null) { TagPipelineMissing = true; return; }
                var m = t.GetMethod("RunFullPipeline", new[] { typeof(Document), typeof(Element) });
                if (m == null) { TagPipelineMissing = true; return; }
                m.Invoke(null, new object[] { fi.Document, fi });
            }
            catch (Exception ex) { StingLog.Warn($"PostPlacementHooks.RunTagPipeline {fi.Id}: {ex.Message}"); }
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
                int unconnected = 0;
                foreach (Connector c in mgr.Connectors)
                {
                    if (c == null) continue;
                    if (!c.IsConnected) unconnected++;
                }
                if (unconnected > 0)
                    StingLog.Info($"PostPlacementHooks.AssignMepSystem: {fi.Id} has {unconnected} unconnected connector(s); MEP join deferred to MEPSystemBuilder.");
            }
            catch (Exception ex) { StingLog.Warn($"PostPlacementHooks.AssignMep {fi.Id}: {ex.Message}"); }
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
