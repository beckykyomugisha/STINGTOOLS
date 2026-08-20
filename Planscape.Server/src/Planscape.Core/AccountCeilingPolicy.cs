using Planscape.Core.Entities;

namespace Planscape.Core;

/// <summary>
/// The one place that decides whether a tenant may add another account.
///
/// <para>Exists because the two enforcement sites (<c>AdminController.CreateUser</c>
/// and <c>ProjectMembersController</c>) each open-coded
/// <c>userCount &gt;= tenant.MaxUsers</c>, and that comparison cannot express
/// "unlimited": every non-positive value — the <c>-1</c> sentinel this repo already
/// uses in <see cref="TierLimits"/>, a <c>0</c> from a partially-populated row, or the
/// <c>-2</c> that a wrapped seat sum produced in #616 — is <c>&gt;=</c> true at zero
/// users and denies the tenant its FIRST account, reporting a limit that points at
/// nothing.</para>
///
/// <para>So this fails OPEN on a nonsensical cap. That is the deliberate direction:
/// <see cref="Tenant.MaxUsers"/> is an anti-abuse ceiling, not a billing gate (#653),
/// and letting a misconfigured tenant over-provision is strictly less harmful than
/// locking a paying customer out of their own organisation with an unactionable
/// message.</para>
/// </summary>
public static class AccountCeilingPolicy
{
    /// <summary>
    /// True if a tenant currently holding <paramref name="activeUserCount"/> active
    /// accounts may add one more. A non-positive <paramref name="maxUsers"/> means
    /// unlimited.
    /// </summary>
    public static bool Allows(int activeUserCount, int maxUsers)
        => maxUsers <= 0 || activeUserCount < maxUsers;

    /// <inheritdoc cref="Allows(int,int)"/>
    public static bool Allows(int activeUserCount, Tenant? tenant)
        => tenant == null || Allows(activeUserCount, tenant.MaxUsers);

    /// <summary>
    /// Human-readable cap for an error message. Never renders a non-positive value,
    /// which would put "User limit (-2) reached" in front of a customer.
    /// </summary>
    public static string Label(int maxUsers)
        => maxUsers <= 0 ? "unlimited" : maxUsers.ToString();
}
