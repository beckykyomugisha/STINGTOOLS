// Test-only StingLog. AcoRefiner / VoxelGrid / etc. write Info / Warn /
// Error via StingTools.Core.StingLog; we ship a minimal identically-
// shaped type so the routing files compile without pulling in the
// full StingTools assembly.

using System;

namespace StingTools.Core
{
    internal static class StingLog
    {
        public static void Info(string msg)            { /* no-op */ }
        public static void Warn(string msg)            { /* no-op */ }
        public static void Error(string msg, Exception ex = null) { /* no-op */ }
    }
}
