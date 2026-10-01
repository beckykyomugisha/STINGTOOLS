using Newtonsoft.Json.Linq;
using Planscape.Companion;
using Xunit;

namespace StingTools.Tags.Tests
{
    /// <summary>
    /// DSCH-46 - the BCC deliverable register badges each row WORKING / REF from
    /// the Companion's sync folder. BCC knows the server project id; the folder is
    /// named after the code the Companion was linked with. CompanionPaths.ProjectCodeFor
    /// is the join, read from the Companion's own settings file shape
    /// (projects[].projectId / projectCode, camelCase - CompanionSettings.cs).
    /// </summary>
    public class CompanionProjectCodeTests
    {
        private static JObject Settings(params (string id, string code)[] projects)
        {
            var arr = new JArray();
            foreach (var (id, code) in projects)
                arr.Add(new JObject { ["projectId"] = id, ["projectCode"] = code, ["autoSync"] = true });
            return new JObject { ["rootFolder"] = @"C:\Planscape", ["projects"] = arr };
        }

        [Fact]
        public void LinkedProject_ReturnsItsCode()
        {
            var s = Settings(("11111111-1111-1111-1111-111111111111", "KUT"),
                             ("22222222-2222-2222-2222-222222222222", "MBALWA"));
            Assert.Equal("MBALWA", CompanionPaths.ProjectCodeFor(s, "22222222-2222-2222-2222-222222222222"));
        }

        [Fact]
        public void GuidFormatsAgree()
        {
            // Guid.ToString() is "D"; a hand-edited settings file may hold "N" or braces.
            var s = Settings(("{22222222-2222-2222-2222-222222222222}", "MBALWA"));
            Assert.Equal("MBALWA", CompanionPaths.ProjectCodeFor(s, "22222222222222222222222222222222"));
        }

        [Fact]
        public void UnlinkedProject_IsNull_NotTheFirstProject()
        {
            var s = Settings(("11111111-1111-1111-1111-111111111111", "KUT"));
            Assert.Null(CompanionPaths.ProjectCodeFor(s, "33333333-3333-3333-3333-333333333333"));
        }

        [Fact]
        public void BlankCode_IsNull()
        {
            var s = Settings(("11111111-1111-1111-1111-111111111111", "  "));
            Assert.Null(CompanionPaths.ProjectCodeFor(s, "11111111-1111-1111-1111-111111111111"));
        }

        [Fact]
        public void NoSettingsOrNoId_IsNull()
        {
            Assert.Null(CompanionPaths.ProjectCodeFor(null, "11111111-1111-1111-1111-111111111111"));
            Assert.Null(CompanionPaths.ProjectCodeFor(new JObject(), "11111111-1111-1111-1111-111111111111"));
            Assert.Null(CompanionPaths.ProjectCodeFor(Settings(("11111111-1111-1111-1111-111111111111", "KUT")), ""));
        }
    }
}
