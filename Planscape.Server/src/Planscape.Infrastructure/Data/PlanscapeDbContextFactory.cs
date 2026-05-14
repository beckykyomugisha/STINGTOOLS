using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Planscape.Infrastructure.Data;

/// <summary>
/// Design-time factory used exclusively by `dotnet ef` tooling (migrations,
/// database update, etc.). It creates the DbContext using only the options
/// constructor so EF doesn't get confused by the multi-constructor overloads
/// that the DI container resolves at runtime.
///
/// Connection string resolution order:
///   1. CONNECTION_STRING environment variable (CI / developer override)
///   2. Host/Port/Database/Username/Password individual env vars
///   3. Hardcoded localhost default that matches docker-compose postgres
///
/// The factory is never registered in DI and is never called at runtime.
/// </summary>
public sealed class PlanscapeDbContextFactory : IDesignTimeDbContextFactory<PlanscapeDbContext>
{
    public PlanscapeDbContext CreateDbContext(string[] args)
    {
        var cs = Environment.GetEnvironmentVariable("CONNECTION_STRING");

        if (string.IsNullOrWhiteSpace(cs))
        {
            var host     = Environment.GetEnvironmentVariable("PGHOST")     ?? "localhost";
            var port     = Environment.GetEnvironmentVariable("PGPORT")     ?? "5432";
            var db       = Environment.GetEnvironmentVariable("PGDATABASE") ?? "planscape";
            var user     = Environment.GetEnvironmentVariable("PGUSER")     ?? "planscape";
            var password = Environment.GetEnvironmentVariable("PGPASSWORD") ?? "Planscape2026!";
            cs = $"Host={host};Port={port};Database={db};Username={user};Password={password}";
        }

        var options = new DbContextOptionsBuilder<PlanscapeDbContext>()
            .UseNpgsql(cs)
            .Options;

        return new PlanscapeDbContext(options);
    }
}
