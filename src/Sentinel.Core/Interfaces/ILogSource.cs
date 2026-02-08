using Sentinel.Core.Models;

namespace Sentinel.Core.Interfaces;

/// <summary>
/// Abstraction for reading logs from any source.
/// </summary>
public interface ILogSource
{
    /// <summary>
    /// Stream of log events from this source.
    /// </summary>
    IAsyncEnumerable<LogEvent> ReadLogsAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Check health status of this source.
    /// </summary>
    Task<SourceHealth> CheckHealthAsync(CancellationToken cancellationToken);
}

public enum SourceHealth
{
    Healthy,
    Degraded,
    Unhealthy,
    Unknown
}
