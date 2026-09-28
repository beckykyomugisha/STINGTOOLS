// TagLibraryPromotion - publish the finished tag library to the shared content
// root, and refuse when the thing being published is not the thing in version
// control.
//
// WHY THE REFUSAL IS THE POINT
//
// The shared root is searched BEFORE the deployed library, and a deploy cannot
// touch it. Those two facts make it the right home for a finished library and a
// trap for an unfinished one: whatever is promoted wins every lookup until
// somebody promotes again.
//
// On 2026-09-21 a shared library from 8 August shadowed the deployed one for six
// weeks. Every correction went to the folder that loses, and the same twelve
// parameter conflicts came back after every fix. Promoting a half-finished
// library would recreate that exactly, with fresher files.
//
// So this refuses to promote a source that differs from the git-tracked copy.
// The deployed folder is overwritten by every deploy, so a difference means the
// work is not committed - and promoting it would publish something no one can
// reproduce, review or roll back. Four separate times today the preserve-guard
// rescued uncommitted family work; this is the same lesson, one tier up.
//
// The git check is SKIPPED, not failed, when no repository copy is discoverable
// - an end-user machine has no checkout, and refusing there would block the
// people the library is for.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;

namespace StingTools.Core.Content
{
    /// <summary>What a promotion would do, and why it must not proceed.</summary>
    public class PromotionPlan
    {
        public string SourceDir { get; set; }
        public string TargetDir { get; set; }

        /// <summary>Present in source, absent from target.</summary>
        public List<string> ToAdd { get; } = new List<string>();
        /// <summary>Present in both, different content.</summary>
        public List<string> ToUpdate { get; } = new List<string>();
        /// <summary>Present in both, identical - copied over anyway would be a no-op.</summary>
        public List<string> Unchanged { get; } = new List<string>();
        /// <summary>In the target and not in the source. NEVER deleted - only reported.</summary>
        public List<string> ExtraInTarget { get; } = new List<string>();

        /// <summary>Reasons the promotion must not run. Empty means go.</summary>
        public List<string> Blockers { get; } = new List<string>();

        public bool CanProceed => Blockers.Count == 0;
        public int WouldWrite => ToAdd.Count + ToUpdate.Count;

        public string Summary()
            => $"{ToAdd.Count} new, {ToUpdate.Count} updated, {Unchanged.Count} unchanged" +
               (ExtraInTarget.Count > 0 ? $", {ExtraInTarget.Count} already in the target and not in the source" : "");
    }

    /// <summary>Plans and records a tag-library promotion.</summary>
    public static class TagLibraryPromotion
    {
        /// <summary>
        /// Compares source against target and against the git-tracked copy.
        ///
        /// <para><paramref name="gitDir"/> may be null or missing - the check is
        /// then skipped rather than failed, because an end-user machine has no
        /// checkout. When it IS present and differs from the source, promotion is
        /// blocked: the deployed folder is overwritten by every deploy, so a
        /// difference means uncommitted work.</para>
        /// </summary>
        public static PromotionPlan Plan(string sourceDir, string targetDir, string gitDir)
        {
            var plan = new PromotionPlan { SourceDir = sourceDir, TargetDir = targetDir };

            if (string.IsNullOrWhiteSpace(sourceDir) || !DirExists(sourceDir))
            {
                plan.Blockers.Add($"the source library '{sourceDir}' does not exist");
                return plan;
            }

            var source = Families(sourceDir);
            if (source.Count == 0)
            {
                plan.Blockers.Add($"the source library '{sourceDir}' holds no .rfa files - " +
                                  "promoting it would publish an empty library over a working one");
                return plan;
            }

            // The refusal that matters.
            if (!string.IsNullOrWhiteSpace(gitDir) && DirExists(gitDir))
            {
                var git = Families(gitDir);
                var drift = Compare(source, git, sourceDir, gitDir);
                if (drift.Count > 0)
                {
                    plan.Blockers.Add(
                        $"the source differs from the version-controlled copy in {drift.Count} file(s): " +
                        string.Join(", ", drift.Take(5)) + (drift.Count > 5 ? ", ..." : "") +
                        ". Commit the library first - a deploy overwrites the source folder, so " +
                        "promoting now would publish something that cannot be reproduced or rolled back.");
                }
            }

            var target = DirExists(targetDir) ? Families(targetDir) : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (var kv in source)
            {
                string tHash;
                if (!target.TryGetValue(kv.Key, out tHash)) plan.ToAdd.Add(kv.Key);
                else if (!string.Equals(tHash, kv.Value, StringComparison.Ordinal)) plan.ToUpdate.Add(kv.Key);
                else plan.Unchanged.Add(kv.Key);
            }
            foreach (var kv in target)
                if (!source.ContainsKey(kv.Key)) plan.ExtraInTarget.Add(kv.Key);

            plan.ToAdd.Sort(StringComparer.Ordinal);
            plan.ToUpdate.Sort(StringComparer.Ordinal);
            plan.ExtraInTarget.Sort(StringComparer.Ordinal);
            return plan;
        }

