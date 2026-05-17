using Microsoft.Extensions.Configuration;

namespace Planscape.Infrastructure.Workflow;

/// <summary>
/// Phase 150 — platform-wide keyword extensions for deliverable state
/// machines. Sits below per-project <c>"keywords"</c> JSON in priority,
/// so an operator can ship vocabulary defaults across every tenant
/// without forcing each project to repeat them. Registered as a
/// singleton on <see cref="IServiceCollection"/> at startup; bound to
/// the <c>DeliverableStateMachine:Keywords</c> section of
/// <c>appsettings.json</c> via <see cref="ConfigPlatformKeywordRegistry"/>.
///
/// Layered priority (highest → lowest):
///   1. Project-level <c>"keywords"</c> in
///      <c>Project.CustomDeliverableStateMachineJson</c>
///   2. Platform <see cref="IPlatformKeywordRegistry.Keywords"/>
///      (this registry)
///   3. Built-in vocabulary on <see cref="DeliverableStateMachine"/>
/// </summary>
public interface IPlatformKeywordRegistry
{
    IReadOnlyDictionary<string, IReadOnlyCollection<string>> Keywords { get; }
}

/// <summary>
/// Reads platform keyword extensions from the
/// <c>DeliverableStateMachine:Keywords</c> appsettings section. Section
/// shape mirrors the per-project JSON block:
///   "DeliverableStateMachine": {
///     "Keywords": {
///       "working":  [ "PARKED", "WAITING_ON_X" ],
///       "terminal": [ "FROZEN", "DECOMMISSIONED" ]
///     }
///   }
/// Anything outside the six canonical role buckets is silently dropped
/// (so a typo in production config doesn't take down the API).
/// </summary>
public sealed class ConfigPlatformKeywordRegistry : IPlatformKeywordRegistry
{
    // Phase 154 — single source of truth on RoleBuckets.Set.
    private static IReadOnlySet<string> ValidRoles => RoleBuckets.Set;

    public IReadOnlyDictionary<string, IReadOnlyCollection<string>> Keywords { get; }

    public ConfigPlatformKeywordRegistry(Microsoft.Extensions.Configuration.IConfiguration config)
    {
        var section = config.GetSection("DeliverableStateMachine:Keywords");
        var result = new Dictionary<string, IReadOnlyCollection<string>>(StringComparer.OrdinalIgnoreCase);

        if (section.Exists())
        {
            foreach (var role in ValidRoles)
            {
                var roleSection = section.GetSection(role);
                if (!roleSection.Exists()) continue;

                var entries = roleSection.GetChildren()
                    .Select(c => c.Value)
                    .Where(v => !string.IsNullOrWhiteSpace(v))
                    .Select(v => v!.Trim().ToUpperInvariant())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
                if (entries.Count > 0)
                    result[role.ToLowerInvariant()] = entries.AsReadOnly();
            }
        }

        Keywords = result;
    }
}

/// <summary>
/// Empty registry used by tests / dev configurations where no
/// platform-wide keywords are configured. Keeps DI surface clean —
/// callers can always assume the service exists rather than
/// null-checking.
/// </summary>
public sealed class EmptyPlatformKeywordRegistry : IPlatformKeywordRegistry
{
    public IReadOnlyDictionary<string, IReadOnlyCollection<string>> Keywords { get; }
        = new Dictionary<string, IReadOnlyCollection<string>>();
}
