using System.IO.Pipes;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Planscape.Companion;

/// <summary>
/// The local surface BCC talks to. Named pipe, one JSON line per request, one
/// per response.
///
/// <para><b>Why a pipe and not the file-drop StingLink.exe uses.</b> That
/// precedent was reasoned about a specific asymmetry: StingLink is short-lived
/// and its receiver (Revit) had no listener, so a pipe's only job would have been
/// handing work to an <c>IIdlingJob</c> that already existed. Neither half holds
/// here — the Companion is long-lived and can host an accept loop, and BCC is
/// asking a question rather than dropping a job.</para>
///
/// <para>The deciding factor is the failure mode. With a file drop, "the
/// Companion isn't running" and "the Companion is busy" look identical at the
/// moment the user clicks Sync now; distinguishing them means writing, polling
/// and then guessing how stale is too stale. A pipe connect that fails in 200 ms
/// answers it exactly.</para>
///
/// <para><b>Why not loopback HTTP.</b> <c>HttpListener</c> on a fixed port needs a
/// URL ACL, which is machine-wide state and an elevation prompt. Kestrel avoids
/// that but drags ASP.NET Core hosting into a tray app to serve two verbs, and an
/// open TCP port is reachable by any local process — including a browser tab — so
/// it would need its own bearer token. A pipe is ACL'd to the creating user by
/// Windows, with no port and no firewall prompt.</para>
/// </summary>
internal static class CompanionIpc
{
    /// <summary>
    /// Fixed name — no user suffix. Windows scopes a default-ACL pipe to the
    /// creating user already, and a predictable name is what lets BCC connect
    /// without a discovery file to keep in sync.
    /// </summary>
    public const string PipeName = "planscape-companion";

    /// <summary>
    /// How long a client waits before calling it "not running". Deliberately
    /// short: this runs on a UI click, and the answer is almost always instant or
    /// never.
    /// </summary>
    public const int ConnectTimeoutMs = 500;
}

/// <summary>
/// Serves <see cref="CompanionIpc"/>. One connection at a time, handled and
/// closed — BCC asks a question every few seconds at most, so a connection pool
/// would be machinery with no load to justify it.
/// </summary>
internal sealed class CompanionIpcServer : IAsyncDisposable
{
    private readonly Func<SyncStatus> _status;
    private readonly Func<string?, Task<string>> _syncNow;
    private readonly CancellationTokenSource _cts = new();
    private Task? _loop;

    /// <param name="status">Reads the live status — called per request, never cached.</param>
    /// <param name="syncNow">Triggers a sync; null projectId means every linked project.
    /// Returns a human-readable outcome for the caller to show.</param>
    public CompanionIpcServer(Func<SyncStatus> status, Func<string?, Task<string>> syncNow)
    {
        _status = status;
        _syncNow = syncNow;
    }

    public void Start()
    {
        _loop = Task.Run(() => AcceptLoopAsync(_cts.Token));
        CompanionLog.Info($"IPC listening on \\\\.\\pipe\\{CompanionIpc.PipeName}");
    }

    private async Task AcceptLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                // A fresh server stream per connection. Reusing one across
                // connections is possible but the disconnect/reconnect state
                // machine is fiddly and buys nothing at this call rate.
                using var pipe = new NamedPipeServerStream(
                    CompanionIpc.PipeName,
                    PipeDirection.InOut,
                    maxNumberOfServerInstances: 1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous);

