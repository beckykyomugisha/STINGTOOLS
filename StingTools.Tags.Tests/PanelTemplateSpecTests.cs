// ══════════════════════════════════════════════════════════════════════════
//  PanelTemplateSpecTests.cs — the STING standard panel schedule templates.
//
//  STING_PANEL_SCHEDULE_SPECS.json is turned into real Revit templates by
//  PanelTemplateBuilder. Everything that can go wrong WITHOUT Revit is held
//  here, against the SHIPPED files, so a bad edit fails a test rather than
//  producing a schedule column that is silently always empty:
//    - the spec parses and validates;
//    - every template is reachable: a rule in STING_PANEL_SCHEDULE_TEMPLATES.json
//      names it, and every board role maps to a rule;
//    - every shared parameter exists in MR_PARAMETERS.txt and is bound to the
//      category the cell reads (header/footer → Electrical Equipment, circuit
//      table → Electrical Circuits) in CATEGORY_BINDINGS.csv.
// ══════════════════════════════════════════════════════════════════════════
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using StingTools.Core.Panels;
using Xunit;

namespace StingTools.Tags.Tests
{
    public class PanelTemplateSpecTests
    {
        private static string DataDir()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "StingTools", "Data")))
                dir = dir.Parent;
            Assert.True(dir != null, "Could not locate StingTools/Data from " + AppContext.BaseDirectory);
            return Path.Combine(dir.FullName, "StingTools", "Data");
        }

        private static PanelTemplateSpecSet Shipped()
            => PanelTemplateSpecSet.Parse(File.ReadAllText(Path.Combine(DataDir(), "STING_PANEL_SCHEDULE_SPECS.json")));

        private static JObject Rules()
            => JObject.Parse(File.ReadAllText(Path.Combine(DataDir(), "STING_PANEL_SCHEDULE_TEMPLATES.json")));

        [Fact]
        public void Shipped_spec_parses_and_validates()
        {
            var set = Shipped();
            Assert.Equal(4, set.Templates.Count);
            Assert.Empty(set.Validate());
        }

        [Fact]
        public void Every_template_is_named_by_a_selection_rule()
        {
            var ruleNames = Rules()["rules"].Select(r => (string)r["templateName"]).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var t in Shipped().Templates)
                Assert.True(ruleNames.Contains(t.Name), $"No rule in STING_PANEL_SCHEDULE_TEMPLATES.json names '{t.Name}' — it would never be picked.");
        }

        [Theory]
        [InlineData(PanelBoardProfile.Switchboard)]
        [InlineData(PanelBoardProfile.ThreePhase)]
        [InlineData(PanelBoardProfile.SinglePhase)]
        [InlineData(PanelBoardProfile.Data)]
        public void Every_board_role_has_a_rule_and_a_spec(string role)
        {
            var rule = Rules()["rules"].FirstOrDefault(r => string.Equals((string)r["panelType"], role, StringComparison.OrdinalIgnoreCase));
            Assert.True(rule != null, $"No rule has panelType '{role}'.");
            string name = (string)rule["templateName"];
            Assert.Contains(Shipped().Templates, t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase));
        }

        [Fact]
        public void Every_shared_parameter_exists_and_is_bound_where_the_cell_reads_it()
        {
            var defined = new HashSet<string>(StringComparer.Ordinal);
            foreach (var line in File.ReadLines(Path.Combine(DataDir(), "MR_PARAMETERS.txt")))
            {
                var f = line.Split('\t');
                if (f.Length > 2 && f[0] == "PARAM") defined.Add(f[2]);
            }
            var bound = new HashSet<string>(StringComparer.Ordinal);
            foreach (var line in File.ReadLines(Path.Combine(DataDir(), "CATEGORY_BINDINGS.csv")))
            {
                var f = line.Split(',');
                if (f.Length > 1) bound.Add(f[0] + "|" + f[1]);
            }

            var failures = new List<string>();
            int checkedCount = 0;
            foreach (var t in Shipped().Templates)
                foreach (var (fields, category) in new[]
                         {
                             (t.Header, "Electrical Equipment"),
                             (t.Footer, "Electrical Equipment"),
                             (t.Body,   "Electrical Circuits"),
                         })
                    foreach (var fld in fields.Where(x => !x.IsBuiltIn && x.Category == null))
                    {
                        checkedCount++;
                        if (!defined.Contains(fld.ParamName))
                            failures.Add($"{t.Name}: '{fld.ParamName}' is not in MR_PARAMETERS.txt");
                        else if (!bound.Contains(fld.ParamName + "|" + category))
                            failures.Add($"{t.Name}: '{fld.ParamName}' is not bound to {category}");
                    }
            Assert.True(checkedCount > 0, "no shared parameters were checked — the test would pass vacuously");
            Assert.Empty(failures);
        }

        [Fact]
        public void Built_in_references_are_well_formed()
        {
            foreach (var t in Shipped().Templates)
                foreach (var f in t.Header.Concat(t.Body).Concat(t.Footer).Where(x => x.IsBuiltIn))
                    Assert.Matches("^[A-Z][A-Z0-9_]+$", f.ParamName);
        }

        [Theory]
        [InlineData(true, 3, "MSB-1", "Switchboard 2000A", PanelBoardProfile.Switchboard)]
        [InlineData(false, 3, "DB-L1", "Panelboard 208V", PanelBoardProfile.ThreePhase)]
        [InlineData(false, 1, "CU-1", "Consumer Unit", PanelBoardProfile.SinglePhase)]
        [InlineData(false, 3, "DATA-01", "Panelboard", PanelBoardProfile.Data)]
        [InlineData(false, 1, "L2 Comms Rack", "Cabinet", PanelBoardProfile.Data)]
        [InlineData(false, 3, "DISTRICT-DB", "Panelboard", PanelBoardProfile.ThreePhase)]
        [InlineData(null, 0, "DB-X", "Panelboard", null)]
        public void Board_role_comes_from_what_the_board_is(bool? isSwitchboard, int phases, string panelName, string typeName, string expected)
        {
            Assert.Equal(expected, PanelBoardProfile.RoleFor(isSwitchboard, phases, panelName, typeName));
        }

        [Fact]
        public void Project_override_replaces_by_name_and_adds_new()
        {
            var corporate = Shipped();
            var project = PanelTemplateSpecSet.Parse(@"{ ""templates"": [
                { ""name"": ""STING - Switchboard Schedule"", ""scheduleType"": ""Switchboard"", ""configuration"": ""OneColumn"",
                  ""body"": [ { ""heading"": ""Way"", ""param"": ""bip:RBS_ELEC_CIRCUIT_NUMBER"" } ] },
                { ""name"": ""Client - DB"", ""scheduleType"": ""Branch"", ""configuration"": ""OneColumn"",
                  ""body"": [ { ""heading"": ""Way"", ""param"": ""bip:RBS_ELEC_CIRCUIT_NUMBER"" } ] } ] }");
            var merged = PanelTemplateSpecSet.Merge(corporate, project);
            Assert.Equal(5, merged.Templates.Count);
            Assert.Single(merged.Templates.Single(t => t.Name == "STING - Switchboard Schedule").Body);
            Assert.Contains(merged.Templates, t => t.Name == "Client - DB");
        }

        [Fact]
        public void Validator_catches_unbuildable_specs()
        {
            var bad = PanelTemplateSpecSet.Parse(@"{ ""templates"": [
                { ""name"": ""A"", ""scheduleType"": ""Panelboard"", ""configuration"": ""TwoSections"", ""body"": [] },
                { ""name"": ""A"", ""scheduleType"": ""Branch"", ""configuration"": ""OneColumn"",
                  ""body"": [ { ""heading"": ""Way"", ""param"": ""bip:X"" }, { ""heading"": ""Way2"", ""param"": ""bip:X"" },
                              { ""heading"": """", ""param"": ""bip:Y"" } ] } ] }");
            var p = bad.Validate();
            Assert.Contains(p, x => x.Contains("defined 2 times"));
            Assert.Contains(p, x => x.Contains("scheduleType 'Panelboard'"));
            Assert.Contains(p, x => x.Contains("configuration 'TwoSections'"));
            Assert.Contains(p, x => x.Contains("body has no columns"));
            Assert.Contains(p, x => x.Contains("used in 2 columns"));
            Assert.Contains(p, x => x.Contains("has no label/heading"));
        }

        [Fact]
        public void Misspelt_or_missing_type_keys_fail_validation_instead_of_defaulting()
        {
            // "schedule_type" is not the key: Newtonsoft ignores it. With a default of
            // Branch a switchboard template would silently be built as a branch panel.
            var bad = PanelTemplateSpecSet.Parse(@"{ ""templates"": [
                { ""name"": ""SB"", ""schedule_type"": ""Switchboard"",
                  ""body"": [ { ""heading"": ""Way"", ""param"": ""bip:X"" } ] } ] }");
            var p = bad.Validate();
            Assert.Contains(p, x => x.Contains("scheduleType ''"));
            Assert.Contains(p, x => x.Contains("configuration ''"));
        }
    }
}
