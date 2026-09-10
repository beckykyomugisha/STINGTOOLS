// ══════════════════════════════════════════════════════════════════════════
//  WorkflowPresetOverrideTests.cs — the gate on W4.
//
//  Workflows were the only layered config in this codebase with no project
//  override: every project got the corporate 26-step kickoff or nothing. W4 adds
//  the layering DrawingTypeRegistry has had all along, and the two things that
//  can go wrong are both silent:
//
//    1. A project preset that does NOT replace the corporate one of the same
//       name. The user edits their file, runs the workflow, and gets the
//       corporate steps — with a report that says SUCCEEDED.
//    2. A project step whose tag resolves to nothing being DROPPED. A 12-step
//       workflow that runs 11 and says nothing is the same shape as a step keyed
//       "tag" instead of "commandTag", which Tier 1 of the wiring gate exists to
//       catch.
//
//  RED / GREEN, recorded 2026-09-10 on this machine:
//
//    A_Project_Preset_Replaces_The_Corporate_One_Of_The_Same_Name
//      RED   name match made case-sensitive   corporate 26 steps survive and the
//            project's 2-step override is APPENDED as a second entry
//      GREEN 1 preset named "Project Kickoff", 2 steps, from the project
//
//    An_Unresolvable_Tag_Is_Reported_And_The_Preset_Still_Loads
//      RED   unresolvable steps filtered out   3-step preset silently becomes 2,
//            UnresolvableSteps 0, no note
//      GREEN 3 steps kept, UnresolvableSteps 1, note names the tag
// ══════════════════════════════════════════════════════════════════════════
using System;
using System.Collections.Generic;
using System.Linq;
using StingTools.Core;
using Xunit;

namespace StingTools.Tags.Tests
{
    public class WorkflowPresetOverrideTests
    {
        private static WorkflowPreset Preset(string name, params string[] tags)
            => new WorkflowPreset
            {
                Name = name,
                IsBuiltIn = true,
                Steps = tags.Select(t => new WorkflowStep { CommandTag = t, Label = t }).ToList(),
            };

        private static ProjectPresetFile File(string fileName, WorkflowPreset p, string err = "")
            => new ProjectPresetFile { FileName = fileName, Preset = p, ParseError = err };

        /// <summary>Stands in for ResolveCommand: everything resolves except what is named.</summary>
        private static Func<string, bool> Resolves(params string[] unknown)
            => t => !unknown.Contains(t, StringComparer.OrdinalIgnoreCase);

        // ══════════════════════════════════════════════════════════════════════
        //  1. Replacement
        // ══════════════════════════════════════════════════════════════════════

        [Fact]
        public void A_Project_Preset_Replaces_The_Corporate_One_Of_The_Same_Name()
        {
            var corporate = new[]
            {
                Preset("Project Kickoff", "LoadParams", "CreateBLEMaterials", "CreateWalls"),
                Preset("Daily QA", "RetagStale"),
            };
            var project = new[] { File("WORKFLOW_PROJECT_KICKOFF.json",
                                       Preset("Project Kickoff", "LoadParams", "CreateWalls")) };

            var r = WorkflowPresetOverride.Merge(corporate, project, Resolves());

            Assert.Equal(2, r.Presets.Count);                       // replaced, not appended
            Assert.Equal(1, r.Replaced);
            Assert.Equal(0, r.Added);

            var kickoff = r.Presets.Single(p => p.Name == "Project Kickoff");
            Assert.Equal(2, kickoff.Steps.Count);                   // the PROJECT's steps
            Assert.False(kickoff.IsBuiltIn);
            Assert.Contains(r.Notes, n => n.Contains("comes from this project")
                                       && n.Contains("replacing the corporate 3"));

            // and the untouched one is untouched
            Assert.True(r.Presets.Single(p => p.Name == "Daily QA").IsBuiltIn);
        }

