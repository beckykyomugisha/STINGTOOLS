// ══════════════════════════════════════════════════════════════════════════
//  ExportRouteMerge.cs — backfill newly-shipped export-route keys onto a
//  project that already has a persisted project_setup.json.
//
//  WHY: ProjectSetup.Load only null-guarded ExportRoutes. A key added to
//  DefaultBimRoutes / DefaultCdeFirstRoutes / DefaultMiniRoutes therefore
//  reached only projects set up AFTER the change. In CdeFirst mode that is not
//  cosmetic — ProjectFolderEngine.GetExportFolder returns MISC for any key the
//  routes do not carry, so the export silently lands in the wrong folder and
//  nothing reports it. This has applied to every export type ever added, not
//  just MaterialSchedule.
//
//  Revit-free so the merge rules are pinned by ExportRouteMergeTests.
// ══════════════════════════════════════════════════════════════════════════
using System;
using System.Collections.Generic;

namespace StingTools.Core
{
    public static class ExportRouteMerge
    {
        /// <summary>
        /// Add every default key the target does not already have. Returns the
        /// number added, so a caller can skip re-saving when nothing changed.
        ///
        /// A key that is PRESENT is never touched, even when its value is empty —
        /// a user who cleared a route meant to clear it, and resurrecting the
        /// default on every load would make that edit impossible to keep.
        /// </summary>
        public static int MergeMissing(IDictionary<string, string> target,
                                       IDictionary<string, string> defaults)
        {
            if (target == null || defaults == null) return 0;

            // Do NOT rely on the target's comparer. Newtonsoft may replace the
            // property's OrdinalIgnoreCase dictionary with an ordinal one when it
            // deserialises, and a case-sensitive ContainsKey would then mint
            // "MaterialSchedule" alongside an existing "materialschedule".
            var present = new HashSet<string>(target.Keys, StringComparer.OrdinalIgnoreCase);

            int added = 0;
            foreach (var kv in defaults)
            {
                if (string.IsNullOrWhiteSpace(kv.Key)) continue;
                if (present.Contains(kv.Key)) continue;   // present ⇒ leave alone
                target[kv.Key] = kv.Value;
                present.Add(kv.Key);
                added++;
            }
            return added;
        }
    }
}
