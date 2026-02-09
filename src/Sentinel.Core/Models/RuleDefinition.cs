namespace Sentinel.Core.Models;

/// <summary>
/// Defines a rule used to detect patterns and trigger actions.
/// </summary>
public sealed record RuleDefinition
{
    private string _name = string.Empty;
    private string _pattern = string.Empty;

    /// <summary>
    /// Unique identifier for this rule.
    /// </summary>
    public required Guid Id { get; init; } = Guid.NewGuid();

    /// <summary>
    /// Friendly name of the rule.
    /// </summary>
    public required string Name
    {
        get => _name;
        init
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException("Name cannot be empty", nameof(Name));
            }

            _name = value;
        }
    }

    /// <summary>
    /// Human-readable description of the rule.
    /// </summary>
    public string? Description { get; init; }

    /// <summary>
    /// Whether the rule is active.
    /// </summary>
    public required bool Enabled { get; init; }

    /// <summary>
    /// Regex pattern used to match log messages.
    /// </summary>
    public required string Pattern
    {
        get => _pattern;
        init
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException("Pattern cannot be empty", nameof(Pattern));
            }

            _pattern = value;
        }
    }

    /// <summary>
    /// Minimum log level required to evaluate the rule.
    /// </summary>
    public required LogLevel MinimumLevel { get; init; }

    /// <summary>
    /// Optional source types this rule applies to.
    /// </summary>
    public IReadOnlyCollection<SourceType>? SourceTypes { get; init; }
}
