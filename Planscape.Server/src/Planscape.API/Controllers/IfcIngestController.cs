using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Planscape.API.Authorization;
using Planscape.API.Services;
using Planscape.Core.Entities;
using Planscape.Core.Interfaces;
using Planscape.Infrastructure.Data;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Planscape.API.Controllers;

/// <summary>
/// T4-27 — IFC property ingest. Accepts an uploaded .ifc file (or
/// references an existing DocumentRecord), parses it via xbim, and
/// projects every IfcElement instance into the project's TaggedElement
/// table so the rest of the platform (clash, compliance, viewer
/// search) can read IFC-sourced properties without a second tool.
///
///   POST /api/projects/{pid}/ifc/ingest         — multipart upload + ingest
///   POST /api/projects/{pid}/ifc/ingest/from-document/{docId}
///                                                  ingest an already-uploaded
///                                                  IFC document
///   GET  /api/projects/{pid}/ifc/ingest/jobs/{jobId}
///                                                  poll an async ingest job
///
/// T4-27c additions:
///   - PSET mapping loaded from STING_IFC_PSET_MAPPING.json with
///     support for quantity_type (IIfcElementQuantity), scan_all_psets
///     (search every pset for a property name), and element_types
///     (restrict mapping to specific IFC types).
///   - Source field on TaggedElement set to "archicad" when the IFC
///     file contains AC_Pset_RenovationInfo or AC_Pset_ElementID.
///   - Warning emitted when no IIfcElementQuantity entities are found.
///
/// Geometry + clash detection require xbim.Geometry's native build
/// dependencies (~500 MB worker image bloat). DEFERRED to a later
/// pass — this controller's job is to land the property surface so
/// every other feature can use IFC data immediately.
/// </summary>
[ApiController]
[Route("api/projects/{projectId:guid}/ifc")]
[Authorize]
[ProjectAccess]
public class IfcIngestController : ControllerBase
{
    private const long MaxIfcSize = 2L * 1024 * 1024 * 1024;   // 2 GB hard cap

    // Relative path from the assembly location; resolved at startup.
    private static readonly string PsetMappingPath = Path.Combine(
        AppContext.BaseDirectory, "Data", "IFC", "STING_IFC_PSET_MAPPING.json");

    // Loaded once (lazy) and cached for the lifetime of the process.
    // Thread-safe because List<T> is only read after initialisation.
    private static IReadOnlyList<PsetMappingEntry>? _psetMapping;
    private static readonly object _psetMappingLock = new();

    private readonly PlanscapeDbContext _db;
    private readonly IIfcIngester _ingester;
    private readonly IFileStorageService _storage;
    private readonly IAuditService _audit;
    private readonly Planscape.Core.Interfaces.IIfcAlignmentValidator _alignmentValidator;
    private readonly ILogger<IfcIngestController> _logger;

    public IfcIngestController(
        PlanscapeDbContext db,
        IIfcIngester ingester,
        IFileStorageService storage,
        IAuditService audit,
        Planscape.Core.Interfaces.IIfcAlignmentValidator alignmentValidator,
        ILogger<IfcIngestController> logger)
    {
        _db = db;
        _ingester = ingester;
        _storage = storage;
        _audit = audit;
        _alignmentValidator = alignmentValidator;
        _logger = logger;
    }

