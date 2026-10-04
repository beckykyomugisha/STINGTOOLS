using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace StingTools.Tags.Tests
{
    /// <summary>
    /// KUT deep review API-2. ASS_CST_STALE_BOOL is YESNO (Integer storage) in MR_PARAMETERS.txt,
    /// but three writers only wrote it when its storage was String, so they did nothing: the
    /// BOQ build never cleared a stale row, a material swap never marked one, and a rate bump
    /// never flagged one. No exception, no log line.
    ///
    /// This guard covers every YESNO parameter: a LookupParameter of one that is gated on
    /// StorageType.String with no Integer branch nearby is a write that can never happen.
    /// </summary>
    public class YesNoParamStorageGuardTests
    {
        private static string Repo()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "StingTools", "StingTools.csproj")))
                dir = dir.Parent;
            Assert.True(dir != null, "could not locate StingTools/StingTools.csproj");
            return dir.FullName;
        }

        private static readonly Regex Lookup = new Regex(
            @"LookupParameter\(\s*(?:""(?<lit>[^""]+)""|ParamRegistry\.(?<con>\w+))\s*\)");

        public static List<string> Offenders(string source, string file, ISet<string> yesNo,
                                             IDictionary<string, string> consts)
        {
            var lines = source.Replace("\r\n", "\n").Split('\n');
            var bad = new List<string>();
            for (int i = 0; i < lines.Length; i++)
            {
                var m = Lookup.Match(lines[i]);
                if (!m.Success) continue;
                string name = m.Groups["lit"].Success ? m.Groups["lit"].Value
                    : (consts.TryGetValue(m.Groups["con"].Value, out var v) ? v : null);
                if (name == null || !yesNo.Contains(name)) continue;
                string window = string.Join("\n", lines.Skip(i).Take(4));
                if (window.Contains("StorageType.String") && !window.Contains("StorageType.Integer"))
                    bad.Add($"{file}:{i + 1} {name}");
            }
            return bad;
        }

        [Fact]
        public void TheStaleFlagIsYesNo()
        {
            var yes = YesNoNames();
            Assert.True(yes.Count > 100, "MR_PARAMETERS.txt parsed to almost no YESNO parameters");
            Assert.Contains("ASS_CST_STALE_BOOL", yes);
        }

        [Fact]
        public void TheDetectorCatchesTheOriginalDefect()
        {
            const string src = "var p = el.LookupParameter(\"ASS_CST_STALE_BOOL\");\n" +
                               "if (p != null && p.StorageType == StorageType.String) p.Set(\"1\");\n";
            var yes = new HashSet<string> { "ASS_CST_STALE_BOOL" };
            Assert.Single(Offenders(src, "x.cs", yes, new Dictionary<string, string>()));
            const string fixedSrc = "var p = el.LookupParameter(\"ASS_CST_STALE_BOOL\");\n" +
                                    "if (p.StorageType == StorageType.Integer) p.Set(1);\n" +
                                    "else if (p.StorageType == StorageType.String) p.Set(\"1\");\n";
            Assert.Empty(Offenders(fixedSrc, "x.cs", yes, new Dictionary<string, string>()));
        }

        [Fact]
        public void NoYesNoParameterIsWrittenOnlyAsText()
        {
            string repo = Repo();
            var yes = YesNoNames();
            var consts = Regex.Matches(File.ReadAllText(Path.Combine(repo, "StingTools", "Core", "ParamRegistry.cs")),
                    @"const\s+string\s+(\w+)\s*=\s*""([^""]+)""")
                .Cast<Match>()
                .GroupBy(m => m.Groups[1].Value)
                .ToDictionary(g => g.Key, g => g.First().Groups[2].Value);

            var sep = Path.DirectorySeparatorChar;
            var bad = Directory.EnumerateFiles(Path.Combine(repo, "StingTools"), "*.cs", SearchOption.AllDirectories)
                .Where(p => !p.Contains(sep + "obj" + sep) && !p.Contains(sep + "bin" + sep))
                .SelectMany(p => Offenders(File.ReadAllText(p), Path.GetFileName(p), yes, consts))
                .ToList();
            Assert.True(bad.Count == 0, "YESNO parameters gated on String storage only:\n" + string.Join("\n", bad));
        }

        private static HashSet<string> YesNoNames()
        {
            var path = Path.Combine(Repo(), "StingTools", "Data", "MR_PARAMETERS.txt");
            return new HashSet<string>(File.ReadLines(path)
                .Select(l => l.Split('\t'))
                .Where(f => f.Length > 3 && f[0] == "PARAM" && f[3] == "YESNO")
                .Select(f => f[2]));
        }
    }
}
