using Planscape.Core.Entities;

namespace Planscape.Core.Interfaces;

/// <summary>
/// Pillar B/C (6A) — turns a twin alert into a WorkOrder and emits it onto the
/// K2 spine (type "workorder.created") so it can pin a 3D marker / notify
/// mobile. One automator, no bespoke channel.
/// </summary>
public interface IWorkOrderAutomator
{
    Task<WorkOrder> RaiseFromAlertAsync(Guid projectId, TwinAlert alert, CancellationToken ct = default);
}
