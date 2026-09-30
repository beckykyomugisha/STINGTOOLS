// Tests for WorksharedProjectRoot — a file-based workshared LOCAL COPY keeps its STING root
// beside the CENTRAL model for a new project, and never moves an existing one (ACC-HARD-3b).

using System;
using System.IO;
using StingTools.Core;
using Xunit;

namespace StingTools.Tags.Tests
{
    public class WorksharedProjectRootTests
    {
        private const string Local = @"C:\Users\ann\Documents\KUT_ARC_ann.rvt";
        private const string Central = @"\\srv\projects\KUT\KUT_ARC.rvt";
        private const string CentralDir = @"\\srv\projects\KUT";

        private static WorksharedRootInputs Ws(RootStampKind stamp = RootStampKind.None, string stampRel = null,
            bool existingLocal = false, bool interactive = false, Func<string, bool> exists = null, string mapping = null,
            string central = Central, bool workshared = true, bool cloud = false, string local = Local)
            => new WorksharedRootInputs
            {
                IsCloud = cloud, IsWorkshared = workshared, LocalPath = local, CentralPath = central,
                ProjectCode = "KUT", Stamp = stamp, CentralStampRelative = stampRel,
                HasExistingLocalRoot = existingLocal, MappingJson = mapping, Interactive = interactive,
                DirectoryExists = exists ?? (_ => true),
            };

        [Fact]
        public void Greenfield_local_copy_roots_beside_the_central()
        {
            var d = WorksharedProjectRoot.Decide(Ws());
            Assert.Equal(WorksharedRootKind.Central, d.Kind);
            Assert.Equal(Path.Combine(CentralDir, "KUT"), d.Root);
            Assert.Equal(CentralDir, d.CentralDir);
        }

        [Fact]
        public void Two_users_local_copies_resolve_to_one_root()
        {
            var a = WorksharedProjectRoot.Decide(Ws(local: @"C:\Users\ann\Documents\KUT_ARC_ann.rvt"));
            var b = WorksharedProjectRoot.Decide(Ws(local: @"D:\Work\KUT_ARC_bob.rvt"));
            Assert.Equal(a.Root, b.Root);
        }

        [Fact]
        public void Legacy_stamped_project_is_unchanged()
        {
            var d = WorksharedProjectRoot.Decide(Ws(stamp: RootStampKind.Local));
            Assert.Equal(WorksharedRootKind.NotApplicable, d.Kind);
            Assert.Null(d.Root);
        }

        [Fact]
        public void Existing_per_user_root_is_unchanged()
        {
            var d = WorksharedProjectRoot.Decide(Ws(existingLocal: true));
            Assert.Equal(WorksharedRootKind.NotApplicable, d.Kind);
        }

        [Fact]
        public void Central_stamp_wins_over_a_leftover_local_root_and_keeps_its_relative_path()
        {
            var d = WorksharedProjectRoot.Decide(Ws(stamp: RootStampKind.Central, stampRel: "STING_KUT", existingLocal: true));
            Assert.Equal(WorksharedRootKind.Central, d.Kind);
            Assert.Equal(Path.Combine(CentralDir, "STING_KUT"), d.Root);
        }

        [Fact]
        public void Non_workshared_and_cloud_and_central_itself_are_unchanged()
        {
            Assert.Equal(WorksharedRootKind.NotApplicable, WorksharedProjectRoot.Decide(Ws(workshared: false)).Kind);
            Assert.Equal(WorksharedRootKind.NotApplicable, WorksharedProjectRoot.Decide(Ws(cloud: true)).Kind);
            Assert.Equal(WorksharedRootKind.NotApplicable, WorksharedProjectRoot.Decide(Ws(local: Central)).Kind);
            Assert.Equal(WorksharedRootKind.NotApplicable,
                WorksharedProjectRoot.Decide(Ws(local: Central.ToUpperInvariant().Replace('\\', '/'))).Kind);
            Assert.Equal(WorksharedRootKind.NotApplicable, WorksharedProjectRoot.Decide(Ws(central: null)).Kind);
        }

        [Fact]
        public void Central_unreachable_interactive_prompts()
        {
            var d = WorksharedProjectRoot.Decide(Ws(interactive: true, exists: _ => false));
            Assert.Equal(WorksharedRootKind.PromptUser, d.Kind);
            Assert.Null(d.Root);
        }

        [Fact]
        public void Central_unreachable_unattended_refuses_never_the_local_folder()
        {
            var d = WorksharedProjectRoot.Decide(Ws(interactive: false, exists: _ => false));
            Assert.Equal(WorksharedRootKind.Refuse, d.Kind);
            Assert.Null(d.Root);
            Assert.Contains("unattended", d.Reason);
        }

        [Fact]
        public void Server_central_has_no_folder_and_is_treated_as_unreachable()
        {
            var d = WorksharedProjectRoot.Decide(Ws(central: "RSN://revitserver/KUT/KUT_ARC.rvt"));
            Assert.Equal(WorksharedRootKind.Refuse, d.Kind);
            Assert.Null(d.CentralDir);
        }

        [Fact]
        public void Mapping_for_the_central_is_honoured_and_malformed_mapping_refuses()
        {
            string key = WorksharedProjectRoot.KeyFor(Central);
            string json = "{ \"" + key.Replace(@"\", @"\\") + "\": \"D:\\\\Shared\\\\KUT\" }";
            var d = WorksharedProjectRoot.Decide(Ws(mapping: json, exists: p => p == @"D:\Shared\KUT"));
            Assert.Equal(WorksharedRootKind.Mapped, d.Kind);
            Assert.Equal(@"D:\Shared\KUT", d.Root);

            var bad = WorksharedProjectRoot.Decide(Ws(mapping: "{ nope", interactive: true));
            Assert.Equal(WorksharedRootKind.Refuse, bad.Kind);
            Assert.True(bad.MappingMalformed);
        }

        [Theory]
        [InlineData("central:KUT", RootStampKind.Central, "KUT")]
        [InlineData("CENTRAL:sub\\KUT", RootStampKind.Central, "sub\\KUT")]
        [InlineData("KUT", RootStampKind.Local, "KUT")]
        [InlineData("..\\..\\KUT", RootStampKind.Local, "..\\..\\KUT")]
        [InlineData("", RootStampKind.None, null)]
        [InlineData(null, RootStampKind.None, null)]
        public void Stamp_classification(string stamp, RootStampKind kind, string rel)
        {
            Assert.Equal(kind, WorksharedProjectRoot.ClassifyStamp(stamp, out string r));
            Assert.Equal(rel, r);
        }

        [Fact]
        public void A_hostile_central_stamp_cannot_escape_to_an_absolute_path()
        {
            Assert.Equal(Path.Combine(CentralDir, "KUT"), WorksharedProjectRoot.CentralRoot(CentralDir, "KUT", @"C:\Elsewhere"));
            Assert.Equal(Path.Combine(CentralDir, "KUT"), WorksharedProjectRoot.CentralRoot(CentralDir, "KUT", "x:y"));
        }
    }
}
