namespace Sentinel.Core.Models;

/// <summary>
/// Defines a remediation action for rule execution or manual triggering.
/// </summary>
public sealed record RemediationActionDefinition
{
    /// <summary>
    /// Type of action to perform.
    /// </summary>
    public required ActionType ActionType { get; init; }

    /// <summary>
    /// Target resource (service name, pod name, etc.).
    /// </summary>
    public string? TargetResource { get; init; }

    /// <summary>
    /// Confidence score (0-100).
    /// </summary>
    public int Confidence { get; init; } = 90;

    /// <summary>
    /// Reason for taking this action.
    /// </summary>
    public string? Reason { get; init; }

    /// <summary>
    /// Optional service name for restart actions.
    /// </summary>
    public string? ServiceName { get; init; }

    /// <summary>
    /// Desired replicas for scale actions.
    /// </summary>
    public int? DesiredReplicas { get; init; }

    /// <summary>
    /// Notification channel for notification actions.
    /// </summary>
    public NotificationChannel? Channel { get; init; }

    /// <summary>
    /// Message for notification actions.
    /// </summary>
    public string? Message { get; init; }
}
