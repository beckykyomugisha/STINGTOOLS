using System.Text;

namespace Planscape.Companion;

/// <summary>
/// The Companion's only diagnostic surface.
///
/// A tray app has no console and, by design (see the plan's §1c), does not
/// toast. When something goes wrong the log IS the evidence, so it is written
/// eagerly rather than buffered — a crash must not take the last few lines with
/// it, and those are exactly the lines that explain the crash.
///
/// Also mirrored to stdout when a console is attached, which is what makes
/// <c>--diagnose</c> usable from a terminal.
/// </summary>
internal static class CompanionLog
{
    private static readonly object Gate = new();
    private static bool _echoToConsole;

    /// <summary>%APPDATA%\StingTools\companion.log — beside the settings file
    /// StingTools already writes, so support asks for one folder, not two.</summary>
    public static string Path => System.IO.Path.Combine(CompanionSettings.SettingsDir, "companion.log");

    /// <summary>Turned on by --diagnose so a terminal run shows its work.</summary>
    public static void EchoToConsole() => _echoToConsole = true;

    public static void Info(string message) => Write("INFO ", message);
    public static void Warn(string message) => Write("WARN ", message);

    public static void Error(string message, Exception? ex = null) =>
        Write("ERROR", ex == null ? message : $"{message} — {ex.GetType().Name}: {ex.Message}");

    private static void Write(string level, string message)
    {
        var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [{level}] {message}";
        if (_echoToConsole)
        {
            try { Console.WriteLine(line); } catch { /* no console attached */ }
        }
        lock (Gate)
        {
            try
            {
                Directory.CreateDirectory(CompanionSettings.SettingsDir);
                // Roll at 2 MB. A sync loop that fails every 30 s for a fortnight
                // would otherwise quietly fill a user's disk — the exact kind of
                // background misbehaviour nobody notices until it matters.
                var path = Path;
                if (File.Exists(path) && new FileInfo(path).Length > 2 * 1024 * 1024)
                {
                    var old = path + ".1";
                    File.Delete(old);
                    File.Move(path, old);
                }
                File.AppendAllText(path, line + Environment.NewLine, Encoding.UTF8);
            }
            catch
            {
                // Logging must never be the thing that breaks the app. If the log
                // cannot be written there is nowhere left to report that fact.
            }
        }
    }
}