        [Fact]
        public void Replacement_Is_Whole_Not_Step_By_Step()
        {
            // A half-corporate half-project chain is a sequence nobody wrote. The
            // corporate step that is absent from the project file must be GONE.
            var corporate = new[] { Preset("K", "A", "B", "C") };
            var project = new[] { File("WORKFLOW_K.json", Preset("K", "B")) };

            var r = WorkflowPresetOverride.Merge(corporate, project, Resolves());
            var k = r.Presets.Single();
            Assert.Equal(new[] { "B" }, k.Steps.Select(s => s.CommandTag).ToArray());
        }

        [Fact]
        public void Name_Matching_Ignores_Case_And_Surrounding_Space()
        {
            var r = WorkflowPresetOverride.Merge(
                new[] { Preset("Project Kickoff", "A") },
                new[] { File("f.json", Preset("  project kickoff  ", "B")) },
                Resolves());

            Assert.Equal(1, r.Replaced);
            Assert.Single(r.Presets);
        }

        [Fact]
        public void A_New_Name_Is_Added_Rather_Than_Replacing_Anything()
        {
            var r = WorkflowPresetOverride.Merge(
                new[] { Preset("Project Kickoff", "A") },
                new[] { File("WORKFLOW_SITE_HANDOVER.json", Preset("Site Handover", "B")) },
                Resolves());

            Assert.Equal(2, r.Presets.Count);
            Assert.Equal(1, r.Added);
            Assert.Equal(0, r.Replaced);
            Assert.Contains(r.Notes, n => n.Contains("is a project workflow"));
        }

        // ══════════════════════════════════════════════════════════════════════
        //  2. A tag that resolves to nothing
        // ══════════════════════════════════════════════════════════════════════

        [Fact]
        public void An_Unresolvable_Tag_Is_Reported_And_The_Preset_Still_Loads()
        {
            var r = WorkflowPresetOverride.Merge(
                new[] { Preset("K", "A") },
                new[] { File("WORKFLOW_K.json", Preset("K", "LoadParams", "Matrials_StmpCodes", "CreateWalls")) },
                Resolves("Matrials_StmpCodes"));

            Assert.Equal(1, r.UnresolvableSteps);
            Assert.Contains(r.Notes, n => n.Contains("Matrials_StmpCodes")
                                       && n.Contains("resolves to nothing"));

            // NOT silently removed. A typo in step 2 is not a reason to withhold 1 and 3.
            var k = r.Presets.Single();
            Assert.Equal(3, k.Steps.Count);
            Assert.Contains(r.Notes, n => n.Contains("not silently removed"));
        }

        [Fact]
        public void A_Step_With_No_Tag_At_All_Is_Reported_Too()
        {
            var p = Preset("K", "LoadParams");
            p.Steps.Add(new WorkflowStep { Label = "does nothing" });

            var r = WorkflowPresetOverride.Merge(
                new[] { Preset("K", "A") }, new[] { File("f.json", p) }, Resolves());

            Assert.Equal(1, r.UnresolvableSteps);
            Assert.Contains(r.Notes, n => n.Contains("has no commandTag"));
        }

        [Fact]
        public void A_Broken_Resolver_Does_Not_Manufacture_Findings()
        {
            // If the check itself throws, the honest answer is "cannot say", not
            // "every step in your file is broken".
            var r = WorkflowPresetOverride.Merge(
                new[] { Preset("K", "A") },
                new[] { File("f.json", Preset("K", "X", "Y")) },
                t => throw new InvalidOperationException("resolver exploded"));

            Assert.Equal(0, r.UnresolvableSteps);
            Assert.Equal(1, r.Replaced);
        }

        [Fact]
        public void With_No_Resolver_The_Tag_Check_Is_Skipped_Not_Failed()
        {
            var r = WorkflowPresetOverride.Merge(
                new[] { Preset("K", "A") }, new[] { File("f.json", Preset("K", "X")) }, null);
            Assert.Equal(0, r.UnresolvableSteps);
            Assert.Equal(1, r.Replaced);
        }

