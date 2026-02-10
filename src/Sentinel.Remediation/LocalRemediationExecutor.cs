using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sentinel.Core.Interfaces;
using Sentinel.Core.Models;

namespace Sentinel.Remediation;

public sealed class LocalRemediationExecutor : IRemediationExecutor
{
    private readonly ILogger<LocalRemediationExecutor> _logger;
    private readonly RemediationOptions _options;
    private readonly CommandRunner _commandRunner = new();

    public LocalRemediationExecutor(ILogger<LocalRemediationExecutor> logger, IOptions<RemediationOptions> options)
    {
        _logger = logger;
        _options = options.Value;
    }

    public async Task<RemediationActionResult> ExecuteAsync(RemediationAction action, CancellationToken cancellationToken)
    {
        if (!_options.Enabled)
        {
            return BuildResult(action, ActionStatus.Skipped, "Remediation execution disabled.");
        }

        try
        {
            return action switch
            {
                RestartServiceAction restartAction => await ExecuteRestartAsync(restartAction, cancellationToken),
                ScaleResourceAction scaleAction => await ExecuteScaleAsync(scaleAction, cancellationToken),
                SendNotificationAction notificationAction => await ExecuteNotificationAsync(notificationAction, cancellationToken),
                _ => BuildResult(action, ActionStatus.Skipped, $"Unsupported action type: {action.ActionType}.")
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to execute remediation action {ActionId}.", action.Id);
            return BuildResult(action, ActionStatus.Failed, ex.Message);
        }
    }

    public async Task<ExecutorHealth> CheckHealthAsync(CancellationToken cancellationToken)
    {
        if (!_options.Enabled)
        {
            return ExecutorHealth.Unknown;
        }

        var timeout = TimeSpan.FromSeconds(_options.CommandTimeoutSeconds);
        var degraded = false;

        if (_options.EnableServiceRestart)
        {
            if (OperatingSystem.IsWindows())
            {
                degraded = true;
            }
            else if (OperatingSystem.IsLinux())
            {
                var hasSystemctl = await _commandRunner.CommandExistsAsync("systemctl", timeout, cancellationToken);
                var hasService = await _commandRunner.CommandExistsAsync("service", timeout, cancellationToken);
                if (!hasSystemctl && !hasService)
                {
                    degraded = true;
                }
            }
            else if (OperatingSystem.IsMacOS())
            {
                var hasLaunchctl = await _commandRunner.CommandExistsAsync("launchctl", timeout, cancellationToken);
                if (!hasLaunchctl)
                {
                    degraded = true;
                }
            }
        }

        if (_options.EnableScaling)
        {
            var hasKubectl = await _commandRunner.CommandExistsAsync("kubectl", timeout, cancellationToken);
            if (!hasKubectl)
            {
                degraded = true;
            }
        }

        return degraded ? ExecutorHealth.Degraded : ExecutorHealth.Healthy;
    }

    private async Task<RemediationActionResult> ExecuteRestartAsync(
        RestartServiceAction action,
        CancellationToken cancellationToken)
    {
        if (!_options.EnableServiceRestart)
        {
            return BuildResult(action, ActionStatus.Skipped, "Service restart disabled by configuration.");
        }

        var timeout = TimeSpan.FromSeconds(_options.CommandTimeoutSeconds);

        if (OperatingSystem.IsLinux())
        {
            if (await _commandRunner.CommandExistsAsync("systemctl", timeout, cancellationToken))
            {
                return await ExecuteCommandAsync(action, "systemctl", $"restart {action.ServiceName}", timeout, cancellationToken);
            }

            if (await _commandRunner.CommandExistsAsync("service", timeout, cancellationToken))
            {
                return await ExecuteCommandAsync(action, "service", $"{action.ServiceName} restart", timeout, cancellationToken);
            }

            return BuildResult(action, ActionStatus.Failed, "No service manager found (systemctl/service).");
        }

        if (OperatingSystem.IsMacOS())
        {
            if (!await _commandRunner.CommandExistsAsync("launchctl", timeout, cancellationToken))
            {
                return BuildResult(action, ActionStatus.Failed, "launchctl not available.");
            }

            var systemResult = await ExecuteCommandAsync(action, "launchctl", $"kickstart -k system/{action.ServiceName}", timeout, cancellationToken);
            if (systemResult.Status == ActionStatus.Success)
            {
                return systemResult;
            }

            var uid = await _commandRunner.GetUidAsync(timeout, cancellationToken);
            if (string.IsNullOrWhiteSpace(uid))
            {
                return systemResult;
            }

            return await ExecuteCommandAsync(action, "launchctl", $"kickstart -k gui/{uid}/{action.ServiceName}", timeout, cancellationToken);
        }

        return BuildResult(action, ActionStatus.Failed, "Service restart not supported on this platform.");
    }

    private async Task<RemediationActionResult> ExecuteScaleAsync(
        ScaleResourceAction action,
        CancellationToken cancellationToken)
    {
        if (!_options.EnableScaling)
        {
            return BuildResult(action, ActionStatus.Skipped, "Scaling disabled by configuration.");
        }

        if (string.IsNullOrWhiteSpace(action.TargetResource))
        {
            return BuildResult(action, ActionStatus.Failed, "Target resource is required for scaling.");
        }

        var timeout = TimeSpan.FromSeconds(_options.CommandTimeoutSeconds);
        if (!await _commandRunner.CommandExistsAsync("kubectl", timeout, cancellationToken))
        {
            return BuildResult(action, ActionStatus.Failed, "kubectl not available.");
        }

        return await ExecuteCommandAsync(
            action,
            "kubectl",
            $"scale {action.TargetResource} --replicas {action.DesiredReplicas}",
            timeout,
            cancellationToken);
    }

    private async Task<RemediationActionResult> ExecuteNotificationAsync(
        SendNotificationAction action,
        CancellationToken cancellationToken)
    {
        if (!_options.EnableNotifications)
        {
            return BuildResult(action, ActionStatus.Skipped, "Notifications disabled by configuration.");
        }

        if (!string.IsNullOrWhiteSpace(_options.NotificationLogPath))
        {
            var line = $"{DateTimeOffset.UtcNow:O} [{action.Channel}] {action.Message}{Environment.NewLine}";
            await File.AppendAllTextAsync(_options.NotificationLogPath, line, cancellationToken);
        }

        _logger.LogInformation("Notification via {Channel} for {Target}: {Message}", action.Channel, action.TargetResource, action.Message);
        return BuildResult(action, ActionStatus.Success, "Notification recorded.");
    }

    private async Task<RemediationActionResult> ExecuteCommandAsync(
        RemediationAction action,
        string command,
        string arguments,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Executing remediation command: {Command} {Arguments}", command, arguments);
        var result = await _commandRunner.RunAsync(command, arguments, timeout, cancellationToken);

        if (result.ExitCode == 0)
        {
            return BuildResult(action, ActionStatus.Success, string.IsNullOrWhiteSpace(result.StandardOutput) ? "Command executed." : result.StandardOutput.Trim());
        }

        var details = string.IsNullOrWhiteSpace(result.StandardError) ? "Command failed." : result.StandardError.Trim();
        return BuildResult(action, ActionStatus.Failed, details);
    }

    private static RemediationActionResult BuildResult(RemediationAction action, ActionStatus status, string details) =>
        new()
        {
            ActionId = action.Id,
            Status = status,
            Timestamp = DateTimeOffset.UtcNow,
            Details = details
        };
}
