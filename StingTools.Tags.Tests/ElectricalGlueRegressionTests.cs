using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace StingTools.Tags.Tests
{
    /// <summary>
    /// Source guards for Revit-bound electrical glue that no Revit-free test can run
    /// (2026-10 electrical defect review). Each assertion names the defect it holds shut.
    /// </summary>
    public class ElectricalGlueRegressionTests
    {
        private static string Root()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "StingTools.addin"))) dir = dir.Parent;
            Assert.NotNull(dir);
            return dir.FullName;
        }

        private static readonly string[] ElectricalRoots =
        {
            "StingTools/Commands/Electrical", "StingTools/Commands/Panels", "StingTools/Core/Electrical",
            "StingTools/Core/Panels", "StingTools/Core/SLD",
        };

        private static IEnumerable<string> ElectricalSources()
        {
            string root = Root();
            foreach (var r in ElectricalRoots)
            {
                string d = Path.Combine(root, r.Replace('/', Path.DirectorySeparatorChar));
                if (!Directory.Exists(d)) continue;
                foreach (var f in Directory.EnumerateFiles(d, "*.cs", SearchOption.AllDirectories)) yield return f;
            }
        }

        private static readonly Regex Call = new Regex(@"\b(SetString|SetIfEmpty|StampForeign)\s*\(");
        private static readonly Regex CultureNumber = new Regex(
            @"\$""[^""]*\{[^}:]+:(?:0[.#0]*|F\d|N\d)\}|\.ToString\(""(?:0[.#0]*|F\d|N\d)""\)");

        /// <summary>
        /// A number written to a TEXT parameter with the machine's culture lands as "2,5" on a
        /// comma-decimal Windows locale, and the readers (GetDouble with thousands allowed) then
        /// read 25. ELC_FEEDER_CSA_MM2 and ELC_CBL_SZ_MM were written that way.
        /// </summary>
        [Fact]
        public void Electrical_writes_never_format_a_number_with_the_machine_culture()
        {
            var hits = new List<string>();
            foreach (var f in ElectricalSources())
            {
                var lines = File.ReadAllLines(f);
                for (int i = 0; i < lines.Length; i++)
                {
                    if (!Call.IsMatch(lines[i])) continue;
                    string text = lines[i];
                    for (int j = i + 1; !text.Contains(';') && j < Math.Min(lines.Length, i + 5); j++) text += lines[j];
                    if (CultureNumber.IsMatch(text) && !text.Contains("Invariant"))
                        hits.Add($"{Path.GetFileName(f)}:{i + 1}: {lines[i].Trim()}");
                }
            }
            Assert.True(hits.Count == 0, "Culture-dependent number written:\n" + string.Join("\n", hits));
        }

        /// <summary>
        /// Commands that write engineering values and then print how many they wrote must
        /// look at Transaction.Commit(): a rolled-back run printed "Stamped N" with nothing
        /// in the model. The other electrical commands are ROADMAP ELEC-28.
        /// </summary>
        [Theory]
        [InlineData("Commands/Panels/PanelComplianceAndBalanceCommands.cs", "STING Circuit Compliance Check")]
        [InlineData("Commands/Panels/PanelComplianceAndBalanceCommands.cs", "STING Phase Balance")]
        [InlineData("Commands/Electrical/FaultCurrent/FaultCurrentCommand.cs", "STING Stamp Fault Levels")]
        [InlineData("Commands/Electrical/FaultCurrent/FaultCurrentCommand.cs", "STING Stamp AIC Tiers")]
        [InlineData("Commands/Electrical/ElectricalPanelCommands.cs", "STING Electrical Param Sync")]
        [InlineData("Commands/Electrical/FeederSizing/FeederSizerCommand.cs", "STING Size Feeders")]
        [InlineData("Commands/Electrical/VoltageDrop/VoltageDropCommand.cs", "STING Stamp Voltage Drop")]
        [InlineData("Commands/Panels/PanelScheduleExcelCommands.cs", "STING Import Panel Schedules")]
        [InlineData("Commands/Panels/PanelTemplateCommands.cs", "STING Panel Schedule Templates")]
        [InlineData("Core/Panels/PanelScheduleApplyEngine.cs", "STING Batch Panel Schedules")]
        [InlineData("Core/Electrical/CableSizerApplyEngine.cs", "STING Cable Sizing")]
        [InlineData("Commands/Electrical/Import/AmtechImportCommand.cs", "STING Amtech Import")]
        [InlineData("Commands/Electrical/Import/EasyPowerImportCommand.cs", "STING EasyPower Import")]
        [InlineData("Commands/Electrical/Import/TrimbleImportCommand.cs", "STING Trimble Import")]
        public void Reporting_commands_check_the_commit_status(string file, string transactionName)
        {
            string src = File.ReadAllText(Path.Combine(Root(), "StingTools", file.Replace('/', Path.DirectorySeparatorChar)));
            var decl = new Regex(@"var\s+(\w+)\s*=\s*new\s+Transaction\(\s*\w+\s*,\s*""" + Regex.Escape(transactionName) + @"""").Match(src);
            Assert.True(decl.Success, $"transaction '{transactionName}' not found in {file}");
            // This transaction's own Commit() (not a SubTransaction's) is the first after it.
            // "status = tx.Commit();" or "status = cond ? tx.RollBack() : tx.Commit();" — the
            // statement assigns the result (an "==" comparison is not an assignment).
            var m = new Regex(@"(\b\w+\s*=(?!=)[^;{}]*?)?\b" + decl.Groups[1].Value + @"\.Commit\(\)\s*;").Match(src, decl.Index);
            Assert.True(m.Success, "no Commit() after the transaction");
            Assert.True(m.Groups[1].Success, $"{file}: '{transactionName}' commits without reading the status: {m.Value}");
        }

        /// <summary>
        /// Panel Audit printed "STING templates out of date: 0" when no template spec loaded —
        /// nothing had been compared.
        /// </summary>
        [Fact]
        public void Panel_audit_says_not_checked_when_no_template_spec_loaded()
        {
            string src = File.ReadAllText(Path.Combine(Root(), "StingTools", "Commands", "Panels", "PanelScheduleAuditCommand.cs"));
            var m = new Regex(@"MetricWarn\(""STING templates out of date"",(?<args>[^;]*);", RegexOptions.Singleline).Match(src);
            Assert.True(m.Success);
            Assert.Contains("specCount == 0", m.Groups["args"].Value);
        }

        /// <summary>
        /// A board looked up, listed or labelled by p.Name / fi.Name is looked up by its family
        /// TYPE name, which every board of that type shares: feeder sizes, wizard circuits and
        /// moved circuits all went to the first board of the type, and the SLD labelled every
        /// one of them alike. Boards are found by element id or BoardNames.Of (Panel Name).
        /// </summary>
        [Theory]
        [InlineData("Commands/Electrical/FeederSizing/FeederSizerCommand.cs")]
        [InlineData("Commands/Electrical/LoadDemand/LoadDemandAuditCommand.cs")]
        [InlineData("Commands/Electrical/CircuitCrudCommands.cs")]
        [InlineData("Commands/Electrical/CircuitWizard/CircuitWizardCommand.cs")]
        [InlineData("UI/CircuitWizardDialog.xaml.cs")]
        [InlineData("Core/SLD/SLDCircuitTraverser.cs")]
        public void Boards_are_not_matched_or_labelled_by_their_type_name(string file)
        {
            string src = File.ReadAllText(Path.Combine(Root(), "StingTools", file.Replace('/', Path.DirectorySeparatorChar)));
            var bad = new Regex(@"Equals\(\s*p\.Name\s*,|\bp\.Name\s*==|Select\(\s*p\s*=>\s*p\.Name\s*\)|Items\.Add\(\s*p\.Name|Label\s*=\s*fi\.Name\b");
            var hits = src.Split('\n').Select((l, i) => (l, i)).Where(x => bad.IsMatch(x.l))
                          .Select(x => $"{file}:{x.i + 1}: {x.l.Trim()}").ToList();
            Assert.True(hits.Count == 0, string.Join("\n", hits));
        }

        /// <summary>A TEXT conductor size read through GetDouble: "2,5" became 25 mm².</summary>
        [Fact]
        public void Text_conductor_sizes_are_not_read_with_GetDouble()
        {
            var rx = new Regex(@"GetDouble\([^)]*""(ELC_CBL_SZ_MM|ELC_FEEDER_CSA_MM2|ELC_CPC_SZ_MM|ELC_CKT_LENGTH_M)""");
            var hits = ElectricalSources().SelectMany(f => File.ReadAllLines(f).Select((l, i) => (f, l, i)))
                .Where(x => rx.IsMatch(x.l)).Select(x => $"{Path.GetFileName(x.f)}:{x.i + 1}").ToList();
            Assert.True(hits.Count == 0, string.Join("\n", hits));
        }

        /// <summary>
        /// Electrical Param Sync stamped the family TYPE name (shared by every board of the
        /// type) as each board's designation, wrote "230V" into a NUMBER parameter that
        /// refused it, and reported every board as synced.
        /// </summary>
        [Fact]
        public void Panel_param_sync_writes_the_panel_name_and_a_plain_voltage_and_counts_landed_writes()
        {
            string src = File.ReadAllText(Path.Combine(Root(), "StingTools", "Commands", "Electrical", "ElectricalPanelCommands.cs"));
            int a = src.IndexOf("class ElecPanelParamSyncCommand", StringComparison.Ordinal);
            int b = src.IndexOf("class ElecPanelWriteParamsCommand", StringComparison.Ordinal);
            Assert.True(a >= 0 && b > a);
            string body = src.Substring(a, b - a);
            Assert.DoesNotMatch(new Regex(@"ELC_PNL_NAME\s*,\s*p\.Name\b"), body);
            Assert.Contains("RBS_ELEC_PANEL_NAME", body);
            Assert.DoesNotContain("}V\"", body);
            Assert.DoesNotMatch(new Regex(@"updated\+\+;\s*\n\s*\}\s*\n\s*catch"), body);
            Assert.Contains("TransactionStatus", body);
        }
    }
}
