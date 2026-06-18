using System;
using System.Linq;
using System.Reflection;
using Planscape.Core.Entities;
using Xunit;

namespace Planscape.Tests;

/// <summary>
/// CP-2 (review-hardening) regression guard. The DbContext applies a global
/// tenant query filter + auto-stamp + auto-index to every <see cref="ITenantScoped"/>
/// entity. An entity that carries a <c>TenantId</c> column but forgets to
/// implement <c>ITenantScoped</c> silently opts out of that safety net — exactly
/// the gap CP-2 closed for InformationDeliverable / SiteDiaryAttachment /
/// MeetingAttendee / MeetingAgendaItem / MeetingActionItem.
///
/// This test fails the build if any new entity reintroduces the gap.
/// </summary>
public class TenantScopedEntityConventionTests
{
    [Fact]
    public void Every_entity_with_a_TenantId_property_implements_ITenantScoped()
    {
        var assembly = typeof(ITenantScoped).Assembly; // Planscape.Core

        var offenders = assembly.GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract
                        && t.Namespace == "Planscape.Core.Entities")
            .Where(HasOwnTenantIdProperty)
            .Where(t => !typeof(ITenantScoped).IsAssignableFrom(t))
            .Select(t => t.Name)
            .OrderBy(n => n)
            .ToArray();

        Assert.True(offenders.Length == 0,
            "Entities with a TenantId property must implement ITenantScoped so the global "
            + "tenant query filter + auto-stamp covers them. Offenders: "
            + string.Join(", ", offenders));
    }

    // A writable instance Guid TenantId property (the shape ITenantScoped requires
    // and the DbContext filter/stamp keys off).
    private static bool HasOwnTenantIdProperty(Type t)
    {
        var p = t.GetProperty("TenantId",
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.FlattenHierarchy);
        return p != null && p.PropertyType == typeof(Guid) && p.CanRead && p.CanWrite;
    }
}
