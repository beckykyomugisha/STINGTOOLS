// KUT deep review INT-4 / INT-10 / INT-14 — source guards on the two Revit-bound commands that
// create ACC issues. They cannot run here; these pin the shape that makes escalation safe.

using System;
using System.IO;
using System.Linq;
using Xunit;

namespace StingTools.Acc.Tests
{
    public class AccCommandLedgerGuardTests
    {
        private static string Src(params string[] parts)
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "StingTools", "StingTools.csproj")))
                dir = dir.Parent;
            Assert.NotNull(dir);
            return File.ReadAllText(Path.Combine(new[] { dir.FullName, "StingTools" }.Concat(parts).ToArray()));
        }

        [Fact]
        public void ClashEscalationCountsFailuresAndUsesTheLedger()
        {
            string s = Src("Clash", "AccPullClashesCommand.cs");
            // INT-4: a run whose every push failed reported "escalated 0" and succeeded.
            Assert.Contains("int pushed = 0, skipped = 0, failed = 0;", s);
            Assert.Contains("failed++", s);
            // INT-14: the ledger is read through AccEscalationLedger, which refuses a corrupt
            // file instead of returning an empty map.
            Assert.Contains("AccEscalationLedger.Load(", s);
            Assert.DoesNotContain("catch (Exception ex) { StingLog.Warn(\"ACC pushed_clashes load: \"", s);
        }

        [Fact]
        public void LifecycleGapPushRefusesWithoutALedger()
        {
            string s = Src("Commands", "Twin", "KutPushLifecycleGapsToAccCommand.cs");
            // INT-10: an unsaved model had no sidecar, so every run raised every gap again.
            Assert.Contains("if (string.IsNullOrEmpty(sidecar))", s);
            Assert.Contains("AccEscalationLedger.Load(sidecar", s);
            Assert.Contains("AccEscalationLedger.Save(sidecar", s);
        }
    }
}
