using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Planscape.Core.Entities;
using Planscape.Core.Interfaces;
using Planscape.Infrastructure.Data;

namespace Planscape.API.Controllers;

[ApiController]
[Route("api/projects/{projectId}/boq")]
[Authorize]
public class BoqController : ControllerBase
{
    private readonly PlanscapeDbContext _db;
    private readonly ILogger<BoqController> _logger;
    private readonly IFileStorageService _storage;
    private readonly INotificationService? _notifications;

    public BoqController(
        PlanscapeDbContext db,
        ILogger<BoqController> logger,
        IFileStorageService storage,
        INotificationService? notifications = null)
    {
        _db = db;
        _logger = logger;
        _storage = storage;
        _notifications = notifications;
    }

    private Guid GetTenantId() =>
        Guid.Parse(User.FindFirst("tenant_id")?.Value
            ?? throw new InvalidOperationException("tenant_id claim missing"));

    // ── BOQ Documents ─────────────────────────────────────────────────────

    [HttpGet("documents")]
    public async Task<ActionResult> GetDocuments(Guid projectId)
    {
        var tenantId = GetTenantId();
        var docs = await _db.BoqDocuments
            .Where(d => d.ProjectId == projectId && d.TenantId == tenantId)
            .OrderByDescending(d => d.CreatedAt)
            .Select(d => new
            {
                d.Id, d.Name, d.ClientName, d.Architect, d.ContractForm,
                d.PricingBasis, d.Currency, d.Status, d.Revision,
                d.DayworkLabourPct, d.DayworkMaterialsPct, d.DayworkPlantPct,
                d.LocationFactor, d.PrimaryClassificationSystemId,
                d.SecondaryClassificationSystemId,
                d.CreatedAt, d.UpdatedAt, d.CreatedBy
            })
            .ToListAsync();
        return Ok(docs);
    }

    [HttpGet("documents/{id}")]
    public async Task<ActionResult> GetDocument(Guid projectId, Guid id)
    {
        var tenantId = GetTenantId();
        var doc = await _db.BoqDocuments
            .Where(d => d.Id == id && d.ProjectId == projectId && d.TenantId == tenantId)
            .FirstOrDefaultAsync();
        return doc is null ? NotFound() : Ok(doc);
    }

    [HttpPost("documents")]
    public async Task<ActionResult> CreateDocument(Guid projectId, [FromBody] CreateBoqDocumentRequest req)
    {
        var tenantId = GetTenantId();
        var doc = new BoqDocument
        {
            TenantId                    = tenantId,
            ProjectId                   = projectId,
            Name                        = req.Name,
            ClientName                  = req.ClientName,
            Architect                   = req.Architect ?? "",
            ContractForm                = req.ContractForm ?? "",
            InsuranceParticulars        = req.InsuranceParticulars,
            DayworkLabourPct            = req.DayworkLabourPct ?? 115m,
            DayworkMaterialsPct         = req.DayworkMaterialsPct ?? 110m,
            DayworkPlantPct             = req.DayworkPlantPct ?? 112m,
            LocationFactor              = req.LocationFactor ?? 1.000m,
            PricingBasis                = req.PricingBasis ?? "Remeasure",
            Currency                    = req.Currency ?? "GBP",
            Status                      = "Draft",
            Revision                    = "A",
            PrimaryClassificationSystemId = req.PrimaryClassificationSystemId,
            SecondaryClassificationSystemId = req.SecondaryClassificationSystemId,
            CreatedBy                   = User.Identity?.Name,
        };
        _db.BoqDocuments.Add(doc);
        await _db.SaveChangesAsync();
        return CreatedAtAction(nameof(GetDocument), new { projectId, id = doc.Id }, doc);
    }

