namespace Planscape.Core.Entities;

/// <summary>
/// The header record for a complete NRM2-format BOQ document. Holds the
/// project particulars, form of tender, and meta-data that frames the
/// measured items. Each project typically has one live
/// <see cref="BoqDocument"/> per contract scope.
///
/// The document is the bridge between the data layer (QuantityLines,
/// Preambles, WorkPackages) and the rendered output (Excel, PDF,
/// IFC-quantities).
/// </summary>
public class BoqDocument : ITenantScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid ProjectId { get; set; }

    public string Name { get; set; } = "";
    public string? Description { get; set; }

    /// <summary>Primary classification system — usually NRM2.</summary>
    public Guid PrimaryClassificationSystemId { get; set; }

    /// <summary>Optional secondary system shown in parallel columns (Uniclass).</summary>
    public Guid? SecondaryClassificationSystemId { get; set; }

    public string MeasurementProtocol { get; set; } = "NRM2_RULES";
    public string Currency { get; set; } = "GBP";
    public string? VatTreatment { get; set; }

    // ── Project particulars (NRM2 §A) ──

    public string? ClientName { get; set; }
    public string? Architect { get; set; }
    public string? StructuralEngineer { get; set; }
    public string? MepEngineer { get; set; }
    public string? CostManager { get; set; }
    public string? PrincipalContractor { get; set; }

    /// <summary>Contract form — "JCT D&B 2016", "JCT SBC/Q 2016", "NEC4 ECC Option A", "FIDIC Red Book".</summary>
    public string? ContractForm { get; set; }

    /// <summary>Insurance requirements summary.</summary>
    public string? InsuranceParticulars { get; set; }

    /// <summary>Daywork percentage additions per NRM2 (Labour / Materials / Plant).</summary>
    public decimal? DayworkLabourPct { get; set; }
    public decimal? DayworkMaterialsPct { get; set; }
    public decimal? DayworkPlantPct { get; set; }

    /// <summary>BCIS regional location factor (1.00 = UK average).</summary>
    public decimal? LocationFactor { get; set; }

    /// <summary>JCT/NEC remeasurement basis — Lump / Remeasured / TargetCost / CostPlus.</summary>
    public string PricingBasis { get; set; } = "Lump";

    /// <summary>Status: Draft / IssuedForTender / TenderReturned / Awarded / Live / FinalAccount / Closed.</summary>
    public string Status { get; set; } = "Draft";

    public string? Revision { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public string? CreatedBy { get; set; }

    public Project? Project { get; set; }
    public ClassificationSystem? PrimaryClassificationSystem { get; set; }
    public ClassificationSystem? SecondaryClassificationSystem { get; set; }
}

/// <summary>
/// NRM2 Section A preliminaries item — site establishment, time-related
/// charges, method-related charges. Not derived from the BIM model;
/// authored from a template library and priced per contract.
/// </summary>
public class Nrm2PreliminariesItem : ITenantScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid ProjectId { get; set; }
    public Guid BoqDocumentId { get; set; }

    public string Code { get; set; } = "";
    public string Description { get; set; } = "";

    /// <summary>"Fixed" / "TimeRelated" / "MethodRelated" / "Percentage".</summary>
    public string Kind { get; set; } = "Fixed";

    public string Unit { get; set; } = "sum";
    public double Quantity { get; set; } = 1;

    public decimal? UnitRate { get; set; }
    public decimal? LineTotal { get; set; }
    public string Currency { get; set; } = "GBP";

    /// <summary>For TimeRelated items — duration in weeks (links to programme).</summary>
    public int? DurationWeeks { get; set; }

    /// <summary>For Percentage items — base value to apply the percentage to.</summary>
    public decimal? PercentageBase { get; set; }
    public decimal? Percentage { get; set; }

    public int SortOrder { get; set; } = 0;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
