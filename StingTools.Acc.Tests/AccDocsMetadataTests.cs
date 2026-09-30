// Tests for the ACC Docs custom-attribute client, driven over a real local HttpListener.
//
// What these guard:
//   * a failed or mis-shaped definitions listing is never "this folder has no attributes"
//     — that answer would make EnsureDefinitions create duplicates;
//   * definitions are only created when explicitly allowed, and every required name is
//     accounted for (existing / created / missing / type mismatch / failed);
//   * the batch-update request is exactly what the APS reference documents: project id
//     without "b.", URL-encoded version URN, a BARE array of {id, value};
//   * 429 honours Retry-After and is bounded — the server-side count proves it.

using System;
using System.Collections.Generic;
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
    public class AccDocsMetadataTests : IDisposable
    {
        private const string Token = "test-access-token";
        private const string Project = "b.c0337487-5b66-422b-a284-c273b424af54";
        private const string Folder = "urn:adsk.wipprod:fs.folder:co.9g7HeA2wRqOxLlgLJ40UGQ";
        private const string Version = "urn:adsk.wipprod:fs.file:vf.AS3XD9MzQvu4MakMF-w7vQ?version=1";

        private readonly List<TimeSpan> _waits = new List<TimeSpan>();

        public AccDocsMetadataTests()
        {
            AccDocsMetadata.DelayHook = t => { lock (_waits) _waits.Add(t); return Task.CompletedTask; };
        }

        public void Dispose()
        {
            AccDocsMetadata.OverrideHostForTests(null);
            AccDocsMetadata.DelayHook = t => Task.Delay(t);
        }

        private static LoopbackServer Serve(Func<int, HttpListenerRequest, CannedResponse> h)
        {
            var s = new LoopbackServer(h);
            AccDocsMetadata.OverrideHostForTests(s.BaseUrl);
            return s;
        }

        private static string ReadBody(HttpListenerRequest r)
        {
            using var sr = new StreamReader(r.InputStream, Encoding.UTF8);
            return sr.ReadToEnd();
        }

        private static string Defs(params (int id, string name, string type)[] d) =>
            new JObject
            {
                ["results"] = new JArray(d.Select(x => new JObject { ["id"] = x.id, ["name"] = x.name, ["type"] = x.type })),
                ["pagination"] = new JObject { ["limit"] = 200, ["offset"] = 0, ["totalResults"] = d.Length },
            }.ToString();

        // ── list: failures are never an empty folder ────────────────────────

        [Theory]
        [InlineData(401, AccFetchStatus.AuthFailed)]
        [InlineData(403, AccFetchStatus.AuthFailed)]
        [InlineData(404, AccFetchStatus.NotFound)]
        [InlineData(500, AccFetchStatus.TransportFailed)]
        public async Task AFailedListing_IsNotAnEmptyFolder(int status, AccFetchStatus expected)
        {
            using var _ = Serve((i, r) => new CannedResponse(status, "{}"));
            var r = await AccDocsMetadata.ListDefinitionsAsync(Token, Project, Folder);
            Assert.False(r.Succeeded);
            Assert.Equal(expected, r.Status);
            Assert.Empty(r.Value);
            Assert.Contains(Folder, r.Detail);
        }

        [Theory]
        [InlineData(@"{""error"":""nope""}")]
        [InlineData(@"<html>login</html>")]
        [InlineData(@"{""results"":[{""name"":""No id"",""type"":""string""}]}")]
        [InlineData(@"{""results"":[{""id"":""abc"",""name"":""Bad id"",""type"":""string""}]}")]
        public async Task A200WithABadShape_IsATransportFailure(string body)
        {
            using var _ = Serve((i, r) => new CannedResponse(200, body));
            var r = await AccDocsMetadata.ListDefinitionsAsync(Token, Project, Folder);
            Assert.False(r.Succeeded);
            Assert.Equal(AccFetchStatus.TransportFailed, r.Status);
        }

        [Fact]
        public async Task List_StripsTheBPrefix_EncodesTheFolderUrn_AndParsesDefinitions()
        {
            string raw = null;
            using var s = Serve((i, r) =>
            {
                raw = r.RawUrl;
                return new CannedResponse(200, @"{""results"":[
                    {""id"":1001,""name"":""ISO Suitability"",""type"":""string""},
                    {""id"":1003,""name"":""Drawing Type"",""type"":""array"",""arrayValues"":[""Plans"",""Details""]}],
                    ""pagination"":{""limit"":200,""offset"":0,""totalResults"":2}}");
            });
            var res = await AccDocsMetadata.ListDefinitionsAsync(Token, Project, Folder);

            Assert.True(res.Succeeded, res.Detail);
            Assert.Equal(2, res.Value.Count);
            Assert.Equal(1003, res.Value[1].Id);
            Assert.Equal(new[] { "Plans", "Details" }, res.Value[1].ArrayValues);
            Assert.StartsWith("/bim360/docs/v1/projects/c0337487-5b66-422b-a284-c273b424af54/folders/", raw);
            Assert.Contains("urn%3Aadsk.wipprod%3Afs.folder%3Aco.9g7HeA2wRqOxLlgLJ40UGQ/custom-attribute-definitions", raw);
            Assert.Contains("limit=200", raw);
        }

        [Fact]
        public async Task List_FollowsPagination()
        {
            using var s = Serve((i, r) => new CannedResponse(200, i == 0
                ? @"{""results"":[{""id"":1,""name"":""A"",""type"":""string""}],""pagination"":{""totalResults"":2}}"
                : @"{""results"":[{""id"":2,""name"":""B"",""type"":""string""}],""pagination"":{""totalResults"":2}}"));
            var res = await AccDocsMetadata.ListDefinitionsAsync(Token, Project, Folder);
            Assert.True(res.Succeeded);
            Assert.Equal(new[] { "A", "B" }, res.Value.Select(d => d.Name));
            Assert.Equal(2, s.RequestCount);
            Assert.Contains("offset=1", s.Paths[1]);
        }

        [Fact]
        public async Task List_AGenuinelyEmptyFolder_IsEmptyOk()
        {
            using var _ = Serve((i, r) => new CannedResponse(200, @"{""results"":[],""pagination"":{""totalResults"":0}}"));
            var res = await AccDocsMetadata.ListDefinitionsAsync(Token, Project, Folder);
            Assert.Equal(AccFetchStatus.EmptyOk, res.Status);
        }

        [Fact]
        public async Task NoRequest_IsSent_ForAMissingTokenOrANonUrnFolder()
        {
            using var s = Serve((i, r) => new CannedResponse(200, "{}"));
            Assert.False((await AccDocsMetadata.ListDefinitionsAsync("", Project, Folder)).Succeeded);
            Assert.False((await AccDocsMetadata.ListDefinitionsAsync(Token, Project, "03 Published")).Succeeded);
            Assert.Equal(0, s.RequestCount);
        }

        // ── ensure ──────────────────────────────────────────────────────────

        [Fact]
        public async Task Ensure_WithoutPermission_ReportsMissing_AndCreatesNothing()
        {
            using var s = Serve((i, r) => new CannedResponse(200, Defs(
                (1, AccDocsAttributeSet.Suitability, "string"),
                (2, AccDocsAttributeSet.Revision, "date"))));
            var res = await AccDocsMetadata.EnsureDefinitionsAsync(Token, Project, Folder, allowCreate: false);

            Assert.True(res.Succeeded, res.Detail);
            var rep = res.Value;
            Assert.False(rep.IsComplete);
            Assert.Single(rep.Existing);
            Assert.Contains(rep.TypeMismatch, m => m.StartsWith(AccDocsAttributeSet.Revision));
            Assert.Equal(4, rep.Missing.Count);
            Assert.Empty(rep.Created);
            Assert.Equal(1, s.RequestCount);                         // the listing only — no POST
            // Every required name is accounted for exactly once.
            Assert.Equal(AccDocsAttributeSet.All.Count, rep.Existing.Count + rep.Missing.Count + rep.TypeMismatch.Count);
        }

        [Fact]
        public async Task Ensure_WithPermission_CreatesOnlyTheMissing_OnePostEach()
        {
            var posted = new List<JObject>();
            using var s = Serve((i, r) =>
            {
                if (r.HttpMethod == "GET")
                    return new CannedResponse(200, Defs((1, AccDocsAttributeSet.Suitability, "string")));
                var b = JObject.Parse(ReadBody(r));
                posted.Add(b);
                return new CannedResponse(201, new JObject { ["id"] = 100 + i, ["name"] = b["name"], ["type"] = b["type"] }.ToString());
            });
            var res = await AccDocsMetadata.EnsureDefinitionsAsync(Token, Project, Folder, allowCreate: true);

            Assert.True(res.Succeeded, res.Detail);
            Assert.True(res.Value.IsComplete);
            Assert.Equal(5, res.Value.Created.Count);
            Assert.Equal(6, res.Value.Usable.Count);
            Assert.Equal(5, posted.Count);
            Assert.DoesNotContain(posted, p => (string)p["name"] == AccDocsAttributeSet.Suitability);
            Assert.All(posted, p => Assert.Equal("string", (string)p["type"]));
        }

        [Fact]
        public async Task Ensure_ACreateRejectedAs403_FailsTheCall_WithThePartialReport()
        {
            using var s = Serve((i, r) => r.HttpMethod == "GET"
                ? new CannedResponse(200, Defs())
                : new CannedResponse(403, @"{""detail"":""no permission""}"));
            var res = await AccDocsMetadata.EnsureDefinitionsAsync(Token, Project, Folder, allowCreate: true);

            Assert.False(res.Succeeded);
            Assert.Equal(AccFetchStatus.AuthFailed, res.Status);
            Assert.Single(res.Value.CreateFailed);
            Assert.Equal(2, s.RequestCount);                         // stopped after the first refusal
        }

        [Fact]
        public async Task Ensure_ACreate400_IsReportedPerName_NotSkipped()
        {
            using var s = Serve((i, r) => r.HttpMethod == "GET"
                ? new CannedResponse(200, Defs())
                : new CannedResponse(400, @"{""detail"":""duplicate name""}"));
            var res = await AccDocsMetadata.EnsureDefinitionsAsync(Token, Project, Folder, allowCreate: true);

            Assert.True(res.Succeeded);
            Assert.False(res.Value.IsComplete);
            Assert.Equal(6, res.Value.CreateFailed.Count);
        }

        [Fact]
        public async Task Ensure_AListingFailure_FailsBeforeAnyCreate()
        {
            using var s = Serve((i, r) => new CannedResponse(404, "{}"));
            var res = await AccDocsMetadata.EnsureDefinitionsAsync(Token, Project, Folder, allowCreate: true);
            Assert.Equal(AccFetchStatus.NotFound, res.Status);
            Assert.Equal(1, s.RequestCount);
        }

        // ── batch update ────────────────────────────────────────────────────

        private static readonly List<AccAttributeDefinition> SetDefs = new List<AccAttributeDefinition>
        {
            new AccAttributeDefinition { Id = 1001, Name = AccDocsAttributeSet.Suitability, Type = "string" },
            new AccAttributeDefinition { Id = 1002, Name = AccDocsAttributeSet.CdeState, Type = "string" },
            new AccAttributeDefinition { Id = 1003, Name = AccDocsAttributeSet.TransmittalId, Type = "string" },
        };

        [Fact]
        public async Task BatchUpdate_SendsABareArrayOfIdValue_ToTheEncodedVersionPath()
        {
            string raw = null, body = null, method = null, contentType = null;
            using var s = Serve((i, r) =>
            {
                raw = r.RawUrl; method = r.HttpMethod; contentType = r.ContentType; body = ReadBody(r);
                return new CannedResponse(200, @"{""results"":[
                    {""id"":1001,""name"":""ISO Suitability"",""type"":""string"",""value"":""S3""},
                    {""id"":1002,""name"":""ISO CDE State"",""type"":""string"",""value"":""SHARED""},
                    {""id"":1003,""name"":""STING Transmittal Id"",""type"":""string"",""value"":null}]}");
            });

            var values = new Dictionary<string, string>
            {
                [AccDocsAttributeSet.Suitability] = "S3",
                [AccDocsAttributeSet.CdeState] = "SHARED",
                [AccDocsAttributeSet.TransmittalId] = null,          // null clears
            };
            var res = await AccDocsMetadata.SetVersionAttributesAsync(Token, Project, Version, values, SetDefs);

            Assert.True(res.Succeeded, res.Detail);
            Assert.True(res.Value.IsConfirmed, string.Join(",", res.Value.NotConfirmed));
            Assert.Equal("POST", method);
            Assert.StartsWith("application/json", contentType);
            Assert.Equal("/bim360/docs/v1/projects/c0337487-5b66-422b-a284-c273b424af54/versions/" +
                         "urn%3Aadsk.wipprod%3Afs.file%3Avf.AS3XD9MzQvu4MakMF-w7vQ%3Fversion%3D1/custom-attributes:batch-update", raw);

            var arr = Assert.IsType<JArray>(JToken.Parse(body));   // bare array, not {"attributes":…}
            Assert.Equal(3, arr.Count);
            Assert.All(arr, t => Assert.Equal(new[] { "id", "value" }, ((JObject)t).Properties().Select(p => p.Name)));
            Assert.Equal(JTokenType.Integer, arr[0]["id"].Type);
            Assert.Equal(1001, (int)arr[0]["id"]);
            Assert.Equal("S3", (string)arr[0]["value"]);
            Assert.Equal(JTokenType.Null, arr[2]["value"].Type);
        }

        [Fact]
        public async Task BatchUpdate_AnUnknownName_RefusesTheWholeWrite_BeforeAnyRequest()
        {
            using var s = Serve((i, r) => new CannedResponse(200, @"{""results"":[]}"));
            var values = new Dictionary<string, string>
            {
                [AccDocsAttributeSet.Suitability] = "S3",
                [AccDocsAttributeSet.DocumentNumber] = "KUT-1",      // not in SetDefs
            };
            var res = await AccDocsMetadata.SetVersionAttributesAsync(Token, Project, Version, values, SetDefs);
            Assert.False(res.Succeeded);
            Assert.Contains(AccDocsAttributeSet.DocumentNumber, res.Detail);
            Assert.Equal(0, s.RequestCount);
        }

        [Fact]
        public async Task BatchUpdate_ADropListValueNotInTheList_IsRefused()
        {
            using var s = Serve((i, r) => new CannedResponse(200, @"{""results"":[]}"));
            var defs = new[] { new AccAttributeDefinition { Id = 5, Name = "Stage", Type = "array", ArrayValues = { "S1", "S2" } } };
            var res = await AccDocsMetadata.SetVersionAttributesAsync(Token, Project, Version,
                new Dictionary<string, string> { ["Stage"] = "S9" }, defs);
            Assert.False(res.Succeeded);
            Assert.Equal(0, s.RequestCount);
        }

        [Fact]
        public async Task BatchUpdate_AnEchoThatDisagrees_IsNotConfirmed()
        {
            using var s = Serve((i, r) => new CannedResponse(200,
                @"{""results"":[{""id"":1001,""name"":""ISO Suitability"",""type"":""string"",""value"":""S2""}]}"));
            var res = await AccDocsMetadata.SetVersionAttributesAsync(Token, Project, Version,
                new Dictionary<string, string> { [AccDocsAttributeSet.Suitability] = "S3" }, SetDefs);
            Assert.True(res.Succeeded);
            Assert.False(res.Value.IsConfirmed);
            Assert.Contains(AccDocsAttributeSet.Suitability, res.Value.NotConfirmed);
        }

        [Theory]
        [InlineData(403, AccFetchStatus.AuthFailed)]
        [InlineData(404, AccFetchStatus.NotFound)]
        [InlineData(400, AccFetchStatus.TransportFailed)]
        public async Task BatchUpdate_AFailedWrite_IsAFailure(int status, AccFetchStatus expected)
        {
            using var s = Serve((i, r) => new CannedResponse(status, "{}"));
            var res = await AccDocsMetadata.SetVersionAttributesAsync(Token, Project, Version,
                new Dictionary<string, string> { [AccDocsAttributeSet.Suitability] = "S3" }, SetDefs);
            Assert.False(res.Succeeded);
            Assert.Equal(expected, res.Status);
            Assert.Empty(res.Value.Written);
        }

        // ── 429 ─────────────────────────────────────────────────────────────

        [Fact]
        public async Task RateLimited_HonoursRetryAfter_ThenSucceeds()
        {
            using var s = Serve((i, r) =>
            {
                if (i < 2)
                {
                    var c = new CannedResponse(429, "{}");
                    c.Headers["Retry-After"] = "7";
                    return c;
                }
                return new CannedResponse(200, Defs((1, "A", "string")));
            });
            var res = await AccDocsMetadata.ListDefinitionsAsync(Token, Project, Folder);

            Assert.True(res.Succeeded, res.Detail);
            Assert.Equal(3, s.RequestCount);                       // two 429s really were re-sent
            Assert.Equal(new[] { TimeSpan.FromSeconds(7), TimeSpan.FromSeconds(7) }, _waits);
        }

        [Fact]
        public async Task RateLimitedEveryTime_StopsAtMaxAttempts_AndFails()
        {
            using var s = Serve((i, r) => new CannedResponse(429, "{}"));
            var res = await AccDocsMetadata.SetVersionAttributesAsync(Token, Project, Version,
                new Dictionary<string, string> { [AccDocsAttributeSet.Suitability] = "S3" }, SetDefs);

            Assert.False(res.Succeeded);
            Assert.Equal(AccFetchStatus.TransportFailed, res.Status);
            Assert.Equal(429, res.HttpStatus);
            Assert.Equal(AccDocsMetadata.MaxAttempts, s.RequestCount);
            Assert.Equal(AccDocsMetadata.MaxAttempts - 1, _waits.Count);
            Assert.Equal(new[] { 1.0, 2.0, 4.0 }, _waits.Select(w => w.TotalSeconds));   // no header -> 1,2,4
        }

        [Fact]
        public void RetryAfter_IsCapped()
        {
            var h = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromMinutes(30));
            Assert.Equal(AccDocsMetadata.MaxRetryWait, AccDocsMetadata.RetryWait(h, 0));
        }

        [Theory]
        [InlineData("b.abc-123", "abc-123")]
        [InlineData("abc-123", "abc-123")]
        [InlineData(" B.abc ", "abc")]
        public void DocsProjectId_StripsOnlyTheDataManagementPrefix(string input, string expected)
            => Assert.Equal(expected, AccDocsMetadata.DocsProjectId(input));
    }
}
