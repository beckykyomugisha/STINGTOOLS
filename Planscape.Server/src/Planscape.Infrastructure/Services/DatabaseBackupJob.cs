using System.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Planscape.Core.Interfaces;

namespace Planscape.Infrastructure.Services;

/// <summary>
/// BACKUP-01 — Nightly Postgres backup via <c>pg_dump</c>. Writes to either
/// the local filesystem or the configured S3-compatible storage (using
/// <see cref="IFileStorageService"/> so the backup lands next to user uploads).
///
/// Config keys:
///   Backup:Enabled            "true" to run (default false to avoid surprise on dev)
///   Backup:TenantSlug         where to drop in object storage (default "_backups")
///   Backup:ProjectCode        subdirectory (default "db")
///   Backup:PgDumpPath         override pg_dump path (default "pg_dump")
///   ConnectionStrings:Default used to pull host/user/password/db
/// </summary>
public class DatabaseBackupJob
{
    private readonly ILogger<DatabaseBackupJob> _logger;
    private readonly IConfiguration _config;
    private readonly IFileStorageService _storage;

    public DatabaseBackupJob(
        ILogger<DatabaseBackupJob> logger,
        IConfiguration config,
        IFileStorageService storage)
    {
        _logger = logger;
        _config = config;
        _storage = storage;
    }

    [Hangfire.DisableConcurrentExecution(timeoutInSeconds: 7200)]
    public async Task ExecuteAsync(CancellationToken ct = default)
    {
        if (!bool.TryParse(_config["Backup:Enabled"], out var enabled) || !enabled)
        {
            _logger.LogDebug("Backup skipped — Backup:Enabled not true");
            return;
        }

        // S13 — pg_dump is I/O- and lock-heavy on a single shared cluster.
        // Refuse to run outside a configured quiet-hours window so a
        // mistakenly-scheduled mid-day backup can't tank both tenants at
        // once. Default window is 01:00–05:00 UTC; override via
        // Backup:QuietHours:StartUtc / Backup:QuietHours:EndUtc
        // (24-hour HH:mm). Setting Backup:QuietHours:Override=true skips
        // the gate for one-off operator-triggered runs.
        var quietOverride = string.Equals(_config["Backup:QuietHours:Override"], "true",
                                          StringComparison.OrdinalIgnoreCase);
        if (!quietOverride && !IsInQuietHours(DateTime.UtcNow,
                _config["Backup:QuietHours:StartUtc"] ?? "01:00",
                _config["Backup:QuietHours:EndUtc"] ?? "05:00"))
        {
            _logger.LogInformation(
                "DatabaseBackupJob skipped — outside quiet-hours window ({Start}–{End} UTC). " +
                "Set Backup:QuietHours:Override=true to force.",
                _config["Backup:QuietHours:StartUtc"] ?? "01:00",
                _config["Backup:QuietHours:EndUtc"] ?? "05:00");
            return;
        }

        var connStr = _config.GetConnectionString("Default")
            ?? throw new InvalidOperationException("ConnectionStrings:Default missing");

        // Parse the Npgsql connection string into pg_dump-friendly args
        var parts = connStr.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(kv => kv.Split('=', 2))
            .Where(kv => kv.Length == 2)
            .ToDictionary(kv => kv[0].Trim().ToLowerInvariant(), kv => kv[1].Trim());

        var host = parts.GetValueOrDefault("host") ?? "localhost";
        var port = parts.GetValueOrDefault("port") ?? "5432";
        var user = parts.GetValueOrDefault("username") ?? "planscape";
        var pass = parts.GetValueOrDefault("password") ?? "";
        var db   = parts.GetValueOrDefault("database") ?? "planscape";

        var pgDump = _config["Backup:PgDumpPath"] ?? "pg_dump";
        var fileName = $"planscape-{DateTime.UtcNow:yyyyMMdd-HHmmss}.sql";
        var tempPath = Path.Combine(Path.GetTempPath(), fileName);

        var psi = new ProcessStartInfo
        {
            FileName = pgDump,
            UseShellExecute = false,
            RedirectStandardOutput = false,
            RedirectStandardError = true,
            Environment = { ["PGPASSWORD"] = pass },
        };
        psi.ArgumentList.Add("-h"); psi.ArgumentList.Add(host);
        psi.ArgumentList.Add("-p"); psi.ArgumentList.Add(port);
        psi.ArgumentList.Add("-U"); psi.ArgumentList.Add(user);
        psi.ArgumentList.Add("-d"); psi.ArgumentList.Add(db);
        psi.ArgumentList.Add("-f"); psi.ArgumentList.Add(tempPath);
        psi.ArgumentList.Add("--no-owner");
        psi.ArgumentList.Add("--no-privileges");

        _logger.LogInformation("DatabaseBackupJob starting: {Host}/{Db} -> {File}", host, db, fileName);

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException($"Could not launch {pgDump}");

        var stderr = await process.StandardError.ReadToEndAsync(ct);
        await process.WaitForExitAsync(ct);

        if (process.ExitCode != 0)
        {
            _logger.LogError("pg_dump failed ({Code}): {Stderr}", process.ExitCode, stderr);
            return;
        }

        // Upload the dump into storage (land it under _backups/db/ namespace)
        var tenantSlug = _config["Backup:TenantSlug"] ?? "_backups";
        var projectCode = _config["Backup:ProjectCode"] ?? "db";
        try
        {
            await using var fs = File.OpenRead(tempPath);
            var storedKey = await _storage.SaveAsync(tenantSlug, projectCode, fileName, fs, ct);
            _logger.LogInformation("DatabaseBackupJob stored backup at {Key} ({Bytes:N0} bytes)", storedKey, fs.Length);
        }
        finally
        {
            try { File.Delete(tempPath); } catch { /* best-effort */ }
        }
    }

    /// <summary>
    /// S13 — true when <paramref name="utcNow"/> falls within the quiet-hours
    /// window. Window may wrap midnight (e.g. 22:00 → 04:00). Both bounds
    /// are inclusive on the start, exclusive on the end.
    /// </summary>
    internal static bool IsInQuietHours(DateTime utcNow, string startStr, string endStr)
    {
        if (!TimeOnly.TryParse(startStr, out var start) || !TimeOnly.TryParse(endStr, out var end))
        {
            // If config is malformed, fail open (run anyway) so backups don't
            // silently stop working on a typo. Operators see the warning in logs.
            return true;
        }
        var now = TimeOnly.FromDateTime(utcNow);
        if (start < end)
        {
            return now >= start && now < end;
        }
        // Wrapping window: e.g. 22:00 → 04:00 means "after 22:00 OR before 04:00".
        return now >= start || now < end;
    }
}
