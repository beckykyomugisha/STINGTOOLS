using System;
using System.Diagnostics;
using System.IO;
using StingTools.Core;
using Xunit;

namespace StingTools.Tags.Tests
{
    /// <summary>
    /// ROADMAP ELEC-32: ~850 commits outside the electrical module discarded the
    /// TransactionStatus and reported counts for work Revit had rolled back.
    /// </summary>
    public class TxReportTests
    {
        [Fact]
        public void A_rollback_message_says_rolled_back_voids_counts_and_quotes_Revit()
        {
            string s = TxReport.RolledBack("STING Batch Tag", "RolledBack",
                new[] { "Element is owned by another user", "Element is owned by another user", "  " });
            Assert.StartsWith("ROLLED BACK (RolledBack): STING Batch Tag", s);
            Assert.Contains("nothing from this step was kept", s);
            Assert.Contains("counts it would have reported are void", s);
            Assert.Contains("Revit reported: Element is owned by another user", s);
            Assert.Equal(1, CountOf(s, "owned by another user"));   // de-duplicated
        }

        [Fact]
        public void Without_Revit_text_the_message_points_at_Revit_warning()
            => Assert.EndsWith("Revit's own warning (if shown) says why.", TxReport.RolledBack(null, null));

        [Fact]
        public void Failure_log_returns_a_transaction_messages_once()
        {
            string name = "STING test " + Guid.NewGuid();
            TxFailureLog.Record(name, new[] { "a", "", "b" });
            Assert.Equal(new[] { "a", "b" }, TxFailureLog.Take(name));
            Assert.Empty(TxFailureLog.Take(name));           // taken — a later commit is not blamed
            Assert.Empty(TxFailureLog.Take("never recorded"));
        }

        /// <summary>The CI gate itself: no Transaction.Commit() in the plugin discards its status.</summary>
        [Fact]
        public void No_transaction_commit_discards_its_status()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "StingTools.addin"))) dir = dir.Parent;
            Assert.NotNull(dir);
            var psi = new ProcessStartInfo("python", "tools/check_unchecked_commits.py")
            {
                WorkingDirectory = dir.FullName, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false,
            };
            using var p = Process.Start(psi);
            string output = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
            p.WaitForExit();
            Assert.True(p.ExitCode == 0, output);
        }

        private static int CountOf(string s, string part)
        {
            int n = 0, i = 0;
            while ((i = s.IndexOf(part, i, StringComparison.Ordinal)) >= 0) { n++; i += part.Length; }
            return n;
        }
    }
}
