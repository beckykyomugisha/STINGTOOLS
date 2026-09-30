// A2 — KUT_PushLifecycleGapsToAcc follows the project's ACC operating policy.
//
//   * "lifecycleGapEscalation" is strict: a cap is required, a stray sub-key is refused;
//   * an unattended run creates NOTHING without it, and at most maxCount with it;
//   * an interactive run always asks, capped;
//   * the issue type is this purpose's own ("Lifecycle" by name, or configured ids) and a
//     container with only a Clash type yields a refusal, never the clash subtype.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using StingTools.Acc.Tests.TestHelpers;
using StingTools.V6;
using Xunit;

namespace StingTools.Acc.Tests
{
    public class AccLifecycleGapPolicyTests : IDisposable
    {
        private readonly string _dir;
        private readonly string _path;

        public AccLifecycleGapPolicyTests()
        {
            _dir = Path.Combine(Path.GetTempPath(), "sting-accgap-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
            _path = Path.Combine(_dir, AccOperatingPolicy.FileName);
        }

        public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

        private AccOperatingPolicy Load(string json)
        {
            File.WriteAllText(_path, json);
            return AccOperatingPolicy.Load(_path);
        }

        // ── parsing ─────────────────────────────────────────────────────────

        [Fact]
        public void Absent_key_means_not_configured()
        {
            var p = Load("{\"unattended\": true}");
            Assert.Equal(AccPolicySource.Loaded, p.Source);
            Assert.Null(p.LifecycleGapMaxCount);
            Assert.Equal(string.Empty, p.LifecycleGapIssueTypeId);
        }

        [Fact]
        public void A_configured_cap_and_type_are_read()
        {
            var p = Load("{\"unattended\": true, \"lifecycleGapEscalation\": {\"maxCount\": 5, \"issueTypeId\": \"t-life\", \"issueSubtypeId\": \"s-life\"}}");
            Assert.Equal(AccPolicySource.Loaded, p.Source);
            Assert.Equal(5, p.LifecycleGapMaxCount);
            Assert.Equal("t-life", p.LifecycleGapIssueTypeId);
            Assert.Equal("s-life", p.LifecycleGapIssueSubtypeId);
        }

        [Theory]
        [InlineData("{\"lifecycleGapEscalation\": {}}", "maxCount")]                                  // no cap = no opt-in
        [InlineData("{\"lifecycleGapEscalation\": {\"maxCount\": 0}}", "greater than 0")]
        [InlineData("{\"lifecycleGapEscalation\": {\"maxCount\": \"5\"}}", "whole number")]
        [InlineData("{\"lifecycleGapEscalation\": {\"maxCount\": 5, \"maxcount\": 3}}", "maxcount")]   // typo'd sub-key
        [InlineData("{\"lifecycleGapEscalation\": {\"maxCount\": 5, \"issueSubtypeId\": \"s\"}}", "issueTypeId")]
        [InlineData("{\"lifecycleGapEscalation\": 5}", "object")]
        public void A_bad_value_makes_the_whole_file_malformed(string json, string named)
        {
            var p = Load(json);
            Assert.Equal(AccPolicySource.Malformed, p.Source);
            Assert.Contains(named, p.LoadError);
            Assert.Null(p.LifecycleGapMaxCount);   // nothing half-applied
        }

        // ── the plan ─────────────────────────────────────────────────────────

        [Fact]
        public void Unattended_without_a_policy_creates_nothing()
        {
            var plan = Load("{\"unattended\": true}").PlanLifecycleGaps(40);
            Assert.Equal(0, plan.Take);
            Assert.False(plan.AskFirst);
            Assert.Contains("no ACC issue was created", plan.Reason);
        }

        [Fact]
        public void Unattended_with_a_policy_creates_at_most_the_cap_without_asking()
        {
            var p = Load("{\"unattended\": true, \"lifecycleGapEscalation\": {\"maxCount\": 5}}");
            var plan = p.PlanLifecycleGaps(40);
            Assert.Equal(5, plan.Take);
            Assert.False(plan.AskFirst);
            Assert.Contains("35 left", plan.Reason);
            Assert.Equal(3, p.PlanLifecycleGaps(3).Take);
        }

        [Fact]
        public void Interactive_always_asks_and_is_capped()
        {
            var noPolicy = AccOperatingPolicy.Load(Path.Combine(_dir, "absent.json")).PlanLifecycleGaps(400);
            Assert.True(noPolicy.AskFirst);
            Assert.Equal(AccOperatingPolicy.LifecycleGapInteractiveFallbackCount, noPolicy.Take);

            var withPolicy = Load("{\"lifecycleGapEscalation\": {\"maxCount\": 7}}").PlanLifecycleGaps(400);
            Assert.True(withPolicy.AskFirst);
            Assert.Equal(7, withPolicy.Take);

            var nothing = Load("{\"lifecycleGapEscalation\": {\"maxCount\": 7}}").PlanLifecycleGaps(0);
            Assert.False(nothing.AskFirst);
            Assert.Equal(0, nothing.Take);
        }

        // ── the issue type ───────────────────────────────────────────────────

        private static List<JToken> Types(string json) => JArray.Parse(json).ToList();
        private const string ClashOnly =
            "[{\"id\":\"t-coord\",\"title\":\"Coordination\",\"subtypes\":[{\"id\":\"s-clash\",\"title\":\"Clash\"}]}]";
        private const string WithLifecycle =
            "[{\"id\":\"t-coord\",\"title\":\"Coordination\",\"subtypes\":[{\"id\":\"s-clash\",\"title\":\"Clash\"}]}," +
            " {\"id\":\"t-life\",\"title\":\"Lifecycle\",\"subtypes\":[{\"id\":\"s-spec\",\"title\":\"Spec gap\"},{\"id\":\"s-life\",\"title\":\"Lifecycle gap\"}]}]";

        [Fact]
        public void Lifecycle_is_chosen_by_name()
        {
            var c = IssueTypeChooser.ChooseByName(Types(WithLifecycle), "", new[] { "Lifecycle" }, "Lifecycle", "Lifecycle", "x");
            Assert.True(c.Ok, c.Reason);
            Assert.Equal("t-life", c.TypeId);
            Assert.Equal("s-life", c.SubtypeId);
        }

        [Fact]
        public void A_container_with_only_a_clash_type_is_refused_never_the_clash_subtype()
        {
            var c = IssueTypeChooser.ChooseByName(Types(ClashOnly), "", new[] { "Lifecycle" }, "Lifecycle", "Lifecycle", "the setting");
            Assert.False(c.Ok);
            Assert.NotEqual("s-clash", c.SubtypeId);
            Assert.Contains("Lifecycle", c.Reason);
            Assert.Contains("'Coordination'", c.Reason);   // names what IS there
        }

        [Fact]
        public async Task Resolve_over_the_wire_files_under_lifecycle_and_leaves_the_clash_cache_alone()
        {
            CredentialIsolation.Reset();
            AccHttp.DelayHook = _ => Task.CompletedTask;
            string typesBody = "{\"results\":" + WithLifecycle + "}";
            using var server = new LoopbackServer((_, req) =>
            {
                string path = WebUtility.UrlDecode(req.Url.AbsolutePath);
                if (path.EndsWith("/authentication/v2/token"))
                    return new CannedResponse(200, "{\"access_token\":\"fresh\",\"refresh_token\":\"rotated\",\"expires_in\":3600}");
                if (path.EndsWith("/issue-types")) return new CannedResponse(200, typesBody);
                return new CannedResponse(599, "{}");
            });
            AccIssueSync.OverrideHostForTests(server.BaseUrl);
            try
            {
                var creds = new AccCredentials
                {
                    ClientId = "c", ClientSecret = "s", RefreshToken = "r", AccessToken = "a",
                    AccessTokenExpiry = DateTime.UtcNow.AddMinutes(30), RefreshTokenIssuedAt = DateTime.UtcNow.AddDays(-1),
                    ProjectId = "b.11111111-2222-3333-4444-555555555555",
                    IssueTypeId = "t-coord", IssueSubtypeId = "s-clash",   // the clash escalation's cached choice
                };
                var r = await AccIssueSync.ResolveNamedIssueTypeAsync(creds, "", "", "Lifecycle", "x");
                Assert.True(r.Succeeded, r.Detail);
                Assert.Equal("s-life", r.Value);
                Assert.Equal("s-clash", creds.IssueSubtypeId);   // not overwritten

                typesBody = "{\"results\":" + ClashOnly + "}";
                var refused = await AccIssueSync.ResolveNamedIssueTypeAsync(creds, "", "", "Lifecycle", "x");
                Assert.False(refused.Succeeded);
                Assert.NotEqual("s-clash", refused.Value);

                var configured = await AccIssueSync.ResolveNamedIssueTypeAsync(creds, "t-x", "s-x", "Lifecycle", "x");
                Assert.Equal("s-x", configured.Value);
            }
            finally
            {
                AccIssueSync.OverrideHostForTests(null);
                AccHttp.DelayHook = t => Task.Delay(t);
            }
        }
    }
}
