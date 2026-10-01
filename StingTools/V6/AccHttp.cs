// Licensed to Planscape under the STING Tools plug-in LICENSE. See LICENSE.
//
// StingTools/V6/AccHttp.cs
//
// One transport for every ACC/APS call the plugin makes.
//
// WHY ONE. Each client (issues, model coordination, discovery, upload) grew its own
// retry loop, and they disagreed: the upload retried nothing, the scope-file download
// retried nothing, 429 back-off ignored Retry-After, a 5xx was never retried, and a token
// revoked part-way through a run failed the run instead of refreshing once. The four
// loops were also where the "request message already sent" defect lived. A single
// transport makes those rules one decision instead of four.
//
// THE RULES.
//   * A request is BUILT PER ATTEMPT (an HttpRequestMessage is single-use).
//   * 429: never processed by the server, so any method may retry. Wait Retry-After when
//     sent, else exponential back-off with jitter, capped.
//   * 502/503/504 and transport exceptions: retried only when the caller says the call is
//     idempotent. A POST that creates an ACC issue is NOT - the server may have created it
//     before the gateway failed, and a retry would create a second issue assigned to a
//     real person. (503 WITH Retry-After is the server saying "not processed, come back",
//     so it is retried for any method.)
//   * 401 with credentials: refresh the token once (forced) and retry. A second 401 is an
//     auth failure.
//   * A per-attempt timeout, not HttpClient.Timeout. HttpClient's default is 100 s for the
//     WHOLE exchange, which cancelled every 100 MB upload part on a link slower than
//     ~8 Mbps - the normal case on a Kampala site connection.
//
// Revit-free; logs through StingLog (the tests link a shim).

using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using StingTools.Core;

namespace StingTools.V6
{
    /// <summary>What came back from <see cref="AccHttp.SendAsync"/>. <see cref="Status"/> is 0
    /// when no response arrived at all.</summary>
    public sealed class AccHttpResponse
    {
        public int Status { get; set; }
        public string Body { get; set; } = string.Empty;
        public byte[] Bytes { get; set; }
        /// <summary>The response's Date header (UTC) - ACC's clock, not this workstation's (E8).
        /// Null when the response carried none.</summary>
        public DateTime? ServerDateUtc { get; set; }
        /// <summary>The transport error when <see cref="Status"/> is 0.</summary>
        public string Error { get; set; } = string.Empty;
        /// <summary>How many attempts were made (1 = no retry).</summary>
        public int Attempts { get; set; }
        /// <summary>Authentication could not be (re)established; <see cref="AuthDetail"/> says why.</summary>
        public AccAuthOutcome Auth { get; set; }

        public bool IsSuccess => Status >= 200 && Status < 300;

        /// <summary>The same classification every ACC read uses.</summary>
        public AccFetchStatus Classify()
        {
            if (Auth != null && !Auth.Ok) return Auth.Status;
            return AccFetchOutcome.Classify(Status, IsSuccess ? 1 : -1);
        }

        public string Describe()
        {
            if (Auth != null && !Auth.Ok) return Auth.Detail;
            if (Status == 0) return "the request did not complete: " + Error;
            return AccFetchOutcome.Describe(Classify(), Status);
        }
    }

    public static class AccHttp
    {
        /// <summary>No whole-exchange timeout: each attempt carries its own (see header).</summary>
        internal static readonly HttpClient Client = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };

        /// <summary>Default per-attempt timeout for API calls (not uploads).</summary>
        public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(100);

        public const int DefaultMaxAttempts = 4;

        /// <summary>Longest single wait, whatever Retry-After asks for. A server asking for an
        /// hour is reported as a failure rather than freezing a Revit command for an hour.</summary>
        public static readonly TimeSpan MaxWait = TimeSpan.FromSeconds(60);

        /// <summary>Test seam for every back-off wait. Production waits the real interval.</summary>
        internal static Func<TimeSpan, Task> DelayHook = t => Task.Delay(t);

        private static readonly Random _jitter = new Random();

