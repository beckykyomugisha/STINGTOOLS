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

        /// <summary>Seed COBIE_* parameters from the rule's StandardRef / Notes.</summary>
        public static bool SeedCobieComponent { get; set; } = false;

        /// <summary>Probe MEP connectors and warn when any are unconnected.</summary>
        public static bool AssignMepSystem { get; set; } = false;

        // Phase 139.27 (C-03) — reflection target resolution result, cached
        // and surfaced once per session. Without this, a renamed / moved
        // TagPipelineHelper silently skipped tagging on every placed
        // instance — provenance + ISO-19650 tags were promised by the
        // Centre's checkbox but never delivered. The first call probes
        // and remembers; later calls skip the probe and either tag or
        // skip cheaply.
        private static System.Reflection.MethodInfo _tagPipelineMethod;
        private static bool _tagPipelineProbed;
        private static bool _tagPipelineMissingWarned;

        /// <summary>True once <see cref="RunDataTagPipeline"/> has been
        /// requested at least once and the reflection target was missing.
        /// PlaceFixturesCommand surfaces this in the result panel so the
        /// user knows tagging was silently degraded.</summary>
        public static bool TagPipelineMissing => _tagPipelineProbed && _tagPipelineMethod == null;

        /// <summary>Reset the cached probe — used by tests / a future hot-reload path.</summary>
        public static void ResetReflectionCache()
        {
            _tagPipelineMethod = null;
            _tagPipelineProbed = false;
            _tagPipelineMissingWarned = false;
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
                // Reflection guard: the tagging pipeline lives in another
                // assembly module and we don't want a hard dependency
                // edge between Placement and Core. If TagPipelineHelper
                // moves or is renamed we surface a one-shot warning so
                // users notice that "Tag every placement" was silently
                // a no-op (the historic behaviour swallowed the miss).
                if (!_tagPipelineProbed)
                {
                    _tagPipelineProbed = true;
                    var t = Type.GetType("StingTools.Core.TagPipelineHelper, StingTools");
                    _tagPipelineMethod = t?.GetMethod(
                        "RunFullPipeline", new[] { typeof(Document), typeof(Element) });
                    if (_tagPipelineMethod == null && !_tagPipelineMissingWarned)
                    {
                        _tagPipelineMissingWarned = true;
                        StingLog.Warn(
                            "PostPlacementHooks.RunDataTagPipeline is enabled but " +
                            "StingTools.Core.TagPipelineHelper.RunFullPipeline(Document,Element) " +
                            "could not be found by reflection — tagging will be skipped on every " +
                            "placed instance for this session. The class may have been renamed or " +
                            "moved. Disable the option in the Placement Centre or repair the lookup.");
                    }
                }
                if (_tagPipelineMethod == null) return;
                _tagPipelineMethod.Invoke(null, new object[] { fi.Document, fi });
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
