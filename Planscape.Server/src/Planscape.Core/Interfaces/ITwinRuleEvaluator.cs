namespace Planscape.Core.Interfaces;

/// <summary>
/// Pillar B (6A) — evaluates twin rules against an ingest batch, firing
/// TwinAlerts + auto work orders. Registered as a no-op in 5A so the ingest
/// path is stable; 6A swaps in the real TwinRuleEngine with zero controller
/// change.
/// </summary>
public interface ITwinRuleEvaluator
{
    Task EvaluateAsync(
        Guid projectId, IReadOnlyList<TelemetryReading> readings, CancellationToken ct = default);
}

/// <summary>5A placeholder — does nothing until 6A registers the real engine.</summary>
public sealed class NoOpTwinRuleEvaluator : ITwinRuleEvaluator
{
    public Task EvaluateAsync(
        Guid projectId, IReadOnlyList<TelemetryReading> readings, CancellationToken ct = default)
        => Task.CompletedTask;
}