        /// <summary>
        /// Send with the ACC retry rules. <paramref name="build"/> is called once per attempt.
        /// When <paramref name="creds"/> is given, a Bearer header is attached and a 401 triggers
        /// one forced refresh.
        /// </summary>
        public static async Task<AccHttpResponse> SendAsync(
            Func<HttpRequestMessage> build,
            AccCredentials creds,
            bool idempotent,
            CancellationToken ct = default,
            TimeSpan? timeout = null,
            int maxAttempts = DefaultMaxAttempts,
            bool readBytes = false)
        {
            var result = new AccHttpResponse();
            bool refreshedFor401 = false;

            if (creds != null)
            {
                var auth = await AccIssueSync.EnsureAuthDetailedAsync(creds).ConfigureAwait(false);
                if (!auth.Ok) { result.Auth = auth; return result; }
            }

            for (int attempt = 0; attempt < Math.Max(1, maxAttempts); attempt++)
            {
                result.Attempts = attempt + 1;
                using var attemptCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                attemptCts.CancelAfter(timeout ?? DefaultTimeout);

                HttpResponseMessage resp = null;
                try
                {
                    using var req = build();
                    if (creds != null)
                        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", creds.AccessToken);
                    resp = await Client.SendAsync(req, HttpCompletionOption.ResponseContentRead, attemptCts.Token)
                        .ConfigureAwait(false);
                    result.Status = (int)resp.StatusCode;
                    result.ServerDateUtc = resp.Headers?.Date?.UtcDateTime;
                    if (readBytes) result.Bytes = await resp.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
                    else result.Body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                    result.Error = string.Empty;
                }
                catch (Exception ex) when (IsTransport(ex, ct))
                {
                    resp?.Dispose();
                    result.Status = 0;
                    result.Error = ex is OperationCanceledException
                        ? $"timed out after {(timeout ?? DefaultTimeout).TotalSeconds:F0} s"
                        : ex.Message;
                    if (!idempotent || attempt == maxAttempts - 1) return result;
                    StingLog.Warn($"AccHttp: transport failure ({result.Error}) — retrying (attempt {attempt + 2}/{maxAttempts})");
                    await DelayHook(Backoff(attempt)).ConfigureAwait(false);
                    continue;
                }

                using (resp)
                {
                    int s = result.Status;
                    if (s == 401 && creds != null && !refreshedFor401)
                    {
                        refreshedFor401 = true;
                        var auth = await AccIssueSync.EnsureAuthDetailedAsync(creds, force: true).ConfigureAwait(false);
                        if (!auth.Ok) { result.Auth = auth; return result; }
                        StingLog.Warn("AccHttp: 401 — token refreshed, retrying once");
                        continue;
                    }

                    TimeSpan? retryAfter = RetryAfter(resp);
                    bool retryable =
                        s == 429 ||
                        (s == 503 && retryAfter.HasValue) ||
                        (idempotent && (s == 502 || s == 503 || s == 504));
                    if (!retryable || attempt == maxAttempts - 1) return result;

                    var wait = retryAfter ?? Backoff(attempt);
                    if (wait > MaxWait)
                    {
                        StingLog.Warn($"AccHttp: HTTP {s} asked to wait {wait.TotalSeconds:F0} s — more than {MaxWait.TotalSeconds:F0} s, not waiting");
                        return result;
                    }
                    StingLog.Warn($"ACC {s} — retrying in {wait.TotalSeconds:F1}s (attempt {attempt + 2}/{maxAttempts})");
                    await DelayHook(wait).ConfigureAwait(false);
                }
            }
            return result;
        }

        /// <summary>Retry-After as delta-seconds or an HTTP date.</summary>
        internal static TimeSpan? RetryAfter(HttpResponseMessage resp)
        {
            var ra = resp?.Headers?.RetryAfter;
            if (ra == null) return null;
            if (ra.Delta.HasValue) return ra.Delta.Value < TimeSpan.Zero ? TimeSpan.Zero : ra.Delta.Value;
            if (ra.Date.HasValue)
            {
                var d = ra.Date.Value - DateTimeOffset.UtcNow;
                return d < TimeSpan.Zero ? TimeSpan.Zero : d;
            }
            return null;
        }

        /// <summary>1, 2, 4, 8 … seconds plus up to 25 % jitter, capped at <see cref="MaxWait"/>.</summary>
        internal static TimeSpan Backoff(int attempt)
        {
            double baseSec = Math.Pow(2, Math.Min(attempt, 6));
            double jitter;
            lock (_jitter) jitter = _jitter.NextDouble() * 0.25 * baseSec;
            double sec = Math.Min(baseSec + jitter, MaxWait.TotalSeconds);
            return TimeSpan.FromSeconds(sec);
        }

        /// <summary>A transport failure is a network error or OUR timeout - never the
        /// caller's own cancellation, which must propagate.</summary>
        private static bool IsTransport(Exception ex, CancellationToken callerToken)
        {
            if (ex is OperationCanceledException) return !callerToken.IsCancellationRequested;
            return ex is HttpRequestException || ex is System.IO.IOException;
        }
    }
}
