namespace Sentinel.Core.Models;

/// <summary>
/// Represents a request to create a rule.
/// </summary>
public sealed record NewRuleRequest
{
    public required string Name { get; init; }
    public required string Pattern { get; init; }
    public required LogLevel MinimumLevel { get; init; }
    public required bool Enabled { get; init; }
}
