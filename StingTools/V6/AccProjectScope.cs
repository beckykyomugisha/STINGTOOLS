// StingTools/V6/AccProjectScope.cs
//
// IM-18 / ACC-HARD-4. Values that identify a PROJECT - the ACC Issues container
// (ProjectId), the Model Coordination container (CoordContainerId), the hub, the upload
// folder, the clash distance unit, the issue type and the hosting region - used to live in
// the machine-wide %APPDATA%\Planscape\acc_credentials.json beside the OAuth secrets. A
// coordinator on two jobs had one value for both, and the second job silently overwrote the
// first. The folder URN was the dangerous one: switching jobs pointed uploads at the previous
// project's folder.
//
// They belong ONLY in <project>/_BIM_COORD/acc/acc_settings.json. The machine file's old
// copies are no longer used at all (the one-release fallback is retired): a project with no
// ACC settings is "not configured", whatever this machine remembers from another job. The
// old values are kept in LegacyProjectId so the ACC card can SHOW them and a person can adopt
// them with one explicit Save - adopting is a decision, never a side effect of reading.
//
// Saving credentials never writes a project's values into the machine file: whatever the file
// held is written back unchanged.
//
// Revit-free and log-free (links into StingTools.Acc.Tests); callers log Describe().

namespace StingTools.V6
{
    public enum AccProjectScopeSource
    {
        /// <summary>This project has no ACC settings: not configured.</summary>
        None,
        /// <summary>From the project's acc_settings.json - the only source.</summary>
        ProjectSettings,
    }

    public static class AccProjectScope
    {
        /// <summary>Lay the project's values over the machine credentials (which then carry only
        /// the sign-in), remembering what the machine file held so a save writes it back
        /// unchanged. <paramref name="projectSettings"/> may be null (no saved project).</summary>
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

            // Start from nothing: the machine file identifies no project.
            string legacy = c.ProjectId ?? string.Empty;
            bool sameContainerAsLegacy = false;
            c.ProjectId = string.Empty;
            c.CoordContainerId = string.Empty;
            c.HubId = string.Empty;
            c.FolderUrn = string.Empty;
            c.DistToMm = 1000.0;
            c.Region = string.Empty;

            string pid = projectSettings?.ProjectId ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(pid))
            {
                sameContainerAsLegacy = string.Equals(AccIds.ForAcc(pid), AccIds.ForAcc(legacy),
                    System.StringComparison.OrdinalIgnoreCase);
                c.ProjectId = pid;
                c.CoordContainerId = projectSettings.CoordContainerId ?? string.Empty;
                c.ProjectScope = AccProjectScopeSource.ProjectSettings;
                c.LegacyProjectId = string.Empty;
            }
            else
            {
                c.ProjectScope = AccProjectScopeSource.None;
                c.LegacyProjectId = legacy;
            }

            // An issue type cached in the machine file is per CONTAINER; it may be reused only
            // for the very container it was resolved for.
            if (!sameContainerAsLegacy)
            {
                c.IssueTypeId = string.Empty;
                c.IssueSubtypeId = string.Empty;
            }

            if (projectSettings != null && c.ProjectScope == AccProjectScopeSource.ProjectSettings)
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

        /// <summary>One line for the log / a dialog.</summary>
        public static string Describe(AccCredentials c)
        {
            if (c?.ProjectScope == AccProjectScopeSource.ProjectSettings)
                return $"ACC project {c.ProjectId} from this project's acc_settings.json";
            if (!string.IsNullOrWhiteSpace(c?.LegacyProjectId))
                return "this project has no ACC settings. This machine remembers ACC project " +
                       $"{c.LegacyProjectId} from before settings were per project — it is NOT used " +
                       "automatically. Open BIM Coordination Center > ACC: the old values are shown there; " +
                       "Save adopts them for this project, or use Find my ACC project";
            return "this project has no ACC settings — BIM Coordination Center > ACC > Find my ACC project";
        }
    }
}
