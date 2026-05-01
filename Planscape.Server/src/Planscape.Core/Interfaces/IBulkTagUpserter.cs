using Planscape.Core.Entities;

namespace Planscape.Core.Interfaces;

/// <summary>
/// S3.1 — fast-path bulk upsert that bypasses EF change tracking. Used by
/// <c>TagSyncController</c> when the request is large or the project has
/// no existing elements (greenfield sync). Writes via Npgsql binary COPY +
/// staging table + INSERT ... ON CONFLICT DO UPDATE so a 30k-element
/// author save lands in &lt;1 s instead of 60+ s.
/// </summary>
public interface IBulkTagUpserter
{
    Task<BulkTagUpsertResult> UpsertAsync(Guid tenantId, Guid projectId, IReadOnlyList<TaggedElement> elements, CancellationToken ct = default);
}

public sealed record BulkTagUpsertResult(int Inserted, int Updated, long ElapsedMs);
