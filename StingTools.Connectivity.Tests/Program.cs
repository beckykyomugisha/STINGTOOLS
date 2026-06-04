using System;
using System.IO;
using System.Net.Http;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Threading.Tasks;
using StingTools.BIMManager;

namespace StingTools.Connectivity.Tests;

// Pure static-logic tests for PlanscapeServerClient connectivity classification.
// Run: dotnet run --project StingTools.Connectivity.Tests
internal static class Program
{
    private static int _pass;
    private static int _fail;

    private static void Check(string name, bool ok, string detail = "")
    {
        if (ok) { _pass++; Console.WriteLine($"  PASS  {name}"); }
        else    { _fail++; Console.WriteLine($"  FAIL  {name}  {detail}"); }
    }

    private static void Contains(string name, string actual, string expectedSubstring) =>
        Check(name, actual.IndexOf(expectedSubstring, StringComparison.OrdinalIgnoreCase) >= 0,
              $"expected to contain «{expectedSubstring}» but was «{actual}»");

    private static void Equal(string name, string actual, string expected) =>
        Check(name, string.Equals(actual, expected, StringComparison.Ordinal),
              $"expected «{expected}» but was «{actual}»");

    private static int Main(string[] args)
    {
        // --live <url> <email> <password> : exercise the ACTUAL plugin
        // connectivity path (NormalizeServerUrl + a real /api/auth/login POST)
        // against a running server and print the resolved tenant/tier. This is
        // the live proof that the deployed connectivity fix logs in; the
        // default (no args) runs the CI-safe pure-logic tests below.
        if (args.Length >= 1 && args[0] == "--live")
            return LiveLogin(args.Length > 1 ? args[1] : "localhost:5000",
                             args.Length > 2 ? args[2] : "admin@planscape.demo",
                             args.Length > 3 ? args[3] : "admin123").GetAwaiter().GetResult();

        Console.WriteLine("PlanscapeServerClient connectivity classification tests");
        Console.WriteLine("=======================================================");

        // ── FIX 1 — TLS / scheme-mismatch / proxy classification ───────────────

        // 1. TLS AuthenticationException (wrapped in HttpRequestException), https URL
        var tlsAuth = new HttpRequestException(
            "The SSL connection could not be established.",
            new AuthenticationException("The remote certificate is invalid."));
        var h1 = PlanscapeServerClient.BuildConnectivityHint(tlsAuth, "https://localhost:5000");
        Contains("TLS AuthenticationException(https) -> HTTPS handshake hint", h1, "secure (HTTPS) handshake");
        Contains("TLS AuthenticationException(https) -> nudges to http", h1, "use http://localhost:5000");
        Contains("TLS AuthenticationException(https) -> proxy note", h1, "TLS-inspection proxy");
        Contains("TLS AuthenticationException(https) -> preserves raw detail", h1, "detail:");

        // 2. Bare HttpRequestException (reached transport, no inner), https URL —
        //    the canonical http/https-mismatch shape (TLS client vs plaintext server).
        var bareHttps = new HttpRequestException("An error occurred while sending the request.");
        var h2 = PlanscapeServerClient.BuildConnectivityHint(bareHttps, "https://localhost:5000");
        Contains("bare HttpRequestException(https) -> HTTPS handshake hint", h2, "secure (HTTPS) handshake");
        Check("bare HttpRequestException(https) -> NOT the raw fall-through",
              !h2.StartsWith("Login failed: An error occurred while sending the request.", StringComparison.Ordinal),
              $"was «{h2}»");

        // 3. IOException (unexpected EOF) wrapped in HttpRequestException, http URL —
        //    server actually requires https.
        var ioHttp = new HttpRequestException(
            "An error occurred while sending the request.",
            new IOException("The response ended prematurely."));
        var h3 = PlanscapeServerClient.BuildConnectivityHint(ioHttp, "http://cloud.example.com");
        Contains("IOException(http) -> points at https", h3, "use https://");
        Contains("IOException(http) -> transport-error wording", h3, "reached the network");
        Contains("IOException(http) -> preserves raw detail", h3, "detail:");

        // ── Existing branches must be unchanged ────────────────────────────────

        // 4. SocketException(ConnectionRefused) wrapped, local URL (https) — socket
        //    branch still wins; TLS classification must NOT shadow it.
        var refused = new HttpRequestException(
            "Connection refused",
            new SocketException((int)SocketError.ConnectionRefused));
        var h4 = PlanscapeServerClient.BuildConnectivityHint(refused, "https://localhost:5000");
        Contains("SocketException(refused, local) -> docker hint", h4, "nothing is listening");
        Check("SocketException(refused) -> NOT misclassified as TLS",
              h4.IndexOf("HTTPS handshake", StringComparison.OrdinalIgnoreCase) < 0, $"was «{h4}»");

        // 4b. HostNotFound still resolves to the DNS hint.
        var dns = new HttpRequestException("dns", new SocketException((int)SocketError.HostNotFound));
        var h4b = PlanscapeServerClient.BuildConnectivityHint(dns, "https://typo.invalid");
        Contains("SocketException(HostNotFound) -> DNS hint", h4b, "could not resolve");

        // 5. TaskCanceledException -> timeout hint (unchanged).
        var timeout = new TaskCanceledException("A task was canceled.");
        var h5 = PlanscapeServerClient.BuildConnectivityHint(timeout, "http://localhost:5000");
        Contains("TaskCanceledException -> timeout hint", h5, "timed out before the server responded");

        // 6. Genuinely-unclassifiable non-transport exception keeps the raw fall-through.
        var weird = new InvalidOperationException("totally unexpected");
        var h6 = PlanscapeServerClient.BuildConnectivityHint(weird, "http://localhost:5000");
        Equal("unknown exception -> raw fall-through", h6, "Login failed: totally unexpected");

        // ── FIX 2 — NormalizeServerUrl (fill missing scheme only) ──────────────

        Equal("normalize: bare host:port -> http://", PlanscapeServerClient.NormalizeServerUrl("localhost:5000"), "http://localhost:5000");
        Equal("normalize: 127.0.0.1:port -> http://", PlanscapeServerClient.NormalizeServerUrl("127.0.0.1:5000"), "http://127.0.0.1:5000");
        Equal("normalize: trailing slash trimmed", PlanscapeServerClient.NormalizeServerUrl("http://localhost:5000/"), "http://localhost:5000");
        Equal("normalize: existing http preserved", PlanscapeServerClient.NormalizeServerUrl("http://localhost:5000"), "http://localhost:5000");
        // CONSTRAINT: never silently rewrite a user's https -> http.
        Equal("normalize: https NOT rewritten to http", PlanscapeServerClient.NormalizeServerUrl("https://localhost:5000"), "https://localhost:5000");
        Equal("normalize: https cloud preserved", PlanscapeServerClient.NormalizeServerUrl("https://app.planscape.example/"), "https://app.planscape.example");
        Equal("normalize: whitespace trimmed + scheme filled", PlanscapeServerClient.NormalizeServerUrl("  localhost:5000  "), "http://localhost:5000");
        Equal("normalize: empty stays empty", PlanscapeServerClient.NormalizeServerUrl(""), "");

        // ── FIX 3 — diagnostics helpers ────────────────────────────────────────

        Equal("scheme of https url", PlanscapeServerClient.SchemeOf("https://localhost:5000"), "https");
        Equal("scheme of http url", PlanscapeServerClient.SchemeOf("http://localhost:5000"), "http");
        Contains("exception chain rendering", PlanscapeServerClient.DescribeExceptionChain(tlsAuth),
                 "inner=HttpRequestException->AuthenticationException");

        Console.WriteLine("=======================================================");
        Console.WriteLine($"RESULT: {_pass} passed, {_fail} failed");
        return _fail == 0 ? 0 : 1;
    }

