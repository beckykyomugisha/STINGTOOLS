using Npgsql;

namespace Planscape.Infrastructure.Data;

/// <summary>
/// Single place where a raw Postgres connection string becomes something
/// Npgsql can actually open, with a bounded client-side pool.
///
/// WHY THIS EXISTS — two separate production failures:
///
/// 1. FORMAT. Render (and Heroku, Fly, Railway, Supabase, Neon) hand out
///    connection strings as a URL: <c>postgresql://user:pass@host:5432/db</c>.
///    Npgsql 8 only accepts the keyword form (<c>Host=…;Port=…;Username=…</c>)
///    and throws "Format of the initialization string does not conform to
///    specification starting at index 0" on a URL. <see cref="Normalise"/>
///    converts URL → keyword form and passes keyword form through untouched.
///
/// 2. POOL SIZE. Npgsql defaults to <b>Maximum Pool Size = 100 per process
///    per connection string</b>. Render Postgres allows ~97 client
///    connections on every basic tier (100 minus 10 reserved), and only
///    reaches 200 at pro-8gb. The API process alone can therefore exhaust
///    the whole database on its own; add the worker process and Hangfire's
///    separate storage pool and the ceiling is breached at roughly 30-40
///    concurrent requests — long before any CPU or RAM limit is reached.
///    Symptom: <c>53300: sorry, too many clients already</c> and blanket
///    500s. <see cref="WithPool"/> caps each pool so the sum across every
///    process stays under the server ceiling.
///
/// See docs/DEPLOY_RUNBOOK.md for the per-process connection budget.
/// </summary>
public static class PgConnectionStrings
{
    /// <summary>
    /// Accepts either a <c>postgres://</c> / <c>postgresql://</c> URL or an
    /// Npgsql keyword connection string, and always returns keyword form.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="raw"/> is null, blank, or is a URL whose
    /// host cannot be parsed. Failing loudly at startup is deliberate — a
    /// silent fallback to localhost is how a production app ends up quietly
    /// talking to the wrong database.
    /// </exception>
    public static string Normalise(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            throw new ArgumentException("Postgres connection string is missing or blank.", nameof(raw));

        raw = raw.Trim();

        var isUrl = raw.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase)
                 || raw.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase);

        if (!isUrl)
        {
            // Already keyword form. Round-trip through the builder so an
            // invalid string fails here at startup rather than on first query.
            return new NpgsqlConnectionStringBuilder(raw).ConnectionString;
        }

        if (!Uri.TryCreate(raw, UriKind.Absolute, out var uri) || string.IsNullOrEmpty(uri.Host))
            throw new ArgumentException(
                "Postgres connection string looks like a URL but could not be parsed. " +
                "Expected postgresql://user:password@host:port/database.", nameof(raw));

        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = uri.Host,
            Port = uri.IsDefaultPort || uri.Port <= 0 ? 5432 : uri.Port,
            Database = Uri.UnescapeDataString(uri.AbsolutePath.TrimStart('/')),
        };

        // UserInfo is percent-encoded in a URL — passwords routinely contain
        // '@', ':' and '/' which MUST be decoded before Npgsql sees them.
        var userInfo = uri.UserInfo ?? string.Empty;
        if (userInfo.Length > 0)
        {
            var split = userInfo.Split(':', 2);
            builder.Username = Uri.UnescapeDataString(split[0]);
            if (split.Length == 2)
                builder.Password = Uri.UnescapeDataString(split[1]);
        }

        // Carry across query-string parameters (sslmode, channel_binding,
        // application_name, …). Unknown keys are skipped rather than fatal:
        // some providers append vendor-specific params Npgsql doesn't know.
        if (!string.IsNullOrEmpty(uri.Query))
        {
            foreach (var pair in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                var kv = pair.Split('=', 2);
                if (kv.Length != 2) continue;
                var key = Uri.UnescapeDataString(kv[0]);
                var value = Uri.UnescapeDataString(kv[1]);
                try { builder[key] = value; }
                catch (ArgumentException) { /* unknown vendor param — ignore */ }
            }
        }

        return builder.ConnectionString;
    }

    /// <summary>
    /// Normalises <paramref name="raw"/> and clamps the client-side pool.
    /// An explicit <c>Maximum Pool Size</c> already present in the string is
    /// respected — the operator's value wins over the default.
    /// </summary>
    /// <param name="maxPoolSize">
    /// Ceiling for this process's pool for this connection string. Budget it
    /// so (every pool in every process) + headroom stays under the server's
    /// max_connections.
    /// </param>
    /// <param name="applicationName">
    /// Stamped as <c>application_name</c> so <c>pg_stat_activity</c> shows
    /// which pool a connection came from. This is what turns "too many
    /// clients" from a guessing game into a two-minute diagnosis.
    /// </param>
    public static string WithPool(string? raw, int maxPoolSize, string applicationName)
    {
        if (maxPoolSize < 1)
            throw new ArgumentOutOfRangeException(nameof(maxPoolSize), maxPoolSize, "Pool size must be at least 1.");

        var normalised = Normalise(raw);
        var builder = new NpgsqlConnectionStringBuilder(normalised);

        // Only set a default — never override an operator's explicit value.
        //
        // NOTE: do NOT use builder.ContainsKey here. NpgsqlConnectionStringBuilder
        // reports true for every keyword it RECOGNISES, whether or not it was
        // actually supplied, so a ContainsKey guard silently skips every default
        // and leaves MaxPoolSize at 100 — precisely the bug this class exists to
        // prevent. Ask the base DbConnectionStringBuilder instead: it holds only
        // the keys really present in the string.
        var supplied = new System.Data.Common.DbConnectionStringBuilder { ConnectionString = normalised };

        bool WasSupplied(params string[] aliases) => aliases.Any(supplied.ContainsKey);

        if (!WasSupplied("maximum pool size", "maxpoolsize"))
            builder.MaxPoolSize = maxPoolSize;

        if (!WasSupplied("application name", "applicationname"))
            builder.ApplicationName = applicationName;

        // Fail a connection attempt in 15s instead of the 30s default: when
        // the pool IS saturated we want the request to error fast and free
        // its thread, not pile up behind a half-minute wait.
        if (!WasSupplied("timeout"))
            builder.Timeout = 15;

        return builder.ConnectionString;
    }
}
