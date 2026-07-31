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
    public string Summary() => State switch
    {
        SyncState.Syncing => "Planscape — syncing…",
        SyncState.Idle => LastSuccessUtc.HasValue
            ? $"Planscape — synced {LastSuccessUtc.Value.ToLocalTime():HH:mm}"
            : "Planscape — connected",
        SyncState.Offline => "Planscape — offline, will retry",
        SyncState.Error => $"Planscape — {Truncate(LastError ?? "sync failed", 40)}",
        _ => "Planscape",
    };

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..(max - 1)] + "…";
}
