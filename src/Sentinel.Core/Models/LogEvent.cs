namespace Sentinel.Core.Models;

/// <summary>
/// Represents a log event from any source.
/// </summary>
public sealed record LogEvent
{
    private Guid _id = Guid.NewGuid();
    private DateTimeOffset _timestamp = DateTimeOffset.UtcNow;
    private string _message = string.Empty;

    /// <summary>
    /// Unique identifier for this log event.
    /// </summary>
    public required Guid Id
    {
        get => _id;
        init => _id = value == Guid.Empty ? Guid.NewGuid() : value;
    }

    /// <summary>
    /// Timestamp when the event occurred.
    /// </summary>
    public required DateTimeOffset Timestamp
    {
        get => _timestamp;
        init => _timestamp = value == default ? DateTimeOffset.UtcNow : value;
    }

    /// <summary>
    /// Severity level of the event.
    /// </summary>
    public required LogLevel Level { get; init; }

    /// <summary>
    /// Source that generated this event.
    /// </summary>
    public required LogSource Source { get; init; }

    /// <summary>
    /// Human-readable message.
    /// </summary>
    public required string Message
    {
        get => _message;
        init
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException("Message cannot be empty", nameof(Message));
            }

            _message = value;
        }
    }

    /// <summary>
    /// Additional structured metadata.
    /// </summary>
    public IReadOnlyDictionary<string, object?>? Metadata { get; init; }
}

/// <summary>
/// Log severity levels.
/// </summary>
public enum LogLevel
{
    Trace,
    Debug,
    Information,
    Warning,
    Error,
    Critical
}
