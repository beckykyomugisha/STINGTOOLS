// WorksharedProjectRoot.cs — where a file-based workshared LOCAL COPY keeps its STING root
// (ACC-HARD-3b).
//
// WHY THIS EXISTS
//
// With file-based worksharing each user opens a LOCAL COPY of the central model, and the
// local copy normally sits in that user's Documents folder. Document.PathName is the local
// copy, so <rvtDir>/<CODE> was a different folder for every user: the issue register, SEQ
// counters and the rest of _data/coord split per user exactly as a cloud model's did.
//
// THE RULE — never moves an existing project
//
//   * Not workshared, the central itself, or a cloud model      → not this file's business.
//   * Already stamped beside the local copy (legacy ES stamp),
//     or a root already exists beside the local copy            → unchanged (NotApplicable).
//     Moving it is the consented Cloud_SetProjectRoot → "Move to shared root" path.
//   * Greenfield (no stamp, no existing root), or a "central:" stamp:
//       mapping hit for this central                            → that folder
//       central folder reachable                                → <centralDir>/<CODE>
//       unreachable, interactive                                → ask
//       unreachable, unattended                                 → refuse (never the local copy)
//
// The ES stamp for a central-rooted project is written "central:<relative path>" — relative
// to the CENTRAL folder, so it means the same thing in every user's local copy (the stamp is
// model data and syncs through central). An unprefixed stamp keeps its old meaning.
//
// Pure: no Revit, no UI, no logging, no writes.

using System;
using System.IO;

namespace StingTools.Core
{
    /// <summary>What the ES root stamp says, as far as this decision cares.</summary>
    public enum RootStampKind
    {
        None,
        /// <summary>A legacy stamp, relative to the model's own folder.</summary>
        Local,
        /// <summary>A "central:" stamp, relative to the central model's folder.</summary>
        Central,
    }

    public sealed class WorksharedRootInputs
    {
        public bool IsCloud;
        public bool IsWorkshared;
        /// <summary>Document.PathName — the local copy.</summary>
        public string LocalPath;
        /// <summary>User-visible central model path (GetWorksharingCentralModelPath).</summary>
        public string CentralPath;
        public string ProjectCode;
        public RootStampKind Stamp;
        /// <summary>For a Central stamp, the path after "central:".</summary>
        public string CentralStampRelative;
        /// <summary>A STING root already exists beside the local copy (folder or setup file).</summary>
        public bool HasExistingLocalRoot;
        /// <summary>Raw text of cloud_project_roots.json; null when absent.</summary>
        public string MappingJson;
        public bool Interactive;
        public Func<string, bool> DirectoryExists;
    }

    public enum WorksharedRootKind { NotApplicable, Central, Mapped, PromptUser, Refuse }

    public sealed class WorksharedRootDecision
    {
        public WorksharedRootKind Kind;
        public string Root;
        public string Key;
        public string CentralDir;
        public string Reason;
        public bool MappingMalformed;
        public override string ToString() => $"{Kind} root={Root ?? "-"} central={CentralDir ?? "-"} — {Reason}";
    }

    public static class WorksharedProjectRoot
    {
        public const string CentralStampPrefix = "central:";
        public const string CentralKeyPrefix = "central:";

        /// <summary>Split an ES stamp into its kind and relative path.</summary>
        public static RootStampKind ClassifyStamp(string stamp, out string relative)
        {
            relative = null;
            if (string.IsNullOrWhiteSpace(stamp)) return RootStampKind.None;
            if (stamp.StartsWith(CentralStampPrefix, StringComparison.OrdinalIgnoreCase))
            {
                relative = stamp.Substring(CentralStampPrefix.Length).Trim();
                return RootStampKind.Central;
            }
            relative = stamp;
            return RootStampKind.Local;
        }

        private static string Norm(string p)
        {
            if (string.IsNullOrWhiteSpace(p)) return "";
            string s = p.Trim().Replace('/', '\\').TrimEnd('\\');
            try { if (Path.IsPathFullyQualified(s)) s = Path.GetFullPath(s); } catch { }
            return s.ToLowerInvariant();
        }

        /// <summary>True when the open file is a local copy of a DIFFERENT central file.</summary>
        public static bool IsLocalCopy(string localPath, string centralPath)
        {
            if (string.IsNullOrWhiteSpace(localPath) || string.IsNullOrWhiteSpace(centralPath)) return false;
            if (CloudProjectRoot.LooksLikeCloudPath(localPath)) return false;
            return Norm(localPath) != Norm(centralPath);
        }

