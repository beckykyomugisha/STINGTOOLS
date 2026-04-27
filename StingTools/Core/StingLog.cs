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

        // ── E-1 cache hit/miss telemetry ───────────────────────────────────
        // Lightweight Interlocked counters for the four highest-value caches.
        // Read via DumpCacheStats(); reset on Reset(). Write paths are
        // RecordHit/RecordMiss exposed below for cache owners to call.

        public enum CacheKind
        {
            ParamCache,
            RoomIndex,
            Tag7Hash,
            DrawingTypeRegistry,
        }

        private static long _paramCacheHits, _paramCacheMisses;
        private static long _roomIndexHits, _roomIndexMisses;
        private static long _tag7HashHits, _tag7HashMisses;
        private static long _dtRegistryHits, _dtRegistryMisses;

        public static void RecordHit(CacheKind kind)
        {
            switch (kind)
            {
                case CacheKind.ParamCache:           System.Threading.Interlocked.Increment(ref _paramCacheHits); break;
                case CacheKind.RoomIndex:            System.Threading.Interlocked.Increment(ref _roomIndexHits); break;
                case CacheKind.Tag7Hash:             System.Threading.Interlocked.Increment(ref _tag7HashHits); break;
                case CacheKind.DrawingTypeRegistry:  System.Threading.Interlocked.Increment(ref _dtRegistryHits); break;
            }
        }

        public static void RecordMiss(CacheKind kind)
        {
            switch (kind)
            {
                case CacheKind.ParamCache:           System.Threading.Interlocked.Increment(ref _paramCacheMisses); break;
                case CacheKind.RoomIndex:            System.Threading.Interlocked.Increment(ref _roomIndexMisses); break;
                case CacheKind.Tag7Hash:             System.Threading.Interlocked.Increment(ref _tag7HashMisses); break;
                case CacheKind.DrawingTypeRegistry:  System.Threading.Interlocked.Increment(ref _dtRegistryMisses); break;
            }
        }

        /// <summary>
        /// E-1: write current cache hit/miss counters to the log at Info level.
        /// Surfaced by the CheckData diagnostic command so users can see whether
        /// caching is doing what we expect across a session.
        /// </summary>
        public static void DumpCacheStats()
        {
            string fmt(long h, long m)
            {
                long total = h + m;
                return total == 0 ? "0/0 (no activity)" : $"{h}/{total} ({h * 100.0 / total:F1}% hit rate)";
            }
            Info($"Cache stats — _paramCache:           hits {fmt(_paramCacheHits,  _paramCacheMisses)}");
            Info($"Cache stats — SpatialAutoDetect room index: {fmt(_roomIndexHits, _roomIndexMisses)}");
            Info($"Cache stats — _tag7HashCache:        {fmt(_tag7HashHits, _tag7HashMisses)}");
            Info($"Cache stats — DrawingTypeRegistry:   {fmt(_dtRegistryHits, _dtRegistryMisses)}");
        }

        /// <summary>E-1: reset all cache hit/miss counters (use sparingly — diagnostics only).</summary>
        public static void ResetCacheStats()
        {
            System.Threading.Interlocked.Exchange(ref _paramCacheHits, 0);
            System.Threading.Interlocked.Exchange(ref _paramCacheMisses, 0);
            System.Threading.Interlocked.Exchange(ref _roomIndexHits, 0);
            System.Threading.Interlocked.Exchange(ref _roomIndexMisses, 0);
            System.Threading.Interlocked.Exchange(ref _tag7HashHits, 0);
            System.Threading.Interlocked.Exchange(ref _tag7HashMisses, 0);
            System.Threading.Interlocked.Exchange(ref _dtRegistryHits, 0);
            System.Threading.Interlocked.Exchange(ref _dtRegistryMisses, 0);
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
