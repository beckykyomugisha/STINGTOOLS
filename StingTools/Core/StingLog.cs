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
    /// Guarded for Windows-only: returns false on non-Windows platforms.
    /// </summary>
    public static class EscapeChecker
    {
        private const int VK_ESCAPE = 0x1B;

        private static readonly bool _isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vKey);

        /// <summary>
        /// Returns true if the Escape key is currently pressed (or was pressed since last check).
        /// Lightweight — safe to call in tight loops. Returns false on non-Windows platforms.
        /// Uses bitmask 0x8001: high bit = currently pressed, low bit = pressed since last call.
        /// </summary>
        public static bool IsEscapePressed()
        {
            if (!_isWindows) return false;
            try
            {
                return (GetAsyncKeyState(VK_ESCAPE) & 0x8001) != 0;
            }
            catch
            {
                return false;
            }
        }
    }

    /// <summary>
    /// Lightweight logger for STING Tools. Writes to a log file alongside the DLL
    /// and optionally to the Revit journal. Replaces silent catch blocks throughout
    /// the codebase so errors are traceable.
    /// Uses a buffered StreamWriter with auto-flush for performance during batch operations.
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
                    string dir = StingToolsApp.DataPath ?? Path.GetTempPath();
                    string parent = Path.GetDirectoryName(dir) ?? dir;
                    _logPath = Path.Combine(parent, "StingTools.log");
                }
                return _logPath;
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
