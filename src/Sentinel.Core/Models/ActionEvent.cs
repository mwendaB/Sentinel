namespace Sentinel.Core.Models;

/// <summary>
/// Represents a recorded remediation action event.
/// </summary>
public sealed record ActionEvent
{
    /// <summary>
    /// Human-readable description of the action.
    /// </summary>
    public required string Description { get; init; }

    /// <summary>
    /// Confidence score (0-100).
    /// </summary>
    public required int Confidence { get; init; }

    /// <summary>
    /// Timestamp when the action occurred.
    /// </summary>
    public required DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Action execution status.
    /// </summary>
    public required ActionStatus Status { get; init; }
}
