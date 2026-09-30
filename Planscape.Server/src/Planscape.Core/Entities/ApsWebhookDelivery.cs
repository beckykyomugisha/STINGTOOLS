namespace Planscape.Core.Entities;

/// <summary>
/// One APS webhook delivery claimed for processing (<c>x-adsk-delivery-id</c>).
/// The primary key IS the claim: inserting a row that already exists does
/// nothing, so exactly one of two concurrent deliveries with the same id wins
/// (ROADMAP ACC-SRV-7). Rows older than the retention window are pruned by
/// <c>ApsWebhookDeliveryGuard</c>.
///
/// NOT tenant-scoped: the receiver is anonymous and the claim is taken before
/// the delivery is matched to a connection. The row holds no tenant data —
/// only the opaque delivery id and when it arrived.
/// </summary>
public class ApsWebhookDelivery
{
    public string DeliveryId { get; set; } = "";
    public DateTime ReceivedAt { get; set; }
}
