namespace Sentinel.Core.Models;

/// <summary>
/// Identifies the source of a log event.
/// </summary>
public sealed record LogSource
{
    /// <summary>
    /// Name of the source (e.g., "nginx", "Application").
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Type of source (EventLog, Syslog, Docker, etc.).
    /// </summary>
    public required SourceType Type { get; init; }

    /// <summary>
    /// Platform where this source exists.
    /// </summary>
    public required Platform Platform { get; init; }

    public override string ToString() => $"{Name} ({Type} on {Platform})";
}

public enum SourceType
{
    WindowsEventLog,
    WindowsPerformanceCounter,
    UnifiedLog,
    Journald,
    Syslog,
    DockerContainer,
    KubernetesPod,
    HttpEndpoint,
    KafkaTopic,
    File
}

public enum Platform
{
    Windows,
    MacOS,
    Linux,
    Universal
}
