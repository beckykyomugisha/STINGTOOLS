// DTW-82 — the Revit-free half of DrawingTypes_SelfTest.
//
// The self-test checks bindings against a list mirrored from
// tools/drawing_binding_contract.json (the plugin does not ship tools/). These tests
// hold the mirror to the file, so adding a parameter to the contract without adding it
// to the self-test fails here, and vice versa.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using StingTools.Core.Drawing;
using Xunit;

namespace StingTools.Tags.Tests
{
    public class DrawingSelfTestModelTests
    {
        private static JObject Contract()
        {
            var d = new DirectoryInfo(AppContext.BaseDirectory);
            while (d != null && !File.Exists(Path.Combine(d.FullName, "tools", "drawing_binding_contract.json"))) d = d.Parent;
            Assert.True(d != null, "could not locate tools/drawing_binding_contract.json");
            return JObject.Parse(File.ReadAllText(Path.Combine(d.FullName, "tools", "drawing_binding_contract.json")));
        }

        private static void AssertSame(string key, IReadOnlyList<string> mirror)
        {
            var file = Contract()[key]?.ToObject<List<string>>();
            Assert.True(file != null && file.Count > 0, $"contract has no '{key}' list");
            var missing = file.Except(mirror, StringComparer.Ordinal).ToList();
            var extra = mirror.Except(file, StringComparer.Ordinal).ToList();
            Assert.True(missing.Count == 0 && extra.Count == 0,
                $"DrawingSelfTestModel's {key} list disagrees with tools/drawing_binding_contract.json — "
                + $"add to the mirror: [{string.Join(", ", missing)}]; not in the contract: [{string.Join(", ", extra)}]");
        }

        [Fact] public void View_list_mirrors_the_contract() => AssertSame("views", DrawingSelfTestModel.ViewParams);
        [Fact] public void Sheet_list_mirrors_the_contract() => AssertSame("sheets", DrawingSelfTestModel.SheetParams);
        [Fact] public void Line_list_mirrors_the_contract() => AssertSame("lines", DrawingSelfTestModel.LineParams);

        [Fact]
        public void The_named_view_parameters_are_in_the_list()
        {
            Assert.Contains("STING_DRAWING_TYPE_ID_TXT", DrawingSelfTestModel.ViewParams);
            Assert.Contains("STING_VIEW_CONTEXT_TAG_TXT", DrawingSelfTestModel.ViewParams);
        }

        [Fact]
        public void Representative_filters_exist_in_the_shipped_library()
        {
            var d = new DirectoryInfo(AppContext.BaseDirectory);
            while (d != null && !File.Exists(Path.Combine(d.FullName, "StingTools", "Data", "STING_AEC_FILTERS.json"))) d = d.Parent;
            Assert.NotNull(d);
            var ids = JObject.Parse(File.ReadAllText(Path.Combine(d.FullName, "StingTools", "Data", "STING_AEC_FILTERS.json")))["filters"]
                .Select(f => (string)f["id"]).ToHashSet(StringComparer.Ordinal);
            var absent = DrawingSelfTestModel.RepresentativeFilters.Where(i => !ids.Contains(i)).ToList();
            Assert.True(absent.Count == 0, "not in STING_AEC_FILTERS.json: " + string.Join(", ", absent));
            Assert.Contains(DrawingSelfTestModel.RepresentativeFilters, i => i.StartsWith("clin-press-", StringComparison.Ordinal));
        }

        [Theory]
        [InlineData("A-001", true)]
        [InlineData("PRJ-PLN-ZZ-00-DR-A-0001", true)]
        [InlineData("A-XX-001", true)]
        [InlineData("", false)]
        [InlineData("A-{seq:D3}", false)]
        [InlineData("A--001", false)]
        [InlineData("-001", false)]
        [InlineData("A-", false)]
        [InlineData(" A-001", false)]
        public void Sheet_number_well_formedness(string number, bool ok)
        {
            Assert.Equal(ok, DrawingSelfTestModel.IsWellFormedSheetNumber(number, out var why));
            Assert.Equal(ok, why == null);
        }

        [Theory]
        [InlineData("00", true)]
        [InlineData("01", true)]
        [InlineData("B1", true)]
        [InlineData("ZZ", true)]
        [InlineData("M01", true)]
        [InlineData("", false)]
        [InlineData(null, false)]
        [InlineData("L", false)]
        [InlineData("Level 1", false)]
        [InlineData("b1", false)]
        public void Level_code_well_formedness(string code, bool ok)
            => Assert.Equal(ok, DrawingSelfTestModel.IsWellFormedLevelCode(code));

        [Fact]
        public void Top_left_inside_uses_the_tolerance()
        {
            Assert.True(DrawingSelfTestModel.TopLeftInside(0, 10, 0, 5, 0, 10, 0));
            Assert.True(DrawingSelfTestModel.TopLeftInside(-0.05, 10.05, 0, 5, 0, 10, 0.1));
            Assert.False(DrawingSelfTestModel.TopLeftInside(-1, 10, 0, 5, 0, 10, 0.1));
            Assert.False(DrawingSelfTestModel.TopLeftInside(2, 11, 0, 5, 0, 10, 0.1));
        }

        [Fact]
        public void Csv_quotes_and_states_the_rollback()
        {
            var rows = new[]
            {
                new SelfTestRow("a. Bindings", "X → Views", SelfTestStatus.Pass, "bound"),
                new SelfTestRow("d. AEC filters", "f", SelfTestStatus.Fail, "REVIT REFUSED: bad, \"rule\""),
            };
            var csv = DrawingSelfTestModel.ToCsv(rows, "Model", new DateTime(2026, 10, 1, 12, 0, 0));
            var lines = csv.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
            Assert.Contains("rolled back", lines[1]);
            Assert.Equal("Check,Item,Status,Detail", lines[2]);
            Assert.Equal("a. Bindings,X → Views,PASS,bound", lines[3]);
            Assert.Equal("d. AEC filters,f,FAIL,\"REVIT REFUSED: bad, \"\"rule\"\"\"", lines[4]);
        }

        [Fact]
        public void Summary_counts_every_status()
        {
            var rows = new List<SelfTestRow>
            {
                new SelfTestRow("a", "1", SelfTestStatus.Pass, ""),
                new SelfTestRow("a", "2", SelfTestStatus.Pass, ""),
                new SelfTestRow("b", "3", SelfTestStatus.Fail, ""),
                new SelfTestRow("c", "4", SelfTestStatus.Skip, "why"),
                new SelfTestRow("c", "5", SelfTestStatus.Info, ""),
            };
            Assert.Equal("2 pass, 1 fail, 1 skip, 1 info", DrawingSelfTestModel.Summary(rows));
        }
    }
}
