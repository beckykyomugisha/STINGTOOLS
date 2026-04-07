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

        private static string _logDate;

        private static string LogPath
        {
            get
            {
                // Daily log rotation: StingTools_20260306.log
                string today = DateTime.Now.ToString("yyyyMMdd");
                if (_logPath == null || _logDate != today)
                {
                    string dir = StingToolsApp.DataPath ?? Path.GetTempPath();
                    string parent = Path.GetDirectoryName(dir) ?? dir;
                    _logPath = Path.Combine(parent, $"StingTools_{today}.log");
                    if (_logDate != today)
                    {
                        // Date changed — close old writer so new file is opened
                        DisposeWriter();
                        _logDate = today;
                    }
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
                    // AutoFlush handles StreamWriter→FileStream; this flushes FileStream→disk
                    // so log entries survive native Revit crashes (C++ segfault).
                    ((FileStream)_writer.BaseStream).Flush(flushToDisk: true);
                }
            }
            catch
            {
                // CRASH FIX: Dispose inside lock to prevent another thread from
                // seeing a non-null but disposed _writer between disposal and null assignment
                lock (Lock)
                {
                    DisposeWriter();
                }
            }
        }

        // AG-10: Maximum log file size before rotation (50 MB)
        private const long MaxLogSizeBytes = 50 * 1024 * 1024;

        private static void EnsureWriter()
        {
            if (_writer == null)
            {
                // AG-10 FIX: Check file size — rotate if exceeded to prevent disk exhaustion
                try
                {
                    string path = LogPath;
                    if (File.Exists(path))
                    {
                        var fi = new FileInfo(path);
                        if (fi.Length > MaxLogSizeBytes)
                        {
                            // Rotate: rename current to .old (overwrite previous .old)
                            string oldPath = path + ".old";
                            try { if (File.Exists(oldPath)) File.Delete(oldPath); } catch { }
                            try { File.Move(path, oldPath); } catch { }
                        }
                    }
                }
                catch { /* Best-effort rotation — don't block logging */ }

                // CRASH FIX: AutoFlush = true ensures every Write goes through to the
                // FileStream immediately. Combined with FileStream.Flush(flushToDisk: true)
                // below, this guarantees log entries survive native Revit crashes that
                // kill the process without running finalizers or flush callbacks.
                var stream = new FileStream(LogPath, FileMode.Append, FileAccess.Write, FileShare.Read);
                _writer = new StreamWriter(stream) { AutoFlush = true };
            }
        }

        private static void DisposeWriter()
        {
            try
            {
                _writer?.Flush(); // LG-07: Ensure buffered entries are flushed before dispose
                _writer?.Dispose();
            }
            catch (Exception) { } // Cannot log from logger itself
            _writer = null;
        }

        /// <summary>
        /// Flush and close the log file. Call on plugin shutdown (OnShutdown).
        /// </summary>
        public static void Shutdown()
        {
            lock (Lock)
            {
                try { _writer?.Flush(); } catch { } // LG-07: Final flush before shutdown
                DisposeWriter();
            }
        }
    }
}
