using Planscape.Infrastructure.Data;
using Planscape.Infrastructure.SignalR;
using Planscape.API.Middleware;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using System.Threading.RateLimiting;
using Serilog;
using Hangfire;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);

// ── Serilog ──
builder.Host.UseSerilog((ctx, lc) => lc
    .ReadFrom.Configuration(ctx.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .WriteTo.File("logs/planscape-.log", rollingInterval: RollingInterval.Day));

// ── Database ──
builder.Services.AddDbContext<PlanscapeDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Default")));

// ── Authentication ──
var jwtKey = builder.Configuration["Jwt:Key"] ?? "Planscape-Dev-Secret-Key-Min32Chars!!";
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = builder.Configuration["Jwt:Issuer"] ?? "Planscape",
            ValidAudience = builder.Configuration["Jwt:Audience"] ?? "Planscape.Client",
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey))
        };
        // SignalR JWT support — read token from query string for WebSocket
        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                var accessToken = context.Request.Query["access_token"];
                var path = context.HttpContext.Request.Path;
                if (!string.IsNullOrEmpty(accessToken) && path.StartsWithSegments("/hubs"))
                    context.Token = accessToken;
                return Task.CompletedTask;
            }
        };
    });
builder.Services.AddAuthorization();

// ── Services ──
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<Planscape.Core.Interfaces.ITenantContext, Planscape.Infrastructure.Services.TenantContext>();
builder.Services.AddSingleton<Planscape.Core.Interfaces.IFileStorageService, Planscape.Infrastructure.Storage.LocalFileStorageService>();
builder.Services.AddScoped<Planscape.Core.Interfaces.IGeofenceValidationService, Planscape.Infrastructure.Services.GeofenceValidationService>();
builder.Services.AddScoped<Planscape.API.Services.IThumbnailService, Planscape.API.Services.ImageSharpThumbnailService>();
builder.Services.AddScoped<Planscape.API.Services.IAuditService, Planscape.API.Services.AuditService>();

// ── Platform Connectors ──
builder.Services.AddSingleton<Planscape.Core.Interfaces.IPlatformConnector, Planscape.Infrastructure.Services.AccConnector>();
builder.Services.AddSingleton<Planscape.Core.Interfaces.IPlatformConnector, Planscape.Infrastructure.Services.ProcoreConnector>();
builder.Services.AddSingleton<Planscape.Core.Interfaces.IPlatformConnector, Planscape.Infrastructure.Services.AconexConnector>();
builder.Services.AddSingleton<Planscape.Core.Interfaces.IPlatformConnector, Planscape.Infrastructure.Services.TrimbleConnector>();
builder.Services.AddSingleton<Planscape.Core.Interfaces.IPlatformConnectorFactory, Planscape.Infrastructure.Services.PlatformConnectorFactory>();

// ── Email ──
if (!string.IsNullOrEmpty(builder.Configuration["Email:Host"]))
    builder.Services.AddSingleton<Planscape.Core.Interfaces.IEmailService, Planscape.Infrastructure.Services.SmtpEmailService>();
else
    builder.Services.AddSingleton<Planscape.Core.Interfaces.IEmailService, Planscape.Infrastructure.Services.NullEmailService>();

// ── Push Notifications ──
// Supports both raw FCM tokens (via Firebase Project) and ExponentPushToken[…]
// tokens issued by the Expo/EAS runtime. ExpoPushService is always registered —
// FirebasePushService delegates to it for Expo-shaped tokens, and the service
// also lets us deliver to Expo Go + TestFlight dev builds without Firebase creds.
builder.Services.AddHttpClient("FCM");
builder.Services.AddHttpClient("Expo");
builder.Services.AddSingleton<Planscape.Infrastructure.Services.ExpoPushService>();
if (!string.IsNullOrEmpty(builder.Configuration["Firebase:ProjectId"])
    || !string.IsNullOrEmpty(builder.Configuration["Expo:AccessToken"])
    || true) // always prefer the real service — when neither is configured it silently no-ops
    builder.Services.AddSingleton<Planscape.Core.Interfaces.IPushNotificationService, Planscape.Infrastructure.Services.FirebasePushService>();
else
    builder.Services.AddSingleton<Planscape.Core.Interfaces.IPushNotificationService, Planscape.Infrastructure.Services.NullPushNotificationService>();

// ── Notifications (SignalR + Push) ──
builder.Services.AddSingleton<Planscape.Core.Interfaces.INotificationService, Planscape.Infrastructure.Services.NotificationService>();

// ── Redis ──
var redisConn = builder.Configuration["Redis:Connection"] ?? "localhost:6379";
builder.Services.AddStackExchangeRedisCache(options =>
{
    options.Configuration = redisConn;
    options.InstanceName = "Planscape:";
});
builder.Services.AddSingleton<IConnectionMultiplexer>(
    ConnectionMultiplexer.Connect(redisConn));

builder.Services.AddSignalR().AddStackExchangeRedis(redisConn, options =>
{
    options.Configuration.ChannelPrefix = RedisChannel.Literal("Planscape");
});

