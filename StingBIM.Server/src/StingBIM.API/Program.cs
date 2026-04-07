using StingBIM.Infrastructure.Data;
using StingBIM.Infrastructure.SignalR;
using StingBIM.API.Middleware;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using System.Threading.RateLimiting;
using Serilog;
using Hangfire;

var builder = WebApplication.CreateBuilder(args);

// ── Serilog ──
builder.Host.UseSerilog((ctx, lc) => lc
    .ReadFrom.Configuration(ctx.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .WriteTo.File("logs/stingbim-.log", rollingInterval: RollingInterval.Day));

// ── Database ──
builder.Services.AddDbContext<StingBimDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Default")));

// ── Authentication ──
var jwtKey = builder.Configuration["Jwt:Key"] ?? "StingBIM-Dev-Secret-Key-Min32Chars!!";
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = builder.Configuration["Jwt:Issuer"] ?? "StingBIM",
            ValidAudience = builder.Configuration["Jwt:Audience"] ?? "StingBIM.Client",
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
builder.Services.AddScoped<StingBIM.Core.Interfaces.ITenantContext, StingBIM.Infrastructure.Services.TenantContext>();

// ── Email ──
if (!string.IsNullOrEmpty(builder.Configuration["Email:Host"]))
    builder.Services.AddSingleton<StingBIM.Core.Interfaces.IEmailService, StingBIM.Infrastructure.Services.SmtpEmailService>();
else
    builder.Services.AddSingleton<StingBIM.Core.Interfaces.IEmailService, StingBIM.Infrastructure.Services.NullEmailService>();

// ── Notifications ──
builder.Services.AddSingleton<StingBIM.Core.Interfaces.INotificationService, StingBIM.Infrastructure.Services.NotificationService>();

builder.Services.AddSignalR();

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
    options.Queues = new[] { "default", "compliance", "notifications" };
});
builder.Services.AddScoped<StingBIM.Infrastructure.Services.ComplianceCheckJob>();
builder.Services.AddScoped<StingBIM.Infrastructure.Services.SlaEscalationJob>();
builder.Services.AddScoped<StingBIM.Infrastructure.Services.StaleWarningCleanupJob>();

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new() { Title = "StingBIM API", Version = "v1" });
    c.AddSecurityDefinition("Bearer", new()
    {
        Type = Microsoft.OpenApi.Models.SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT"
    });
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
app.UseAuthorization();

app.UseHangfireDashboard("/hangfire", new DashboardOptions
{
    Authorization = new[] { new StingBIM.Infrastructure.Services.HangfireAuthorizationFilter() }
});

// ── Health check ──
app.MapGet("/health", () => Results.Ok(new { status = "healthy", timestamp = DateTime.UtcNow, version = "1.0.0" }));

app.MapControllers();
app.MapHub<ComplianceHub>("/hubs/compliance");
app.MapHub<TagSyncHub>("/hubs/tagsync");

// ── Auto-migrate and seed in development ──
if (app.Environment.IsDevelopment())
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<StingBimDbContext>();
    db.Database.EnsureCreated();
    await StingBIM.API.SeedData.SeedAsync(db);
}

// ── Recurring background jobs ──
RecurringJob.AddOrUpdate<StingBIM.Infrastructure.Services.ComplianceCheckJob>(
    "compliance-snapshot", j => j.ExecuteAsync(CancellationToken.None),
    Cron.Hourly, new RecurringJobOptions { QueueName = "compliance" });
RecurringJob.AddOrUpdate<StingBIM.Infrastructure.Services.SlaEscalationJob>(
    "sla-escalation", j => j.ExecuteAsync(CancellationToken.None),
    "*/15 * * * *", new RecurringJobOptions { QueueName = "default" });
RecurringJob.AddOrUpdate<StingBIM.Infrastructure.Services.StaleWarningCleanupJob>(
    "stale-warning-cleanup", j => j.ExecuteAsync(CancellationToken.None),
    Cron.Daily, new RecurringJobOptions { QueueName = "default" });

await app.RunAsync();
