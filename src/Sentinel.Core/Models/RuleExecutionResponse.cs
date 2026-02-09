namespace Sentinel.Core.Models;

/// <summary>
/// Represents the response from executing or dry-running a rule.
/// </summary>
public sealed record RuleExecutionResponse
{
    public required RemediationActionResult Result { get; init; }
    public ActionEvent? ActionEvent { get; init; }
}
