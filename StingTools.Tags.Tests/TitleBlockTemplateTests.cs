using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using StingTools.Core.Drawing;
using Xunit;

namespace StingTools.Tags.Tests
{
    /// <summary>
    /// T-5 — the title-block value-template grammar.
    ///
    /// <para><b>The defect.</b> Unknown <c>{token}</c>s were copied through
    /// literally and <c>{token}</c> substitution was skipped entirely when the
    /// dict was empty; TitleBlockParamMigrateCommand passed no dict, so
    /// "A-{lvl}-{seq:D3}" was stamped onto real title blocks. Meanwhile a
    /// <c>${Param}</c> whose parameter did not exist became "" and blanked the
    /// cell — and every shipped profile names the stem
    /// (<c>${PRJ_ORG_CLIENT_NAME}</c>) of a parameter registered as
    /// <c>PRJ_ORG_CLIENT_NAME_TXT</c>, so the applier blanked Client Name /
    /// Project Code / Originator / Company on every sheet.</para>
    /// </summary>
    public class TitleBlockTemplateTests
    {
        private static readonly Dictionary<string, string> Pi = new Dictionary<string, string>
        {
            { "PRJ_ORG_CLIENT_NAME_TXT", "The Church" },
            { "PRJ_ORG_PROJECT_CODE_TXT", "KUT" },
            { "PRJ_EMPTY_TXT", "" },
        };

        private static string Lookup(string n) => Pi.TryGetValue(n, out var v) ? v : null;

        private static Dictionary<string, string> Tok(params string[] kv)
        {
            var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < kv.Length; i += 2) d[kv[i]] = kv[i + 1];
            return d;
        }

        [Fact]
        public void Known_tokens_and_width_format_resolve()
        {
            var r = TitleBlockTemplate.Resolve("A-{lvl}-{seq:D3}", Lookup, Tok("lvl", "L02", "seq", "0007"));
            Assert.True(r.IsResolved);
            Assert.Equal("A-L02-007", r.Text);
        }

        [Fact]
        public void Missing_token_is_unresolved_never_literal_output()
        {
            var r = TitleBlockTemplate.Resolve("A-{lvl}-{seq:D3}", Lookup, Tok("seq", "7"));
            Assert.False(r.IsResolved);
            Assert.Equal(new[] { "{lvl}" }, r.UnresolvedTokens);
        }

        [Fact]
        public void Null_token_dict_leaves_every_token_unresolved()
        {
            // The Migrate path: no dict at all.
            var r = TitleBlockTemplate.Resolve("SP-{disc}-{sys}-{lvl}-{seq:D4}", Lookup, null);
            Assert.False(r.IsResolved);
            Assert.Equal(new[] { "{disc}", "{sys}", "{lvl}", "{seq:D4}" }, r.UnresolvedTokens);
        }

        [Fact]
        public void Stem_projectinfo_name_resolves_to_registry_TXT_parameter()
        {
            var r = TitleBlockTemplate.Resolve("${PRJ_ORG_CLIENT_NAME}", Lookup, null);
            Assert.True(r.IsResolved);
            Assert.Equal("The Church", r.Text);
        }

        [Fact]
        public void Unbound_projectinfo_is_unresolved_not_blank()
        {
            var r = TitleBlockTemplate.Resolve("${PRJ_NOT_BOUND}", Lookup, null);
            Assert.False(r.IsResolved);
            Assert.Equal(new[] { "PRJ_NOT_BOUND" }, r.MissingProjectInfo);
        }

        [Fact]
        public void Bound_but_empty_projectinfo_and_empty_token_are_deliberate_blanks()
        {
            var r = TitleBlockTemplate.Resolve("${PRJ_EMPTY_TXT}{mark}", Lookup, Tok("mark", ""));
            Assert.True(r.IsResolved);
            Assert.Equal("", r.Text);
            Assert.Equal(new[] { "mark" }, r.EmptyTokens);
        }

        [Fact]
        public void Mixed_template_resolves_projectinfo_and_tokens()
        {
            var r = TitleBlockTemplate.Resolve("${PRJ_ORG_PROJECT_CODE}-{disc}", Lookup, Tok("disc", "P"));
            Assert.True(r.IsResolved);
            Assert.Equal("KUT-P", r.Text);
        }

        [Theory]
        [InlineData("{{lvl}}", "{lvl}")]
        [InlineData("SET {{A}} {lvl}", "SET {A} L01")]
        public void Doubled_braces_are_literal(string template, string expected)
        {
            var r = TitleBlockTemplate.Resolve(template, Lookup, Tok("lvl", "L01"));
            Assert.True(r.IsResolved);
            Assert.Equal(expected, r.Text);
        }

