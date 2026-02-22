using System.Collections.Concurrent;
using Sentinel.Core.Models;
using Sentinel.Web.Hubs;
using Models = Sentinel.Core.Models;

namespace Sentinel.Web.Services;

public sealed class LogStreamState
{
    private readonly ConcurrentQueue<LogEvent> _events = new();
    private readonly ConcurrentQueue<ActionRecord> _actions = new();
    private readonly ConcurrentQueue<MetricSample> _metrics = new();

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

    public IReadOnlyList<MetricSample> GetRecentMetrics(int count) =>
        _metrics.Reverse().Take(count).Reverse().ToList();

    public void ReplaceMetrics(MetricsSnapshot metrics)
    {
        EventsPerSecond = metrics.EventsPerSecond;
        ActiveRules = metrics.ActiveRules;
        ActionsToday = metrics.ActionsToday;
        ActiveSources = metrics.ActiveSources;
        RulesEvaluated = metrics.RulesEvaluated;
        ActionSuccessRate = metrics.ActionSuccessRate;
        AddMetricSample(metrics);
        Updated?.Invoke();
    }

    public void ReplaceEvents(IReadOnlyList<LogEvent> events)
    {
        Clear(_events);
        foreach (var logEvent in events)
        {
            _events.Enqueue(logEvent);
        }
        Updated?.Invoke();
    }

    public void ReplaceActions(IReadOnlyList<ActionEvent> actions)
    {
        Clear(_actions);
        foreach (var action in actions)
        {
            _actions.Enqueue(new ActionRecord(action.Description, action.Confidence, action.Timestamp, action.Status));
        }
        Updated?.Invoke();
    }

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
        AddMetricSample(new MetricsSnapshot
        {
            EventsPerSecond = eventsPerSecond,
            ActiveRules = ActiveRules,
            ActionsToday = ActionsToday,
            ActiveSources = ActiveSources,
            RulesEvaluated = RulesEvaluated,
            ActionSuccessRate = ActionSuccessRate
        });
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

        AddMetricSample(new MetricsSnapshot
        {
            EventsPerSecond = EventsPerSecond,
            ActiveRules = ActiveRules,
            ActionsToday = ActionsToday,
            ActiveSources = ActiveSources,
            RulesEvaluated = RulesEvaluated,
            ActionSuccessRate = ActionSuccessRate
        });
    }

    private void AddMetricSample(MetricsSnapshot metrics)
    {
        _metrics.Enqueue(new MetricSample(DateTimeOffset.UtcNow, metrics.EventsPerSecond, metrics.ActionSuccessRate));
        Trim(_metrics, 120);
    }

    private static void Trim<T>(ConcurrentQueue<T> queue, int max)
    {
        while (queue.Count > max && queue.TryDequeue(out _))
        {
        }
    }

    private static void Clear<T>(ConcurrentQueue<T> queue)
    {
        while (queue.TryDequeue(out _))
        {
        }
    }
}

public sealed record MetricSample(
    DateTimeOffset Timestamp,
    int EventsPerSecond,
    int ActionSuccessRate);
