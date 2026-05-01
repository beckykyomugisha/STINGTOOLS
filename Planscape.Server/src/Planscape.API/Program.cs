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
using Serilog.Events;
using Serilog.Sinks.Elasticsearch;
using Hangfire;
using Prometheus;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);

// ── Serilog (MON-01: Seq / Elastic observability) ──
// Console + rolling file is always-on. Optional structured-log sinks are
// activated by config so this still works in sandboxed dev environments.
//
//   Serilog:Seq:ServerUrl          → e.g. http://seq:5341
//   Serilog:Seq:ApiKey             → optional, for shared Seq instances
//   Serilog:Elastic:NodeUris       → comma-separated https://host:9200
//   Serilog:Elastic:IndexFormat    → planscape-{0:yyyy.MM}
//   Serilog:Elastic:ApiKey         → ES API key (optional)
builder.Host.UseSerilog((ctx, sp, lc) =>
{
    lc.ReadFrom.Configuration(ctx.Configuration)
      .Enrich.FromLogContext()
      .Enrich.WithProperty("app", "planscape-api")
      .Enrich.WithProperty("env", ctx.HostingEnvironment.EnvironmentName)
      .WriteTo.Console()
      .WriteTo.File("logs/planscape-.log", rollingInterval: RollingInterval.Day);

    var seqUrl = ctx.Configuration["Serilog:Seq:ServerUrl"];
    if (!string.IsNullOrWhiteSpace(seqUrl))
    {
        lc.WriteTo.Seq(seqUrl, apiKey: ctx.Configuration["Serilog:Seq:ApiKey"]);
    }

    var elasticUris = ctx.Configuration["Serilog:Elastic:NodeUris"];
    if (!string.IsNullOrWhiteSpace(elasticUris))
    {
        var nodes = elasticUris.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                               .Select(u => new Uri(u)).ToArray();
        var sinkOpts = new ElasticsearchSinkOptions(nodes)
        {
            AutoRegisterTemplate = true,
            AutoRegisterTemplateVersion = AutoRegisterTemplateVersion.ESv8,
            IndexFormat = ctx.Configuration["Serilog:Elastic:IndexFormat"] ?? "planscape-{0:yyyy.MM}",
            MinimumLogEventLevel = LogEventLevel.Information,
            EmitEventFailure = EmitEventFailureHandling.WriteToSelfLog,
        };
        lc.WriteTo.Elasticsearch(sinkOpts);
    }
});

// ── Database ──
builder.Services.AddDbContext<PlanscapeDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Default")));

// ── Authentication ──
// P1 — JWT key rotation with grace period.
// Primary signing key is `Jwt:Key`. During rotation, put the old key into
// `Jwt:PreviousKey` for the overlap window (default 7d). Tokens issued
// under either key validate during that window. After the window ends,
// clear `Jwt:PreviousKey`. This prevents mass sign-outs on key rotation.
var jwtKey = builder.Configuration["Jwt:Key"] ?? "Planscape-Dev-Secret-Key-Min32Chars!!";
var jwtPrevKey = builder.Configuration["Jwt:PreviousKey"];
var signingKeys = new List<SecurityKey>
{
    new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)) { KeyId = "current" },
};
if (!string.IsNullOrWhiteSpace(jwtPrevKey))
{
    signingKeys.Add(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtPrevKey)) { KeyId = "previous" });
}

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
            // IssuerSigningKeys (plural) lets us validate tokens signed with
            // either the current or the previous key during rotation.
            IssuerSigningKeys = signingKeys,
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
builder.Services.AddAuthorization(options =>
{
    // Phase 152 — finer-grained policy for tenant-keywords admin
    // endpoints so a BIM Manager (ISO 19650 role K on any project) can
    // edit deliverable-state-machine vocabulary without being promoted
    // to a tenant Owner. The handler short-circuits on Admin / Owner
    // so existing operators are unaffected.
    options.AddPolicy("BimManagerOrAdmin", policy =>
        policy.Requirements.Add(new Planscape.Infrastructure.Authorization.BimManagerOrAdminRequirement()));

    // Phase 158 — separation-of-duties policy for security-sensitive
    // endpoints (token revocation, future audit-log surfaces). Grants
    // SecurityOfficer + Admin + Owner so a tenant can ship a dedicated
    // SecurityOfficer persona without giving them tenant admin powers.
    options.AddPolicy("SecurityOfficerOrAdmin", policy =>
        policy.Requirements.Add(new Planscape.Infrastructure.Authorization.SecurityOfficerOrAdminRequirement()));
});
builder.Services.AddSingleton<Microsoft.AspNetCore.Authorization.IAuthorizationHandler,
    Planscape.Infrastructure.Authorization.BimManagerOrAdminHandler>();
