namespace Sentinel.Core.Models;

/// <summary>
/// Represents the outcome of a remediation action.
/// </summary>
public sealed record RemediationActionResult
{
    /// <summary>
    /// Identifier of the action that was executed.
    /// </summary>
    public required Guid ActionId { get; init; }

    /// <summary>
    /// Result status for the execution.
    /// </summary>
    public required ActionStatus Status { get; init; }

    /// <summary>
    /// Timestamp when the result was recorded.
    /// </summary>
    public required DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Optional details or error information.
    /// </summary>
    public string? Details { get; init; }
}

public enum ActionStatus
{
    Success,
    InProgress,
    Failed,
    Skipped
}