    [HttpPut("documents/{id}")]
    public async Task<ActionResult> UpdateDocument(Guid projectId, Guid id, [FromBody] CreateBoqDocumentRequest req)
    {
        var tenantId = GetTenantId();
        var doc = await _db.BoqDocuments
            .FirstOrDefaultAsync(d => d.Id == id && d.ProjectId == projectId && d.TenantId == tenantId);
        if (doc is null) return NotFound();

        doc.Name                        = req.Name;
        doc.ClientName                  = req.ClientName ?? doc.ClientName;
        doc.Architect                   = req.Architect ?? doc.Architect;
        doc.ContractForm                = req.ContractForm ?? doc.ContractForm;
        doc.InsuranceParticulars        = req.InsuranceParticulars ?? doc.InsuranceParticulars;
        doc.DayworkLabourPct            = req.DayworkLabourPct ?? doc.DayworkLabourPct;
        doc.DayworkMaterialsPct         = req.DayworkMaterialsPct ?? doc.DayworkMaterialsPct;
        doc.DayworkPlantPct             = req.DayworkPlantPct ?? doc.DayworkPlantPct;
        doc.LocationFactor              = req.LocationFactor ?? doc.LocationFactor;
        doc.PricingBasis                = req.PricingBasis ?? doc.PricingBasis;
        doc.Currency                    = req.Currency ?? doc.Currency;
        doc.UpdatedAt                   = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return Ok(doc);
    }

    // ── Cost Snapshot ───────────────────────────────────────────────────────
    // Drift 6 — this route was referenced (IfcBoqSeedJob cites
    // "BoqController.PushSnapshot") and matches the plugin's
    // PlanscapeServerClient.PushBoqSnapshotAsync POST /boq/snapshot, but was
    // never added → 404. The BoqSnapshot entity, BoqSnapshotDto and
    // BoqSnapshots DbSet already exist; this persists the aggregate cost
    // snapshot and broadcasts the same boq_snapshot_updated signal the IFC
    // seed job sends, so the mobile cost dashboard auto-refreshes.

    [HttpPost("snapshot")]
    public async Task<ActionResult> PushSnapshot(Guid projectId, [FromBody] BoqSnapshotDto dto)
    {
        if (dto == null) return BadRequest("missing body");
        var tenantId = GetTenantId();

        var snapshot = new BoqSnapshot
        {
            ProjectId       = projectId,
            TenantId        = tenantId,
            CreatedAt       = DateTime.UtcNow,
            CreatedByUserId = User.Identity?.Name ?? "",
            SnapshotJson    = System.Text.Json.JsonSerializer.Serialize(dto),
        };
        _db.BoqSnapshots.Add(snapshot);
        await _db.SaveChangesAsync();

        if (_notifications != null)
        {
            _ = _notifications.NotifyProjectAsync(
                projectId,
                "boq_snapshot_updated",
                "BOQ Snapshot Updated",
                $"Cost snapshot pushed — estimated {dto.TotalEstimated:F2}, actual {dto.TotalActual:F2}.",
                new { projectId, source = "plugin", snapshotId = snapshot.Id },
                CancellationToken.None);
        }

        return Ok(new { id = snapshot.Id, createdAt = snapshot.CreatedAt });
    }

    // ── Baselines ─────────────────────────────────────────────────────────

    [HttpGet("baselines")]
    public async Task<ActionResult> GetBaselines(Guid projectId)
    {
        var tenantId = GetTenantId();
        var baselines = await _db.BoqBaselines
            .Where(b => b.ProjectId == projectId && b.TenantId == tenantId)
            .OrderByDescending(b => b.CreatedAt)
            .Select(b => new
            {
                b.Id, b.Name, b.Kind, b.TotalValue, b.Currency,
                b.BaselinedAt, b.Checksum, b.DocumentRecordId, b.Description,
                b.CreatedAt, b.CreatedBy
            })
            .ToListAsync();
        return Ok(baselines);
    }

    [HttpPost("baselines")]
    public async Task<ActionResult> CreateBaseline(Guid projectId, [FromBody] CreateBaselineRequest req)
    {
        var tenantId = GetTenantId();

        var baseline = new BoqBaseline
        {
            TenantId      = tenantId,
            ProjectId     = projectId,
            Name          = req.Name,
            Kind          = req.Kind ?? "Tender",
            Currency      = req.Currency ?? "GBP",
            Description   = req.Description,
            CreatedBy     = User.Identity?.Name,
        };
        _db.BoqBaselines.Add(baseline);
        await _db.SaveChangesAsync();
        return Ok(baseline);
    }

    // ── Quantity Lines ────────────────────────────────────────────────────