        // ══════════════════════════════════════════════════════════════════════
        //  3. Refusals — an override must not remove a workflow by accident
        // ══════════════════════════════════════════════════════════════════════

        [Fact]
        public void An_Empty_Project_Preset_Cannot_Replace_A_Working_One()
        {
            var r = WorkflowPresetOverride.Merge(
                new[] { Preset("Project Kickoff", "A", "B") },
                new[] { File("f.json", new WorkflowPreset { Name = "Project Kickoff" }) },
                Resolves());

            Assert.Equal(1, r.Refused);
            Assert.Equal(0, r.Replaced);
            Assert.Equal(2, r.Presets.Single().Steps.Count);   // corporate survives
            Assert.Contains(r.Notes, n => n.Contains("has no steps"));
        }

        [Fact]
        public void A_Preset_That_Would_Not_Parse_Is_Named_Not_Ignored()
        {
            var r = WorkflowPresetOverride.Merge(
                new[] { Preset("K", "A") },
                new[] { File("WORKFLOW_BROKEN.json", null, "Unexpected character at line 4") },
                Resolves());

            Assert.Equal(1, r.Refused);
            Assert.Contains(r.Notes, n => n.Contains("WORKFLOW_BROKEN.json")
                                       && n.Contains("line 4"));
        }

        [Fact]
        public void A_Preset_With_No_Name_Is_Refused()
        {
            var r = WorkflowPresetOverride.Merge(
                new[] { Preset("K", "A") },
                new[] { File("f.json", Preset("   ", "B")) },
                Resolves());
            Assert.Equal(1, r.Refused);
            Assert.Contains(r.Notes, n => n.Contains("no \"name\""));
        }

        // ══════════════════════════════════════════════════════════════════════
        //  4. The no-project case must be the pre-W4 answer, exactly
        // ══════════════════════════════════════════════════════════════════════

        [Fact]
        public void With_No_Project_Files_Nothing_Changes_And_Nothing_Is_Said()
        {
            var corporate = new[] { Preset("A", "x"), Preset("B", "y") };

            foreach (var project in new IEnumerable<ProjectPresetFile>[]
                     { null, new ProjectPresetFile[0] })
            {
                var r = WorkflowPresetOverride.Merge(corporate, project, Resolves());
                Assert.Equal(2, r.Presets.Count);
                Assert.All(r.Presets, p => Assert.True(p.IsBuiltIn));
                Assert.Empty(r.Notes);
                Assert.False(r.ProjectHasSomethingToSay);
                Assert.Equal("", WorkflowPresetOverride.Summary(r));
            }
        }

        [Fact]
        public void The_Summary_Says_Nothing_When_There_Is_Nothing_To_Say()
        {
            // "0 replaced, 0 added" is noise that trains a reader to skip the line.
            Assert.Equal("", WorkflowPresetOverride.Summary(null));

            var r = WorkflowPresetOverride.Merge(
                new[] { Preset("K", "A") }, new[] { File("f.json", Preset("K", "B")) }, Resolves());
            Assert.Contains("1 replaced", WorkflowPresetOverride.Summary(r));
        }

        [Fact]
        public void Two_Project_Files_Naming_One_Preset_Both_Apply_In_Order()
        {
            // Last file wins, and BOTH are reported — two files claiming one name is a
            // data question, and reporting only the winner hides it.
            var r = WorkflowPresetOverride.Merge(
                new[] { Preset("K", "corp") },
                new[]
                {
                    File("WORKFLOW_K_a.json", Preset("K", "first")),
                    File("WORKFLOW_K_b.json", Preset("K", "second")),
                },
                Resolves());

            Assert.Equal(2, r.Replaced);
            Assert.Equal("second", r.Presets.Single().Steps.Single().CommandTag);
            Assert.Contains(r.Notes, n => n.Contains("WORKFLOW_K_a.json"));
            Assert.Contains(r.Notes, n => n.Contains("WORKFLOW_K_b.json"));
        }
    }
}