        /// <summary>
        /// The manifest written beside the promoted library. It records what was
        /// published and from where, so the next person can tell whether the
        /// shared copy is the committed one without diffing 206 binaries.
        /// </summary>
        public static string BuildManifest(PromotionPlan plan, string user, DateTime whenUtc)
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("# STING tag library - promotion manifest");
            sb.AppendLine("# Written by Promote Tag Library. Do not hand-edit: it is the record of");
            sb.AppendLine("# what this shared library contains and where it came from.");
            sb.AppendLine($"promotedUtc={whenUtc:yyyy-MM-ddTHH:mm:ssZ}");
            sb.AppendLine($"promotedBy={user}");
            sb.AppendLine($"source={plan.SourceDir}");
            sb.AppendLine($"added={plan.ToAdd.Count}");
            sb.AppendLine($"updated={plan.ToUpdate.Count}");
            sb.AppendLine($"unchanged={plan.Unchanged.Count}");
            sb.AppendLine($"extraInTarget={plan.ExtraInTarget.Count}");
            sb.AppendLine("# files written by this promotion:");
            foreach (var f in plan.ToAdd) sb.AppendLine("ADD\t" + f);
            foreach (var f in plan.ToUpdate) sb.AppendLine("UPD\t" + f);
            foreach (var f in plan.ExtraInTarget) sb.AppendLine("KEPT\t" + f);   // present, untouched
            return sb.ToString();
        }

        /// <summary>File name to SHA-256, for the .rfa directly in a folder.</summary>
        public static Dictionary<string, string> Families(string dir)
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                foreach (var f in Directory.GetFiles(dir, "*.rfa", SearchOption.TopDirectoryOnly))
                {
                    string name = Path.GetFileName(f);
                    if (IsRevitBackupName(name)) continue;      // ".0001.rfa" is not a family
                    map[name] = Hash(f);
                }
            }
            catch (Exception ex) { StingLog.Warn($"TagLibraryPromotion.Families('{dir}'): {ex.Message}"); }
            return map;
        }

        /// <summary>Names that differ between two hashed sets, in either direction.</summary>
        public static List<string> Compare(Dictionary<string, string> a, Dictionary<string, string> b,
                                           string aLabel = "a", string bLabel = "b")
        {
            var diff = new List<string>();
            foreach (var kv in a ?? new Dictionary<string, string>())
            {
                string other;
                if (!(b ?? new Dictionary<string, string>()).TryGetValue(kv.Key, out other)) diff.Add(kv.Key);
                else if (!string.Equals(other, kv.Value, StringComparison.Ordinal)) diff.Add(kv.Key);
            }
            foreach (var kv in b ?? new Dictionary<string, string>())
                if (!(a ?? new Dictionary<string, string>()).ContainsKey(kv.Key)) diff.Add(kv.Key);

            diff.Sort(StringComparer.Ordinal);
            return diff;
        }

        /// <summary>"Family.0001.rfa" - Revit's own backup, not a family.</summary>
        public static bool IsRevitBackupName(string fileName)
        {
            string stem = Path.GetFileNameWithoutExtension(fileName) ?? "";
            int dot = stem.LastIndexOf('.');
            if (dot < 0 || dot == stem.Length - 1) return false;
            string tail = stem.Substring(dot + 1);
            return tail.Length == 4 && tail.All(char.IsDigit);
        }

        private static bool DirExists(string d)
        { try { return !string.IsNullOrWhiteSpace(d) && Directory.Exists(d); } catch { return false; } }

        private static string Hash(string path)
        {
            try
            {
                using (var sha = SHA256.Create())
                using (var fs = File.OpenRead(path))
                    return BitConverter.ToString(sha.ComputeHash(fs)).Replace("-", "");
            }
            catch (Exception ex)
            {
                // A file that cannot be read must never compare EQUAL to anything -
                // that would let a locked or corrupt family pass as unchanged.
                StingLog.Warn($"TagLibraryPromotion.Hash('{path}'): {ex.Message}");
                return "UNREADABLE:" + Guid.NewGuid().ToString("N");
            }
        }
    }
}
