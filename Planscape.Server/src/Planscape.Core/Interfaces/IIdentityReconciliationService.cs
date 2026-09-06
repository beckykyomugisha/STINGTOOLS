namespace Planscape.Core.Interfaces;

/// <summary>
/// R1 (Phase A, Increment 2) — dedup/backfill tool for the cross-host identity.
///
/// The same physical element can exist as TWO <c>TaggedElement</c> rows: one
/// from the Revit door (keyed on RevitElementId) and one from the /ifc/data door
/// (keyed on the IFC GlobalId in UniqueId). This service (a) backfills
/// <c>IfcGlobalId</c> onto Revit rows from <c>ExternalElementMapping</c>, then
/// (b) merges each <c>(ProjectId, IfcGlobalId)</c> group down to one row.
///
/// Deliberately human-triggered (admin endpoint) with a dry-run mode — a
/// data-mutating merge must be reviewed before it runs, and re-running is a
/// cheap no-op once clean (idempotent).
/// </summary>
public interface IIdentityReconciliationService
{
    /// <summary>Dry-run: report what a merge WOULD do; mutates nothing.</summary>
    Task<IdentityReconciliationReport> AnalyzeAsync(Guid tenantId, Guid? projectId = null, CancellationToken ct = default);

    /// <summary>Apply the backfill + merge. Idempotent.</summary>
    Task<IdentityReconciliationReport> ApplyAsync(Guid tenantId, Guid? projectId = null, CancellationToken ct = default);
}

/// <summary>Counts from an identity-reconciliation analyze/apply pass.</summary>
public sealed record IdentityReconciliationReport
{
    /// <summary>False for a dry-run (AnalyzeAsync), true for ApplyAsync.</summary>
    public bool Applied { get; init; }

    /// <summary>Revit rows (RevitElementId &gt; 0) that got an IfcGlobalId from ExternalElementMapping.</summary>
    public int RevitRowsBackfilled { get; init; }

    /// <summary>Distinct (ProjectId, IfcGlobalId) groups holding more than one row.</summary>
    public int DuplicateGroups { get; init; }

    /// <summary>Total surplus rows across those groups (rows that would be / were removed).</summary>
    public int DuplicateRows { get; init; }

    /// <summary>Rows actually removed by the merge (0 on a dry-run; equals DuplicateRows on apply).</summary>
    public int RowsMerged { get; init; }

    /// <summary>
    /// Groups that carried more than one DISTINCT RevitElementId &gt; 0 — a data
    /// anomaly (two Revit elements claiming one GlobalId). The primary keeps one;
    /// surfaced so an operator can investigate.
    /// </summary>
    public int RevitIdConflicts { get; init; }
}
