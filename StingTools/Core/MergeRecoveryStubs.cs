// MergeRecoveryStubs.cs
//
// Minimal stub types + partial-class additions that the 196-branch
// consolidation referenced but whose backing implementations did not
// make it into the merged tree (originating branches were squashed
// or rebased before the bulk merge ran). The stubs let the assembly
// compile so the rest of the codebase is testable; runtime behaviour
// for the dependent features is non-functional until the real
// implementations are restored from their source branches.
//
// Grep "MergeRecoveryStubs" to find every consumer that needs a real impl.

#nullable enable annotations
#nullable disable warnings

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Autodesk.Revit.DB;

// ──────────────────────────────────────────────────────────────────────
// 1. Symbol library stubs
// ──────────────────────────────────────────────────────────────────────
namespace StingTools.Core.Symbols
{
    /// <summary>Stub — per-standard geometry/parameter override carrier.</summary>
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
    public enum TemplateKind { Unknown, Model, DetailItem, Annotation }
}

// ──────────────────────────────────────────────────────────────────────
// 2. Plumbing — options DTOs + panel extension methods
// ──────────────────────────────────────────────────────────────────────
namespace StingTools.Commands.Plumbing
{
    public sealed class RoutePTrapOptions
    {
        public bool   IncludeWc     { get; set; } = true;
        public bool   IncludeBasin  { get; set; } = true;
        public bool   IncludeShower { get; set; } = true;
        public bool   IncludeBath   { get; set; } = true;
        public bool   IncludeSink   { get; set; } = true;
        public bool   IncludeGully  { get; set; } = true;
        public bool   IncludeFloor  { get; set; } = true;
        public double MaxRadiusMm   { get; set; }
        public double MinOdMm       { get; set; }
        public string Scope         { get; set; } = "Project";
    }

    public sealed class RouteHangerOptions { public string Scope { get; set; } = "Project"; public string RodSize { get; set; } = "M8"; }
    public sealed class RouteSleeveOptions { public string Scope { get; set; } = "Project"; public double MinOdMm { get; set; } }
    public sealed class SpecialtyOptions
    {
        public string Scope       { get; set; } = "Project";
        public bool   MatAll      { get; set; } = true;
        public bool   MatGalvanic { get; set; }
        public bool   MatJointing { get; set; }
        public bool   MatWras     { get; set; }
    }

    // Row DTOs surfaced by the Plumbing panel — superset of every property
    // the merged code touches. Defaults are sentinel-safe (string="", num=0).
    public sealed class DrainageSlopeRow      { public string Name { get; set; } public string Status { get; set; } public bool Apply { get; set; } public string Pipe { get; set; } public double DElevMm { get; set; } }
    public sealed class DrainageSizingRow     { public string Name { get; set; } public string Status { get; set; } public string Pipe { get; set; } public double SigmaDu { get; set; } public int Dn { get; set; } public double VelocityMps { get; set; } public double HdRatio { get; set; } }
    public sealed class DrainageVentRow       { public string Name { get; set; } public string Status { get; set; } public string Drain { get; set; } public double Du { get; set; } public int VentDn { get; set; } public double MaxLenM { get; set; } public string Flag { get; set; } }
    public sealed class DrainageInvertRow     { public string Name { get; set; } public string Status { get; set; } public string Pipe { get; set; } public double UsInvM { get; set; } public double DsInvM { get; set; } public double CoverM { get; set; } }
    public sealed class DrainageDuScanRow     { public string Name { get; set; } public string Status { get; set; } public string Fixture { get; set; } public int Count { get; set; } public double DuEach { get; set; } public double SigmaDu { get; set; } }
    public sealed class SupplyTmvRow          { public string Name { get; set; } public string Status { get; set; } public string Fixture { get; set; } public string Type { get; set; } public bool Pass { get; set; } }
    public sealed class SupplySizingRow       { public string Name { get; set; } public string Status { get; set; } public string Section { get; set; } public double SigmaLu { get; set; } public int Dn { get; set; } public double VelocityMps { get; set; } }
    public sealed class SupplyFixtureScanRow  { public string Name { get; set; } public string Status { get; set; } public string Fixture { get; set; } public int Count { get; set; } public double LuCw { get; set; } public double LuHw { get; set; } }
    public sealed class SpecialtyCrossConnRow { public string Name { get; set; } public string Status { get; set; } public string SystemA { get; set; } public string SystemB { get; set; } public string Separation { get; set; } public string Risk { get; set; } }
    public sealed class DocsManholeRow        { public string Name { get; set; } public string Status { get; set; } public string Ref { get; set; } public double InvInM { get; set; } public double InvOutM { get; set; } public double CoverM { get; set; } public double DepthM { get; set; } }
    public sealed class DocsBoqRow            { public string Name { get; set; } public string Status { get; set; } public string Item { get; set; } public string Description { get; set; } public double Qty { get; set; } public string Unit { get; set; } }
    public sealed class DocsPipeScheduleRow   { public string Name { get; set; } public string Status { get; set; } public string System { get; set; } public int Dn { get; set; } public string Material { get; set; } public double LengthM { get; set; } }
    public sealed class AuditIssueRow         { public string Code { get; set; } public string Detail { get; set; } public string Severity { get; set; } = "Info"; public string Element { get; set; } public string Issue { get; set; } }

