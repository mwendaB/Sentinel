namespace Sentinel.Core.Models;

/// <summary>
/// Represents a snapshot of system metrics.
/// </summary>
public sealed record MetricsSnapshot
{
    public required int EventsPerSecond { get; init; }
    public required int ActiveRules { get; init; }
    public required int ActionsToday { get; init; }
    public required int ActiveSources { get; init; }
    public required int RulesEvaluated { get; init; }
    public required int ActionSuccessRate { get; init; }
}
