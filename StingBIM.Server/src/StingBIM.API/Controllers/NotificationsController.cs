using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using StingBIM.Infrastructure.Data;

namespace StingBIM.API.Controllers;

/// <summary>
/// Mobile push notification token registration.
/// Stores FCM (Android) or APNs (iOS) device tokens on the user record.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class NotificationsController : ControllerBase
{
    private readonly StingBimDbContext _db;
    public NotificationsController(StingBimDbContext db) => _db = db;

    /// <summary>Register or update push notification token for current device.</summary>
    [HttpPost("register")]
    public async Task<ActionResult> RegisterToken([FromBody] PushTokenRequest req)
    {
        var userId = Guid.TryParse(
            User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
            ?? User.FindFirst("sub")?.Value, out var id) ? id : Guid.Empty;

        var user = await _db.Users.FindAsync(userId);
        if (user == null) return NotFound();

        user.PushToken          = req.Token;
        user.PushPlatform       = req.Platform;
        user.PushTokenUpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        return Ok(new { message = "Push token registered." });
    }

    /// <summary>Remove push token (user logged out of device).</summary>
    [HttpDelete("token")]
    public async Task<ActionResult> RemoveToken()
    {
        var userId = Guid.TryParse(
            User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
            ?? User.FindFirst("sub")?.Value, out var id) ? id : Guid.Empty;

        var user = await _db.Users.FindAsync(userId);
        if (user == null) return NotFound();

        user.PushToken    = null;
        user.PushPlatform = null;
        await _db.SaveChangesAsync();
        return Ok();
    }
}

public record PushTokenRequest
{
    public string Token    { get; init; } = "";
    public string Platform { get; init; } = "android"; // "android" | "ios" | "web"
}
