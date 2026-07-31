using Microsoft.Win32;

namespace Planscape.Companion;

/// <summary>
/// Start-on-login registration.
///
/// <b>HKEY_CURRENT_USER only.</b> Same rule, same reasoning as
/// <c>PlanscapeProtocol.EnsureRegistered</c>: <c>HKCU\…\Run</c> needs no
/// elevation and is scoped to the signed-in user. Writing an HKLM Run entry
/// would start this process for every account on the machine, which is both a
/// machine-wide change made unattended and wrong on its own terms — sync is
/// per-user, against per-user credentials, into a per-user folder.
///
/// The design says explicitly that a machine-wide Windows Service is out of
/// scope; this is the per-user tray app it asks for instead.
/// </summary>
internal static class AutoStart
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "PlanscapeCompanion";

    public static string? CurrentCommand()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(ValueName) as string;
        }
        catch (Exception ex)
        {
            CompanionLog.Warn($"autostart read failed: {ex.Message}");
            return null;
        }
    }

    public static string ExePath()
    {
        // MainModule is the apphost .exe. Environment.ProcessPath is the same
        // thing without the extra P/Invoke, and is null only for a self-hosted
        // runtime this app never runs under.
        var path = Environment.ProcessPath;
        return string.IsNullOrEmpty(path)
            ? System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName ?? ""
            : path;
    }

    private static string CommandFor(string exe) => $"\"{exe}\"";

    /// <summary>
    /// Register, or repair a registration pointing at a path that has moved.
    /// Idempotent — returns false with a reason when nothing changed, including
    /// the common "already correct" case.
    ///
    /// Re-checked on every start for the same reason the protocol handler is:
    /// the deployment folder moves, and an autostart entry pointing at a deleted
    /// exe fails silently at login, which is the worst possible time to fail.
    /// </summary>
    public static bool Install(out string detail)
    {
        var exe = ExePath();
        if (string.IsNullOrWhiteSpace(exe) || !File.Exists(exe))
        {
            detail = $"cannot resolve own path ({exe})";
            return false;
        }

        var want = CommandFor(exe);
        if (string.Equals(CurrentCommand(), want, StringComparison.OrdinalIgnoreCase))
        {
            detail = "already registered";
            return false;
        }

        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey);
            if (key == null) { detail = "could not open HKCU Run key"; return false; }
            key.SetValue(ValueName, want);
            detail = want;
            return true;
        }
        catch (Exception ex)
        {
            detail = ex.Message;
            return false;
        }
    }

    /// <summary>Remove the entry. Never called automatically — stopping an app
    /// from starting is a decision a user makes, not one an app makes for them.</summary>
    public static bool Uninstall(out string detail)
    {
        detail = "";
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
            if (key?.GetValue(ValueName) == null) { detail = "was not registered"; return false; }
            key.DeleteValue(ValueName, throwOnMissingValue: false);
            return true;
        }
        catch (Exception ex)
        {
            detail = ex.Message;
            return false;
        }
    }
}
