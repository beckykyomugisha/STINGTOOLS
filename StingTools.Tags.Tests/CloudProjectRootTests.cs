// Tests for CloudProjectRoot — where a cloud model's STING project root lives (ACC-HARD-3).
//
// The failure these guard against: for a Revit cloud model Document.PathName is
// "Autodesk Docs://<project>/<model>.rvt", which has no directory, so the old resolution
// fell through to %USERPROFILE%\Documents\<CODE> — a private root per user and per
// machine, splitting the issue register, ACC settings and counters without any error.

using System;
using System.Collections.Generic;
using System.IO;
using StingTools.Core;
using Xunit;

namespace StingTools.Tags.Tests
{
    public class CloudProjectRootTests
    {
        private const string ProjGuid = "0b8f1c7e-1111-4a2b-9c3d-222233334444";
        private const string ModelGuid = "7d1e2f3a-5555-4b6c-8d7e-666677778888";
        private static readonly string Share = @"\\srv\projects\KUT";

        private static CloudRootInputs Cloud(string mappingJson, bool interactive, Func<string, bool> exists = null,
            string proj = ProjGuid, string model = ModelGuid)
            => new CloudRootInputs
            {
                IsCloud = true,
                PathName = "Autodesk Docs://KUT Temple/KUT_ARC.rvt",
                CloudProjectGuid = proj,
                ModelGuid = model,
                MappingJson = mappingJson,
                Interactive = interactive,
                DirectoryExists = exists ?? (_ => true),
            };

