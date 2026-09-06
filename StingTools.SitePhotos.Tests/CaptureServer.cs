using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace StingTools.SitePhotos.Tests;

/// <summary>
/// A minimal in-process HTTP server the client can be pointed at.
///
/// It exists for one reason the real docker stack cannot serve: proving a
/// NEGATIVE. "The client-side guard fired before any request was made" is only
/// demonstrable by counting the requests that arrived, and that requires owning
/// the server. Every request is recorded with its method and path.
///
/// It also lets the export tests hand back an exact byte sequence (a real PDF
/// header, or a deliberately empty body) which a live server will not reproduce
/// on demand.
/// </summary>
public sealed class CaptureServer : IDisposable
{
    private readonly HttpListener _listener = new();
    private readonly CancellationTokenSource _cts = new();
    private readonly List<(string Method, string Path)> _requests = new();
    private readonly object _lock = new();

    /// <summary>Handlers keyed by path suffix; first match wins. Return (status, contentType, body).</summary>
    public List<(Func<string, bool> Match, Func<HttpListenerRequest, (int Status, string ContentType, byte[] Body)> Handle)> Routes { get; } = new();

    public string BaseUrl { get; }

    public CaptureServer()
    {
        var port = FreePort();
        BaseUrl = $"http://127.0.0.1:{port}";
        _listener.Prefixes.Add(BaseUrl + "/");
        _listener.Start();
        _ = Task.Run(LoopAsync);

        // Auth is not what these tests are about, but every client method calls
        // EnsureAuthenticatedAsync() before its guards, so a session has to exist
        // or every assertion would collapse into "Not connected."
        Routes.Add((p => p.EndsWith("/api/auth/login", StringComparison.OrdinalIgnoreCase),
            _ => (200, "application/json", Encoding.UTF8.GetBytes(
                "{\"accessToken\":\"" + FakeJwt() + "\"," +
                "\"refreshToken\":\"r\"," +
                "\"expiresAt\":\"" + DateTime.UtcNow.AddHours(8).ToString("o") + "\"," +
                "\"userName\":\"harness@test\",\"tier\":\"Professional\"}"))));
    }

    /// <summary>Requests seen so far, oldest first.</summary>
    public IReadOnlyList<(string Method, string Path)> Requests
    {
        get { lock (_lock) return _requests.ToArray(); }
    }

    public int RequestCount { get { lock (_lock) return _requests.Count; } }

    /// <summary>Paths seen since a marker count — the basis of "no call was made".</summary>
    public IReadOnlyList<string> PathsSince(int marker)
    {
        lock (_lock)
        {
            var outp = new List<string>();
            for (int i = marker; i < _requests.Count; i++) outp.Add(_requests[i].Path);
            return outp;
        }
    }

    /// <summary>Stop accepting connections — used to simulate the API going down mid-session.</summary>
    public void Kill()
    {
        try { _cts.Cancel(); } catch { }
        try { _listener.Stop(); } catch { }
        try { _listener.Close(); } catch { }
    }

    private async Task LoopAsync()
    {
        while (!_cts.IsCancellationRequested)
        {
            HttpListenerContext ctx;
            try { ctx = await _listener.GetContextAsync().ConfigureAwait(false); }
            catch { return; } // listener stopped

            var path = ctx.Request.Url?.AbsolutePath ?? "";
            lock (_lock) _requests.Add((ctx.Request.HttpMethod, path));

            int status = 404;
            string contentType = "application/json";
            byte[] body = Encoding.UTF8.GetBytes("{\"error\":\"no_route\"}");

            foreach (var (match, handle) in Routes)
            {
                if (!match(path)) continue;
                try { (status, contentType, body) = handle(ctx.Request); }
                catch (Exception ex)
                {
                    status = 500;
                    body = Encoding.UTF8.GetBytes("{\"error\":\"" + ex.Message.Replace("\"", "'") + "\"}");
                }
                break;
            }

            try
            {
                ctx.Response.StatusCode = status;
                ctx.Response.ContentType = contentType;
                ctx.Response.ContentLength64 = body.Length;
                await ctx.Response.OutputStream.WriteAsync(body, 0, body.Length).ConfigureAwait(false);
                ctx.Response.OutputStream.Close();
            }
            catch { /* client hung up */ }
        }
    }

    /// <summary>
    /// Three base64url segments so ParseTenantAndUser can split it. Its body is a
    /// real JSON payload with a tenant id, because the client decodes it — a
    /// malformed token is swallowed there, but a valid one keeps the test honest
    /// about what the client actually receives.
    /// </summary>
    private static string FakeJwt()
    {
        string B64(string s) => Convert.ToBase64String(Encoding.UTF8.GetBytes(s))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var payload = "{\"tenant_id\":\"" + Guid.NewGuid() + "\",\"sub\":\"" + Guid.NewGuid() + "\"}";
        return B64("{\"alg\":\"none\",\"typ\":\"JWT\"}") + "." + B64(payload) + ".sig";
    }

    private static int FreePort()
    {
        var l = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        l.Start();
        var p = ((IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        return p;
    }

    public void Dispose() => Kill();
}
