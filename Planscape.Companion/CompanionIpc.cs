using System.IO.Pipes;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Planscape.Companion;

// The wire contract (pipe name, timeouts, command names) and the CLIENT live in
// CompanionIpcContract.cs, which is compiled into StingTools too so the two sides
// cannot drift. This file is the SERVER, which only the Companion ever runs.

/// <summary>
/// Serves <see cref="CompanionIpc"/>. One connection at a time, handled and
/// closed — BCC asks a question every few seconds at most, so a connection pool
/// would be machinery with no load to justify it.
/// </summary>
internal sealed class CompanionIpcServer : IAsyncDisposable
{
    private readonly Func<SyncStatus> _status;
    private readonly Func<string?, Task<string>> _syncNow;
    private readonly Func<string, string, Task<string>>? _syncHistory;
    private readonly CancellationTokenSource _cts = new();
    private Task? _loop;

    /// <param name="status">Reads the live status — called per request, never cached.</param>
    /// <param name="syncNow">Triggers a sync; null projectId means every linked project.
    /// Returns a human-readable outcome for the caller to show.</param>
    /// <param name="syncHistory">Slice E — pulls the full version history of ONE
    /// document. Optional so a host that does not offer it refuses the command
    /// rather than accepting and doing nothing.</param>
    public CompanionIpcServer(
        Func<SyncStatus> status,
        Func<string?, Task<string>> syncNow,
        Func<string, string, Task<string>>? syncHistory = null)
    {
        _status = status;
        _syncNow = syncNow;
        _syncHistory = syncHistory;
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
            case CompanionIpc.CmdPing:
                // Deliberately cheap and side-effect free: this is how BCC asks
                // "are you there" without provoking any work.
                return JsonConvert.SerializeObject(new
                {
                    ok = true,
                    pid = Environment.ProcessId,
                    version = typeof(CompanionIpcServer).Assembly.GetName().Version?.ToString(),
                });

            case CompanionIpc.CmdStatus:
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
                    checkedOutCount = s.CheckedOutCount,
                    checkedOut = s.CheckedOut,
                });
            }

            case CompanionIpc.CmdSyncNow:
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

            case CompanionIpc.CmdHistory:
            {
                var projectId = req["projectId"]?.Value<string>();
                var documentId = req["documentId"]?.Value<string>();
                if (_syncHistory == null)
                    return JsonConvert.SerializeObject(new { ok = false, error = "history download is not available" });
                if (string.IsNullOrWhiteSpace(projectId) || string.IsNullOrWhiteSpace(documentId))
                    return JsonConvert.SerializeObject(new { ok = false, error = "projectId and documentId are required" });

                // Fire-and-forget like sync-now, for the same reason: a document
                // with twenty versions is a long download and the caller is a UI.
                _ = Task.Run(async () =>
                {
                    try { CompanionLog.Info(await _syncHistory(projectId!, documentId!)); }
                    catch (Exception ex) { CompanionLog.Error("sync-history failed", ex); }
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