    // PlumbStormInputs lives at StingTools.UI.Plumbing.StingPlumbingPanel.cs alongside
    // the panel; merge-recovery added the Rwh*/Soak*/Suds*/Septic* fields there.

    /// <summary>
    /// Extension methods on the Plumbing dock panel so call sites
    /// `panel?.ReadXxx()` / `panel?.SetXxx(...)` compile. All real
    /// implementations were lost to the merge; returns sensible defaults.
    /// </summary>
    internal static class PlumbingPanelStubExt
    {
        // Read* options
        public static RoutePTrapOptions   ReadRoutePTrapOptions     (this StingTools.UI.Plumbing.StingPlumbingPanel _) => new RoutePTrapOptions();
        public static RouteHangerOptions  ReadRouteHangerOptions    (this StingTools.UI.Plumbing.StingPlumbingPanel _) => new RouteHangerOptions();
        public static RouteSleeveOptions  ReadRouteSleeveOptions    (this StingTools.UI.Plumbing.StingPlumbingPanel _) => new RouteSleeveOptions();
        public static SpecialtyOptions    ReadSpecialtyOptions      (this StingTools.UI.Plumbing.StingPlumbingPanel _) => new SpecialtyOptions();
        public static string              ReadDrainageSlopeScope    (this StingTools.UI.Plumbing.StingPlumbingPanel _) => "Project";
        public static bool                ReadDrainageAutoSizeApply (this StingTools.UI.Plumbing.StingPlumbingPanel _) => false;

