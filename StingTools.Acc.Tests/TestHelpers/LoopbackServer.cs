// A tiny real HTTP server for the ACC loopback tests.
//
// Why a real listener rather than a mocked HttpMessageHandler: the defects these tests
// exist for live in the CLIENT's handling of a real response — a 404 that used to become
// an empty list, and an HttpRequestMessage that could not be re-sent. A handler mock
// would have let both survive. The listener also lets the test COUNT requests
// server-side, which is what proves a retry actually ran rather than merely not throwing.
//
// http://localhost:<port>/ is the one HttpListener prefix Windows allows without an
// http.sys URL ACL / elevation; it also works on the Linux CI runner.

using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace StingTools.Acc.Tests.TestHelpers
{
    /// <summary>What the server should answer for one request.</summary>
    public sealed class CannedResponse
    {
        public int Status { get; }
        public string Body { get; }
        public string ContentType { get; }

        /// <summary>True for the sentinel below: close the connection without answering.</summary>
        public bool IsAbort { get; private set; }

        public CannedResponse(int status, string body, string contentType = "application/json")
        {
            Status = status;
            Body = body ?? string.Empty;
            ContentType = contentType;
        }

        /// <summary>
        /// Drop the connection without sending a response, so the client raises a genuine
        /// transport error.
        ///
        /// Tests used to simulate this by disposing the server inside its own handler and
        /// relying on the race between shutdown and the response write. That is
        /// platform-dependent: on Windows nothing reached the client and the read failed
        /// (HttpStatus 0), while on Linux the status line arrived and the body was
        /// truncated, so the client saw 200 with unparseable JSON and attributed the
        /// failure to the payload instead of the transport. Both are "the transport
        /// broke", but only one is what a transport-attribution test means to assert.
        /// </summary>
        public static CannedResponse Abort => new CannedResponse(0, string.Empty) { IsAbort = true };
    }

    public sealed class LoopbackServer : IDisposable
    {
        private readonly HttpListener _listener = new HttpListener();
        private readonly Func<int, HttpListenerRequest, CannedResponse> _handler;
        private readonly CancellationTokenSource _cts = new CancellationTokenSource();
        private readonly Task _pump;
        private int _count;
        private readonly List<string> _paths = new List<string>();
        private readonly object _gate = new object();

        /// <summary>Base URL, no trailing slash — feed straight to OverrideHostForTests.</summary>
        public string BaseUrl { get; }

        /// <summary>How many requests the SERVER actually received. Counting here, rather
        /// than trusting the client not to throw, is what proves a retry loop ran.</summary>
        public int RequestCount => Volatile.Read(ref _count);

        /// <summary>Paths received, in order.</summary>
        public IReadOnlyList<string> Paths { get { lock (_gate) return _paths.ToArray(); } }

        /// <param name="handler">Called with the zero-based request index, so a test can
        /// answer 429, 429, then 201.</param>
        public LoopbackServer(Func<int, HttpListenerRequest, CannedResponse> handler)
        {
            _handler = handler ?? throw new ArgumentNullException(nameof(handler));
            int port = FreePort();
            BaseUrl = $"http://localhost:{port}";
            _listener.Prefixes.Add(BaseUrl + "/");
            _listener.Start();
            _pump = Task.Run(PumpAsync);
        }

        /// <summary>Every request gets the same answer.</summary>
        public static LoopbackServer Always(int status, string body)
            => new LoopbackServer((_, __) => new CannedResponse(status, body));

        private async Task PumpAsync()
        {
            while (!_cts.IsCancellationRequested)
            {
                HttpListenerContext ctx;
                try { ctx = await _listener.GetContextAsync().ConfigureAwait(false); }
                catch (Exception) { return; }   // listener stopped

                int index = Interlocked.Increment(ref _count) - 1;
                lock (_gate) _paths.Add(ctx.Request.Url?.PathAndQuery ?? string.Empty);

                CannedResponse canned;
                try { canned = _handler(index, ctx.Request) ?? new CannedResponse(500, "{}"); }
                catch (Exception ex) { canned = new CannedResponse(500, "{\"handlerThrew\":\"" + ex.Message + "\"}"); }

                if (canned.IsAbort)
                {
                    try { ctx.Response.Abort(); } catch (Exception) { /* already gone */ }
                    continue;
                }

                try
                {
                    // Drain the request body so the client sees a clean exchange.
                    if (ctx.Request.HasEntityBody)
                        using (var sr = new StreamReader(ctx.Request.InputStream, Encoding.UTF8))
                            await sr.ReadToEndAsync().ConfigureAwait(false);

                    var bytes = Encoding.UTF8.GetBytes(canned.Body);
                    ctx.Response.StatusCode = canned.Status;
                    ctx.Response.ContentType = canned.ContentType;
                    ctx.Response.ContentLength64 = bytes.Length;
                    await ctx.Response.OutputStream.WriteAsync(bytes, 0, bytes.Length).ConfigureAwait(false);
                    ctx.Response.OutputStream.Close();
                }
                catch (Exception) { /* client hung up; the count is already recorded */ }
            }
        }

        private static int FreePort()
        {
            var probe = new TcpListener(IPAddress.Loopback, 0);
            probe.Start();
            int port = ((IPEndPoint)probe.LocalEndpoint).Port;
            probe.Stop();
            return port;
        }

        public void Dispose()
        {
            _cts.Cancel();
            try { _listener.Stop(); } catch { }
            try { _listener.Close(); } catch { }
            try { _pump.Wait(TimeSpan.FromSeconds(2)); } catch { }
            _cts.Dispose();
        }
    }
}
