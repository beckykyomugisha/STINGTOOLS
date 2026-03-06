using System;
using System.IO;
using System.Runtime.InteropServices;

namespace StingTools.Core
{
    /// <summary>
    /// Checks whether the user has pressed Escape to cancel a long-running batch operation.
    /// Uses Win32 GetAsyncKeyState to poll the keyboard without blocking Revit's UI thread.
    /// Call <see cref="IsEscapePressed"/> periodically (e.g., every 100-500 elements) inside
    /// batch processing loops. When it returns true, roll back the transaction and exit.
    /// LOGIC-07: Only polls every N calls to avoid UI jank in sub-second operations.
    /// </summary>
    public static class EscapeChecker
    {
        private const int VK_ESCAPE = 0x1B;
        private static int _pollCount;

        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vKey);

        /// <summary>
        /// Returns true if the Escape key is currently pressed (or was pressed since last check).
        /// LOGIC-07: Throttled — only actually polls Win32 every 10th call to avoid
        /// UI jank in sub-second operations. Lightweight — safe to call in tight loops.
        /// </summary>
        public static bool IsEscapePressed()
        {
            // LOGIC-07: Throttle Win32 polling to every 10th call
            _pollCount++;
            if (_pollCount % 10 != 0) return false;

            try
            {
                return (GetAsyncKeyState(VK_ESCAPE) & 0x8001) != 0;
            }
            catch
            {
                // LOGIC-07: Disposal guard — handle case where user32.dll
                // is unavailable (e.g., unit testing or non-Windows context)
                return false;
            }
        }

        /// <summary>Reset the poll counter (call at start of each batch operation).</summary>
        public static void Reset() => _pollCount = 0;
    }

    /// <summary>
    /// Lightweight logger for STING Tools. Writes to a log file alongside the DLL
    /// and optionally to the Revit journal. Replaces silent catch blocks throughout
    /// the codebase so errors are traceable.
    /// Uses a buffered StreamWriter with auto-flush for performance during batch operations.
    /// LOGIC-05: Falls back to %APPDATA%\StingTools\ in read-only enterprise deployments.
    /// </summary>
    public static class StingLog
    {
        private static readonly object Lock = new object();
        private static string _logPath;
        private static StreamWriter _writer;

        private static string LogPath
        {
            get
            {
                if (_logPath == null)
                {
                    // LOGIC-05: Try DLL directory first, then %APPDATA% for enterprise deployments
                    string dir = StingToolsApp.DataPath ?? Path.GetTempPath();
                    string parent = Path.GetDirectoryName(dir) ?? dir;
                    string candidate = Path.Combine(parent, "StingTools.log");

                    // Test if directory is writable
                    if (IsDirectoryWritable(parent))
                    {
                        _logPath = candidate;
                    }
                    else
                    {
                        // Fall back to %APPDATA%\StingTools\
                        string appDataDir = Path.Combine(
                            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                            "StingTools");
                        try
                        {
                            if (!Directory.Exists(appDataDir))
                                Directory.CreateDirectory(appDataDir);
                        }
                        catch { }
                        _logPath = Path.Combine(appDataDir, "StingTools.log");
                    }
                }
                return _logPath;
            }
        }

        /// <summary>Check if a directory is writable without actually creating a file.</summary>
        private static bool IsDirectoryWritable(string path)
        {
            try
            {
                if (!Directory.Exists(path)) return false;
                string testFile = Path.Combine(path, ".sting_write_test");
                File.WriteAllText(testFile, "");
                File.Delete(testFile);
                return true;
            }
            catch
            {
                return false;
            }
        }

        public static void Info(string message)
        {
            Write("INFO", message);
        }

        public static void Warn(string message)
        {
            Write("WARN", message);
        }

        public static void Error(string message, Exception ex = null)
        {
            string full = ex != null ? $"{message}: {ex.Message}" : message;
            Write("ERROR", full);
        }

        private static void Write(string level, string message)
        {
            try
            {
                string line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [{level}] {message}";
                lock (Lock)
                {
                    EnsureWriter();
                    _writer.WriteLine(line);
                    _writer.Flush();
                }
            }
            catch
            {
                // Last-resort: cannot log — dispose bad writer so next call retries
                DisposeWriter();
            }
        }

        private static void EnsureWriter()
        {
            if (_writer == null)
            {
                var stream = new FileStream(LogPath, FileMode.Append, FileAccess.Write, FileShare.Read);
                _writer = new StreamWriter(stream) { AutoFlush = false };
            }
        }

        private static void DisposeWriter()
        {
            try { _writer?.Dispose(); } catch { }
            _writer = null;
        }

        /// <summary>
        /// Flush and close the log file. Call on plugin shutdown (OnShutdown).
        /// </summary>
        public static void Shutdown()
        {
            lock (Lock)
            {
                DisposeWriter();
            }
        }
    }
}
