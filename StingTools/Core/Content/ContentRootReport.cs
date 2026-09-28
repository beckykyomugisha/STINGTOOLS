// ContentRootReport - say which content libraries are in play, in search order,
// with what is in them, and shout when a higher-priority one shadows the
// baseline.
//
// WHY THIS EXISTS
//
// ContentRoots.Resolve searches project -> shared -> baseline. The baseline is
// the deployed Data/TagFamilies - the library a deploy updates, the one under
// version control, the one every fix lands in. It is searched LAST.
//
// On 2026-09-21 a machine carried %PROGRAMDATA%/STING/ContentLibrary/Tags with
// 207 tag families dated 8 August. That is the SHARED tier, so it won every
// lookup. A whole day of corrections - 127 categories, 977 re-pointed
// parameters, two parser fixes - went into the baseline and could never be
// reached. The same twelve shared-parameter type conflicts came back after
// every fix, because the families being loaded were never the families being
// fixed.
//
// Nothing anywhere said the shared library existed. No log line named the
// roots, no count compared them, and the report said "loaded existing family"
// without saying from where. The fix for that last part landed earlier the same
// day and is what finally identified the folder.
//
// So: name the roots at startup, and flag the shadow. Both are cheap, and
// either one alone would have turned six weeks of recurrence into five minutes
// of reading.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace StingTools.Core.Content
{
    /// <summary>One content root, where it sits in the search order, and what it holds.</summary>
    public class ContentRootInfo
    {
        /// <summary>1-based position in the search order. 1 wins.</summary>
        public int Rank { get; set; }
        public string Path { get; set; }
        /// <summary>"baseline" (the deployed library) or "above-baseline".</summary>
        public string Tier { get; set; }
        public bool Exists { get; set; }
        /// <summary>Families directly in this root's tag folder.</summary>
        public int FamilyCount { get; set; }
        /// <summary>Newest family write time, or null when empty.</summary>
        public DateTime? Newest { get; set; }

        public override string ToString()
            => $"#{Rank} [{Tier}] {Path} — " +
               (Exists ? $"{FamilyCount} family/families" +
                         (Newest.HasValue ? $", newest {Newest:yyyy-MM-dd}" : "")
                       : "does not exist");
    }

    /// <summary>Describes the content roots and detects a shadowed baseline.</summary>
    public static class ContentRootReport
    {
        /// <summary>
        /// Builds the per-root summary. Revit-free apart from the caller's root
        /// list, so the shadow rule below can be tested directly.
        /// </summary>
        public static List<ContentRootInfo> Describe(IEnumerable<string> rootsInOrder,
                                                     string baselinePath,
                                                     string tagSubFolder = "Tags")
        {
            var list = new List<ContentRootInfo>();
            int rank = 0;

            foreach (var root in rootsInOrder ?? Enumerable.Empty<string>())
            {
                if (string.IsNullOrWhiteSpace(root)) continue;
                rank++;

                // The baseline IS the tag folder; the other tiers hold it beneath them.
                bool isBaseline = !string.IsNullOrEmpty(baselinePath) &&
                                  string.Equals(root.TrimEnd('\\', '/'),
                                                baselinePath.TrimEnd('\\', '/'),
                                                StringComparison.OrdinalIgnoreCase);

                string tagDir = isBaseline ? root : System.IO.Path.Combine(root, tagSubFolder);

                var info = new ContentRootInfo
                {
                    Rank = rank,
                    Path = tagDir,
                    // Do NOT guess the tier from the rank. Resolve() returns a
                    // flat, already-ordered list, so "rank 1 means project" is an
                    // invention that is wrong whenever no project root exists -
                    // which is most of the time. What matters for the warning is
                    // only baseline vs above-it, and that IS knowable.
                    Tier = isBaseline ? "baseline" : "above-baseline",
                    Exists = SafeExists(tagDir)
                };

                if (info.Exists)
                {
                    var files = SafeFiles(tagDir);
                    info.FamilyCount = files.Count;
                    if (files.Count > 0)
                        info.Newest = files.Max(f => SafeWriteTime(f));
                }
                list.Add(info);
            }
            return list;
        }

        /// <summary>
        /// The warning to emit, or null when nothing shadows the baseline.
        ///
        /// <para>A higher-priority root that holds families SHADOWS the baseline -
        /// every lookup it can answer, it answers, and the baseline is never
        /// consulted. That is legitimate for a curated firm library and a trap
        /// when the library is stale, because a deploy cannot update it and the
        /// corrected families can never win.</para>
        ///
        /// <para>Staleness is judged on DATE, not on count. A shared library with
        /// the same number of families can still be months behind, and on
        /// 2026-09-21 it was: 207 files from 8 August shadowing 206 corrected
        /// that same day.</para>
        /// </summary>
        public static string ShadowWarning(List<ContentRootInfo> roots)
        {
            if (roots == null || roots.Count == 0) return null;

            var baseline = roots.FirstOrDefault(r => r.Tier == "baseline" && r.Exists && r.FamilyCount > 0);
            if (baseline == null) return null;

            var shadowing = roots
                .Where(r => r.Rank < baseline.Rank && r.Exists && r.FamilyCount > 0)
                .ToList();
            if (shadowing.Count == 0) return null;

            var parts = new List<string>();
            foreach (var s in shadowing)
            {
                string age = "";
                if (s.Newest.HasValue && baseline.Newest.HasValue)
                {
                    int days = (int)(baseline.Newest.Value.Date - s.Newest.Value.Date).TotalDays;
                    if (days > 0)
                        age = $" and is {days} day(s) OLDER than the baseline";
                }
                parts.Add($"'{s.Path}' ({s.FamilyCount} families, newest " +
                          $"{(s.Newest.HasValue ? s.Newest.Value.ToString("yyyy-MM-dd") : "unknown")}){age}");
            }

            return "Content shadowing: " + string.Join("; ", parts) +
                   $" is searched BEFORE the deployed library '{baseline.Path}' " +
                   $"({baseline.FamilyCount} families, newest " +
                   $"{(baseline.Newest.HasValue ? baseline.Newest.Value.ToString("yyyy-MM-dd") : "unknown")}). " +
                   "Families load from the higher-priority root, so corrections made to the deployed " +
                   "library will NOT be used. Move or refresh the shadowing library, or point " +
                   "STING_CONTENT_LIB at the one you want.";
        }

        /// <summary>Logs the roots in order, then the shadow warning if there is one.</summary>
        public static void LogRoots(List<ContentRootInfo> roots)
        {
            if (roots == null) return;
            foreach (var r in roots) StingLog.Info("Content root " + r);

            string warn = ShadowWarning(roots);
            if (string.IsNullOrEmpty(warn)) return;

            // The MESSAGE is right either way and is never suppressed: once a
            // shared library wins, a deploy alone stops changing what loads, and
            // a fix that looks ignored is the whole failure this exists to
            // prevent. Only the SEVERITY differs.
            //
            // A newer shared library is Promote Tag Library working as intended.
            // Logging that as a warning would put one on every document open in
            // the correct configuration - and a warning that is always there is
            // one nobody reads, including on the day it finally matters.
            //
            // An OLDER one is the 2026-09-21 bug itself: 207 families from 8
            // August winning over 206 corrected that day, so six weeks of fixes
            // could never load. That stays a warning.
            if (IsStale(roots)) StingLog.Warn(warn);
            else StingLog.Info(warn + " (the shadowing library is NEWER, so this is " +
                               "a promoted library winning as intended - but remember that " +
                               "deploying alone will no longer change what loads.)");
        }

        /// <summary>
        /// Is anything shadowing the baseline actually OLDER than it?
        ///
        /// <para>An unknown date on either side counts as stale. A library whose
        /// age cannot be established is not evidence of safety, and the cost of
        /// being wrong in that direction is six weeks of invisible fixes.</para>
        /// </summary>
        public static bool IsStale(List<ContentRootInfo> roots)
        {
            if (roots == null) return false;
            var baseline = roots.FirstOrDefault(r => r.Tier == "baseline" && r.Exists && r.FamilyCount > 0);
            if (baseline == null) return false;

            return roots.Any(r => r.Rank < baseline.Rank && r.Exists && r.FamilyCount > 0 &&
                                  (!r.Newest.HasValue || !baseline.Newest.HasValue ||
                                   r.Newest.Value.Date < baseline.Newest.Value.Date));
        }

        private static bool SafeExists(string p)
        { try { return Directory.Exists(p); } catch { return false; } }

        private static List<string> SafeFiles(string p)
        { try { return Directory.GetFiles(p, "*.rfa", SearchOption.TopDirectoryOnly).ToList(); }
          catch { return new List<string>(); } }

        private static DateTime SafeWriteTime(string f)
        { try { return File.GetLastWriteTime(f); } catch { return DateTime.MinValue; } }
    }
}
