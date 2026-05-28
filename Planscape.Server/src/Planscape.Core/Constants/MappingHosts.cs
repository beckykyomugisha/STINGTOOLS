namespace Planscape.Core.Constants;

/// <summary>
/// K1 keystone — the single source of truth for cross-host identifiers used
/// by <see cref="Planscape.Core.Entities.ExternalElementMapping"/>. Every
/// surface (IFC ingest, identity resolver, IoT binding) validates and
/// normalises host strings through here instead of carrying its own literal
/// set, so adding a host (e.g. "iot") is a one-line change.
/// </summary>
public static class MappingHosts
{
    public const string Revit    = "revit";
    public const string Blender  = "blender";
    public const string ArchiCad = "archicad";
    public const string Tekla    = "tekla";
    public const string Headless = "headless";

    /// <summary>
    /// IoT / digital-twin devices bind to model elements through the same
    /// cross-host table: an ExternalElementMapping row with Host="iot" and
    /// HostElementId = the device id. This is what lets a telemetry feed
    /// resolve to the very IFC GlobalId the viewer and Revit already use —
    /// no separate device↔element identity system.
    /// </summary>
    public const string Iot = "iot";

    public static readonly IReadOnlySet<string> All =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            Revit, Blender, ArchiCad, Tekla, Headless, Iot,
        };

    public static string Normalize(string? host) => (host ?? "").Trim().ToLowerInvariant();

    public static bool IsValid(string? host) => All.Contains(Normalize(host));
}
