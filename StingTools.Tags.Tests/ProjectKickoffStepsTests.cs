// ══════════════════════════════════════════════════════════════════════════
//  ProjectKickoffStepsTests.cs — the gate on W2.
//
//  W2 puts four steps into ProjectKickoff at the seams the 2026-09-10 register
//  audit made visible. Two things need holding, and the second is a rule rather
//  than a feature:
//
//    1. The four are THERE, in the right places, with the read-only pair marked
//       optional. A read-only audit that can abort a 32-step TransactionGroup
//       after a project's whole catalogue has been built is worse than no audit.
//
//    2. Baseline_RenameTypes and Baseline_Apply are in NO preset, ever. Both are
//       destructive and both ask first, and a chained rename is what 2026-09-09
//       produced — 87 floor types proposed the identical name PLNS_SLB_RC100.
//       W1 made them RESOLVABLE, which is what a deliberate one-step workflow
//       needs; that is the same edit that makes it possible to drop one into a
//       26-step chain by accident. This test is the thing standing in the way.
//
//  It reads the engine's source rather than running it, for the reason
//  check_workflow_wiring.ps1 gives: the test project cannot reference
//  StingTools.csproj, which needs the Revit API.
//
//  RED / GREEN, recorded 2026-09-10 on this machine:
//    Baseline_RenameTypes added to ProjectKickoff
//      RED   "destructive command in a preset ... Baseline_RenameTypes"
//      GREEN neither tag appears in any preset, built-in or JSON
//    Materials_RegisterAudit's Optional = true removed
//      RED   "read-only audit ... is not optional"
//      GREEN both audits optional
// ══════════════════════════════════════════════════════════════════════════
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;
using Xunit.Abstractions;

namespace StingTools.Tags.Tests
{
    public class ProjectKickoffStepsTests
    {
        private readonly ITestOutputHelper _out;
        public ProjectKickoffStepsTests(ITestOutputHelper output) => _out = output;

