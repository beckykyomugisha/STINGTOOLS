namespace Planscape.Core.Interfaces;

using Planscape.Core.Entities;

public interface IIfcAlignmentValidator
{
    /// <summary>
    /// Inspect the IFC file header for georeferencing data (project units,
    /// true north, survey point, IfcMapConversion). Compare against the
    /// project's reference model (first uploaded, or a designated 'survey
    /// of record' model). Returns a structured validation report with
    /// actionable fix hints for ArchiCAD/Revit users.
    /// </summary>
    Task<IfcAlignmentReport> ValidateAsync(
        string ifcPath,
        Guid projectId,
        Guid projectModelId,
        Guid tenantId,
        CancellationToken ct);
}
