using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Planscape.Infrastructure.Data;

namespace Planscape.API.Services;

/// <summary>
/// Where the ASP.NET Core DataProtection key ring lives.
///
/// The key ring encrypts the ACC OAuth tokens at rest (PlatformTokenProtection),
/// the MFA / SSO <c>*Encrypted</c> columns, and the sealed ACC OAuth state. An
/// EPHEMERAL ring is regenerated on every process start, so after a restart or
/// redeploy every stored token is unreadable and every ACC connection needs a
/// reconnect. It is also per-process: the API and the worker (separate Render
/// services, same image) would each hold a different ring, so a token the API
/// encrypted could never be decrypted by the worker's scheduled sync.
///
/// SELECTION (<c>DataProtection:KeyStore</c> = auto | database | filesystem | ephemeral)
///   * filesystem — <c>DataProtection:KeysPath</c> (a mounted persistent volume).
///     Honoured whenever KeysPath is set and KeyStore is auto.
///   * database   — the <c>DataProtectionKeys</c> table in the app's Postgres
///     (created by OnModelCreating on a fresh DB, and by PlatformSchemaPatcher on
///     an existing one). The default whenever a connection string is configured:
///     Postgres is always present on Render, shared by API and worker, and backed
///     up with everything else. Render's API/worker services have no disk.
///   * ephemeral  — in-memory. Only when chosen explicitly (tests) or when there is
///     nothing durable to use. In Production that is logged as an ERROR at startup.
///
/// The keys themselves are stored unencrypted-at-rest in the chosen store (the
/// ASP.NET default without an XML encryptor); protect the database accordingly.
/// </summary>
public static class DataProtectionKeyStore
{
    public enum Kind { FileSystem, Database, Ephemeral }

    public sealed record Choice(Kind Kind, string Description, string? Problem);

    public const string ApplicationName = "Planscape";

    /// <summary>Pure selection — no side effects, unit-testable.</summary>
    public static Choice Select(IConfiguration config)
    {
        string mode = (config["DataProtection:KeyStore"] ?? "auto").Trim().ToLowerInvariant();
        string? keysPath = config["DataProtection:KeysPath"];
        bool hasPath = !string.IsNullOrWhiteSpace(keysPath);
        bool hasDb = !string.IsNullOrWhiteSpace(config.GetConnectionString("Default"));

        switch (mode)
        {
            case "filesystem":
                return hasPath
                    ? new Choice(Kind.FileSystem, $"file system ({keysPath})", null)
                    : new Choice(Kind.Ephemeral, "in-memory (EPHEMERAL)",
                        "DataProtection:KeyStore=filesystem but DataProtection:KeysPath is not set.");
            case "database":
                return hasDb
                    ? new Choice(Kind.Database, "database table \"DataProtectionKeys\"", null)
                    : new Choice(Kind.Ephemeral, "in-memory (EPHEMERAL)",
                        "DataProtection:KeyStore=database but ConnectionStrings:Default is not set.");
            case "ephemeral":
                return new Choice(Kind.Ephemeral, "in-memory (EPHEMERAL)",
                    "DataProtection:KeyStore=ephemeral was chosen explicitly.");
            case "auto":
            case "":
                if (hasPath) return new Choice(Kind.FileSystem, $"file system ({keysPath})", null);
                if (hasDb) return new Choice(Kind.Database, "database table \"DataProtectionKeys\"", null);
                return new Choice(Kind.Ephemeral, "in-memory (EPHEMERAL)",
                    "No DataProtection:KeysPath and no ConnectionStrings:Default — nothing durable to store keys in.");
            default:
                return new Choice(Kind.Ephemeral, "in-memory (EPHEMERAL)",
                    $"Unknown DataProtection:KeyStore '{mode}' (expected auto, database, filesystem or ephemeral).");
        }
    }

    /// <summary>Register DataProtection for the selected store. Returns the choice for logging.</summary>
    public static Choice Configure(IServiceCollection services, IConfiguration config)
    {
        var choice = Select(config);
        var dp = services.AddDataProtection().SetApplicationName(ApplicationName);
        switch (choice.Kind)
        {
            case Kind.FileSystem:
                var path = config["DataProtection:KeysPath"]!;
                try { Directory.CreateDirectory(path); } catch (IOException) { /* may already exist / be a mount */ }
                dp.PersistKeysToFileSystem(new DirectoryInfo(path));
                break;
            case Kind.Database:
                dp.PersistKeysToDbContext<PlanscapeDbContext>();
                break;
            case Kind.Ephemeral:
                break;   // ASP.NET default: in-memory ring
        }
        return choice;
    }

    /// <summary>Loud startup line. Ephemeral in Production is an ERROR, not a warning.</summary>
    public static void LogChoice(ILogger logger, Choice choice, bool isProduction)
    {
        if (choice.Kind != Kind.Ephemeral)
        {
            // Warning, not Information: production logs at Warning and above, and
            // "which key store" must be answerable from the production log.
            logger.LogWarning("[DataProtection] key ring store: {Store}. Stored ACC / MFA / SSO secrets survive restarts.", choice.Description);
            return;
        }
        if (isProduction)
            logger.LogError(
                "[DataProtection] key ring store: {Store} — {Problem} Every restart generates a new key: stored ACC tokens, MFA and SSO secrets become " +
                "unreadable (connections report RECONNECT_REQUIRED) and the API and worker cannot read each other's ciphertext. " +
                "Set DataProtection:KeyStore=database (default when ConnectionStrings:Default is set) or DataProtection:KeysPath.",
                choice.Description, choice.Problem);
        else
            logger.LogWarning("[DataProtection] key ring store: {Store} — {Problem} Keys are lost on restart (acceptable outside Production).",
                choice.Description, choice.Problem);
    }
}
