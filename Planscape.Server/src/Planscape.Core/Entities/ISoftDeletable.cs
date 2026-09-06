namespace Planscape.Core.Entities;

/// <summary>
/// Marks an entity that is tombstoned rather than hard-deleted.
///
/// <para>WHY A NULLABLE TIMESTAMP AND NOT A <c>bool IsDeleted</c>:</para>
/// <list type="bullet">
///   <item>It carries <i>when</i>, so a tombstone is an audit record rather than
///   a bare flag — needed to answer "when did this element leave the model?".</item>
///   <item>Undelete (a Revit undo restoring a deleted element — which WILL
///   happen) becomes an ordinary field update, <c>DeletedAtUtc = null</c>,
///   not a special case.</item>
///   <item><c>"DeletedAtUtc" IS NULL</c> is the cheap, index-friendly predicate
///   the global query filter needs on every read.</item>
/// </list>
///
/// <para>ENFORCEMENT: <c>PlanscapeDbContext.ApplyGlobalQueryFilters</c> AND-folds
/// <c>DeletedAtUtc == null</c> into the global query filter of every implementing
/// entity, so soft-deleted rows disappear from all LINQ reads by default. Two
/// consequences callers must know:</para>
/// <list type="number">
///   <item>Code that must SEE tombstones (the TagSync upsert, which has to find a
///   deleted row to undelete it) has to call <c>.IgnoreQueryFilters()</c>. Note
///   that in EF Core 8 this drops the tenant predicate too, so such a query must
///   carry its own ownership check.</item>
///   <item>Raw SQL bypasses query filters entirely and must spell out
///   <c>AND "DeletedAtUtc" IS NULL</c> by hand.</item>
/// </list>
/// </summary>
public interface ISoftDeletable
{
    /// <summary>
    /// UTC instant the row was tombstoned; <c>null</c> means live. Absent /
    /// null is the backward-compatible default, so rows written before this
    /// column existed read as live.
    /// </summary>
    DateTime? DeletedAtUtc { get; set; }
}