        // Set* result sinks
        public static void SetDrainageSlopeResult   (this StingTools.UI.Plumbing.StingPlumbingPanel _, List<DrainageSlopeRow> rows, string status = "") { }
        public static void SetDrainageSizingResult  (this StingTools.UI.Plumbing.StingPlumbingPanel _, List<DrainageSizingRow> rows, string status = "") { }
        public static void SetDrainageVentResult    (this StingTools.UI.Plumbing.StingPlumbingPanel _, List<DrainageVentRow> rows, string status = "") { }
        public static void SetDrainageDuScanResult  (this StingTools.UI.Plumbing.StingPlumbingPanel _, List<DrainageDuScanRow> rows, string status = "") { }
        public static void SetSupplySizingResult    (this StingTools.UI.Plumbing.StingPlumbingPanel _, List<SupplySizingRow> rows, string status = "") { }
        public static void SetSupplyFixtureScanResult(this StingTools.UI.Plumbing.StingPlumbingPanel _, List<SupplyFixtureScanRow> rows, string status = "") { }
        public static void SetSpecialtyCrossConnResult(this StingTools.UI.Plumbing.StingPlumbingPanel _, List<SpecialtyCrossConnRow> rows, string status = "") { }
        public static void SetStormSepticResult     (this StingTools.UI.Plumbing.StingPlumbingPanel _, string line = "", string status = "") { }
        public static void SetStormSoakResult       (this StingTools.UI.Plumbing.StingPlumbingPanel _, string line = "", string status = "") { }
        public static void SetStormSudsResult       (this StingTools.UI.Plumbing.StingPlumbingPanel _, string line = "", string status = "") { }
        public static void SetStormRwhResult        (this StingTools.UI.Plumbing.StingPlumbingPanel _, string line = "", string status = "") { }
        public static void SetStormRoofResult       (this StingTools.UI.Plumbing.StingPlumbingPanel _, string line = "", string status = "") { }
        // SetAuditRag overloads — callers pass either bare rag, (string, double, int), or (string, string, string).
        public static void SetAuditRag              (this StingTools.UI.Plumbing.StingPlumbingPanel _, string rag) { }
        public static void SetAuditRag              (this StingTools.UI.Plumbing.StingPlumbingPanel _, string rag, double pct, int issues) { }
        public static void SetAuditRag              (this StingTools.UI.Plumbing.StingPlumbingPanel _, string rag, string a, string b) { }
        public static void SetAuditFindings         (this StingTools.UI.Plumbing.StingPlumbingPanel _, List<AuditIssueRow> rows) { }
        public static void SetAuditFindings         (this StingTools.UI.Plumbing.StingPlumbingPanel _, string domain, List<AuditIssueRow> rows) { }
        public static void SetDocsManholeResult     (this StingTools.UI.Plumbing.StingPlumbingPanel _, List<DocsManholeRow> rows, string status = "") { }
        public static void SetDocsPipeScheduleResult(this StingTools.UI.Plumbing.StingPlumbingPanel _, List<DocsPipeScheduleRow> rows, string status = "") { }
        public static void SetDocsBoqResult         (this StingTools.UI.Plumbing.StingPlumbingPanel _, List<DocsBoqRow> rows, string status = "") { }
        public static void SetDrainageInvertResult  (this StingTools.UI.Plumbing.StingPlumbingPanel _, List<DrainageInvertRow> rows, string status = "") { }
        public static RoutePTrapOptions ReadRouteAutoOptions(this StingTools.UI.Plumbing.StingPlumbingPanel _) => new RoutePTrapOptions();
    }
}

// ──────────────────────────────────────────────────────────────────────
// 3. PlacementCenter — async run request
// ──────────────────────────────────────────────────────────────────────
namespace StingTools.UI.PlacementCenter
{
    public sealed class PlacementRunRequest
    {
        public Document Doc { get; set; }
        public IReadOnlyList<ElementId> RoomIds { get; set; }
        public List<StingTools.Core.Placement.PlacementRule> Rules { get; set; }
        public StingTools.UI.StingProgressDialog Progress { get; set; }
        public System.Threading.CancellationTokenSource HeartbeatCts { get; set; }
        public DateTime StartUtc { get; set; }
        public bool PrevStamp { get; set; }
        public bool PrevLearn { get; set; }
    }

    /// <summary>Stub handler — wires up the modeless async-run pattern but does no work.</summary>
    public sealed class PlacementRunHandler : Autodesk.Revit.UI.IExternalEventHandler
    {
        public PlacementRunHandler() { }
        public PlacementRunHandler(StingPlacementCenter owner) { }
        public void Execute(Autodesk.Revit.UI.UIApplication app) { }
        public string GetName() => "PlacementRunHandler";
    }
}

