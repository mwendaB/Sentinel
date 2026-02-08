using Sentinel.Core.Models;

namespace Sentinel.Core.Interfaces;

/// <summary>
/// Executes remediation actions on target resources.
/// </summary>
public interface IRemediationExecutor
{
    /// <summary>
    /// Execute a remediation action and return the result.
    /// </summary>
    Task<RemediationActionResult> ExecuteAsync(
        RemediationAction action,
        CancellationToken cancellationToken);

    /// <summary>
    /// Check health status of this executor.
    /// </summary>
    Task<ExecutorHealth> CheckHealthAsync(CancellationToken cancellationToken);
}

public enum ExecutorHealth
{
    Healthy,
    Degraded,
    Unhealthy,
    Unknown
}
