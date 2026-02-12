namespace Sentinel.Remediation;

public sealed record RemediationOptions
{
    public const string SectionName = "Remediation";

    public bool Enabled { get; init; } = true;
    public bool EnableServiceRestart { get; init; } = true;
    public bool EnableScaling { get; init; } = true;
    public bool EnableNotifications { get; init; } = true;
    public int CommandTimeoutSeconds { get; init; } = 20;
    public string? NotificationLogPath { get; init; }
}
