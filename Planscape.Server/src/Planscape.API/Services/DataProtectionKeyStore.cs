using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
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
/// KEY ENCRYPTION AT REST (ACC-SRV-8) — see <see cref="SelectKeyEncryption"/>.
/// Without a certificate the key XML is stored in plain text (the ASP.NET default
/// with no XML encryptor on Linux), so anyone who can read the store can decrypt
/// every protected secret. With <c>DataProtection:CertificateBase64</c> (a PFX,
/// + <c>DataProtection:CertificatePassword</c>) or, on Windows,
/// <c>DataProtection:CertificateThumbprint</c>, NEW keys are written encrypted
/// with that certificate (ProtectKeysWithCertificate). Keys written before — in
/// plain text — stay readable, so enabling it needs no migration.
/// <c>DataProtection:PreviousCertificatesBase64</c> (comma-separated PFXs, same
/// password) keeps keys encrypted under a retired certificate readable during a
/// rotation (UnprotectKeysWithAnyCertificate).
/// </summary>
public static class DataProtectionKeyStore
{
    public enum Kind { FileSystem, Database, Ephemeral }

    public sealed record Choice(Kind Kind, string Description, string? Problem)
    {
        /// <summary>Key encryption at rest, filled by <see cref="Configure"/>.</summary>
        public KeyEncryption? Encryption { get; init; }
    }

    public enum KeyEncryptionKind { None, Certificate, Invalid }

    /// <param name="Certificate">Encrypts new keys (Certificate kind only).</param>
    /// <param name="DecryptionCertificates">Every certificate keys may be read with (current + previous).</param>
    public sealed record KeyEncryption(KeyEncryptionKind Kind, X509Certificate2? Certificate,
        IReadOnlyList<X509Certificate2> DecryptionCertificates, string Description, string? Problem);

    public const string CertBase64Key = "DataProtection:CertificateBase64";
    public const string CertPasswordKey = "DataProtection:CertificatePassword";
    public const string CertThumbprintKey = "DataProtection:CertificateThumbprint";
    public const string PreviousCertsKey = "DataProtection:PreviousCertificatesBase64";

