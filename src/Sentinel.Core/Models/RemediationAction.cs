namespace Sentinel.Core.Models;

/// <summary>
/// Base class for all remediation actions.
/// </summary>
public abstract record RemediationAction
{
    private int _confidence;

    /// <summary>
    /// Unique identifier for this action.
    /// </summary>
    public required Guid Id { get; init; } = Guid.NewGuid();

    /// <summary>
    /// Type of action to perform.
    /// </summary>
    public required ActionType ActionType { get; init; }

    /// <summary>
    /// Target resource (service name, pod name, etc.).
    /// </summary>
    public required string TargetResource { get; init; }

    /// <summary>
    /// Confidence score (0-100).
    /// </summary>
    public required int Confidence
    {
        get => _confidence;
        init
        {
            if (value is < 0 or > 100)
            {
                throw new ArgumentOutOfRangeException(nameof(Confidence), "Confidence must be between 0 and 100.");
            }

            _confidence = value;
        }
    }

    /// <summary>
    /// Reason for taking this action.
    /// </summary>
    public string? Reason { get; init; }
}

/// <summary>
/// Action to restart a service.
/// </summary>
public sealed record RestartServiceAction : RemediationAction
{
    public required string ServiceName { get; init; }
}

/// <summary>
/// Action to scale a resource.
/// </summary>
public sealed record ScaleResourceAction : RemediationAction
{
    public required int DesiredReplicas { get; init; }
}

/// <summary>
/// Action to send a notification.
/// </summary>
public sealed record SendNotificationAction : RemediationAction
{
    public required NotificationChannel Channel { get; init; }
    public required string Message { get; init; }
}

public enum ActionType
{
    RestartService,
    RestartContainer,
    ScaleReplicas,
    ClearCache,
    SendNotification,
    RunScript,
    UpdateConfiguration
}

public enum NotificationChannel
{
    Email,
    Slack,
    Teams,
    PagerDuty,
    SMS
}