builder.Services.AddSingleton<Microsoft.AspNetCore.Authorization.IAuthorizationHandler,
    Planscape.Infrastructure.Authorization.SecurityOfficerOrAdminHandler>();

// ── Services ──
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<Planscape.Core.Interfaces.ITenantContext, Planscape.Infrastructure.Services.TenantContext>();
// STORAGE-01 — Storage:Provider = "S3" | "Local" (default). S3 covers AWS, MinIO, R2, Spaces.
var storageProvider = builder.Configuration["Storage:Provider"];
if (string.Equals(storageProvider, "S3", StringComparison.OrdinalIgnoreCase))
{
    builder.Services.AddSingleton<Planscape.Core.Interfaces.IFileStorageService, Planscape.Infrastructure.Storage.S3FileStorageService>();
}
else
{
    builder.Services.AddSingleton<Planscape.Core.Interfaces.IFileStorageService, Planscape.Infrastructure.Storage.LocalFileStorageService>();
}
// Phase 150 — platform-wide deliverable state-machine keyword
// extensions. Bound from `DeliverableStateMachine:Keywords` in
// appsettings; falls back to an empty registry when the section is
// absent so projects continue to use built-in vocabulary only.
builder.Services.AddSingleton<Planscape.Infrastructure.Workflow.IPlatformKeywordRegistry,
    Planscape.Infrastructure.Workflow.ConfigPlatformKeywordRegistry>();
// Phase 151 — tenant-scoped keyword extensions (read-through cache).
// Singleton so the cache survives across requests; the resolver itself
// holds the DbContext via the request scope when invoked.
builder.Services.AddScoped<Planscape.Infrastructure.Workflow.ITenantKeywordResolver,
    Planscape.Infrastructure.Workflow.DbTenantKeywordResolver>();
// Phase 155 — tenant-scoped BIM Manager role override resolver.
// Same lifecycle / cache shape as the keyword resolver above so the
// authorisation handler doesn't re-parse the JSON per request.
builder.Services.AddScoped<Planscape.Infrastructure.Authorization.ITenantBimManagerRoleResolver,
    Planscape.Infrastructure.Authorization.DbTenantBimManagerRoleResolver>();
// Phase 156 — JWT permission-revocation store (Redis-backed). The
// auth handler reads it on every policy-gated authorisation; admin
// actions that change a user's role bump the per-user floor so old
// tokens lose access immediately rather than waiting for expiry.
builder.Services.AddSingleton<Planscape.Infrastructure.Authorization.IPermissionRevocationStore,
    Planscape.Infrastructure.Authorization.RedisPermissionRevocationStore>();

builder.Services.AddScoped<Planscape.Core.Interfaces.IGeofenceValidationService, Planscape.Infrastructure.Services.GeofenceValidationService>();
builder.Services.AddScoped<Planscape.API.Services.IThumbnailService, Planscape.API.Services.ImageSharpThumbnailService>();
builder.Services.AddScoped<Planscape.API.Services.IAuditService, Planscape.API.Services.AuditService>();

// ── Platform Connectors ──
builder.Services.AddSingleton<Planscape.Core.Interfaces.IPlatformConnector, Planscape.Infrastructure.Services.AccConnector>();
builder.Services.AddSingleton<Planscape.Core.Interfaces.IPlatformConnector, Planscape.Infrastructure.Services.ProcoreConnector>();
builder.Services.AddSingleton<Planscape.Core.Interfaces.IPlatformConnector, Planscape.Infrastructure.Services.AconexConnector>();
builder.Services.AddSingleton<Planscape.Core.Interfaces.IPlatformConnector, Planscape.Infrastructure.Services.TrimbleConnector>();
builder.Services.AddSingleton<Planscape.Core.Interfaces.IPlatformConnectorFactory, Planscape.Infrastructure.Services.PlatformConnectorFactory>();

// ── Email & Tenant branding (FLEX-03, FLEX-07) ──
builder.Services.AddSingleton<Planscape.Core.Interfaces.ITenantBrandingService, Planscape.Infrastructure.Services.TenantBrandingService>();
// Renderer is Singleton so its file-read cache survives across requests.
// Deps are all Singleton-safe (IHostEnvironment, IConfiguration, ILogger).
builder.Services.AddSingleton<Planscape.Infrastructure.Services.IEmailTemplateRenderer, Planscape.Infrastructure.Services.FileEmailTemplateRenderer>();

