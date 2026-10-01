// StingTools — the Revit-free decisions of DrainageInvertDimensioner's note pass:
// whether the pass has anything to do, and which stamped notes it must remove.
//
// Kept here, under test (StingTools.Tags.Tests), because the first one was wrong in
// a way no warning showed: the pass returned as soon as the view had no drainage
// pipe, so deleting the last drain left its stamped IL / gradient notes behind
// forever (DRAW-9, smoke run 2026-10-01 on 3bf1a78b6).

using System;
using System.Collections.Generic;
using System.Linq;

namespace StingTools.Core.Drawing.Dimensioning
{
    public static class InvertNoteRules
    {
        /// <summary>
        /// Whether the note pass must run. It must run while the view has
        /// drainage pipes to annotate OR stamped notes to reconcile — a view whose
        /// last drain was deleted still holds that drain's notes, and only the
        /// pass removes them.
        /// </summary>
        public static bool NeedsPass(int drainagePipesInView, int stampedNotesInView)
        {
            return drainagePipesInView > 0 || stampedNotesInView > 0;
        }

        /// <summary>
        /// Stamped keys whose notes are provably stale and must be removed: not
        /// written this run, and either the host no longer exists, or it was
        /// processed this run and no longer has that end (a pipe made level has no
        /// downstream IL and no gradient). A host that exists but was not in this
        /// run (filtered out, no invert) is left alone.
        /// </summary>
        public static List<string> KeysToRemove(
            IEnumerable<string> stampedKeys,
            ISet<string> writtenKeys,
            ISet<string> processedHosts,
            Func<string, bool> hostExists)
        {
            if (stampedKeys == null) return new List<string>();
            if (hostExists == null) throw new ArgumentNullException(nameof(hostExists));
            var remove = new List<string>();
            foreach (var key in stampedKeys.Where(k => !string.IsNullOrEmpty(k)))
            {
                if (writtenKeys != null && writtenKeys.Contains(key)) continue;
                var host = AnnotationProvenance.HostOf(key);
                bool gone = string.IsNullOrEmpty(host) || !hostExists(host);
                if (!gone && (processedHosts == null || !processedHosts.Contains(host))) continue;
                remove.Add(key);
            }
            return remove;
        }
    }
}