        /// <summary>The central model's folder; null for a server path ("RSN://…") which has none.</summary>
        public static string CentralDirOf(string centralPath)
        {
            if (string.IsNullOrWhiteSpace(centralPath) || CloudProjectRoot.LooksLikeCloudPath(centralPath)) return null;
            try { return Path.GetDirectoryName(centralPath.Trim()); } catch { return null; }
        }

        /// <summary>Mapping key for a central model: "central:" + its normalised path.</summary>
        public static string KeyFor(string centralPath)
            => string.IsNullOrWhiteSpace(centralPath) ? null : CentralKeyPrefix + Norm(centralPath);

        /// <summary>The shared root for a reachable central folder.</summary>
        public static string CentralRoot(string centralDir, string projectCode, string stampRelative)
        {
            if (string.IsNullOrEmpty(centralDir)) return null;
            string rel = !string.IsNullOrWhiteSpace(stampRelative) ? stampRelative : projectCode;
            if (string.IsNullOrWhiteSpace(rel) || rel.IndexOf(':') >= 0 || Path.IsPathRooted(rel)) rel = projectCode;
            if (string.IsNullOrWhiteSpace(rel)) return null;
            try { return Path.GetFullPath(Path.Combine(centralDir, rel)); }
            catch { return Path.Combine(centralDir, rel); }
        }

        public static WorksharedRootDecision Decide(WorksharedRootInputs i)
        {
            WorksharedRootDecision Na(string why) => new WorksharedRootDecision { Kind = WorksharedRootKind.NotApplicable, Reason = why };

            if (i == null) return Na("no inputs");
            if (i.IsCloud) return Na("cloud model — CloudProjectRoot decides");
            if (!i.IsWorkshared) return Na("not workshared — local resolution applies");
            if (!IsLocalCopy(i.LocalPath, i.CentralPath))
                return Na("this is the central model itself (or it has no central path) — root beside it as before");
            if (i.Stamp == RootStampKind.Local)
                return Na("already stamped beside the local copy — kept as is (move it with Cloud_SetProjectRoot)");
            if (i.Stamp == RootStampKind.None && i.HasExistingLocalRoot)
                return Na("a STING root already exists beside the local copy — kept as is (move it with Cloud_SetProjectRoot)");

            string key = KeyFor(i.CentralPath);
            string centralDir = CentralDirOf(i.CentralPath);
            var exists = i.DirectoryExists ?? Directory.Exists;

            var map = CloudProjectRoot.ParseMapping(i.MappingJson, out string parseError);
            if (map == null)
                return new WorksharedRootDecision
                {
                    Kind = WorksharedRootKind.Refuse, Key = key, CentralDir = centralDir, MappingMalformed = true,
                    Reason = $"{CloudProjectRoot.MappingFileName} cannot be read: {parseError}. Fix or remove it; STING will not overwrite it",
                };

            if (key != null && map.TryGetValue(key, out string mapped)
                && CloudProjectRoot.IsUsableFolder(mapped, out _) && exists(mapped))
                return new WorksharedRootDecision
                {
                    Kind = WorksharedRootKind.Mapped, Key = key, CentralDir = centralDir, Root = mapped,
                    Reason = $"mapped in {CloudProjectRoot.MappingFileName}",
                };

            if (centralDir != null && exists(centralDir))
            {
                string root = CentralRoot(centralDir, i.ProjectCode,
                    i.Stamp == RootStampKind.Central ? i.CentralStampRelative : null);
                if (root != null)
                    return new WorksharedRootDecision
                    {
                        Kind = WorksharedRootKind.Central, Key = key, CentralDir = centralDir, Root = root,
                        Reason = i.Stamp == RootStampKind.Central ? "central stamp, beside the central model"
                                                                  : "new project — root beside the central model, shared by every local copy",
                    };
            }

            string problem = centralDir == null
                ? $"the central model '{i.CentralPath}' has no folder on disk (server path)"
                : $"the central model's folder '{centralDir}' is not reachable from this machine";
            return new WorksharedRootDecision
            {
                Kind = i.Interactive ? WorksharedRootKind.PromptUser : WorksharedRootKind.Refuse,
                Key = key, CentralDir = centralDir,
                Reason = i.Interactive
                    ? problem + " — asking the user to choose the shared folder"
                    : problem + " — nobody to ask (unattended), so STING refuses rather than use the local copy's folder",
            };
        }
    }
}
