namespace Sentinel.Worker.Ingestion;

public sealed class IngestionOptions
{
    public const string SectionName = "Ingestion";

    public FileTailOptions FileTail { get; init; } = new();
    public JournaldOptions Journald { get; init; } = new();
    public MacUnifiedOptions MacUnified { get; init; } = new();
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

public sealed class SyntheticOptions
{
    public bool Enabled { get; init; } = false;
}
