// Every StingToolsApp.FindDataFile("x") target must exist, or be a named,
// justified optional.
//
// FindDataFile returns NULL for a name it cannot resolve, and every caller
// handles null by skipping. That is correct for an optional overlay and silent
// death for a required file - and the two are indistinguishable from the call
// site.
//
// Measured 2026-09-21: ViewParameterMetadataCommand (TEMP-07) asked for
// "PARAMETER__CATEGORIES.csv" with TWO underscores. The file is
// PARAMETER_CATEGORIES.csv and always has been, so a command whose stated
// purpose was "activates the previously unused file" had never once read it.
// It reported "not found" and returned Failed, every time, for as long as it
// had existed - and nothing else noticed, because a command that fails loudly
// to the one person who runs it leaves no other trace.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace StingTools.Tags.Tests
{
    public class DataFileReferenceTests
    {
        /// <summary>
        /// Names FindDataFile is allowed not to find. Each one is optional by
        /// design and its caller falls back to something real - not to silence.
        /// Add to this list only with the reason written down.
        /// </summary>
        private static readonly Dictionary<string, string> KnownOptional =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["project_config.json"]             = "per-project overrides; TagConfig falls back to LoadDefaults()",
                ["QA_RULES.json"]                   = "CUSTOM rules, additive to the built-in set",
                ["default_clash_matrix.json"]       = "ClashMatrix.LoadOrDefault supplies the default",
                ["default_clash_rules.json"]        = "as above",
                ["STING_HVAC_CARBON_FACTORS.json"]  = "project factor OVERLAY; defaults are in the command",
                ["AEC_COMPLIANCE_RULES.csv"]        = "falls back to SeedDefaultRules(engine)",
                ["STING_TEMPLATE_RECIPES.json"]     = "corporate recipe overlay; RecipeEngine has built-ins",
                ["lps_compliance_report.docx"]      = "document template authored per project",
                ["lps_spd_spec.docx"]               = "as above",
                ["legionella_risk_assessment.docx"] = "as above",
            };

        private static DirectoryInfo RepoRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "StingTools", "Data")))
                dir = dir.Parent;
            return dir;
        }

        [Fact]
        public void EveryFindDataFileTargetResolvesOrIsAKnownOptional()
        {
            var root = RepoRoot();
            Assert.True(root != null, "repo root not found");

            string src = Path.Combine(root.FullName, "StingTools");
            string data = Path.Combine(src, "Data");

            var rx = new Regex(@"FindDataFile\(\s*""([^""]+)""", RegexOptions.Compiled);
            var refs = new Dictionary<string, string>(StringComparer.Ordinal);

            foreach (var f in Directory.GetFiles(src, "*.cs", SearchOption.AllDirectories))
            {
                if (f.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar)) continue;
                foreach (Match m in rx.Matches(File.ReadAllText(f)))
                {
                    string name = m.Groups[1].Value;
                    if (name.Contains("*")) continue;              // glob, resolved elsewhere
                    if (!refs.ContainsKey(name))
                        refs[name] = Path.GetFileName(f);
                }
            }

            Assert.True(refs.Count > 100, $"expected many data references, found {refs.Count}");

            var missing = new List<string>();
            foreach (var kv in refs)
            {
                string name = kv.Key;
                if (KnownOptional.ContainsKey(Path.GetFileName(name))) continue;

                // FindDataFile tries DataPath/name first, then searches
                // subdirectories by file name, so mirror both here.
                string direct = Path.Combine(data, name.Replace('/', Path.DirectorySeparatorChar));
                if (File.Exists(direct)) continue;
                if (Directory.GetFiles(data, Path.GetFileName(name), SearchOption.AllDirectories).Length > 0) continue;

                missing.Add($"{name}  (referenced from {kv.Value})");
            }

            Assert.True(missing.Count == 0,
                        $"{missing.Count} FindDataFile target(s) resolve to nothing, so the caller " +
                        "silently skips:\n  " + string.Join("\n  ", missing.OrderBy(x => x)));
        }

        [Fact]
        public void TheParameterCategoriesFileIsSpelledWithOneUnderscore()
        {
            // The specific regression. "PARAMETER__CATEGORIES" never existed.
            var root = RepoRoot();
            var offenders = Directory
                .GetFiles(Path.Combine(root.FullName, "StingTools"), "*.cs", SearchOption.AllDirectories)
                .Where(f => !f.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar))
                .Where(f => File.ReadAllText(f).Contains("PARAMETER__CATEGORIES"))
                .Select(Path.GetFileName)
                .ToList();

            Assert.True(offenders.Count == 0,
                        "double-underscore PARAMETER__CATEGORIES still referenced in: " +
                        string.Join(", ", offenders));
        }

        [Fact]
        public void KnownOptionalEntriesAreStillReferencedSomewhere()
        {
            // Keeps the allow-list honest: an entry for a name nobody asks for
            // any more is dead weight that hides the next real one.
            var root = RepoRoot();
            string src = Path.Combine(root.FullName, "StingTools");
            var all = new System.Text.StringBuilder();
            foreach (var f in Directory.GetFiles(src, "*.cs", SearchOption.AllDirectories))
            {
                if (f.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar)) continue;
                all.Append(File.ReadAllText(f));
            }
            string text = all.ToString();

            var stale = KnownOptional.Keys.Where(k => !text.Contains(k)).ToList();
            Assert.True(stale.Count == 0,
                        "allow-list names nothing references any more: " + string.Join(", ", stale));
        }
    }
}
