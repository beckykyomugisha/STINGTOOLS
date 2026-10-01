// DTW-168 — every VIEW_FILTER row in MR_SCHEDULES.csv must parse to either an
// explicit category-only filter or a rule. The command used to read a grammar
// no row uses, so every rule row became a category-wide filter.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using StingTools.Core.Drawing;
using Xunit;

namespace StingTools.Tags.Tests
{
    public class ViewFilterRuleTextTests
    {
        [Theory]
        [InlineData("System_Type Contains Chilled Water Supply", "RBS_SYSTEM_NAME_PARAM", true, "Contains", "Chilled Water Supply")]
        [InlineData("Fire_Rating HasValue", "FIRE_RATING", true, "HasValue", "")]
        [InlineData("ASS_TAG_1 HasNoValue", "ASS_TAG_1_TXT", false, "HasNoValue", "")]
        [InlineData("Rule1: Pipes | System Classification equals Sanitary", "RBS_SYSTEM_CLASSIFICATION_PARAM", true, "Equals", "Sanitary")]
        [InlineData("Warranty_Status Equals Active", "Warranty_Status", false, "Equals", "Active")]
        [InlineData("ASS_STATUS_TXT NotEquals NEW", "ASS_STATUS_TXT", false, "NotEquals", "NEW")]
        public void RuleProseParses(string text, string param, bool builtIn, string op, string value)
        {
            var s = ViewFilterRuleText.Parse(text);
            Assert.Equal(ViewFilterRuleKind.Rule, s.Kind);
            Assert.Equal(param, s.Param);
            Assert.Equal(builtIn, s.IsBuiltIn);
            Assert.Equal(op, s.Op);
            Assert.Equal(value, s.Value);
        }

        [Fact]
        public void ExplicitNoRuleIsCategoryOnly()
            => Assert.Equal(ViewFilterRuleKind.CategoryOnly, ViewFilterRuleText.Parse("Type=Visibility (no parameter rules)").Kind);

        [Theory]
        [InlineData("")]
        [InlineData("System_Type Contains")]
        [InlineData("ASS_TAG_1 HasValue yes")]
        [InlineData("System_Type is chilled")]
        [InlineData("Rule=Equals, Param=ASS_STATUS_TXT, Value=NEW")]
        public void AnythingElseIsUnparseableNeverCategoryOnly(string text)
            => Assert.Equal(ViewFilterRuleKind.Unparseable, ViewFilterRuleText.Parse(text).Kind);

        [Fact]
        public void EveryShippedViewFilterRowParses()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "StingTools", "Data", "MR_SCHEDULES.csv")))
                dir = dir.Parent;
            Assert.True(dir != null, "Could not locate StingTools/Data");
            var rows = File.ReadAllLines(Path.Combine(dir.FullName, "StingTools", "Data", "MR_SCHEDULES.csv"))
                .Where(l => l.StartsWith("VIEW_FILTER,", StringComparison.Ordinal))
                .Select(Csv).ToList();
            Assert.True(rows.Count > 50, "expected the VIEW_FILTER rows");

            var bad = rows.Select(r => (Name: r[3], Spec: ViewFilterRuleText.Parse(r.Count > 8 ? r[8] : "")))
                .Where(x => x.Spec.Kind == ViewFilterRuleKind.Unparseable)
                .Select(x => $"{x.Name}: {x.Spec.Reason}").ToList();
            Assert.True(bad.Count == 0, string.Join("\n", bad));
            Assert.Contains(rows, r => ViewFilterRuleText.Parse(r[8]).Kind == ViewFilterRuleKind.Rule);
        }

        /// <summary>Minimal RFC-4180 split, enough for MR_SCHEDULES.csv.</summary>
        private static List<string> Csv(string line)
        {
            var cols = new List<string>();
            var cur = new System.Text.StringBuilder();
            bool q = false;
            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (q)
                {
                    if (c == '"' && i + 1 < line.Length && line[i + 1] == '"') { cur.Append('"'); i++; }
                    else if (c == '"') q = false;
                    else cur.Append(c);
                }
                else if (c == '"') q = true;
                else if (c == ',') { cols.Add(cur.ToString()); cur.Clear(); }
                else cur.Append(c);
            }
            cols.Add(cur.ToString());
            return cols;
        }
    }
}
