using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace StingTools.Tags.Tests
{
    /// <summary>
    /// T-10 — Families/AssemblyTitleBlocks/*.params.txt are the authoring
    /// spec for eight title-block families. The first drafts named eight
    /// distinct parameters that existed in no parameter file
    /// (ELC_PNL_VOLTAGE, ELC_GLAND_COUNT_NR, DISCIPLINE, PROJECT_REF_TXT,
    /// SUBMISSION_REV_TXT, SUBMISSION_DATE_TXT, APPROVED_BY_TXT and the detail
    /// item AUTHORITY_STAMP_AREA), so an author following them created
    /// family-local cells nothing could fill, schedule or tag.
    ///
    /// Every line under a "... PARAMETERS" heading must be either a PARAM
    /// name from MR_PARAMETERS.txt / Data/Parameters/*.txt, or explicitly
    /// "local:NAME". A line that does not parse at all also fails — an
    /// unparsed line is a name nobody checked.
    /// </summary>
    public class AssemblyTitleBlockStubTests
    {
        private static readonly Regex Header = new Regex(@"^[A-Z][A-Z0-9 \-/]*PARAMETERS\b");
        private static readonly Regex OtherHeader = new Regex(@"^[A-Z][A-Z0-9 \-/()]+$");
        private static readonly Regex Row = new Regex(@"^(local:)?([A-Za-z][A-Za-z0-9_]*)\s*\(");

        private static string RepoRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "Families", "AssemblyTitleBlocks")))
                dir = dir.Parent;
            Assert.True(dir != null, "Could not locate Families/AssemblyTitleBlocks");
            return dir.FullName;
        }

        internal static List<(string File, int Line, bool Local, string Name, string Raw)> ParseStubs(string root)
        {
            var rows = new List<(string, int, bool, string, string)>();
            foreach (var f in Directory.GetFiles(Path.Combine(root, "Families", "AssemblyTitleBlocks"), "*.params.txt"))
            {
                bool inSection = false;
                int n = 0;
                foreach (var raw in File.ReadLines(f))
                {
                    n++;
                    var line = raw.TrimEnd();
                    if (line.Length == 0 || line.StartsWith("#")) continue;
                    if (Header.IsMatch(line)) { inSection = true; continue; }
                    if (OtherHeader.IsMatch(line)) { inSection = false; continue; }
                    if (!inSection) continue;
                    var m = Row.Match(line);
                    rows.Add((Path.GetFileName(f), n, m.Success && m.Groups[1].Success,
                              m.Success ? m.Groups[2].Value : null, line));
                }
            }
            return rows;
        }

        [Fact]
        public void Every_stub_parameter_is_a_registry_parameter_or_marked_local()
        {
            var root = RepoRoot();
            var registry = TitleBlockTemplateTests.RegistryParamNames();
            Assert.True(registry.Count > 1000, $"registry looks empty ({registry.Count})");

            var rows = ParseStubs(root);
            Assert.True(rows.Count >= 60, $"only {rows.Count} stub rows parsed — the gate is not reading the files");

            var bad = rows
                .Where(r => r.Name == null || (!r.Local && !registry.Contains(r.Name)))
                .Select(r => r.Name == null
                    ? $"{r.File}:{r.Line} unparseable: '{r.Raw}'"
                    : $"{r.File}:{r.Line} '{r.Name}' is not in MR_PARAMETERS.txt / Data/Parameters/*.txt (mark it local: if it is family-local)")
                .ToList();
            Assert.True(bad.Count == 0, string.Join("\n", bad));
        }

        [Fact]
        public void Local_names_are_not_shadowing_a_registry_parameter()
        {
            // A local: marker on a name the registry DOES have would create a
            // family parameter with the same name as a shared one — two cells
            // that look identical and never agree.
            var registry = TitleBlockTemplateTests.RegistryParamNames();
            var clash = ParseStubs(RepoRoot()).Where(r => r.Local && registry.Contains(r.Name))
                .Select(r => $"{r.File}:{r.Line} local:{r.Name}").ToList();
            Assert.True(clash.Count == 0, string.Join("\n", clash));
        }
    }
}
