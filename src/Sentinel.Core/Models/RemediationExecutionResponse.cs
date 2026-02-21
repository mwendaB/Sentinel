namespace Sentinel.Core.Models;

/// <summary>
/// Represents the response from executing or dry-running a remediation action.
/// </summary>
public sealed record RemediationExecutionResponse
{
    public required RemediationActionResult Result { get; init; }
    public ActionEvent? ActionEvent { get; init; }
}