    [HttpGet("baselines/{baselineId}/lines")]
    public async Task<ActionResult> GetQuantityLines(Guid projectId, Guid baselineId,
        [FromQuery] string? sectionCode = null, [FromQuery] Guid? workPackageId = null,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 200)
    {
        if (page < 1) page = 1;
        pageSize = Math.Clamp(pageSize, 1, 500);

        var tenantId = GetTenantId();
        var query = _db.QuantityLines
            .Where(l => l.BaselineId == baselineId && l.ProjectId == projectId && l.TenantId == tenantId);

        if (!string.IsNullOrEmpty(sectionCode))
            query = query.Where(l => l.SectionCode == sectionCode);
        if (workPackageId.HasValue)
            query = query.Where(l => l.WorkPackageId == workPackageId);

        var total = await query.CountAsync();
        var lines = await query
            .OrderBy(l => l.SectionCode).ThenBy(l => l.ItemDescription)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(l => new
            {
                l.Id, l.SectionCode, l.ItemDescription, l.IfcGlobalId, l.IfcType,
                l.Level, l.Zone, l.Unit, l.NetQuantity, l.WastePercent, l.Quantity,
                l.UnitRate, l.LineTotal, l.Currency, l.LineKind, l.PricingBasis,
                l.EmbodiedCarbonPerUnit, l.EmbodiedCarbonTotal,
                ClassificationCodeId = l.ClassificationCodeId,
                WorkPackageId        = l.WorkPackageId,
            })
            .ToListAsync();

        return Ok(new { total, page, pageSize, items = lines });
    }

    [HttpPost("baselines/{baselineId}/lines")]
    public async Task<ActionResult> UpsertQuantityLines(Guid projectId, Guid baselineId,
        [FromBody] List<UpsertQuantityLineRequest> req)
    {
        var tenantId = GetTenantId();
        var baseline = await _db.BoqBaselines
            .FirstOrDefaultAsync(b => b.Id == baselineId && b.ProjectId == projectId && b.TenantId == tenantId);
        if (baseline is null) return NotFound();
        if (req.Count == 0) return BadRequest("No lines provided.");
        if (baseline.LockedAt.HasValue) return Conflict("Baseline is locked and cannot be modified.");

        // Validate numeric ranges — negative quantities are invalid for standard lines.
        var invalid = req.Where(r => r.NetQuantity < 0 || r.WastePercent < 0 || r.WastePercent > 100).ToList();
        if (invalid.Count > 0)
            return BadRequest($"{invalid.Count} line(s) have invalid NetQuantity or WastePercent.");

        // True upsert: if a line with the same IfcGlobalId already exists on this baseline, update it.
        var incomingGlobalIds = req
            .Where(r => !string.IsNullOrEmpty(r.IfcGlobalId))
            .Select(r => r.IfcGlobalId!)
            .ToHashSet();

        var existingByGuid = await _db.QuantityLines
            .Where(l => l.BaselineId == baselineId && l.TenantId == tenantId
                     && incomingGlobalIds.Contains(l.IfcGlobalId!))
            .ToDictionaryAsync(l => l.IfcGlobalId!);

        int created = 0, updated = 0;
        foreach (var r in req)
        {
            var grossQty  = r.NetQuantity * (1 + r.WastePercent / 100.0);
            var lineTotal = r.UnitRate.HasValue ? (decimal)grossQty * r.UnitRate.Value : (decimal?)null;

            if (!string.IsNullOrEmpty(r.IfcGlobalId) && existingByGuid.TryGetValue(r.IfcGlobalId, out var existing))
            {
                // Update existing line.
                existing.NetQuantity          = r.NetQuantity;
                existing.WastePercent         = r.WastePercent;
                existing.Quantity             = grossQty;
                existing.UnitRate             = r.UnitRate ?? existing.UnitRate;
                existing.LineTotal            = lineTotal ?? existing.LineTotal;
                existing.Level                = r.Level ?? existing.Level;
                existing.Zone                 = r.Zone ?? existing.Zone;
                existing.SectionCode          = r.SectionCode ?? existing.SectionCode;
                existing.ItemDescription      = r.ItemDescription ?? existing.ItemDescription;
                existing.EmbodiedCarbonPerUnit = r.EmbodiedCarbonPerUnit ?? existing.EmbodiedCarbonPerUnit;
                existing.EmbodiedCarbonTotal  = existing.EmbodiedCarbonPerUnit.HasValue
                    ? existing.EmbodiedCarbonPerUnit * existing.NetQuantity : null;
                existing.UpdatedAt            = DateTime.UtcNow;
                updated++;
            }
            else
            {
                _db.QuantityLines.Add(new QuantityLine
                {
                    TenantId             = tenantId,
                    ProjectId            = projectId,
                    BaselineId           = baselineId,
                    ClassificationCodeId = r.ClassificationCodeId,
                    TakeoffRuleId        = r.TakeoffRuleId,
                    WorkPackageId        = r.WorkPackageId,
                    ProjectModelId       = r.ProjectModelId,
                    IfcGlobalId          = r.IfcGlobalId,
                    IfcType              = r.IfcType ?? "",
                    RevitElementId       = long.TryParse(r.RevitElementId, out var revitId) ? revitId : (long?)null,
                    Level                = r.Level ?? "",
                    Zone                 = r.Zone ?? "",
                    SectionCode          = r.SectionCode ?? "",
                    ItemDescription      = r.ItemDescription ?? "",
                    Unit                 = r.Unit ?? "m2",
                    NetQuantity          = r.NetQuantity,
                    WastePercent         = r.WastePercent,
                    Quantity             = grossQty,
                    UnitRate             = r.UnitRate,
                    LineTotal            = lineTotal,
                    Currency             = r.Currency ?? "GBP",
                    LineKind             = r.LineKind ?? "Measured",
                    PricingBasis         = r.PricingBasis ?? "Remeasure",
                    EmbodiedCarbonPerUnit = r.EmbodiedCarbonPerUnit,
                    EmbodiedCarbonTotal  = r.EmbodiedCarbonPerUnit.HasValue
                                           ? r.EmbodiedCarbonPerUnit * r.NetQuantity : null,
                });
                created++;
            }
        }

        await _db.SaveChangesAsync();

        // Recompute baseline total from all persisted lines (authoritative, avoids double-count on re-calls).
        baseline.TotalValue = await _db.QuantityLines
            .Where(l => l.BaselineId == baselineId && l.LineTotal.HasValue)
            .SumAsync(l => l.LineTotal ?? 0m);
        await _db.SaveChangesAsync();

        return Ok(new { created, updated, baselineTotal = baseline.TotalValue });
    }

