using Microsoft.Extensions.Logging;
using Planscape.Core.Interfaces;

namespace Planscape.Infrastructure.Services;

/// <summary>
/// P7 — the "aps" <see cref="IModelConverter"/> provider. It REFUSES, by design.
///
/// <see cref="IModelConverter.ConvertToGlbAsync"/> promises a glTF/GLB at the
/// output path. Autodesk Model Derivative cannot produce one: its 3D outputs are
/// SVF/SVF2 (plus OBJ/STL/STEP/IGES/IFC/DWG), never glTF/GLB. There is no request
/// to APS that fulfils this interface.
///
/// The previous implementation uploaded nothing (it minted a deterministic fake
/// object URN from the file name), submitted a real — billable — translation job
/// against that fake URN, and on the rare path that got that far wrote SVF bytes
/// to a path named *.glb before reporting failure. None of that is kept.
///
/// Selecting <c>ModelConverter:Provider = aps</c> now logs an error at startup
/// (Program.cs) and every conversion fails immediately with the reason, spending
/// no APS credits. Use <c>ifcconvert</c> for IFC → GLB. If an APS path is wanted,
/// it needs a different contract (SVF2 + the APS Viewer, or SVF → glTF via a
/// separate converter) — a new interface, not this one.
/// </summary>
public class ApsModelDerivativeConverter : IModelConverter
{
    public const string RefusalMessage =
        "ModelConverter:Provider=aps cannot produce GLB: Autodesk Model Derivative outputs SVF/SVF2, not glTF/GLB. " +
        "Set ModelConverter:Provider=ifcconvert for IFC→GLB.";

    private readonly ILogger<ApsModelDerivativeConverter> _logger;

    public string ProviderName => "aps";

    public ApsModelDerivativeConverter(ILogger<ApsModelDerivativeConverter> logger) => _logger = logger;

    public Task<ConversionResult> ConvertToGlbAsync(string inputPath, string outputPath, CancellationToken ct = default)
    {
        _logger.LogError("ApsModelDerivativeConverter refused {File}: {Reason}", Path.GetFileName(inputPath), RefusalMessage);
        return Task.FromResult(new ConversionResult(false, ProviderName, 0, 0, null, RefusalMessage));
    }
}
