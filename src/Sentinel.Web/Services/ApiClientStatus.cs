namespace Sentinel.Web.Services;

public sealed class ApiClientStatus
{
    private readonly object _lock = new();

    public event Action? Updated;

    public ApiStatusSnapshot Snapshot { get; private set; } = ApiStatusSnapshot.HealthySnapshot;

    public void MarkRetry(int attempt, TimeSpan delay, string reason)
    {
        var now = DateTimeOffset.UtcNow;
        Update(new ApiStatusSnapshot(
            Healthy: false,
            InBackoff: true,
            RetryAttempt: attempt,
            BackoffDelay: delay,
            NextRetryAt: now.Add(delay),
            LastError: reason,
            LastUpdated: now));
    }

    public void MarkFailure(string reason)
    {
        var now = DateTimeOffset.UtcNow;
        Update(new ApiStatusSnapshot(
            Healthy: false,
            InBackoff: false,
            RetryAttempt: 0,
            BackoffDelay: null,
            NextRetryAt: null,
            LastError: reason,
            LastUpdated: now));
    }

    public void MarkSuccess()
    {
        var now = DateTimeOffset.UtcNow;
        Update(new ApiStatusSnapshot(
            Healthy: true,
            InBackoff: false,
            RetryAttempt: 0,
            BackoffDelay: null,
            NextRetryAt: null,
            LastError: null,
            LastUpdated: now));
    }

    private void Update(ApiStatusSnapshot snapshot)
    {
        lock (_lock)
        {
            Snapshot = snapshot;
        }

        Updated?.Invoke();
    }
}

public sealed record ApiStatusSnapshot(
    bool Healthy,
    bool InBackoff,
    int RetryAttempt,
    TimeSpan? BackoffDelay,
    DateTimeOffset? NextRetryAt,
    string? LastError,
    DateTimeOffset LastUpdated)
{
    public static ApiStatusSnapshot HealthySnapshot { get; } =
        new(true, false, 0, null, null, null, DateTimeOffset.UtcNow);
}
