using StingBIM.Infrastructure.Data;
using StingBIM.Infrastructure.SignalR;
using StingBIM.API.Middleware;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using Serilog;

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
builder.Services.AddSignalR();
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
app.UseCors("Dashboard");
app.UseMiddleware<TenantResolutionMiddleware>();
app.UseAuthentication();
app.UseAuthorization();

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

await app.RunAsync();