    // ── BOQ Variations ────────────────────────────────────────────────────

    [HttpGet("variations")]
    public async Task<ActionResult> GetVariations(Guid projectId)
    {
        var tenantId = GetTenantId();
        var variations = await _db.BoqVariations
            .Where(v => v.ProjectId == projectId && v.TenantId == tenantId)
            .OrderByDescending(v => v.CreatedAt)
            .Select(v => new
            {
                v.Id, v.Reference, v.Title, v.Description, v.Kind,
                v.Status, v.NetValue, v.Currency,
                // Phase 184o — reason + liability surface in the mobile list.
                v.Reason, v.Liability, v.EotDays,
                // Phase 184q — contract family.
                v.ContractForm,
                v.BimIssueId, v.SubmittedAt, v.ApprovedAt, v.ApprovedBy,
                v.CreatedAt, v.CreatedBy
            })
            .ToListAsync();
        return Ok(variations);
    }

    [HttpPost("variations")]
    public async Task<ActionResult> CreateVariation(Guid projectId, [FromBody] CreateVariationRequest req)
    {
        var tenantId = GetTenantId();
        var exists = await _db.BoqVariations
            .AnyAsync(v => v.ProjectId == projectId && v.Reference == req.Reference && v.TenantId == tenantId);
        if (exists) return Conflict($"Variation reference '{req.Reference}' already exists.");

        var variation = new BoqVariation
        {
            TenantId   = tenantId,
            ProjectId  = projectId,
            BaselineId = req.BaselineId,
            Reference  = req.Reference,
            Title      = req.Title,
            Description = req.Description,
            Kind       = req.Kind ?? "VO",
            Status     = "Draft",
            NetValue   = req.NetValue,
            Currency   = req.Currency ?? "GBP",
            BimIssueId = req.BimIssueId,
            // Phase 184o — reason + liability + EOT carried in from the plugin.
            Reason     = req.Reason ?? "Other",
            Liability  = req.Liability ?? "Employer",
            ReasonDetail = req.ReasonDetail,
            EotDays    = req.EotDays ?? 0,
            // Phase 184q
            ContractForm = req.ContractForm ?? "JCT2024",
            CreatedBy  = User.Identity?.Name,
        };
        _db.BoqVariations.Add(variation);
        await _db.SaveChangesAsync();
        return Ok(variation);
    }

