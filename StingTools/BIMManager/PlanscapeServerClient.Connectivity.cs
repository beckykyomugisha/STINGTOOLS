#nullable enable
using System;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace StingTools.BIMManager;

/// <summary>
/// Connectivity diagnostics for the Planscape login flow, split into its own
/// partial so the classification logic is pure BCL (no Revit API, no WPF, no
/// other StingTools members) and can be unit-tested in isolation
/// (StingTools.Connectivity.Tests links just this file).
///
/// FIX 1 — classify the TLS / scheme-mismatch / proxy family in addition to the
/// SocketException + timeout families. An http/https scheme mismatch (e.g. the
/// user enters https://localhost:5000 while the docker stack serves PLAIN HTTP
/// on 5000) reaches the transport and breaks at the TLS handshake, producing NO
/// SocketException — so the old code fell through to the raw HttpRequestException
/// message ("An error occurred while sending the request"), the least helpful
/// string in the whole flow.
/// </summary>
public sealed partial class PlanscapeServerClient
{
    /// <summary>
    /// FIX 2 — fill a MISSING URL scheme with http:// (the docker-compose
    /// default). Only fills when the scheme is absent; NEVER rewrites an
    /// explicit scheme (so a user-supplied https:// cloud URL is preserved and
    /// a real http/https config mistake is surfaced by FIX 1 rather than masked).
    /// Also trims a trailing slash so BaseAddress comparison is stable.
    /// </summary>
    internal static string NormalizeServerUrl(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return raw ?? "";
        var s = raw!.Trim();
        // "://" present ⇒ an explicit scheme is already there ⇒ leave it alone.
        if (s.IndexOf("://", StringComparison.Ordinal) < 0)
            s = "http://" + s;
        return s.TrimEnd('/');
    }

    /// <summary>Best-effort URI scheme of a (possibly schemeless) server URL.</summary>
    internal static string SchemeOf(string? serverUrl)
    {
        if (string.IsNullOrWhiteSpace(serverUrl)) return "";
        return Uri.TryCreate(serverUrl, UriKind.Absolute, out var u) ? u.Scheme : "";
    }

    /// <summary>
    /// FIX 3 — render the inner-exception type chain, e.g.
    /// "inner=HttpRequestException-&gt;AuthenticationException", so the log file
    /// distinguishes scheme-mismatch from proxy from genuine socket failure.
    /// </summary>
    internal static string DescribeExceptionChain(Exception ex)
    {
        var sb = new StringBuilder("inner=");
        bool first = true;
        for (Exception? cur = ex; cur != null; cur = cur.InnerException)
        {
            if (!first) sb.Append("->");
            sb.Append(cur.GetType().Name);
            first = false;
        }
        return sb.ToString();
    }

    /// <summary>Maps the raw HttpClient/socket exception to an actionable message.
    /// Connection refused on localhost almost always means the docker stack
    /// isn't running, which is the most common first-time-setup mistake; a
    /// failed TLS handshake on an https:// URL almost always means an
    /// http/https mismatch (the local stack is PLAIN HTTP) or a TLS-inspection
    /// proxy — the second most common first-setup mistake (FIX 1).</summary>
    internal static string BuildConnectivityHint(Exception ex, string serverUrl)
    {
        // Walk the inner exception chain — HttpRequestException usually wraps
        // a SocketException whose ErrorCode tells us refused vs. unreachable
        // vs. DNS failure.
        var sock = ex as System.Net.Sockets.SocketException;
        for (var cur = ex; sock == null && cur != null; cur = cur.InnerException)
            sock = cur.InnerException as System.Net.Sockets.SocketException;

        bool isLocal = !string.IsNullOrEmpty(serverUrl) &&
                       (serverUrl.IndexOf("localhost", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        serverUrl.IndexOf("127.0.0.1", StringComparison.OrdinalIgnoreCase) >= 0);

        if (sock != null)
        {
            switch (sock.SocketErrorCode)
            {
                case System.Net.Sockets.SocketError.ConnectionRefused:
                    return isLocal
                        ? $"Login failed: nothing is listening on {serverUrl}. Start the local Planscape server with 'docker compose up -d' from Planscape.Server/docker, then wait for the 'api' container to become healthy (docker compose ps)."
                        : $"Login failed: {serverUrl} refused the connection. The server may be stopped or a firewall is blocking the port.";
                case System.Net.Sockets.SocketError.HostNotFound:
                case System.Net.Sockets.SocketError.NoData:
                    return $"Login failed: could not resolve '{serverUrl}'. Check the URL spelling and your DNS/internet connection.";
                case System.Net.Sockets.SocketError.TimedOut:
                    return $"Login failed: connection to {serverUrl} timed out. Check the server is reachable from this machine and no firewall is dropping the request.";
                case System.Net.Sockets.SocketError.NetworkUnreachable:
                case System.Net.Sockets.SocketError.HostUnreachable:
                    return $"Login failed: {serverUrl} is not reachable from this network.";
            }
        }

        if (ex is TaskCanceledException || ex is OperationCanceledException)
            return $"Login failed: request to {serverUrl} timed out before the server responded.";

        // FIX 1 — TLS / scheme-mismatch / proxy classification.
        //
        // Only when there is NO SocketException anywhere in the chain (a socket
        // failure is a connect-level problem already handled above, and we must
        // not misclassify an unlisted socket error as TLS). A failed handshake
        // shows up as one of:
        //   • AuthenticationException — the TLS handshake itself was rejected
        //   • IOException            — "received an unexpected EOF" / "response
        //                              ended prematurely" when a TLS client hits
        //                              a plaintext server (or vice-versa)
        //   • a bare HttpRequestException whose inner is NOT a SocketException —
        //     the request reached the transport but broke at TLS/HTTP/proxy.
        if (sock == null)
        {
            bool handshakeFamily = false;
            for (Exception? cur = ex; cur != null; cur = cur.InnerException)
            {
                if (cur is System.Security.Authentication.AuthenticationException
                    || cur is System.IO.IOException)
                {
                    handshakeFamily = true;
                    break;
                }
            }
            // A bare HttpRequestException that reached transport (no socket
            // failure in the chain) is the canonical http/https-mismatch shape.
            if (handshakeFamily || ex is HttpRequestException)
            {
                var scheme = SchemeOf(serverUrl);
                if (string.Equals(scheme, "https", StringComparison.OrdinalIgnoreCase))
                {
                    return $"Login failed: couldn't complete a secure (HTTPS) handshake with {serverUrl}. " +
                           "This usually means an http/https mismatch — the local Planscape docker stack serves " +
                           "PLAIN HTTP, so use http://localhost:5000 (not https). If you're on a corporate network, " +
                           $"a TLS-inspection proxy may also be blocking the request. (detail: {ex.Message})";
                }
                if (string.Equals(scheme, "http", StringComparison.OrdinalIgnoreCase))
                {
                    return $"Login failed: the request to {serverUrl} reached the network but the connection broke " +
                           "before a response (HTTP/transport error). If this server requires HTTPS, use https:// " +
                           "instead of http://. A corporate TLS-inspection proxy can also cause this. " +
                           $"(detail: {ex.Message})";
                }
            }
        }

        return $"Login failed: {ex.Message}";
    }
}
