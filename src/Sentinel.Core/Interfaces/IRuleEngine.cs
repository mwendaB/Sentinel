using Sentinel.Core.Models;

namespace Sentinel.Core.Interfaces;

/// <summary>
/// Evaluates rules against log events and returns remediation actions.
/// </summary>
public interface IRuleEngine
{
    /// <summary>
    /// Evaluate rules against the provided log events.
    /// </summary>
    Task<IReadOnlyList<RemediationAction>> EvaluateAsync(
        IReadOnlyList<LogEvent> events,
        IReadOnlyList<RuleDefinition> rules,
        CancellationToken cancellationToken);
}
