using System.Collections.Concurrent;
using Sentinel.Core.Models;
using Sentinel.Web.Hubs;
using Models = Sentinel.Core.Models;

namespace Sentinel.Web.Services;

public sealed class LogStreamState
{
    private readonly ConcurrentQueue<LogEvent> _events = new();
    private readonly ConcurrentQueue<ActionRecord> _actions = new();

    public event Action? Updated;

    public int EventsPerSecond { get; private set; } = 1247;
    public int ActiveRules { get; private set; } = 42;
    public int ActionsToday { get; private set; } = 156;
    public int ActiveSources { get; private set; } = 9;
    public int RulesEvaluated { get; private set; } = 1480;
    public int ActionSuccessRate { get; private set; } = 96;

    public IReadOnlyList<LogEvent> GetRecentEvents(int count) =>
        _events.Reverse().Take(count).Reverse().ToList();

    public IReadOnlyList<ActionRecord> GetRecentActions(int count) =>
        _actions.Reverse().Take(count).Reverse().ToList();

    public void AddEvent(LogEvent logEvent)
    {
        _events.Enqueue(logEvent);
        Trim(_events, 200);
        Updated?.Invoke();
    }

    public void AddAction(ActionRecord action)
    {
        _actions.Enqueue(action);
        Trim(_actions, 100);
        ActionsToday++;
        Updated?.Invoke();
    }

    public void UpdateMetrics(int eventsPerSecond)
    {
        EventsPerSecond = eventsPerSecond;
        Updated?.Invoke();
    }

    public void Seed()
    {
        if (_events.Count > 0)
        {
            return;
        }

        AddEvent(new LogEvent
        {
            Id = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow.AddSeconds(-12),
            Level = Models.LogLevel.Error,
            Message = "nginx connection timeout (x3)",
            Source = new LogSource { Name = "nginx", Platform = Platform.Linux, Type = SourceType.Syslog }
        });

        AddEvent(new LogEvent
        {
            Id = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow.AddSeconds(-28),
            Level = Models.LogLevel.Warning,
            Message = "postgres slow query detected",
            Source = new LogSource { Name = "postgres", Platform = Platform.Linux, Type = SourceType.Journald }
        });

        AddEvent(new LogEvent
        {
            Id = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow.AddSeconds(-44),
            Level = Models.LogLevel.Information,
            Message = "redis cache hit rate: 94%",
            Source = new LogSource { Name = "redis", Platform = Platform.Linux, Type = SourceType.DockerContainer }
        });

        AddAction(new ActionRecord("Restarted nginx", 95, DateTimeOffset.UtcNow.AddMinutes(-3), ActionStatus.Success));
        AddAction(new ActionRecord("Scaling database", 89, DateTimeOffset.UtcNow.AddMinutes(-7), ActionStatus.InProgress));
        AddAction(new ActionRecord("Notified on-call via Slack", 92, DateTimeOffset.UtcNow.AddMinutes(-12), ActionStatus.Success));
    }

    private static void Trim<T>(ConcurrentQueue<T> queue, int max)
    {
        while (queue.Count > max && queue.TryDequeue(out _))
        {
        }
    }
}
