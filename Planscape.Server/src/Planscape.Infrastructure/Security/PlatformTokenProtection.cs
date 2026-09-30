using Microsoft.Extensions.Logging;

namespace Planscape.Infrastructure.Security;

/// <summary>Symmetric protect/unprotect seam. Production binds it to an ASP.NET
/// Core <c>IDataProtector</c> — the same mechanism MfaController / SsoController
/// use for the <c>*Encrypted</c> columns — without making Infrastructure take a
/// package dependency on DataProtection.</summary>
public interface ISecretCipher
{
    string Protect(string plaintext);
    /// <summary>Throws (CryptographicException) when the value cannot be decrypted.</summary>
    string Unprotect(string ciphertext);
}

/// <summary>
/// At-rest encryption for <c>PlatformConnection.AccessToken</c> / <c>RefreshToken</c>,
/// applied by an EF value converter in <c>PlanscapeDbContext</c> so every reader
/// and writer (controllers, AccSyncService, PlatformSyncJob) gets it without
/// having to remember.
///
/// STORED FORMAT
///   * <c>enc:v1:&lt;DataProtection payload&gt;</c> — encrypted.
///   * anything else — a LEGACY plaintext row written before this existed. It is
///     returned as-is so existing connections keep working, and is re-encrypted
///     the next time the column is written (every token refresh writes it).
///   * "" stays "" (a disconnected row) — there is nothing to protect.
///
/// FAILURE MODES — deliberately loud, never a fabricated token
///   * Writing a token with no cipher configured THROWS. Silently storing
///     plaintext is exactly the bug this class exists to remove.
///   * An <c>enc:v1:</c> value that cannot be decrypted (the DataProtection key
///     ring changed — e.g. an ephemeral ring; see DataProtectionKeyStore) reads
///     back as the CIPHERTEXT ITSELF and logs an error. Not null, not "": an
///     empty token is indistinguishable from "never connected". Consumers test
///     it with <see cref="IsUnreadable"/> — AccTokenRefresher treats it as not
///     fresh, refuses to refresh with it, and the connection reports
///     RECONNECT_REQUIRED. Writing it back is a no-op (<see cref="Protect"/>
///     passes an <c>enc:v1:</c> value through unchanged), so restoring the key
///     ring restores the connection. Throwing instead would fail the whole
///     materialisation — one bad row would stop the scheduled sweep for every
///     tenant.
/// </summary>
public static class PlatformTokenProtection
{
    public const string Prefix = "enc:v1:";

    private static ISecretCipher? _cipher;
    private static ILogger? _logger;

    public static bool IsConfigured => _cipher != null;

    /// <summary>
    /// Set the cipher. FIRST CALL WINS for the life of the process: a token
    /// encrypted under one cipher must never be read back under another, and a
    /// second host in the same process (test factories) must not swap the key
    /// out from under rows already written. Returns false when already configured.
    /// </summary>
    public static bool Configure(ISecretCipher cipher, ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(cipher);
        if (Interlocked.CompareExchange(ref _cipher, cipher, null) != null) return false;
        _logger = logger;
        return true;
    }

    public static bool IsEncrypted(string? stored)
        => stored != null && stored.StartsWith(Prefix, StringComparison.Ordinal);

    /// <summary>
    /// True when a MODEL value (an entity property after materialisation) is
    /// still ciphertext — the stored token could not be decrypted. Such a value
    /// must never be sent to a provider as a token; the connection needs a
    /// reconnect (or the key ring restored).
    /// </summary>
    public static bool IsUnreadable(string? modelValue) => IsEncrypted(modelValue);

    /// <summary>Model value → column value.</summary>
    public static string Protect(string value)
    {
        if (string.IsNullOrEmpty(value) || IsEncrypted(value)) return value;
        var cipher = _cipher ?? throw new InvalidOperationException(
            "PlatformTokenProtection is not configured — refusing to store an OAuth token in plaintext. " +
            "Call PlatformTokenProtection.Configure(...) at startup.");
        return Prefix + cipher.Protect(value);
    }

    /// <summary>Column value → model value.</summary>
    public static string? Unprotect(string stored)
    {
        if (!IsEncrypted(stored)) return stored;   // legacy plaintext (or "")
        var cipher = _cipher;
        if (cipher == null)
        {
            _logger?.LogError("PlatformTokenProtection: encrypted token read but no cipher configured.");
            return stored;   // stays ciphertext: IsUnreadable -> reconnect
        }
        try
        {
            return cipher.Unprotect(stored.Substring(Prefix.Length));
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex,
                "PlatformTokenProtection: a stored platform token could not be decrypted (DataProtection key ring changed?). " +
                "The connection must be reconnected, or the key ring restored.");
            return stored;   // stays ciphertext: IsUnreadable -> reconnect
        }
    }
}
