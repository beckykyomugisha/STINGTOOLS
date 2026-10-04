using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace StingTools.Tags.Tests
{
    /// <summary>
    /// KUT deep review API-1/API-3. Revit refuses to build an Extensible Storage schema with a
    /// floating-point field that has no spec — "Units are required for field CompliancePct" is in
    /// the live plugin log of 2026-09-29 — so SchemaBuilder.Finish throws, GetOrCreate returns
    /// null, and nothing in that schema is ever stored. Six schemas had the defect, among them the
    /// Fohlio snapshot (so Fohlio_Audit could never see an import) and the compliance baseline.
    /// These are source guards: schemas cannot be built without Revit.
    /// </summary>
    public class EsSchemaUnitSpecTests
    {
        private static string Repo()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "StingTools", "StingTools.csproj")))
                dir = dir.Parent;
            Assert.True(dir != null, "could not locate StingTools/StingTools.csproj");
            return dir.FullName;
        }

        private static IEnumerable<string> PluginSources() =>
            Directory.EnumerateFiles(Path.Combine(Repo(), "StingTools"), "*.cs", SearchOption.AllDirectories)
                .Where(p => !p.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar)
                         && !p.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar));

        // One AddSimpleField statement, up to its terminating ';'.
        private static readonly Regex FloatField = new Regex(
            @"AddSimpleField\(\s*(?<name>\w+)\s*,\s*typeof\((?:double|float|XYZ|UV)\)\s*\)(?<rest>[^;]*);",
            RegexOptions.Singleline);

        public static List<string> FloatFieldsWithoutSpec(string source, string file)
            => FloatField.Matches(source).Cast<Match>()
                .Where(m => !m.Groups["rest"].Value.Contains("SetSpec("))
                .Select(m => $"{file}: AddSimpleField({m.Groups["name"].Value}, typeof(double)) has no SetSpec")
                .ToList();

        [Fact]
        public void EveryFloatingPointExtensibleStorageFieldHasASpec()
        {
            var offenders = new List<string>();
            int seen = 0;
            foreach (var p in PluginSources())
            {
                string s = File.ReadAllText(p);
                seen += FloatField.Matches(s).Count;
                offenders.AddRange(FloatFieldsWithoutSpec(s, Path.GetFileName(p)));
            }
            // The instrument must find the fields it guards before its silence means anything.
            Assert.True(seen >= 10, $"found only {seen} floating-point ES fields; the scan is not reading the schemas");
            Assert.True(offenders.Count == 0, string.Join("\n", offenders));
        }

        [Fact]
        public void EveryDoubleReadFromAnEntityPassesAUnit()
        {
            // Get<double>(field) on a specced field throws at run time; it needs the unit overload.
            var rx = new Regex(@"\.Get<double>\(\s*\w+\s*\)");
            var offenders = PluginSources()
                .SelectMany(p => File.ReadAllLines(p).Select((l, i) => (p, i, l)))
                .Where(t => rx.IsMatch(t.l))
                .Select(t => $"{Path.GetFileName(t.p)}:{t.i + 1}  {t.l.Trim()}")
                .ToList();
            Assert.True(offenders.Count == 0, string.Join("\n", offenders));
        }

        [Fact]
        public void TheGuardRecognisesTheShapeThatShipped()
        {
            const string shipped = "sb.AddSimpleField(FieldUnitCost, typeof(double))\n" +
                                   "    .SetDocumentation(\"Fohlio procurement unit cost\");";
            Assert.Single(FloatFieldsWithoutSpec(shipped, "x"));
            const string fixedShape = "sb.AddSimpleField(FieldUnitCost, typeof(double)).SetSpec(SpecTypeId.Number)\n" +
                                      "    .SetDocumentation(\"Fohlio procurement unit cost\");";
            Assert.Empty(FloatFieldsWithoutSpec(fixedShape, "x"));
        }
    }
}