    [HttpPut("variations/{id}/status")]
    public async Task<ActionResult> UpdateVariationStatus(Guid projectId, Guid id, [FromBody] UpdateStatusRequest req)
    {
        var tenantId = GetTenantId();
        var variation = await _db.BoqVariations
            .FirstOrDefaultAsync(v => v.Id == id && v.ProjectId == projectId && v.TenantId == tenantId);
        if (variation is null) return NotFound();

        var allowed = new[] { "Draft", "Submitted", "Reviewed", "Approved", "Rejected", "Incorporated" };
        if (!allowed.Contains(req.Status)) return BadRequest("Invalid status.");

        variation.Status    = req.Status;
        variation.UpdatedAt = DateTime.UtcNow;
        if (req.Status is "Approved" or "Rejected")
        {
            variation.ApprovedAt = DateTime.UtcNow;
            variation.ApprovedBy = User.Identity?.Name;
        }
        await _db.SaveChangesAsync();
        return Ok(variation);
    }

    // ── Variation detail (Phase 184k / P7 mobile) ─────────────────────────

    [HttpGet("variations/{id}")]
    public async Task<ActionResult> GetVariation(Guid projectId, Guid id)
    {
        var tenantId = GetTenantId();
        var v = await _db.BoqVariations
            .FirstOrDefaultAsync(x => x.Id == id && x.ProjectId == projectId && x.TenantId == tenantId);
        if (v is null) return NotFound();

        // The mobile detail screen expects an `items` array. The
        // LineDeltaJson column carries the snapshot at submission.
        object[]? items = null;
        if (!string.IsNullOrEmpty(v.LineDeltaJson))
        {
            try { items = Newtonsoft.Json.JsonConvert.DeserializeObject<object[]>(v.LineDeltaJson); }
            catch { items = null; }
        }

        return Ok(new
        {
            v.Id,
            number = v.Reference,
            kind = v.Kind,
            // Phase 184q — contract family.
            contractForm = v.ContractForm,
            // Phase 184o — reason / liability / EOT routed back to mobile detail.
            reason = v.Reason,
            liability = v.Liability,
            reasonDetail = v.ReasonDetail,
            eotDays = v.EotDays,
            status = v.Status,
            title = v.Title,
            description = v.Description,
            totalValue = v.NetValue,
            currency = v.Currency,
            instructionDate = v.SubmittedAt ?? v.CreatedAt,
            approvalDate = v.ApprovedAt,
            approvedBy = v.ApprovedBy,
            issuedBy = v.CreatedBy,
            items = items ?? Array.Empty<object>()
        });
    }

    // ── Payment certificates (Phase 184k / P5.1 + P7 mobile) ──────────────

    [HttpGet("payment-certs")]
    public async Task<ActionResult> GetPaymentCerts(Guid projectId)
    {
        var tenantId = GetTenantId();
        var certs = await _db.PaymentCertificates
            .Where(c => c.ProjectId == projectId && c.TenantId == tenantId)
            .OrderByDescending(c => c.ValuationDate)
            .Select(c => new
            {
                c.Id, c.CertNumber, c.ContractRef, c.Form, c.Status,
                c.Currency, c.GrossValuation, c.RetentionAmount,
                c.TotalPayable, c.ValuationDate
            })
            .ToListAsync();
        return Ok(certs);
    }

    [HttpGet("payment-certs/{id}")]
    public async Task<ActionResult> GetPaymentCert(Guid projectId, Guid id)
    {
        var tenantId = GetTenantId();
        var c = await _db.PaymentCertificates
            .FirstOrDefaultAsync(x => x.Id == id && x.ProjectId == projectId && x.TenantId == tenantId);
        if (c is null) return NotFound();

        object[]? lines = null;
        if (!string.IsNullOrEmpty(c.SovJson))
        {
            try { lines = Newtonsoft.Json.JsonConvert.DeserializeObject<object[]>(c.SovJson); }
            catch { lines = null; }
        }

        return Ok(new
        {
            c.Id, c.CertNumber, c.ContractRef, c.Form, c.Status,
            c.Currency, c.ContractorName, c.EmployerName, c.ProjectName,
            c.GrossValuation, c.EffectiveRetentionPercent, c.RetentionAmount,
            c.OtherDeductions, c.NetThisCert, c.VatPercent, c.VatAmount,
            c.TotalPayable, c.ValuationDate,
            lines = lines ?? Array.Empty<object>(),
            c.SignedByContractor, c.ContractorSignedDate,
            c.SignedByEmployer, c.EmployerSignedDate
        });
    }

