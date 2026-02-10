using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using Sentinel.Core.Models;
using Models = Sentinel.Core.Models;

namespace Sentinel.Api.Services;

public sealed class LogDataStore
{
    private readonly ConcurrentQueue<LogEvent> _events = new();
    private readonly ConcurrentQueue<ActionEvent> _actions = new();
    private readonly List<RuleDefinition> _rules = new();

    private int _eventsPerSecond = 1247;
    private int _activeSources = 9;
    private int _rulesEvaluated = 1480;
    private int _actionSuccessRate = 96;

    public LogDataStore()
    {
        SeedRules();
    }

    public IReadOnlyList<RuleDefinition> Rules => _rules;

    public MetricsSnapshot GetMetrics() => new()
    {
        EventsPerSecond = _eventsPerSecond,
        ActiveRules = _rules.Count(rule => rule.Enabled),
        ActionsToday = _actions.Count,
        ActiveSources = _activeSources,
        RulesEvaluated = _rulesEvaluated,
        ActionSuccessRate = _actionSuccessRate
    };

    public IReadOnlyList<LogEvent> GetLogs(int count) =>
        _events.Reverse().Take(count).Reverse().ToList();

    public IReadOnlyList<ActionEvent> GetActions(int count) =>
        _actions.Reverse().Take(count).Reverse().ToList();

    public void AddLog(LogEvent logEvent)
    {
        _events.Enqueue(logEvent);
        Trim(_events, 500);
        _eventsPerSecond = Math.Max(200, _eventsPerSecond + Random.Shared.Next(-80, 120));
    }

    public void AddAction(ActionEvent actionEvent)
    {
        _actions.Enqueue(actionEvent);
        Trim(_actions, 200);
    }

    public RuleDefinition AddRule(NewRuleRequest request)
    {
        var rule = new RuleDefinition
        {
            Id = Guid.NewGuid(),
            Name = request.Name,
            Pattern = request.Pattern,
            MinimumLevel = request.MinimumLevel,
            Enabled = request.Enabled
        };

        _rules.Add(rule);
        return rule;
    }

    public RuleDefinition? ToggleRule(Guid ruleId)
    {
        var index = _rules.FindIndex(rule => rule.Id == ruleId);
        if (index < 0)
        {
            return null;
        }

        var current = _rules[index];
        var updated = current with { Enabled = !current.Enabled };
        _rules[index] = updated;
        return updated;
    }

    public RuleTestResult TestRule(Guid ruleId)
    {
        var rule = _rules.FirstOrDefault(r => r.Id == ruleId);
        if (rule is null)
        {
            return new RuleTestResult
            {
                RuleId = ruleId,
                MatchCount = 0,
                Matches = Array.Empty<LogEvent>()
            };
        }

        var regex = new Regex(rule.Pattern, RegexOptions.IgnoreCase | RegexOptions.Compiled);
        var matches = _events
            .Where(evt => evt.Level >= rule.MinimumLevel && regex.IsMatch(evt.Message))
            .TakeLast(50)
            .ToList();

        return new RuleTestResult
        {
            RuleId = ruleId,
            MatchCount = matches.Count,
            Matches = matches
        };
    }

    public RuleExecutionResponse ExecuteRule(Guid ruleId, bool dryRun)
    {
        var rule = _rules.FirstOrDefault(r => r.Id == ruleId);
        var result = new RemediationActionResult
        {
            ActionId = Guid.NewGuid(),
            Status = dryRun ? ActionStatus.Skipped : ActionStatus.Success,
            Timestamp = DateTimeOffset.UtcNow,
            Details = rule is null ? "Rule not found" : (dryRun ? "Dry-run" : "Executed")
        };

        ActionEvent? actionEvent = null;
        if (!dryRun && rule is not null)
        {
            actionEvent = new ActionEvent
            {
                Id = Guid.NewGuid(),
                Description = $"Executed rule: {rule.Name}",
                Confidence = 92,
                Timestamp = result.Timestamp,
                Status = ActionStatus.Success
            };
            AddAction(actionEvent);
        }

        return new RuleExecutionResponse
        {
            Result = result,
            ActionEvent = actionEvent
        };
    }

    private void SeedRules()
    {
        _rules.Add(new RuleDefinition
        {
            Id = Guid.NewGuid(),
            Name = "High latency on postgres",
            Pattern = "slow query detected",
            MinimumLevel = Models.LogLevel.Warning,
            Enabled = true
        });

        _rules.Add(new RuleDefinition
        {
            Id = Guid.NewGuid(),
            Name = "Nginx timeouts",
            Pattern = "connection timeout",
            MinimumLevel = Models.LogLevel.Error,
            Enabled = true
        });

        _rules.Add(new RuleDefinition
        {
            Id = Guid.NewGuid(),
            Name = "Cache miss spike",
            Pattern = "cache miss",
            MinimumLevel = Models.LogLevel.Warning,
            Enabled = false
        });
    }

    private static void Trim<T>(ConcurrentQueue<T> queue, int max)
    {
        while (queue.Count > max && queue.TryDequeue(out _))
        {
        }
    }
}
