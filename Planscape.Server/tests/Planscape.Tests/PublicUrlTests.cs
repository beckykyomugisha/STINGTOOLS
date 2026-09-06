using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;

namespace Planscape.Tests;

/// <summary>
/// Guards the production failure PublicUrl exists to prevent: an emailed invite
/// link that the recipient cannot open.
///
/// Behind a TLS-terminating proxy (Render, Cloudflare, most PaaS) the request
/// reaches the app over plain HTTP, so <c>request.Scheme</c> is "http" while the
/// public URL is https. Links built from the raw scheme came out as http://…
/// and the host answered with a closed connection — a real invitee clicked one
/// and got ERR_CONNECTION_CLOSED, so every invite email was dead on arrival.
/// </summary>
public class PublicUrlTests
{
    private static IConfiguration Config(string? publicBaseUrl = null) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(publicBaseUrl == null
                ? new Dictionary<string, string?>()
                : new Dictionary<string, string?> { ["Planscape:PublicBaseUrl"] = publicBaseUrl })
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
    public void UsesHttps_WhenProxyReportsHttps_EvenThoughRequestArrivedOverHttp()
    {
        // THE regression. Render forwards to the app over http and reports the
        // original scheme in X-Forwarded-Proto.
        var url = Planscape.API.PublicUrl.Resolve(
            Config(), Request("http", "planscape-api-free.onrender.com", "https"));

        Assert.Equal("https://planscape-api-free.onrender.com", url);
    }

    [Fact]
    public void TakesTheFirstProto_WhenSeveralProxiesChain()
    {
        // Comma-separated list; the ORIGINAL client scheme is first.
        var url = Planscape.API.PublicUrl.Resolve(
            Config(), Request("http", "example.com", "https, http"));

        Assert.Equal("https://example.com", url);
    }

    [Fact]
    public void FallsBackToRequestScheme_WhenThereIsNoProxyHeader()
    {
        // A bare local dev run must keep working with zero config.
        var url = Planscape.API.PublicUrl.Resolve(Config(), Request("http", "localhost:5000"));

        Assert.Equal("http://localhost:5000", url);
    }

    [Fact]
    public void ConfiguredPublicBaseUrlStillWins()
    {
        // Explicit config outranks anything inferred — the documented behaviour
        // for tunnels, where even the forwarded scheme/host is not the public one.
        var url = Planscape.API.PublicUrl.Resolve(
            Config("https://api.planscape.build"),
            Request("http", "internal:5000", "http"));

        Assert.Equal("https://api.planscape.build", url);
    }
}
