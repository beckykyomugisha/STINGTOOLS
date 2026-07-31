// EVERY using is explicit, including the ones ImplicitUsings would supply.
// This file is compiled into two projects with different settings — the
// Companion enables ImplicitUsings, StingTools does not — so relying on them
// builds on one side and fails on the other. (It did, exactly once.)
//
// Same reason for the #nullable directive: the Companion enables nullable
// reference types project-wide, StingTools disables them, and the `string?`
// annotations below are CS8632 warnings in a disabled context. Turning the
// context on for this one file keeps StingTools' 0-warning baseline intact
// without either project having to change its own setting.
#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Planscape.Companion;

/// <summary>
/// The Companion↔BCC wire contract, and the client half of it.
///
/// <para><b>This file is compiled into BOTH assemblies</b> — the Companion (which
/// serves it) and StingTools (which calls it), via a <c>&lt;Compile Include&gt;</c>
/// in <c>StingTools.csproj</c>. Same trick <c>StingLink.exe</c> uses to share
/// <c>PlanscapeProtocol.cs</c>, and for the same reason: a request shape defined
/// twice is a request shape that will eventually disagree with itself, silently,
/// in a direction neither side logs.</para>
///
/// <para>So it must stay free of Revit, WPF, WinForms and anything else the other
/// side cannot load. Only <c>System.IO.Pipes</c> and Newtonsoft. If a build error
/// points here, that rule is what broke — move the offending code into
/// <c>CompanionIpc.cs</c> (Companion-only) rather than adding a reference.</para>
///
/// Transport rationale is in the plan §1a: a named pipe, because the failure mode
/// that matters is telling "not running" apart from "busy" at the instant a user
/// clicks Sync now, and a pipe connect answers that in a bounded time where a file
/// drop needs a staleness heuristic.
/// </summary>
internal static class CompanionIpc
{
    /// <summary>
    /// Fixed name — no user suffix. Windows scopes a default-ACL pipe to the
    /// creating user already, and a predictable name is what lets BCC connect
    /// without a discovery file that could itself go stale.
    /// </summary>
    public const string PipeName = "planscape-companion";

    /// <summary>
    /// How long a client waits before calling it "not running". Deliberately
    /// short: this runs on a UI click, the answer is almost always instant or
    /// never, and a Revit UI thread must not be held while we find out.
    /// </summary>
    public const int ConnectTimeoutMs = 500;

    public const string CmdPing = "ping";
    public const string CmdStatus = "status";
    public const string CmdSyncNow = "sync-now";
    public const string CmdHistory = "sync-history";
}

/// <summary>
/// Where the Companion keeps things, and how a project code becomes a folder.
///
/// Shared because BCC needs the same answers to badge a document row with its
/// local state, and a second implementation of "which folder is this project in"
/// would drift the first time someone changed the override rules.
/// </summary>
internal static class CompanionPaths
{
    /// <summary>
    /// <c>%APPDATA%\StingTools</c> — the same directory
    /// <c>PlanscapeServerClient.MachineSettingsPath</c> already uses, so support
    /// asks for one folder rather than two.
    /// </summary>
    public static string SettingsDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "StingTools");

    /// <summary>A separate file from the plugin's: two processes writing one file race.</summary>
    public static string SettingsPath => Path.Combine(SettingsDir, "planscape_sync.json");

    /// <summary>
    /// <c>%USERPROFILE%\Planscape</c>. Deliberately not under %APPDATA% — an
    /// Author has to find these files from Explorer and from Revit's own
    /// file-open dialog, and a path nobody navigates to by habit is a path they
    /// copy files out of.
    /// </summary>
    public static string DefaultRoot => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Planscape");

    /// <summary>
    /// A project code is user-supplied and travels into a path. Strip what
    /// Windows refuses plus the traversal characters, so a project coded
    /// <c>..\..\Windows</c> cannot write outside the sync root.
    /// </summary>
    public static string SanitiseFolderName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "project";
        var cleaned = new string(name!
            .Where(c => (!Path.GetInvalidFileNameChars().Contains(c) && c != '.') || char.IsLetterOrDigit(c))
            .ToArray()).Trim();
        return string.IsNullOrWhiteSpace(cleaned) ? "project" : cleaned;
    }

    /// <summary>
    /// The sync folder for a project code, read straight from the settings file:
    /// per-project override → global root → default. Returns null when the file
    /// is missing or the project is not linked on this machine, which is the
    /// normal case for a project nobody has asked to sync.
    /// </summary>
    public static string? ResolveProjectFolder(string? projectCode)
    {
        if (string.IsNullOrWhiteSpace(projectCode)) return null;
        try
        {
            if (!File.Exists(SettingsPath)) return null;
            var o = JObject.Parse(File.ReadAllText(SettingsPath));

            var match = o["projects"] as JArray;
            JObject? project = null;
            foreach (var p in match ?? new JArray())
            {
                if (string.Equals(p?["projectCode"]?.Value<string>(), projectCode,
                        StringComparison.OrdinalIgnoreCase))
                {
                    project = p as JObject;
                    break;
                }
            }
            if (project == null) return null;

            var over = project["folderOverride"]?.Value<string>();
            if (!string.IsNullOrWhiteSpace(over)) return over;

            var root = o["rootFolder"]?.Value<string>();
            if (string.IsNullOrWhiteSpace(root)) root = DefaultRoot;
            return Path.Combine(root!, SanitiseFolderName(projectCode));
        }
        catch (Exception)
        {
            // A settings file mid-write, or one a newer build reshaped. Reading it
            // is a convenience for a badge; failing to is not worth surfacing.
            return null;
        }
    }
}

