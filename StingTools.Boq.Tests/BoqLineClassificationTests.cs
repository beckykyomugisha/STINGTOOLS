using System.IO;
using System.Text.RegularExpressions;
using StingTools.BOQ.Sync;
using Xunit;

namespace StingTools.Boq.Tests
{
    /// <summary>
    /// DSCH-44 follow-up — a synced BOQ line is classified on the server by its NRM2
    /// section; a line without one is reported before anything is pushed.
    /// </summary>
    public class BoqLineClassificationTests
    {
        [Theory]
        [InlineData("14", "14")]
        [InlineData(" 22 ", "22")]
        [InlineData("", null)]
        [InlineData("   ", null)]
        [InlineData(null, null)]
        public void Code_Is_The_Trimmed_Section_Or_Nothing(string section, string expected)
            => Assert.Equal(expected, BoqLineClassification.CodeFor(section));

        [Fact]
        public void System_Is_NRM2()
            => Assert.Equal("NRM2", BoqLineClassification.SystemCode);

        [Fact]
        public void Preflight_Passes_When_Every_Line_Has_A_Section()
            => Assert.Null(BoqLineClassification.PreflightProblem(new[] { "14", "22", "41" }));

        [Fact]
        public void Preflight_Counts_Lines_Without_A_Section()
        {
            string p = BoqLineClassification.PreflightProblem(new[] { "14", "", null, " " });
            Assert.NotNull(p);
            Assert.StartsWith("3 BOQ line(s)", p);
        }

        [Fact]
        public void The_Server_Request_Carries_The_Fields_The_Plugin_Sends()
        {
            // Contract between two builds: the payload property names in
            // BoqSyncCoordinator must exist on the server's UpsertQuantityLineRequest
            // (System.Text.Json binds them case-insensitively).
            string root = RepoRoot();
            string server = File.ReadAllText(Path.Combine(root, "Planscape.Server", "src", "Planscape.API", "Controllers", "BoqController.cs"));
            string plugin = File.ReadAllText(Path.Combine(root, "StingTools", "BOQ", "Sync", "BoqSyncCoordinator.cs"));
            var request = Regex.Match(server, @"public record UpsertQuantityLineRequest\((.*?)\n\s*public record ", RegexOptions.Singleline);
            Assert.True(request.Success, "UpsertQuantityLineRequest not found — update this test if it moved.");
            foreach (string field in new[] { "ClassificationSystemCode", "ClassificationCode", "ProvisionalSumType" })
            {
                Assert.Matches(@"string\?\s+" + field + @"\b", request.Groups[1].Value);
                Assert.Matches(@"\b" + char.ToLowerInvariant(field[0]) + field.Substring(1) + @"\s*=", plugin);
            }
        }

        private static string RepoRoot()
        {
            var d = new DirectoryInfo(System.AppContext.BaseDirectory);
            while (d != null && !File.Exists(Path.Combine(d.FullName, "StingTools", "StingTools.csproj"))) d = d.Parent;
            Assert.NotNull(d);
            return d.FullName;
        }
    }
}
