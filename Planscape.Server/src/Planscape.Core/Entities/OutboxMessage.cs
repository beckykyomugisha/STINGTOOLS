namespace Planscape.Core.Entities;

/// <summary>
/// S3.2 — transactional outbox row. Domain handlers persist messages here
/// inside the same DB transaction as the state change (issue created,
/// invoice paid, …). A Hangfire worker drains the outbox every minute and
/// dispatches to SignalR / push / webhook / email with at-least-once
/// semantics. Decouples write durability from notification reliability.
/// </summary>
public class OutboxMessage : ITenantScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }

    /// <summary>"signalr" | "push" | "email" | "webhook" — routes to the dispatcher.</summary>
    public string Channel { get; set; } = "";

    /// <summary>Message type — e.g. "issue.created", "invoice.paid".</summary>
    public string Topic { get; set; } = "";

    /// <summary>JSON payload the dispatcher decodes per channel.</summary>
    public string PayloadJson { get; set; } = "{}";

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? DispatchedAt { get; set; }
    public int Attempts { get; set; }
    public DateTime? LastAttemptAt { get; set; }
    public string? LastError { get; set; }
    public DateTime? NextAttemptAt { get; set; }

    public OutboxStatus Status { get; set; } = OutboxStatus.Pending;
}

public enum OutboxStatus
{
    Pending = 0,
    Dispatched = 1,
    DeadLettered = 2,
}
