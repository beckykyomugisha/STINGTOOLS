// P5: "Find my ACC project" wrote the new projectId/hubId/region and kept everything else - the
// previous project's coordination container, model set, issue type, upload folders and review
// workflow. The next Pull Clashes, issue push or upload then went to the OLD project's ids under
// the new project's name. A switch now clears them; a re-pick of the same project clears nothing.

using System.Linq;
using Newtonsoft.Json.Linq;
using StingTools.V6;
using Xunit;

namespace StingTools.Acc.Tests
{
    public class AccProjectSwitchTests
    {
        private static JObject Configured(string projectId) => new JObject
        {
            ["projectId"] = projectId,
            ["hubId"] = "b.hub-1",
            ["coordContainerId"] = "container-old",
            ["coordModelSetId"] = "set-old",
            ["coordModelSetName"] = "KUT federated",
            ["issueTypeId"] = "type-old",
            ["issueSubtypeId"] = "sub-old",
            ["folderUrn"] = "urn:adsk.wipprod:fs.folder:co.old",
            ["cdeFolders"] = new JObject { ["SHARED"] = "urn:adsk.wipprod:fs.folder:co.shared-old" },
            ["startAccReviewOnPublish"] = "workflow-old",
            ["lifecycleGapEscalation"] = new JObject { ["maxCount"] = 5, ["issueTypeId"] = "type-old", ["issueSubtypeId"] = "sub-old" },
            ["unattended"] = true,
        };

        [Fact]
        public void Switching_project_clears_every_id_that_belongs_to_the_old_project()
        {
            var edits = AccOperatingPolicy.ProjectChangeEdits(Configured("b.old-project"), "new-project");

            foreach (var k in AccOperatingPolicy.ProjectScopedKeys)
            {
                Assert.True(edits.ContainsKey(k), k + " was kept across the switch");
                Assert.Equal(JTokenType.Null, edits[k].Type);
            }
            var lg = Assert.IsType<JObject>(edits["lifecycleGapEscalation"]);
            Assert.Equal(5, (int)lg["maxCount"]);           // the opt-in survives
            Assert.Null(lg["issueTypeId"]);                 // the old project's type ids do not
            Assert.Null(lg["issueSubtypeId"]);
            Assert.False(edits.ContainsKey("unattended"));  // not project-scoped
            Assert.False(edits.ContainsKey("hubId"));       // written by the caller, not cleared here
        }

        [Theory]
        [InlineData("b.same-project", "same-project")]   // the b. prefix is not a different project
        [InlineData("same-project", "b.same-project")]
        [InlineData("same-project", "SAME-PROJECT")]
        public void Re_picking_the_same_project_clears_nothing(string stored, string chosen)
            => Assert.Empty(AccOperatingPolicy.ProjectChangeEdits(Configured(stored), chosen));

        [Fact]
        public void A_first_pick_with_no_project_before_clears_nothing()
        {
            var cfg = Configured("x");
            cfg.Remove("projectId");
            Assert.Empty(AccOperatingPolicy.ProjectChangeEdits(cfg, "new-project"));
            Assert.Empty(AccOperatingPolicy.ProjectChangeEdits(null, "new-project"));
        }

        [Fact]
        public void Only_keys_present_are_cleared()
        {
            var cfg = new JObject { ["projectId"] = "old", ["folderUrn"] = "urn:x" };
            var edits = AccOperatingPolicy.ProjectChangeEdits(cfg, "new");
            Assert.Equal(new[] { "folderUrn" }, edits.Keys.ToArray());
        }

        [Fact]
        public void Every_project_scoped_key_is_a_known_settings_key()
        {
            // KnownKeys is strict: a typo here would silently clear nothing.
            foreach (var k in AccOperatingPolicy.ProjectScopedKeys)
                Assert.Contains(k, AccOperatingPolicy.KnownKeys);
        }
    }
}
