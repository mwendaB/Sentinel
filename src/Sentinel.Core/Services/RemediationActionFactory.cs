namespace Sentinel.Core.Services;

using Sentinel.Core.Models;

/// <summary>
/// Builds remediation actions from definitions with validation.
/// </summary>
public static class RemediationActionFactory
{
    public static bool TryCreate(
        RemediationActionDefinition definition,
        string? fallbackTarget,
        out RemediationAction action,
        out string error)
    {
        action = null!;
        error = string.Empty;

        if (definition is null)
        {
            error = "Remediation action definition is required.";
            return false;
        }

        var target = string.IsNullOrWhiteSpace(definition.TargetResource) ? fallbackTarget : definition.TargetResource;
        if (string.IsNullOrWhiteSpace(target))
        {
            error = "TargetResource is required.";
            return false;
        }

        if (definition.Confidence is < 0 or > 100)
        {
            error = "Confidence must be between 0 and 100.";
            return false;
        }

        switch (definition.ActionType)
        {
            case ActionType.RestartService:
            {
                var serviceName = string.IsNullOrWhiteSpace(definition.ServiceName) ? target : definition.ServiceName;
                if (string.IsNullOrWhiteSpace(serviceName))
                {
                    error = "ServiceName is required for RestartService actions.";
                    return false;
                }

                action = new RestartServiceAction
                {
                    Id = Guid.NewGuid(),
                    ActionType = definition.ActionType,
                    TargetResource = target,
                    Confidence = definition.Confidence,
                    Reason = definition.Reason,
                    ServiceName = serviceName
                };
                return true;
            }
            case ActionType.ScaleReplicas:
            {
                if (!definition.DesiredReplicas.HasValue)
                {
                    error = "DesiredReplicas is required for ScaleReplicas actions.";
                    return false;
                }

                action = new ScaleResourceAction
                {
                    Id = Guid.NewGuid(),
                    ActionType = definition.ActionType,
                    TargetResource = target,
                    Confidence = definition.Confidence,
                    Reason = definition.Reason,
                    DesiredReplicas = definition.DesiredReplicas.Value
                };
                return true;
            }
            case ActionType.SendNotification:
            {
                if (!definition.Channel.HasValue || string.IsNullOrWhiteSpace(definition.Message))
                {
                    error = "Channel and Message are required for SendNotification actions.";
                    return false;
                }

                action = new SendNotificationAction
                {
                    Id = Guid.NewGuid(),
                    ActionType = definition.ActionType,
                    TargetResource = target,
                    Confidence = definition.Confidence,
                    Reason = definition.Reason,
                    Channel = definition.Channel.Value,
                    Message = definition.Message
                };
                return true;
            }
            default:
                error = $"Action type {definition.ActionType} is not supported yet.";
                return false;
        }
    }
}
