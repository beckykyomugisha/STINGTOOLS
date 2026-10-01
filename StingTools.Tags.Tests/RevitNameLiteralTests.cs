// DT-R11-G: names that reach Revit's name-creating APIs must be legal.
//
// Revit refuses an element name containing \ : { } [ ] | ; < > ? ` ~ (proven in a
// real Revit 2025 run, Drawing Self-Test round 11: ParameterFilterElement.Create
// refused 'STING - Struct: Concrete'). DT-R11-C routed the AEC filter library
// through RevitNameRules; the older template code still created 'STING - Disc:
// Mechanical', 'STING - Sys: HVAC', 'STING - Status: Existing', 'STING - QA: …'
// straight through ParameterFilterElement.Create and looked them up by the same
// raw string — so in a real model none of them could exist, and every overlay
// that looked one up silently did nothing.
//
// These are source scans: they read the plugin's .cs files, so they run without
// Revit. They are deliberately narrow — they check what can be checked from the
// text (a literal handed straight to a creating API; the name argument of every
// ParameterFilterElement.Create) and say so when they cannot.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using StingTools.Core.Drawing;
using Xunit;

namespace StingTools.Tags.Tests
{
    public class RevitNameLiteralTests
    {
        // Independent of RevitNameRules.Prohibited so a hole there cannot hide itself.
        private static readonly char[] RevitRefuses = { '\\', ':', '{', '}', '[', ']', '|', ';', '<', '>', '?', '`', '~' };

