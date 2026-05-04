using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Planscape.Core.Entities;
using Planscape.Core.Interfaces;
using Planscape.Infrastructure.Data;

namespace Planscape.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class NotificationsController : ControllerBase
{
    private readonly IPushNotificationService _pushService;
    private readonly PlanscapeDbContext _db;

    public NotificationsController(IPushNotificationService pushService, PlanscapeDbContext db)
    {
        _pushService = pushService;
        _db = db;
    }

    /// <summary>
    /// Register a device push token for the authenticated user.
    /// POST /api/notifications/subscribe
    /// </summary>
    [HttpPost("subscribe")]
    public async Task<ActionResult> Subscribe([FromBody] SubscribeRequest req)
    {
        var userId = GetUserId();
        var tenantId = GetTenantId();
        if (userId == Guid.Empty) return Unauthorized();

        if (string.IsNullOrWhiteSpace(req.Token))
            return BadRequest(new { message = "Token is required" });

        await _pushService.RegisterTokenAsync(userId, tenantId, req.Token, req.Platform ?? "FCM", req.DeviceName);
        return Ok(new { message = "Device registered for push notifications" });
    }

    /// <summary>
    /// Unregister a device push token.
    /// POST /api/notifications/unsubscribe
    /// </summary>
    [HttpPost("unsubscribe")]
    public async Task<ActionResult> Unsubscribe([FromBody] UnsubscribeRequest req)
    {
        var userId = GetUserId();
        if (userId == Guid.Empty) return Unauthorized();

        await _pushService.UnregisterTokenAsync(userId, req.Token);
        return Ok(new { message = "Device unregistered" });
    }

    /// <summary>
    /// List registered devices for the authenticated user.
    /// GET /api/notifications/devices
    /// </summary>
    [HttpGet("devices")]
    public async Task<ActionResult> GetDevices()
    {
        var userId = GetUserId();
        if (userId == Guid.Empty) return Unauthorized();

        var devices = await _db.DevicePushTokens
            .Where(d => d.UserId == userId)
            .Select(d => new
            {
                d.Id,
                d.Platform,
                d.DeviceName,
                d.CreatedAt,
                d.LastUsedAt,
                tokenPreview = d.Token.Length > 10 ? d.Token.Substring(0, 10) + "..." : d.Token
            })
            .OrderByDescending(d => d.LastUsedAt)
            .ToListAsync();

        return Ok(new { devices });
    }

    /// <summary>
    /// Send a test push notification to the authenticated user's devices.
    /// POST /api/notifications/test
    /// </summary>
    [HttpPost("test")]
    public async Task<ActionResult> TestPush()
    {
        var userId = GetUserId();
        if (userId == Guid.Empty) return Unauthorized();

        await _pushService.SendToUserAsync(userId, new PushPayload
        {
            Title = "Planscape Test",
            Body = "Push notifications are working!",
            Channel = "test",
            Data = new Dictionary<string, string> { ["type"] = "test" }
        });

        return Ok(new { message = "Test notification sent" });
    }

    private Guid GetUserId() =>
        Guid.TryParse(User.FindFirst("sub")?.Value, out var id) ? id : Guid.Empty;

    private Guid GetTenantId() =>
        Guid.TryParse(User.FindFirst("tenant_id")?.Value, out var id) ? id : Guid.Empty;
}

public record SubscribeRequest(string Token, string? Platform, string? DeviceName);
public record UnsubscribeRequest(string Token);