                await pipe.WaitForConnectionAsync(ct);
                await HandleAsync(pipe, ct);
            }
            catch (OperationCanceledException)
            {
                return; // shutting down
            }
            catch (Exception ex)
            {
                // One bad client must not end the accept loop — that would take
                // the whole IPC surface down until the next login.
                CompanionLog.Warn($"IPC connection failed: {ex.Message}");
                try { await Task.Delay(250, ct); } catch { return; }
            }
        }
    }

    private async Task HandleAsync(NamedPipeServerStream pipe, CancellationToken ct)
    {
        // Not `using` on the reader/writer: disposing them would close the pipe
        // the caller still owns and disposes itself.
        var reader = new StreamReader(pipe, Encoding.UTF8, leaveOpen: true);
        var writer = new StreamWriter(pipe, new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true };

        var line = await reader.ReadLineAsync(ct);
        if (string.IsNullOrWhiteSpace(line)) return;

        string response;
        try
        {
            response = Dispatch(await ParseAsync(line));
        }
        catch (Exception ex)
        {
            CompanionLog.Warn($"IPC request failed: {ex.Message}");
            response = JsonConvert.SerializeObject(new { ok = false, error = ex.Message });
        }
        await writer.WriteLineAsync(response);

        async Task<JObject> ParseAsync(string raw) => await Task.FromResult(JObject.Parse(raw));
    }

    private string Dispatch(JObject req)
    {
        var cmd = req["cmd"]?.Value<string>()?.ToLowerInvariant();
        switch (cmd)
        {
            case "ping":
                // Deliberately cheap and side-effect free: this is how BCC asks
                // "are you there" without provoking any work.
                return JsonConvert.SerializeObject(new
                {
                    ok = true,
                    pid = Environment.ProcessId,
                    version = typeof(CompanionIpcServer).Assembly.GetName().Version?.ToString(),
                });

            case "status":
            {
                var s = _status();
                return JsonConvert.SerializeObject(new
                {
                    ok = true,
                    state = s.State.ToString(),
                    summary = s.Summary(),
                    lastSuccessUtc = s.LastSuccessUtc,
                    lastError = s.LastError,
                    lastErrorUtc = s.LastErrorUtc,
                    consecutiveFailures = s.ConsecutiveFailures,
                    linkedProjects = s.LinkedProjects,
                    filesLastSync = s.FilesLastSync,
                });
            }

            case "sync-now":
            {
                var projectId = req["projectId"]?.Value<string>();
                // Fire-and-forget with the result reported through status. A sync
                // can take minutes; holding the pipe open would block BCC's UI
                // thread on a file download, which is exactly the freeze this
                // whole out-of-process design exists to avoid.
                _ = Task.Run(async () =>
                {
                    try { CompanionLog.Info(await _syncNow(projectId)); }
                    catch (Exception ex) { CompanionLog.Error("sync-now failed", ex); }
                });
                return JsonConvert.SerializeObject(new { ok = true, started = true });
            }

            default:
                return JsonConvert.SerializeObject(new { ok = false, error = $"unknown command '{cmd}'" });
        }
    }

    public async ValueTask DisposeAsync()
    {
        _cts.Cancel();
        // A pipe blocked in WaitForConnectionAsync only unblocks when the token
        // fires; give it a moment, but never hang shutdown on it.
        if (_loop != null)
        {
            try { await _loop.WaitAsync(TimeSpan.FromSeconds(2)); }
            catch (Exception) { /* shutting down anyway */ }
        }
        _cts.Dispose();
    }
}

/// <summary>
/// Client side. Lives here rather than in StingTools so the request and response
/// shapes cannot drift apart — when BCC integration lands (Slice D) it links this
/// file in, the same way StingLink.exe shares PlanscapeProtocol.cs.
/// </summary>
internal static class CompanionIpcClient
{
    /// <summary>
    /// Send one command. Returns null when the Companion is not running, which is
    /// a normal answer and not an error — a user may simply not have started it.
    /// </summary>
    public static async Task<JObject?> SendAsync(object request, CancellationToken ct = default)
    {
        try
        {
            using var pipe = new NamedPipeClientStream(".", CompanionIpc.PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
            await pipe.ConnectAsync(CompanionIpc.ConnectTimeoutMs, ct);

            var writer = new StreamWriter(pipe, new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true };
            var reader = new StreamReader(pipe, Encoding.UTF8, leaveOpen: true);

            await writer.WriteLineAsync(JsonConvert.SerializeObject(request));
            var line = await reader.ReadLineAsync(ct);
            return string.IsNullOrWhiteSpace(line) ? null : JObject.Parse(line);
        }
        catch (TimeoutException) { return null; }   // not running
        catch (IOException) { return null; }        // died mid-request
        catch (OperationCanceledException) { return null; }
    }
}
