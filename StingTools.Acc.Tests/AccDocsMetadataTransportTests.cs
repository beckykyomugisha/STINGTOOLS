// AUT-4: AccDocsMetadata had its own HttpClient and a bare token string, so an ISO 19650
// attribute stamp that ran after the token expired failed with 401 and the upload reported
// "attributes NOT written" - although every other ACC call would have refreshed and gone on.
// With credentials it now goes through AccHttp: one forced refresh on 401, then the resend.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using StingTools.Acc.Tests.TestHelpers;
using StingTools.V6;
using Xunit;

namespace StingTools.Acc.Tests
{
    public class AccDocsMetadataTransportTests : IDisposable
    {
        public AccDocsMetadataTransportTests()
        {
            AccHttp.DelayHook = _ => Task.CompletedTask;
            AccDocsMetadata.DelayHook = _ => Task.CompletedTask;
            CredentialIsolation.Reset();
        }

        public void Dispose()
        {
            AccHttp.DelayHook = t => Task.Delay(t);
            AccDocsMetadata.DelayHook = t => Task.Delay(t);
            AccIssueSync.OverrideHostForTests(null);
            AccDocsMetadata.OverrideHostForTests(null);
        }

        private const string Folder = "urn:adsk.wipprod:fs.folder:co.F1";
        private const string Defs = "{\"pagination\":{\"limit\":200,\"offset\":0,\"totalResults\":1}," +
                                    "\"results\":[{\"id\":1,\"name\":\"Suitability\",\"type\":\"string\"}]}";

        private static LoopbackServer ExpiringTokenServer(Func<int> tokenCalls, Action onToken)
            => new LoopbackServer((i, req) =>
            {
                if (req.Url.AbsolutePath.Contains("/authentication/v2/token"))
                {
                    onToken();
                    return new CannedResponse(200, "{\"access_token\":\"new-access\",\"refresh_token\":\"new-refresh\",\"expires_in\":3600}");
                }
                return req.Headers["Authorization"] == "Bearer new-access"
                    ? new CannedResponse(200, Defs)
                    : new CannedResponse(401, "{\"detail\":\"expired\"}");
            });

        [Fact]
        public async Task WithCredentials_A401_IsRefreshedAndTheListingSucceeds()
        {
            int tokens = 0;
            using var server = ExpiringTokenServer(() => tokens, () => tokens++);
            AccIssueSync.OverrideHostForTests(server.BaseUrl);
            AccDocsMetadata.OverrideHostForTests(server.BaseUrl);
            var creds = H.Creds();

            var r = await AccDocsMetadata.ListDefinitionsAsync(creds.AccessToken, H.Project, Folder, creds);

            Assert.True(r.Succeeded, r.Detail);
            Assert.Single(r.Value);
            Assert.Equal(1, tokens);
            Assert.Equal("new-access", creds.AccessToken);
        }

        [Fact]
        public async Task WithoutCredentials_TheOldPathIsUnchanged_A401IsAnAuthFailure()
        {
            int tokens = 0;
            using var server = ExpiringTokenServer(() => tokens, () => tokens++);
            AccIssueSync.OverrideHostForTests(server.BaseUrl);
            AccDocsMetadata.OverrideHostForTests(server.BaseUrl);

            var r = await AccDocsMetadata.ListDefinitionsAsync("old-token", H.Project, Folder);

            Assert.False(r.Succeeded);
            Assert.Equal(AccFetchStatus.AuthFailed, r.Status);
            Assert.Equal(0, tokens);                       // nothing to refresh with: no refresh attempted
        }

        [Fact]
        public async Task WithCredentials_AStampAfterExpiry_IsWritten()
        {
            int tokens = 0;
            using var server = new LoopbackServer((i, req) =>
            {
                if (req.Url.AbsolutePath.Contains("/authentication/v2/token"))
                {
                    tokens++;
                    return new CannedResponse(200, "{\"access_token\":\"new-access\",\"refresh_token\":\"new-refresh\",\"expires_in\":3600}");
                }
                if (req.Headers["Authorization"] != "Bearer new-access") return new CannedResponse(401, "{}");
                return new CannedResponse(200, "{\"results\":[{\"id\":1,\"name\":\"Suitability\",\"value\":\"S3\"}]}");
            });
            AccIssueSync.OverrideHostForTests(server.BaseUrl);
            AccDocsMetadata.OverrideHostForTests(server.BaseUrl);
            var creds = H.Creds();
            var defs = new List<AccAttributeDefinition> { new AccAttributeDefinition { Id = 1, Name = "Suitability", Type = "string" } };

            var w = await AccDocsMetadata.SetVersionAttributesAsync(creds.AccessToken, H.Project,
                "urn:adsk.wipprod:fs.file:vf.X?version=1", new Dictionary<string, string> { ["Suitability"] = "S3" }, defs, creds);

            Assert.True(w.Succeeded, w.Detail);
            Assert.Equal(1, tokens);
        }
    }
}