// ──────────────────────────────────────────────────────────────────────
// 4. Site Photos DTOs
// ──────────────────────────────────────────────────────────────────────
namespace StingTools.UI
{
    public sealed class PhotoAlbumDto
    {
        public Guid   Id          { get; set; }
        public string Name        { get; set; } = "";
        public string Description { get; set; } = "";
        public string Visibility  { get; set; } = "Project";
        public string Kind        { get; set; }
        public int    PhotoCount  { get; set; }
        public bool   IsLocked    { get; set; }
        public List<PhotoAlbumEntryDto> Photos { get; set; } = new();
    }

    /// <summary>Stub — single photo entry inside an album.</summary>
    public sealed class PhotoAlbumEntryDto
    {
        public Guid PhotoId { get; set; }
    }

    /// <summary>Stub — Site Photos NDA policy payload.</summary>
    public sealed class PhotoPolicyDto
    {
        public string NdaText  { get; set; }
        public string NdaSha   { get; set; }
        public bool   Required { get; set; }
    }

    /// <summary>Stub — share-link response.</summary>
    public sealed class PhotoShareLinkDto
    {
        public string   Token         { get; set; } = "";
        public DateTime ExpiresAt     { get; set; }
        public bool     ForceRedacted { get; set; }
    }

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

// ──────────────────────────────────────────────────────────────────────
// 5. ThemeManager — FallbackTheme stub
// ──────────────────────────────────────────────────────────────────────
namespace StingTools.UI
{
    public static partial class ThemeManager
    {
        /// <summary>Stub — fallback theme name when the active theme can't be resolved.</summary>
        public static string FallbackTheme { get; } = "Light";
    }
}

// ──────────────────────────────────────────────────────────────────────
// 6. ViewStylePack — missing properties referenced by registry/editor/applier
// ──────────────────────────────────────────────────────────────────────
namespace StingTools.Core.Drawing
{
    public sealed partial class ViewStylePack
    {
        // FarClipMm / AnnotationCrop / ViewRange are already declared on the
        // runtime class — those are bool?/double?/PackViewRange. Only the
        // missing surface is declared here.
        public string ViewTemplate     { get; set; }
        public string DetailLevel      { get; set; }
        public string ScaleHint        { get; set; }
        public string ColorScheme      { get; set; }
        public PackAppearanceDto Appearance { get; set; }
        public string PhaseName        { get; set; }
        public Dictionary<string, ByMaterialClassOverride> ByMaterialClass  { get; set; }
    }

    /// <summary>Stub — pack-level appearance settings referenced by ViewStylePack.
    /// Field names match the corporate STING_VIEW_STYLE_PACKS.json "appearance"
    /// object so it round-trips; PromoteAppearance flattens these onto the pack.</summary>
    public sealed class PackAppearanceDto
    {
        [Newtonsoft.Json.JsonProperty("lineWeightScale")]   public double? LineWeightScale    { get; set; }
        [Newtonsoft.Json.JsonProperty("textStyleName")]     public string  TextStyleName      { get; set; }
        [Newtonsoft.Json.JsonProperty("dimensionStyleName")] public string DimensionStyleName { get; set; }
        [Newtonsoft.Json.JsonProperty("hatchPalette")]      public string  HatchPalette       { get; set; }

        // Optional graphic-override fields (kept for editor round-trip; not in baseline JSON).
        public string FillPattern      { get; set; }
        public string LinePattern      { get; set; }
        public int?   ProjLineWeight   { get; set; }
        public int?   CutLineWeight    { get; set; }
        public string ProjColor        { get; set; }
        public string CutColor         { get; set; }
        public int?   Transparency     { get; set; }
        public bool?  Halftone         { get; set; }
    }

