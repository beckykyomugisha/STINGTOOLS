// StingTools/V6/AccProjectScope.cs
//
// IM-18. Values that identify a PROJECT - the ACC Issues container (ProjectId), the
// Model Coordination container (CoordContainerId), the hub, the upload folder, the clash
// distance unit, the issue type and the hosting region - lived in the machine-wide
// %APPDATA%\Planscape\acc_credentials.json beside the OAuth secrets. A coordinator on two
// jobs had one value for both, and the second job silently overwrote the first. The
// folder URN was the dangerous one: switching jobs pointed uploads at the previous
// project's folder.
//
// They now belong in <project>/_BIM_COORD/acc/acc_settings.json. The credentials file's
// copies are still read when the project file has none, and every such read says so in
// the log - a fallback nobody can see is how a field ends up with two homes forever.
//
// Two rules make the move safe:
//   * Saving credentials never writes a project's values into the machine file. The
//     values the file held are restored on save.
//   * IssueTypeId / IssueSubtypeId are resolved PER CONTAINER. When the project's
//     container differs from the one the machine file cached them for, they are cleared
//     in memory so the type is resolved for the right one.
//
// Revit-free and log-free (links into StingTools.Acc.Tests); callers log Describe().

using System.Collections.Generic;

namespace StingTools.V6
{
    public enum AccProjectScopeSource
    {
        /// <summary>No container id anywhere.</summary>
        None,
        /// <summary>From the project's acc_settings.json - the intended home.</summary>
        ProjectSettings,
        /// <summary>From the machine credentials file - deprecated fallback.</summary>
        CredentialsFile,
    }

    public static class AccProjectScope
    {
        /// <summary>Overlay the project's values onto <paramref name="c"/>, remembering what
        /// the credentials file held so <see cref="AccIssueSync.SaveCredentials(AccCredentials)"/>
        /// can put it back. <paramref name="projectSettings"/> may be null (no saved project).</summary>
        public static AccCredentials Apply(AccCredentials c, AccOperatingPolicy projectSettings)
        {
            if (c == null) return null;
            c.FileProjectId = c.ProjectId;
            c.FileCoordContainerId = c.CoordContainerId;
            c.FileIssueTypeId = c.IssueTypeId;
            c.FileIssueSubtypeId = c.IssueSubtypeId;
            c.FileHubId = c.HubId;
            c.FileFolderUrn = c.FolderUrn;
            c.FileDistToMm = c.DistToMm;
            c.FileRegion = c.Region;
            c.ScopeApplied = true;

            string pid = projectSettings?.ProjectId ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(pid))
            {
                bool otherContainer = !string.Equals(AccIds.ForAcc(pid), AccIds.ForAcc(c.ProjectId),
                    System.StringComparison.OrdinalIgnoreCase);
                c.ProjectId = pid;
                c.CoordContainerId = projectSettings.CoordContainerId ?? string.Empty;
                if (otherContainer)
                {
                    // Cached for the machine file's container, not this one.
                    c.IssueTypeId = string.Empty;
                    c.IssueSubtypeId = string.Empty;
                    // A folder and a hub belong to one project too.
                    c.FolderUrn = string.Empty;
                    c.HubId = string.Empty;
                }
                c.ProjectScope = AccProjectScopeSource.ProjectSettings;
            }
            else
            {
                c.ProjectScope = string.IsNullOrWhiteSpace(c.ProjectId)
                    ? AccProjectScopeSource.None
                    : AccProjectScopeSource.CredentialsFile;
            }

            if (projectSettings != null)
            {
                if (!string.IsNullOrWhiteSpace(projectSettings.HubId)) c.HubId = projectSettings.HubId;
                if (!string.IsNullOrWhiteSpace(projectSettings.FolderUrn)) c.FolderUrn = projectSettings.FolderUrn;
                if (projectSettings.DistToMm.HasValue) c.DistToMm = projectSettings.DistToMm.Value;
                if (!string.IsNullOrWhiteSpace(projectSettings.Region)) c.Region = projectSettings.Region;
                if (!string.IsNullOrWhiteSpace(projectSettings.IssueTypeId))
                {
                    c.IssueTypeId = projectSettings.IssueTypeId;
                    c.IssueSubtypeId = projectSettings.IssueSubtypeId ?? string.Empty;
                }
            }
            return c;
        }

        /// <summary>Project values still being taken from the machine file - each one a
        /// deprecation warning the caller should log.</summary>
        public static IReadOnlyList<string> MachineFileFallbacks(AccCredentials c, AccOperatingPolicy projectSettings)
        {
            var list = new List<string>();
            if (c == null) return list;
            if (c.ProjectScope == AccProjectScopeSource.CredentialsFile) list.Add("projectId");
            if (!string.IsNullOrWhiteSpace(c.FolderUrn) && string.IsNullOrWhiteSpace(projectSettings?.FolderUrn)) list.Add("folderUrn");
            if (!string.IsNullOrWhiteSpace(c.HubId) && string.IsNullOrWhiteSpace(projectSettings?.HubId)) list.Add("hubId");
            return list;
        }

        /// <summary>One line for the log / a dialog.</summary>
        public static string Describe(AccCredentials c)
        {
            switch (c?.ProjectScope)
            {
                case AccProjectScopeSource.ProjectSettings:
                    return $"ACC container {c.ProjectId} from this project's acc_settings.json";
                case AccProjectScopeSource.CredentialsFile:
                    return $"ACC container {c.ProjectId} from the machine credentials file - DEPRECATED: " +
                           "set it per project (BIM Coordination Center > ACC > Save) so another job " +
                           "cannot overwrite it";
                default:
                    return "no ACC container id is configured";
            }
        }
    }
}