    /// <summary>
    /// Live proof of the Planscape Connect path: normalise the URL exactly as
    /// the plugin does (PlanscapeServerClient.NormalizeServerUrl — the FIX-2
    /// code), then POST /api/auth/login and read the tenant/tier the BIM
    /// Coordination Center shows on a successful connect.
    /// </summary>
    private static async System.Threading.Tasks.Task<int> LiveLogin(string url, string email, string password)
    {
        var normalized = PlanscapeServerClient.NormalizeServerUrl(url);
        Console.WriteLine($"Planscape Connect — live login");
        Console.WriteLine($"  input url      : {url}");
        Console.WriteLine($"  normalized url : {normalized}   (scheme={PlanscapeServerClient.SchemeOf(normalized)})");
        try
        {
            using var http = new HttpClient { BaseAddress = new Uri(normalized), Timeout = TimeSpan.FromSeconds(20) };
            var body = new StringContent(
                "{\"email\":\"" + email + "\",\"password\":\"" + password + "\"}",
                System.Text.Encoding.UTF8, "application/json");
            var resp = await http.PostAsync("/api/auth/login", body);
            var json = await resp.Content.ReadAsStringAsync();
            if (!resp.IsSuccessStatusCode)
            {
                Console.WriteLine($"  FAILED  HTTP {(int)resp.StatusCode}: {json}");
                return 1;
            }
            string Pick(string key)
            {
                var i = json.IndexOf("\"" + key + "\"", StringComparison.OrdinalIgnoreCase);
                if (i < 0) return "";
                var c = json.IndexOf(':', i); var q1 = json.IndexOf('"', c + 1);
                var q2 = json.IndexOf('"', q1 + 1);
                return q1 < 0 || q2 < 0 ? "" : json.Substring(q1 + 1, q2 - q1 - 1);
            }
            var token = Pick("accessToken");
            Console.WriteLine($"  CONNECTED  user={Pick("userName")}  role={Pick("role")}  tier={Pick("tier")}  tokenLen={token.Length}");
            return string.IsNullOrEmpty(token) ? 1 : 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine("  " + PlanscapeServerClient.BuildConnectivityHint(ex, normalized));
            return 1;
        }
    }
}
