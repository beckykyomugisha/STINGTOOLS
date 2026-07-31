using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Planscape.Companion;

/// <summary>
/// One project this machine syncs.
///
/// Explicit camelCase <see cref="JsonPropertyAttribute"/> names throughout: the
/// top-level keys are written by hand and are camelCase, and without these the
/// nested objects would serialise PascalCase, leaving one file with two
/// conventions. It is a file people open and edit by hand — consistency is the
/// whole point of following the existing settings-file pattern.
/// </summary>
internal sealed class LinkedProject
{
    [JsonProperty("projectId")]
    public string ProjectId { get; set; } = "";

    /// <summary>Used for the folder name — <c>%USERPROFILE%\Planscape\{ProjectCode}\</c>.</summary>
    [JsonProperty("projectCode")]
    public string ProjectCode { get; set; } = "";

    /// <summary>
    /// The per-project auto/manual toggle from the design, on by default. Off
    /// means the project stays linked but nothing happens until someone triggers
    /// a sync — the toggle gates only whether the PUSH is allowed to fire, never
    /// which code path runs.
    /// </summary>
    [JsonProperty("autoSync")]
    public bool AutoSync { get; set; } = true;

    /// <summary>Per-project folder override. Null = the default convention.</summary>
    [JsonProperty("folderOverride")]
    public string? FolderOverride { get; set; }

    /// <summary>
    /// High-water mark for the delta query. Always a value the SERVER gave us
    /// (<c>serverTimeUtc</c>), never this machine's clock — a laptop running fast
    /// would otherwise ask for changes since an instant that has not happened and
    /// silently sync nothing for as long as the skew lasts.
    /// </summary>
    [JsonProperty("lastSyncUtc")]
    public DateTime? LastSyncUtc { get; set; }
}

/// <summary>
/// Per-machine Companion settings.
///
/// Deliberately the same shape, directory and library as
/// <c>PlanscapeServerClient.MachineSettingsPath</c>
/// (<c>%APPDATA%\StingTools\planscape_server.json</c>): read-merge-write over a
/// <see cref="JObject"/> so an unknown key written by a newer build survives an
/// older one. A different settings mechanism for the same product would be a
/// second thing to find, back up and support.
///
/// A SEPARATE FILE from the plugin's, though. Two processes writing one file
/// race, and the plugin's file is the server URL that the Companion only ever
/// reads.
/// </summary>
internal sealed class CompanionSettings
{
    public string? ServerUrl { get; set; }

    /// <summary>
    /// A personal access token (see /settings/tokens in the web app), exchanged
    /// for a JWT at <c>POST /api/auth/token/exchange</c>.
    ///
    /// A PAT rather than a stored password: it is scoped to one user, revocable
    /// from the web app without a password change, and expires on its own. The
    /// same choice StingBridge made, for the same reasons.
    /// </summary>
    public string? AccessToken { get; set; }

    /// <summary>Global root override. Null = <c>%USERPROFILE%\Planscape</c>.</summary>
    public string? RootFolder { get; set; }

    public List<LinkedProject> Projects { get; set; } = new();

    // ── Location ──────────────────────────────────────────────────────────────

    public static string SettingsDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "StingTools");

    public static string SettingsPath => Path.Combine(SettingsDir, "planscape_sync.json");

    /// <summary>
    /// The default sync root: <c>%USERPROFILE%\Planscape</c>.
    ///
    /// Deliberately NOT under %APPDATA%. An Author has to find these files from
    /// Explorer and from Revit's own file-open dialog; a path nobody can navigate
    /// to by habit is a path they will copy files out of.
    /// </summary>
    public static string DefaultRoot => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Planscape");

    /// <summary>Resolved sync folder for a project: override → global root → default.</summary>
    public string FolderFor(LinkedProject project)
    {
        if (!string.IsNullOrWhiteSpace(project.FolderOverride)) return project.FolderOverride!;
        var root = string.IsNullOrWhiteSpace(RootFolder) ? DefaultRoot : RootFolder!;
        return Path.Combine(root, SanitiseFolderName(project.ProjectCode));
    }

    /// <summary>
    /// A project code is user-supplied and travels into a path. Strip what
    /// Windows refuses plus the traversal characters, so a project coded
    /// <c>..\..\Windows</c> cannot write outside the sync root.
    /// </summary>
    public static string SanitiseFolderName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "project";
        var cleaned = new string(name
            .Where(c => !Path.GetInvalidFileNameChars().Contains(c) && c != '.' || char.IsLetterOrDigit(c))
            .ToArray()).Trim();
        return string.IsNullOrWhiteSpace(cleaned) ? "project" : cleaned;
    }

    // ── Load / save ───────────────────────────────────────────────────────────

    public static CompanionSettings Load()
    {
        try
        {
            if (!File.Exists(SettingsPath)) return new CompanionSettings();
            var o = JObject.Parse(File.ReadAllText(SettingsPath));
            return new CompanionSettings
            {
                ServerUrl = o["serverUrl"]?.Value<string>(),
                AccessToken = o["accessToken"]?.Value<string>(),
                RootFolder = o["rootFolder"]?.Value<string>(),
                Projects = o["projects"]?.ToObject<List<LinkedProject>>() ?? new List<LinkedProject>(),
            };
        }
        catch (Exception ex)
        {
            // A corrupt settings file must not stop the app from starting — it
            // would take the tray icon with it and leave no way to fix anything.
            CompanionLog.Error($"settings unreadable, starting with defaults ({SettingsPath})", ex);
            return new CompanionSettings();
        }
    }

    /// <summary>
    /// Merge-and-write, preserving unknown keys — the same read-modify-write
    /// discipline PlanscapeServerClient.SaveDefaultServerUrl uses.
    /// </summary>
    public void Save()
    {
        try
        {
            Directory.CreateDirectory(SettingsDir);
            var o = File.Exists(SettingsPath)
                ? JObject.Parse(File.ReadAllText(SettingsPath))
                : new JObject();

            if (ServerUrl != null) o["serverUrl"] = ServerUrl;
            if (AccessToken != null) o["accessToken"] = AccessToken;
            o["rootFolder"] = RootFolder;
            o["projects"] = JArray.FromObject(Projects);
            o["updatedUtc"] = DateTime.UtcNow.ToString("o");

            // Write via a temp file + replace: a power cut mid-write would
            // otherwise leave a half-written JSON file, and the catch in Load()
            // would silently discard every linked project.
            var tmp = SettingsPath + ".tmp";
            File.WriteAllText(tmp, o.ToString(Formatting.Indented));
            File.Move(tmp, SettingsPath, overwrite: true);
        }
        catch (Exception ex)
        {
            CompanionLog.Error("could not save settings", ex);
        }
    }

    public LinkedProject? Find(string projectId) =>
        Projects.FirstOrDefault(p => string.Equals(p.ProjectId, projectId, StringComparison.OrdinalIgnoreCase));
}
