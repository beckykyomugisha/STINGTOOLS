using System.Diagnostics;
using System.Threading;

namespace Planscape.Companion;

/// <summary>
/// Planscape Companion — entry point and host.
///
/// Modes:
///   (no args)             tray app; the normal start-on-login path
///   --diagnose            headless one-shot: connect, sync every linked
///                         project once, print what happened, exit
///   --install-autostart   HKCU Run registration (user scope, no elevation)
///   --uninstall-autostart remove it
///   --status              print the persisted status and exit
///   --link &lt;projectId&gt; &lt;code&gt;   link a project on this machine
///   --set-server &lt;url&gt; · --set-token &lt;pat&gt;
///
/// <para><c>--diagnose</c> exists because a tray app is otherwise unprovable: it
/// has no console, no window and no exit code worth reading. It is how this was
/// verified during development and it doubles as the first thing to ask a user to
/// run when sync "isn't working".</para>
/// </summary>
internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        // A background process must not take the session down over an unobserved
        // exception on a socket continuation.
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            CompanionLog.Error("unobserved task exception", e.Exception);
            e.SetObserved();
        };

        var mode = args.Length > 0 ? args[0].ToLowerInvariant() : "";
        try
        {
            return mode switch
            {
                "--install-autostart" => InstallAutostart(),
                "--uninstall-autostart" => UninstallAutostart(),
                "--status" => PrintStatus(),
                "--diagnose" => Diagnose().GetAwaiter().GetResult(),
                "--set-server" => SetServer(args),
                "--set-token" => SetToken(args),
                "--link" => LinkProject(args),
                "--help" or "-h" or "/?" => Help(),
                "" => RunTray(),
                _ => Help($"unknown option '{args[0]}'"),
            };
        }
        catch (Exception ex)
        {
            CompanionLog.Error($"fatal in mode '{mode}'", ex);
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }

    // ── Modes ─────────────────────────────────────────────────────────────────

    private static int Help(string? problem = null)
    {
        if (problem != null) Console.Error.WriteLine(problem);
        Console.WriteLine("""
            Planscape Companion — syncs cloud documents to this machine.

              (no arguments)          run in the system tray
              --diagnose              connect + sync once, print the result, exit
              --status                print the last recorded status
              --set-server <url>      e.g. http://localhost:5000
              --set-token <pat>       a personal access token from the web app
              --link <projectId> <code>   sync a project into <root>\<code>\
              --install-autostart     start at login (current user only)
              --uninstall-autostart   stop starting at login

            Settings:  %APPDATA%\StingTools\planscape_sync.json
            Log:       %APPDATA%\StingTools\companion.log
            """);
        return problem == null ? 0 : 2;
    }

    private static int InstallAutostart()
    {
        var changed = AutoStart.Install(out var detail);
        Console.WriteLine(changed ? $"Autostart registered: {detail}" : $"No change: {detail}");
        CompanionLog.Info($"autostart install — {(changed ? detail : "no change: " + detail)}");
        return 0;
    }

    private static int UninstallAutostart()
    {
        var changed = AutoStart.Uninstall(out var detail);
        Console.WriteLine(changed ? "Autostart removed." : $"No change: {detail}");
        return 0;
    }

    private static int PrintStatus()
    {
        var s = SyncStatus.Load();
        Console.WriteLine($"State                : {s.State}");
        Console.WriteLine($"Summary              : {s.Summary()}");
        Console.WriteLine($"Last success (UTC)   : {s.LastSuccessUtc?.ToString("O") ?? "never"}");
        Console.WriteLine($"Last error           : {s.LastError ?? "none"}");
        Console.WriteLine($"Consecutive failures : {s.ConsecutiveFailures}");
        Console.WriteLine($"Linked projects      : {s.LinkedProjects}");
        Console.WriteLine($"Settings             : {CompanionSettings.SettingsPath}");
        Console.WriteLine($"Log                  : {CompanionLog.Path}");
        return 0;
    }

    private static int SetServer(string[] args)
    {
        if (args.Length < 2) return Help("--set-server needs a URL");
        var settings = CompanionSettings.Load();
        settings.ServerUrl = args[1].TrimEnd('/');
        settings.Save();
        Console.WriteLine($"Server set to {settings.ServerUrl}");
        return 0;
    }

    private static int SetToken(string[] args)
    {
        if (args.Length < 2) return Help("--set-token needs a personal access token");
        var settings = CompanionSettings.Load();
        settings.AccessToken = args[1];
        settings.Save();
        // Never echo the token — this writes to a console that may be logged.
        Console.WriteLine("Access token saved.");
        return 0;
    }

    private static int LinkProject(string[] args)
    {
        if (args.Length < 3) return Help("--link needs a project id and a project code");
        var settings = CompanionSettings.Load();
        var existing = settings.Find(args[1]);
        if (existing != null)
        {
            existing.ProjectCode = args[2];
            // Clearing the mark makes the next sync an initial one, which is what
            // re-linking should mean.
            existing.LastSyncUtc = null;
        }
        else
        {
            settings.Projects.Add(new LinkedProject { ProjectId = args[1], ProjectCode = args[2] });
        }
        settings.Save();
        var project = settings.Find(args[1])!;
        Console.WriteLine($"Linked {args[2]} → {settings.FolderFor(project)}");
        return 0;
    }

    /// <summary>
    /// Headless one-shot. Connects, syncs every linked project once, reports, exits.
    /// Everything the tray does, minus the tray.
    /// </summary>
    private static async Task<int> Diagnose()
    {
        CompanionLog.EchoToConsole();
        CompanionLog.Info("--diagnose starting");

        var settings = CompanionSettings.Load();
        Console.WriteLine($"Settings : {CompanionSettings.SettingsPath}");
        Console.WriteLine($"Log      : {CompanionLog.Path}");
        Console.WriteLine($"Server   : {settings.ServerUrl ?? "(not set — use --set-server)"}");
        Console.WriteLine($"Token    : {(string.IsNullOrEmpty(settings.AccessToken) ? "(not set — use --set-token)" : "set")}");
        Console.WriteLine($"Root     : {(string.IsNullOrWhiteSpace(settings.RootFolder) ? CompanionSettings.DefaultRoot : settings.RootFolder)}");
        Console.WriteLine($"Projects : {settings.Projects.Count}");
        foreach (var p in settings.Projects)
            Console.WriteLine($"   • {p.ProjectCode} ({p.ProjectId}) auto={p.AutoSync} last={p.LastSyncUtc?.ToString("O") ?? "never"} → {settings.FolderFor(p)}");

        // The offline half is still worth exercising with no server configured:
        // it proves the settings round-trip and the superseded purge on real files.
        foreach (var p in settings.Projects)
            SyncEngine.PurgeExpiredSuperseded(settings.FolderFor(p));

        if (string.IsNullOrWhiteSpace(settings.ServerUrl) || string.IsNullOrWhiteSpace(settings.AccessToken))
        {
            Console.WriteLine();
            Console.WriteLine("No server or token configured — nothing to sync. Set both and re-run.");
            CompanionLog.Info("--diagnose finished (not configured)");
            return 0;
        }

        using var service = new CompanionService(settings);
        var ok = await service.RunOnceAsync(CancellationToken.None);
        Console.WriteLine();
        Console.WriteLine(service.Status.Summary());
        CompanionLog.Info("--diagnose finished");
        return ok ? 0 : 1;
    }

    /// <summary>
    /// The normal path: a tray icon and a background service, until the user
    /// exits or logs out.
    /// </summary>
    private static int RunTray()
    {
        // One Companion per user session. A second instance would open a second
        // pipe (failing), fight the first over the settings file, and download
        // everything twice.
        using var single = new Mutex(true, @"Local\PlanscapeCompanion", out var isFirst);
        if (!isFirst)
        {
            CompanionLog.Info("another Companion is already running in this session; exiting");
            return 0;
        }

        // REPAIR an existing autostart entry whose path has gone stale (the
        // deployment folder moves, and a Run entry pointing at a deleted exe
        // fails silently at login, which is the worst time to fail).
        //
        // It deliberately does NOT register itself when no entry exists. Adding
        // yourself to a user's startup because they ran you once is a change to
        // their machine that they did not ask for. Opting in is explicit —
        // `--install-autostart`, which is what the StingTools first-run hook
        // will call once Slice D lands.
        if (AutoStart.CurrentCommand() != null && AutoStart.Install(out var autoDetail))
            CompanionLog.Info($"autostart entry repaired → {autoDetail}");

        var settings = CompanionSettings.Load();
        using var service = new CompanionService(settings);
        using var tray = new CompanionTray(service);

        service.Start();
        CompanionLog.Info($"tray started (pid {Environment.ProcessId})");
        System.Windows.Forms.Application.Run();
        CompanionLog.Info("tray exiting");
        return 0;
    }
}
