// Test-only no-op StingLog. AccModelCoordSync and AccIssueSync log via
// StingTools.Core.StingLog; we supply a minimal identically-shaped type in the same
// namespace so the tests compile without pulling in the full net8.0-windows StingTools
// assembly (StingTools/Core/StingLog.cs itself drags in StingTools.Core.Drawing).
//
// Same shape as StingTools.Visibility.Tests/TestHelpers/StingLogShim.cs.
using System;

namespace StingTools.Core
{
    internal static class StingLog
    {
        public static void Info(string msg) { /* no-op in tests */ }
        public static void Warn(string msg) { /* no-op in tests */ }
        public static void Error(string msg, Exception ex = null) { /* no-op in tests */ }
    }
}