        [Fact]
        public void Local_model_is_not_touched()
        {
            // Everything a local file carries is irrelevant: the decision is NotCloud and no
            // root is proposed, so ProjectFolderEngine continues with its unchanged resolution.
            var d = CloudProjectRoot.Decide(new CloudRootInputs
            {
                IsCloud = false,
                PathName = @"C:\Projects\KUT\KUT_ARC.rvt",
                MappingJson = "{ \"" + ProjGuid + "\": \"" + Share.Replace(@"\", @"\\") + "\" }",
                Interactive = true,
            });
            Assert.Equal(CloudRootDecisionKind.NotCloud, d.Kind);
            Assert.Null(d.Root);
        }

        [Theory]
        [InlineData(@"C:\Projects\KUT\KUT_ARC.rvt", false)]
        [InlineData(@"\\server\share\KUT_ARC.rvt", false)]
        [InlineData("Autodesk Docs://KUT Temple/KUT_ARC.rvt", true)]
        [InlineData("BIM 360://KUT Temple/KUT_ARC.rvt", true)]
        [InlineData("", false)]
        public void Cloud_path_heuristic(string path, bool expected)
            => Assert.Equal(expected, CloudProjectRoot.LooksLikeCloudPath(path));

        [Fact]
        public void Cloud_mapped_resolves_to_the_recorded_folder()
        {
            string json = "{ \"" + ProjGuid.ToUpperInvariant() + "\": \"" + Share.Replace(@"\", @"\\") + "\" }";
            var d = CloudProjectRoot.Decide(Cloud(json, interactive: false));
            Assert.Equal(CloudRootDecisionKind.Mapped, d.Kind);
            Assert.Equal(Share, d.Root);
            Assert.Equal(ProjGuid, d.Key); // GUID case normalised
        }

        [Fact]
        public void Every_model_in_the_project_shares_the_project_key()
        {
            Assert.Equal(ProjGuid, CloudProjectRoot.KeyFor(ProjGuid, ModelGuid));
            Assert.Equal(ProjGuid, CloudProjectRoot.KeyFor(ProjGuid, Guid.NewGuid().ToString()));
            Assert.Equal("model:" + ModelGuid, CloudProjectRoot.KeyFor(null, ModelGuid));
            Assert.Equal("model:" + ModelGuid, CloudProjectRoot.KeyFor(Guid.Empty.ToString(), ModelGuid));
            Assert.Null(CloudProjectRoot.KeyFor("", " "));
        }

        [Fact]
        public void Cloud_unmapped_interactive_prompts()
        {
            var d = CloudProjectRoot.Decide(Cloud(null, interactive: true));
            Assert.Equal(CloudRootDecisionKind.PromptUser, d.Kind);
            Assert.Null(d.Root);
            Assert.Equal(ProjGuid, d.Key);
        }

        [Fact]
        public void Cloud_unmapped_unattended_refuses_and_never_proposes_a_per_user_folder()
        {
            var d = CloudProjectRoot.Decide(Cloud("{}", interactive: false));
            Assert.Equal(CloudRootDecisionKind.Refuse, d.Kind);
            Assert.Null(d.Root);
            Assert.Contains("unattended", d.Reason);
        }

        [Fact]
        public void Mapped_but_unreachable_is_not_a_hit()
        {
            string json = "{ \"" + ProjGuid + "\": \"" + Share.Replace(@"\", @"\\") + "\" }";
            var unattended = CloudProjectRoot.Decide(Cloud(json, interactive: false, exists: _ => false));
            Assert.Equal(CloudRootDecisionKind.Refuse, unattended.Kind);
            Assert.Contains("not reachable", unattended.Reason);
            var attended = CloudProjectRoot.Decide(Cloud(json, interactive: true, exists: _ => false));
            Assert.Equal(CloudRootDecisionKind.PromptUser, attended.Kind);
        }

        [Theory]
        [InlineData("not json at all")]
        [InlineData("[ \"a\", \"b\" ]")]
        [InlineData("{ \"x\": 42 }")]
        [InlineData("{ \"x\": ")]
        public void Malformed_mapping_refuses_even_when_interactive_and_is_never_overwritten(string bad)
        {
            var d = CloudProjectRoot.Decide(Cloud(bad, interactive: true));
            Assert.Equal(CloudRootDecisionKind.Refuse, d.Kind);
            Assert.True(d.MappingMalformed);

            string updated = CloudProjectRoot.WithMapping(bad, ProjGuid, Share, out string err);
            Assert.Null(updated);
            Assert.Contains("not overwriting", err);
            Assert.Null(CloudProjectRoot.WithoutMapping(bad, ProjGuid, out _));
        }

        [Fact]
        public void No_guid_at_all_refuses()
        {
            var d = CloudProjectRoot.Decide(Cloud(null, interactive: true, proj: null, model: null));
            Assert.Equal(CloudRootDecisionKind.Refuse, d.Kind);
            Assert.Null(d.Key);
        }

        [Theory]
        [InlineData("relative\\folder")]
        [InlineData("Autodesk Docs://KUT Temple")]
        [InlineData("")]
        public void Unusable_recorded_folder_is_rejected(string folder)
        {
            string json = "{ \"" + ProjGuid + "\": \"" + folder.Replace(@"\", @"\\") + "\" }";
            var d = CloudProjectRoot.Decide(Cloud(json, interactive: false));
            Assert.Equal(CloudRootDecisionKind.Refuse, d.Kind);
            Assert.Contains("unusable", d.Reason);
            Assert.Null(CloudProjectRoot.WithMapping("{}", ProjGuid, folder, out _));
        }

        [Fact]
        public void Writing_a_mapping_preserves_other_projects()
        {
            string other = Guid.NewGuid().ToString("D");
            string start = "{ \"" + other + "\": \"D:\\\\Other\" }";
            string updated = CloudProjectRoot.WithMapping(start, ProjGuid, Share, out string err);
            Assert.Null(err);
            var map = CloudProjectRoot.ParseMapping(updated, out string perr);
            Assert.Null(perr);
            Assert.Equal(@"D:\Other", map[other]);
            Assert.Equal(Share, map[ProjGuid]);

            string removed = CloudProjectRoot.WithoutMapping(updated, ProjGuid, out _);
            var map2 = CloudProjectRoot.ParseMapping(removed, out _);
            Assert.False(map2.ContainsKey(ProjGuid));
            Assert.True(map2.ContainsKey(other));
        }

        [Fact]
        public void Missing_or_blank_mapping_file_is_empty_not_malformed()
        {
            Assert.Empty(CloudProjectRoot.ParseMapping(null, out string e1));
            Assert.Null(e1);
            Assert.Empty(CloudProjectRoot.ParseMapping("   ", out string e2));
            Assert.Null(e2);
            Assert.NotNull(CloudProjectRoot.WithMapping(null, ProjGuid, Share, out _));
        }

        [Fact]
        public void Picked_folder_gets_the_project_code_like_a_local_model()
        {
            Func<string, bool> none = _ => false;
            Assert.Equal(Path.Combine(@"\\srv\projects", "KUT"),
                CloudProjectRoot.RootForPickedFolder(@"\\srv\projects", "KUT", none));
            // Already the root by name — do not nest KUT\KUT.
            Assert.Equal(@"\\srv\projects\KUT", CloudProjectRoot.RootForPickedFolder(@"\\srv\projects\KUT\", "KUT", none));
            // Already a STING root by content (different name) — use as is.
            Assert.Equal(@"D:\Shared\Temple",
                CloudProjectRoot.RootForPickedFolder(@"D:\Shared\Temple", "KUT",
                    p => p == Path.Combine(@"D:\Shared\Temple", "_data", "project_setup.json")));
        }
    }
}
