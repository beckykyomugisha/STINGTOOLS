using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Planscape.Core.Entities;
using Planscape.Core.Interfaces;
using Planscape.Infrastructure.Data;

namespace Planscape.Infrastructure.Services;

/// <summary>
/// FLEX-03 — DB-backed <see cref="ITenantBrandingService"/> with in-memory cache.
/// Registered as Singleton so it can live alongside the singleton email service;
/// uses <see cref="IServiceScopeFactory"/> to open a scope for each DB access.
/// </summary>
public class TenantBrandingService : ITenantBrandingService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfiguration _config;
    private readonly ILogger<TenantBrandingService> _logger;

    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(5);
    private readonly ConcurrentDictionary<Guid, (ResolvedBranding Branding, DateTime CachedAt)> _cache = new();

    public TenantBrandingService(
        IServiceScopeFactory scopeFactory,
        IConfiguration config,
        ILogger<TenantBrandingService> logger)
    {
        _scopeFactory = scopeFactory;
        _config = config;
        _logger = logger;
    }

    public async Task<ResolvedBranding> GetAsync(Guid tenantId, CancellationToken ct = default)
    {
        if (_cache.TryGetValue(tenantId, out var hit) && DateTime.UtcNow - hit.CachedAt < CacheTtl)
            return hit.Branding;

        TenantBranding? row = null;
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<PlanscapeDbContext>();
            row = await db.Set<TenantBranding>().AsNoTracking()
                          .FirstOrDefaultAsync(b => b.TenantId == tenantId, ct);
        }
        catch (Exception ex)
        {
            // DB might not have the table yet (migration pending). Fall back to defaults.
            _logger.LogWarning(ex, "Tenant branding lookup failed, falling back to defaults");
        }

        var resolved = Resolve(row);
        _cache[tenantId] = (resolved, DateTime.UtcNow);
        return resolved;
    }

    public async Task SetAsync(Guid tenantId, TenantBranding branding, CancellationToken ct = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PlanscapeDbContext>();
        var existing = await db.Set<TenantBranding>().FirstOrDefaultAsync(b => b.TenantId == tenantId, ct);
        if (existing == null)
        {
            branding.TenantId = tenantId;
            branding.CreatedAt = DateTime.UtcNow;
            branding.UpdatedAt = DateTime.UtcNow;
            db.Set<TenantBranding>().Add(branding);
        }
        else
        {
            existing.ProductName = branding.ProductName;
            existing.AccentColor = branding.AccentColor;
            existing.HeaderColor = branding.HeaderColor;
            existing.LogoUrl = branding.LogoUrl;
            existing.SupportEmail = branding.SupportEmail;
            existing.EmailFromName = branding.EmailFromName;
            existing.EmailFromAddress = branding.EmailFromAddress;
            existing.EmailSignature = branding.EmailSignature;
            existing.DefaultLanguage = branding.DefaultLanguage;
            existing.UpdatedAt = DateTime.UtcNow;
            existing.UpdatedByUserId = branding.UpdatedByUserId;
        }
        await db.SaveChangesAsync(ct);
        InvalidateCache(tenantId);
    }

    public void InvalidateCache(Guid? tenantId = null)
    {
        if (tenantId.HasValue) _cache.TryRemove(tenantId.Value, out _);
        else _cache.Clear();
    }

    private ResolvedBranding Resolve(TenantBranding? row)
    {
        // Layer: row → config → hardcoded. First non-empty wins.
        string Pick(string? rowVal, string configKey, string fallback) =>
            !string.IsNullOrWhiteSpace(rowVal) ? rowVal!
            : !string.IsNullOrWhiteSpace(_config[$"Tenant:DefaultBranding:{configKey}"]) ? _config[$"Tenant:DefaultBranding:{configKey}"]!
            : fallback;

        string? PickOptional(string? rowVal, string configKey) =>
            !string.IsNullOrWhiteSpace(rowVal) ? rowVal
            : !string.IsNullOrWhiteSpace(_config[$"Tenant:DefaultBranding:{configKey}"]) ? _config[$"Tenant:DefaultBranding:{configKey}"]
            : null;

        return new ResolvedBranding(
            ProductName:      Pick(row?.ProductName, "ProductName", "Planscape"),
            AccentColor:      Pick(row?.AccentColor, "AccentColor", "#E8912D"),
            HeaderColor:      Pick(row?.HeaderColor, "HeaderColor", "#1A237E"),
            LogoUrl:          PickOptional(row?.LogoUrl, "LogoUrl"),
            SupportEmail:     Pick(row?.SupportEmail, "SupportEmail", "support@planscape.io"),
            EmailFromName:    Pick(row?.EmailFromName, "EmailFromName",
                                   Pick(null, "ProductName", "Planscape")),
            EmailFromAddress: Pick(row?.EmailFromAddress, "EmailFromAddress",
                                   _config["Smtp:FromAddress"] ?? _config["Email:FromAddress"] ?? "no-reply@planscape.io"),
            EmailSignature:   PickOptional(row?.EmailSignature, "EmailSignature"),
            DefaultLanguage:  Pick(row?.DefaultLanguage, "DefaultLanguage", "en"));
    }
}