// ── Hangfire background jobs ──
builder.Services.AddHangfire(config => config
    .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
    .UseSimpleAssemblyNameTypeSerializer()
    .UseRecommendedSerializerSettings()
    .UsePostgreSqlStorage(o => o.UseNpgsqlConnection(
        builder.Configuration.GetConnectionString("Default"))));
builder.Services.AddHangfireServer(options =>
{
    options.WorkerCount = 2;
    options.Queues = new[] { "default", "compliance", "notifications", "platform-sync" };
});
builder.Services.AddScoped<Planscape.Infrastructure.Services.ComplianceCheckJob>();
builder.Services.AddScoped<Planscape.Infrastructure.Services.SlaEscalationJob>();
builder.Services.AddScoped<Planscape.Infrastructure.Services.StaleWarningCleanupJob>();
builder.Services.AddScoped<Planscape.Infrastructure.Services.PlatformSyncJob>();

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new()
    {
        Title = "Planscape API",
        Version = "v1",
        Description = "ISO 19650-compliant BIM coordination platform — tag sync, compliance, CDE, issues, documents, meetings, workflows, and asset lifecycle management.",
        Contact = new() { Name = "Planscape", Email = "support@planscape.io" }
    });
    c.AddSecurityDefinition("Bearer", new()
    {
        Type = Microsoft.OpenApi.Models.SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        Description = "Paste a JWT obtained from POST /api/auth/login"
    });
    c.OperationFilter<Planscape.API.Swagger.SecurityRequirementFilter>();

    // Include XML documentation comments
    var xmlFile = $"{System.Reflection.Assembly.GetExecutingAssembly().GetName().Name}.xml";
    var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);
    if (File.Exists(xmlPath))
        c.IncludeXmlComments(xmlPath);
});

// ── Rate Limiting ──
// auth: 10 req/min per IP (prevents brute-force)
// api:  tier-based — configured per user after auth; global fallback 120 req/min
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    // Fixed-window for auth endpoints — 10 requests per minute per IP
    options.AddFixedWindowLimiter("auth", o =>
    {
        o.PermitLimit         = 10;
        o.Window              = TimeSpan.FromMinutes(1);
        o.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
        o.QueueLimit          = 0;
    });

    // Sliding-window for API endpoints — 120 requests per minute per IP (global fallback)
    options.AddSlidingWindowLimiter("api", o =>
    {
        o.PermitLimit         = 120;
        o.Window              = TimeSpan.FromMinutes(1);
        o.SegmentsPerWindow   = 6;  // 10-second buckets
        o.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
        o.QueueLimit          = 0;
    });

    // Tag sync: 30 req/min per IP (large payloads — be conservative)
    options.AddFixedWindowLimiter("tagsync", o =>
    {
        o.PermitLimit         = 30;
        o.Window              = TimeSpan.FromMinutes(1);
        o.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
        o.QueueLimit          = 0;
    });

    // Mobile: 120 req/min per device (partitioned by X-Device-Id header, IP fallback)
    options.AddPolicy("mobile", context =>
    {
        var deviceId = context.Request.Headers["X-Device-Id"].FirstOrDefault()
                       ?? context.Connection.RemoteIpAddress?.ToString()
                       ?? "unknown";
        return RateLimitPartition.GetFixedWindowLimiter(deviceId, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 120,
            Window = TimeSpan.FromMinutes(1),
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            QueueLimit = 0
        });
    });
});

// ── CORS ──
// Default origins cover the web dashboard plus common Expo dev surfaces
// (Metro 19000-19006 and tunnelled exp:// scheme). Override via Cors:Origins in config.
var corsOrigins = builder.Configuration.GetSection("Cors:Origins").Get<string[]>() ?? new[]
{
    "http://localhost:3000",
    "http://localhost:19000",
    "http://localhost:19001",
    "http://localhost:19002",
    "http://localhost:19006",
    "exp://localhost:19000",
    "exp://localhost:8081",
};
builder.Services.AddCors(options =>
{
    // Dashboard policy keeps credentials allowed for the cookie-based web app.
    options.AddPolicy("Dashboard", policy => policy
        .WithOrigins(corsOrigins)
        .AllowAnyMethod()
        .AllowAnyHeader()
        .AllowCredentials());

    // NEW-SRV-21: Mobile policy is permissive on origin (Expo Go uses dynamic LAN IPs)
    // but does not allow credentials, since mobile uses Bearer tokens not cookies.
    options.AddPolicy("Mobile", policy => policy
        .SetIsOriginAllowed(origin =>
        {
            if (string.IsNullOrEmpty(origin)) return true;
            return origin.StartsWith("exp://", StringComparison.OrdinalIgnoreCase)
                || origin.StartsWith("http://localhost", StringComparison.OrdinalIgnoreCase)
                || origin.StartsWith("http://192.168.", StringComparison.OrdinalIgnoreCase)
                || origin.StartsWith("http://10.", StringComparison.OrdinalIgnoreCase)
                || origin.StartsWith("http://172.", StringComparison.OrdinalIgnoreCase);
        })
        .AllowAnyMethod()
        .AllowAnyHeader());
});

