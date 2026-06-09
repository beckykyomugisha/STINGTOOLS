using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Planscape.Core.Interfaces;
using Planscape.Infrastructure.Services;

namespace Planscape.Tests;

/// <summary>
/// WS3 regression — a project named with markup must render as TEXT in outbound
/// email, never as live HTML. The renderer HTML-encodes every {{Placeholder}} in
/// HTML mode EXCEPT the trusted, server-composed {{Body}} slot (which must stay
/// verbatim or the recipient sees literal &lt;h2&gt; tags — the double-escape bug
/// fixed in 58fec0780). This test locks both halves of that contract so a future
/// refactor can't silently reintroduce a stored-XSS vector via the project name.
/// </summary>
public class EmailEscapingTests
{
    // Minimal IHostEnvironment — the renderer only touches ContentRootPath as a
    // fallback, and we override the template root via Tenant:EmailTemplatesPath,
    // so the file provider is never exercised.
    private sealed class FakeEnv : IHostEnvironment
    {
        public string ApplicationName { get; set; } = "Planscape.Tests";
        public string EnvironmentName { get; set; } = "Test";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } =
            new NullFileProvider();
    }

    private static ResolvedBranding Branding() => new(
        ProductName: "Planscape",
        AccentColor: "#1976d2",
        HeaderColor: "#0d47a1",
        LogoUrl: null,
        SupportEmail: "support@planscape.test",
        EmailFromName: "Planscape",
        EmailFromAddress: "no-reply@planscape.test",
        EmailSignature: null,
        DefaultLanguage: "en");

    private static (FileEmailTemplateRenderer renderer, string dir) BuildRenderer(string htmlTemplate)
    {
        var dir = Path.Combine(Path.GetTempPath(), "ps_email_regtest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "regtest.en.html"), htmlTemplate);

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Tenant:EmailTemplatesPath"] = dir,
            })
            .Build();

        var renderer = new FileEmailTemplateRenderer(
            new FakeEnv(),
            config,
            NullLogger<FileEmailTemplateRenderer>.Instance);
        return (renderer, dir);
    }

    [Fact]
    public async Task ProjectNameWithMarkup_RendersAsText_NotHtml()
    {
        var (renderer, dir) = BuildRenderer("<p>Project: {{ProjectName}}</p>");
        try
        {
            var rendered = await renderer.RenderAsync(
                "regtest", "en",
                new Dictionary<string, string?> { ["ProjectName"] = "<b>x</b>" },
                Branding());

            // The angle brackets must be entity-encoded — the recipient sees the
            // literal text "<b>x</b>", not a bold-rendered "x".
            Assert.Contains("&lt;b&gt;x&lt;/b&gt;", rendered.Html);
            Assert.DoesNotContain("<b>x</b>", rendered.Html);
        }
        finally { try { Directory.Delete(dir, true); } catch { /* best effort */ } }
    }

    [Fact]
    public async Task TrustedBodySlot_StaysVerbatim_NoDoubleEscape()
    {
        // The layout {{Body}} slot carries pre-rendered, server-trusted markup and
        // must NOT be re-encoded, or the recipient sees raw <h2> tags.
        var (renderer, dir) = BuildRenderer("<div>{{Body}}</div>");
        try
        {
            var rendered = await renderer.RenderAsync(
                "regtest", "en",
                new Dictionary<string, string?> { ["Body"] = "<h2>Welcome</h2>" },
                Branding());

            Assert.Contains("<h2>Welcome</h2>", rendered.Html);
            Assert.DoesNotContain("&lt;h2&gt;", rendered.Html);
        }
        finally { try { Directory.Delete(dir, true); } catch { /* best effort */ } }
    }
}
