using System;

namespace Planscape.Core.Entities;

/// <summary>
/// Server-side dedupe record for offline-replay safety. The mobile offline
/// queue stamps every replayable write with a stable <c>X-Idempotency-Key</c>
/// header; controllers record (TenantId, Scope, Key) → ResultId here so a
/// retried action (at-least-once delivery) resolves to the SAME result instead
/// of creating a duplicate. Backs Prompt 18 "build idempotency" for
/// issue create/update and meeting-action add.
/// </summary>
public class IdempotencyRecord : ITenantScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }

    /// <summary>Endpoint scope, e.g. "issue.create" / "issue.update" / "meeting.action".</summary>
    public string Scope { get; set; } = "";

    /// <summary>Client-supplied idempotency key (unique per tenant+scope).</summary>
    public string Key { get; set; } = "";

    /// <summary>Id of the entity created/affected by the original request.</summary>
    public Guid ResultId { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