    /// <summary>Stub — per-material-class graphic override used by ApplyMaterialClassOverrides.</summary>
    public sealed class ByMaterialClassOverride
    {
        public bool?  Halftone             { get; set; }
        public int?   Transparency         { get; set; }
        public int?   ProjectionLineWeight { get; set; }
        public int?   CutLineWeight        { get; set; }
        public string ProjectionLineColor  { get; set; }
        public string CutLineColor         { get; set; }
    }
}

// ──────────────────────────────────────────────────────────────────────
// 7. PlanscapeServerClient — stub async methods (return null/false)
// ──────────────────────────────────────────────────────────────────────
namespace StingTools.BIMManager
{
    public sealed partial class PlanscapeServerClient
    {
        // HVAC publish — snapshot caller uses .HasValue (bool? result), bulk/nc
        // callers do `bool ok = await ...` (plain bool result).
        public Task<bool?> PushHvacSnapshotAsync(Guid projectId, object payload) => Task.FromResult<bool?>(false);
        public Task<bool>  PushHvacLoadsBulkAsync(Guid projectId, object payload) => Task.FromResult(false);
        public Task<bool>  PushHvacNcAsync(Guid projectId, object payload) => Task.FromResult(false);

        // Model registry — ComputeSha256 must be static per call sites in PublishModelCommand.
        public static string ComputeSha256(string s) => "";
        public Task<Guid?> FindModelByHashAsync(Guid projectId, string sha256) => Task.FromResult<Guid?>(null);
        public Task<(bool ok, string error)> RefreshModelMetadataAsync(Guid projectId, Guid modelId,
            string? elementMapPath = null, string? name = null, string? discipline = null,
            string? revision = null, int elementCount = 0) => Task.FromResult((true, ""));
        public Task<(bool ok, string error)> DeleteModelAsync(Guid projectId, Guid modelId) => Task.FromResult((true, ""));

        // Site Photos — NDA / policy
        public Task<StingTools.UI.PhotoPolicyDto?> GetPhotoPolicyAsync(Guid projectId) => Task.FromResult<StingTools.UI.PhotoPolicyDto?>(null);
        public Task<bool>     AcceptPhotoNdaAsync(Guid projectId, string? ndaSha = null) => Task.FromResult(false);
        public Task<bool>     AcceptPhotoNdaAsync(Guid projectId, Guid photoId) => Task.FromResult(false);
        public HashSet<Guid>  LastNdaRequiredIds { get; set; } = new();

        // Site Photos — checklists / albums / distribution
        public Task<List<StingTools.UI.PhotoChecklistDto>?> ListPhotoChecklistsAsync(Guid projectId, string? status = null) => Task.FromResult<List<StingTools.UI.PhotoChecklistDto>?>(new List<StingTools.UI.PhotoChecklistDto>());
        public Task<List<StingTools.UI.PhotoAlbumDto>?>     ListPhotoAlbumsAsync(Guid projectId) => Task.FromResult<List<StingTools.UI.PhotoAlbumDto>?>(new List<StingTools.UI.PhotoAlbumDto>());
        public Task<StingTools.UI.PhotoAlbumDto?>           GetPhotoAlbumAsync(Guid projectId, Guid albumId) => Task.FromResult<StingTools.UI.PhotoAlbumDto?>(null);
        public Task<StingTools.UI.PhotoAlbumDto?>           CreatePhotoAlbumAsync(Guid projectId, string name, string? description = null, string visibility = "Project") => Task.FromResult<StingTools.UI.PhotoAlbumDto?>(null);
        public Task<bool> AddPhotosToAlbumAsync(Guid projectId, Guid albumId, IEnumerable<Guid> photoIds) => Task.FromResult(false);
        public Task<bool> LockPhotoAlbumAsync(Guid projectId, Guid albumId, bool locked) => Task.FromResult(false);
        public Task<StingTools.UI.PhotoShareLinkDto?> CreatePhotoShareLinkAsync(Guid projectId, Guid albumId, TimeSpan? expiry = null, string? label = null) => Task.FromResult<StingTools.UI.PhotoShareLinkDto?>(null);
        public Task<string?> ExportPhotosAsync(Guid projectId, string outputPath, Guid? albumId = null, string format = "zip") => Task.FromResult<string?>(null);
        public Task<bool>    ExportPhotosAsync(Guid projectId, IEnumerable<Guid>? photoIds = null, string format = "zip") => Task.FromResult(false);

