using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;

namespace Planscape.Tests;

/// <summary>
/// Guards the production failure WebAppUrl exists to prevent: an invitee who
/// successfully sets a password and is then sent to a URL that renders nothing.
///
/// The accept-invite page is served by the API but the product lives on a
/// different origin. The page used to guess that origin in JavaScript with
/// `location.origin` as its last resort — which, on every deployment whose API
/// host doesn't follow the api./planscape-api naming, pointed at
/// {api-origin}/projects/{id}: a route the API does not serve and has no SPA
/// fallback for, i.e. an empty 404. The page "starts opening, then stops".
///
/// The contract these tests lock down is therefore as much about the NULL as
/// the happy path: "I don't know" must never degrade into "this origin".
/// </summary>
public class WebAppUrlTests
{
    private static IConfiguration Config(params (string key, string value)[] entries) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(entries.ToDictionary(e => e.key, e => (string?)e.value))
            .Build();

    private static HttpRequest Request(string scheme, string host, string? forwardedProto = null)
    {
        var ctx = new DefaultHttpContext();
        ctx.Request.Scheme = scheme;
        ctx.Request.Host = new HostString(host);
        if (forwardedProto != null) ctx.Request.Headers["X-Forwarded-Proto"] = forwardedProto;
        return ctx.Request;
    }

    [Fact]
    public void ConfiguredWebAppUrlWins()
    {
        var url = Planscape.API.WebAppUrl.Resolve(
            Config(("Planscape:WebAppUrl", "https://app.planscape.build/")),
            Request("http", "anything-at-all.example", "https"));

        Assert.Equal("https://app.planscape.build", url);
    }

    [Fact]
    public void DerivesAppHost_FromCustomDomainApiHost()
    {
        var url = Planscape.API.WebAppUrl.Resolve(
            Config(), Request("http", "api.planscape.build", "https"));

        Assert.Equal("https://app.planscape.build", url);
    }

    [Fact]
    public void DerivesWebService_FromRenderApiService()
    {
        var url = Planscape.API.WebAppUrl.Resolve(
            Config(), Request("http", "planscape-api-free.onrender.com", "https"));

        Assert.Equal("https://planscape-web-free.onrender.com", url);
    }

    [Theory]
    // A Cloudflare quick tunnel — the shape this platform actually runs behind
    // in the field, and the one the old JS fallback silently broke on.
    [InlineData("random-words-here.trycloudflare.com")]
    // A bare local run.
    [InlineData("localhost:5000")]
    // Any other custom host.
    [InlineData("bim.someclient.co.ug")]
    public void ReturnsNull_RatherThanThisOrigin_WhenThePairingIsUnknown(string host)
    {
        var url = Planscape.API.WebAppUrl.Resolve(Config(), Request("https", host));

        // THE regression. Returning the API's own origin here is what produced
        // the blank page; the caller must be told "unknown" so it can say so.
        Assert.Null(url);
    }

    [Fact]
    public void PreservesNonDefaultPort_WhenDeriving()
    {
        var url = Planscape.API.WebAppUrl.DeriveFromApiOrigin("http://api.local:8080");

        Assert.Equal("http://app.local:8080", url);
    }

    [Fact]
    public void HandlesNullAndGarbage()
    {
        Assert.Null(Planscape.API.WebAppUrl.DeriveFromApiOrigin(null));
        Assert.Null(Planscape.API.WebAppUrl.DeriveFromApiOrigin(""));
        Assert.Null(Planscape.API.WebAppUrl.DeriveFromApiOrigin("not a url"));
    }
}