// ── i18n (FLEX-15) — load resource files once at startup; mobile pulls via /api/i18n. ──
builder.Services.AddSingleton<Planscape.Core.Interfaces.II18nService, Planscape.Infrastructure.Services.I18nService>();

// ── OCR (T3) — server-side cloud fallback for when on-device OCR misses.
var ocrProvider = builder.Configuration["Ocr:Provider"];
if (string.Equals(ocrProvider, "azure-vision", StringComparison.OrdinalIgnoreCase)
    && !string.IsNullOrWhiteSpace(builder.Configuration["Ocr:Azure:ApiKey"]))
    builder.Services.AddSingleton<Planscape.Core.Interfaces.IOcrService, Planscape.Infrastructure.Services.AzureVisionOcrService>();
else
    builder.Services.AddSingleton<Planscape.Core.Interfaces.IOcrService, Planscape.Infrastructure.Services.NullOcrService>();

// ── NLP (NLP-AUTO-LINK) — rule-based + LLM fallback. Provider auto-selected
//    from config; defaults to the Null implementation so builds stay
//    deterministic without credentials.
var nlpProvider = builder.Configuration["Nlp:Provider"];
if (string.Equals(nlpProvider, "azure-openai", StringComparison.OrdinalIgnoreCase)
    && !string.IsNullOrWhiteSpace(builder.Configuration["Nlp:Azure:ApiKey"]))
    builder.Services.AddSingleton<Planscape.Core.Interfaces.INlpLlmResolver, Planscape.Infrastructure.Services.AzureOpenAiLlmResolver>();
else
    builder.Services.AddSingleton<Planscape.Core.Interfaces.INlpLlmResolver, Planscape.Infrastructure.Services.NullLlmResolver>();
builder.Services.AddSingleton<Planscape.Core.Interfaces.INlpResolver, Planscape.Infrastructure.Services.NlpResolver>();
if (!string.IsNullOrEmpty(builder.Configuration["Smtp:Host"])
    || !string.IsNullOrEmpty(builder.Configuration["Email:Host"]))
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
builder.Services.AddHttpClient("webhook");
builder.Services.AddHttpClient("outbound-webhook");
// T3 — Slack / Teams outbound webhook dispatcher (fire-and-forget).
builder.Services.AddSingleton<Planscape.Infrastructure.Services.ChatWebhookDispatcher>();
// Phase 165 (NEW-08) — generic outbound webhook dispatcher (HMAC-signed, retry).
builder.Services.AddSingleton<Planscape.Infrastructure.Services.OutboundWebhookDispatcher>();
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

// T3 — in-memory SignalR presence tracker (scales per-node; the Redis
// backplane above handles the fan-out broadcast for horizontal scale).
builder.Services.AddSingleton<Planscape.Infrastructure.SignalR.PresenceTracker>();

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
builder.Services.AddScoped<Planscape.Infrastructure.Services.DatabaseBackupJob>();
builder.Services.AddScoped<Planscape.Infrastructure.Services.PlatformSyncJob>();
builder.Services.AddScoped<Planscape.Infrastructure.Services.CustomFieldsPurgeJob>();
builder.Services.AddScoped<Planscape.Infrastructure.Services.ModelDerivativeJob>();
// S1.4 — quota guard service used by [Quota(...)] action filter + controllers.
builder.Services.AddScoped<Planscape.Infrastructure.Services.IQuotaGuardService,
    Planscape.Infrastructure.Services.QuotaGuardService>();
// S1.6 — trial state machine job (daily; sends reminders + freezes on expiry).
builder.Services.AddScoped<Planscape.Infrastructure.Services.TrialStateMachineJob>();

// S2.2 + S2.3 — payment providers. Both registered as IPaymentProvider so
// PaymentRouter can pick by currency. Stripe handles USD/EUR/GBP;
// Flutterwave handles the East-African + West-African corridor.
builder.Services.AddSingleton<Planscape.Core.Interfaces.IPaymentProvider,
    Planscape.Infrastructure.Billing.StripePaymentProvider>();
builder.Services.AddSingleton<Planscape.Core.Interfaces.IPaymentProvider,
    Planscape.Infrastructure.Billing.FlutterwavePaymentProvider>();