        [Theory]
        [InlineData("A-{lvl")]      // unclosed
        [InlineData("A-}")]         // stray close
        [InlineData("A-{lvl:X3}")]  // bad format
        [InlineData("A-{}")]        // empty
        public void Malformed_braces_are_unresolved(string template)
        {
            var r = TitleBlockTemplate.Resolve(template, Lookup, Tok("lvl", "L01"));
            Assert.False(r.IsResolved);
        }

        [Fact]
        public void Plain_text_passes_through()
        {
            var r = TitleBlockTemplate.Resolve("FOR FABRICATION", Lookup, null);
            Assert.True(r.IsResolved);
            Assert.Equal("FOR FABRICATION", r.Text);
        }

        // ── Existing-sheet token recovery (Heal / Migrate / drift) ──

        [Fact]
        public void Context_stamp_supplies_level_and_mark()
        {
            var t = ExistingSheetTokens.Resolve("Level 02::123::North Wing::SB-A", "L99", 4, 12);
            Assert.Equal("Level 02", t.Level);
            Assert.Equal("North Wing", t.Mark);
            Assert.Equal(4, t.Seq);
        }

        [Fact]
        public void Level_falls_back_to_segment_stamp_and_seq_to_sheet_number()
        {
            var t = ExistingSheetTokens.Resolve(null, "L03", 0, 12);
            Assert.Equal("L03", t.Level);
            Assert.Null(t.Mark);   // unknown -> omitted, never blanked
            Assert.Equal(12, t.Seq);
        }

        [Fact]
        public void Nothing_provable_means_nothing_supplied()
        {
            var t = ExistingSheetTokens.Resolve("", "{lvl}", null, null);
            Assert.Null(t.Level);
            Assert.Null(t.Mark);
            Assert.Null(t.Seq);
        }

        [Fact]
        public void Context_parse_keeps_empty_segments_and_rejects_foreign_strings()
        {
            var c = SheetProductionContext.Parse("::::");
            Assert.NotNull(c);
            Assert.Equal("", c.Level);
            Assert.Null(SheetProductionContext.Parse("just text"));
        }

        // ── Shipped catalogue gate ──

        private static string DataDir()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "StingTools", "Data", "STING_DRAWING_TYPES.json")))
                dir = dir.Parent;
            Assert.True(dir != null, "Could not locate StingTools/Data");
            return Path.Combine(dir.FullName, "StingTools", "Data");
        }

        internal static HashSet<string> RegistryParamNames()
        {
            var data = DataDir();
            var files = new List<string> { Path.Combine(data, "MR_PARAMETERS.txt") };
            var pdir = Path.Combine(data, "Parameters");
            if (Directory.Exists(pdir)) files.AddRange(Directory.GetFiles(pdir, "*.txt"));
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var f in files)
                foreach (var line in File.ReadLines(f))
                {
                    if (!line.StartsWith("PARAM\t", StringComparison.Ordinal)) continue;
                    var cols = line.Split('\t');
                    if (cols.Length > 2) names.Add(cols[2].Trim());
                }
            return names;
        }

        [Fact]
        public void Every_shipped_projectinfo_reference_names_a_registry_parameter()
        {
            var registry = RegistryParamNames();
            Assert.NotEmpty(registry);
            var root = JObject.Parse(File.ReadAllText(Path.Combine(DataDir(), "STING_DRAWING_TYPES.json")));
            var rx = new Regex(@"\$\{([A-Za-z0-9_]+)\}");
            var bad = new List<string>();
            int seen = 0;
            foreach (var dt in root["drawingTypes"] ?? new JArray())
            {
                if (!(dt["titleBlockParams"] is JObject tbp)) continue;
                foreach (var prop in tbp.Properties())
                    foreach (Match m in rx.Matches(prop.Value?.ToString() ?? ""))
                    {
                        var name = m.Groups[1].Value;
                        if (name.StartsWith("MAT_", StringComparison.OrdinalIgnoreCase)) continue;
                        seen++;
                        // Resolve through the SAME candidate list the applier uses.
                        var r = TitleBlockTemplate.Resolve("${" + name + "}",
                            n => registry.Contains(n) ? "x" : null, null);
                        if (!r.IsResolved) bad.Add($"{dt["id"]}: {prop.Name} -> ${{{name}}}");
                    }
            }
            Assert.True(seen > 0, "no ${...} references found — gate is vacuous");
            Assert.True(bad.Count == 0,
                $"{bad.Count} ${{...}} reference(s) resolve to no registry parameter:\n" + string.Join("\n", bad.Distinct().Take(20)));
        }
    }
}
