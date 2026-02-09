namespace Sentinel.Core.Models;

/// <summary>
/// Represents the result of testing a rule against recent logs.
/// </summary>
public sealed record RuleTestResult
{
    public required Guid RuleId { get; init; }
    public required int MatchCount { get; init; }
    public required IReadOnlyList<LogEvent> Matches { get; init; }
}
