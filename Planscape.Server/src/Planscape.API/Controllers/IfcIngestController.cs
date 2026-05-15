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
    private readonly ILogger<IfcIngestController> _logger;

    public IfcIngestController(
        PlanscapeDbContext db,
        IIfcIngester ingester,
        IFileStorageService storage,
        IAuditService audit,
        ILogger<IfcIngestController> logger)
    {
        _db = db;
        _ingester = ingester;
        _storage = storage;
        _audit = audit;
        _logger = logger;
    }

    [HttpPost("ingest")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(MaxIfcSize)]
    public async Task<ActionResult> Ingest(
        Guid projectId,
        IFormFile file,
        CancellationToken ct)
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

            var result = await _ingester.IngestAsync(tmp, ct);
            await PersistAsTaggedElementsAsync(projectId, result, ct);

            // Build warnings list — include the quantity-set advisory when
            // the ingester found no IIfcElementQuantity entities.
            var responseWarnings = new List<string?>();
            if (result.Warnings != null) responseWarnings.Add(result.Warnings);
            if (!result.HasQuantitySets)
                responseWarnings.Add("No quantity sets found. Re-export from ArchiCAD with 'Export Quantity Sets (Qto)' enabled for cost extraction.");

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

            string ResolveParam(string paramName)
            {
                var entries = mapping.Where(m => m.StingParam == paramName);
                foreach (var m in entries)
                {
                    if (m.ScanAllPsets)
                    {
                        var suffix = $".{m.PropertyName}";
                        var hit = props.Keys
                            .FirstOrDefault(k => k.EndsWith(suffix, StringComparison.Ordinal));
                        if (hit != null && props.TryGetValue(hit, out var sv)) return sv;
                    }
                    else
                    {
                        // quantity_type and standard pset entries both use
                        // the same "pset_name.property_name" bag key.
                        var key = $"{m.PsetName}.{m.PropertyName}";
                        if (props.TryGetValue(key, out var pv)) return pv;
                    }
                }
                return "";
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
}
