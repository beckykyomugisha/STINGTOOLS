using Planscape.Core.Interfaces;

namespace Planscape.Infrastructure.Billing;

/// <summary>
/// S2.2/S2.3 — picks the right <see cref="IPaymentProvider"/> based on the
/// tenant's <c>Currency</c>. Centralised so signup, checkout, and the
/// webhook controller all agree on the routing.
/// </summary>
public class PaymentRouter
{
    private readonly IEnumerable<IPaymentProvider> _providers;
    public PaymentRouter(IEnumerable<IPaymentProvider> providers) { _providers = providers; }

    public IPaymentProvider RouteByCurrency(string currency)
    {
        var hit = _providers.FirstOrDefault(p => p.Supports(currency));
        if (hit != null) return hit;
        throw new InvalidOperationException(
            $"No payment provider supports currency '{currency}'. " +
            $"Wired providers: [{string.Join(", ", _providers.Select(p => p.Name))}].");
    }

    public IPaymentProvider RouteByName(string providerName)
    {
        var hit = _providers.FirstOrDefault(p => string.Equals(p.Name, providerName, StringComparison.OrdinalIgnoreCase));
        if (hit != null) return hit;
        throw new InvalidOperationException(
            $"Unknown payment provider '{providerName}'. Wired: [{string.Join(", ", _providers.Select(p => p.Name))}].");
    }
}
