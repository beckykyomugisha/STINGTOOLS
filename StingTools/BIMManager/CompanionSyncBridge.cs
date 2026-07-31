using System;
using System.IO;
using System.Threading.Tasks;
using Planscape.Companion;
using StingTools.Core;

namespace StingTools.BIMManager
{
    /// <summary>
    /// What a synced document looks like on THIS machine. Derived from the file
    /// system, not from the server — the point of the badge is to answer "do I
    /// have this, and may I edit it", which is a local question.
    /// </summary>
    internal enum LocalSyncState
    {
        /// <summary>No local copy — either not synced yet, or not visible to this account.</summary>
        NotSynced,

        /// <summary>A writable WIP working copy. The one you are meant to edit.</summary>
        WorkingCopy,

        /// <summary>A read-only reference copy (SHARED / PUBLISHED).</summary>
        Reference,
    }

    /// <summary>
    /// BCC's window onto the Planscape Companion.
    ///
    /// <para>Two jobs, and deliberately no third: read the Companion's status, and
    /// ask it to sync. <b>BCC never runs sync logic itself</b> — that is the whole
    /// reason the Companion is a separate process (it has to work when Revit is
    /// closed), and a second implementation living in the plugin would be the
    /// thing that eventually disagrees with the first.</para>
    ///
    /// <para>Every call here treats "the Companion is not running" as a normal
    /// answer rather than an error. It is by far the most likely outcome on a
    /// machine where nobody has set sync up, and a plugin that throws or logs
    /// scary things about it would be wrong about its own product.</para>
    /// </summary>
    internal static class CompanionSyncBridge
    {
        /// <summary>
        /// Live status, or a not-running placeholder. Never throws.
        ///
        /// Synchronous by necessity — BCC is WPF and the call sites are click
        /// handlers and cell renderers. The underlying connect is bounded by
        /// <see cref="CompanionIpc.ConnectTimeoutMs"/>, so the worst case a UI
        /// thread can see is that timeout, not an indefinite block.
        /// </summary>
        public static CompanionStatus GetStatus()
        {
            try
            {
                return CompanionIpcClient.GetStatusAsync().GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                StingLog.Warn($"Companion status: {ex.Message}");
                return CompanionStatus.NotRunning();
            }
        }

        /// <summary>
        /// Ask the Companion to sync. Returns false only when it is not running —
        /// true means STARTED, not finished, because the reply comes back
        /// immediately and the download happens in the Companion's own time.
        /// </summary>
        public static bool SyncNow(string projectId = null)
        {
            try
            {
                return CompanionIpcClient.SyncNowAsync(projectId).GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                StingLog.Warn($"Companion sync-now: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Resolve a document's local state by looking for it in the project's
        /// sync folder.
        ///
        /// <para>Matched on FILE NAME, which is the only key the two sides share:
        /// BCC's deliverable rows come from the local
        /// <c>_BIM_COORD/deliverables.json</c> register and carry no server
        /// document GUID. That makes this a best-effort match — a deliverable
        /// whose register name differs from the uploaded file name reads as
        /// NotSynced even when a copy exists. Stated plainly rather than papered
        /// over; the fix is a document id on the register, which is a bigger
        /// change than a badge.</para>
        ///
        /// <para>WorkingCopy vs Reference comes from the read-only ATTRIBUTE the
        /// sync engine sets — a removable hint, never a lock. A user who clears
        /// it will see the badge change, which is honest: the badge reports what
        /// is on disk, not what the server wishes were on disk.</para>
        /// </summary>
        public static LocalSyncState ResolveState(string projectCode, string fileName)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(fileName)) return LocalSyncState.NotSynced;
                string folder = CompanionPaths.ResolveProjectFolder(projectCode);
                if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
                    return LocalSyncState.NotSynced;

                string path = Path.Combine(folder, Path.GetFileName(fileName));
                if (!File.Exists(path))
                {
                    // The register often carries a name without an extension (a
                    // document code rather than a file). Fall back to a prefix
                    // match so those rows still badge rather than all reading
                    // NotSynced, which would make the column useless.
                    string stem = Path.GetFileNameWithoutExtension(fileName);
                    if (string.IsNullOrWhiteSpace(stem)) return LocalSyncState.NotSynced;
                    foreach (var candidate in Directory.EnumerateFiles(folder, stem + ".*"))
                    {
                        // Never match a superseded copy — it is history, not the
                        // live document, and badging a row from it would say
                        // "you have this" about a file that is no longer current.
                        if (Path.GetFileName(candidate).IndexOf("(superseded ", StringComparison.Ordinal) >= 0)
                            continue;
                        path = candidate;
                        break;
                    }
                    if (!File.Exists(path)) return LocalSyncState.NotSynced;
                }

                var attrs = File.GetAttributes(path);
                return (attrs & FileAttributes.ReadOnly) == FileAttributes.ReadOnly
                    ? LocalSyncState.Reference
                    : LocalSyncState.WorkingCopy;
            }
            catch (Exception ex)
            {
                StingLog.Warn($"Companion local state for '{fileName}': {ex.Message}");
                return LocalSyncState.NotSynced;
            }
        }

        /// <summary>Short chip label, or empty for a row with nothing to say.</summary>
        public static string BadgeLabel(LocalSyncState state)
        {
            switch (state)
            {
                case LocalSyncState.WorkingCopy: return "WORKING";
                case LocalSyncState.Reference: return "REF";
                default: return "";
            }
        }

        /// <summary>Tooltip for the chip. Spells out the read-only hint, since that is the part users query.</summary>
        public static string BadgeTooltip(LocalSyncState state)
        {
            switch (state)
            {
                case LocalSyncState.WorkingCopy:
                    return "Synced to this machine as an editable working copy (WIP).";
                case LocalSyncState.Reference:
                    return "Synced to this machine as a read-only reference copy.\n"
                         + "The read-only flag is a hint, not a lock — you can clear it, "
                         + "but a newer revision will still replace this file.";
                default:
                    return "No local copy on this machine.";
            }
        }
    }
}