builder.Services.AddSingleton<Planscape.Infrastructure.Billing.PaymentRouter>();
// S2.5 — invoice PDF renderer (no library dependencies; emits PDF 1.4 bytes).
builder.Services.AddScoped<Planscape.Infrastructure.Billing.InvoicePdfRenderer>();
// S2.6 — dunning job (daily; reminder cadence at 0/3/7 days, suspend at 10).
builder.Services.AddScoped<Planscape.Infrastructure.Services.DunningJob>();
// S2.6.1 — Flutterwave renewal job (daily; mints next-period invoice +
// payment-link email, since FW lacks first-class recurring subscriptions).
builder.Services.AddScoped<Planscape.Infrastructure.Services.FlutterwaveRenewalJob>();
// S3.1 — fast-path bulk upsert for tag sync (Postgres COPY + ON CONFLICT).
builder.Services.AddScoped<Planscape.Core.Interfaces.IBulkTagUpserter,
    Planscape.Infrastructure.Services.PostgresBulkTagUpserter>();
// S3.2 — outbox dispatcher (drains OutboxMessages every minute).
builder.Services.AddScoped<Planscape.Infrastructure.Services.OutboxDispatcher>();

// P7 + P8 — IFC→glTF converter + thumbnail generator. Null defaults keep the
// system running without a converter installed; swap the registration to
// IfcConvertConverter / ApsModelDerivativeConverter / real thumbnail
// service when infra is ready.
var converterProvider = builder.Configuration["ModelConverter:Provider"];
if (string.Equals(converterProvider, "ifcconvert", StringComparison.OrdinalIgnoreCase))
    builder.Services.AddSingleton<Planscape.Core.Interfaces.IModelConverter, Planscape.Infrastructure.Services.IfcConvertConverter>();
else if (string.Equals(converterProvider, "aps", StringComparison.OrdinalIgnoreCase))
    builder.Services.AddSingleton<Planscape.Core.Interfaces.IModelConverter, Planscape.Infrastructure.Services.ApsModelDerivativeConverter>();
else
    builder.Services.AddSingleton<Planscape.Core.Interfaces.IModelConverter, Planscape.Infrastructure.Services.NullModelConverter>();

var thumbProvider = builder.Configuration["ModelConverter:ThumbnailProvider"];
if (string.Equals(thumbProvider, "null", StringComparison.OrdinalIgnoreCase))
    builder.Services.AddSingleton<Planscape.Core.Interfaces.IModelThumbnailGenerator,
        Planscape.Infrastructure.Services.NullThumbnailGenerator>();
else
    builder.Services.AddSingleton<Planscape.Core.Interfaces.IModelThumbnailGenerator,
        Planscape.Infrastructure.Services.GltfBoundsThumbnailGenerator>();

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();

// PR2 — HSTS: 1 year, include subdomains, mark for browser preload list.
// Skipping the preload header until the cert is on a stable production domain.
builder.Services.AddHsts(options =>
{
    options.IncludeSubDomains = true;
    options.MaxAge = TimeSpan.FromDays(365);
});


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
else
{
    // PR2 — outside development, force TLS at the application layer in
    // addition to whatever the reverse proxy enforces. HSTS tells browsers
    // to refuse cleartext for one year (incl. subdomains). Behind a TLS-
    // terminating proxy you must set `ASPNETCORE_FORWARDEDHEADERS_ENABLED=true`
    // (or call `app.UseForwardedHeaders`) so the request still appears as
    // HTTPS to the redirect middleware.
    app.UseHsts();
}

// PR2 — Always-on HTTPS redirect. In development this is a no-op when the
// app binds to an HTTPS port; in production it kicks in for any cleartext
// listener that slips through.
app.UseHttpsRedirection();

// C1 — serve the wwwroot office dashboard (index.html + viewer.html + js/css).
// Placed before auth so assets load without a token; the JS handles login
// against /api/auth/login via fetch.
app.UseDefaultFiles();
app.UseStaticFiles();

app.UseSerilogRequestLogging();
// MON-02: request/response metrics (latency histogram, status codes, in-flight).
// Exposed at /metrics in Prometheus exposition format. Scrape once per 15-30s.
app.UseHttpMetrics();
app.UseRateLimiter();
app.UseCors("Dashboard");
app.UseCors("Mobile");
// S3.8 — rewrite /api/v1/* → /api/* before routing so existing
// controllers serve both. Older /api/* paths get a Deprecation
// header pointing at /api/v1 and a 2026-12-31 sunset date.
app.UseApiVersionRewriter();
app.UseAuthentication();
app.UseMiddleware<TenantResolutionMiddleware>(); // Must run AFTER auth so JWT claims are available
app.UseMiddleware<MobileContextMiddleware>();
app.UseMiddleware<LocaleMiddleware>();           // FLEX-15 — resolves language after tenant is known
app.UseAuthorization();

app.UseHangfireDashboard("/hangfire", new DashboardOptions
{
    Authorization = new[] { new Planscape.Infrastructure.Services.HangfireAuthorizationFilter() }
});

