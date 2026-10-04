using StingTools.Core.Electrical;
using Xunit;

namespace StingTools.Tags.Tests
{
    /// <summary>
    /// What a command reports after its transaction (Circuit Check, Fault Level stamp,
    /// AIC stamp). A rolled-back Commit() used to be reported as "Stamped N values".
    /// </summary>
    public class ElecWriteReportTests
    {
        [Fact]
        public void Committed_run_reports_its_count()
        {
            Assert.Equal("12 values stamped.", ElecWriteReport.Landed("values stamped", 12, committed: true, "Committed"));
            Assert.Equal(12, ElecWriteReport.Kept(12, committed: true));
        }

        [Fact]
        public void Rolled_back_run_reports_zero_and_says_rolled_back()
        {
            string s = ElecWriteReport.Landed("values stamped", 12, committed: false, "RolledBack");
            Assert.StartsWith("ROLLED BACK (RolledBack): 0 values stamped", s);
            Assert.Contains("12 write(s) were made and then undone", s);
            Assert.DoesNotContain("12 values stamped", s);
            Assert.Equal(0, ElecWriteReport.Kept(12, committed: false));
        }

        [Fact]
        public void A_rolled_back_step_says_so_and_voids_its_counts()
        {
            string s = ElecWriteReport.RolledBack("STING Batch Home-Run Arrows", "RolledBack");
            Assert.StartsWith("ROLLED BACK (RolledBack): STING Batch Home-Run Arrows", s);
            Assert.Contains("nothing from this step was kept", s);
            Assert.Contains("counts it would have reported are void", s);
            Assert.StartsWith("ROLLED BACK (not committed): the change", ElecWriteReport.RolledBack(null, null));
        }

        /// <summary>ELEC-28: no electrical Transaction is committed without reading the status.</summary>
        [Fact]
        public void No_electrical_transaction_commits_without_reading_the_status()
        {
            var dir = new System.IO.DirectoryInfo(System.AppContext.BaseDirectory);
            while (dir != null && !System.IO.File.Exists(System.IO.Path.Combine(dir.FullName, "StingTools.addin"))) dir = dir.Parent;
            Assert.NotNull(dir);
            var hits = new System.Collections.Generic.List<string>();
            foreach (var r in new[] { "Commands/Electrical", "Commands/Panels", "Core/Electrical", "Core/Panels", "Core/SLD" })
            {
                string d = System.IO.Path.Combine(dir.FullName, "StingTools", r.Replace('/', System.IO.Path.DirectorySeparatorChar));
                foreach (var f in System.IO.Directory.EnumerateFiles(d, "*.cs", System.IO.SearchOption.AllDirectories))
                {
                    string src = System.IO.File.ReadAllText(f);
                    var txVars = new System.Collections.Generic.HashSet<string>();
                    foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(src,
                                 @"\b(?:var|Transaction)\s+(\w+)\s*=\s*new\s+Transaction\("))
                        txVars.Add(m.Groups[1].Value);
                    var lines = System.IO.File.ReadAllLines(f);
                    for (int i = 0; i < lines.Length; i++)
                    {
                        var m = System.Text.RegularExpressions.Regex.Match(lines[i], @"(?:^|[;{}]|\belse)\s*(\w+)\.Commit\(\)\s*;");
                        if (m.Success && txVars.Contains(m.Groups[1].Value) && !lines[i].TrimStart().StartsWith("//"))
                            hits.Add($"{System.IO.Path.GetFileName(f)}:{i + 1}: {lines[i].Trim()}");
                    }
                }
            }
            Assert.True(hits.Count == 0, "Commit() status discarded (use ElecTx.Commit or read the status):\n" + string.Join("\n", hits));
        }

        [Fact]
        public void Rolled_back_with_no_writes_still_says_rolled_back()
            => Assert.Equal("ROLLED BACK (not committed): 0 verdicts.", ElecWriteReport.Landed("verdicts", 0, committed: false));
    }
}