        private static string RepoRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "StingTools", "StingTools.csproj")))
                dir = dir.Parent;
            Assert.True(dir != null, "Could not locate StingTools/StingTools.csproj");
            return dir.FullName;
        }

        private static IEnumerable<(string Rel, string Text)> PluginSources()
        {
            var root = RepoRoot();
            var plugin = Path.Combine(root, "StingTools");
            foreach (var f in Directory.EnumerateFiles(plugin, "*.cs", SearchOption.AllDirectories))
            {
                var rel = Path.GetRelativePath(root, f).Replace('\\', '/');
                if (rel.Contains("/obj/") || rel.Contains("/bin/")) continue;
                if (DeliberateProbes.Contains(rel)) continue;
                yield return (rel, File.ReadAllText(f));
            }
        }

        /// <summary>
        /// Files that hand Revit a refused name on purpose, to ask whether it refuses it.
        /// The Drawing Self-Test's "k. Revit name probes" does exactly that inside its
        /// always-rolled-back TransactionGroup.
        /// </summary>
        private static readonly HashSet<string> DeliberateProbes = new HashSet<string>(StringComparer.Ordinal)
        {
            "StingTools/Commands/Drawing/DrawingSelfTestCommand.cs",
        };

        private static string Source(string rel) => File.ReadAllText(Path.Combine(RepoRoot(), rel));

        private static int LineOf(string text, int index) => text.Take(index).Count(c => c == '\n') + 1;

        // A C# string literal: regular "…" or interpolated $"…" (verbatim @"…" is not
        // used for names in this code base and is not parsed).
        private const string Lit = @"(\$?""(?:[^""\\\r\n]|\\.)*"")";

        /// <summary>
        /// The text Revit would see from a literal: escapes dropped, and for an
        /// interpolated string the holes removed (a hole's value is not a literal, and
        /// "{DateTime.Now:HHmm}" carries a format colon that never reaches the name).
        /// </summary>
        private static string LiteralText(string lit)
        {
            bool interp = lit.StartsWith("$", StringComparison.Ordinal);
            string body = interp ? lit.Substring(2, lit.Length - 3) : lit.Substring(1, lit.Length - 2);
            var sb = new StringBuilder();
            int depth = 0;
            for (int i = 0; i < body.Length; i++)
            {
                char c = body[i];
                if (c == '\\' && i + 1 < body.Length) { i++; continue; }
                if (interp)
                {
                    if (c == '{' && i + 1 < body.Length && body[i + 1] == '{' && depth == 0) { sb.Append('{'); i++; continue; }
                    if (c == '}' && i + 1 < body.Length && body[i + 1] == '}' && depth == 0) { sb.Append('}'); i++; continue; }
                    if (c == '{') { depth++; continue; }
                    if (c == '}') { depth--; continue; }
                    if (depth > 0) continue;
                }
                sb.Append(c);
            }
            return sb.ToString();
        }

        private static bool Legal(string s) => s.IndexOfAny(RevitRefuses) < 0;

        // APIs that give a Revit element a name, with the name as a direct literal argument.
        private static readonly Regex[] NamingCalls =
        {
            new Regex(@"\.Name\s*=\s*" + Lit),
            new Regex(@"\.Duplicate\(\s*" + Lit),
            new Regex(@"\b(?:ParameterFilterElement|SelectionFilterElement|Workset|Material|ViewSheet)\.Create\(\s*\w+\s*,\s*" + Lit),
            new Regex(@"\.NewSubcategory\(\s*[^,()]+,\s*" + Lit),
            new Regex(@"\bnew\s+(?:FillPattern|LinePattern)\(\s*" + Lit),
        };

        [Fact]
        public void No_literal_handed_to_a_naming_API_holds_a_character_Revit_refuses()
        {
            var bad = new List<string>();
            int seen = 0;
            foreach (var (rel, text) in PluginSources())
                foreach (var rx in NamingCalls)
                    foreach (Match m in rx.Matches(text))
                    {
                        seen++;
                        var s = LiteralText(m.Groups[1].Value);
                        if (!Legal(s)) bad.Add($"{rel}:{LineOf(text, m.Index)}  {m.Value.Trim()}");
                    }
            Assert.True(seen > 20, $"The scan matched only {seen} naming call(s) — the patterns no longer fit the code.");
            Assert.True(bad.Count == 0,
                "Literal element name(s) Revit will refuse (wrap in RevitNameRules.Sanitize or rename):\n  "
                + string.Join("\n  ", bad));
        }

        // Same APIs, with the name in a local: "string viewName = $"… [{sheet}]"; v.Name = viewName;".
        private static readonly Regex[] NamingCallsByLocal =
        {
            new Regex(@"\.Name\s*=\s*([A-Za-z_]\w*)\s*;"),
            new Regex(@"\.Duplicate\(\s*([A-Za-z_]\w*)\s*\)"),
        };

        [Fact]
        public void No_literal_assigned_to_a_local_that_names_an_element_holds_a_character_Revit_refuses()
        {
            var bad = new List<string>();
            int seen = 0;
            foreach (var (rel, raw) in PluginSources())
            {
                var text = StripComments(raw);
                foreach (var rx in NamingCallsByLocal)
                    foreach (Match m in rx.Matches(text))
                    {
                        var id = m.Groups[1].Value;
                        foreach (Match a in Regex.Matches(text, @"\b" + Regex.Escape(id) + @"\s*=(?!=)\s*" + Lit + @"\s*;"))
                        {
                            seen++;
                            var s = LiteralText(a.Groups[1].Value);
                            if (!Legal(s)) bad.Add($"{rel}:{LineOf(text, a.Index)}  {a.Value.Trim()}");
                        }
                    }
            }
            Assert.True(seen > 5, $"The scan matched only {seen} named local(s) — the patterns no longer fit the code.");
            Assert.True(bad.Count == 0,
                "Element name(s) built from a literal Revit will refuse (wrap in RevitNameRules.Sanitize or rename):\n  "
                + string.Join("\n  ", bad.Distinct()));
        }

        /// <summary>Line comments removed, line breaks kept (so line numbers still hold).</summary>
        private static string StripComments(string text)
            => Regex.Replace(text, @"(?m)^[ \t]*//[^\r\n]*", string.Empty);

        // ── ParameterFilterElement.Create: the name argument must be provably legal ──

        /// <summary>Expressions that are known to return a Revit-legal name.</summary>
        private static readonly string[] Sanitisers =
        {
            "RevitNameRules.Sanitize(",
            "MaterialClassFilterName(",        // ProductionEdgeDecisions: Sanitize("STING_MAT_CLASS_" + c)
        };

        /// <summary>
        /// Call sites whose name cannot be sanitised without breaking something else,
        /// each with the reason. Keep this short and honest.
        /// </summary>
        private static readonly Dictionary<string, string> KnownUnsanitised = new Dictionary<string, string>
        {
            // VisibilityRuleMatcher.FilterName("STING VIS - ZONE=Z02") round-trips through
            // TryParseFilterName to recover the token value; sanitising a value that holds
            // a refused character would make the filter unrecognisable to Reset/Purge.
            // A refused name fails loudly at Create (caught and reported per rule).
            ["StingTools/Core/Visibility/VisibilityFilterBuilder.cs|pf.Name"] = "round-trip naming; reported on failure",
        };

        private static List<string> SplitArgs(string text, int openParen, out int close)
        {
            var args = new List<string>();
            var cur = new StringBuilder();
            int depth = 0; bool inStr = false;
            for (int i = openParen + 1; i < text.Length; i++)
            {
                char c = text[i];
                if (inStr)
                {
                    cur.Append(c);
                    if (c == '\\' && i + 1 < text.Length) { cur.Append(text[++i]); continue; }
                    if (c == '"') inStr = false;
                    continue;
                }
                if (c == '"') { inStr = true; cur.Append(c); continue; }
                if (c == '(' || c == '[' || c == '{') depth++;
                if (c == ')' || c == ']' || c == '}')
                {
                    if (depth == 0 && c == ')') { args.Add(cur.ToString().Trim()); close = i; return args; }
                    depth--;
                }
                if (c == ',' && depth == 0) { args.Add(cur.ToString().Trim()); cur.Clear(); continue; }
                cur.Append(c);
            }
            close = -1;
            return args;
        }

        private static bool ProvablyLegal(string rel, string text, string arg, out string why)
        {
            why = null;
            if (Sanitisers.Any(s => arg.Contains(s, StringComparison.Ordinal))) return true;
            var lit = Regex.Match(arg, "^" + Lit + "$");
            if (lit.Success)
            {
                var s = LiteralText(lit.Groups[1].Value);
                if (Legal(s) && !lit.Groups[1].Value.StartsWith("$", StringComparison.Ordinal)) return true;
                why = Legal(s) ? "interpolated literal (hole values unchecked)" : $"literal '{s}' holds a refused character";
                return false;
            }
            if (KnownUnsanitised.ContainsKey(rel + "|" + arg)) return true;
            if (Regex.IsMatch(arg, @"^[A-Za-z_]\w*$"))
            {
                // Every assignment to the identifier in this file must be sanitised or a
                // legal plain literal.
                var assigns = Regex.Matches(text, @"\b" + Regex.Escape(arg) + @"\s*=(?!=)\s*([^;]+);")
                    .Cast<Match>().Select(m => m.Groups[1].Value.Trim()).ToList();
                if (assigns.Count == 0) { why = $"'{arg}' is not assigned in this file"; return false; }
                foreach (var a in assigns)
                {
                    if (Sanitisers.Any(s => a.Contains(s, StringComparison.Ordinal))) continue;
                    var al = Regex.Match(a, "^" + Lit + "$");
                    if (al.Success && !al.Groups[1].Value.StartsWith("$", StringComparison.Ordinal)
                        && Legal(LiteralText(al.Groups[1].Value))) continue;
                    why = $"'{arg}' = {a}";
                    return false;
                }
                return true;
            }
            why = $"name argument '{arg}' is not sanitised";
            return false;
        }

        [Fact]
        public void Every_ParameterFilterElement_Create_names_its_filter_legally()
        {
            var bad = new List<string>();
            int seen = 0;
            foreach (var (rel, text) in PluginSources().Select(s => (s.Rel, StripComments(s.Text))))
                foreach (Match m in Regex.Matches(text, @"\bParameterFilterElement\.Create\s*\("))
                {
                    int open = m.Index + m.Length - 1;
                    var args = SplitArgs(text, open, out _);
                    if (args.Count < 2) continue;
                    seen++;
                    if (!ProvablyLegal(rel, text, args[1], out var why))
                        bad.Add($"{rel}:{LineOf(text, m.Index)}  {why}");
                }
            Assert.True(seen >= 8, $"Found only {seen} ParameterFilterElement.Create call(s) — the scan no longer fits the code.");
            Assert.True(bad.Count == 0,
                "ParameterFilterElement.Create with a name Revit may refuse (route it through RevitNameRules.Sanitize):\n  "
                + string.Join("\n  ", bad));
        }

        // ── The template filter set: creation and lookup meet on one Revit name ──

        private static List<string> CreatedTemplateFilterNames()
        {
            // DisciplineFilters + ParameterFilterDefs: tuples whose first item is a "STING - …" literal.
            var text = Source("StingTools/Temp/TemplateCommands.cs");
            return Regex.Matches(text, @"\(\s*""(STING - [^""]+)""\s*,")
                .Cast<Match>().Select(m => m.Groups[1].Value).Distinct().ToList();
        }

        [Fact]
        public void The_template_filters_are_created_under_legal_distinct_names()
        {
            var created = CreatedTemplateFilterNames();
            Assert.True(created.Count >= 40, $"Expected the template filter tables; found {created.Count} names.");
            Assert.Contains("STING - Disc: Mechanical", created);   // witness: the data keeps its human names
            Assert.All(created, n => Assert.True(Legal(RevitNameRules.Sanitize(n)), $"'{n}' -> '{RevitNameRules.Sanitize(n)}'"));
            var clashes = created.GroupBy(RevitNameRules.Sanitize, StringComparer.OrdinalIgnoreCase)
                .Where(g => g.Count() > 1).Select(g => string.Join(" | ", g)).ToList();
            Assert.True(clashes.Count == 0, "Sanitised template filter names collide:\n  " + string.Join("\n  ", clashes));
        }

        [Fact]
        public void Every_template_filter_name_looked_up_is_one_the_template_code_creates()
        {
            // PresentationStyleHelper and the template VG pass look filters up by the data
            // spelling. Through LookupComparer that spelling must find the element the
            // creation pass made under its sanitised name.
            var created = new HashSet<string>(
                CreatedTemplateFilterNames().Select(RevitNameRules.Sanitize), RevitNameRules.LookupComparer);
            var referenced = new List<string>();
            foreach (var rel in new[] { "StingTools/Temp/PresentationStyleHelper.cs", "StingTools/Temp/TemplateCommands.cs" })
                referenced.AddRange(Regex.Matches(Source(rel), @"""(STING - (?:Disc|Sys|Status|QA): [^""]+)""")
                    .Cast<Match>().Select(m => m.Groups[1].Value));
            Assert.True(referenced.Count > 30, $"Expected the overlay lookups; found {referenced.Count}.");
            var orphans = referenced.Distinct().Where(n => !created.Contains(n)).ToList();
            Assert.True(orphans.Count == 0, "Looked up but never created:\n  " + string.Join("\n  ", orphans));

            // ...and the dictionaries those lookups go through must actually use it. A plain
            // Dictionary keyed by e.Name answers "STING - Status: Existing" with nothing.
            var plain = new List<string>();
            foreach (var (rel, text) in PluginSources().Where(s => s.Rel.StartsWith("StingTools/Temp/", StringComparison.Ordinal)))
                foreach (Match m in Regex.Matches(text, @"new\s+Dictionary<string,\s*ParameterFilterElement>\(([^)]*)\)"))
                    if (!m.Groups[1].Value.Contains("RevitNameRules.LookupComparer", StringComparison.Ordinal))
                        plain.Add($"{rel}:{LineOf(text, m.Index)}  {m.Value}");
            Assert.True(plain.Count == 0,
                "Filter lookups keyed without RevitNameRules.LookupComparer:\n  " + string.Join("\n  ", plain));
        }

        [Fact]
        public void The_lookup_comparer_meets_the_data_spelling_and_keeps_legal_names_exact()
        {
            var cmp = RevitNameRules.LookupComparer;
            Assert.True(cmp.Equals("STING - Disc - Mechanical", "STING - Disc: Mechanical"));
            Assert.True(cmp.Equals("sting - status - existing", "STING - Status: Existing"));
            Assert.Equal(cmp.GetHashCode("STING - Sys - HVAC"), cmp.GetHashCode("STING - Sys: HVAC"));
            Assert.False(cmp.Equals("STING - Sys - HVAC", "STING - Sys: HWS"));
            Assert.True(cmp.Equals("STING - Mechanical", "sting - mechanical"));   // legal: plain OrdinalIgnoreCase
            Assert.False(cmp.Equals("STING - Mechanical", "STING - Mechanical Plan"));
            Assert.True(cmp.Equals(null, null));
            Assert.False(cmp.Equals("x", null));

            var lookup = new Dictionary<string, int>(cmp) { ["STING - Status - Existing"] = 7 };
            Assert.True(lookup.TryGetValue("STING - Status: Existing", out var v) && v == 7);
        }
    }
}
