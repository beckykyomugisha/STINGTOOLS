// Tests for AccDocsLifecycle — retiring a superseded deliverable's ACC document — over a
// real local HttpListener.
//
// What these guard:
//   * the copy request is the documented one: POST items?copyFrom=<encoded version URN>,
//     "b."-prefixed project, JSON:API body naming ONLY the target folder;
//   * a copy that fails, or a 2xx without item/version ids, changes nothing and says so;
//   * a retirement that copied but could not stamp the original is NOT reported as done;
//   * bad inputs never reach the network.

using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using StingTools.Acc.Tests.TestHelpers;
using StingTools.V6;
using Xunit;

namespace StingTools.Acc.Tests
{
    public class AccDocsLifecycleTests : IDisposable
    {
        private const string Token = "test-access-token";
        private const string Project = "c0337487-5b66-422b-a284-c273b424af54";   // no "b." — the client adds it
        private const string Source = "urn:adsk.wipprod:fs.folder:co.SHAREDfolder";
        private const string Archive = "urn:adsk.wipprod:fs.folder:co.ARCHIVEfolder";
        private const string Version = "urn:adsk.wipprod:fs.file:vf.OLDitem?version=3";
        private const string NewItem = "urn:adsk.wipprod:dm.lineage:NEWitem";
        private const string NewVersion = "urn:adsk.wipprod:fs.file:vf.NEWitem?version=1";

        public AccDocsLifecycleTests()
        {
            AccDocsMetadata.DelayHook = t => Task.CompletedTask;
        }

        public void Dispose()
        {
            AccDocsLifecycle.OverrideHostForTests(null);
            AccDocsMetadata.OverrideHostForTests(null);
            AccDocsMetadata.DelayHook = t => Task.Delay(t);
        }

        private static LoopbackServer Serve(Func<int, HttpListenerRequest, string, CannedResponse> h)
        {
            var s = new LoopbackServer((i, r) =>
            {
                string body;
                using (var sr = new StreamReader(r.InputStream, Encoding.UTF8)) body = sr.ReadToEnd();
                return h(i, r, body);
            });
            AccDocsLifecycle.OverrideHostForTests(s.BaseUrl);
            AccDocsMetadata.OverrideHostForTests(s.BaseUrl);
            return s;
        }

        private static string CopyCreated() => new JObject
        {
            ["data"] = new JObject
            {
                ["type"] = "items", ["id"] = NewItem,
                ["relationships"] = new JObject { ["tip"] = new JObject { ["data"] = new JObject { ["type"] = "versions", ["id"] = NewVersion } } },
            },
        }.ToString();

        private const string Defs = @"{""results"":[{""id"":11,""name"":""Suitability"",""type"":""string""},{""id"":12,""name"":""CDE State"",""type"":""string""}],""pagination"":{""totalResults"":2}}";
        private const string Echo = @"{""results"":[{""id"":11,""name"":""Suitability"",""type"":""string"",""value"":""AB""},{""id"":12,""name"":""CDE State"",""type"":""string"",""value"":""ARCHIVE""}]}";

        [Fact]
        public async Task Copy_IsTheDocumentedRequest()
        {
            string raw = null, method = null, ctype = null, sent = null;
            using var s = Serve((i, r, b) =>
            {
                raw = r.RawUrl; method = r.HttpMethod; ctype = r.ContentType; sent = b;
                return new CannedResponse(201, CopyCreated(), "application/vnd.api+json");
            });
            var res = await AccDocsLifecycle.CopyToFolderAsync(Token, Project, Version, Archive);

            Assert.True(res.Succeeded, res.Detail);
            Assert.Equal(NewItem, res.Value.itemUrn);
            Assert.Equal(NewVersion, res.Value.versionUrn);
            Assert.Equal("POST", method);
            Assert.StartsWith("/data/v1/projects/b.c0337487-5b66-422b-a284-c273b424af54/items?copyFrom=", raw);
            Assert.Contains("urn%3Aadsk.wipprod%3Afs.file%3Avf.OLDitem%3Fversion%3D3", raw);
            Assert.StartsWith("application/vnd.api+json", ctype);
            var body = JObject.Parse(sent);
            Assert.Equal("items", (string)body["data"]["type"]);
            Assert.Equal(Archive, (string)body["data"]["relationships"]["parent"]["data"]["id"]);
            Assert.Equal("folders", (string)body["data"]["relationships"]["parent"]["data"]["type"]);
        }