    [HttpPost("payment-certs")]
    public async Task<ActionResult> CreatePaymentCert(Guid projectId,
        [FromBody] CreatePaymentCertRequest req)
    {
        var tenantId = GetTenantId();
        var exists = await _db.PaymentCertificates.AnyAsync(c =>
            c.ProjectId == projectId && c.TenantId == tenantId &&
            c.ContractRef == req.ContractRef && c.CertNumber == req.CertNumber);
        if (exists)
            return Conflict($"Cert #{req.CertNumber} already exists for contract '{req.ContractRef}'.");

        var c = new PaymentCertificate
        {
            TenantId = tenantId,
            ProjectId = projectId,
            CertNumber = req.CertNumber,
            ContractRef = req.ContractRef,
            Form = req.Form ?? "NEC4",
            Status = "Draft",
            ValuationDate = req.ValuationDate ?? DateTime.UtcNow,
            Currency = req.Currency ?? "GBP",
            ContractorName = req.ContractorName ?? "",
            EmployerName = req.EmployerName ?? "",
            ProjectName = req.ProjectName ?? "",
            RetentionPercent = req.RetentionPercent ?? 3.0m,
            EffectiveRetentionPercent = req.EffectiveRetentionPercent ?? req.RetentionPercent ?? 3.0m,
            HalfRetentionAtPercent = req.HalfRetentionAtPercent ?? 100.0m,
            VatPercent = req.VatPercent ?? 20.0m,
            GrossValuation = req.GrossValuation,
            RetentionAmount = req.RetentionAmount,
            OtherDeductions = req.OtherDeductions,
            NetThisCert = req.NetThisCert,
            VatAmount = req.VatAmount,
            TotalPayable = req.TotalPayable,
            SovJson = req.SovJson,
            CreatedBy = User.Identity?.Name
        };
        _db.PaymentCertificates.Add(c);
        await _db.SaveChangesAsync();
        return Ok(c);
    }

    [HttpPut("payment-certs/{id}/sign")]
    public async Task<ActionResult> SignPaymentCert(Guid projectId, Guid id,
        [FromBody] SignPaymentCertRequest req)
    {
        var tenantId = GetTenantId();
        var c = await _db.PaymentCertificates
            .FirstOrDefaultAsync(x => x.Id == id && x.ProjectId == projectId && x.TenantId == tenantId);
        if (c is null) return NotFound();

        var action = (req.Action ?? "").ToLowerInvariant();
        var signer = string.IsNullOrEmpty(req.SignerName) ? User.Identity?.Name ?? "" : req.SignerName!;

        // Signature PNG handling — if the client supplied a graphical
        // signature, persist it via IFileStorageService so the same
        // call works against the local filesystem in dev + S3 / MinIO
        // in production without controller-level branching.
        // SaveScopedAsync returns a tenant-prefixed storage path
        // (t_{tenantId}/{projectId}/signatures/...) which is what the
        // download / presign endpoints expect.
        string? signaturePath = null;
        if (!string.IsNullOrEmpty(req.SignaturePngBase64))
        {
            try
            {
                byte[] pngBytes = Convert.FromBase64String(req.SignaturePngBase64);
                string fileName = $"signatures/cert_{c.Id:N}_{action}_{DateTime.UtcNow:yyyyMMdd_HHmmss}.png";
                using var ms = new System.IO.MemoryStream(pngBytes);
                signaturePath = await _storage.SaveScopedAsync(
                    tenantId, c.ProjectId, fileName, ms);
                _logger.LogInformation(
                    "Payment cert {Id} signature persisted to {Path} ({Bytes} bytes) via {StorageType}",
                    c.Id, signaturePath, pngBytes.Length, _storage.GetType().Name);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to persist signature PNG for cert {Id}", c.Id);
            }
        }

        // Compose the Note field with rationale + signature path so the
        // mobile UI's offline-replay path can read both back.
        string composedNote = string.Join(" | ",
            new[] {
                string.IsNullOrEmpty(req.Rationale) ? null : "Rationale: " + req.Rationale,
                signaturePath == null ? null : "Signature: " + signaturePath
            }.Where(s => !string.IsNullOrEmpty(s)));

        if (action == "agree")
        {
            // Contractor agrees. State: Issued → Agreed.
            if (c.Status != "Issued") return Conflict($"Cert must be in 'Issued' to be agreed; currently '{c.Status}'.");
            c.Status = "Agreed";
            c.SignedByContractor = signer;
            c.ContractorSignedDate = DateTime.UtcNow;
            if (!string.IsNullOrEmpty(composedNote)) c.Note = composedNote;
        }
        else if (action == "dispute")
        {
            // Contractor disputes. State: Issued → Disputed; rationale stored.
            if (c.Status != "Issued") return Conflict($"Cert must be in 'Issued' to be disputed; currently '{c.Status}'.");
            c.Status = "Disputed";
            c.SignedByContractor = signer;
            c.ContractorSignedDate = DateTime.UtcNow;
            c.Note = composedNote ?? req.Rationale;
        }
        else if (action == "issue")
        {
            // Employer issues. State: Draft → Issued.
            if (c.Status != "Draft") return Conflict($"Cert must be in 'Draft' to be issued; currently '{c.Status}'.");
            c.Status = "Issued";
            c.IssuedDate = DateTime.UtcNow;
            c.SignedByEmployer = signer;
            c.EmployerSignedDate = DateTime.UtcNow;
        }
        else if (action == "pay")
        {
            if (c.Status != "Agreed") return Conflict($"Cert must be in 'Agreed' to be paid; currently '{c.Status}'.");
            c.Status = "Paid";
        }
        else
        {
            return BadRequest($"Unknown action '{req.Action}'. Allowed: issue, agree, dispute, pay.");
        }

        c.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return Ok(c);
    }

