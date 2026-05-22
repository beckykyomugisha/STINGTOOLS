// MergeRecoveryStubs.cs
//
// Minimal stub types that the 196-branch consolidation referenced but
// whose backing implementations did not make it into the merged tree
// (the originating feature branches were squashed or rebased before
// the merge ran). The stubs let the assembly compile so the rest of
// the codebase is testable; runtime behaviour for the dependent
// features (Symbol library per-standard variants, Plumbing P-trap
// flag-based route filtering, Placement-center async run requests,
// Site-photo album/checklist browser) is non-functional until the
// real implementations are restored from their source branches.
//
// Search the codebase for "MergeRecoveryStubs" to find every consumer
// that needs a real implementation.

using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;

namespace StingTools.Core.Symbols
{
    /// <summary>Stub — per-standard geometry/parameter override carrier. See <see cref="SymbolDefinition"/> for the fields shadowed.</summary>
    public sealed class StandardGeometryOverride
    {
        public double? SymbolSize { get; set; }
        public List<ParameterDefinition> Parameters { get; set; }
        public string ParameterMode { get; set; }
        public SymbolGeometry Geometry { get; set; }
        public List<ConnectorDefinition> Connectors { get; set; }
        public Solid3DDefinition Solid3D { get; set; }
    }

    /// <summary>Stub — categorises Revit .rft templates by what they can host.</summary>
    public enum TemplateKind
    {
        Unknown,
        Model,
        DetailItem,
        Annotation,
    }
}

namespace StingTools.Commands.Plumbing
{
    /// <summary>Stub — flag-based P-trap auto-route filter options. Populated from the Plumbing dock panel.</summary>
    public sealed class RoutePTrapOptions
    {
        public bool   IncludeWc     { get; set; } = true;
        public bool   IncludeBasin  { get; set; } = true;
        public bool   IncludeShower { get; set; } = true;
        public bool   IncludeBath   { get; set; } = true;
        public bool   IncludeSink   { get; set; } = true;
        public bool   IncludeGully  { get; set; } = true;
        public bool   IncludeFloor  { get; set; } = true;
        public double MaxRadiusMm   { get; set; } = 0;
        public double MinOdMm       { get; set; } = 0;
    }

    /// <summary>Extension stub so `panel?.ReadRoutePTrapOptions()` compiles. Returns defaults.</summary>
    internal static class PlumbingPanelStubExt
    {
        public static RoutePTrapOptions ReadRoutePTrapOptions(this StingTools.UI.Plumbing.StingPlumbingPanel _)
            => new RoutePTrapOptions();
    }
}

namespace StingTools.UI.PlacementCenter
{
    /// <summary>Stub — async placement-run request payload. Captured at click time and consumed by the run worker.</summary>
    public sealed class PlacementRunRequest
    {
        public Document Doc { get; set; }
        public IReadOnlyList<ElementId> RoomIds { get; set; }
        public object Rules { get; set; }
        public object Progress { get; set; }
        public System.Threading.CancellationTokenSource HeartbeatCts { get; set; }
        public DateTime StartUtc { get; set; }
        public object PrevStamp { get; set; }
        public object PrevLearn { get; set; }
    }
}

namespace StingTools.UI
{
    /// <summary>Stub — Site Photos album DTO surfaced by the BCC Review tab. Mirrors the server contract.</summary>
    public sealed class PhotoAlbumDto
    {
        public Guid   Id          { get; set; }
        public string Name        { get; set; } = "";
        public string Description { get; set; } = "";
        public string Visibility  { get; set; } = "Project";
        public string Kind        { get; set; }
        public int    PhotoCount  { get; set; }
        public bool   IsLocked    { get; set; }
    }

    /// <summary>Stub — Site Photos checklist DTO surfaced by the BCC Review tab. Mirrors the server contract.</summary>
    public sealed class PhotoChecklistDto
    {
        public Guid     Id        { get; set; }
        public string   Name      { get; set; } = "";
        public string   Status    { get; set; } = "Open";
        public string   Kind      { get; set; }
        public string   LevelCode { get; set; }
        public string   ZoneCode  { get; set; }
        public DateTime? DueAt    { get; set; }
        public DateTime CreatedAt { get; set; }
        public int      Total     { get; set; }
        public int      Done      { get; set; }
    }
}
