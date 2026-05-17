using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Planscape.Infrastructure.Security;

/// <summary>
/// Phase 175 audit P1-15 — minimal clamd client over TCP. Speaks the
/// INSTREAM protocol so we don't have to share a filesystem with the
/// scanner. Single-shot per call: open socket, send n INSTREAM chunks
/// terminated with a 0-length frame, read response, close.
///
/// Why not the AspNetCore.ClamAVClient NuGet? Pulls a heavy dependency
/// graph for a 60-line socket protocol. Inline implementation keeps
/// the dependency surface tight.
/// </summary>
public interface IClamAvScanner
{
    Task<ClamAvResult> ScanStreamAsync(Stream content, CancellationToken ct = default);
}

public sealed record ClamAvResult(bool IsClean, string? ThreatName, string Raw);

public sealed class TcpClamAvScanner : IClamAvScanner
{
    // clamd default chunk size is 256 KiB. Keep room under StreamMaxLength
    // (clamd default 25 MiB) — bigger files split across many INSTREAM
    // frames; clamd reassembles internally.
    private const int ChunkSize = 256 * 1024;
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan ScanTimeout    = TimeSpan.FromMinutes(2);

    private readonly string _host;
    private readonly int    _port;
    private readonly ILogger<TcpClamAvScanner> _logger;

    public TcpClamAvScanner(IConfiguration cfg, ILogger<TcpClamAvScanner> logger)
    {
        _host = cfg["ClamAv:Host"] ?? "clamav";
        _port = cfg.GetValue<int?>("ClamAv:Port") ?? 3310;
        _logger = logger;
    }

    public async Task<ClamAvResult> ScanStreamAsync(Stream content, CancellationToken ct = default)
    {
        using var tcp = new TcpClient();
        using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        connectCts.CancelAfter(ConnectTimeout);
        await tcp.ConnectAsync(_host, _port, connectCts.Token);

        using var scanCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        scanCts.CancelAfter(ScanTimeout);

        await using var net = tcp.GetStream();

        // INSTREAM command framing: "zINSTREAM\0", then for each chunk
        // a 4-byte big-endian length prefix + chunk bytes, then a
        // length=0 terminator.
        await net.WriteAsync(Encoding.ASCII.GetBytes("zINSTREAM\0"), scanCts.Token);

        var buffer = new byte[ChunkSize];
        int read;
        while ((read = await content.ReadAsync(buffer, scanCts.Token)) > 0)
        {
            var lenBytes = BitConverter.GetBytes(read);
            if (BitConverter.IsLittleEndian) Array.Reverse(lenBytes);
            await net.WriteAsync(lenBytes, scanCts.Token);
            await net.WriteAsync(buffer.AsMemory(0, read), scanCts.Token);
        }
        // Zero-length terminator
        await net.WriteAsync(new byte[] { 0, 0, 0, 0 }, scanCts.Token);

        // Read response — typically 1 line, NUL-terminated:
        //   "stream: OK\0"
        //   "stream: Eicar-Test-Signature FOUND\0"
        var rsp = new byte[4096];
        var rspLen = await net.ReadAsync(rsp, scanCts.Token);
        var raw = Encoding.ASCII.GetString(rsp, 0, rspLen).TrimEnd('\0', '\n', '\r');
        if (raw.EndsWith("OK", StringComparison.Ordinal))
            return new ClamAvResult(true, null, raw);

        // Format on hit: "stream: <Threat> FOUND"
        string? threat = null;
        var foundIdx = raw.IndexOf(" FOUND", StringComparison.Ordinal);
        if (foundIdx > 0)
        {
            var startIdx = raw.IndexOf(": ", StringComparison.Ordinal);
            if (startIdx >= 0 && startIdx + 2 < foundIdx)
                threat = raw.Substring(startIdx + 2, foundIdx - startIdx - 2);
        }
        _logger.LogWarning("ClamAV detected threat: {Threat} (raw: {Raw})", threat, raw);
        return new ClamAvResult(false, threat ?? "UNKNOWN", raw);
    }
}

/// <summary>
/// No-op scanner used in dev / tests when clamd isn't reachable.
/// Reports every file as clean — DO NOT register this in production.
/// </summary>
public sealed class NullClamAvScanner : IClamAvScanner
{
    public Task<ClamAvResult> ScanStreamAsync(Stream content, CancellationToken ct = default)
        => Task.FromResult(new ClamAvResult(true, null, "scanner-disabled"));
}
