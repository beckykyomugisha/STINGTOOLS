using Planscape.Infrastructure.Data;
using StackExchange.Redis;
using Xunit;

namespace Planscape.Tests;

/// <summary>
/// Guards the production failure RedisConnectionStrings exists to prevent:
/// Render hands out redis:// URLs, but StackExchange.Redis's own parser
/// expects its native comma-separated form and silently mis-parses a URL
/// into a broken endpoint — no exception, just a multiplexer that never
/// connects. See RedisConnectionStrings' doc comment for the full story.
/// </summary>
public class RedisConnectionStringsTests
{
    [Fact]
    public void Normalise_ParsesRenderStyleUrl()
    {
        var opts = ConfigurationOptions.Parse(RedisConnectionStrings.Normalise(
            "redis://red-abc123-a.frankfurt-redis.render.com:6379"));

        Assert.Single(opts.EndPoints);
        Assert.Contains("red-abc123-a.frankfurt-redis.render.com", opts.EndPoints[0].ToString());
    }

    [Fact]
    public void Normalise_DefaultsPortWhenUrlOmitsIt()
    {
        var opts = ConfigurationOptions.Parse(RedisConnectionStrings.Normalise("redis://cache.example.com"));

        Assert.Contains(":6379", opts.EndPoints[0].ToString());
    }

    [Fact]
    public void Normalise_CarriesUserAndPassword()
    {
        var opts = ConfigurationOptions.Parse(RedisConnectionStrings.Normalise(
            "redis://default:s3cret@cache.example.com:6379"));

        Assert.Equal("default", opts.User);
        Assert.Equal("s3cret", opts.Password);
    }

    [Fact]
    public void Normalise_DecodesPercentEncodedPassword()
    {
        var opts = ConfigurationOptions.Parse(RedisConnectionStrings.Normalise(
            "redis://default:p%40ss%3Aw%2Frd@cache.example.com:6379"));

        Assert.Equal("p@ss:w/rd", opts.Password);
    }

    [Fact]
    public void Normalise_EnablesSslForRedissScheme()
    {
        var opts = ConfigurationOptions.Parse(RedisConnectionStrings.Normalise(
            "rediss://cache.example.com:6380"));

        Assert.True(opts.Ssl);
    }

    [Fact]
    public void Normalise_PassesNativeFormThrough()
    {
        var opts = ConfigurationOptions.Parse(
            RedisConnectionStrings.Normalise("localhost:6379,password=dev"));

        Assert.Contains("localhost:6379", opts.EndPoints[0].ToString());
        Assert.Equal("dev", opts.Password);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Normalise_DefaultsToLocalhostWhenMissing(string? raw)
        => Assert.Equal("localhost:6379", RedisConnectionStrings.Normalise(raw));

    [Fact]
    public void Normalise_ReturnsRawStringOnUnparseableUrl()
    {
        // Falls through unchanged rather than throwing, so ConfigurationOptions.Parse
        // (called downstream in Program.cs, inside its own try/catch) produces the
        // actual error — startup must never crash on a malformed Redis URL.
        var result = RedisConnectionStrings.Normalise("redis://");
        Assert.Equal("redis://", result);
    }
}