/// <summary>
/// A parsed <c>status</c> reply. Mirrors the Companion's own <c>SyncStatus</c>
/// (plan §1c) — the two are kept in step by this file being shared.
///
/// <see cref="Running"/> false means the pipe did not answer, which is a real and
/// expected answer rather than an error: the user may simply not have started the
/// Companion. Everything else on the object is meaningless in that case.
/// </summary>
internal sealed class CompanionStatus
{
    public bool Running { get; set; }

    /// <summary>Idle | Syncing | Offline | Error. Empty when not running.</summary>
    public string State { get; set; } = "";

    /// <summary>One line already formatted for a tooltip or status bar.</summary>
    public string Summary { get; set; } = "";

    public DateTime? LastSuccessUtc { get; set; }
    public string? LastError { get; set; }
    public DateTime? LastErrorUtc { get; set; }
    public int ConsecutiveFailures { get; set; }
    public int LinkedProjects { get; set; }
    public int FilesLastSync { get; set; }

    /// <summary>WIP working copies on this machine (Slice E). Drives the tray count
    /// and BCC's read of it.</summary>
    public int CheckedOutCount { get; set; }

    /// <summary>The file names behind <see cref="CheckedOutCount"/>, capped by the server side.</summary>
    public List<string> CheckedOut { get; set; } = new();

    /// <summary>Not running — the one state BCC must render as information, not failure.</summary>
    public static CompanionStatus NotRunning() => new()
    {
        Running = false,
        State = "",
        Summary = "Planscape Companion is not running",
    };

    /// <summary>True when a human needs to do something. Offline deliberately does NOT count.</summary>
    public bool NeedsAttention =>
        Running && string.Equals(State, "Error", StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// Client side of <see cref="CompanionIpc"/>. Used by BCC; also used by the
/// Companion's own CLI so both exercise the same path.
/// </summary>
internal static class CompanionIpcClient
{
    /// <summary>
    /// Send one command and read one reply. Returns null when the Companion is
    /// not running — a normal answer, not an exception, because "the user hasn't
    /// started it" is the single most likely outcome and must not surface as a
    /// stack trace inside Revit.
    /// </summary>
    public static async Task<JObject?> SendAsync(object request, CancellationToken ct = default)
    {
        try
        {
            using var pipe = new NamedPipeClientStream(
                ".", CompanionIpc.PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
            await pipe.ConnectAsync(CompanionIpc.ConnectTimeoutMs, ct);

            var writer = new StreamWriter(pipe, new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true };
            var reader = new StreamReader(pipe, Encoding.UTF8, leaveOpen: true);

            await writer.WriteLineAsync(JsonConvert.SerializeObject(request));
            var line = await reader.ReadLineAsync(ct);
            return string.IsNullOrWhiteSpace(line) ? null : JObject.Parse(line);
        }
        catch (TimeoutException) { return null; }              // not running
        catch (IOException) { return null; }                    // died mid-request
        catch (UnauthorizedAccessException) { return null; }    // another user's pipe
        catch (OperationCanceledException) { return null; }
        catch (JsonReaderException) { return null; }            // garbage on the wire
    }

    /// <summary>Is the Companion up? Cheap and side-effect free.</summary>
    public static async Task<bool> IsRunningAsync(CancellationToken ct = default)
        => (await SendAsync(new { cmd = CompanionIpc.CmdPing }, ct))?["ok"]?.Value<bool>() == true;

    /// <summary>Read the live status. Never throws; never returns null.</summary>
    public static async Task<CompanionStatus> GetStatusAsync(CancellationToken ct = default)
    {
        var reply = await SendAsync(new { cmd = CompanionIpc.CmdStatus }, ct);
        if (reply == null || reply["ok"]?.Value<bool>() != true) return CompanionStatus.NotRunning();

        return new CompanionStatus
        {
            Running = true,
            State = reply["state"]?.Value<string>() ?? "",
            Summary = reply["summary"]?.Value<string>() ?? "",
            LastSuccessUtc = reply["lastSuccessUtc"]?.Value<DateTime?>(),
            LastError = reply["lastError"]?.Value<string>(),
            LastErrorUtc = reply["lastErrorUtc"]?.Value<DateTime?>(),
            ConsecutiveFailures = reply["consecutiveFailures"]?.Value<int>() ?? 0,
            LinkedProjects = reply["linkedProjects"]?.Value<int>() ?? 0,
            FilesLastSync = reply["filesLastSync"]?.Value<int>() ?? 0,
            CheckedOutCount = reply["checkedOutCount"]?.Value<int>() ?? 0,
            CheckedOut = reply["checkedOut"]?.ToObject<List<string>>() ?? new List<string>(),
        };
    }

    /// <summary>
    /// Ask for a sync. <paramref name="projectId"/> null = every linked project.
    ///
    /// Returns false only when the Companion is not running. A true means the
    /// sync was STARTED, not finished — the Companion answers immediately and
    /// works in the background, because holding the pipe open for a multi-minute
    /// download would block the caller's UI thread, which is the freeze this
    /// whole out-of-process design exists to avoid.
    /// </summary>
    public static async Task<bool> SyncNowAsync(string? projectId = null, CancellationToken ct = default)
    {
        var reply = await SendAsync(new { cmd = CompanionIpc.CmdSyncNow, projectId }, ct);
        return reply?["ok"]?.Value<bool>() == true;
    }

    /// <summary>
    /// Slice E — pull the FULL version history of one document, deliberately.
    /// Never a default: history is opt-in per document so it cannot silently grow
    /// on every machine. Returns false only when the Companion is not running.
    /// </summary>
    public static async Task<bool> DownloadHistoryAsync(
        string projectId, string documentId, CancellationToken ct = default)
    {
        var reply = await SendAsync(
            new { cmd = CompanionIpc.CmdHistory, projectId, documentId }, ct);
        return reply?["ok"]?.Value<bool>() == true;
    }
}
