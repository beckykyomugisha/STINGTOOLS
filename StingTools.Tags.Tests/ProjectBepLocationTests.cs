using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using Xunit;

namespace StingTools.Tags.Tests
{
    /// <summary>
    /// A project's BEP lives in that project's coordination folder. The plugin data
    /// folder ships <c>project_bep.json</c> as a SAMPLE ("Sample Project", PRJ-001) and
    /// is shared by every project on the machine, so any code that reads a BEP from it
    /// answers for some other project: the deliverable tracker marked the BEP Complete
    /// and the BREEAM assessor awarded Man 01 credits on every project, and Create BEP
    /// overwrote the sample with whichever project ran the wizard last.
    /// These are source guards: the readers are Revit-bound and cannot run here.
    /// </summary>
    public class ProjectBepLocationTests
    {
        private static string RepoRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "StingTools", "StingTools.csproj")))
                dir = dir.Parent;
            Assert.True(dir != null, "Could not locate StingTools/StingTools.csproj from " + AppContext.BaseDirectory);
            return dir.FullName;
        }

        /// <summary>A BEP path built on the plugin data folder, in either spelling.</summary>
        private static readonly Regex DataFolderBep = new Regex(
            @"(DataPath[^;\n]{0,80}""project_bep\.json""|FindDataFile\(\s*""project_bep\.json""\s*\))",
            RegexOptions.IgnoreCase);

        [Fact]
        public void NoPluginCodeLocatesAProjectBepInThePluginDataFolder()
        {
            string src = Path.Combine(RepoRoot(), "StingTools");
            var offenders = Directory.EnumerateFiles(src, "*.cs", SearchOption.AllDirectories)
                .Where(p => !p.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar)
                         && !p.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar))
                .SelectMany(p => File.ReadAllLines(p)
                    .Select((line, i) => (p, i, line))
                    .Where(t => !t.line.TrimStart().StartsWith("//") && DataFolderBep.IsMatch(t.line)))
                .Select(t => Path.GetRelativePath(src, t.p) + ":" + (t.i + 1) + "  " + t.line.Trim())
                .ToList();

            Assert.True(offenders.Count == 0,
                "these lines locate project_bep.json in the shared plugin data folder; " +
                "use BIMManagerEngine.GetBIMManagerFilePath(doc, \"project_bep.json\"):\n" +
                string.Join("\n", offenders));
        }

        /// <summary>The guard above is only worth something if its pattern matches the
        /// shapes that were actually in the code. Proven against the removed lines.</summary>
        [Theory]
        [InlineData("Status = File.Exists(Path.Combine(StingToolsApp.DataPath ?? \"\", \"project_bep.json\")) ? \"Complete\" : \"NotStarted\",")]
        [InlineData("System.IO.Path.Combine(StingToolsApp.DataPath ?? \"\", \"project_bep.json\"));")]
        [InlineData("string fallback = StingToolsApp.FindDataFile(\"project_bep.json\");")]
        [InlineData("OutputLocationHelper.WriteAllTextAtomic(Path.Combine(dataPath, \"project_bep.json\"),")]
        public void TheGuardMatchesTheRemovedShapes(string removedLine)
        {
            Assert.Matches(DataFolderBep, removedLine);
        }

        /// <summary>The KUT BEP is the generated document. The mobilisation workflow must
        /// not open the in-Revit wizard, which registers a second BEP in the CDE register.</summary>
        [Fact]
        public void KutMobilisationHasNoBepWizardStep()
        {
            string path = Path.Combine(RepoRoot(), "StingTools", "Data", "WORKFLOW_KUT_Mobilisation.json");
            var tags = JObject.Parse(File.ReadAllText(path))["steps"]
                .Select(s => (string)s["commandTag"]).ToList();
            Assert.NotEmpty(tags);
            Assert.DoesNotContain("GenerateBEP", tags);
            Assert.DoesNotContain("CreateBEP", tags);
        }
    }
}