        // Site Photos — admin bulk — return the count of affected photos
        // (callers do `n > 0` on the result).
        public Task<int> BulkReclassifyPhotosAsync(Guid projectId, IEnumerable<Guid> photoIds, string newClass) => Task.FromResult(0);
        public Task<int> BulkReanchorPhotosAsync(Guid projectId, IEnumerable<Guid> photoIds, object? payload = null, string? levelCode = null, string? zoneCode = null) => Task.FromResult(0);
        public Task<List<DistributionGroupDto>?> ListDistributionGroupsAsync(Guid projectId) => Task.FromResult<List<DistributionGroupDto>?>(new List<DistributionGroupDto>());
        public Task<bool> CreateDistributionGroupAsync(Guid projectId, string name, IEnumerable<string>? recipients = null, string? kind = null) => Task.FromResult(false);
    }

    /// <summary>Stub — distribution group DTO mirroring the server contract.</summary>
    public sealed class DistributionGroupDto
    {
        public Guid   Id   { get; set; }
        public string Name { get; set; } = "";
        public string Kind { get; set; }
        public int    MemberCount { get; set; }
        public bool   IncludeInDailyDigest { get; set; }
        public bool   ForceRedacted { get; set; }
    }
}

// ──────────────────────────────────────────────────────────────────────
// 8. Missing IExternalCommand classes in StingTools.Commands.Symbols
// ──────────────────────────────────────────────────────────────────────
namespace StingTools.Commands.Symbols
{
    using Autodesk.Revit.Attributes;
    using Autodesk.Revit.UI;
    using Result = Autodesk.Revit.UI.Result;

    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class SymbolsAutoPlaceToggleCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData d, ref string m, ElementSet e) => Result.Succeeded;
    }

    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class RemoveSymbolsInViewCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData d, ref string m, ElementSet e) => Result.Succeeded;
    }

    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class RemoveSymbolsProjectWideCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData d, ref string m, ElementSet e) => Result.Succeeded;
    }
}

// ──────────────────────────────────────────────────────────────────────
// 9. StingToolsApp — missing static fields + EnsureStingRibbonTab
// ──────────────────────────────────────────────────────────────────────
namespace StingTools.Core
{
    public partial class StingToolsApp
    {
        // Briefing — referenced by various command handlers; populated by
        // morning-briefing flow that didn't make the merge. Default null/false.
        internal static Autodesk.Revit.DB.Document _pendingBriefingDoc;
        internal static bool _briefingSubscribed;
        internal static bool _briefingPending;

        /// <summary>Stub — the real ribbon-tab initialiser lives in EnsureRibbonTab; this alias keeps merged call sites compiling.</summary>
        internal static void EnsureStingRibbonTab(Autodesk.Revit.UI.UIControlledApplication application) => EnsureRibbonTab(application);
    }
}

// ──────────────────────────────────────────────────────────────────────
// 10. StingPlacementCenter — missing run-handler / run-event fields + helpers
// ──────────────────────────────────────────────────────────────────────
namespace StingTools.UI.PlacementCenter
{
    public partial class StingPlacementCenter
    {
        private PlacementRunHandler _runHandler;
        private Autodesk.Revit.UI.ExternalEvent _runEvent;
        private PlacementRunRequest _runRequest;

        private void EnsureRunEvent()
        {
            if (_runHandler == null) _runHandler = new PlacementRunHandler();
            if (_runEvent == null)   _runEvent   = Autodesk.Revit.UI.ExternalEvent.Create(_runHandler);
        }

        private void RaiseRevitToFront()
        {
            // Best-effort Revit-window foreground hint. Real impl uses Win32 SetForegroundWindow.
            try { this.Activate(); } catch { /* may not be on UI thread */ }
        }
    }
}

