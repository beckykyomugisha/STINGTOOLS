// Every category: the DISC / SYS / FUNC / PROD it defaults to must pass the
// validator's own cross-checks.
//
// The maps are read from TagConfig.Defaults.cs and the validator's tables from
// ISO19650Validator.cs (both reach Revit, so they cannot be compiled here). The
// decision rules are the Revit-free CategoryTokenDefaults the plugin calls, so
// this exercises the same code, not a copy of it.
//
// Measured before the 2026-09-27 fix: 18 categories defaulted to SYS=LPS (every
// wall, roof, foundation and generic model), Fire Alarm Devices carried the
// invalid DISC "FLS", and 7 categories failed a cross-check at their defaults.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using StingTools.Core;
using Xunit;

namespace StingTools.Tags.Tests
{
    public class CategoryTokenAuditTests
    {
        // ── source readers ─────────────────────────────────────────────────

        private static string Repo()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "StingTools", "Core")))
                dir = dir.Parent;
            Assert.True(dir != null, "could not locate StingTools/Core");
            return dir.FullName;
        }

        private static string Src(params string[] parts)
            => File.ReadAllText(Path.Combine(new[] { Repo() }.Concat(parts).ToArray()));

        private static string Block(string src, string start, string end)
        {
            int a = src.IndexOf(start, StringComparison.Ordinal);
            Assert.True(a >= 0, "not found: " + start);
            int b = src.IndexOf(end, a, StringComparison.Ordinal);
            Assert.True(b > a, "not found after " + start + ": " + end);
            return src.Substring(a, b - a);
        }

        private const string Pair = @"\{\s*""([^""]+)""\s*,\s*""([^""]+)""\s*\}";

        private static Dictionary<string, string> Map(string start, string end)
        {
            var d = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (Match m in Regex.Matches(Block(Src("StingTools", "Core", "TagConfig.Defaults.cs"), start, end), Pair))
                d[m.Groups[1].Value] = m.Groups[2].Value;
            return d;
        }

        private static readonly Lazy<Dictionary<string, string>> DiscMap =
            new(() => Map("DefaultDiscMap()", "DefaultSysMap()"));
        private static readonly Lazy<Dictionary<string, string>> ProdMap =
            new(() => Map("DefaultProdMap()", "DefaultFuncMap()"));
        private static readonly Lazy<Dictionary<string, string>> FuncMap =
            new(() => Map("DefaultFuncMap()", "DefaultLocCodes()"));

        /// <summary>SysMap in declaration order (order used to decide the default).</summary>
        private static readonly Lazy<List<KeyValuePair<string, List<string>>>> SysMap = new(() =>
        {
            string b = Block(Src("StingTools", "Core", "TagConfig.Defaults.cs"), "DefaultSysMap()", "DefaultProdMap()");
            b = Regex.Replace(b, @"//[^\n]*", "");
            var list = new List<KeyValuePair<string, List<string>>>();
            foreach (Match m in Regex.Matches(b, @"\{\s*""([A-Z]+)"",\s*new List<string>\s*\{(.*?)\}\s*\}", RegexOptions.Singleline))
                list.Add(new(m.Groups[1].Value,
                    Regex.Matches(m.Groups[2].Value, @"""([^""]+)""").Select(x => x.Groups[1].Value).ToList()));
            return list;
        });

        private static List<string> SystemsListing(string cat)
            => SysMap.Value.Where(kv => kv.Value.Contains(cat)).Select(kv => kv.Key).ToList();

        private static string V() => Src("StingTools", "Core", "ISO19650Validator.cs");

        private static readonly Lazy<HashSet<string>> ValidDisc = new(() =>
            new HashSet<string>(Regex.Matches(Block(V(), "_builtInDiscCodes", "};"), @"""([A-Z]+)""")
                .Select(m => m.Groups[1].Value)));

        private static Dictionary<string, HashSet<string>> SetTable(string start, string end)
        {
            var d = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (Match m in Regex.Matches(Block(V(), start, end),
                         @"\{\s*""([A-Z0-9]+)"",\s*new HashSet<string>(?:\([^)]*\))?\s*\{([^}]*)\}"))
                d[m.Groups[1].Value] = new HashSet<string>(
                    Regex.Matches(m.Groups[2].Value, @"""([A-Z0-9]+)""").Select(x => x.Groups[1].Value),
                    StringComparer.OrdinalIgnoreCase);
            return d;
        }

        private static readonly Lazy<Dictionary<string, HashSet<string>>> ValidFuncForSys = new(() =>
        {
            var d = SetTable("_validFuncsForSys =", "ProdCodesByDisc");
            foreach (string raw in Src("StingTools", "Data", "STING_FUNC_SYS_MATRIX.csv").Split('\n').Skip(1))
            {
                var f = raw.Trim().Split(',');
                if (f.Length < 3 || f[0].StartsWith("#")) continue;
                if (!d.TryGetValue(f[0], out var s)) d[f[0]] = s = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                s.Add(f[2].Trim());
            }
            return d;
        });

        private static readonly Lazy<Dictionary<string, HashSet<string>>> ValidSysForDisc =
            new(() => SetTable("_validSysForDisc =", "private static string ValidateProdForDisc"));
        private static readonly Lazy<Dictionary<string, HashSet<string>>> CuratedProdByDisc =
            new(() => SetTable("ProdCodesByDisc =", "_validSysForDisc"));

        private static readonly Lazy<Dictionary<string, HashSet<string>>> ProdVocab = new(() =>
        {
            var rules = new List<KeyValuePair<string, string>>();
            foreach (string raw in Src("StingTools", "Data", "STING_PROD_CODES.csv").Split('\n').Skip(1))
            {
                var f = raw.Trim().Split(',');
                if (f.Length < 3 || f[0].StartsWith("#")) continue;
                rules.Add(new(f[1].Trim(), f[0].Trim()));
            }
            return CategoryTokenDefaults.ProdVocabularyByDiscipline(DiscMap.Value, ProdMap.Value, rules);
        });

        // ── the validator's cross-checks, as ISO19650Validator.ValidateElement runs them ──

        private static List<string> CrossCheck(string cat, string disc, string sys, string func, string prod)
        {
            var errors = new List<string>();
            if (!ValidDisc.Value.Contains(disc)) errors.Add($"DISC {disc} is not a DISC code");

            string expected = CategoryTokenDefaults.SystemAwareDisc(DiscMap.Value[cat], sys, cat);
            if (expected != disc) errors.Add($"DISC mismatch, validator expects {expected}");

            string discForCat = DiscMap.Value[cat];
            bool sysOk = SystemsListing(cat).Contains(sys)
                      || sys == CategoryTokenDefaults.DisciplineDefaultSys(discForCat)
                      || (ValidSysForDisc.Value.TryGetValue(expected, out var sd) && sd.Contains(sys));
            if (!sysOk) errors.Add($"SYS {sys} not valid for the category");

            if (ValidFuncForSys.Value.TryGetValue(sys, out var vf) && vf.Count > 0 && !vf.Contains(func))
                errors.Add($"FUNC {func} not valid for SYS {sys}");

            if (!(prod is "GEN" or "SPE" or "MED" or "VFD" or "PMP")
                && !(ProdVocab.Value.TryGetValue(disc, out var own) && own.Contains(prod))
                && CuratedProdByDisc.Value.TryGetValue(disc, out var cur) && !cur.Contains(prod))
            {
                var other = CuratedProdByDisc.Value.FirstOrDefault(kv => kv.Key != disc && kv.Value.Contains(prod)).Key;
                if (other != null) errors.Add($"PROD {prod} reported as belonging to {other}");
            }
            return errors;
        }

        private static (string disc, string sys, string func, string prod) Defaults(string cat)
        {
            string d = DiscMap.Value[cat];
            string sys = CategoryTokenDefaults.ChooseCategorySys(SystemsListing(cat), d);
            if (string.IsNullOrEmpty(sys)) sys = CategoryTokenDefaults.DisciplineDefaultSys(d);
            string disc = CategoryTokenDefaults.SystemAwareDisc(d, sys, cat);
            string func = FuncMap.Value.TryGetValue(sys, out var f) ? f : "GEN";
            string prod = ProdMap.Value.TryGetValue(cat, out var p) ? p : "GEN";
            return (disc, sys, func, prod);
        }

        // ── tests ──────────────────────────────────────────────────────────

        [Fact]
        public void The_maps_were_actually_read()
        {
            Assert.True(DiscMap.Value.Count >= 100, "DiscMap looks empty");
            Assert.True(SysMap.Value.Count >= 15, "SysMap looks empty");
            Assert.True(ValidFuncForSys.Value.Count >= 15, "FUNC table looks empty");
            Assert.Contains("M", ValidDisc.Value);
        }

        [Fact]
        public void Every_category_default_passes_the_validator_cross_checks()
        {
            var failures = new List<string>();
            foreach (string cat in DiscMap.Value.Keys)
            {
                var t = Defaults(cat);
                foreach (string e in CrossCheck(cat, t.disc, t.sys, t.func, t.prod))
                    failures.Add($"{cat} [{t.disc}-{t.sys}-{t.func}-{t.prod}]: {e}");
            }
            Assert.True(failures.Count == 0, string.Join("\n", failures));
        }

        [Fact]
        public void No_category_defaults_to_a_name_detected_system()
        {
            // HV, BMS, RAD and MGS are read off names and systems; listing a category under
            // them makes them valid for it, never its default.
            var hits = DiscMap.Value.Keys.Where(c => new[] { "HV", "BMS", "RAD", "MGS", "CHW", "CDW", "REF", "SWD", "GWR", "RWH", "SDS", "SEP", "STW", "BGD", "SPH", "INT", "CMP", "POL", "LBW", "IRR", "FOL", "STM", "CON", "CHE" }.Contains(Defaults(c).sys)).ToList();
            Assert.True(hits.Count == 0, "Defaults to a name-detected system: " + string.Join(", ", hits));
        }

        [Fact]
        public void No_category_defaults_to_lightning_protection()
        {
            // LPS is applied by family name; listing a category under LPS makes it valid,
            // not the default.
            var lps = DiscMap.Value.Keys.Where(c => Defaults(c).sys == "LPS").ToList();
            Assert.True(lps.Count == 0, "Default SYS=LPS: " + string.Join(", ", lps));
        }

        [Theory]
        [InlineData("Walls", "ARC")]
        [InlineData("Roofs", "ARC")]
        [InlineData("Structural Foundations", "STR")]
        [InlineData("Generic Models", "GEN")]
        [InlineData("Electrical Equipment", "LV")]
        [InlineData("Pipes", "HVAC")]
        [InlineData("Plumbing Fixtures", "DCW")]
        [InlineData("Fire Alarm Devices", "FLS")]
        [InlineData("Communication Devices", "COM")]
        public void Category_default_system(string cat, string sys)
            => Assert.Equal(sys, Defaults(cat).sys);

        [Theory]
        // Plant on wet services, a basin on hot water, every pipe system.
        [InlineData("Mechanical Equipment", "HWS")]
        [InlineData("Mechanical Equipment", "DCW")]
        [InlineData("Mechanical Equipment", "DHW")]
        [InlineData("Mechanical Equipment", "GAS")]
        [InlineData("Mechanical Equipment", "SAN")]
        [InlineData("Mechanical Equipment", "FP")]
        [InlineData("Plumbing Fixtures", "DHW")]
        [InlineData("Plumbing Fixtures", "SAN")]
        [InlineData("Plumbing Equipment", "DHW")]
        [InlineData("Pipes", "DCW")]
        [InlineData("Pipes", "DHW")]
        [InlineData("Pipes", "HWS")]
        [InlineData("Pipes", "SAN")]
        [InlineData("Pipes", "RWD")]
        [InlineData("Pipes", "GAS")]
        [InlineData("Pipes", "FP")]
        [InlineData("Pipes", "MGS")]
        [InlineData("Electrical Equipment", "HV")]
        [InlineData("Electrical Equipment", "BMS")]
        [InlineData("Mechanical Control Devices", "BMS")]
        [InlineData("Walls", "RAD")]
        [InlineData("Specialty Equipment", "RAD")]
        [InlineData("Doors", "RAD")]
        [InlineData("Pipes", "CHW")]
        [InlineData("Pipes", "CDW")]
        [InlineData("Pipes", "REF")]
        [InlineData("Pipes", "SWD")]
        [InlineData("Pipes", "GWR")]
        [InlineData("Pipes", "RWH")]
        [InlineData("Pipes", "SDS")]
        [InlineData("Pipes", "SEP")]
        [InlineData("Pipes", "STW")]
        [InlineData("Pipes", "BGD")]
        [InlineData("Pipes", "SPH")]
        [InlineData("Pipes", "INT")]
        [InlineData("Pipes", "CMP")]
        [InlineData("Pipes", "POL")]
        [InlineData("Pipes", "LBW")]
        [InlineData("Pipes", "IRR")]
        [InlineData("Pipes", "FOL")]
        [InlineData("Pipes", "STM")]
        [InlineData("Pipes", "CON")]
        [InlineData("Pipes", "CHE")]
        [InlineData("Mechanical Equipment", "CHW")]
        [InlineData("Mechanical Equipment", "CDW")]
        [InlineData("Mechanical Equipment", "REF")]
        [InlineData("Plumbing Fixtures", "INT")]
        [InlineData("Plumbing Equipment", "SEP")]
        [InlineData("Plumbing Fixtures", "MGS")]
        [InlineData("Mechanical Equipment", "MGS")]
        [InlineData("Pipe Insulation", "DCW")]
        [InlineData("Fire Alarm Devices", "FP")]
        [InlineData("Electrical Equipment", "LPS")]
        [InlineData("Walls", "LPS")]
        public void A_detected_system_passes_the_validator(string cat, string sys)
        {
            string disc = CategoryTokenDefaults.SystemAwareDisc(DiscMap.Value[cat], sys, cat);
            string func = FuncMap.Value.TryGetValue(sys, out var f) ? f : "GEN";
            string prod = ProdMap.Value[cat];
            var errors = CrossCheck(cat, disc, sys, func, prod);
            Assert.True(errors.Count == 0, $"{cat} on {sys} [{disc}-{func}-{prod}]: " + string.Join("; ", errors));
        }

        [Theory]
        [InlineData("DCW", "P")]
        [InlineData("DHW", "P")]
        [InlineData("SAN", "P")]
        [InlineData("FP", "FP")]
        [InlineData("HWS", "M")]   // heating water is Mechanical; was P
        [InlineData("MGS", "P")]   // medical gas pipework is Plumbing
        [InlineData("CHW", "M")]
        [InlineData("REF", "M")]
        [InlineData("STM", "M")]
        [InlineData("SWD", "P")]
        [InlineData("GWR", "P")]
        [InlineData("INT", "P")]
        [InlineData("HVAC", "M")]
        public void Pipe_discipline_follows_the_system(string sys, string disc)
        {
            Assert.Equal(disc, CategoryTokenDefaults.SystemAwareDisc("M", sys, "Pipes"));
            Assert.Equal(disc, CategoryTokenDefaults.SystemAwareDisc("M", sys, "Pipe Insulation"));
        }

        [Fact]
        public void Every_PROD_rule_is_accepted_for_its_categorys_discipline()
        {
            var failures = new List<string>();
            foreach (string raw in Src("StingTools", "Data", "STING_PROD_CODES.csv").Split('\n').Skip(1))
            {
                var f = raw.Trim().Split(',');
                if (f.Length < 3 || f[0].StartsWith("#")) continue;
                string code = f[0].Trim(), cat = f[1].Trim();
                if (!DiscMap.Value.TryGetValue(cat, out string disc))
                {
                    failures.Add($"rule {code} names category '{cat}', which is not in DiscMap — it can never fire");
                    continue;
                }
                if (!ProdVocab.Value[disc].Contains(code)) failures.Add($"{cat} → {code}");
            }
            Assert.True(failures.Count == 0, string.Join("\n", failures));
        }

        [Theory]
        [InlineData("Domestic Hot Water", "Pipes", "DHW")]   // before HOT WATER → HWS
        [InlineData("DHW Secondary Return", "Pipes", "DHW")]
        [InlineData("LTHW Flow", "Pipes", "HWS")]
        public void Domestic_hot_water_names_map_to_DHW_before_heating(string name, string cat, string sys)
            => Assert.Equal(sys, SystemNameClassifier.FromSystemName(name, cat));

        [Fact]
        public void TagConfig_maps_system_names_through_the_classifier()
        {
            string body = Block(Src("StingTools", "Core", "TagConfig.cs"),
                                "private static string MapSystemNameToCode(", "public static HashSet<string> GetViewRelevantDisciplines");
            Assert.Contains("SystemNameClassifier.FromSystemName(", body);
            Assert.DoesNotContain("return \"", body);
        }

        [Fact]
        public void A_sewage_pump_is_drainage_not_cold_water()
        {
            string body = Block(Src("StingTools", "Core", "TagConfig.cs"),
                                "private static string GetSysFromFamilyName(", "private static string GetSysFromRoomType(");
            Assert.Matches(@"SEWAGE""\)\)\)\s*return ""SAN"";", body);
        }
    }
}
