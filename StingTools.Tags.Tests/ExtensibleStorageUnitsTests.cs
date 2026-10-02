// Extensible Storage: every floating-point field needs a spec, and every
// Get/Set on one needs a unit.
//
// Since Revit 2022 a double / float / XYZ / UV ES field declared without
// FieldBuilder.SetSpec makes SchemaBuilder.Finish() throw "Units are required
// for field <name>". Each STING GetOrCreate catches that, logs one WARN and
// returns null, so the schema never exists and every write behind it stores
// nothing. A real Revit 2025 run logged exactly that for StingViewCropSchema
// (field MarginMm) on every produced view; the same shape had silently disabled
// the compliance baseline, the cost-rate override (v1 and v2), the Fohlio v2
// snapshot and the PBR texture state.
//
// The plugin cannot be loaded here (Revit-bound), so this scans the source:
//   1. every AddSimpleField / AddArrayField / AddMapField of a floating type
//      chains .SetSpec(...);
//   2. every Entity.Set on such a field passes a third (unit) argument, and
//      every Entity.Get<double|float|XYZ|UV> passes a second one.
// A Set/Get without the unit throws at runtime even once the schema exists.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace StingTools.Tags.Tests
{
    public class ExtensibleStorageUnitsTests
    {
        private const string FloatTypes = @"(?:double|float|Double|Single|XYZ|UV)";

        private static IEnumerable<(string Rel, string Text)> PluginSources()
        {
            string root = Path.Combine(DrawingCatalogueFixture.RepoRoot(), "StingTools");
            foreach (var path in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
            {
                string rel = Path.GetRelativePath(root, path).Replace('\\', '/');
                if (rel.StartsWith("obj/") || rel.StartsWith("bin/")) continue;
                yield return (rel, StripLineComments(File.ReadAllText(path)));
            }
        }

        /// <summary>Drop // comments (outside string literals) so prose about the rule is not read as code.</summary>
        private static string StripLineComments(string src)
        {
            var sb = new System.Text.StringBuilder(src.Length);
            bool inStr = false, verbatim = false;
            for (int i = 0; i < src.Length; i++)
            {
                char c = src[i];
                if (inStr)
                {
                    sb.Append(c);
                    if (!verbatim && c == '\\' && i + 1 < src.Length) { sb.Append(src[++i]); continue; }
                    if (c == '"')
                    {
                        if (verbatim && i + 1 < src.Length && src[i + 1] == '"') { sb.Append(src[++i]); continue; }
                        inStr = false;
                    }
                    continue;
                }
                if (c == '"') { inStr = true; verbatim = i > 0 && src[i - 1] == '@'; sb.Append(c); continue; }
                if (c == '/' && i + 1 < src.Length && src[i + 1] == '/')
                {
                    while (i < src.Length && src[i] != '\n') i++;
                    sb.Append('\n');
                    continue;
                }
                sb.Append(c);
            }
            return sb.ToString();
        }

        /// <summary>Top-level argument count of the call whose '(' is at openIdx.</summary>
        private static int ArgCount(string s, int openIdx)
        {
            int depth = 0, commas = 0; bool inStr = false, any = false;
            for (int i = openIdx; i < s.Length; i++)
            {
                char c = s[i];
                if (inStr) { if (c == '\\') i++; else if (c == '"') inStr = false; continue; }
                if (c == '"') { inStr = true; any = true; continue; }
                if (c == '(' || c == '[' || c == '{') { depth++; continue; }
                if (c == ')' || c == ']' || c == '}')
                {
                    depth--;
                    if (depth == 0) return any ? commas + 1 : 0;
                    continue;
                }
                if (depth == 1)
                {
                    if (c == ',') commas++;
                    else if (!char.IsWhiteSpace(c)) any = true;
                }
            }
            return -1;
        }

        private static int Line(string s, int idx) => s.Take(idx).Count(ch => ch == '\n') + 1;

        private sealed class Scan
        {
            public readonly List<string> Violations = new();
            public int FloatFields;
            public int SchemaFiles;
        }

        private static Scan Run()
        {
            var r = new Scan();
            var addField = new Regex(@"\.Add(?:Simple|Array|Map)Field\s*\(", RegexOptions.Compiled);
            var floatType = new Regex(@"typeof\(\s*(?:System\.)?" + FloatTypes + @"\s*\)", RegexOptions.Compiled);

            foreach (var (rel, src) in PluginSources())
            {
                if (!src.Contains("SchemaBuilder") && !src.Contains("GetEntity(")) continue;
                if (src.Contains("SchemaBuilder")) r.SchemaFiles++;

                var floatFieldIds = new HashSet<string>(StringComparer.Ordinal);

                // Rule 1: floating fields carry a spec.
                foreach (Match m in addField.Matches(src))
                {
                    int end = src.IndexOf(';', m.Index);
                    if (end < 0) continue;
                    string stmt = src.Substring(m.Index, end - m.Index);
                    if (!floatType.IsMatch(stmt)) continue;
                    r.FloatFields++;
                    if (!stmt.Contains(".SetSpec("))
                        r.Violations.Add($"{rel}:{Line(src, m.Index)} floating ES field without SetSpec: {Squash(stmt)}");

                    var id = Regex.Match(stmt, @"Field\s*\(\s*(@?""[^""]*""|\w+)\s*,");
                    if (!id.Success) continue;
                    string first = id.Groups[1].Value;
                    if (first == "name")
                    {
                        // foreach (var name in new[] { FieldA, FieldB, ... }) sb.AddSimpleField(name, typeof(double))
                        int open = src.LastIndexOf("new[]", m.Index, StringComparison.Ordinal);
                        int brace = open < 0 ? -1 : src.IndexOf('{', open);
                        int close = brace < 0 ? -1 : src.IndexOf('}', brace);
                        if (close > 0 && close < m.Index)
                            foreach (var tok in src.Substring(brace + 1, close - brace - 1).Split(','))
                                if (tok.Trim().Length > 0) floatFieldIds.Add(tok.Trim());
                    }
                    else floatFieldIds.Add(first);
                }

                // Rule 2a: Set on a floating field passes a unit.
                foreach (var fid in floatFieldIds)
                {
                    foreach (Match m in Regex.Matches(src, @"\.Set\s*\(\s*" + Regex.Escape(fid) + @"\s*,"))
                    {
                        int open = src.IndexOf('(', m.Index);
                        if (ArgCount(src, open) < 3)
                            r.Violations.Add($"{rel}:{Line(src, m.Index)} Entity.Set on floating field {fid} without a unit");
                    }
                }

                // Rule 2b: Get<floating>(...) on an entity passes a unit.
                foreach (Match m in Regex.Matches(src, @"\w\s*\.\s*Get\s*<\s*" + FloatTypes + @"\s*>\s*\("))
                {
                    int open = m.Index + m.Length - 1;
                    if (ArgCount(src, open) < 2)
                        r.Violations.Add($"{rel}:{Line(src, m.Index)} Entity.Get of a floating field without a unit");
                }
            }
            return r;
        }

        private static string Squash(string s) => Regex.Replace(s, @"\s+", " ").Trim();

        [Fact]
        public void Every_floating_point_ES_field_has_a_spec_and_every_access_a_unit()
        {
            var scan = Run();

            // Non-vacuous: the scan must actually see the plugin's schemas and
            // its floating-point fields, or a broken walk would pass silently.
            Assert.True(scan.SchemaFiles >= 20, $"Only {scan.SchemaFiles} files with SchemaBuilder found — scan broken?");
            Assert.True(scan.FloatFields >= 10, $"Only {scan.FloatFields} floating ES fields found — scan broken?");

            Assert.True(scan.Violations.Count == 0,
                "Extensible Storage floating-point fields need FieldBuilder.SetSpec, and Entity.Get/Set on them " +
                "a unit (use StingEsUnits.Spec / StingEsUnits.Unit), or SchemaBuilder.Finish() throws " +
                "\"Units are required for field ...\" and nothing is ever stored:\n  " +
                string.Join("\n  ", scan.Violations));
        }

        [Fact]
        public void Scanner_flags_the_shapes_it_is_meant_to_catch()
        {
            // Rule self-test on synthetic source, so a regex regression cannot turn
            // the real check into a silent pass.
            Assert.Equal(2, ArgCount("(a, b)", 0));
            Assert.Equal(3, ArgCount("(F, Foo(x, y), UnitTypeId.General)", 0));
            Assert.Equal(1, ArgCount("(Field)", 0));
            Assert.Equal(0, ArgCount("()", 0));
            Assert.Equal(2, ArgCount("(\"a,b\", c)", 0));
            Assert.Equal("x\n", StripLineComments("x// sb.AddSimpleField(F, typeof(double))"));
            Assert.Equal("\"http://a\"", StripLineComments("\"http://a\""));
        }
    }
}
