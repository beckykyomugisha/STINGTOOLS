using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace StingTools.Tags.Tests
{
    /// <summary>
    /// DSCH-46b - every MAT_* button the Material Hub builds must reach a handler.
    ///
    /// The Hub builds its buttons in code (MaterialHubPanel.Builders.cs), so the
    /// dock-panel XAML wiring gate (check_workflow_wiring.ps1 Tier 4) never sees
    /// them. Their StingCommandHandler cases were lost in a -X ours merge on
    /// 2026-05-23 and every MAT_* button logged "Unrecognised command tag" from
    /// then on. This reads both sources so a button and its case cannot drift.
    /// </summary>
    public class MaterialHubDispatchTests
    {
        private static string UiDir()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "StingTools", "UI", "StingCommandHandler.cs")))
                dir = dir.Parent;
            Assert.True(dir != null, "Could not locate the repository root");
            return Path.Combine(dir.FullName, "StingTools", "UI");
        }

        [Fact]
        public void EveryHubMatButton_HasAHandlerCase()
        {
            string ui = UiDir();
            string hub = string.Concat(Directory.GetFiles(ui, "MaterialHub*.cs").Select(File.ReadAllText));
            // A button tag is the second string of ("Label", "MAT_X") or MakeAction("Label", "MAT_X");
            // an activity-feed id such as Add("MAT_PrismConvert", ...) is not a button.
            var tags = Regex.Matches(hub, @",\s*""(MAT_[A-Za-z]+)""\s*\)").Select(m => m.Groups[1].Value)
                .Distinct().OrderBy(t => t).ToList();
            Assert.True(tags.Count >= 25, $"expected the Hub's MAT_ buttons, found {tags.Count}");

            string handler = File.ReadAllText(Path.Combine(ui, "StingCommandHandler.cs"));
            var cases = Regex.Matches(handler, "case\\s+\"(MAT_[A-Za-z]+)\"\\s*:")
                .Select(m => m.Groups[1].Value).ToHashSet();

            var missing = tags.Where(t => !cases.Contains(t)).ToList();
            Assert.True(missing.Count == 0, "Material Hub buttons with no handler case: " + string.Join(", ", missing));
        }
    }
}
