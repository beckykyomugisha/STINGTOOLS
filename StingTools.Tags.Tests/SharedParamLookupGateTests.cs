using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace StingTools.Tags.Tests
{
    /// <summary>
    /// TAGFAM-6: shared-parameter definitions are looked up through
    /// <c>SharedParamDefinitionIndex</c> (one walk per opened file). Four private
    /// <c>FindSharedDefinition</c> copies each walked the whole ~3,300-definition file
    /// for every name, inside per-parameter and per-family loops; this keeps them gone.
    /// </summary>
    public class SharedParamLookupGateTests
    {
        [Fact]
        public void No_per_name_walk_of_the_shared_parameter_file_is_defined()
        {
            string root = Path.Combine(DrawingCatalogueFixture.RepoRoot(), "StingTools");
            var sep = Path.DirectorySeparatorChar;
            var files = Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
                .Where(f => !f.Contains(sep + "obj" + sep) && !f.Contains(sep + "bin" + sep))
                .ToList();
            Assert.True(files.Count > 1000, $"Only {files.Count} source files found — wrong root?");

            var offenders = files
                .Where(f => Regex.IsMatch(File.ReadAllText(f),
                    @"ExternalDefinition\s+FindSharedDefinition\s*\(\s*DefinitionFile"))
                .Select(Path.GetFileName)
                .ToList();
            Assert.True(offenders.Count == 0,
                "Use SharedParamDefinitionIndex.Build once + Find per name instead of a per-name walk: "
                + string.Join(", ", offenders));

            Assert.Contains(files, f => f.EndsWith("SharedParamDefinitionIndex.cs"));
        }
    }
}
