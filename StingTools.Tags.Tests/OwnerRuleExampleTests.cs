using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using Xunit;

namespace StingTools.Tags.Tests
{
    /// <summary>
    /// KUT deep review ACC-15. The KUT sheet-number rule's own description gave the example
    /// KUT-ZZZ-XX-XX-M3-A-0001, which the rule's regex rejects (volume XX is not 00-06 or ZZ).
    /// Someone copying the documented example got a violation. An example a rule prints must
    /// pass that rule.
    /// </summary>
    public class OwnerRuleExampleTests
    {
        [Fact]
        public void EveryContainerExampleInASheetRuleDescriptionPassesThatRule()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "StingTools", "StingTools.csproj")))
                dir = dir.Parent;
            Assert.NotNull(dir);
            var doc = JObject.Parse(File.ReadAllText(Path.Combine(dir.FullName, "project-templates", "KUT", "_BIM_COORD", "owner_standards.json")));

            int examples = 0;
            foreach (var rule in doc["rules"].Where(r => (string)r["type"] == "sheetNumberPattern"))
            {
                var rx = new Regex((string)rule["pattern"]);
                foreach (Match m in Regex.Matches((string)rule["description"] ?? "", @"\bKUT-[A-Z0-9]{3}(?:-[A-Z0-9]{1,4}){5}\b"))
                {
                    examples++;
                    Assert.True(rx.IsMatch(m.Value), $"rule {rule["id"]} gives the example {m.Value}, which its own pattern rejects");
                }
            }
            Assert.True(examples > 0, "no container example found in any sheet rule description");
        }
    }
}