    // ── Work Packages ─────────────────────────────────────────────────────

    [HttpGet("work-packages")]
    public async Task<ActionResult> GetWorkPackages(Guid projectId)
    {
        var tenantId = GetTenantId();
        var wps = await _db.WorkPackages
            .Where(w => w.ProjectId == projectId && w.TenantId == tenantId)
            .OrderBy(w => w.Code)
            .ToListAsync();
        return Ok(wps);
    }

    [HttpPost("work-packages")]
    public async Task<ActionResult> CreateWorkPackage(Guid projectId, [FromBody] CreateWorkPackageRequest req)
    {
        var tenantId = GetTenantId();
        var exists = await _db.WorkPackages
            .AnyAsync(w => w.ProjectId == projectId && w.Code == req.Code && w.TenantId == tenantId);
        if (exists) return Conflict($"Work package code '{req.Code}' already exists.");

        var wp = new WorkPackage
        {
            TenantId       = tenantId,
            ProjectId      = projectId,
            Code           = req.Code,
            Name           = req.Name,
            Discipline     = req.Discipline ?? "",
            SectionPrefixesJson = req.SectionPrefixesJson ?? "[]",
            Contractor     = req.Contractor,
            EstimatedValue = req.EstimatedValue,
            Status         = "Pending",
        };
        _db.WorkPackages.Add(wp);
        await _db.SaveChangesAsync();
        return Ok(wp);
    }

    // ── Preliminaries ─────────────────────────────────────────────────────

    [HttpGet("documents/{docId}/preliminaries")]
    public async Task<ActionResult> GetPreliminaries(Guid projectId, Guid docId)
    {
        var tenantId = GetTenantId();
        var items = await _db.Nrm2PreliminariesItems
            .Where(p => p.BoqDocumentId == docId && p.ProjectId == projectId && p.TenantId == tenantId)
            .OrderBy(p => p.SortOrder)
            .ToListAsync();
        return Ok(items);
    }

    [HttpPost("documents/{docId}/preliminaries")]
    public async Task<ActionResult> AddPreliminaryItem(Guid projectId, Guid docId, [FromBody] CreatePreliminaryRequest req)
    {
        var tenantId = GetTenantId();
        var item = new Nrm2PreliminariesItem
        {
            TenantId      = tenantId,
            ProjectId     = projectId,
            BoqDocumentId = docId,
            Code          = req.Code,
            Description   = req.Description,
            Kind          = req.Kind ?? "Fixed",
            DurationWeeks = req.DurationWeeks,
            PercentageBase = req.PercentageBase,
            SortOrder     = req.SortOrder,
        };
        _db.Nrm2PreliminariesItems.Add(item);
        await _db.SaveChangesAsync();
        return Ok(item);
    }

