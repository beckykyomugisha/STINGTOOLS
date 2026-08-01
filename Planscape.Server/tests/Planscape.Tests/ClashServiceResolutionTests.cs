using Microsoft.Extensions.DependencyInjection;
using Planscape.Infrastructure.Services;

namespace Planscape.Tests;

/// <summary>
/// Regression guard for a DI break that reached production TWICE.
///
/// ClashesController takes IClashDetectionJob. That was unregistered, so the
/// container could not build the controller and every /clashes endpoint
/// returned 500 (which the browser mis-reported as CORS, because an ASP.NET
/// 500 loses its CORS headers). The first fix registered IClashDetectionJob
/// — but ClashDetectionJob itself takes IClashAutomationService, which was
/// still unregistered, so the container failed one level DEEPER and the
/// endpoint kept returning the same 500:
///
///   System.InvalidOperationException: Unable to resolve service for type
///   'IClashAutomationService' while attempting to activate 'ClashDetectionJob'.
///
/// A compile never catches this — the graph is only proven at RESOLVE time.
/// Resolving the root walks the WHOLE chain (job → automation service → db +
/// notifications + webhooks), so any future link that goes missing fails here
/// instead of in production.
/// </summary>
public class ClashServiceResolutionTests : IClassFixture<PlanscapeWebApplicationFactory>
{
    private readonly PlanscapeWebApplicationFactory _factory;

    public ClashServiceResolutionTests(PlanscapeWebApplicationFactory factory)
        => _factory = factory;

    [Fact]
    public void ClashDetectionJob_ResolvesWithItsWholeDependencyChain()
    {
        using var scope = _factory.Services.CreateScope();

        var job = scope.ServiceProvider.GetRequiredService<IClashDetectionJob>();

        Assert.NotNull(job);
    }

    [Fact]
    public void ClashAutomationService_IsRegistered()
    {
        using var scope = _factory.Services.CreateScope();

        var automation = scope.ServiceProvider.GetRequiredService<IClashAutomationService>();

        Assert.NotNull(automation);
    }
}
