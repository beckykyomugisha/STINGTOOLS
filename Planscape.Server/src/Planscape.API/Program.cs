using Planscape.Infrastructure.Data;
using Planscape.Infrastructure.SignalR;
using Planscape.API.Middleware;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using System.Threading.RateLimiting;
using Serilog;
using Serilog.Events;
using Serilog.Sinks.Elasticsearch;
using Hangfire;
using Hangfire.PostgreSql;
using Prometheus;
using StackExchange.Redis;

using Microsoft.AspNetCore.Http.Features;

var builder = WebApplication.CreateBuilder(args);

// Raise the multipart body limit globally to 200 MB so model uploads aren't
// rejected by the default 128 MB ceiling before the action-filter pipeline runs.
const long MaxUploadBytes = 200L * 1024 * 1024;
builder.WebHost.ConfigureKestrel(o => o.Limits.MaxRequestBodySize = MaxUploadBytes);
builder.Services.Configure<FormOptions>(o => o.MultipartBodyLengthLimit = MaxUploadBytes);

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
// S1 — fail fast in Production if Jwt:Key is missing. The dev fallback is
// *publicly known* and was leaking into deployments where the production
// settings file was misnamed or undeployed.
var jwtKey = builder.Configuration["Jwt:Key"];
if (string.IsNullOrWhiteSpace(jwtKey))
{
    if (builder.Environment.IsProduction())
    {
        throw new InvalidOperationException(
            "Jwt:Key is required in Production. Set it via environment variable or appsettings.Production.json.");
    }
    jwtKey = "Planscape-Dev-Secret-Key-Min32Chars!!";
}
if (jwtKey.Length < 32)
{
    throw new InvalidOperationException("Jwt:Key must be at least 32 characters (HMAC-SHA256 minimum).");
}
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
        // SEC-EA-01 — Algorithm whitelist + RequireSignedTokens.
        //
        // Two attacks this defeats:
        //   1. "alg: none" — attacker submits an unsigned token; pre-2015
        //      JWT libraries would honour it. RequireSignedTokens=true and
        //      explicit ValidAlgorithms make this impossible to reach.
        //   2. Algorithm confusion (RS↔HS) — attacker signs an HS256 token
        //      using the public key as the HMAC secret and submits it; a
        //      lazy validator that auto-detects the alg accepts it. The
        //      whitelist below restricts the validator to only the alg we
        //      actually issue.
        //
        // TODO-SEC: Production should migrate signing to RSA (RS256) with
        //   an asymmetric key pair so the public key can be rotated without
        //   re-issuing the secret to every plugin/mobile client. Until that
        //   migration ships, ValidAlgorithms is pinned to HS256 — the alg
        //   AuthController.GenerateJwt currently emits. After RS256
        //   migration, replace HmacSha256 below with RsaSha256.
        options.RequireHttpsMetadata = !builder.Environment.IsDevelopment();
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            RequireSignedTokens = true,
            ValidAlgorithms = new[] { SecurityAlgorithms.HmacSha256 },
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
            },
            // S5 — global iat-floor check. The JwtBearer pipeline only
            // verifies signature + issuer + audience + lifetime. Per-user
            // revocation (set via IPermissionRevocationStore.RevokeAllPriorTokensAsync
            // on password change, role demotion, etc.) is a Planscape
            // concept and must be enforced here, not just inside the
            // BimManagerOrAdmin policy handler.
            OnTokenValidated = async context =>
            {
                // SEC-EA-02 — per-token JTI revocation check. /api/auth/logout
                // writes "revoked:{jti}" into Redis with a TTL that matches
                // the token's remaining lifetime; if it's there, refuse.
                var jtiClaim = context.Principal?.FindFirst(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Jti)?.Value
                            ?? context.Principal?.FindFirst("jti")?.Value;
                if (!string.IsNullOrEmpty(jtiClaim))
                {
                    try
                    {
                        var redis = context.HttpContext.RequestServices
                            .GetRequiredService<StackExchange.Redis.IConnectionMultiplexer>();
                        var revoked = await redis.GetDatabase().KeyExistsAsync($"revoked:{jtiClaim}");
                        if (revoked)
                        {
                            context.Fail("Token has been revoked");
                            return;
                        }
                    }
                    catch
                    {
                        // Fail-open on Redis outage rather than locking every
                        // user out of the platform; the iat-floor below still
                        // applies and SEC-EA-09 (account lockout) caps abuse.
                    }
                }

                var sub = context.Principal?.FindFirst("user_id")?.Value
                       ?? context.Principal?.FindFirst("sub")?.Value
                       ?? context.Principal?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
                if (!Guid.TryParse(sub, out var userId)) return;

                var revocations = context.HttpContext.RequestServices
                    .GetRequiredService<Planscape.Infrastructure.Authorization.IPermissionRevocationStore>();
                var floor = await revocations.GetMinIatAsync(userId);
                if (floor is not long minIat) return;

                var iatClaim = context.Principal?.FindFirst("iat")?.Value;
                if (!long.TryParse(iatClaim, out var tokenIat))
                {
                    // Conservative: a Planscape token without iat is
                    // non-conformant; don't grant access on it once a
                    // floor exists for the user.
                    context.Fail("Token missing iat");
                    return;
                }
                if (tokenIat < minIat)
                {
                    context.Fail("Token issued before revocation floor");
                }
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
// Scoped (not singleton) because both implementations inject the scoped
// ITenantContext for per-request tenant isolation. A singleton would fail
// service-validation with 'Cannot consume scoped service ITenantContext
// from singleton IFileStorageService'.
var storageProvider = builder.Configuration["Storage:Provider"];
if (string.Equals(storageProvider, "S3", StringComparison.OrdinalIgnoreCase))
{
    builder.Services.AddScoped<Planscape.Core.Interfaces.IFileStorageService, Planscape.Infrastructure.Storage.S3FileStorageService>();
}
else
{
    builder.Services.AddScoped<Planscape.Core.Interfaces.IFileStorageService, Planscape.Infrastructure.Storage.LocalFileStorageService>();
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
// S4 — connection registry + revocation notifier so deactivating a
// ProjectMember evicts that user's running SignalR connections from the
// project group instead of letting them keep receiving events.
builder.Services.AddSingleton<Planscape.Infrastructure.SignalR.HubConnectionRegistry>();
builder.Services.AddSingleton<Planscape.Infrastructure.SignalR.IProjectMembershipNotifier,
                              Planscape.Infrastructure.SignalR.ProjectMembershipNotifier>();

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
// S4.2 — demo sandbox reset job (daily; resets the 'demo' tenant).
builder.Services.AddScoped<Planscape.Infrastructure.Services.DemoSandboxJob>();
// S7.2 — SLA burn-rate alert job (every 5 min).
builder.Services.AddScoped<Planscape.Infrastructure.Services.SlaBurnRateJob>();
// S7.4.1 — daily GDPR/POPIA hard-delete job (after 30-day cooling-off).
builder.Services.AddScoped<Planscape.Infrastructure.Services.DataErasureJob>();
// Idempotent platform-tenant seeder ('planscape' slug). Runs once on boot.
builder.Services.AddScoped<Planscape.Infrastructure.Services.PlatformTenantSeeder>();

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
// auth: 5 attempts / 5 min per IP (SEC-EA-06 — credential stuffing defence)
// api:  100 req / 60s per authenticated user (SEC-EA-06 baseline; tier
//       policies below extend this for paying customers)
//
// SEC-EA-06: when these thresholds change, update the "Retry-After"
// behaviour expectations in the mobile retry helper.
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    // Emit a Retry-After header alongside the 429 so clients can honour it.
    options.OnRejected = async (ctx, ct) =>
    {
        if (ctx.Lease.TryGetMetadata(System.Threading.RateLimiting.MetadataName.RetryAfter, out var retryAfter))
        {
            ctx.HttpContext.Response.Headers["Retry-After"] =
                ((int)retryAfter.TotalSeconds).ToString(System.Globalization.NumberFormatInfo.InvariantInfo);
        }
        ctx.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        ctx.HttpContext.Response.ContentType = "application/json";
        await ctx.HttpContext.Response.WriteAsync("{\"error\":\"rate_limit_exceeded\"}", ct);
    };

    // SEC-EA-06 — auth endpoints. 5 attempts per 5 minutes per IP defeats
    // credential stuffing while still allowing a careful human typist
    // who fat-fingers their password a few times. Queue = 0 so 6th
    // attempt rejects immediately rather than parking.
    options.AddFixedWindowLimiter("auth", o =>
    {
        o.PermitLimit         = 5;
        o.Window              = TimeSpan.FromMinutes(5);
        o.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
        o.QueueLimit          = 0;
        o.AutoReplenishment   = true;
    });

    // SEC-EA-06 — API baseline. Partition by user_id (sub) when
    // authenticated so a shared NAT IP doesn't punish concurrent users;
    // IP fallback for anonymous public endpoints.
    options.AddPolicy("api", context =>
    {
        var sub = context.User?.FindFirst("sub")?.Value
                  ?? context.User?.FindFirst("user_id")?.Value;
        var partitionKey = !string.IsNullOrWhiteSpace(sub)
            ? $"user:{sub}"
            : $"ip:{context.Connection.RemoteIpAddress?.ToString() ?? "unknown"}";
        return RateLimitPartition.GetFixedWindowLimiter(
            partitionKey,
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 100,
                Window = TimeSpan.FromSeconds(60),
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = 10,
                AutoReplenishment = true,
            });
    });

    // Tag sync: 30 req/min per IP (large payloads — be conservative)
    options.AddFixedWindowLimiter("tagsync", o =>
    {
        o.PermitLimit         = 30;
        o.Window              = TimeSpan.FromMinutes(1);
        o.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
        o.QueueLimit          = 0;
    });

    // Mobile: tier-aware. Authenticated callers partition by user_id (so a
    // shared device or rotating X-Device-Id can't multiply the budget).
    // Unauthenticated callers fall back to IP — same fixed envelope as
    // before (S17). Permit limit scales with the caller's licence tier:
    //   Starter      30 req/min
    //   Professional 120 req/min
    //   Premium      300 req/min
    //   Enterprise   600 req/min
    //   anonymous    60 req/min
    // (S18) Per-tenant DoS prevention — even a Premium tenant can't burn
    // the cluster's budget by spamming a sync endpoint.
    options.AddPolicy("mobile", context =>
    {
        var userIdClaim = context.User?.FindFirst("user_id")?.Value
                         ?? context.User?.FindFirst("sub")?.Value;
        var partitionKey = !string.IsNullOrWhiteSpace(userIdClaim)
            ? $"user:{userIdClaim}"
            : $"ip:{context.Connection.RemoteIpAddress?.ToString() ?? "unknown"}";

        var tier = context.User?.FindFirst("tier")?.Value;
        var permit = tier switch
        {
            "Enterprise"   => 600,
            "Premium"      => 300,
            "Professional" => 120,
            "Starter"      => 30,
            _              => string.IsNullOrWhiteSpace(userIdClaim) ? 60 : 120,
        };

        return RateLimitPartition.GetFixedWindowLimiter(partitionKey, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = permit,
            Window = TimeSpan.FromMinutes(1),
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            QueueLimit = 0
        });
    });

    // S7.6 — per-tenant policy. Budgets a tenant's whole organisation
    // proportional to its plan so a buggy automation account on
    // tenant A can't DoS the cluster for tenant B. Reads tenant id
    // from JWT claim 'tenant_id' (set by AuthController on login);
    // anonymous requests partition by IP as a fallback.
    options.AddPolicy("per-tenant", context =>
    {
        var tenantId = context.User?.FindFirst("tenant_id")?.Value
                       ?? context.Connection.RemoteIpAddress?.ToString()
                       ?? "anon";
        // Default budget: 600/min for paying tenants, 60/min for trial.
        // Plan resolution happens in middleware (TenantContext is scoped),
        // so we keep a simple bucket here and let the [Quota] attribute
        // (S1.4) own plan-specific axes. This rate-limit is a 'cluster
        // safety net' — not a feature gate.
        return RateLimitPartition.GetFixedWindowLimiter(tenantId, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 600,
            Window = TimeSpan.FromMinutes(1),
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            QueueLimit = 0,
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
// S10 — Swagger is on in Development by default; in Production the
// operator must explicitly opt in via Swagger:Enabled=true. Leaks of the
// API schema are useful to attackers (they enumerate every endpoint
// shape, parameter, and authorisation requirement at zero cost), so the
// default for any non-dev environment is off.
var swaggerExplicitlyEnabled = string.Equals(
    app.Configuration["Swagger:Enabled"], "true", StringComparison.OrdinalIgnoreCase);
if (app.Environment.IsDevelopment() || swaggerExplicitlyEnabled)
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

// SEC-EA-07 — security response headers (HSTS / nosniff / frame-deny /
// CSP / Referrer-Policy / Permissions-Policy). Inserted early so even
// short-circuit responses from the rate limiter or auth middleware
// still carry the hardening headers. /health endpoints are skipped.
app.UseSecurityHeaders();
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
// S7.2 — count every response into rolling SLA buckets in Redis.
app.UseSlaMetrics();
app.UseAuthentication();
// S9 — push correlation ID + tenant + user into Serilog LogContext.
// Must run AFTER UseAuthentication so the JWT claims are populated.
app.UseMiddleware<Planscape.API.Middleware.CorrelationIdMiddleware>();
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
    // S11 — never leak DB connection-string / driver internals into the
    // response body. Probes only need a 200 vs 503; debugging goes
    // through the logs (correlation ID is enriched by S9).
    try
    {
        return await db.Database.CanConnectAsync()
            ? Results.Ok(new { status = "ready" })
            : Results.Json(new { status = "not-ready" }, statusCode: 503);
    }
    catch
    {
        return Results.Json(new { status = "not-ready" }, statusCode: 503);
    }
}).AllowAnonymous();

app.MapGet("/health", async (HttpContext httpCtx, PlanscapeDbContext db, IConnectionMultiplexer? redis, Planscape.Core.Interfaces.IPushNotificationService push, IConfiguration config) =>
{
    // S11 — full health diagnostic exposes the topology (which DB,
    // which push provider, Redis ping). Restrict to:
    //   • loopback / private-network callers (Docker / k8s / VPC), AND
    //   • a shared secret in the X-Health-Token header that matches
    //     Health:Token in config. Either alone is treated as a fail.
    // In Production both gates fire; in Development the loopback gate
    // alone is enough so localhost diagnostics still work.
    var remote = httpCtx.Connection.RemoteIpAddress;
    var isPrivate = remote != null
                    && (System.Net.IPAddress.IsLoopback(remote)
                        || IsRfc1918(remote));
    var configuredToken = config["Health:Token"];
    var presentedToken = httpCtx.Request.Headers["X-Health-Token"].ToString();
    var tokenOk = !string.IsNullOrWhiteSpace(configuredToken)
                  && string.Equals(configuredToken, presentedToken, StringComparison.Ordinal);

    var allowed = httpCtx.RequestServices
                       .GetRequiredService<IHostEnvironment>()
                       .IsDevelopment()
        ? isPrivate
        : isPrivate && tokenOk;

    if (!allowed)
    {
        return Results.Json(new { status = "forbidden" }, statusCode: 403);
    }

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

// SEC-EA-06 — apply the "api" baseline limit to every controller. Per-
// endpoint [EnableRateLimiting("auth"|"mobile"|"tagsync"|...)] attributes
// override this default, so paying tiers still get their tier-aware
// budget while bare endpoints get the 100/60s baseline.
app.MapControllers().RequireRateLimiting("api");
app.MapHub<ComplianceHub>("/hubs/compliance");
app.MapHub<TagSyncHub>("/hubs/tagsync");
app.MapHub<NotificationHub>("/hubs/notifications");
// S6.3 — CRDT relay for collaborative pin / issue editing.
app.MapHub<Planscape.Infrastructure.SignalR.CrdtHub>("/hubs/crdt");

// ── Database schema + seed ──
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<PlanscapeDbContext>();

    // The hand-authored migrations under Planscape.Infrastructure/Data/Migrations/
    // are missing their .Designer.cs companions and the model snapshot is stale,
    // so Migrate() cannot apply them in order. For dev / local docker stacks we
    // materialise the schema directly from OnModelCreating which always matches
    // the current entity classes. Production deployments should regenerate the
    // migration set with `dotnet ef migrations` once the model is stable.
    var useEnsureCreated = app.Environment.IsDevelopment()
        || string.Equals(
            Environment.GetEnvironmentVariable("PLANSCAPE_USE_ENSURE_CREATED"),
            "true", StringComparison.OrdinalIgnoreCase);

    if (useEnsureCreated)
    {
        // EnsureCreated() short-circuits if the *database* exists, and the
        // built-in HasTables() returns true even for non-app tables like
        // Hangfire's job-store schema or __EFMigrationsHistory left over
        // from earlier attempts. Probe specifically for the Tenants table
        // (the first row created by SeedData) and materialise the full
        // schema from OnModelCreating only when it's missing.
        var conn = db.Database.GetDbConnection();
        if (conn.State != System.Data.ConnectionState.Open) await conn.OpenAsync();
        bool hasAppSchema;
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT EXISTS (SELECT 1 FROM information_schema.tables "
                            + "WHERE table_schema = 'public' AND table_name = 'Tenants')";
            hasAppSchema = (bool)(await cmd.ExecuteScalarAsync() ?? false);
        }
        if (!hasAppSchema)
        {
            var creator = (Microsoft.EntityFrameworkCore.Storage.RelationalDatabaseCreator)
                db.Database.GetService<Microsoft.EntityFrameworkCore.Storage.IDatabaseCreator>();
            creator.CreateTables();
        }
    }
    else
    {
        db.Database.Migrate();
    }

    // Idempotent column patcher — runs in BOTH branches because:
    //   • EnsureCreated short-circuits once tables exist, so older dev
    //     DBs don't pick up entity additions.
    //   • The hand-authored Migrate() set is also incomplete (Program.cs
    //     comment at line 854-859), so production DBs with the same
    //     vintage hit the same gap.
    // 'ADD COLUMN IF NOT EXISTS' is a no-op when the column already
    // exists, so running it on a healthy DB costs nothing.
    {
        var patchConn = db.Database.GetDbConnection();
        if (patchConn.State != System.Data.ConnectionState.Open)
            await patchConn.OpenAsync();
        await PatchDevSchemaAsync(patchConn);
    }

    if (app.Environment.IsDevelopment())
    {
        await Planscape.API.SeedData.SeedAsync(db, app.Environment);
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

// S4.2 — daily demo sandbox reset. Wipes everything in the 'demo' tenant
// and re-seeds. Runs at 02:00 UTC (05:00 EAT) so morning prospects find
// a clean slate.
RecurringJob.AddOrUpdate<Planscape.Infrastructure.Services.DemoSandboxJob>(
    "demo-reset", j => j.ExecuteAsync(CancellationToken.None),
    "0 2 * * *", new RecurringJobOptions { QueueName = "default" });

// S7.2 — SLA burn-rate alerts every 5 minutes. Reads rolling-window
// 5xx counts from Redis (populated by the request middleware in S7.2.1)
// and pages the founder when burn rate exceeds the threshold.
RecurringJob.AddOrUpdate<Planscape.Infrastructure.Services.SlaBurnRateJob>(
    "sla-burn", j => j.ExecuteAsync(CancellationToken.None),
    "*/5 * * * *", new RecurringJobOptions { QueueName = "default" });

// S7.4.1 — daily GDPR/POPIA erasure job. Walks tenants whose
// PendingErasureAt has elapsed (set by /api/data-rights/erase) and
// hard-deletes them. Runs at 04:00 UTC (07:00 EAT) — late enough that
// any cancel-erase from yesterday has landed before today's sweep.
RecurringJob.AddOrUpdate<Planscape.Infrastructure.Services.DataErasureJob>(
    "data-erasure", j => j.ExecuteAsync(CancellationToken.None),
    "0 4 * * *", new RecurringJobOptions { QueueName = "default" });

// Seed the well-known 'planscape' platform tenant idempotently on startup
// so /api/platform/revenue + SlaBurnRateJob alerts find their target.
using (var scope = app.Services.CreateScope())
{
    var seeder = scope.ServiceProvider.GetRequiredService<Planscape.Infrastructure.Services.PlatformTenantSeeder>();
    try { await seeder.EnsureAsync(); }
    catch (Exception ex)
    {
        // Don't fail boot — log and continue; first request will surface the error.
        Log.Warning(ex, "PlatformTenantSeeder failed");
    }
}

await app.RunAsync();

// Idempotent 'ADD COLUMN IF NOT EXISTS' patcher for databases that were
// created on an older entity model. Runs unconditionally on startup —
// 'IF NOT EXISTS' makes every statement a no-op when the column is
// already there, so it's safe to keep around. Add new rows here
// whenever an entity gains a nullable column that older deployments
// won't have via the migration set.
static async Task PatchDevSchemaAsync(System.Data.Common.DbConnection conn)
{
    var patches = new[]
    {
        // Phase 169 — project location + cover image + pin flag.
        "ALTER TABLE \"Projects\" ADD COLUMN IF NOT EXISTS \"Latitude\" double precision",
        "ALTER TABLE \"Projects\" ADD COLUMN IF NOT EXISTS \"Longitude\" double precision",
        "ALTER TABLE \"Projects\" ADD COLUMN IF NOT EXISTS \"City\" text",
        "ALTER TABLE \"Projects\" ADD COLUMN IF NOT EXISTS \"Country\" text",
        "ALTER TABLE \"Projects\" ADD COLUMN IF NOT EXISTS \"CoverImageUrl\" text",
        "ALTER TABLE \"Projects\" ADD COLUMN IF NOT EXISTS \"IsPinned\" boolean NOT NULL DEFAULT false",
    };
    int applied = 0, failed = 0;
    foreach (var sql in patches)
    {
        try
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            await cmd.ExecuteNonQueryAsync();
            applied++;
        }
        catch (Exception ex)
        {
            // Don't crash startup over a schema patch — log and continue.
            // The original column-missing error will still surface on the
            // request that needs it, which is the correct fallback.
            failed++;
            Console.WriteLine($"[schema-patch] FAILED: {sql} — {ex.Message}");
        }
    }
    Console.WriteLine($"[schema-patch] done — {applied} ok, {failed} failed");
}

// S11 — RFC 1918 + IPv6 unique-local + IPv4-mapped IPv6 helper. Used by
// the /health full-diagnostic gate to allow callers from the same
// private network as the API (Docker compose internal network, k8s
// pod CIDR, VPC), and reject the public internet.
static bool IsRfc1918(System.Net.IPAddress ip)
{
    if (ip.IsIPv4MappedToIPv6) ip = ip.MapToIPv4();
    if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
    {
        var b = ip.GetAddressBytes();
        if (b[0] == 10) return true;                                   // 10.0.0.0/8
        if (b[0] == 172 && b[1] >= 16 && b[1] <= 31) return true;      // 172.16.0.0/12
        if (b[0] == 192 && b[1] == 168) return true;                   // 192.168.0.0/16
        if (b[0] == 127) return true;                                  // loopback
        return false;
    }
    if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6)
    {
        var b = ip.GetAddressBytes();
        if ((b[0] & 0xFE) == 0xFC) return true;                        // fc00::/7 (ULA)
        if (System.Net.IPAddress.IsLoopback(ip)) return true;          // ::1
        return false;
    }
    return false;
}