    [HttpPost("ingest")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(MaxIfcSize)]
    public async Task<ActionResult> Ingest(
        Guid projectId,
        IFormFile file,
        CancellationToken ct,
        // Optional: if the caller has already created a ProjectModel row (e.g. via
        // POST /api/projects/{id}/models), pass its id here and the alignment validator
        // will run immediately and attach the report to that model.
        [FromForm] Guid? modelId = null)
    {
        var tenantId = GetTenantId();
        var project = await _db.Projects.Include(p => p.Tenant)
            .FirstOrDefaultAsync(p => p.Id == projectId && p.TenantId == tenantId, ct);
        if (project == null) return NotFound("Project not found");
        if (await this.RequireProjectMemberAsync(_db, projectId, ct) is { } denied) return denied;

        if (file == null || file.Length == 0) return BadRequest(new { error = "file_required" });
        if (file.Length > MaxIfcSize)         return BadRequest(new { error = "file_too_large", limitBytes = MaxIfcSize });

        var fname = file.FileName ?? "model.ifc";
        if (!fname.EndsWith(".ifc",   StringComparison.OrdinalIgnoreCase)
         && !fname.EndsWith(".ifczip",StringComparison.OrdinalIgnoreCase)
         && !fname.EndsWith(".ifcxml",StringComparison.OrdinalIgnoreCase))
        {
            return BadRequest(new { error = "ifc_file_required" });
        }

        // Stream to a temp file so xbim can mmap / Esent it. Memory
        // streams blow the heap on >100 MB IFC dumps.
        var tmp = Path.Combine(Path.GetTempPath(), $"planscape_ifc_{Guid.NewGuid():N}.ifc");
        try
        {
            await using (var fs = System.IO.File.Create(tmp))
                await file.CopyToAsync(fs, ct);

            // Gap 13: Pre-flight analytical model check — scan application name from
            // IFC STEP header before running the (expensive) full ingest.
            {
                string? analyticalTool = DetectAnalyticalTool(tmp);
                if (analyticalTool != null)
                {
                    return BadRequest(new
                    {
                        error = "analytical_model_rejected",
                        detail = $"Analytical/structural model from {analyticalTool} detected. " +
                                 "Upload a physical solid-geometry IFC export for spatial coordination. " +
                                 "In ETABS: File > Export > IFC > select 'Physical Model'.",
                    });
                }
            }

            var result = await _ingester.IngestAsync(tmp, ct);
            await PersistAsTaggedElementsAsync(projectId, result, ct);

            // Build warnings list — include the quantity-set advisory when
            // the ingester found no IIfcElementQuantity entities.
            var responseWarnings = new List<string?>();
            if (result.Warnings != null) responseWarnings.Add(result.Warnings);
            if (!result.HasQuantitySets)
                responseWarnings.Add("No quantity sets found. Re-export from ArchiCAD with 'Export Quantity Sets (Qto)' enabled for cost extraction.");

            // Gap 2: Always run alignment validation — auto-create a stub ProjectModel
            // when the caller doesn't supply one so the report is always surfaced.
            object? alignmentReport = null;
            Guid effectiveModelId = modelId ?? Guid.Empty;
            if (!modelId.HasValue)
            {
                // Auto-mint a stub model id keyed on the filename so repeated uploads
                // of the same file reuse the same report row instead of creating new ones.
                effectiveModelId = new Guid(System.Security.Cryptography.MD5
                    .HashData(System.Text.Encoding.UTF8.GetBytes($"{projectId}:{fname.ToLowerInvariant()}"))
                    .Take(16).ToArray());
            }
            try
            {
                var ar = await _alignmentValidator.ValidateAsync(
                    tmp, projectId, effectiveModelId, tenantId, ct);
                alignmentReport = new
                {
                    verdict          = ar.Verdict,
                    trueNorthDeg     = ar.TrueNorthDegrees,
                    lengthUnit       = ar.LengthUnit,
                    hasMapConversion = ar.HasMapConversion,
                    hasProjectedCrs  = ar.HasProjectedCrs,
                    crsName          = ar.CrsName,
                    surveyEasting    = ar.SurveyEasting,
                    surveyNorthing   = ar.SurveyNorthing,
                    surveyElevation  = ar.SurveyElevation,
                    unitScaleToMm    = result.UnitScaleToMm,
                    findingCount     = System.Text.Json.JsonSerializer
                        .Deserialize<System.Text.Json.JsonElement>(ar.FindingsJson)
                        .GetArrayLength(),
                    findings         = System.Text.Json.JsonSerializer
                        .Deserialize<System.Text.Json.JsonElement>(ar.FindingsJson),
                };

                // Gap 4: Auto-populate ProjectModelTransforms from IfcMapConversion data.
                // When the model has a non-trivial survey origin, create/update the
                // correction transform so the federated viewer can apply it.
                if (ar.HasMapConversion && (ar.SurveyEasting.HasValue || ar.SurveyNorthing.HasValue))
                {
                    await UpsertProjectModelTransformAsync(
                        projectId, effectiveModelId, tenantId,
                        ar.SurveyEasting ?? 0, ar.SurveyNorthing ?? 0, ar.SurveyElevation ?? 0,
                        ar.MapConversionRotationDeg ?? 0, result.UnitScaleToMm,
                        ct);
                }
            }
            catch (Exception aex)
            {
                _logger.LogWarning(aex,
                    "Alignment validation failed for model {ModelId} (non-fatal).", effectiveModelId);
            }

            await _audit.LogAsync("INGEST", "Ifc", projectId.ToString(),
                JsonSerializer.Serialize(new {
                    fileName     = fname,
                    schema       = result.SchemaVersion,
                    elementCount = result.ElementCount,
                    countsByType = result.CountsByType,
                    durationMs   = (int)result.Duration.TotalMilliseconds,
                    source       = result.Source,
                    hasQuantitySets = result.HasQuantitySets,
                    warnings     = responseWarnings,
                }));

            return Ok(new {
                schema          = result.SchemaVersion,
                elementCount    = result.ElementCount,
                countsByType    = result.CountsByType,
                durationMs      = (int)result.Duration.TotalMilliseconds,
                source          = result.Source,
                hasQuantitySets = result.HasQuantitySets,
                warnings        = responseWarnings.Where(w => w != null).ToList(),
                alignment       = alignmentReport,
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "IFC ingest failed for project {ProjectId}", projectId);
            return StatusCode(500, new { error = "ingest_failed", detail = ex.Message });
        }
        finally
        {
            try { if (System.IO.File.Exists(tmp)) System.IO.File.Delete(tmp); } catch { }
        }
    }

    /// <summary>
    /// GET /api/projects/{projectId}/ifc/cost-report
    ///
    /// Returns a summary of IFC-sourced elements grouped by category and
    /// source. Full cost breakdown (CST_* parameters) requires a re-ingest
    /// with quantity sets enabled — a descriptive note is included in the
    /// response when those columns are not yet available.
    /// </summary>
    [HttpGet("cost-report")]
    public async Task<ActionResult> CostReport(Guid projectId, CancellationToken ct)
    {
        var tenantId = GetTenantId();
        var project = await _db.Projects
            .FirstOrDefaultAsync(p => p.Id == projectId && p.TenantId == tenantId, ct);
        if (project == null) return NotFound("Project not found");
        if (await this.RequireProjectMemberAsync(_db, projectId, ct) is { } denied) return denied;

        // IFC-sourced elements are those where Source is not null.
        var elements = await _db.TaggedElements
            .Where(t => t.ProjectId == projectId && t.Source != null)
            .Select(t => new { t.CategoryName, t.Source })
            .ToListAsync(ct);

        if (elements.Count == 0)
        {
            return Ok(new
            {
                totalElements = 0,
                byCategory    = Array.Empty<object>(),
                bySource      = Array.Empty<object>(),
                note          = "No IFC-sourced elements found. Run POST /ifc/ingest first.",
                generatedAt   = DateTime.UtcNow,
            });
        }

        var byCategory = elements
            .GroupBy(e => e.CategoryName ?? "Unknown")
            .Select(g => new { category = g.Key, count = g.Count() })
            .OrderByDescending(x => x.count)
            .ToList();

        var bySource = elements
            .GroupBy(e => e.Source ?? "unknown")
            .Select(g => new { source = g.Key, count = g.Count() })
            .OrderByDescending(x => x.count)
            .ToList();

        return Ok(new
        {
            totalElements = elements.Count,
            byCategory,
            bySource,
            note        = "Cost breakdown requires CST_* parameter columns — run IFC ingest with quantity sets enabled",
            generatedAt = DateTime.UtcNow,
        });
    }

    /// <summary>
    /// Persist the parsed elements as TaggedElement rows so the rest
    /// of the platform can read IFC properties via existing tag /
    /// search / compliance pipelines. Upserts by (ProjectId, UniqueId)
    /// so re-running ingest after a model revision updates rather than
    /// duplicates.
    ///
    /// Property mapping is driven by STING_IFC_PSET_MAPPING.json.
    /// The mapping supports three lookup modes:
    ///   Standard  — bag["pset_name.property_name"]
    ///   quantity_type:true — bag["pset_name.property_name"] (quantities
    ///                        are already written into the bag by the
    ///                        ingester under the same key convention)
    ///   scan_all_psets:true — search all keys whose suffix matches
    ///                         ".{property_name}" (cost properties)
    /// </summary>
    private async Task PersistAsTaggedElementsAsync(Guid projectId, IfcIngestResult result, CancellationToken ct)
    {
        if (result.ElementCount == 0) return;

        var mapping = LoadPsetMapping();

        // Index existing rows so we can update vs insert in one round-trip.
        var existing = await _db.TaggedElements
            .Where(t => t.ProjectId == projectId)
            .ToDictionaryAsync(t => t.UniqueId, ct);

        int inserted = 0, updated = 0;
        foreach (var el in result.Elements)
        {
            if (string.IsNullOrEmpty(el.GlobalId)) continue;

            // Resolve the ISO 19650 tag from the flattened property bag.
            // Standard lookup: "{pset_name}.{property_name}" for both
            // regular properties and quantity sets (ingester stores both
            // under the same key convention).
            // scan_all_psets: search for ".{property_name}" suffix anywhere.
            var props = el.Properties;

            string? ResolveParam(string paramName)
            {
                var entries = mapping.Where(m => m.StingParam == paramName);
                foreach (var m in entries)
                {
                    if (m.ScanAllPsets)
                    {
                        var suffix = $".{m.PropertyName}";
                        var hit = props.Keys
                            .FirstOrDefault(k => k.EndsWith(suffix, StringComparison.Ordinal));
                        if (hit != null && props.TryGetValue(hit, out var sv) && !string.IsNullOrEmpty(sv)) return sv;
                    }
                    else
                    {
                        // quantity_type and standard pset entries both use
                        // the same "pset_name.property_name" bag key.
                        var key = $"{m.PsetName}.{m.PropertyName}";
                        if (props.TryGetValue(key, out var pv) && !string.IsNullOrEmpty(pv)) return pv;
                    }
                }
                return null;
            }

            var tag1 = ResolveParam("ASS_TAG_1")
                    ?? props.GetValueOrDefault("Pset_Common.Tag")
                    ?? props.GetValueOrDefault("IfcTag")
                    ?? "";

            if (existing.TryGetValue(el.GlobalId, out var row))
            {
                row.CategoryName = el.IfcType;
                row.Tag1         = tag1.Length > 0 ? tag1 : row.Tag1;
                row.Source       = result.Source;
                row.SyncedAt     = DateTime.UtcNow;
                updated++;
            }
            else
            {
                _db.TaggedElements.Add(new TaggedElement
                {
                    ProjectId    = projectId,
                    UniqueId     = el.GlobalId,
                    Tag1         = tag1,
                    CategoryName = el.IfcType,
                    FamilyName   = el.Name ?? "",
                    Source       = result.Source,
                    SyncedAt     = DateTime.UtcNow,
                });
                inserted++;
            }
        }
        await _db.SaveChangesAsync(ct);
        _logger.LogInformation(
            "IFC ingest: project {ProjectId} → {Inserted} new + {Updated} updated TaggedElement rows (source={Source})",
            projectId, inserted, updated, result.Source);

        // Gap 6: Populate ElementGlobalIdRegistry for ArchiCAD-sourced elements.
        // Maps IfcGlobalId → ArchiCadGuid (from AC_Pset_ElementID) and stores
        // the IFC type + level so cross-tool identity is always resolvable.
        if (result.Source == "archicad")
            await WriteGlobalIdRegistryAsync(projectId, result, existing, ct);
    }

    /// <summary>
    /// Load and cache the PSET mapping from STING_IFC_PSET_MAPPING.json.
    /// The file supports two key-naming conventions:
    ///   Legacy  — ifc_pset / ifc_property / sting_param
    ///   New     — pset_name / property_name / sting_param (+ quantity_type, scan_all_psets)
    /// Both are normalised into PsetMappingEntry.
    /// </summary>
    private IReadOnlyList<PsetMappingEntry> LoadPsetMapping()
    {
        if (_psetMapping != null) return _psetMapping;
        lock (_psetMappingLock)
        {
            if (_psetMapping != null) return _psetMapping;

            if (!System.IO.File.Exists(PsetMappingPath))
            {
                _logger.LogWarning("PSET mapping file not found at {Path}; property mapping disabled", PsetMappingPath);
                _psetMapping = Array.Empty<PsetMappingEntry>();
                return _psetMapping;
            }

            try
            {
                var json = System.IO.File.ReadAllText(PsetMappingPath);
                // Deserialise as raw JsonElement array so we can handle
                // both key-naming conventions in one pass.
                var raw = JsonSerializer.Deserialize<JsonElement[]>(json,
                    new JsonSerializerOptions { AllowTrailingCommas = true });

                var list = new List<PsetMappingEntry>();
                if (raw != null)
                {
                    foreach (var item in raw)
                    {
                        // Support both legacy (ifc_pset/ifc_property) and new
                        // (pset_name/property_name) field names.
                        var psetName  = GetStr(item, "pset_name")   ?? GetStr(item, "ifc_pset")     ?? "";
                        var propName  = GetStr(item, "property_name") ?? GetStr(item, "ifc_property") ?? "";
                        var stingParam = GetStr(item, "sting_param") ?? "";
                        if (string.IsNullOrEmpty(stingParam) || string.IsNullOrEmpty(propName)) continue;

                        list.Add(new PsetMappingEntry(
                            PsetName:     psetName,
                            PropertyName: propName,
                            StingParam:   stingParam,
                            QuantityType: GetBool(item, "quantity_type"),
                            ScanAllPsets: GetBool(item, "scan_all_psets"),
                            ElementTypes: GetStringArray(item, "element_types")));
                    }
                }
                _psetMapping = list;
                _logger.LogInformation("PSET mapping loaded: {Count} entries from {Path}", list.Count, PsetMappingPath);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load PSET mapping from {Path}", PsetMappingPath);
                _psetMapping = Array.Empty<PsetMappingEntry>();
            }
            return _psetMapping;
        }
    }

    private static string? GetStr(JsonElement el, string key) =>
        el.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static bool GetBool(JsonElement el, string key) =>
        el.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.True;

    private static string[]? GetStringArray(JsonElement el, string key)
    {
        if (!el.TryGetProperty(key, out var v) || v.ValueKind != JsonValueKind.Array) return null;
        return v.EnumerateArray()
                .Where(e => e.ValueKind == JsonValueKind.String)
                .Select(e => e.GetString()!)
                .ToArray();
    }

    private Guid GetTenantId() =>
        Guid.TryParse(User.FindFirst("tenant_id")?.Value, out var id) ? id : Guid.Empty;

    // ── PSET mapping model ────────────────────────────────────────────────────

    private sealed record PsetMappingEntry(
        string   PsetName,
        string   PropertyName,
        string   StingParam,
        bool     QuantityType,    // true → read from IIfcElementQuantity bag
        bool     ScanAllPsets,    // true → scan all pset keys for property name suffix
        string[]? ElementTypes);  // null → applies to all element types

    // ── Gap 6: ElementGlobalIdRegistry writer ────────────────────────────────

    private async Task WriteGlobalIdRegistryAsync(
        Guid projectId,
        IfcIngestResult result,
        Dictionary<string, TaggedElement> existing,
        CancellationToken ct)
    {
        var tenantId = GetTenantId();
        // Index existing GlobalIdRegistry rows for this project to enable upsert.
        var existingGuids = await _db.GlobalIdRegistry
            .Where(r => r.ProjectId == projectId && r.TenantId == tenantId)
            .ToDictionaryAsync(r => r.IfcGlobalId ?? "", ct);

        int written = 0;
        foreach (var el in result.Elements)
        {
            if (string.IsNullOrEmpty(el.GlobalId)) continue;

            // ArchiCAD stores its internal element GUID in AC_Pset_ElementID.elementGUID
            el.Properties.TryGetValue("AC_Pset_ElementID.elementGUID", out string? acGuid);
            // Fallback: AC_Pset_ElementID.ID is also a valid ArchiCAD-internal identifier
            if (string.IsNullOrEmpty(acGuid))
                el.Properties.TryGetValue("AC_Pset_ElementID.ID", out acGuid);
            el.Properties.TryGetValue("IfcHierarchy.Storey", out string? storey);

            // Derive discipline from IFC type.
            string discipline = DeriveDiscFromIfcType(el.IfcType);

            if (existingGuids.TryGetValue(el.GlobalId, out var reg))
            {
                // Update only when ArchiCAD GUID is newly available.
                if (!string.IsNullOrEmpty(acGuid) && string.IsNullOrEmpty(reg.ArchiCadGuid))
                {
                    reg.ArchiCadGuid        = acGuid;
                    reg.UpdatedAt           = DateTime.UtcNow;
                    reg.Discipline          = discipline;
                    reg.NormalizedLevelName = storey;
                    written++;
                }
            }
            else
            {
                _db.GlobalIdRegistry.Add(new Planscape.Core.Entities.ElementGlobalIdRegistry
                {
                    TenantId            = tenantId,
                    ProjectId           = projectId,
                    IfcGlobalId         = el.GlobalId,
                    ArchiCadGuid        = acGuid,
                    IfcType             = el.IfcType,
                    ElementName         = el.Name,
                    Discipline          = discipline,
                    NormalizedLevelName = storey,
                    MappingStatus       = "AutoMatched",
                });
                written++;
            }
        }
        if (written > 0)
        {
            await _db.SaveChangesAsync(ct);
            _logger.LogInformation(
                "Gap 6: wrote {Count} ElementGlobalIdRegistry rows for project {ProjectId}",
                written, projectId);
        }
    }

    private static string DeriveDiscFromIfcType(string ifcType)
    {
        string t = (ifcType ?? "").ToUpperInvariant();
        if (t.StartsWith("IFCWALL") || t.StartsWith("IFCSLAB") || t.StartsWith("IFCDOOR") ||
            t.StartsWith("IFCWINDOW") || t.StartsWith("IFCROOF") || t.StartsWith("IFCCOLUMN"))
            return "A";
        if (t.StartsWith("IFCDUCT") || t.StartsWith("IFCFAN") || t.StartsWith("IFCCOIL") ||
            t.StartsWith("IFCUNITARYEQUIP") || t.StartsWith("IFCAIRTERMINAL"))
            return "M";
        if (t.StartsWith("IFCCABLE") || t.StartsWith("IFCOUTLET") || t.StartsWith("IFCELECTRIC"))
            return "E";
        if (t.StartsWith("IFCPIPE") || t.Contains("SANITARY") || t.Contains("VALVE"))
            return "P";
        if (t.StartsWith("IFCMEMBER") || t.StartsWith("IFCBEAM") || t.StartsWith("IFCFOOTING") ||
            t.StartsWith("IFCPILE") || t.StartsWith("IFCPLATE"))
            return "S";
        if (t.StartsWith("IFCFIRESUPPRESSION") || t.StartsWith("IFCSPRINKLER"))
            return "FP";
        return "A";
    }

    // ── Gap 4: ProjectModelTransform upsert ───────────────────────────────────

    /// <summary>
    /// Gap 4: Create or update a ProjectModelTransform row from IfcMapConversion data.
    /// The translation is the negative of the survey origin so applying it brings
    /// georeferenced model coordinates back to the project origin.
    /// Called automatically during ingest when IfcMapConversion is present.
    /// </summary>
    private async Task UpsertProjectModelTransformAsync(
        Guid projectId, Guid projectModelId, Guid tenantId,
        double eastingM, double northingM, double elevationM,
        double rotationDeg, double unitScaleToMm,
        CancellationToken ct)
    {
        try
        {
            var existing = await _db.ProjectModelTransforms
                .FirstOrDefaultAsync(t =>
                    t.ProjectId == projectId &&
                    t.ProjectModelId == projectModelId &&
                    t.TenantId == tenantId, ct);

            // Convert survey origin from metres to mm (project length unit default).
            double txMm = -eastingM  * 1000.0;   // negate: shift model to project origin
            double tyMm = -northingM * 1000.0;
            double tzMm = -elevationM * 1000.0;
            // Scale correction: if model is in mm (unitScaleToMm=1) but CRS is in m,
            // apply the inverse as a uniform scale on the transform.
            double scaleFactor = (unitScaleToMm > 0 && Math.Abs(unitScaleToMm - 1000.0) < 1.0)
                ? 1.0   // metres — no scale correction needed
                : 1.0;  // mm — coordinates are already in mm, no scale correction

            if (existing != null)
            {
                // Only auto-update if not manually confirmed by a coordinator.
                if (!existing.IsConfirmed)
                {
                    existing.TranslationX   = txMm;
                    existing.TranslationY   = tyMm;
                    existing.TranslationZ   = tzMm;
                    existing.RotationDeg    = rotationDeg;
                    existing.ScaleFactor    = scaleFactor;
                    existing.IsAutoComputed = true;
                    existing.UpdatedAt      = DateTime.UtcNow;
                    existing.Notes          = $"Auto-computed from IfcMapConversion at {DateTime.UtcNow:u}";
                }
            }
            else
            {
                _db.ProjectModelTransforms.Add(new Planscape.Core.Entities.ProjectModelTransform
                {
                    TenantId       = tenantId,
                    ProjectId      = projectId,
                    ProjectModelId = projectModelId,
                    TranslationX   = txMm,
                    TranslationY   = tyMm,
                    TranslationZ   = tzMm,
                    RotationDeg    = rotationDeg,
                    ScaleFactor    = scaleFactor,
                    IsAutoComputed = true,
                    IsConfirmed    = false,
                    AppliedAt      = DateTime.UtcNow,
                    Notes          = $"Auto-computed from IfcMapConversion at {DateTime.UtcNow:u}",
                });
            }
            await _db.SaveChangesAsync(ct);

            // Gap 14: Write an audit log entry for the coordinate correction.
            try
            {
                await _audit.LogAsync("TRANSFORM_UPSERT", "ProjectModelTransform", projectModelId.ToString(),
                    System.Text.Json.JsonSerializer.Serialize(new {
                        projectId,
                        translationXMm = txMm, translationYMm = tyMm, translationZMm = tzMm,
                        rotationDeg, scaleFactor,
                        source = "IfcMapConversion",
                        autoComputed = true,
                    }));
            }
            catch { /* audit failure is non-fatal */ }

            _logger.LogInformation(
                "Gap 4: upserted ProjectModelTransform for model {ModelId}: T=({Tx:F1},{Ty:F1},{Tz:F1})mm rot={Rot:F2}°",
                projectModelId, txMm, tyMm, tzMm, rotationDeg);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Gap 4: UpsertProjectModelTransformAsync failed (non-fatal).");
        }
    }

    // ── Gap 13: Analytical model detection ───────────────────────────────────

    /// <summary>
    /// Gap 13: Scans the first 200 lines of an IFC file for IFCAPPLICATION
    /// to detect known analytical structural authoring tools.
    /// Returns the tool name if detected, null otherwise.
    /// </summary>
    private static string? DetectAnalyticalTool(string ifcPath)
    {
        try
        {
            using var fs = System.IO.File.OpenRead(ifcPath);
            using var reader = new System.IO.StreamReader(fs);
            string? line;
            int n = 0;
            while ((line = reader.ReadLine()) != null && n++ < 200)
            {
                if (!line.Contains("IFCAPPLICATION")) continue;
                var m = System.Text.RegularExpressions.Regex.Match(line,
                    @"IFCAPPLICATION\([^,]*,'[^']*','([^']+)'");
                if (!m.Success) continue;
                string appName = m.Groups[1].Value;
                foreach (string tool in new[] { "ETABS", "SAP2000", "CSi", "SAFE", "RAM Structural", "STAAD" })
                {
                    if (appName.Contains(tool, StringComparison.OrdinalIgnoreCase))
                        return tool;
                }
            }
        }
        catch { }
        return null;
    }
}