        private static DirectoryInfo RepoRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "StingTools", "Core", "WorkflowEngine.cs")))
                dir = dir.Parent;
            Assert.True(dir != null, "Could not locate WorkflowEngine.cs from " + AppContext.BaseDirectory);
            return dir;
        }

        private static string Engine() =>
            File.ReadAllText(Path.Combine(RepoRoot().FullName, "StingTools", "Core", "WorkflowEngine.cs"));

        /// <summary>The body of one built-in preset's step list.</summary>
        private static string PresetBody(string src, string caseName)
        {
            int i = src.IndexOf("case \"" + caseName + "\":", StringComparison.Ordinal);
            Assert.True(i > 0, "built-in preset not found: " + caseName);
            int j = src.IndexOf("case \"", i + 10, StringComparison.Ordinal);
            if (j < 0) j = src.Length;
            return src.Substring(i, j - i);
        }

        private static List<string> Tags(string body) =>
            Regex.Matches(body, "CommandTag = \"([A-Za-z_0-9]+)\"")
                 .Select(m => m.Groups[1].Value).ToList();

        // ══════════════════════════════════════════════════════════════════════
        //  1. The rule with teeth
        // ══════════════════════════════════════════════════════════════════════

        [Theory]
        [InlineData("Baseline_RenameTypes")]
        [InlineData("Baseline_Apply")]
        public void A_Destructive_Command_Is_In_No_Preset(string tag)
        {
            // Built-in presets, in C#.
            string src = Engine();
            var inBuiltIn = Regex.Matches(src, "CommandTag = \"" + Regex.Escape(tag) + "\"")
                                 .Select(m => m.Value).ToList();
            Assert.True(inBuiltIn.Count == 0,
                "destructive command in a built-in preset: " + tag + ". Both ask first, and a "
              + "chained rename is what 2026-09-09 produced. W1 made it resolvable so a human "
              + "can write a deliberate one-step workflow — not so it can ride inside kickoff.");

            // Shipped JSON presets.
            string data = Path.Combine(RepoRoot().FullName, "StingTools", "Data");
            foreach (string f in Directory.GetFiles(data, "WORKFLOW_*.json"))
                Assert.False(File.ReadAllText(f).Contains("\"" + tag + "\"", StringComparison.Ordinal),
                    "destructive command in a preset: " + tag + " in " + Path.GetFileName(f));
        }

        // ══════════════════════════════════════════════════════════════════════
        //  2. The four steps, where they belong
        // ══════════════════════════════════════════════════════════════════════

        [Fact]
        public void Kickoff_Stamps_And_Classes_Materials_Right_After_Importing_Them()
        {
            var tags = Tags(PresetBody(Engine(), "ProjectKickoff"));

            int mep = tags.IndexOf("CreateMEPMaterials");
            int stamp = tags.IndexOf("Materials_StampCodes");
            int cls = tags.IndexOf("Materials_SetClass");
            int walls = tags.IndexOf("CreateWalls");

            Assert.True(mep >= 0 && stamp >= 0 && cls >= 0 && walls >= 0,
                "a step is missing: " + string.Join(", ", tags));

            // Between importing the register and building types FROM it. Before the
            // types, because a type built from an uncoded material is what the audit
            // found 138 of.
            Assert.Equal(mep + 1, stamp);
            Assert.Equal(stamp + 1, cls);
            Assert.True(cls < walls);
        }

        [Fact]
        public void Kickoff_Audits_The_Types_It_Just_Built()
        {
            var tags = Tags(PresetBody(Engine(), "ProjectKickoff"));
            int pipes = tags.IndexOf("CreatePipes");
            int audit = tags.IndexOf("Materials_RegisterAudit");
            Assert.True(pipes >= 0 && audit >= 0);
            Assert.Equal(pipes + 1, audit);
        }

        [Fact]
        public void Kickoff_Checks_Prod_Coverage_Before_Tagging_Not_After()
        {
            var tags = Tags(PresetBody(Engine(), "ProjectKickoff"));
            int prod = tags.IndexOf("Prod_CoverageAudit");
            int tag = tags.IndexOf("TagAndCombine");
            Assert.True(prod >= 0 && tag >= 0);
            Assert.True(prod < tag,
                "an unruled family takes a PROD code by category fallback, and PROD is what "
              + "the rate chain's most specific pass keys on. Naming them after tagging is "
              + "naming them too late.");
        }

        [Fact]
        public void The_Two_Read_Only_Audits_Are_Optional()
        {
            // They REPORT. Neither may abort a 32-step TransactionGroup that has already
            // built a project's whole catalogue.
            string body = PresetBody(Engine(), "ProjectKickoff");
            foreach (string tag in new[] { "Materials_RegisterAudit", "Prod_CoverageAudit" })
            {
                var m = Regex.Match(body,
                    "CommandTag = \"" + tag + "\"[^}]*?Optional = true", RegexOptions.Singleline);
                Assert.True(m.Success, "read-only audit " + tag + " is not optional — it can "
                                     + "abort the whole kickoff after the catalogue is built");
            }
        }

        [Fact]
        public void The_Two_Writing_Steps_Are_Gated_By_A_Condition_Not_By_A_Data_Hash()
        {
            // skipIfDataUnchanged compares a hash of the DEPLOYED data folder against a
            // sidecar. It says nothing about this model's materials, so a project that
            // gained 200 materials with the corporate CSVs untouched would SKIP the
            // stamp and report success. The conditions ask the model.
            string body = PresetBody(Engine(), "ProjectKickoff");

            Assert.Matches("CommandTag = \"Materials_StampCodes\"[\\s\\S]*?Condition = \"has_uncoded_materials\"", body);
            Assert.Matches("CommandTag = \"Materials_SetClass\"[\\s\\S]*?Condition = \"has_unclassed_materials\"", body);

            foreach (string tag in new[] { "Materials_StampCodes", "Materials_SetClass" })
            {
                var m = Regex.Match(body,
                    "CommandTag = \"" + tag + "\"[^}]*?SkipIfDataUnchanged", RegexOptions.Singleline);
                Assert.False(m.Success, tag + " uses skipIfDataUnchanged, which compares the "
                                      + "deployed data folder and not this model");
            }
        }

        [Fact]
        public void Kickoff_Grew_By_Exactly_Four_Steps()
        {
            var tags = Tags(PresetBody(Engine(), "ProjectKickoff"));
            _out.WriteLine(string.Join("\n", tags.Select((t, n) => $"{n + 1,2} {t}")));

            // 28 before W2 — NOT the 26 the brief said; counted from the source.
            Assert.Equal(32, tags.Count);
            Assert.Equal(4, tags.Count(t => t == "Materials_StampCodes" || t == "Materials_SetClass"
                                         || t == "Materials_RegisterAudit" || t == "Prod_CoverageAudit"));
            Assert.Equal(tags.Count, tags.Distinct().Count());   // no step added twice
        }

        [Fact]
        public void Every_Kickoff_Step_Has_A_Label()
        {
            // A step with no label reports as a blank line in the progress dialog and in
            // the run report, which is how a step nobody noticed gets added.
            string body = PresetBody(Engine(), "ProjectKickoff");
            int steps = Regex.Matches(body, "CommandTag = \"").Count;
            int labels = Regex.Matches(body, "Label = \"").Count;
            Assert.Equal(steps, labels);
        }
    }
}
