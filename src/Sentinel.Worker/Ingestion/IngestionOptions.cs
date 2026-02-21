namespace Sentinel.Worker.Ingestion;

public sealed class IngestionOptions
{
    public const string SectionName = "Ingestion";

    public FileTailOptions FileTail { get; init; } = new();
    public JournaldOptions Journald { get; init; } = new();
    public MacUnifiedOptions MacUnified { get; init; } = new();
    public WindowsEventLogOptions WindowsEventLog { get; init; } = new();
    public SyslogOptions Syslog { get; init; } = new();
    public KafkaOptions Kafka { get; init; } = new();
    public DockerOptions Docker { get; init; } = new();
    public KubernetesOptions Kubernetes { get; init; } = new();
    public SyntheticOptions Synthetic { get; init; } = new();
}

public sealed class FileTailOptions
{
    public bool Enabled { get; init; } = true;
    public string[] Paths { get; init; } = Array.Empty<string>();
}

public sealed class JournaldOptions
{
    public bool Enabled { get; init; } = true;
}

public sealed class MacUnifiedOptions
{
    public bool Enabled { get; init; } = true;
}

public sealed class WindowsEventLogOptions
{
    public bool Enabled { get; init; } = true;
    public string[] LogNames { get; init; } = ["Application", "System"];
    public int PollIntervalSeconds { get; init; } = 4;
}

public sealed class SyslogOptions
{
    public bool Enabled { get; init; } = false;
    public string BindAddress { get; init; } = "0.0.0.0";
    public int Port { get; init; } = 5140;
}

public sealed class KafkaOptions
{
    public bool Enabled { get; init; } = false;
    public string BootstrapServers { get; init; } = string.Empty;
    public string GroupId { get; init; } = "sentinel-ingestion";
    public string[] Topics { get; init; } = Array.Empty<string>();
    public string AutoOffsetReset { get; init; } = "Latest";
}

public sealed class DockerOptions
{
    public bool Enabled { get; init; } = false;
    public string[] Containers { get; init; } = Array.Empty<string>();
    public int SinceSeconds { get; init; } = 60;
    public bool IncludeTimestamps { get; init; } = true;
}

public sealed class KubernetesOptions
{
    public bool Enabled { get; init; } = false;
    public string Namespace { get; init; } = "default";
    public string[] Pods { get; init; } = Array.Empty<string>();
    public string? Container { get; init; }
    public int SinceSeconds { get; init; } = 60;
    public bool IncludeTimestamps { get; init; } = true;
}

public sealed class SyntheticOptions
{
    public bool Enabled { get; init; } = false;
}