// ──────────────────────────────────────────────────────────────────────
// 11. ViewStylePackApplier — missing Resolve* helpers
// ──────────────────────────────────────────────────────────────────────
namespace StingTools.Core.Drawing
{
    public static partial class ViewStylePackApplier
    {
        // ResolveCategoryId already exists in the main partial (line 562) — this
        // file only adds the *Cached variants the merged code expects.
        internal static ElementId ResolveCategoryIdCached(Document doc, string key)
        {
            try { if (Enum.TryParse<BuiltInCategory>(key, out var bic)) return new ElementId(bic); }
            catch { }
            return ElementId.InvalidElementId;
        }
        internal static ElementId ResolveFilterIdCached(Document doc, string name)
            => new FilteredElementCollector(doc).OfClass(typeof(ParameterFilterElement))
                .Cast<ParameterFilterElement>()
                .FirstOrDefault(f => string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase))?.Id
                ?? ElementId.InvalidElementId;
        internal static ElementId ResolveFillPattern(Document doc, string name)
            => new FilteredElementCollector(doc).OfClass(typeof(FillPatternElement))
                .Cast<FillPatternElement>()
                .FirstOrDefault(f => string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase))?.Id
                ?? ElementId.InvalidElementId;
    }
}

// ──────────────────────────────────────────────────────────────────────
// 12. TitleBlockParamApplier — missing helpers + Peek overload
// ──────────────────────────────────────────────────────────────────────
namespace StingTools.Core.Drawing
{
    public static partial class TitleBlockParamApplier
    {

        internal static bool TitleBlockHasAnyKey(Element tb, System.Collections.Generic.IEnumerable<string> keys)
        {
            if (tb == null || keys == null) return false;
            foreach (var k in keys)
            {
                try { if (tb.LookupParameter(k) != null) return true; } catch { }
            }
            return false;
        }

        /// <summary>Stub Peek overload that accepts an `unresolved` out-list. Real impl was lost to the merge.</summary>
        public static System.Collections.Generic.Dictionary<string, string> Peek(
            Document doc, DrawingType dt, System.Collections.Generic.IDictionary<string, string> tokens,
            System.Collections.Generic.List<string> unresolved)
        {
            unresolved?.Clear();
            return new System.Collections.Generic.Dictionary<string, string>();
        }
    }
}

// ──────────────────────────────────────────────────────────────────────
// 13. ManagedTemplateSyncer — 3-arg EnsureTemplate overload
// ──────────────────────────────────────────────────────────────────────
namespace StingTools.Core.Drawing
{
    internal static partial class ManagedTemplateSyncer
    {
        public static ElementId EnsureTemplate(Document doc, ViewStylePack pack, Autodesk.Revit.DB.ViewType vt)
            => EnsureTemplate(doc, pack, vt, new PackApplyResult());
    }
}

// ──────────────────────────────────────────────────────────────────────
// 14. DropEngineBase — soffit-clash hook stubs
// ──────────────────────────────────────────────────────────────────────
namespace StingTools.Core.Routing
{
    public abstract partial class DropEngineBase
    {
        // The merged callers do:  if (CheckSoffitClash) { if (_soffitAwareness?.SegmentIsRoutable(...)) {...} }
        // CheckSoffitClash is a bool property; _soffitAwareness is the real
        // StingTools.Core.Placement.StructuralAwareness class (defined in
        // StingTools/Core/Placement/StructuralAwareness.cs — not stubbed here).
        protected bool CheckSoffitClash { get; set; }
        protected StingTools.Core.Placement.StructuralAwareness _soffitAwareness;
    }
}

// ──────────────────────────────────────────────────────────────────────
// 15. Pipe.SetSlope extension — used by SlopeAutoCorrector
// ──────────────────────────────────────────────────────────────────────
namespace StingTools.Core.Calc
{
    internal static class PipeSetSlopeExt
    {
        public static void SetSlope(this Autodesk.Revit.DB.Plumbing.Pipe _, double slopePct, double minDropMm = 0) { /* stub */ }
        public static void SetSlope(this Autodesk.Revit.DB.Plumbing.Pipe _, Autodesk.Revit.DB.Connector fixedConnector, double slopeFtFt) { /* stub */ }
    }
}



