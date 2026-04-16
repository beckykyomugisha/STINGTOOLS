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
builder.Services.AddHttpClient("FCM");
if (!string.IsNullOrEmpty(builder.Configuration["Firebase:ProjectId"]))
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

// ── CORS for web dashboard ──
builder.Services.AddCors(options =>
{
    options.AddPolicy("Dashboard", policy => policy
        .WithOrigins(builder.Configuration.GetSection("Cors:Origins").Get<string[]>() ?? new[] { "http://localhost:3000" })
        .AllowAnyMethod()
        .AllowAnyHeader()
        .AllowCredentials());
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
app.UseAuthentication();
app.UseMiddleware<TenantResolutionMiddleware>(); // Must run AFTER auth so JWT claims are available
app.UseMiddleware<MobileContextMiddleware>();
app.UseAuthorization();

app.UseHangfireDashboard("/hangfire", new DashboardOptions
{
    Authorization = new[] { new Planscape.Infrastructure.Services.HangfireAuthorizationFilter() }
});

// ── Health check ──
app.MapGet("/health", () => Results.Ok(new { status = "healthy", timestamp = DateTime.UtcNow, version = "1.0.0" }));

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
