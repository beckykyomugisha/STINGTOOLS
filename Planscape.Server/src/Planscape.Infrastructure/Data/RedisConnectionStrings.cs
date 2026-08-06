namespace Planscape.Infrastructure.Data;

/// <summary>
/// Single place where a raw Redis connection string becomes something
/// StackExchange.Redis can actually open.
///
/// WHY THIS EXISTS — same class of failure as <see cref="PgConnectionStrings"/>.
/// Render (and most managed-Redis providers) hand out connection strings as a
/// URL: <c>redis://[user:pass@]host:6379</c> or <c>rediss://...</c> for TLS.
/// StackExchange.Redis's <c>ConfigurationOptions.Parse</c> does NOT understand
/// this scheme — it expects its own comma-separated form
/// (<c>host:port,password=...,ssl=true</c>). Handed a raw
/// <c>redis://host:port</c> string, the parser mis-splits on the embedded
/// colons and silently builds a broken endpoint, so the multiplexer never
/// connects and every Redis-dependent feature (cache, SignalR backplane, the
/// Redis-backed rate limiters) fails or times out — with no exception at
/// startup, because <c>AbortOnConnectFail = false</c> is deliberately set to
/// keep the app booting. <see cref="Normalise"/> converts URL form to
/// StackExchange.Redis's native form and passes native form through
/// untouched.
/// </summary>
public static class RedisConnectionStrings
{
    /// <summary>
    /// Accepts either a <c>redis://</c> / <c>rediss://</c> URL or an already
    /// StackExchange.Redis-native connection string, and always returns
    /// native form (<c>host:port[,password=...][,user=...][,ssl=true]</c>).
    /// </summary>
    public static string Normalise(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return "localhost:6379";

        raw = raw.Trim();

        var isUrl = raw.StartsWith("redis://", StringComparison.OrdinalIgnoreCase)
                 || raw.StartsWith("rediss://", StringComparison.OrdinalIgnoreCase);

        if (!isUrl)
            // Already native form — pass through untouched.
            return raw;

        if (!Uri.TryCreate(raw, UriKind.Absolute, out var uri) || string.IsNullOrEmpty(uri.Host))
            // Malformed — return as-is so ConfigurationOptions.Parse throws
            // its own descriptive error rather than us swallowing it here.
            return raw;

        var isTls = raw.StartsWith("rediss://", StringComparison.OrdinalIgnoreCase);
        var port = uri.IsDefaultPort || uri.Port <= 0 ? 6379 : uri.Port;

        var parts = new List<string> { $"{uri.Host}:{port}" };

        // UserInfo is percent-encoded in a URL — passwords routinely contain
        // characters that MUST be decoded before StackExchange.Redis sees them.
        var userInfo = uri.UserInfo ?? string.Empty;
        if (userInfo.Length > 0)
        {
            var split = userInfo.Split(':', 2);
            var user = Uri.UnescapeDataString(split[0]);
            if (!string.IsNullOrEmpty(user))
                parts.Add($"user={user}");
            if (split.Length == 2)
                parts.Add($"password={Uri.UnescapeDataString(split[1])}");
        }

        if (isTls)
            parts.Add("ssl=true");

        return string.Join(',', parts);
    }
}
