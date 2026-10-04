using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using StingTools.BIMManager;
using Xunit;

namespace StingTools.Tags.Tests
{
    /// <summary>
    /// KUT deep review ISO-12 / ISO-13 / ISO-14. Five writers allocated transmittal ids five ways
    /// (Count+1; a process-static counter that restarted at 1 each Revit session; scans of one
    /// field), so an auto row and a manual row could both be TX-0002, and MarkSent then picked the
    /// AUTO duplicate and left the PREPARED row unissued. The auto row also carried issue_date for
    /// something never issued.
    /// </summary>
    public class TransmittalIdAllocationTests
    {
        [Fact]
        public void NextIdReadsEveryRowShape()
        {
            var rows = JArray.Parse(@"[
                { ""transmittal_id"": ""TX-0001"", ""status"": ""ISSUED"" },
                { ""id"": ""TX-0004"", ""status"": ""AUTO_GENERATED"" },
                { ""tx_id"": ""TX-0002"" },
                { ""id"": ""RV-0099"" }
            ]");
            Assert.Equal("TX-0005", TransmittalRecord.NextId(rows));
        }

        [Fact]
        public void NextIdOfNothingIsTheFirst()
        {
            Assert.Equal("TX-0001", TransmittalRecord.NextId(new JArray()));
            Assert.Equal("TX-0001", TransmittalRecord.NextId(null));
        }

        [Fact]
        public void MarkSentMovesThePreparedRow_NotAnAutoDuplicate()
        {
            var rows = JArray.Parse(@"[
                { ""id"": ""TX-0002"", ""status"": ""AUTO_GENERATED"" },
                { ""transmittal_id"": ""TX-0002"", ""status"": ""PREPARED"" }
            ]");
            var row = TransmittalRecord.MarkSent(rows, "TX-0002", new DateTime(2026, 10, 4), "u", "n");
            Assert.NotNull(row);
            Assert.Equal("SENT", (string)rows[1]["status"]);
            Assert.Equal("AUTO_GENERATED", (string)rows[0]["status"]);
        }

        [Fact]
        public void EveryWriterUsesTheOneAllocator()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "StingTools", "StingTools.csproj")))
                dir = dir.Parent;
            Assert.NotNull(dir);
            string R(params string[] p) => File.ReadAllText(Path.Combine(dir.FullName, Path.Combine(p)));

            string pfe = R("StingTools", "Core", "ProjectFolderEngine.cs");
            Assert.DoesNotContain("$\"TX-{(arr.Count + 1):D4}\"", pfe);
            Assert.DoesNotContain("[\"issue_date\"]   = DateTime.UtcNow", pfe);

            string bim = R("StingTools", "BIMManager", "BIMManagerCommands.cs");
            Assert.DoesNotMatch(new Regex(@"GetNextSequentialId\([^)]*""TX""\)"), bim);
            Assert.DoesNotContain("NextIdFromArray(txArray, \"TX\"", bim);

            foreach (var f in new[] { R("StingTools", "UI", "DocumentManagementDialog.cs"),
                                      R("StingTools", "Docs", "Templates", "TransmittalOrchestrator.cs") })
                Assert.Contains("TransmittalRecord.NextId(", f);
        }
    }
}