    // ── Summary ───────────────────────────────────────────────────────────

    [HttpGet("baselines/{baselineId}/summary")]
    public async Task<ActionResult> GetBaselineSummary(Guid projectId, Guid baselineId)
    {
        var tenantId = GetTenantId();
        var baseline = await _db.BoqBaselines
            .FirstOrDefaultAsync(b => b.Id == baselineId && b.ProjectId == projectId && b.TenantId == tenantId);
        if (baseline is null) return NotFound();

        var bySection = await _db.QuantityLines
            .Where(l => l.BaselineId == baselineId && l.TenantId == tenantId)
            .GroupBy(l => l.SectionCode)
            .Select(g => new
            {
                SectionCode = g.Key,
                ItemCount   = g.Count(),
                NetTotal    = g.Sum(l => l.LineTotal),
                EmbodiedCarbon = g.Sum(l => l.EmbodiedCarbonTotal),
            })
            .OrderBy(s => s.SectionCode)
            .ToListAsync();

        return Ok(new
        {
            baseline.Id,
            baseline.Name,
            baseline.Kind,
            baseline.TotalValue,
            baseline.Currency,
            baseline.BaselinedAt,
            sections = bySection
        });
    }
}

// ── Request DTOs ──────────────────────────────────────────────────────────

public record CreateBoqDocumentRequest(
    string Name,
    string? ClientName,
    string? Architect,
    string? ContractForm,
    string? InsuranceParticulars,
    decimal? DayworkLabourPct,
    decimal? DayworkMaterialsPct,
    decimal? DayworkPlantPct,
    decimal? LocationFactor,
    string? PricingBasis,
    string? Currency,
    Guid PrimaryClassificationSystemId,
    Guid? SecondaryClassificationSystemId);

public record CreateBaselineRequest(string Name, string? Kind, string? Currency, string? Description);

public record UpsertQuantityLineRequest(
    Guid ClassificationCodeId,
    Guid? TakeoffRuleId,
    Guid? WorkPackageId,
    Guid? ProjectModelId,
    string? IfcGlobalId,
    string? IfcType,
    string? RevitElementId,
    string? Level,
    string? Zone,
    string? SectionCode,
    string? ItemDescription,
    string? Unit,
    double NetQuantity,
    double WastePercent,
    decimal? UnitRate,
    string? Currency,
    string? LineKind,
    string? PricingBasis,
    double? EmbodiedCarbonPerUnit);

public record CreateVariationRequest(
    Guid BaselineId,
    string Reference,
    string Title,
    string? Description,
    string? Kind,
    decimal NetValue,
    string? Currency,
    Guid? BimIssueId,
    // Phase 184o
    string? Reason,
    string? Liability,
    string? ReasonDetail,
    int? EotDays,
    // Phase 184q
    string? ContractForm);

public record UpdateStatusRequest(string Status);

public record CreatePaymentCertRequest(
    int CertNumber,
    string ContractRef,
    string? Form,
    DateTime? ValuationDate,
    string? Currency,
    string? ContractorName,
    string? EmployerName,
    string? ProjectName,
    decimal? RetentionPercent,
    decimal? EffectiveRetentionPercent,
    decimal? HalfRetentionAtPercent,
    decimal? VatPercent,
    decimal GrossValuation,
    decimal RetentionAmount,
    decimal OtherDeductions,
    decimal NetThisCert,
    decimal VatAmount,
    decimal TotalPayable,
    string? SovJson);

// SignaturePngBase64: Base64-encoded PNG of the captured signature (from
// react-native-signature-canvas). Optional -- typed-name signing without
// a graphical signature is still permitted on desktop.
// SignaturePngBase64: Base64-encoded PNG of the captured signature (from
// react-native-signature-canvas). Optional -- typed-name signing without
// a graphical signature is still permitted on desktop.
public record SignPaymentCertRequest(
    string Action,
    string? SignerName,
    string? Rationale,
    string? SignaturePngBase64);

public record CreateWorkPackageRequest(
    string Code,
    string Name,
    string? Discipline,
    string? SectionPrefixesJson,
    string? Contractor,
    decimal? EstimatedValue);

public record CreatePreliminaryRequest(
    string Code,
    string Description,
    string? Kind,
    int? DurationWeeks,
    decimal? PercentageBase,
    int SortOrder);
