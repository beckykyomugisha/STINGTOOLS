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
    private readonly IEmailService _emailService;
    private readonly IConfiguration _config;
    private readonly ILogger<NotificationsController> _logger;

    public NotificationsController(
        IPushNotificationService pushService,
        PlanscapeDbContext db,
        IEmailService emailService,
        IConfiguration config,
        ILogger<NotificationsController> logger)
    {
        _pushService = pushService;
        _db = db;
        _emailService = emailService;
        _config = config;
        _logger = logger;
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

    /// <summary>
    /// Send a sample invite-styled email to the signed-in user's own address,
    /// through the EXACT same render path as a real invite — a true render check
    /// without having to invite a real member. Returns { sent, message, to }.
    /// POST /api/notifications/test-email
    /// </summary>
    [HttpPost("test-email")]
    public async Task<ActionResult> TestEmail(CancellationToken ct)
    {
        var userId = GetUserId();
        if (userId == Guid.Empty) return Unauthorized();

        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (user == null || string.IsNullOrWhiteSpace(user.Email))
            return BadRequest(new { sent = false, message = "Your account has no email address on file.", to = (string?)null });

        if (!_emailService.IsConfigured)
        {
            _logger.LogWarning("[email-test] requested by {Email} but SMTP not configured", user.Email);
            return Ok(new { sent = false, message = "SMTP is not configured on the server — no email was sent. Set Smtp__Host (and recreate the api container) to enable email.", to = user.Email });
        }

        // Resolve the public base URL exactly like the invite path so the link in
        // the test email matches a real invite.
        var baseUrl = Planscape.API.PublicUrl.Resolve(_config, Request);
        var displayName = string.IsNullOrWhiteSpace(user.DisplayName) ? user.Email : user.DisplayName;

        // Clearly non-functional sample token: the button renders + is clickable
        // (lands on reset-password.html) but won't validate — this is a render
        // check, not a real invitation, so it never mutates the account.
        const string sampleToken = "SAMPLE-RENDER-CHECK-TOKEN";

        try
        {
            await _emailService.SendInviteEmailAsync(
                toEmail:    user.Email,
                displayName: displayName,
                inviterName: "Planscape (test email)",
                projectName: "Sample Project",
                serverUrl:   baseUrl,
                resetToken:  sampleToken,
                projectId:   Guid.Empty,
                ct:          ct);

            _logger.LogInformation("[email-test] sent invite-styled test email to {Email}", user.Email);
            return Ok(new { sent = true, message = $"Test email sent to {user.Email}", to = user.Email });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[email-test] send failed to {Email}", user.Email);
            return StatusCode(StatusCodes.Status502BadGateway, new { sent = false, message = ex.Message, to = user.Email });
        }
    }

    // Resolve via the shared helper: the JWT carries "sub", but .NET's JWT
    // handler remaps "sub" → ClaimTypes.NameIdentifier, so FindFirst("sub")
    // returns null and every POST here would 401. ProjectVisibility.GetUserId
    // checks NameIdentifier → sub → user_id.
    private Guid GetUserId() =>
        Planscape.Infrastructure.Services.ProjectVisibility.GetUserId(User);

    private Guid GetTenantId() =>
        Guid.TryParse(User.FindFirst("tenant_id")?.Value, out var id) ? id : Guid.Empty;
}

public record SubscribeRequest(string Token, string? Platform, string? DeviceName);
public record UnsubscribeRequest(string Token);
