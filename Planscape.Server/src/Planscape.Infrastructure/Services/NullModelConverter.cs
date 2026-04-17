using Microsoft.Extensions.Logging;
using Planscape.Core.Interfaces;

namespace Planscape.Infrastructure.Services;

/// <summary>
/// P7 — Fallback converter. Returns "not implemented" so IFC uploads end up
/// in storage with a "conversion pending" banner instead of crashing the
/// viewer. Swap out by registering <see cref="IfcConvertConverter"/> or
/// <see cref="ApsModelDerivativeConverter"/> in Program.cs.
/// </summary>
public class NullModelConverter : IModelConverter
{
    public string ProviderName => "null";

    public Task<ConversionResult> ConvertToGlbAsync(string inputPath, string outputPath, CancellationToken ct = default)
        => Task.FromResult(new ConversionResult(
            Success: false,
            ProviderName: ProviderName,
            ElapsedMs: 0,
            OutputSizeBytes: 0,
            ElementCount: null,
            Error: "No IFC→glTF converter configured. Register IfcConvertConverter or ApsModelDerivativeConverter."));
}

/// <summary>
/// P7 — `IfcConvert` CLI wrapper. Expects `IfcConvert` on the PATH or at the
/// path configured by <c>ModelConverter:IfcConvertPath</c>. Producer ships a
/// docker-compose service for this — see
/// <c>Planscape.Server/docker/docker-compose.yml</c>.
/// </summary>
public class IfcConvertConverter : IModelConverter
{
    private readonly ILogger<IfcConvertConverter> _logger;
    private readonly string _binaryPath;

    public string ProviderName => "ifcconvert";

    public IfcConvertConverter(ILogger<IfcConvertConverter> logger,
        Microsoft.Extensions.Configuration.IConfiguration config)
    {
        _logger = logger;
        _binaryPath = config["ModelConverter:IfcConvertPath"] ?? "IfcConvert";
    }

    public async Task<ConversionResult> ConvertToGlbAsync(string inputPath, string outputPath, CancellationToken ct = default)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = _binaryPath,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            // `--use-element-guids` keeps the Revit UniqueId in userData.extras.guid
            // so the mobile viewer's element map still lines up.
            psi.ArgumentList.Add("--use-element-guids");
            psi.ArgumentList.Add(inputPath);
            psi.ArgumentList.Add(outputPath);

            using var proc = System.Diagnostics.Process.Start(psi)
                             ?? throw new InvalidOperationException("Failed to start IfcConvert");
            await proc.WaitForExitAsync(ct);
            sw.Stop();

            if (proc.ExitCode != 0)
            {
                var err = await proc.StandardError.ReadToEndAsync(ct);
                _logger.LogWarning("IfcConvert failed (exit {Code}): {Err}", proc.ExitCode, err);
                return new ConversionResult(false, ProviderName, sw.ElapsedMilliseconds, 0, null, err);
            }

            var size = new FileInfo(outputPath).Length;
            return new ConversionResult(true, ProviderName, sw.ElapsedMilliseconds, size, null, null);
        }
        catch (Exception ex)
        {
            sw.Stop();
            _logger.LogError(ex, "IfcConvert crashed");
            return new ConversionResult(false, ProviderName, sw.ElapsedMilliseconds, 0, null, ex.Message);
        }
    }
}

/// <summary>
/// P8 — Fallback thumbnail generator. No-ops; mobile list falls back to an
/// emoji when no thumbnail is present. Register a real implementation
/// (three.js headless microservice) when ready.
/// </summary>
public class NullThumbnailGenerator : IModelThumbnailGenerator
{
    public string ProviderName => "null";

    public Task<ThumbnailResult> GenerateAsync(string modelPath, string outputPngPath, CancellationToken ct = default)
        => Task.FromResult(new ThumbnailResult(false, ProviderName, 0, "No thumbnail generator configured."));
}