var app = builder.Build();

// ── Pipeline ──
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseSerilogRequestLogging();
app.UseRateLimiter();
app.UseCors("Dashboard");
app.UseCors("Mobile");
app.UseAuthentication();
app.UseMiddleware<TenantResolutionMiddleware>(); // Must run AFTER auth so JWT claims are available
app.UseMiddleware<MobileContextMiddleware>();
app.UseAuthorization();

app.UseHangfireDashboard("/hangfire", new DashboardOptions
{
    Authorization = new[] { new Planscape.Infrastructure.Services.HangfireAuthorizationFilter() }
});

// ── Health check ── (NEW-SRV-22)
// Returns sub-check results so mobile can detect partial degradation.
// Status codes: 200 healthy, 503 degraded (any sub-check failed).
app.MapGet("/health", async (PlanscapeDbContext db, IConnectionMultiplexer? redis, Planscape.Core.Interfaces.IPushNotificationService push) =>
{
    var checks = new Dictionary<string, object>();
    var anyFailure = false;

    // Database
    try
    {
        var canConnect = await db.Database.CanConnectAsync();
        checks["database"] = new { healthy = canConnect, provider = "postgres" };
        if (!canConnect) anyFailure = true;
    }
    catch (Exception ex)
    {
        checks["database"] = new { healthy = false, error = ex.Message };
        anyFailure = true;
    }

    // Redis
    try
    {
        if (redis == null) { checks["redis"] = new { healthy = false, error = "not configured" }; anyFailure = true; }
        else
        {
            var pong = await redis.GetDatabase().PingAsync();
            checks["redis"] = new { healthy = true, pingMs = pong.TotalMilliseconds };
        }
    }
    catch (Exception ex)
    {
        checks["redis"] = new { healthy = false, error = ex.Message };
        anyFailure = true;
    }

    // Push provider — non-blocking; just report which implementation is wired
    checks["push"] = new
    {
        healthy = true,
        implementation = push.GetType().Name,
    };

    var body = new
    {
        status = anyFailure ? "degraded" : "healthy",
        timestamp = DateTime.UtcNow,
        version = "1.0.0",
        checks,
    };
    return anyFailure ? Results.Json(body, statusCode: 503) : Results.Ok(body);
});

// ── Mobile crash diagnostics endpoint ── (supports NEW-MOB-16 crash reporter)
// Accepts JSON crash entries; logs through Serilog and stores nothing else.
app.MapPost("/api/diagnostics/crash", async (HttpContext ctx, Microsoft.Extensions.Logging.ILoggerFactory lf) =>
{
    var logger = lf.CreateLogger("MobileDiagnostics");
    using var reader = new StreamReader(ctx.Request.Body);
    var body = await reader.ReadToEndAsync();
    if (body.Length > 64 * 1024)
    {
        return Results.BadRequest(new { error = "payload too large" });
    }
    logger.LogWarning("[mobile-crash] {Body}", body);
    return Results.Accepted();
}).RequireRateLimiting("mobile");

app.MapControllers();
app.MapHub<ComplianceHub>("/hubs/compliance");
app.MapHub<TagSyncHub>("/hubs/tagsync");
app.MapHub<NotificationHub>("/hubs/notifications");

// ── Database migration + seed ──
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<PlanscapeDbContext>();
    if (app.Environment.IsDevelopment())
    {
        // In development, auto-migrate (apply pending migrations or create DB)
        db.Database.Migrate();
        await Planscape.API.SeedData.SeedAsync(db);
    }
    else
    {
        // In production, only apply pending migrations — no seed data
        db.Database.Migrate();
    }
}

// ── Recurring background jobs ──
RecurringJob.AddOrUpdate<Planscape.Infrastructure.Services.ComplianceCheckJob>(
    "compliance-snapshot", j => j.ExecuteAsync(CancellationToken.None),
    Cron.Hourly, new RecurringJobOptions { QueueName = "compliance" });
RecurringJob.AddOrUpdate<Planscape.Infrastructure.Services.SlaEscalationJob>(
    "sla-escalation", j => j.ExecuteAsync(CancellationToken.None),
    "*/15 * * * *", new RecurringJobOptions { QueueName = "default" });
RecurringJob.AddOrUpdate<Planscape.Infrastructure.Services.StaleWarningCleanupJob>(
    "stale-warning-cleanup", j => j.ExecuteAsync(CancellationToken.None),
    Cron.Daily, new RecurringJobOptions { QueueName = "default" });
RecurringJob.AddOrUpdate<Planscape.Infrastructure.Services.PlatformSyncJob>(
    "platform-sync", j => j.ExecuteAsync(CancellationToken.None),
    "*/30 * * * *", new RecurringJobOptions { QueueName = "platform-sync" });

await app.RunAsync();
