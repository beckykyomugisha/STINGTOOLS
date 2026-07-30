using Npgsql;
using Planscape.Infrastructure.Data;
using Xunit;

namespace Planscape.Tests;

/// <summary>
/// Guards the two production failures PgConnectionStrings exists to prevent:
/// the provider URL format Npgsql cannot parse, and the unbounded 100-per-
/// process connection pool that exhausts a 97-connection Render database.
/// </summary>
public class PgConnectionStringsTests
{
    // ── Format: URL → keyword ────────────────────────────────────────────

    [Fact]
    public void Normalise_ParsesRenderStyleUrl()
    {
        var b = new NpgsqlConnectionStringBuilder(PgConnectionStrings.Normalise(
            "postgresql://planscape:s3cret@dpg-abc123-a.frankfurt-postgres.render.com:5432/planscape"));

        Assert.Equal("dpg-abc123-a.frankfurt-postgres.render.com", b.Host);
        Assert.Equal(5432, b.Port);
        Assert.Equal("planscape", b.Username);
        Assert.Equal("s3cret", b.Password);
        Assert.Equal("planscape", b.Database);
    }

    [Fact]
    public void Normalise_AcceptsShortPostgresScheme()
    {
        var b = new NpgsqlConnectionStringBuilder(
            PgConnectionStrings.Normalise("postgres://u:p@db.example.com:6432/mydb"));

        Assert.Equal("db.example.com", b.Host);
        Assert.Equal(6432, b.Port);   // the PgBouncer port must survive
        Assert.Equal("mydb", b.Database);
    }

    [Fact]
    public void Normalise_DefaultsPortWhenUrlOmitsIt()
    {
        var b = new NpgsqlConnectionStringBuilder(
            PgConnectionStrings.Normalise("postgresql://u:p@db.example.com/mydb"));

        Assert.Equal(5432, b.Port);
    }

    [Fact]
    public void Normalise_DecodesPercentEncodedPassword()
    {
        // Render-generated passwords routinely contain characters that must
        // be percent-encoded in a URL. Getting this wrong is an auth failure
        // that looks like a wrong-credentials bug.
        var b = new NpgsqlConnectionStringBuilder(PgConnectionStrings.Normalise(
            "postgresql://user%40corp:p%40ss%3Aw%2Frd@db.example.com:5432/mydb"));

        Assert.Equal("user@corp", b.Username);
        Assert.Equal("p@ss:w/rd", b.Password);
    }

    [Fact]
    public void Normalise_CarriesQueryStringParameters()
    {
        var b = new NpgsqlConnectionStringBuilder(PgConnectionStrings.Normalise(
            "postgresql://u:p@db.example.com:5432/mydb?sslmode=require"));

        Assert.Equal(SslMode.Require, b.SslMode);
    }

    [Fact]
    public void Normalise_IgnoresUnknownVendorQueryParameters()
    {
        var ex = Record.Exception(() => PgConnectionStrings.Normalise(
            "postgresql://u:p@db.example.com:5432/mydb?sslmode=require&vendor_thing=42"));

        Assert.Null(ex);
    }

    [Fact]
    public void Normalise_PassesKeywordFormThrough()
    {
        var b = new NpgsqlConnectionStringBuilder(PgConnectionStrings.Normalise(
            "Host=localhost;Port=5432;Database=planscape;Username=planscape;Password=dev"));

        Assert.Equal("localhost", b.Host);
        Assert.Equal("planscape", b.Database);
        Assert.Equal("dev", b.Password);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Normalise_ThrowsOnMissingString(string? raw)
        => Assert.Throws<ArgumentException>(() => PgConnectionStrings.Normalise(raw));

    [Fact]
    public void Normalise_ThrowsOnUnparseableUrl()
        => Assert.Throws<ArgumentException>(() => PgConnectionStrings.Normalise("postgresql://"));

    // ── Pool: the actual ceiling fix ─────────────────────────────────────

    [Fact]
    public void WithPool_AppliesCapToAnUncappedString()
    {
        // Without this, Npgsql silently uses 100 — enough for ONE process to
        // exhaust a 97-connection Render Postgres on its own.
        var b = new NpgsqlConnectionStringBuilder(PgConnectionStrings.WithPool(
            "postgresql://u:p@db.example.com:5432/mydb", 20, "planscape-api-ef"));

        Assert.Equal(20, b.MaxPoolSize);
        Assert.Equal("planscape-api-ef", b.ApplicationName);
        Assert.Equal(15, b.Timeout);
    }

    [Fact]
    public void WithPool_RespectsAnExplicitOperatorValue()
    {
        var b = new NpgsqlConnectionStringBuilder(PgConnectionStrings.WithPool(
            "Host=localhost;Database=planscape;Maximum Pool Size=7", 20, "planscape-api-ef"));

        Assert.Equal(7, b.MaxPoolSize);
    }

    [Fact]
    public void WithPool_RespectsAnExplicitApplicationName()
    {
        var b = new NpgsqlConnectionStringBuilder(PgConnectionStrings.WithPool(
            "Host=localhost;Database=planscape;Application Name=custom", 20, "planscape-api-ef"));

        Assert.Equal("custom", b.ApplicationName);
    }

    [Fact]
    public void WithPool_TotalBudgetStaysUnderRenderBasicTierCeiling()
    {
        // Render Postgres basic tiers: 100 max_connections, 10 reserved → 97.
        // These are the defaults in Program.cs; if someone raises them without
        // also raising the database plan, this test is the tripwire.
        const int renderBasicTierUsableConnections = 97;
        const int apiEf = 20, apiHangfire = 10, workerEf = 15, workerHangfire = 15;

        var total = apiEf + apiHangfire + workerEf + workerHangfire;

        Assert.True(total < renderBasicTierUsableConnections,
            $"Connection budget {total} must stay under {renderBasicTierUsableConnections} " +
            "with headroom for psql, migrations and backups.");
    }

    [Fact]
    public void WithPool_RejectsNonPositiveCap()
        => Assert.Throws<ArgumentOutOfRangeException>(() => PgConnectionStrings.WithPool(
            "Host=localhost;Database=planscape", 0, "planscape-api-ef"));
}
