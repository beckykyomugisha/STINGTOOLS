using Newtonsoft.Json;

namespace Planscape.Companion;

/// <summary>
/// What the Companion is doing, and whether anyone needs to care.
///
/// The distinction that carries the whole error-visibility decision (plan §1c):
/// <b>Offline is not an error.</b> A closed laptop, a dropped VPN and a
/// restarting server all produce a failed sync, all resolve themselves on
/// reconnect, and all happen constantly. Treating them as errors — a red icon, a
/// toast — trains the user to ignore the indicator, and then a genuinely broken
/// sync goes unnoticed too.
///
/// So only <see cref="SyncState.Error"/> (auth rejected, folder unwritable, disk
/// full) changes the tray icon. Offline is a quiet, muted state.
/// </summary>
internal enum SyncState
{
    /// <summary>Connected, nothing to do.</summary>
    Idle,

    /// <summary>A sync pass is running right now.</summary>
    Syncing,

    /// <summary>Cannot reach the server. Expected, self-healing, quiet.</summary>
    Offline,

    /// <summary>Something a human has to fix. This is the one that shouts.</summary>
    Error,
}

internal sealed class SyncStatus
{
    public SyncState State { get; set; } = SyncState.Offline;
    public DateTime? LastSuccessUtc { get; set; }
    public string? LastError { get; set; }
    public DateTime? LastErrorUtc { get; set; }
    public int ConsecutiveFailures { get; set; }
    public int LinkedProjects { get; set; }
    public int FilesLastSync { get; set; }

    /// <summary>
    /// Slice E — WIP working copies on this machine, across every linked project.
    /// Recomputed from disk after each sync pass (see SyncEngine.WorkingCopiesIn),
    /// so it cannot drift from what is actually there.
    /// </summary>
    public int CheckedOutCount { get; set; }

    /// <summary>
    /// The file names behind <see cref="CheckedOutCount"/>, for the tray's
    /// click-to-expand list. Capped when stored — the tray shows a short list,
    /// not a file manager.
    /// </summary>
    public List<string> CheckedOut { get; set; } = new();

    /// <summary>Set when the app starts, so a stale status file is recognisable.</summary>
    public DateTime StartedUtc { get; set; } = DateTime.UtcNow;

    /// <summary>Separate from the settings file: this is written on every state
    /// change, that one is written rarely. One file for both would mean a
    /// high-frequency writer racing the record of which projects are linked.</summary>
    public static string StatusPath => Path.Combine(CompanionSettings.SettingsDir, "companion_status.json");

    /// <summary>
    /// Persisted so a failure at 17:00 is still visible at 09:00 the next day,
    /// even though the Companion restarted at login in between. An error a user
    /// never saw is an error that never got fixed.
    /// </summary>
    public void Save()
    {
        try
        {
            Directory.CreateDirectory(CompanionSettings.SettingsDir);
            var tmp = StatusPath + ".tmp";
            File.WriteAllText(tmp, JsonConvert.SerializeObject(this, Formatting.Indented));
            File.Move(tmp, StatusPath, overwrite: true);
        }
        catch (Exception ex)
        {
            CompanionLog.Warn($"could not save status: {ex.Message}");
        }
    }

    public static SyncStatus Load()
    {
        try
        {
            if (File.Exists(StatusPath))
            {
                var loaded = JsonConvert.DeserializeObject<SyncStatus>(File.ReadAllText(StatusPath));
                if (loaded != null)
                {
                    // The previous run's live state is meaningless now; its
                    // FAILURE record is not, so that part carries over.
                    loaded.State = SyncState.Offline;
                    loaded.StartedUtc = DateTime.UtcNow;
                    return loaded;
                }
            }
        }
        catch (Exception ex)
        {
            CompanionLog.Warn($"could not read status: {ex.Message}");
        }
        return new SyncStatus();
    }

    /// <summary>One line for the tray tooltip. Kept under the 63-character limit
    /// Windows silently truncates NotifyIcon.Text at.</summary>
    public string Summary()
    {
        // The checked-out count is the at-a-glance number the design asks the
        // tray to carry, so it rides on every non-error state. It is deliberately
        // dropped from the Error text: when something needs fixing, the failure
        // is the message, and a count competing with it costs the one line
        // Windows gives a tooltip.
        var checkedOut = CheckedOutCount > 0 ? $" · {CheckedOutCount} checked out" : "";
        return State switch
        {
            SyncState.Syncing => "Planscape — syncing…" + checkedOut,
            SyncState.Idle => (LastSuccessUtc.HasValue
                ? $"Planscape — synced {LastSuccessUtc.Value.ToLocalTime():HH:mm}"
                : "Planscape — connected") + checkedOut,
            // Offline says "will retry" on purpose: it is expected and
            // self-healing, and the wording is what stops a user treating a
            // closed laptop as a fault (plan §1c).
            SyncState.Offline => "Planscape — offline, will retry" + checkedOut,
            SyncState.Error => $"Planscape — {Truncate(LastError ?? "sync failed", 40)}",
            _ => "Planscape",
        };
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..(max - 1)] + "…";
}