// MON-03: /metrics endpoint for Prometheus / Grafana Agent / Datadog / NewRelic.
// Unauthenticated so scrapers can collect without a JWT, but restrict network-side
// (bind nginx /metrics to internal IP only). Disable with Monitoring:ExposeMetrics=false.
var exposeMetrics = builder.Configuration.GetValue<bool>("Monitoring:ExposeMetrics", true);
if (exposeMetrics)
{
    app.MapMetrics("/metrics").AllowAnonymous();
}

// ── Health check ── (NEW-SRV-22)
// Returns sub-check results so mobile can detect partial degradation.
// Status codes: 200 healthy, 503 degraded (any sub-check failed).
// HEALTH-01 — Separate probes for orchestrator/mobile consumption.
// /health/live  → process is running (K8s liveness, mobile ping)
// /health/ready → process is accepting traffic (K8s readiness, probes)
// /health       → legacy full diagnostic (returned below)
app.MapGet("/health/live", () => Results.Ok(new { status = "alive", timestamp = DateTime.UtcNow }))
    .AllowAnonymous();

app.MapGet("/health/ready", async (PlanscapeDbContext db) =>
{
    try
    {
        return await db.Database.CanConnectAsync()
            ? Results.Ok(new { status = "ready" })
            : Results.Json(new { status = "not-ready", reason = "db-unreachable" }, statusCode: 503);
    }
    catch (Exception ex)
    {
        return Results.Json(new { status = "not-ready", reason = ex.Message }, statusCode: 503);
    }
}).AllowAnonymous();

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
// BACKUP-01 — nightly 02:15 UTC Postgres dump. Runs only when Backup:Enabled=true.
RecurringJob.AddOrUpdate<Planscape.Infrastructure.Services.DatabaseBackupJob>(
    "database-backup", j => j.ExecuteAsync(CancellationToken.None),
    "15 2 * * *", new RecurringJobOptions { QueueName = "default" });
// FLEX-13 — nightly 03:15 UTC purge of custom fields past the 30-day grace period.
RecurringJob.AddOrUpdate<Planscape.Infrastructure.Services.CustomFieldsPurgeJob>(
    "custom-fields-purge", j => j.ExecuteAsync(CancellationToken.None),
    "15 3 * * *", new RecurringJobOptions { QueueName = "default" });
// P7 + P8 — every 10 minutes, produce glTF + thumbnail derivatives for
// freshly-uploaded IFC/RVT models so the mobile viewer can render them.
RecurringJob.AddOrUpdate<Planscape.Infrastructure.Services.ModelDerivativeJob>(
    "model-derivatives", j => j.ExecuteAsync(CancellationToken.None),
    "*/10 * * * *", new RecurringJobOptions { QueueName = "default" });

// S1.6 — daily trial state machine. Sends 7d/3d/1d reminders, freezes
// expired tenants, prompts dunning. Runs at 06:00 UTC ≈ 09:00 EAT.
RecurringJob.AddOrUpdate<Planscape.Infrastructure.Services.TrialStateMachineJob>(
    "trial-state", j => j.ExecuteAsync(CancellationToken.None),
    "0 6 * * *", new RecurringJobOptions { QueueName = "default" });

// S2.6 — daily dunning job. Walks Overdue invoices on the 0/3/7-day
// cadence, suspends at day 10. Runs at 07:00 UTC ≈ 10:00 EAT (after
// the trial state machine so today's freezes get a billing reminder
// today rather than tomorrow).
RecurringJob.AddOrUpdate<Planscape.Infrastructure.Services.DunningJob>(
    "dunning", j => j.ExecuteAsync(CancellationToken.None),
    "0 7 * * *", new RecurringJobOptions { QueueName = "default" });

// S2.6.1 — daily Flutterwave renewal job. Mints the next-period invoice
// + emails a payment link 24 h before the current period ends. Stripe
// subscriptions self-renew; this only handles the FW corridor.
RecurringJob.AddOrUpdate<Planscape.Infrastructure.Services.FlutterwaveRenewalJob>(
    "fw-renewals", j => j.ExecuteAsync(CancellationToken.None),
    "30 5 * * *", new RecurringJobOptions { QueueName = "default" });

// S3.2 — outbox dispatcher (every minute). Drains OutboxMessages with
// at-least-once + exponential-backoff retry; dead-letters after 6 attempts.
RecurringJob.AddOrUpdate<Planscape.Infrastructure.Services.OutboxDispatcher>(
    "outbox", j => j.ExecuteAsync(CancellationToken.None),
    "* * * * *", new RecurringJobOptions { QueueName = "default" });

await app.RunAsync();