    /// <summary>
    /// Pure selection of key encryption at rest (ACC-SRV-8). No side effects beyond
    /// loading the certificate. Order: <c>CertificateBase64</c> (+ <c>CertificatePassword</c>),
    /// else <c>CertificateThumbprint</c> (Windows certificate store: CurrentUser\My,
    /// then LocalMachine\My), else none. A configured certificate that cannot be
    /// used is <see cref="KeyEncryptionKind.Invalid"/> — reported, never silently
    /// treated as "not configured".
    /// </summary>
    public static KeyEncryption SelectKeyEncryption(IConfiguration config)
    {
        string? b64 = config[CertBase64Key];
        string? thumb = config[CertThumbprintKey];
        string? password = config[CertPasswordKey];

        var previous = new List<X509Certificate2>();
        string? previousProblem = null;
        foreach (var p in (config[PreviousCertsKey] ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var (c, err) = LoadPfx(p, password);
            if (c != null) previous.Add(c);
            else previousProblem ??= $"{PreviousCertsKey}: {err}";
        }

        X509Certificate2? cert = null;
        string? problem = null;
        string source = "";
        if (!string.IsNullOrWhiteSpace(b64))
        {
            (cert, problem) = LoadPfx(b64, password);
            source = CertBase64Key;
            if (problem != null) problem = $"{CertBase64Key}: {problem}";
        }
        else if (!string.IsNullOrWhiteSpace(thumb))
        {
            source = CertThumbprintKey;
            if (!OperatingSystem.IsWindows())
                problem = $"{CertThumbprintKey} is a Windows certificate-store lookup; on this host use {CertBase64Key}.";
            else
            {
                cert = FindByThumbprint(thumb);
                if (cert == null) problem = $"{CertThumbprintKey}: no certificate {thumb.Trim()} with a private key in CurrentUser\\My or LocalMachine\\My.";
            }
        }

        if (cert != null)
        {
            problem = Unusable(cert);
            if (problem != null) { problem = $"{source}: {problem}"; cert = null; }
        }

        if (problem != null)
            return new KeyEncryption(KeyEncryptionKind.Invalid, null, previous,
                "NOT encrypted (the configured certificate cannot be used)", problem);
        if (cert == null)
            return new KeyEncryption(KeyEncryptionKind.None, null, previous,
                "NOT encrypted at rest", previousProblem);

        var all = new List<X509Certificate2> { cert };
        all.AddRange(previous);
        string expiry = cert.NotAfter < DateTime.Now ? $" (EXPIRED {cert.NotAfter:yyyy-MM-dd} — still used; rotate it)" : $" (expires {cert.NotAfter:yyyy-MM-dd})";
        return new KeyEncryption(KeyEncryptionKind.Certificate, cert, all,
            $"encrypted with certificate {cert.Thumbprint}{expiry}" + (previous.Count > 0 ? $"; {previous.Count} previous certificate(s) accepted for reading" : ""),
            previousProblem);
    }

    private static (X509Certificate2? cert, string? error) LoadPfx(string base64, string? password)
    {
        byte[] bytes;
        try { bytes = Convert.FromBase64String(base64.Trim()); }
        catch (FormatException) { return (null, "not valid base64."); }
        try
        {
            // EphemeralKeySet: nothing is written to a machine / user key store.
            return (new X509Certificate2(bytes, password, X509KeyStorageFlags.EphemeralKeySet), null);
        }
        catch (CryptographicException ex)
        {
            return (null, $"the PFX could not be loaded (wrong {CertPasswordKey}?): {ex.Message}");
        }
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static X509Certificate2? FindByThumbprint(string thumbprint)
    {
        string t = thumbprint.Replace(" ", "").Trim();
        foreach (var location in new[] { StoreLocation.CurrentUser, StoreLocation.LocalMachine })
        {
            using var store = new X509Store(StoreName.My, location);
            try { store.Open(OpenFlags.ReadOnly); } catch (CryptographicException) { continue; }
            var found = store.Certificates.Find(X509FindType.FindByThumbprint, t, validOnly: false);
            var withKey = found.FirstOrDefault(c => c.HasPrivateKey);
            if (withKey != null) return withKey;
        }
        return null;
    }

    /// <summary>Why a loaded certificate cannot encrypt the key ring, or null.</summary>
    private static string? Unusable(X509Certificate2 cert)
    {
        if (!cert.HasPrivateKey) return "the certificate has no private key — keys encrypted with it could never be read back.";
        using var rsa = cert.GetRSAPrivateKey();
        if (rsa == null) return "the certificate is not RSA (the key ring's XML encryption needs an RSA key).";
        return null;
    }

    /// <summary>Register the encryptor / decryption certificates on the builder.</summary>
    public static void ApplyKeyEncryption(IDataProtectionBuilder dp, KeyEncryption enc)
    {
        if (enc.Certificate != null) dp.ProtectKeysWithCertificate(enc.Certificate);
        if (enc.DecryptionCertificates.Count > 0) dp.UnprotectKeysWithAnyCertificate(enc.DecryptionCertificates.ToArray());
    }

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
        var enc = SelectKeyEncryption(config);
        ApplyKeyEncryption(dp, enc);
        return choice with { Encryption = enc };
    }

    /// <summary>Loud startup line. Ephemeral in Production is an ERROR, not a warning.</summary>
    public static void LogChoice(ILogger logger, Choice choice, bool isProduction)
    {
        if (choice.Kind != Kind.Ephemeral)
        {
            // Warning, not Information: production logs at Warning and above, and
            // "which key store" must be answerable from the production log.
            logger.LogWarning("[DataProtection] key ring store: {Store}. Stored ACC / MFA / SSO secrets survive restarts.", choice.Description);
            LogKeyEncryption(logger, choice.Encryption, isProduction);
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

    /// <summary>
    /// The at-rest line. Not configured in Production is a WARNING (keys still work;
    /// they are readable by anyone with store access). Configured but unusable is an
    /// ERROR — someone meant to encrypt and it is not happening.
    /// </summary>
    public static void LogKeyEncryption(ILogger logger, KeyEncryption? enc, bool isProduction)
    {
        if (enc == null) return;
        switch (enc.Kind)
        {
            case KeyEncryptionKind.Certificate:
                logger.LogWarning("[DataProtection] keys at rest: {Encryption}.", enc.Description);
                if (enc.Problem != null) logger.LogError("[DataProtection] {Problem}", enc.Problem);
                break;
            case KeyEncryptionKind.Invalid:
                logger.LogError(
                    "[DataProtection] keys at rest: {Encryption} — {Problem} New keys are stored UNENCRYPTED until this is fixed.",
                    enc.Description, enc.Problem);
                break;
            default:
                if (isProduction)
                    logger.LogWarning(
                        "[DataProtection] keys at rest: NOT encrypted. Anyone who can read the key store (the DataProtectionKeys table / key folder) " +
                        "can decrypt stored ACC tokens and MFA / SSO secrets. Set {Setting} (base64 PFX) and {Password} — see docs/DEPLOY_RUNBOOK.md. " +
                        "Keys keep working without it.",
                        CertBase64Key.Replace(":", "__"), CertPasswordKey.Replace(":", "__"));
                if (enc.Problem != null) logger.LogError("[DataProtection] {Problem}", enc.Problem);
                break;
        }
    }
}