        // D9: the copy had its own HttpClient, so a throttled bulk supersede failed on the first 429.
        [Fact]
        public async Task Copy_RetriesAThrottle_ThenSucceeds()
        {
            var waits = new System.Collections.Generic.List<TimeSpan>();
            var saved = AccHttp.DelayHook;
            AccHttp.DelayHook = t => { lock (waits) waits.Add(t); return Task.CompletedTask; };
            try
            {
                int posts = 0;
                using var s = Serve((i, r, b) =>
                {
                    if (System.Threading.Interlocked.Increment(ref posts) == 1)
                    {
                        var throttled = new CannedResponse(429, "{\"detail\":\"slow down\"}");
                        throttled.Headers["Retry-After"] = "2";
                        return throttled;
                    }
                    return new CannedResponse(201, CopyCreated(), "application/vnd.api+json");
                });
                var res = await AccDocsLifecycle.CopyToFolderAsync(Token, Project, Version, Archive);
                Assert.True(res.Succeeded, res.Detail);
                Assert.Equal(2, posts);
                Assert.Contains(waits, w => w >= TimeSpan.FromSeconds(2));
            }
            finally { AccHttp.DelayHook = saved; }
        }

        [Fact]
        public async Task Copy_AHardRejection_IsNotRetried()
        {
            int posts = 0;
            using var s = Serve((i, r, b) => { System.Threading.Interlocked.Increment(ref posts); return new CannedResponse(403, "{}"); });
            var res = await AccDocsLifecycle.CopyToFolderAsync(Token, Project, Version, Archive);
            Assert.False(res.Succeeded);
            Assert.Equal(1, posts);   // a POST is never repeated blind
        }

        [Fact]
        public async Task Copy_VersionFromIncluded_WhenNoTip()
        {
            using var s = Serve((i, r, b) => new CannedResponse(201,
                $@"{{""data"":{{""type"":""items"",""id"":""{NewItem}""}},""included"":[{{""type"":""versions"",""id"":""{NewVersion}""}}]}}"));
            var res = await AccDocsLifecycle.CopyToFolderAsync(Token, Project, Version, Archive);
            Assert.True(res.Succeeded, res.Detail);
            Assert.Equal(NewVersion, res.Value.versionUrn);
        }

        [Theory]
        [InlineData(403, AccFetchStatus.AuthFailed)]
        [InlineData(404, AccFetchStatus.NotFound)]
        [InlineData(500, AccFetchStatus.TransportFailed)]
        public async Task FailedCopy_ChangesNothingElse(int status, AccFetchStatus expected)
        {
            using var s = Serve((i, r, b) => new CannedResponse(status, "{}"));
            var res = await AccDocsLifecycle.RetireAsync(Token, Project, Version, Source, Archive, "AB");
            Assert.False(res.Ok);
            Assert.Equal(expected, res.Status);
            Assert.Equal(1, s.RequestCount);   // no stamping after a failed copy
            Assert.Contains("nothing was changed", res.Detail);
        }

        [Fact]
        public async Task A2xxWithoutIds_IsNotACopy()
        {
            using var s = Serve((i, r, b) => new CannedResponse(201, @"{""data"":{""type"":""items""}}"));
            var res = await AccDocsLifecycle.CopyToFolderAsync(Token, Project, Version, Archive);
            Assert.False(res.Succeeded);
            Assert.Equal(AccFetchStatus.TransportFailed, res.Status);
        }

