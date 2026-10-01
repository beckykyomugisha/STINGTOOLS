// H-9: the KUT fortnightly issue (the cycle every ACC upload belongs to) had no launcher -
// only the paged "Run preset" picker. It now has an entry on SETUP's quick-workflow combo.
// The three pieces live in three files and nothing compiles them together: the combo tag,
// the exact preset name the handler passes, and the preset file's "name". A rename of any
// one would silently fall back to the picker, so this test reads all three.

using System.IO;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using Xunit;

namespace StingTools.Acc.Tests
{
    public class KutPresetLauncherTests
    {
        private const string Tag = "RunWorkflow_KUTFortnightlyIssue";

        [Fact]
        public void TheComboTag_TheHandlerCase_AndThePresetNameAgree()
        {
            string xaml = File.ReadAllText(AccAttributeNamesTests.FindRepoFile("StingTools", "UI", "StingDockPanel.xaml"));
            Assert.Contains($"Tag=\"{Tag}\"", xaml);

            string handler = File.ReadAllText(AccAttributeNamesTests.FindRepoFile("StingTools", "UI", "StingCommandHandler.cs"));
            var m = Regex.Match(handler, "case \"" + Tag + "\":\\s*\\{\\s*SetExtraParam\\(\"WorkflowPresetName\", \"([^\"]+)\"\\);");
            Assert.True(m.Success, "the handler case must pass the preset name verbatim");

            var preset = JObject.Parse(File.ReadAllText(
                AccAttributeNamesTests.FindRepoFile("StingTools", "Data", "WORKFLOW_KUT_FortnightlyIssue.json")));
            Assert.Equal((string)preset["name"], m.Groups[1].Value);
        }
    }
}
