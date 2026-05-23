using System.Text.Json.Serialization;

namespace Planscape.Core.DTOs;

/// <summary>
/// I-2 — Material library snapshot payload from the Revit plugin.
/// One snapshot per project per user — overwrites on push, no
/// per-row history (audit log carries the diff history).
/// </summary>
public class MaterialSyncRequest
{
    public Guid ProjectId { get; set; }
    public string? RevitDocPath { get; set; }
    public DateTime CapturedUtc { get; set; } = DateTime.UtcNow;
    public List<MaterialSyncRow> Materials { get; set; } = new();
}

public class MaterialSyncRow
{
    public string Name { get; set; } = "";
    public string Class { get; set; } = "";
    public string Origin { get; set; } = "";
    public string ColorRgb { get; set; } = "";
    public int UsageCount { get; set; }
    public double Cost { get; set; }
    public double CarbonKgCo2e { get; set; }
    public string EpdSource { get; set; } = "";
    public string EpdDate { get; set; } = "";
    public string UniclassCode { get; set; } = "";
}

public class MaterialSyncResponse
{
    public int RowsAccepted { get; set; }
    public DateTime ReceivedUtc { get; set; } = DateTime.UtcNow;
    public string? Notes { get; set; }
}
