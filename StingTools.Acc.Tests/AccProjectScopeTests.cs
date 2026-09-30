// IM-18: the ACC container ids are a project's, not a machine's. The project's
// acc_settings.json wins; the machine credentials file is a visible fallback; and a
// save never copies one project's ids into the machine-wide file.

using System;
using System.IO;
using StingTools.V6;
using Xunit;

namespace StingTools.Acc.Tests
{
    public class AccProjectScopeTests : IDisposable
    {
        private readonly string _dir;
        private readonly string _path;

        public AccProjectScopeTests()
        {
            _dir = Path.Combine(Path.GetTempPath(), "sting-accscope-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
            _path = Path.Combine(_dir, AccOperatingPolicy.FileName);
        }

        public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

        private AccOperatingPolicy Settings(string json)
        {
            File.WriteAllText(_path, json);
            return AccOperatingPolicy.Load(_path);
        }

        private static AccCredentials Machine() => new AccCredentials
        {
            ClientId = "cid", RefreshToken = "rt",
            ProjectId = "b.machine", CoordContainerId = "c.machine",
            IssueTypeId = "type-for-machine-container", IssueSubtypeId = "sub",
        };

        [Fact]
        public void The_project_settings_file_reads_the_two_ids()
        {
            var p = Settings(@"{ ""projectId"": ""b.kut"", ""coordContainerId"": ""c.kut"" }");
            Assert.Equal(AccPolicySource.Loaded, p.Source);
            Assert.Equal("b.kut", p.ProjectId);
            Assert.Equal("c.kut", p.CoordContainerId);
        }

        [Fact]
        public void A_project_id_of_the_wrong_type_makes_the_file_malformed_not_ignored()
        {
            var p = Settings(@"{ ""projectId"": 42 }");
            Assert.Equal(AccPolicySource.Malformed, p.Source);
            Assert.Equal("", p.ProjectId);
        }

        [Fact]
        public void The_project_ids_win_and_per_container_caches_are_dropped()
        {
            var c = AccProjectScope.Apply(Machine(), Settings(@"{ ""projectId"": ""b.kut"" }"));
            Assert.Equal(AccProjectScopeSource.ProjectSettings, c.ProjectScope);
            Assert.Equal("b.kut", c.ProjectId);
            Assert.Equal("", c.CoordContainerId);        // then the Issues container is used
            Assert.Equal("b.kut", c.CoordContainer);
            Assert.Equal("", c.IssueTypeId);             // resolved for THIS container, not reused
            Assert.Equal("", c.IssueSubtypeId);
        }

        [Fact]
        public void The_same_container_keeps_its_cached_issue_type()
        {
            var c = AccProjectScope.Apply(Machine(), Settings(@"{ ""projectId"": ""b.machine"" }"));
            Assert.Equal("type-for-machine-container", c.IssueTypeId);
        }

        [Fact]
        public void With_no_project_ids_the_machine_file_is_NOT_used_but_is_shown()
        {
            // ACC-HARD-4: the one-release fallback is retired. The machine file's old ids are
            // reported (so a person can adopt them with an explicit Save), never acted on.
            var c = AccProjectScope.Apply(Machine(), Settings(@"{ ""unattended"": false }"));
            Assert.Equal(AccProjectScopeSource.None, c.ProjectScope);
            Assert.Equal("", c.ProjectId);
            Assert.Equal("", c.CoordContainer);
            Assert.Equal("", c.IssueTypeId);
            Assert.Equal("b.machine", c.LegacyProjectId);
            Assert.Contains("NOT used", AccProjectScope.Describe(c));

            var none = AccProjectScope.Apply(new AccCredentials(), null);
            Assert.Equal(AccProjectScopeSource.None, none.ProjectScope);
            Assert.DoesNotContain("remembers", AccProjectScope.Describe(none));
        }

        [Fact]
        public void Saving_never_writes_a_projects_ids_into_the_machine_file()
        {
            var c = AccProjectScope.Apply(Machine(), Settings(@"{ ""projectId"": ""b.kut"", ""coordContainerId"": ""c.kut"" }"));
            c.AccessToken = "fresh";                     // what a token refresh changes
            var j = AccIssueSync.ToMachineFile(c);
            Assert.Equal("b.machine", (string)j["ProjectId"]);
            Assert.Equal("c.machine", (string)j["CoordContainerId"]);
            Assert.Equal("type-for-machine-container", (string)j["IssueTypeId"]);
            Assert.Equal("fresh", (string)j["AccessToken"]);
            Assert.Null(j["ProjectScope"]);              // bookkeeping is not serialised
            Assert.Null(j["FileProjectId"]);
        }

        [Fact]
        public void Credentials_that_never_had_a_token_can_be_saved()
        {
            // AccessTokenExpiry is DateTime.MinValue until the first refresh. Serialising
            // the computed IsStale used to throw on it, so a first save wrote nothing.
            var c = new AccCredentials { ClientId = "cid", ClientSecret = "s" };
            var j = AccIssueSync.ToMachineFile(c);
            Assert.Equal("cid", (string)j["ClientId"]);
            Assert.Null(j["IsStale"]);
            Assert.True(c.IsStale);
        }

        [Fact]
        public void Without_a_project_scope_saving_still_leaves_the_machine_ids_alone()
        {
            var c = AccProjectScope.Apply(Machine(), null);
            c.ProjectId = "b.edited";
            Assert.Equal("b.machine", (string)AccIssueSync.ToMachineFile(c)["ProjectId"]);
        }

        [Fact]
        public void Credentials_read_without_a_project_are_saved_as_they_are()
        {
            // The keep-alive reads the machine file with no project; its save must round-trip.
            var c = Machine();
            c.AccessToken = "fresh";
            var j = AccIssueSync.ToMachineFile(c);
            Assert.Equal("b.machine", (string)j["ProjectId"]);
            Assert.Equal("fresh", (string)j["AccessToken"]);
        }
    }
}
