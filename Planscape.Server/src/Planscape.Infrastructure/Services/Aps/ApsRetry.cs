using System.Net;
using Microsoft.Extensions.Logging;

namespace Planscape.Infrastructure.Services.Aps;

/// <summary>
/// Small shared retry policy for APS HTTP calls.
///
/// RULES
///   * 429 is always retried: the server refused the request before doing
///     anything, so even a non-idempotent POST cannot have been applied.
///   * 503 is retried for a non-idempotent request ONLY when the response
///     carries Retry-After — that pairing is the "I did not process this,
///     come back later" signal. A bare 503/500/502/504 on a POST is
///     ambiguous (the create may have landed) and is NOT retried: a
///     duplicated ACC issue is worse than a reported failure.
///   * Idempotent requests (GET) also retry 408/500/502/503/504 and transport
///     errors.
///   * Retry-After (delta-seconds or HTTP-date) is honoured, capped at
///     <see cref="MaxDelay"/>; otherwise exponential backoff with full jitter.
///
/// The request is rebuilt by <paramref name="makeRequest"/> on every attempt
/// because an HttpRequestMessage cannot be sent twice.
/// </summary>
public static class ApsRetry
{
    public const int DefaultMaxAttempts = 4;
    public static readonly TimeSpan MaxDelay = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan BaseDelay = TimeSpan.FromSeconds(1);

    /// <summary>Test seam: replace to avoid real sleeps.</summary>
    internal static Func<TimeSpan, CancellationToken, Task> Delay = (d, ct) => Task.Delay(d, ct);

    public static async Task<HttpResponseMessage> SendAsync(
        HttpClient http,
        Func<HttpRequestMessage> makeRequest,
        bool idempotent,
        ILogger? logger,
        CancellationToken ct,
        int maxAttempts = DefaultMaxAttempts)
    {
        for (int attempt = 1; ; attempt++)
        {
            HttpResponseMessage resp;
            try
            {
                resp = await http.SendAsync(makeRequest(), ct);
            }
            catch (HttpRequestException ex) when (idempotent && attempt < maxAttempts)
            {
                var d = Backoff(attempt);
                logger?.LogWarning(ex, "APS request transport error (attempt {Attempt}/{Max}); retrying in {Delay}",
                    attempt, maxAttempts, d);
                await Delay(d, ct);
                continue;
            }
            catch (TaskCanceledException ex) when (!ct.IsCancellationRequested && idempotent && attempt < maxAttempts)
            {
                // HttpClient timeout, not caller cancellation.
                var d = Backoff(attempt);
                logger?.LogWarning(ex, "APS request timed out (attempt {Attempt}/{Max}); retrying in {Delay}",
                    attempt, maxAttempts, d);
                await Delay(d, ct);
                continue;
            }

            if (attempt >= maxAttempts || !ShouldRetry(resp, idempotent))
                return resp;

            var delay = RetryAfter(resp) ?? Backoff(attempt);
            if (delay > MaxDelay) delay = MaxDelay;
            logger?.LogWarning("APS request returned HTTP {Status} (attempt {Attempt}/{Max}); retrying in {Delay}",
                (int)resp.StatusCode, attempt, maxAttempts, delay);
            resp.Dispose();
            await Delay(delay, ct);
        }
    }

    internal static bool ShouldRetry(HttpResponseMessage resp, bool idempotent)
    {
        var code = resp.StatusCode;
        if (code == HttpStatusCode.TooManyRequests) return true;
        if (!idempotent)
            return code == HttpStatusCode.ServiceUnavailable && RetryAfter(resp).HasValue;
        return code is HttpStatusCode.RequestTimeout
            or HttpStatusCode.InternalServerError
            or HttpStatusCode.BadGateway
            or HttpStatusCode.ServiceUnavailable
            or HttpStatusCode.GatewayTimeout;
    }

    internal static TimeSpan? RetryAfter(HttpResponseMessage resp)
    {
        var ra = resp.Headers.RetryAfter;
        if (ra == null) return null;
        if (ra.Delta.HasValue) return ra.Delta.Value < TimeSpan.Zero ? TimeSpan.Zero : ra.Delta.Value;
        if (ra.Date.HasValue)
        {
            var d = ra.Date.Value - DateTimeOffset.UtcNow;
            return d < TimeSpan.Zero ? TimeSpan.Zero : d;
        }
        return null;
    }

    private static TimeSpan Backoff(int attempt)
    {
        // Full jitter: random in [0, base * 2^(attempt-1)], capped at 30 s.
        double ceiling = Math.Min(30_000, BaseDelay.TotalMilliseconds * Math.Pow(2, attempt - 1));
        return TimeSpan.FromMilliseconds(Random.Shared.NextDouble() * ceiling);
    }
}
