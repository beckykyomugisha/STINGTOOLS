using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Planscape.Core.Entities;

namespace Planscape.Infrastructure.SignalR;

/// <summary>
/// Pillar B (5A) — live twin state to the viewer (K3 overlay) and the mobile
/// Live tab. On each ingest batch the server pushes the touched twins'
/// health + a ready-to-render ViewerOverlayProfile; alerts push too (6A).
///
/// Client usage:
///   conn.on("TwinOverlay", profile => STING_VIEWER.applyOverlay(profile)); // K3
///   conn.on("TwinState",   twins   => liveTab.update(twins));
///   conn.on("TwinAlert",   alert   => pushBanner(alert));
/// </summary>
[Authorize]
public class TwinHub : Hub
{
    private static string Group(string projectId) => $"twin:{projectId}";

    public Task JoinProject(string projectId)
        => Groups.AddToGroupAsync(Context.ConnectionId, Group(projectId));

    public Task LeaveProject(string projectId)
        => Groups.RemoveFromGroupAsync(Context.ConnectionId, Group(projectId));

    public static Task NotifyState(IHubContext<TwinHub> hub, Guid projectId, object twins)
        => hub.Clients.Group(Group(projectId.ToString())).SendAsync("TwinState", twins);

    public static Task NotifyOverlay(IHubContext<TwinHub> hub, Guid projectId, object overlayProfile)
        => hub.Clients.Group(Group(projectId.ToString())).SendAsync("TwinOverlay", overlayProfile);

    public static Task NotifyAlert(IHubContext<TwinHub> hub, Guid projectId, object alert)
        => hub.Clients.Group(Group(projectId.ToString())).SendAsync("TwinAlert", alert);
}

/// <summary>
/// Builds a K3 ViewerOverlayProfile from twin health — the twin feed is just
/// another overlay source, rendered by the same applyOverlay() path.
/// </summary>
public static class TwinOverlayBuilder
{
    public const string Green = "#27ae60", Amber = "#f39c12", Red = "#e74c3c",
                        Grey = "#888888", Dark = "#555555";

    public static string ColorFor(string healthState) => healthState switch
    {
        "OK"       => Green,
        "WARNING"  => Amber,
        "ALARM"    => Red,
        "OFFLINE"  => Grey,
        _          => Dark,
    };

    public static object Build(IEnumerable<DeviceTwin> twins)
    {
        var map = new Dictionary<string, string>();
        foreach (var t in twins)
            if (!string.IsNullOrWhiteSpace(t.IfcGlobalId))
                map[t.IfcGlobalId!] = ColorFor(t.HealthState);

        return new
        {
            source = "twin",
            mode = "map",
            title = "Building live",
            guidColorMap = map,
            defaultColor = Dark,
            legend = new[]
            {
                new { label = "OK", color = Green },
                new { label = "Warning", color = Amber },
                new { label = "Alarm", color = Red },
                new { label = "Offline", color = Grey },
                new { label = "No telemetry", color = Dark },
            },
        };
    }
}
