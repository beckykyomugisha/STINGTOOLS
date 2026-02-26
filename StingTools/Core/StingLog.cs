using System;
using System.IO;

namespace StingTools.Core
{
    /// <summary>
    /// Lightweight logger for STING Tools. Writes to a log file alongside the DLL
    /// and optionally to the Revit journal. Replaces silent catch blocks throughout
    /// the codebase so errors are traceable.
    /// </summary>
    public static class StingLog
    {
        private static readonly object Lock = new object();
        private static string _logPath;

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
                    File.AppendAllText(LogPath, line + Environment.NewLine);
                }
            }
            catch
            {
                // Last-resort: cannot log, do nothing
            }
        }
    }
}
