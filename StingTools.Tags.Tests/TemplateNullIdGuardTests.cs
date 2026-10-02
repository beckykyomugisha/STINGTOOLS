using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using StingTools.Core.Drawing;
using Xunit;

namespace StingTools.Tags.Tests
{
    /// <summary>
    /// DT-R11 — the in-Revit Drawing Self-Test threw ArgumentNullException ("id") from
    /// Document.GetElement inside ManagedTemplateSyncer.EnsureTemplate on a model with no
    /// style templates. A cache miss (Dictionary.TryGetValue) leaves an ElementId local
    /// null; `null != ElementId.InvalidElementId` is true, so the null went straight to
    /// GetElement. These guards hold the produce path to a null-safe id test, and pin the
    /// "template owns this setting" decision that replaced the Detail Level failure note.
    /// </summary>
    public class TemplateNullIdGuardTests
    {
        // A bare `x != ElementId.InvalidElementId` with no `x != null &&` in front of it.
        private static readonly Regex BareInvalidTest = new Regex(
            @"(?<!!=\s*null\s*&&\s*)\b(?<v>[A-Za-z_][A-Za-z0-9_]*)\s*!=\s*ElementId\.InvalidElementId",
            RegexOptions.Compiled);

        [Theory]
        [InlineData("ManagedTemplateSyncer.cs")]
        [InlineData("DrawingTypePresentation.cs")]
        public void NoElementIdIsTestedOnlyAgainstInvalid(string file)
        {
            var lines = File.ReadAllLines(Path.Combine(RepoRoot(), "StingTools", "Core", "Drawing", file));
            var offenders = lines
                .Select((l, i) => (line: l, no: i + 1))
                .Where(x => !x.line.TrimStart().StartsWith("//", StringComparison.Ordinal))
                .Where(x => BareInvalidTest.Matches(x.line).Cast<Match>()
                    .Any(m => !x.line.Contains(m.Groups["v"].Value + " != null", StringComparison.Ordinal)
                           && !x.line.Contains(m.Groups["v"].Value + " == null", StringComparison.Ordinal)
                           && !x.line.Contains(m.Groups["v"].Value + " ?? ElementId.InvalidElementId", StringComparison.Ordinal)))
                .Select(x => $"{file}:{x.no}: {x.line.Trim()}")
                .ToList();
            Assert.True(offenders.Count == 0,
                "An ElementId that can be null (a TryGetValue miss, an unset field) passes `!= InvalidElementId` "
                + "and reaches Document.GetElement. Use ManagedTemplateSyncer.IsUsable(id):\n" + string.Join("\n", offenders));
        }

        [Fact]
        public void TheSelfTestGuardsTheManagedTemplateIdBeforeGetElement()
        {
            var src = File.ReadAllText(Path.Combine(RepoRoot(), "StingTools", "Commands", "Drawing", "DrawingSelfTestCommand.cs"));
            int at = src.IndexOf("ManagedTemplateSyncer.EnsureTemplate(doc, pack, ViewType.FloorPlan, result);", StringComparison.Ordinal);
            Assert.True(at >= 0, "CheckManagedVg's EnsureTemplate call not found");
            int get = src.IndexOf("doc.GetElement(tid)", at, StringComparison.Ordinal);
            int guard = src.IndexOf("IsUsable(tid)", at, StringComparison.Ordinal);
            Assert.True(guard >= 0 && (get < 0 || guard < get), "tid must be checked with IsUsable before GetElement");
        }

        [Theory]
        [InlineData(false, false, false, false)]
        [InlineData(false, true,  true,  false)]   // no template: never defer, even if Revit says read-only
        [InlineData(true,  false, false, false)]   // template present but the setting is free
        [InlineData(true,  true,  false, true)]    // template lists it as controlled
        [InlineData(true,  false, true,  true)]    // controlled shows up as read-only on the view
        [InlineData(true,  true,  true,  true)]
        public void TheTemplateOwnsTheSettingOnlyWhenItControlsIt(bool hasTemplate, bool controls, bool readOnly, bool owned)
            => Assert.Equal(owned, TemplateOwnedSetting.OwnedByTemplate(hasTemplate, controls, readOnly));

        [Fact]
        public void TheNoteNamesTheSettingTheViewAndTheTemplate()
        {
            var note = TemplateOwnedSetting.Note("DetailLevel", "Medium", "L01 Plan", "STING:corp-arch-plan:FloorPlan");
            Assert.Contains("DetailLevel Medium", note);
            Assert.Contains("'L01 Plan'", note);
            Assert.Contains("'STING:corp-arch-plan:FloorPlan'", note);
            Assert.DoesNotContain("cannot be modified", note);
        }

        private static string RepoRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "StingTools", "Core", "Drawing", "ManagedTemplateSyncer.cs")))
                dir = dir.Parent;
            Assert.True(dir != null, "repo root");
            return dir.FullName;
        }
    }
}