        [Fact]
        public async Task Retire_CopiesThenStampsCopyAndOriginal()
        {
            using var s = Serve((i, r, b) => i switch
            {
                0 => new CannedResponse(201, CopyCreated()),
                1 => new CannedResponse(200, Defs),   // archive folder definitions
                2 => new CannedResponse(200, Echo),   // stamp the copy
                3 => new CannedResponse(200, Defs),   // source folder definitions
                _ => new CannedResponse(200, Echo),   // stamp the original
            });
            var res = await AccDocsLifecycle.RetireAsync(Token, Project, Version, Source, Archive, "ab");

            Assert.True(res.Ok, res.Detail);
            Assert.True(res.CopyStamped);
            Assert.True(res.OriginalStamped);
            Assert.Equal(NewItem, res.ArchivedItemUrn);
            Assert.Equal(5, s.RequestCount);
            Assert.Contains("vf.NEWitem", Uri.UnescapeDataString(s.Paths[2]));
            Assert.Contains("vf.OLDitem", Uri.UnescapeDataString(s.Paths[4]));
            Assert.Contains(":batch-update", s.Paths[4]);
        }

        [Fact]
        public async Task Retire_UsesTheProjectsOwnAttributeNames()
        {
            // A project that named its attributes differently (docsAttributeNames) must be stamped
            // under THOSE names; the defaults would find no definition on its folders.
            const string defs = @"{""results"":[{""id"":21,""name"":""KUT Status"",""type"":""string""},{""id"":22,""name"":""KUT CDE"",""type"":""string""}],""pagination"":{""totalResults"":2}}";
            const string echo = @"{""results"":[{""id"":21,""name"":""KUT Status"",""type"":""string"",""value"":""AR""},{""id"":22,""name"":""KUT CDE"",""type"":""string"",""value"":""ARCHIVE""}]}";
            using var s = Serve((i, r, b) => i switch
            {
                0 => new CannedResponse(201, CopyCreated()),
                1 => new CannedResponse(200, defs),
                2 => new CannedResponse(200, echo),
                3 => new CannedResponse(200, defs),
                _ => new CannedResponse(200, echo),
            });
            var names = AccAttributeNames.FromSettings(new System.Collections.Generic.Dictionary<string, string>
            {
                ["suitability"] = "KUT Status",
                ["cdeState"] = "KUT CDE",
            });
            var res = await AccDocsLifecycle.RetireAsync(Token, Project, Version, Source, Archive, "AR", names);
            Assert.True(res.Ok, res.Detail);
            Assert.True(res.CopyStamped && res.OriginalStamped);
        }

        [Fact]
        public async Task CopiedButOriginalNotStamped_IsNotDone()
        {
            using var s = Serve((i, r, b) => i switch
            {
                0 => new CannedResponse(201, CopyCreated()),
                1 => new CannedResponse(200, Defs),
                2 => new CannedResponse(200, Echo),
                _ => new CannedResponse(404, "{}"),   // source folder definitions unreadable
            });
            var res = await AccDocsLifecycle.RetireAsync(Token, Project, Version, Source, Archive, "AB");
            Assert.False(res.Ok);
            Assert.True(res.CopyStamped);
            Assert.False(res.OriginalStamped);
            Assert.Equal(NewItem, res.ArchivedItemUrn);   // the copy is reported so it can be finished
            Assert.Contains("3. stamp the original: FAILED", res.Detail);
        }

        [Fact]
        public async Task UnknownSourceFolder_ReportsOriginalNotStamped()
        {
            using var s = Serve((i, r, b) => i == 0 ? new CannedResponse(201, CopyCreated())
                                              : i == 1 ? new CannedResponse(200, Defs)
                                              : new CannedResponse(200, Echo));
            var res = await AccDocsLifecycle.RetireAsync(Token, Project, Version, "", Archive, "AR");
            Assert.False(res.Ok);
            Assert.Equal(3, s.RequestCount);
            Assert.Contains("NOT DONE", res.Detail);
        }

        [Theory]
        [InlineData("A1", Archive)]                                   // not a retirement code
        [InlineData("AB", "urn:adsk.wipprod:fs.file:vf.notAFolder")]  // not a folder URN
        [InlineData("AB", "")]
        public async Task BadInputs_NeverReachTheNetwork(string suit, string archive)
        {
            using var s = Serve((i, r, b) => new CannedResponse(201, CopyCreated()));
            var res = await AccDocsLifecycle.RetireAsync(Token, Project, Version, Source, archive, suit);
            Assert.False(res.Ok);
            Assert.Equal(0, s.RequestCount);
        }
    }
}
