// StingTools/V6/AccProjectScope.cs
//
// IM-18. The ACC Issues container (ProjectId) and the Model Coordination container
// (CoordContainerId) identify a PROJECT, but they lived in the machine-wide
// %APPDATA%\Planscape\acc_credentials.json beside the OAuth secrets. A coordinator
// on two jobs had one value for both, and the second job silently overwrote the
// first.
//
// They now belong in <project>/_BIM_COORD/acc/acc_settings.json (projectId /
// coordContainerId). For one release the credentials file's copies are still read
// when the project file has none, and every such read says so in the log - a
// fallback nobody can see is how a field ends up with two homes forever.
//
// Two rules make the move safe:
//   * Saving credentials never writes a project's ids into the machine file. The
//     values the file held are restored on save when the project scope applied.
//   * IssueTypeId / IssueSubtypeId are resolved PER CONTAINER. When the project's
//     container differs from the one the machine file cached them for, they are
//     cleared in memory so EnsureIssueTypeAsync resolves them for the right one.
//
// Revit-free and log-free (links into StingTools.Acc.Tests); callers log Describe().

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
        /// <summary>Overlay the project's container ids onto <paramref name="c"/>, remembering
        /// what the credentials file held so <see cref="AccIssueSync.SaveCredentials"/> can put
        /// it back. <paramref name="projectSettings"/> may be null (no saved project).</summary>
        public static AccCredentials Apply(AccCredentials c, AccOperatingPolicy projectSettings)
        {
            if (c == null) return null;
            c.FileProjectId = c.ProjectId;
            c.FileCoordContainerId = c.CoordContainerId;
            c.FileIssueTypeId = c.IssueTypeId;
            c.FileIssueSubtypeId = c.IssueSubtypeId;

            string pid = projectSettings?.ProjectId ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(pid))
            {
                bool otherContainer = !string.Equals(pid, c.ProjectId, System.StringComparison.OrdinalIgnoreCase);
                c.ProjectId = pid;
                c.CoordContainerId = projectSettings.CoordContainerId ?? string.Empty;
                if (otherContainer)
                {
                    // Cached for the machine file's container, not this one.
                    c.IssueTypeId = string.Empty;
                    c.IssueSubtypeId = string.Empty;
                }
                c.ProjectScope = AccProjectScopeSource.ProjectSettings;
            }
            else
            {
                c.ProjectScope = string.IsNullOrWhiteSpace(c.ProjectId)
                    ? AccProjectScopeSource.None
                    : AccProjectScopeSource.CredentialsFile;
            }
            return c;
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
